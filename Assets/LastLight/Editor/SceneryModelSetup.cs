using System.IO;
using System.Linq;
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
            "Assets/LastLight/Art/Tripo/jeep/",
        };

        /// <summary>Models placed whole (garage, vehicles) rather than merged into road chunks.</summary>
        internal static bool IsWhole(string path) =>
            path.Contains("/Tripo/garage/") || path.Contains("/Tripo/hauler_pickup/") || path.Contains("/Tripo/rhino_apc/") || path.Contains("/Tripo/angel_transponder/")
            || path.Contains("/Tripo/jeep/");

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
            if (assetPath.ToLowerInvariant().Contains("normal.")) ti.textureType = TextureImporterType.NormalMap;
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = assetPath.ToLowerInvariant().Contains("basecolor");
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
            ("jeep", "LL_Jeep"),
        };

        /// <summary>
        /// Which VehicleCatalog slot each vehicle model fills, a roll (degrees about X, applied first) for a model that was
        /// exported lying on its side, and the extra turn (degrees about Y) that points its nose up the road. Checked
        /// against the preview renders vehicle_N_side.png.
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<string, (int slot, float roll, float yaw)> Vehicles = new System.Collections.Generic.Dictionary<string, (int, float, float)>
        {
            { "LL_Jeep", (0, -90f, 180f) },         // exported on its side (roof along +Z) and facing backwards
            { "LL_HaulerPickup", (1, 0f, 0f) },
            { "LL_RhinoAPC", (2, 0f, 180f) },        // exported facing backwards
            { "LL_AngelTransponder", (3, 0f, 0f) },
        };
        /// <summary>Extra turn for the garage so its open side faces the player (it was exported with the back wall forward).</summary>
        const float GarageYaw = 180f;
        const float VehicleLength = 2.6f, VehicleMaxWidth = 1.55f;
        const float GarageWidth = 21f;

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
                mat.SetTexture("_BaseMap", FindBaseColor(dir, name));
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
                else if (Vehicles.TryGetValue(name, out var v))
                {
                    if (asset.vehicleMeshes == null || asset.vehicleMeshes.Length < 4) asset.vehicleMeshes = new Mesh[4];
                    if (asset.vehicleMaterials == null || asset.vehicleMaterials.Length < 4) asset.vehicleMaterials = new Material[4];
                    asset.vehicleMeshes[v.slot] = BakeFitted(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), name + "_Vehicle", asset, v.yaw, false, v.roll);
                    asset.vehicleMaterials[v.slot] = mat;
                }
                else if (name == "LL_Garage")
                {
                    asset.garageMesh = BakeFitted(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), name + "_Diorama", asset, GarageYaw, true, 0f);
                    asset.garageMaterial = mat;
                }

                Debug.Log($"[LastLight] Scenery {name}: {(mesh != null ? mesh.GetIndexCount(0) / 3 : 0)} tris, " +
                          $"size {b.size.ToString("0.000")} m, base y {b.min.y:0.000}, centre {b.center.ToString("0.000")}, " +
                          $"material {(mat.GetTexture("_BaseMap") != null ? "textured" : "NO TEXTURE")}");
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LastLight] Scenery models: pine {(asset.HasPine ? "ready" : "MISSING")}, rock {(asset.HasRock ? "ready" : "MISSING")}, grass {(asset.HasGrass ? "ready" : "MISSING")}, " +
                      $"vehicles {string.Join("/", System.Linq.Enumerable.Range(0, 4).Select(i => asset.HasVehicle(i) ? "ready" : "MISSING"))}, garage {(asset.HasGarage ? "ready" : "MISSING")}");
            return asset;
        }

        /// <summary>The base colour map in a model's Textures folder: "{name}_BaseColor.png", or any file named *basecolor*.</summary>
        static Texture2D FindBaseColor(string dir, string name)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Textures/{name}_BaseColor.png");
            if (tex != null || !Directory.Exists(dir + "/Textures")) return tex;
            foreach (var f in Directory.GetFiles(dir + "/Textures"))
            {
                string lower = f.ToLowerInvariant();
                if (lower.EndsWith(".meta") || !lower.Contains("basecolor")) continue;
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
                if (tex != null) return tex;
            }
            return null;
        }

        /// <summary>
        /// Copies a whole model (vehicle or garage) with its import transform applied and fits it for the game.
        /// A vehicle is turned so its long side runs along Z (plus <paramref name="yaw"/> to point the nose up the road),
        /// centred, set with its lowest point on y = 0, and scaled to the jeep's 2.6 x 1.55 footprint.
        /// The garage is turned by <paramref name="yaw"/>, scaled to 21 units across and centred, with the top of its
        /// built-in turntable (the highest point near the middle) on y = 0, so a vehicle placed at the origin stands on it.
        /// </summary>
        static Mesh BakeFitted(GameObject prefab, string meshName, Object owner, float yaw, bool garage, float roll)
        {
            if (prefab == null) return null;
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

                // Stand a model up that was exported on its side.
                if (roll != 0f)
                {
                    var r0 = Quaternion.Euler(roll, 0f, 0f);
                    for (int i = 0; i < verts.Count; i++) { verts[i] = r0 * verts[i]; norms[i] = r0 * norms[i]; }
                }
                var b = new Bounds(verts[0], Vector3.zero);
                foreach (var p in verts) b.Encapsulate(p);
                // Long side along Z for vehicles, then the configured turn.
                float turn = yaw + (!garage && b.size.x > b.size.z ? 90f : 0f);
                var rot = Quaternion.Euler(0f, turn, 0f);
                for (int i = 0; i < verts.Count; i++) { verts[i] = rot * (verts[i] - b.center); norms[i] = rot * norms[i]; }
                b = new Bounds(verts[0], Vector3.zero);
                foreach (var p in verts) b.Encapsulate(p);
                float scale = garage ? GarageWidth / Mathf.Max(b.size.x, b.size.z, 1e-4f)
                    : Mathf.Min(VehicleLength / Mathf.Max(b.size.z, 1e-4f), VehicleMaxWidth / Mathf.Max(b.size.x, 1e-4f));
                for (int i = 0; i < verts.Count; i++) verts[i] = new Vector3(verts[i].x - b.center.x, verts[i].y, verts[i].z - b.center.z) * scale;
                float floor = float.MinValue;
                if (garage)
                {
                    // The turntable's top: the highest vertex within a small circle round the middle.
                    float r = GarageWidth * 0.06f;
                    foreach (var p in verts) if (p.x * p.x + p.z * p.z < r * r && p.y > floor) floor = p.y;
                }
                else
                {
                    floor = float.MaxValue;
                    foreach (var p in verts) floor = Mathf.Min(floor, p.y);
                }
                for (int i = 0; i < verts.Count; i++) verts[i] = new Vector3(verts[i].x, verts[i].y - floor, verts[i].z);

                Mesh mesh = null;
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(owner)))
                    if (sub is Mesh existing && existing.name == meshName) mesh = existing;
                if (mesh == null)
                {
                    mesh = new Mesh { name = meshName };
                    AssetDatabase.AddObjectToAsset(mesh, owner);
                }
                mesh.Clear();
                mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                Debug.Log($"[LastLight] Baked {meshName}: {verts.Count} verts, {tris.Count / 3} tris, size {mesh.bounds.size.ToString("0.00")}, turned {turn:0} deg{(garage ? $", turntable top lifted {-floor:0.00}" : "")}");
                return mesh;
            }
            finally { Object.DestroyImmediate(inst); }
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
