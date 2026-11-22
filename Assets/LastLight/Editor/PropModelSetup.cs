using System.IO;
using UnityEditor;
using UnityEngine;

namespace LastLight.EditorTools
{
    /// <summary>
    /// Import rules for the second batch of Tripo props (hazards, coin, gear, pickups, cliff rock and the Scout Jeep):
    /// no rig, no animation, no imported materials (the game uses LastLight/DioramaTextured). Textures are capped at 512,
    /// or 1024 for the Scout Jeep, which is seen up close.
    /// </summary>
    public class PropModelPostprocessor : AssetPostprocessor
    {
        internal const string Root = "Assets/LastLight/Art/Tripo/";

        internal static readonly (string folder, string name)[] Props =
        {
            ("storm_branch", "LL_StormBranch"),
            ("falling_boulder", "LL_FallingBoulder"),
            ("road_barricade", "LL_RoadBarricade"),
            ("coin", "LL_Coin"),
            ("fallen_log", "LL_FallenLog"),
            ("fallen_tree_wide", "LL_FallenTreeWide"),
            ("roof_turret", "LL_RoofTurret"),
            ("plough_shovel", "LL_PloughShovel"),
            ("shield_pickup", "LL_ShieldPickup"),
            ("boost_pickup", "LL_BoostPickup"),
            ("flashlight_pickup", "LL_FlashlightPickup"),
            ("repair_pickup", "LL_RepairPickup"),
            ("cliff_rock", "LL_CliffRock"),
            ("scout_jeep", "LL_ScoutJeep"),
        };

        static bool IsProp(string path)
        {
            foreach (var (folder, _) in Props) if (path.StartsWith(Root + folder + "/")) return true;
            return false;
        }

