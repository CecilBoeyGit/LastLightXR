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
    /// Creates each prop's LastLight/DioramaTextured material next to its FBX and logs its size and triangle count.
    /// The props aren't placed in the game yet. Batch: -executeMethod LastLight.EditorTools.PropModelSetup.ConfigureBatch
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
                mat.SetFloat("_ClipToDisk", 1f);
                mat.SetFloat("_Fade", 1f);
                mat.SetFloat("_UseVertexColor", 0f);
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
            AssetDatabase.SaveAssets();
            Debug.Log($"[LastLight] Prop models ready: {ready}/{PropModelPostprocessor.Props.Length}");
            return ready;
        }

        public static void ConfigureBatch()
        {
            try { EditorApplication.Exit(Configure() == PropModelPostprocessor.Props.Length ? 0 : 1); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
