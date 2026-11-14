using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LastLight.EditorTools
{
    /// <summary>
    /// Import rules for the Tripo zombie FBX files: generic rig (cheaper than Humanoid for an 18-strong horde,
    /// and the clips were made on the same skeleton), no imported materials (the game uses
    /// LastLight/DioramaTextured), and clips named run / idle / spit with looping set.
    /// </summary>
    public class ZombieModelPostprocessor : AssetPostprocessor
    {
        const string Folder = "Assets/LastLight/Art/Tripo/zombie_";

        bool IsZombie => assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsZombie) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = true;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;     // the spitter's acid glow follows the head bone
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        void OnPreprocessAnimation()
        {
            if (!IsZombie) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string take = c.takeName.ToLowerInvariant();
                if (take.StartsWith("run")) { c.name = "run"; c.loopTime = true; }
                else if (take.StartsWith("idle")) { c.name = "idle"; c.loopTime = true; }
                else { c.name = "spit"; c.loopTime = false; }   // the AI clip's take is named after its prompt
            }
            mi.clipAnimations = clips;
        }
    }

    /// <summary>
    /// Gathers the zombie models, clips and materials into Resources/LastLightModels/ZombieModels.asset, measures
    /// each model's height, and finds the moment in the spit clip where the head thrusts furthest forward.
    /// Batch: -executeMethod LastLight.EditorTools.ZombieModelSetup.ConfigureBatch
    /// </summary>
    public static class ZombieModelSetup
    {
        const string ArtDir = "Assets/LastLight/Art/Tripo";
        const string RunnerFbx = ArtDir + "/zombie_runner/LL_Zombie_Runner.fbx";
        const string RunnerMat = ArtDir + "/zombie_runner/LL_Zombie_Runner.mat";
        const string SpitterFbx = ArtDir + "/zombie_spitter/LL_Zombie_Spitter.fbx";
        const string SpitterMat = ArtDir + "/zombie_spitter/LL_Zombie_Spitter.mat";
        const string AssetPath = "Assets/LastLight/Resources/" + ZombieModels.ResourcePath + ".asset";

        [MenuItem("Last Light/Configure Zombie Models")]
        public static ZombieModels Configure()
        {
            // Reimport so the postprocessor's rules apply even if the files were imported before it existed.
            foreach (var p in new[] { RunnerFbx, SpitterFbx })
                if (File.Exists(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var asset = AssetDatabase.LoadAssetAtPath<ZombieModels>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ZombieModels>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.runnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerFbx);
            asset.runnerMaterial = AssetDatabase.LoadAssetAtPath<Material>(RunnerMat);
            asset.runnerRun = Clip(RunnerFbx, "run");
            asset.spitterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpitterFbx);
            asset.spitterMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpitterMat);
            asset.spitterIdle = Clip(SpitterFbx, "idle");
            asset.spitterSpit = Clip(SpitterFbx, "spit");

            if (asset.runnerPrefab != null) asset.runnerHeight = Height(asset.runnerPrefab, null);   // default pose; Tripo bakes the first run frame in, so this is running height
            if (asset.spitterPrefab != null) asset.spitterHeight = Height(asset.spitterPrefab, null);
            if (asset.spitterPrefab != null && asset.spitterSpit != null) asset.spitApexTime = SpitApex(asset.spitterPrefab, asset.spitterSpit);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LastLight] Zombie models: runner {(asset.HasRunner ? "ready" : "MISSING")} (height {asset.runnerHeight:0.000}, run {Len(asset.runnerRun)}), " +
                      $"spitter {(asset.HasSpitter ? "ready" : "MISSING")} (height {asset.spitterHeight:0.000}, idle {Len(asset.spitterIdle)}, spit {Len(asset.spitterSpit)}, apex {asset.spitApexTime:0.00} s)");
            return asset;
        }

        /// <summary>Batch: configure the models, then run the headless preview.</summary>
        public static void ConfigureAndPreviewBatch()
        {
            try
            {
                var a = Configure();
                if (!a.HasRunner || !a.HasSpitter) { EditorApplication.Exit(1); return; }
                PrototypeBuilder.Preview();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void ConfigureBatch()
        {
            try
            {
                var a = Configure();
                EditorApplication.Exit(a.HasRunner && a.HasSpitter ? 0 : 1);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static string Len(AnimationClip c) => c == null ? "missing" : $"{c.length:0.00} s";

        static AnimationClip Clip(string fbx, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == name && !c.name.StartsWith("__preview__"));

        /// <summary>Height of the posed model in its root's local units (root at the origin, unscaled).</summary>
        static float Height(GameObject prefab, AnimationClip pose)
        {
            var go = Object.Instantiate(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (pose != null) pose.SampleAnimation(go, 0f);
                float min = float.MaxValue, max = float.MinValue;
                var baked = new Mesh();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.BakeMesh(baked, true);
                    var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (var v in baked.vertices)
                    {
                        float y = m.MultiplyPoint3x4(v).y;
                        min = Mathf.Min(min, y);
                        max = Mathf.Max(max, y);
                    }
                }
                Object.DestroyImmediate(baked);
                float h = max - min;
                return h / Mathf.Max(go.transform.localScale.y, 1e-4f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Seconds into the spit clip where the head is furthest forward of where it started.</summary>
        static float SpitApex(GameObject prefab, AnimationClip spit)
        {
            var go = Object.Instantiate(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var head = go.GetComponentsInChildren<Transform>().FirstOrDefault(b => b.name == "Head" || b.name.EndsWith(":Head"));
                if (head == null) return spit.length * 0.5f;
                float best = float.MinValue, bestT = spit.length * 0.5f;
                for (float t = 0f; t <= spit.length; t += 1f / 30f)
                {
                    spit.SampleAnimation(go, t);
                    float z = head.position.z;
                    if (z > best) { best = z; bestT = t; }
                }
                return bestT;
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