        void OnPreprocessModel()
        {
            if (!IsProp(assetPath) || !assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
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
            if (!IsProp(assetPath) || !assetPath.Contains("/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = assetPath.Contains("/scout_jeep/") ? 1024 : 512;
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
    /// Creates each prop's LastLight/DioramaTextured material next to its FBX, logs its size and triangle count, and
    /// bakes the props the game uses (see <see cref="Bakes"/>) into SceneryModels.asset at their gameplay size. Batch: -executeMethod LastLight.EditorTools.PropModelSetup.ConfigureBatch
    /// </summary>
    public static class PropModelSetup
    {
        [MenuItem("Last Light/Configure Prop Models")]
        public static int Configure()
        {
            var shader = Shader.Find("LastLight/DioramaTextured");
            if (shader == null) throw new System.Exception("Shader LastLight/DioramaTextured not found");
            int ready = 0;
            foreach (var (folder, name) in PropModelPostprocessor.Props)
            {
                string dir = PropModelPostprocessor.Root + folder;
                string fbx = $"{dir}/{name}.fbx";
                if (!File.Exists(fbx)) { Debug.LogWarning("[LastLight] Prop model missing: " + fbx); continue; }
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
                // The Scout Jeep is a vehicle, shown off the disk in the garage; the cliff rock is merged into chunks with a shade per copy.
                mat.SetFloat("_ClipToDisk", folder == "scout_jeep" ? 0f : 1f);
                mat.SetFloat("_Fade", 1f);
                mat.SetFloat("_UseVertexColor", folder == "cliff_rock" ? 1f : 0f);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);

                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                Bounds b = new Bounds();
                bool any = false;
                int tris = 0;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += (int)mf.sharedMesh.GetIndexCount(0) / 3;
                }
                Object.DestroyImmediate(inst);
                bool textured = mat.GetTexture("_BaseMap") != null;
                if (textured && tris > 0) ready++;
                Debug.Log($"[LastLight] Prop {name}: {tris} tris, size {b.size.ToString("0.000")} m, base y {b.min.y:0.000}, " +
                          $"material {(textured ? "textured" : "NO TEXTURE")}");
            }
            int baked = BakeAll();
            AssetDatabase.SaveAssets();
            Debug.Log($"[LastLight] Prop models ready: {ready}/{PropModelPostprocessor.Props.Length}, baked for the game: {baked}/{Bakes.Length}");
            return ready;
        }

        enum Fit
        {
            /// <summary>Long side turned onto X, base on y = 0, centred in X and Z, scaled to <c>size</c> along X.</summary>
            AlongX,
            /// <summary>As AlongX but without the automatic turn: for gear whose front must face +Z.</summary>
            WidthX,
            /// <summary>Base on y = 0, centred in X and Z, the larger of X and Z scaled to <c>size</c>.</summary>
            Footprint,
            /// <summary>Centred on the origin, the largest side scaled to <c>size</c>.</summary>
            Centred,
            /// <summary>Base centre at the origin, exactly 1 unit tall, for merging scaled copies into chunks.</summary>
            UnitTall,
        }

        /// <summary>
        /// How each prop is fitted for the game: a rotation (degrees, applied to the model as exported, before fitting) that
        /// stands it the right way up and turns its front to -Z (hazards, which face the oncoming car) or +Z (gear, which
        /// faces up the road), then the fit, its size in game units and a squash applied after fitting (the plough is
        /// flattened so that, scaled to the 2.6-wide gameplay blade, it doesn't hide the front of the car). Checked
        /// against Logs/LastLightPreview/prop_*.png.
        /// </summary>
        static readonly (string name, Vector3 euler, Fit fit, float size, Vector3 squash, System.Action<SceneryModels, SceneryModels.Model> store)[] Bakes =
        {
            ("LL_StormBranch", new Vector3(0f, 0f, 0f), Fit.AlongX, 2.6f, Vector3.one, (a, m) => a.branch = m),
            ("LL_FallingBoulder", new Vector3(0f, 0f, 0f), Fit.Centred, 2.0f, Vector3.one, (a, m) => a.fallingBoulder = m),
            ("LL_RoadBarricade", new Vector3(0f, 0f, 0f), Fit.AlongX, 2.64f, Vector3.one, (a, m) => a.barricade = m),
            ("LL_FallenLog", new Vector3(0f, 0f, 0f), Fit.AlongX, 2.6f, Vector3.one, (a, m) => a.fallenLog = m),
            ("LL_FallenTreeWide", new Vector3(0f, 0f, 0f), Fit.AlongX, 5.2f, Vector3.one, (a, m) => a.wideTree = m),
            ("LL_CliffRock", new Vector3(0f, 0f, 0f), Fit.UnitTall, 1f, Vector3.one, (a, m) => a.cliffRock = m),
            ("LL_Coin", new Vector3(0f, 0f, 0f), Fit.Centred, 1.7f, Vector3.one, (a, m) => a.coin = m),
            ("LL_RoofTurret", new Vector3(0f, 0f, 0f), Fit.Footprint, 0.95f, Vector3.one, (a, m) => a.turret = m),
            ("LL_PloughShovel", new Vector3(0f, 0f, 0f), Fit.WidthX, 2.6f, new Vector3(1f, 0.68f, 0.8f), (a, m) => a.shovel = m),
        };

        const string ModelsPath = "Assets/LastLight/Resources/" + SceneryModels.ResourcePath + ".asset";

        static string FolderOf(string name)
        {
            foreach (var (folder, n) in PropModelPostprocessor.Props) if (n == name) return folder;
            return null;
        }

        /// <summary>Bakes the fitted props into SceneryModels.asset as readable sub-meshes. Returns how many are ready.</summary>
        static int BakeAll()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SceneryModels>(ModelsPath);
            if (asset == null) { Debug.LogWarning("[LastLight] No SceneryModels asset: run Configure Scenery Models first"); return 0; }
            int ready = 0;
            foreach (var bake in Bakes)
            {
                string dir = PropModelPostprocessor.Root + FolderOf(bake.name);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{bake.name}.fbx");
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{bake.name}.mat");
                var mesh = prefab != null ? BakeFitted(prefab, bake.name + "_Game", asset, bake.euler, bake.fit, bake.size, bake.squash) : null;
                var model = new SceneryModels.Model { mesh = mesh, material = mat };
                bake.store(asset, model);
                if (model.Ready) ready++;
                else Debug.LogWarning($"[LastLight] Prop {bake.name} not baked (model {(prefab != null ? "found" : "missing")}, material {(mat != null ? "found" : "missing")})");
            }
            EditorUtility.SetDirty(asset);
            return ready;
        }

        static Mesh BakeFitted(GameObject prefab, string meshName, Object owner, Vector3 euler, Fit fit, float size, Vector3 squash)
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

                var rot = Quaternion.Euler(euler);
                for (int i = 0; i < verts.Count; i++) { verts[i] = rot * verts[i]; norms[i] = rot * norms[i]; }
                var b = Measure(verts);
                if (fit == Fit.AlongX && b.size.z > b.size.x)
                {
                    var turn = Quaternion.Euler(0f, 90f, 0f);
                    for (int i = 0; i < verts.Count; i++) { verts[i] = turn * verts[i]; norms[i] = turn * norms[i]; }
                    b = Measure(verts);
                }
                float scale = fit switch
                {
                    Fit.AlongX or Fit.WidthX => size / Mathf.Max(b.size.x, 1e-4f),
                    Fit.Footprint => size / Mathf.Max(b.size.x, b.size.z, 1e-4f),
                    Fit.Centred => size / Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-4f),
                    _ => 1f / Mathf.Max(b.size.y, 1e-4f),
                };
                var pivot = fit == Fit.Centred ? b.center : new Vector3(b.center.x, b.min.y, b.center.z);
                for (int i = 0; i < verts.Count; i++) verts[i] = Vector3.Scale((verts[i] - pivot) * scale, squash);
                if (squash != Vector3.one)
                {
                    var inv = new Vector3(1f / squash.x, 1f / squash.y, 1f / squash.z);
                    for (int i = 0; i < norms.Count; i++) norms[i] = Vector3.Scale(norms[i], inv).normalized;
                }

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
                Debug.Log($"[LastLight] Baked {meshName}: {verts.Count} verts, {tris.Count / 3} tris, size {mesh.bounds.size.ToString("0.00")}, fit {fit}");
                return mesh;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static Bounds Measure(System.Collections.Generic.List<Vector3> verts)
        {
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var p in verts) b.Encapsulate(p);
            return b;
        }

