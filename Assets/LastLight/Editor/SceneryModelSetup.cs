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
                if (garage) RepairPlatform(verts, norms, uvs, tris);

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
        /// Patches the Tripo garage's central platform, where the retopology left wedge-shaped gaps in the turntable:
        /// flat platform faces wound downward are flipped back (they were being culled from above), upward faces with
        /// downward normals get upward ones, and open edge loops
        /// that lie flat on the platform are capped with an upward-facing fan. Runs on the fitted mesh (turntable top at
        /// y = 0, 21 units across), so the platform test is in game units.
        /// </summary>
        static void RepairPlatform(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<Vector3> norms,
            System.Collections.Generic.List<Vector2> uvs, System.Collections.Generic.List<int> tris)
        {
            const float PlatformRadius = GarageWidth * 0.36f;   // the floor and turntable, inside the walls
            const float FlatTolerance = 0.9f;                   // a hole whose rim spans more height than this is not a floor hole

            // 1. Platform faces wound backwards: flat faces on the floor or turntable whose winding points down, so they
            //    are culled when seen from above. Their vertex normals already point up, so only the winding changes.
            int flipped = 0, darkNormals = 0;
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                bool onPlatform = true;
                foreach (var p in new[] { a, b, c })
                    if (new Vector2(p.x, p.z).magnitude > PlatformRadius || p.y < -1.5f || p.y > 0.6f) onPlatform = false;
                if (!onPlatform) continue;
                Vector3 fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f) continue;
                fn.Normalize();
                if (fn.y < -0.8f)
                {
                    (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
                    flipped++;
                }
                else if (fn.y > 0.8f)
                {
                    // Upward faces shaded as if facing down read as black wedges too.
                    for (int k = 0; k < 3; k++)
                        if (norms[tris[t + k]].y < 0f) { norms[tris[t + k]] = Vector3.up; darkNormals++; }
                }
            }

            // 2. Weld by position (Tripo splits vertices at UV seams), then find edges used by only one triangle.
            var weld = new int[verts.Count];
            var keyToId = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            var rep = new System.Collections.Generic.List<int>();   // a representative vertex per welded id
            for (int i = 0; i < verts.Count; i++)
            {
                var k = Vector3Int.RoundToInt(verts[i] * 2000f);
                if (!keyToId.TryGetValue(k, out int id)) { id = rep.Count; keyToId[k] = id; rep.Add(i); }
                weld[i] = id;
            }
            var edgeUse = new System.Collections.Generic.Dictionary<long, int>();
            long Key(int x, int y) => x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
            for (int t = 0; t < tris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int x = weld[tris[t + e]], y = weld[tris[t + (e + 1) % 3]];
                    if (x == y) continue;
                    long k = Key(x, y);
                    edgeUse[k] = edgeUse.TryGetValue(k, out int n) ? n + 1 : 1;
                }
            // Directed boundary edges, as the triangles wind them; a hole's rim follows them head to tail. Several can leave
            // one vertex: the turntable's wedges all meet at its centre, each leaving a thin sliver open there.
            var outgoing = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
            for (int t = 0; t < tris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int x = weld[tris[t + e]], y = weld[tris[t + (e + 1) % 3]];
                    if (x == y || edgeUse[Key(x, y)] != 1) continue;
                    if (!outgoing.TryGetValue(x, out var list)) outgoing[x] = list = new System.Collections.Generic.List<int>();
                    list.Add(y);
                }
            Vector2 Flat(int id) => new Vector2(verts[rep[id]].x, verts[rep[id]].z);

            // 3. Walk each loop; where several rim edges leave a vertex, take the one closest in direction to the edge we
            //    came in on (a sliver's two long sides run almost parallel). Cap the loops lying flat on the platform.
            int capped = 0, skipped = 0;
            var used = new System.Collections.Generic.HashSet<long>();
            long Dir(int x, int y) => ((long)x << 32) | (uint)y;
            foreach (var kv in outgoing)
                foreach (int first in kv.Value)
                {
                    int s0 = kv.Key;
                    if (used.Contains(Dir(s0, first))) continue;
                    var loop = new System.Collections.Generic.List<int> { s0 };
                    used.Add(Dir(s0, first));
                    int prev = s0, cur = first;
                    bool closed = false;
                    while (loop.Count < 4000)
                    {
                        if (cur == s0) { closed = true; break; }
                        loop.Add(cur);
                        if (!outgoing.TryGetValue(cur, out var outs)) break;
                        Vector2 back = (Flat(prev) - Flat(cur)).normalized;
                        int pick = -1;
                        float bestAngle = float.MaxValue;
                        foreach (int cand in outs)
                        {
                            if (used.Contains(Dir(cur, cand))) continue;
                            float ang = Vector2.Angle(back, (Flat(cand) - Flat(cur)).normalized);
                            if (ang < bestAngle) { bestAngle = ang; pick = cand; }
                        }
                        if (pick < 0) break;
                        used.Add(Dir(cur, pick));
                        prev = cur;
                        cur = pick;
                    }
                    if (!closed || loop.Count < 3) { skipped++; continue; }
                    float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f;
                    Vector3 centre = Vector3.zero;
                    foreach (int id in loop)
                    {
                        Vector3 p = verts[rep[id]];
                        centre += p;
                        minY = Mathf.Min(minY, p.y);
                        maxY = Mathf.Max(maxY, p.y);
                        maxR = Mathf.Max(maxR, new Vector2(p.x, p.z).magnitude);
                    }
                    if (maxR > PlatformRadius || maxY - minY > FlatTolerance) { skipped++; continue; }
                    centre /= loop.Count;
                    // The cap takes the texture of the rim vertex nearest its centre, so a sliver reads as the wedge beside it.
                    int nearest = rep[loop[0]];
                    foreach (int id in loop) if ((verts[rep[id]] - centre).sqrMagnitude < (verts[nearest] - centre).sqrMagnitude) nearest = rep[id];
                    int c = verts.Count;
                    verts.Add(centre);
                    norms.Add(Vector3.up);
                    uvs.Add(uvs[nearest]);
                    for (int i = 0; i < loop.Count; i++)
                    {
                        int a = rep[loop[i]], b = rep[loop[(i + 1) % loop.Count]];
                        // Wind each cap triangle so it faces up, the way the platform is seen.
                        if (Vector3.Cross(verts[b] - verts[a], centre - verts[a]).y >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
                        else { tris.Add(b); tris.Add(a); tris.Add(c); }
                    }
                    capped++;
                }

            // 4. The turntable is built as separate wedge slabs with their own side walls and nothing underneath, so the thin
            //    gaps between wedges see straight through. A backing disc just under the turntable top, in the turntable's own
            //    colour, closes them. Its centre is the hub where the wedges meet (the most connected vertex near the middle),
            //    its radius the ring of side-wall vertices at the turntable's edge, measured from that hub.
            var valence = new int[rep.Count];
            for (int t = 0; t < tris.Count; t++) if (tris[t] < weld.Length) valence[weld[tris[t]]]++;   // caps added above aren't welded
            int hub = -1;
            for (int id = 0; id < rep.Count; id++)
            {
                Vector3 p = verts[rep[id]];
                if (Mathf.Abs(p.y) > 0.15f || new Vector2(p.x, p.z).magnitude > GarageWidth * 0.15f) continue;
                if (hub < 0 || valence[id] > valence[hub]) hub = id;
            }
            float rim = 0f;
            Vector3 hubPos = hub >= 0 ? verts[rep[hub]] : Vector3.zero;
            if (hub >= 0)
            {
                // Side walls: steep faces at turntable height. Their vertices pile up at the turntable's edge.
                var bins = new int[40];
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c2 = verts[tris[t + 2]];
                    Vector3 fn = Vector3.Cross(b - a, c2 - a);
                    if (fn.sqrMagnitude < 1e-12f || Mathf.Abs(fn.normalized.y) > 0.4f) continue;
                    foreach (var p in new[] { a, b, c2 })
                    {
                        if (p.y > 0.1f || p.y < -0.6f) continue;
                        float r = new Vector2(p.x - hubPos.x, p.z - hubPos.z).magnitude;
                        int bin = (int)(r / 0.15f);
                        if (r > 2.4f && bin < bins.Length) bins[bin]++;
                    }
                }
                int peak = 0;
                for (int k = 0; k < bins.Length; k++) if (bins[k] > bins[peak]) peak = k;
                if (bins[peak] > 0) rim = (peak + 0.5f) * 0.15f;
            }
            int sample = -1;
            float best = float.MaxValue;
            for (int i = 0; i < verts.Count; i++)
            {
                // A texel from the turntable's flat top, half way out from the hub.
                float d = Mathf.Abs(new Vector2(verts[i].x - hubPos.x, verts[i].z - hubPos.z).magnitude - rim * 0.5f);
                if (Mathf.Abs(verts[i].y - hubPos.y) < 0.05f && norms[i].y > 0.9f && d < best) { best = d; sample = i; }
            }
            if (rim > 1f && sample >= 0)
            {
                const int Segments = 48;
                const float BackingDepth = 0.1f;
                float discR = rim * 1.06f;   // the wedge walls end just inside the copper ring, which hides the disc's edge
                Vector2 uv = uvs[sample];
                int centre = verts.Count;
                verts.Add(new Vector3(hubPos.x, hubPos.y - BackingDepth, hubPos.z)); norms.Add(Vector3.up); uvs.Add(uv);
                for (int k = 0; k < Segments; k++)
                {
                    float ang = k * Mathf.PI * 2f / Segments;
                    verts.Add(new Vector3(hubPos.x + Mathf.Cos(ang) * discR, hubPos.y - BackingDepth, hubPos.z + Mathf.Sin(ang) * discR));
                    norms.Add(Vector3.up);
                    uvs.Add(uv);
                }
                for (int k = 0; k < Segments; k++)
                {
                    int a = centre + 1 + k, b = centre + 1 + (k + 1) % Segments;
                    tris.Add(centre); tris.Add(b); tris.Add(a);   // clockwise from above, so it faces up
                }
            }
            Debug.Log($"[LastLight] Garage platform repair: {flipped} reversed faces flipped, {darkNormals} downward normals turned up, {capped} holes capped, {skipped} other open edge chains left (walls, outer rim); turntable hub {hubPos.ToString("0.00")} (valence {(hub >= 0 ? valence[hub] : 0)}), edge radius {rim:0.00}, backing disc {(rim > 1f && sample >= 0 ? "added" : "NOT added")}");
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

        /// <summary>
        /// Renders the baked garage flat-lit against magenta from above and from the front, so any hole in the platform
        /// shows as magenta. Writes Logs/LastLightPreview/garage_check_top.png and garage_check_front.png.
        /// Batch: -executeMethod LastLight.EditorTools.SceneryModelSetup.RenderGarageCheckBatch
        /// </summary>
        public static void RenderGarageCheckBatch()
        {
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneryModels>(AssetPath);
                if (asset == null || asset.garageMesh == null) throw new System.Exception("No baked garage");
                Shader.SetGlobalVector("_LL_Ambient", new Vector4(1f, 1f, 1f, 0f));
                Shader.SetGlobalVector("_LL_Moon", new Vector4(0f, 1f, 0f, 0f));
                Shader.SetGlobalVector("_LL_Disk", new Vector4(0f, 0f, 0f, 1000f));
                Shader.SetGlobalVector("_LL_HeadPos", new Vector4(0f, -100f, 0f, 0.001f));
                var go = new GameObject("Garage Check");
                go.AddComponent<MeshFilter>().sharedMesh = asset.garageMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = asset.garageMaterial;
                var cam = new GameObject("Check Camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.magenta;
                cam.fieldOfView = 40f;
                var rt = new RenderTexture(1600, 1200, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LastLightPreview"));
                Directory.CreateDirectory(dir);
                void Shot(Vector3 eye, Vector3 at, string file)
                {
                    cam.transform.position = eye;
                    cam.transform.LookAt(at);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                Shot(new Vector3(0f, 16f, 0.01f), Vector3.zero, "garage_check_top.png");
                Shot(new Vector3(0f, 9f, -14f), new Vector3(0f, -0.5f, 0f), "garage_check_front.png");
                RenderTexture.active = null;
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void ConfigureBatch()
        {
            try { var a = Configure(); EditorApplication.Exit(a.HasPine && a.HasRock && a.HasGrass ? 0 : 1); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
