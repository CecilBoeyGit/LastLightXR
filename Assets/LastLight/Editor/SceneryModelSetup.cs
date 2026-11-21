using System.IO;
using UnityEditor;
using UnityEngine;

namespace LastLight.EditorTools
{
    /// <summary>
    /// Import rules for the static Tripo scenery props (pine, boulder, grass tuft): no rig, no animation and no
    /// imported materials (the game uses LastLight/DioramaTextured). Textures are capped at 512 for the headset.
    /// </summary>
    public class SceneryModelPostprocessor : AssetPostprocessor
    {
        internal static readonly string[] Folders =
        {
            "Assets/LastLight/Art/Tripo/pine/",
            "Assets/LastLight/Art/Tripo/boulder/",
            "Assets/LastLight/Art/Tripo/grass_tuft/",
            "Assets/LastLight/Art/Tripo/garage/",
            "Assets/LastLight/Art/Tripo/hauler_pickup/",
            "Assets/LastLight/Art/Tripo/rhino_apc/",
            "Assets/LastLight/Art/Tripo/angel_transponder/",
        };

        /// <summary>Models placed whole (garage, vehicles) rather than merged into road chunks.</summary>
        internal static bool IsWhole(string path) =>
            path.Contains("/Tripo/garage/") || path.Contains("/Tripo/hauler_pickup/") || path.Contains("/Tripo/rhino_apc/") || path.Contains("/Tripo/angel_transponder/");

        static bool InScenery(string path)
        {
            foreach (var f in Folders) if (path.StartsWith(f)) return true;
            return false;
        }

        void OnPreprocessModel()
        {
            if (!InScenery(assetPath) || !assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.addCollider = false;
        }

        void OnPreprocessTexture()
        {
            if (!InScenery(assetPath) || !assetPath.Contains("/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            // The garage and the vehicles are single close-up models, so they keep more texture detail than the props.
            ti.maxTextureSize = assetPath.Contains("/Tripo/garage/") ? 2048 : IsWhole(assetPath) ? 1024 : 512;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Compressed;
            if (assetPath.EndsWith("_Normal.png")) ti.textureType = TextureImporterType.NormalMap;
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = assetPath.EndsWith("_BaseColor.png");
            }
        }
    }

    /// <summary>
    /// Creates each prop's LastLight/DioramaTextured material next to its FBX (base colour map, vertex colour on for
    /// per-copy shading), then bakes each model into a normalised mesh (base centre at the origin, 1 unit tall) inside
    /// Resources/LastLightModels/SceneryModels.asset, which TrackGenerator merges into its road chunks.
    /// Batch: -executeMethod LastLight.EditorTools.SceneryModelSetup.ConfigureBatch
    /// </summary>
    public static class SceneryModelSetup
    {
        static readonly (string folder, string name)[] Props =
        {
            ("pine", "LL_Pine"),
            ("boulder", "LL_Boulder"),
            ("grass_tuft", "LL_GrassTuft"),
            ("garage", "LL_Garage"),       // material only from here down: placed whole, not merged into chunks
            ("hauler_pickup", "LL_HaulerPickup"),
            ("rhino_apc", "LL_RhinoAPC"),
            ("angel_transponder", "LL_AngelTransponder"),
        };

        const string AssetPath = "Assets/LastLight/Resources/" + SceneryModels.ResourcePath + ".asset";

        [MenuItem("Last Light/Configure Scenery Models")]
        public static SceneryModels Configure()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var asset = AssetDatabase.LoadAssetAtPath<SceneryModels>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SceneryModels>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            var shader = Shader.Find("LastLight/DioramaTextured");
            if (shader == null) throw new System.Exception("Shader LastLight/DioramaTextured not found");
            foreach (var (folder, name) in Props)
            {
                string dir = "Assets/LastLight/Art/Tripo/" + folder;
                string fbx = $"{dir}/{name}.fbx";
                if (!File.Exists(fbx)) { Debug.LogWarning("[LastLight] Scenery model missing: " + fbx); continue; }
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
                foreach (var tex in Directory.GetFiles(dir + "/Textures", "*.png"))
                    AssetDatabase.ImportAsset(tex.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);

                string matPath = $"{dir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = shader;
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Textures/{name}_BaseColor.png"));
                mat.SetColor("_Tint", Color.white);
                mat.SetFloat("_Emission", 0f);
                mat.SetFloat("_ClipToDisk", SceneryModelPostprocessor.IsWhole(dir + "/") ? 0f : 1f);   // the garage and vehicles also show off the road disk
                mat.SetFloat("_Fade", 1f);
                mat.SetFloat("_UseVertexColor", SceneryModelPostprocessor.IsWhole(dir + "/") ? 0f : 1f);     // merged prop copies carry a shade in their vertex colour
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(fbx);
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                Bounds b = new Bounds();
                bool any = false;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                Object.DestroyImmediate(inst);
                var baked = SceneryModelPostprocessor.IsWhole(dir + "/") ? null : Bake(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), name + "_Prop", asset);
                if (name == "LL_Pine") { asset.pineMesh = baked; asset.pineMaterial = mat; }
                else if (name == "LL_Boulder") { asset.rockMesh = baked; asset.rockMaterial = mat; }
                else if (name == "LL_GrassTuft") { asset.grassMesh = baked; asset.grassMaterial = mat; }

                Debug.Log($"[LastLight] Scenery {name}: {(mesh != null ? mesh.GetIndexCount(0) / 3 : 0)} tris, " +
                          $"size {b.size.ToString("0.000")} m, base y {b.min.y:0.000}, centre {b.center.ToString("0.000")}, " +
                          $"material {(mat.GetTexture("_BaseMap") != null ? "textured" : "NO TEXTURE")}");
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LastLight] Scenery models: pine {(asset.HasPine ? "ready" : "MISSING")}, rock {(asset.HasRock ? "ready" : "MISSING")}, grass {(asset.HasGrass ? "ready" : "MISSING")}");
            return asset;
        }