        /// <summary>
        /// Renders each baked prop flat-lit on grey, from the front (looking along +Z, as the car sees a hazard), the
        /// right side and above, into Logs/LastLightPreview/prop_{name}.png, with a red bar along +X and a blue bar
        /// along +Z on the ground so the orientation can be read.
        /// Batch: -executeMethod LastLight.EditorTools.PropModelSetup.RenderPropSheetBatch
        /// </summary>
        public static void RenderPropSheetBatch()
        {
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneryModels>(ModelsPath);
                if (asset == null) throw new System.Exception("No SceneryModels asset");
                Shader.SetGlobalVector("_LL_Ambient", new Vector4(1f, 1f, 1f, 0f));
                Shader.SetGlobalVector("_LL_Moon", new Vector4(0f, 1f, 0f, 0f));
                Shader.SetGlobalVector("_LL_Disk", new Vector4(0f, 0f, 0f, 1000f));
                Shader.SetGlobalVector("_LL_HeadPos", new Vector4(0f, -100f, 0f, 0.001f));
                Shader.SetGlobalVector("_LL_Weather", Vector4.zero);
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LastLightPreview"));
                Directory.CreateDirectory(dir);
                var cam = new GameObject("Prop Camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.35f, 0.35f, 0.38f);
                cam.orthographic = true;
                const int W = 512;
                var rt = new RenderTexture(W, W, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var axisMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                var models = new (string, SceneryModels.Model)[]
                {
                    ("LL_StormBranch", asset.branch), ("LL_FallingBoulder", asset.fallingBoulder), ("LL_RoadBarricade", asset.barricade),
                    ("LL_FallenLog", asset.fallenLog), ("LL_FallenTreeWide", asset.wideTree), ("LL_CliffRock", asset.cliffRock),
                    ("LL_Coin", asset.coin), ("LL_RoofTurret", asset.turret), ("LL_PloughShovel", asset.shovel),
                };
                foreach (var (name, model) in models)
                {
                    if (!model.Ready) continue;
                    var go = new GameObject(name);
                    go.AddComponent<MeshFilter>().sharedMesh = model.mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = model.material;
                    var b = model.mesh.bounds;
                    float r = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 1.25f;
                    var xBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    xBar.transform.localScale = new Vector3(r, 0.02f * r, 0.02f * r);
                    xBar.transform.position = new Vector3(r * 0.5f, b.min.y, 0f);
                    var zBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    zBar.transform.localScale = new Vector3(0.02f * r, 0.02f * r, r);
                    zBar.transform.position = new Vector3(0f, b.min.y, r * 0.5f);
                    var xm = new Material(axisMat) { color = Color.red };
                    var zm = new Material(axisMat) { color = Color.blue };
                    xBar.GetComponent<Renderer>().sharedMaterial = xm;
                    zBar.GetComponent<Renderer>().sharedMaterial = zm;
                    cam.orthographicSize = r;
                    var sheet = new Texture2D(W * 3, W, TextureFormat.RGB24, false);
                    Vector3[] eyes = { new Vector3(0f, 0f, -10f * r), new Vector3(10f * r, 0f, 0f), new Vector3(0f, 10f * r, 0f) };
                    for (int k = 0; k < 3; k++)
                    {
                        cam.transform.position = b.center + eyes[k];
                        cam.transform.LookAt(b.center, k == 2 ? Vector3.forward : Vector3.up);
                        cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f * r;
                        cam.Render();
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, W, W), k * W, 0);
                    }
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, "prop_" + name + ".png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Object.DestroyImmediate(go); Object.DestroyImmediate(xBar); Object.DestroyImmediate(zBar);
                }
                RenderTexture.active = null;
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void ConfigureBatch()
        {
            try { EditorApplication.Exit(Configure() == PropModelPostprocessor.Props.Length ? 0 : 1); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
