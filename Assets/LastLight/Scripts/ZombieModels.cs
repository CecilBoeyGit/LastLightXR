using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The Tripo zombie models, their clips and materials, gathered by the editor's Configure Zombie Models
    /// step into Resources so the code-built game can load them at runtime. When the asset or a model is
    /// missing, the horde and spitters fall back to their procedural box meshes.
    /// </summary>
    public class ZombieModels : ScriptableObject
    {
        public const string ResourcePath = "LastLightModels/ZombieModels";

        [Header("Runner (horde walkers)")]
        public GameObject runnerPrefab;
        public Material runnerMaterial;
        public AnimationClip runnerRun;
        [Tooltip("Height of the imported runner in its own units, measured by the editor step.")]
        public float runnerHeight = 1f;

        [Header("Spitter (roadside shooters)")]
        public GameObject spitterPrefab;
        public Material spitterMaterial;
        public AnimationClip spitterIdle;
        public AnimationClip spitterSpit;
        public float spitterHeight = 1f;
        [Tooltip("Seconds into the spit clip where the head thrusts furthest forward; the glob launches here.")]
        public float spitApexTime = 1f;

        public bool HasRunner => runnerPrefab != null && runnerMaterial != null && runnerRun != null && runnerHeight > 0f;
        public bool HasSpitter => spitterPrefab != null && spitterMaterial != null && spitterIdle != null && spitterSpit != null && spitterHeight > 0f;

        static ZombieModels cached;
        static bool loaded;

        /// <summary>The configured models, or null. Loaded once per session.</summary>
        public static ZombieModels Load()
        {
            if (!loaded || cached == null)
            {
                cached = Resources.Load<ZombieModels>(ResourcePath);
                loaded = true;
            }
            return cached;
        }

        /// <summary>Set false to force the procedural box zombies (for comparisons and tests).</summary>
        public static bool Enabled = true;

        public static ZombieModels Active => Enabled ? Load() : null;
    }
}
