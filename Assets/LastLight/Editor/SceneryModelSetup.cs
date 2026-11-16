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
        };

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
            ti.maxTextureSize = 512;
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
    /// Creates each prop's LastLight/DioramaTextured material next to its FBX, with the base colour map and
    /// GPU instancing on (pines and grass appear many times per road chunk).
    /// Batch: -executeMethod LastLight.EditorTools.SceneryModelSetup.ConfigureBatch
    /// </summary>
    public static class SceneryModelSetup
    {
        static readonly (string folder, string name)[] Props =
        {
            ("pine", "LL_Pine"),
            ("boulder", "LL_Boulder"),
            ("grass_tuft", "LL_GrassTuft"),
        };

        [MenuItem("Last Light/Configure Scenery Models")]
        public static void Configure()
        {
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
                mat.SetFloat("_ClipToDisk", 1f);
                mat.SetFloat("_Fade", 1f);
                mat.SetFloat("_UseVertexColor", 0f);
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
                Debug.Log($"[LastLight] Scenery {name}: {(mesh != null ? mesh.GetIndexCount(0) / 3 : 0)} tris, " +
                          $"size {b.size.ToString("0.000")} m, base y {b.min.y:0.000}, centre {b.center.ToString("0.000")}, " +
                          $"material {(mat.GetTexture("_BaseMap") != null ? "textured" : "NO TEXTURE")}");
            }
            AssetDatabase.SaveAssets();
        }

        public static void ConfigureBatch()
        {
            try { Configure(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
