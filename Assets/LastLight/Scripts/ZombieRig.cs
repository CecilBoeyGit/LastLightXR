using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LastLight
{
    /// <summary>
    /// One instance of a rigged Tripo zombie. Plays its clips through a manually evaluated PlayableGraph,
    /// so no Animator Controller asset is needed and the game's own clock drives the pose (frozen menus,
    /// headless previews and the slowed horde all just pass a time). Works in edit mode for the preview.
    /// </summary>
    [ExecuteAlways]
    public class ZombieRig : MonoBehaviour
    {
        static readonly int TintId = Shader.PropertyToID("_Tint");

        public Transform Head { get; private set; }
        public Renderer[] Renderers { get; private set; }

        AnimationClip[] clips;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        MaterialPropertyBlock mpb;

        /// <summary>
        /// Instantiates <paramref name="prefab"/> under <paramref name="parent"/>, scaled so it stands
        /// <paramref name="targetHeight"/> local units tall, with every renderer on <paramref name="material"/>.
        /// </summary>
        public static ZombieRig Spawn(GameObject prefab, Material material, Transform parent, float targetHeight, float sourceHeight, params AnimationClip[] clips)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = prefab.name;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = prefab.transform.localScale * (targetHeight / Mathf.Max(sourceHeight, 1e-4f));

            var rig = go.AddComponent<ZombieRig>();
            rig.clips = clips;
            rig.Renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rig.Renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            rig.Head = FindBone(t, "Head");
            rig.mpb = new MaterialPropertyBlock();

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            rig.graph = PlayableGraph.Create("LL " + prefab.name);
            rig.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(rig.graph, "Pose", animator);
            rig.mixer = AnimationMixerPlayable.Create(rig.graph, clips.Length);
            rig.playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var p = AnimationClipPlayable.Create(rig.graph, clips[i]);
                p.SetApplyFootIK(false);
                p.SetApplyPlayableIK(false);
                rig.graph.Connect(p, 0, rig.mixer, i);
                rig.mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
                rig.playables[i] = p;
            }
            output.SetSourcePlayable(rig.mixer);
            rig.Pose(0, 0f);
            return rig;
        }

        static Transform FindBone(Transform root, string suffix)
        {
            foreach (var b in root.GetComponentsInChildren<Transform>(true))
                if (b.name == suffix || b.name.EndsWith(":" + suffix)) return b;
            return null;
        }

        public float Length(int clip) => clips[clip].length;

        /// <summary>Poses the model at <paramref name="time"/> seconds into clip <paramref name="clip"/>, looping it.</summary>
        public void Pose(int clip, float time)
        {
            if (!graph.IsValid()) return;
            for (int i = 0; i < playables.Length; i++) mixer.SetInputWeight(i, i == clip ? 1f : 0f);
            playables[clip].SetTime(Mathf.Repeat(time, clips[clip].length));
            graph.Evaluate(0f);
        }

        /// <summary>
        /// Blends a looping base clip with a one-shot overlay clip. <paramref name="overlayWeight"/> 0 shows only the
        /// base; the overlay time is clamped to its length.
        /// </summary>
        public void Pose(int baseClip, float baseTime, int overlayClip, float overlayTime, float overlayWeight)
        {
            if (!graph.IsValid()) return;
            overlayWeight = Mathf.Clamp01(overlayWeight);
            for (int i = 0; i < playables.Length; i++)
                mixer.SetInputWeight(i, i == baseClip ? 1f - overlayWeight : i == overlayClip ? overlayWeight : 0f);
            playables[baseClip].SetTime(Mathf.Repeat(baseTime, clips[baseClip].length));
            playables[overlayClip].SetTime(Mathf.Clamp(overlayTime, 0f, clips[overlayClip].length));
            graph.Evaluate(0f);
        }

        /// <summary>Multiplies the model's colour, for hit flashes and crowd variety.</summary>
        public void SetTint(Color c)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.Clear();
            mpb.SetColor(TintId, c);
            foreach (var r in Renderers) if (r != null) r.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
