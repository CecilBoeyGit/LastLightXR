using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The floating disk. Owns the miniature coordinate frame ("game units"):
    /// the road runs along local +Z with its sideways position on local X, and the road surface is local Y = 0.
    /// The whole game is simulated in these units and scaled down by this transform.
    /// </summary>
    public class Diorama : MonoBehaviour
    {
        public const float Radius = 15f;        // disk radius in game units
        public const float RoadHalfWidth = 4.4f;
        public const float CarZ = -3f;          // the car stays just behind the centre
        public const float ScoutRange = 24f;    // how far past the rim the scout light can reach

        static readonly int IdDisk = Shader.PropertyToID("_LL_Disk");
        static readonly int IdScout = Shader.PropertyToID("_LL_Scout");
        static readonly int IdScoutIntensity = Shader.PropertyToID("_LL_ScoutIntensity");
        static readonly int IdHeadPos = Shader.PropertyToID("_LL_HeadPos");
        static readonly int IdHeadDir = Shader.PropertyToID("_LL_HeadDir");
        static readonly int IdMoon = Shader.PropertyToID("_LL_Moon");
        static readonly int IdAmbient = Shader.PropertyToID("_LL_Ambient");
        static readonly int IdScoutColor = Shader.PropertyToID("_LL_ScoutColor");
        static readonly int IdGhostColor = Shader.PropertyToID("_LL_GhostColor");
        static readonly int IdReveal = Shader.PropertyToID("_LL_Reveal");

        public Color ambient = new Color(0.17f, 0.21f, 0.21f);
        public Color scoutColor = new Color(1.0f, 0.62f, 0.22f);
        public Color ghostColor = new Color(0.95f, 0.78f, 0.48f);
        public Vector3 moonDirection = new Vector3(-0.4f, 1f, 0.3f);
        public float moonIntensity = 0.6f;

        public float Scale => transform.lossyScale.x;


        public Vector3 ToWorld(Vector3 local) => transform.TransformPoint(local);
        public Vector3 ToLocal(Vector3 world) => transform.InverseTransformPoint(world);
        public Vector3 DirToLocal(Vector3 worldDir) => transform.InverseTransformDirection(worldDir);

        /// <summary>Converts a world distance in metres to game units.</summary>
        public float MetresToUnits(float metres) => metres / Mathf.Max(Scale, 1e-5f);

        public void PushShaderGlobals(Vector3 scoutLocal, float scoutRadius, float scoutIntensity, Vector3 headLocal, Vector3 headDirLocal, float revealAll)
        {
            float s = Scale;
            Vector3 c = transform.position;
            Shader.SetGlobalVector(IdDisk, new Vector4(c.x, c.y, c.z, Radius * s));
            Vector3 sw = ToWorld(scoutLocal);
            Shader.SetGlobalVector(IdScout, new Vector4(sw.x, sw.y, sw.z, scoutRadius * s));
            Shader.SetGlobalFloat(IdScoutIntensity, scoutIntensity);
            Vector3 hw = ToWorld(headLocal);
            Shader.SetGlobalVector(IdHeadPos, new Vector4(hw.x, hw.y, hw.z, 16f * s));
            Vector3 hd = transform.TransformDirection(headDirLocal).normalized;
            Shader.SetGlobalVector(IdHeadDir, new Vector4(hd.x, hd.y, hd.z, 0));
            Vector3 md = moonDirection.normalized;
            Shader.SetGlobalVector(IdMoon, new Vector4(md.x, md.y, md.z, moonIntensity));
            Shader.SetGlobalVector(IdAmbient, ambient);
            Shader.SetGlobalVector(IdScoutColor, scoutColor);
            Shader.SetGlobalVector(IdGhostColor, ghostColor);
            Vector3 fw = transform.forward;
            Shader.SetGlobalVector(IdReveal, new Vector4(fw.x, revealAll, fw.z, (Radius + ScoutRange) * s));
        }

        public static bool InsideDisk(Vector3 local) => new Vector2(local.x, local.z).magnitude <= Radius;
    }
}