        /// <summary>
        /// Copies the model's mesh with its import transform applied, moved so the base centre sits at the origin and
        /// scaled to exactly 1 unit tall. Stored as a readable sub-asset of <paramref name="owner"/>.
        /// </summary>
        static Mesh Bake(GameObject prefab, string meshName, Object owner)
        {
            var inst = Object.Instantiate(prefab);
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var verts = new System.Collections.Generic.List<Vector3>();
                var norms = new System.Collections.Generic.List<Vector3>();
                var uvs = new System.Collections.Generic.List<Vector2>();
                var tris = new System.Collections.Generic.List<int>();
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    var src = mf.sharedMesh;
                    if (src == null) continue;
                    var m = mf.transform.localToWorldMatrix;
                    int start = verts.Count;
                    var v = src.vertices; var n = src.normals; var uv = src.uv;
                    for (int i = 0; i < v.Length; i++)
                    {
                        verts.Add(m.MultiplyPoint3x4(v[i]));
                        norms.Add(m.MultiplyVector(i < n.Length ? n[i] : Vector3.up).normalized);
                        uvs.Add(i < uv.Length ? uv[i] : Vector2.zero);
                    }
                    foreach (int t in src.triangles) tris.Add(start + t);
                }
                if (verts.Count == 0) return null;
                var b = new Bounds(verts[0], Vector3.zero);
                foreach (var p in verts) b.Encapsulate(p);
                float h = Mathf.Max(b.size.y, 1e-4f);
                var offset = new Vector3(b.center.x, b.min.y, b.center.z);
                for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - offset) / h;

                Mesh mesh = null;
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(owner)))
                    if (sub is Mesh existing && existing.name == meshName) mesh = existing;
                if (mesh == null)
                {
                    mesh = new Mesh { name = meshName };
                    AssetDatabase.AddObjectToAsset(mesh, owner);
                }
                mesh.Clear();
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        public static void ConfigureBatch()
        {
            try { var a = Configure(); EditorApplication.Exit(a.HasPine && a.HasRock && a.HasGrass ? 0 : 1); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
