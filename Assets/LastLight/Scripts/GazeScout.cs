using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace LastLight
{
    public enum GazeSource { None, EyeGaze, Mouse, Head, Scripted }

    /// <summary>
    /// Direction 02 scouting. Filtered eye gaze settles a broad light patch on terrain past the rim.
    /// Hazards caught in the light leave a short-lived outline that scrolls with them.
    /// Looking never steers or brakes: it only reveals, and collects pickups you stare at.
    /// </summary>
    public class GazeScout
    {
        public const float PatchRadius = 8.5f;
        const float HoldAfterLoss = 0.7f;
        const float MemorySeconds = 3.5f;
        const float HeadFallbackDelay = 0.5f;   // long enough to ride out a blink
        const float StaleAfter = 0.35f;         // real eye data always jitters, so a frozen value means no data
        const float HorizonLimit = -0.02f;      // flatter than this, aim at the edge of the scouting range

        // Speed-adaptive smoothing: steady during a stare, quick on a glance, never stop-go.
        const float MinCutoffHz = 1.6f;
        const float SpeedCutoffGain = 0.09f;
        const float SpeedFilterHz = 1.2f;
        const float DiagnosticSeconds = 5f;

        public GazeSource Source { get; private set; } = GazeSource.None;
        public Vector3 Patch { get; private set; } = new Vector3(0f, 0f, Diorama.Radius + 6f);
        public float Intensity { get; private set; }
        public bool EyeAvailable { get; private set; }

        /// <summary>While true, the flashlight powerup reveals the whole scouting region without gaze.</summary>
        public bool FlashlightActive;

        /// <summary>When set, the autopilot or tests supply the gaze point (diorama-local).</summary>
        public Vector3? ScriptedTarget;

        /// <summary>When set, tests supply a world-space gaze ray that goes through the real ray handling.</summary>
        public Ray? ScriptedWorldRay;

        bool localRayValid;
        Vector3 localRayOrigin, localRayDir;

        /// <summary>The current gaze ray in diorama-local units, used for staring at pickups.</summary>
        public bool TryGetLocalRay(out Vector3 origin, out Vector3 direction)
        {
            origin = localRayOrigin;
            direction = localRayDir;
            return localRayValid;
        }

        /// <summary>True when the scout light or the flashlight is currently lighting a disk-local point.</summary>
        public bool IsLit(Vector3 local)
        {
            if (FlashlightActive) return true;
            var flat = new Vector2(local.x - Patch.x, local.z - Patch.z);
            return Intensity > 0.3f && flat.magnitude < PatchRadius;
        }

        readonly Diorama diorama;
        readonly Transform head;
        readonly Material glowMat;
        readonly Transform orb, pool;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static Mesh markerMesh;

        PXR_Manager pxr;
        Vector3 target;
        Vector3 patchVel;
        Vector3 targetVelocity;
        float lostTimer = 99f;
        float eyeLostTimer = 99f;
        Vector3 lastEyeDir;
        float eyeStaleTimer;
        float diagTimer;
        int diagEye, diagEyeFail, diagEyeStale, diagHead, diagNone, diagClamped;

        public GazeScout(Diorama diorama, Transform head, Material glowMat)
        {
            this.diorama = diorama;
            this.head = head;
            this.glowMat = glowMat;
            target = Patch;

            var mb = new MeshBuilder();
            mb.Orb(Vector3.zero, 0.4f, new Color(1f, 0.75f, 0.35f, 0.9f));
            mb.Orb(Vector3.zero, 0.7f, new Color(1f, 0.6f, 0.2f, 0.2f));
            orb = MeshBuilder.Spawn("Scout Light", diorama.transform, mb.ToMesh("LL_ScoutOrb"), glowMat).transform;
            mb.Clear();
            // Faint pool plus a thin edge ring: the ghost terrain inside stays readable.
            mb.SoftDisc(Vector3.zero, PatchRadius, 32, new Color(1f, 0.62f, 0.22f, 0.06f), new Color(1f, 0.55f, 0.2f, 0f));
            mb.Ring(Vector3.zero, PatchRadius * 0.97f, PatchRadius, 40, new Color(1f, 0.62f, 0.22f, 0.35f));
            pool = MeshBuilder.Spawn("Scout Pool", diorama.transform, mb.ToMesh("LL_ScoutPool"), glowMat).transform;

            if (markerMesh == null)
            {
                mb.Clear();
                mb.Ring(Vector3.zero, 1.5f, 1.8f, 24, new Color(1f, 1f, 1f, 0.9f));
                mb.Box(new Vector3(0f, 1.8f, 0f), new Vector3(0.08f, 3.2f, 0.08f), new Color(1f, 1f, 1f, 0.6f));
                markerMesh = mb.ToMesh("LL_MemoryMarker");
            }
            pxr = Object.FindFirstObjectByType<PXR_Manager>();
        }

        public void Reset()
        {
            Patch = new Vector3(0f, 0f, Diorama.Radius + 6f);
            target = Patch;
            patchVel = Vector3.zero;
            targetVelocity = Vector3.zero;
            Intensity = 0f;
        }

        public void Tick(float dt, IList<Hazard> hazards, bool allowMouse)
        {
            Vector3 hit;
            bool valid = TryGetGazePoint(allowMouse, out hit);

            if (valid)
            {
                lostTimer = 0f;
                float step = Mathf.Max(dt, 1e-4f);
                Vector3 d = hit - target; d.y = 0f;
                // Averaging the velocity cancels eye jitter, so only a real movement raises the cutoff.
                targetVelocity = Vector3.Lerp(targetVelocity, d / step, 1f - Mathf.Exp(-2f * Mathf.PI * SpeedFilterHz * step));
                float cutoff = MinCutoffHz + SpeedCutoffGain * targetVelocity.magnitude;
                target += d * (1f - Mathf.Exp(-2f * Mathf.PI * cutoff * step));
            }
            else
            {
                lostTimer += dt;
                targetVelocity = Vector3.zero;
            }

            Patch = Vector3.SmoothDamp(Patch, target, ref patchVel, 0.06f, 400f, Mathf.Max(dt, 1e-4f));
            ReportDiagnostics(dt);
            float wanted = lostTimer < HoldAfterLoss ? 1f : 0f;
            Intensity = Mathf.MoveTowards(Intensity, wanted, dt * (wanted > Intensity ? 4f : 1.5f));

            orb.localPosition = Patch + Vector3.up * 3.2f;
            orb.gameObject.SetActive(Intensity > 0.01f);
            pool.localPosition = Patch + Vector3.up * 0.1f;
            pool.gameObject.SetActive(Intensity > 0.01f);

            UpdateMemory(dt, hazards);
        }

        /// <summary>Writes a short summary to the device log, so real-headset gaze quality can be measured.</summary>
        void ReportDiagnostics(float dt)
        {
            if (!Application.isPlaying) return;
            switch (Source)
            {
                case GazeSource.EyeGaze: diagEye++; break;
                case GazeSource.Head: diagHead++; break;
                case GazeSource.None: diagNone++; break;
            }
            diagTimer += dt;
            if (diagTimer < DiagnosticSeconds) return;
            Debug.Log($"[LastLight] Gaze {DiagnosticSeconds:0}s: eye frames {diagEye}, head {diagHead}, none {diagNone}, eye read failed {diagEyeFail}, eye stale {diagEyeStale}, clamped to range {diagClamped}, source {Source}, intensity {Intensity:0.00}");
            diagTimer = 0f;
            diagEye = diagEyeFail = diagEyeStale = diagHead = diagNone = diagClamped = 0;
        }

        bool TryGetGazePoint(bool allowMouse, out Vector3 local)
        {
            local = default;
            localRayValid = false;
            if (ScriptedWorldRay.HasValue)
            {
                Source = GazeSource.Scripted;
                return Intersect(ScriptedWorldRay.Value, out local);
            }
            if (ScriptedTarget.HasValue)
            {
                Source = GazeSource.Scripted;
                local = ClampToRange(ScriptedTarget.Value);
                localRayValid = true;
                localRayOrigin = ScriptedTarget.Value + Vector3.up * 30f;
                localRayDir = Vector3.down;
                return true;
            }

            Ray ray;
            if (TryEyeRay(out ray))
            {
                eyeLostTimer = 0f;
                Source = GazeSource.EyeGaze;
                return Intersect(ray, out local);
            }
            eyeLostTimer += Time.unscaledDeltaTime;

            if (allowMouse && Mouse.current != null && head != null)
            {
                var cam = head.GetComponent<Camera>();
                if (cam != null)
                {
                    Source = GazeSource.Mouse;
                    return Intersect(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out local);
                }
            }

            // Head aim is the explicit fallback when gaze data is missing for a while.
            if (head != null && (!EyeAvailable || eyeLostTimer > HeadFallbackDelay))
            {
                Source = GazeSource.Head;
                return Intersect(new Ray(head.position, head.forward), out local);
            }
            Source = GazeSource.None;
            return false;
        }

        bool TryEyeRay(out Ray ray)
        {
            ray = default;
            EyeAvailable = false;
            if (Application.platform != RuntimePlatform.Android || !XRSettings.isDeviceActive) return false;
            if (pxr == null) pxr = Object.FindFirstObjectByType<PXR_Manager>();
            if (pxr == null || !pxr.eyeTracking || head == null) return false;
            EyeAvailable = true;
            if (!PXR_EyeTracking.GetCombineEyeGazeVector(out Vector3 dir) || dir.sqrMagnitude < 1e-6f)
            {
                diagEyeFail++;
                return false;
            }
            // The read can keep succeeding while returning the same old value. Treat that as lost gaze.
            if ((dir - lastEyeDir).sqrMagnitude < 1e-12f)
            {
                eyeStaleTimer += Time.unscaledDeltaTime;
                if (eyeStaleTimer > StaleAfter)
                {
                    diagEyeStale++;
                    return false;
                }
            }
            else eyeStaleTimer = 0f;
            lastEyeDir = dir;
            PXR_EyeTracking.GetCombineEyeGazePoint(out Vector3 origin);
            // Both values are in the head camera's frame.
            ray = new Ray(head.TransformPoint(origin), head.TransformDirection(dir.normalized));
            return true;
        }

        bool Intersect(Ray worldRay, out Vector3 local)
        {
            local = default;
            Vector3 o = diorama.ToLocal(worldRay.origin);
            Vector3 d = diorama.DirToLocal(worldRay.direction);
            localRayValid = true;
            localRayOrigin = o;
            localRayDir = d.normalized;

            // Looking far away or above the horizon keeps the light at the edge of its range, in the
            // direction of the gaze. Rejecting those rays is what used to freeze the light in place.
            Vector3 p;
            bool flat = localRayDir.y > HorizonLimit;
            float t = flat ? 0f : (0.3f - o.y) / d.y;
            if (flat || t <= 0f)
            {
                var horizontal = new Vector3(localRayDir.x, 0f, localRayDir.z);
                if (horizontal.sqrMagnitude < 1e-4f) return false; // Looking straight up: no direction to follow.
                p = o + horizontal.normalized * 1000f;
            }
            else p = o + d * t;

            if (new Vector2(p.x, p.z).magnitude > Diorama.Radius + Diorama.ScoutRange) diagClamped++;
            local = ClampToRange(p);
            return true;
        }

        static Vector3 ClampToRange(Vector3 p)
        {
            p.y = 0f;
            var flat = new Vector2(p.x, p.z);
            float max = Diorama.Radius + Diorama.ScoutRange - PatchRadius * 0.5f;
            if (flat.magnitude > max) flat = flat.normalized * max;
            return new Vector3(flat.x, 0f, flat.y);
        }

        void UpdateMemory(float dt, IList<Hazard> hazards)
        {
            foreach (var h in hazards)
            {
                if (h == null || !h.alive) continue;
                Vector3 p = h.DioramaLocal;
                Vector2 flat = new Vector2(p.x - Patch.x, p.z - Patch.z);
                bool lit = FlashlightActive || (Intensity > 0.5f && flat.magnitude < PatchRadius * 0.9f);
                bool beyondRim = !Diorama.InsideDisk(p);
                if (lit && beyondRim)
                {
                    h.discovered = true;
                    h.memory = MemorySeconds;
                }
                else if (h.memory > 0f) h.memory -= dt;

                if (h.memory > 0f && h.marker == null)
                {
                    var r = MeshBuilder.Spawn("Memory", h.chunk.root, markerMesh, glowMat);
                    h.marker = r;
                }
                if (h.marker != null)
                {
                    bool show = h.memory > 0f;
                    h.marker.gameObject.SetActive(show);
                    if (show)
                    {
                        Vector3 mp = h.transform.localPosition; mp.y = 0.25f;
                        h.marker.transform.localPosition = mp;
                        float a = Mathf.Clamp01(h.memory / 1.5f);
                        Color c = Palette.Danger; c.a = a;
                        mpb.SetColor(TintId, c);
                        h.marker.SetPropertyBlock(mpb);
                    }
                }
            }
        }
    }
}
