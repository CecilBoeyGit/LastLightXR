using UnityEngine;

namespace LastLight
{
    public enum RepairPhase { Hidden, Wrench, Dots }

    /// <summary>
    /// The repair quick-time event. Whenever the jeep is down to its last health bar, a wrench floats above it.
    /// Look at the wrench for half a second and three gaze dots follow, one at a time, each at a random spot
    /// around the jeep: hold your eyes on each for half a second. Clearing all three repairs one health bar.
    /// Getting a bar back any other way, or losing the last one, cancels it.
    /// Positions are disk-local; the wrench and dots ride along with the jeep.
    /// </summary>
    public sealed class RepairQte
    {
        public const float WrenchSeconds = 0.5f;
        public const float DotSeconds = 0.5f;
        public const int DotCount = 3;
        /// <summary>Seconds each dot waits to be looked at before the repair fails and the wrench comes back.</summary>
        public const float DotTimeLimit = 2.5f;
        const float WrenchHeight = 2.3f;
        const float WrenchScale = 0.6f;
        const float WrenchRadius = 1.2f, DotRadius = 1.15f;     // game units around the target that count as looking
        const float LoseGrace = 0.2f;
        static readonly Color WrenchColor = new Color(0.45f, 1f, 0.6f, 1f);
        static readonly Color[] DotColors = { new Color(0.45f, 1f, 0.6f, 1f), new Color(1f, 0.85f, 0.3f, 1f), new Color(0.5f, 0.85f, 1f, 1f) };

        public RepairPhase Phase { get; private set; }
        /// <summary>Which dot is showing, 0 to 2, while the dots are up.</summary>
        public int DotIndex { get; private set; }
        public float Dwell { get; private set; }
        public Vector3 WrenchLocal { get; private set; }
        public Vector3 DotLocal { get; private set; }
        /// <summary>Where the current dot sits relative to the jeep. For tests.</summary>
        public Vector3 DotOffset => dotOffset;
        public bool WantsGaze => Phase != RepairPhase.Hidden;

        public System.Action OnWrenchShown, OnStarted, OnCancelled, OnTimedOut;
        /// <summary>Seconds left on the current dot before the repair fails.</summary>
        public float DotTimeLeft => dotTimer;
        public System.Action<int> OnDotCleared;

        readonly Transform wrench, dot, ring;
        readonly MeshRenderer wrenchRenderer, dotRenderer, ringRenderer;
        readonly Mesh ringMesh;
        readonly UISprite badge;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        System.Random rng = new System.Random(1);
        Vector3 dotOffset;
        float lastAngle, lost, time, popTime, dotTimer;

        public RepairQte(Transform parent, Material glowMat, Material spriteMat = null)
        {
            Texture2D icon = Resources.Load<Texture2D>(UISprite.ResourceFolder + "pu_wrench");
            if (icon == null || spriteMat == null)
            {
                // Fallback line-art wrench: a handle with an open jaw at one end and a ring at the other, flat in XY.
                Color c = Color.white;
                mb.Box(new Vector3(0f, 0f, 0f), new Vector3(0.28f, 1.7f, 0.05f), c);
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.Lerp(-200f, 20f, i / 7f) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.42f + new Vector3(0f, 1.05f, 0f);
                    mb.Box(p, new Vector3(0.2f, 0.2f, 0.05f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg), c);
                }
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.3f + new Vector3(0f, -1.0f, 0f);
                    mb.Box(p, new Vector3(0.16f, 0.16f, 0.05f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg), c);
                }
            }
            // A soft glow behind the wrench, so it stands out over the road.
            mb.SoftDisc(Vector3.forward * 0.05f, 1.5f, 24, new Color(1f, 1f, 1f, 0.22f), new Color(1f, 1f, 1f, 0f));
            wrenchRenderer = MeshBuilder.Spawn("Repair Wrench", parent, mb.ToMesh("LL_RepairWrench"), glowMat);
            wrench = wrenchRenderer.transform;
            // The comic wrench badge, in front of the glow.
            if (icon != null && spriteMat != null) badge = new UISprite("Wrench Badge", wrench, icon, new Vector2(2.3f, 2.3f), spriteMat, 7);

            mb.Clear();
            mb.Orb(Vector3.zero, 0.5f, new Color(1f, 1f, 1f, 0.95f));
            mb.Orb(Vector3.zero, 0.9f, new Color(1f, 1f, 1f, 0.25f));
            dotRenderer = MeshBuilder.Spawn("Repair Dot", parent, mb.ToMesh("LL_RepairDot"), glowMat);
            dot = dotRenderer.transform;

            ringMesh = new Mesh { name = "LL_RepairRing" };
            ringRenderer = MeshBuilder.Spawn("Repair Ring", parent, ringMesh, glowMat);
            ring = ringRenderer.transform;
            Hide();
        }

        public void Reset(int seed)
        {
            rng = new System.Random(seed);
            Phase = RepairPhase.Hidden;
            DotIndex = 0;
            Dwell = 0f;
            lost = 0f;
            Hide();
        }

        void Hide()
        {
            wrench.gameObject.SetActive(false);
            dot.gameObject.SetActive(false);
            ring.gameObject.SetActive(false);
        }

        /// <summary>
        /// Advances the event. Returns true on the frame the third dot is cleared, when a health bar should be given back.
        /// </summary>
        /// <param name="available">The run is going and the jeep is on its last bar.</param>
        /// <param name="carLocal">The jeep's disk-local position.</param>
        /// <param name="gaze">Disk-local gaze ray, if there is one.</param>
        /// <param name="headLocal">The head in disk units, so the icons face the player.</param>
        public bool Tick(float dt, bool available, Vector3 carLocal, Ray? gaze, Vector3 headLocal)
        {
            time += dt;
            if (!available)
            {
                if (Phase == RepairPhase.Dots) OnCancelled?.Invoke();
                if (Phase != RepairPhase.Hidden) { Phase = RepairPhase.Hidden; Dwell = 0f; Hide(); }
                return false;
            }
            if (Phase == RepairPhase.Hidden)
            {
                Phase = RepairPhase.Wrench;
                Dwell = 0f;
                popTime = 0f;
                OnWrenchShown?.Invoke();
            }
            popTime += dt;

            WrenchLocal = carLocal + Vector3.up * WrenchHeight;
            if (Phase == RepairPhase.Wrench)
            {
                if (Stare(dt, gaze, WrenchLocal, WrenchRadius, WrenchSeconds))
                {
                    Phase = RepairPhase.Dots;
                    DotIndex = 0;
                    PickDot();
                    OnStarted?.Invoke();
                }
            }
            else
            {
                DotLocal = carLocal + dotOffset;
                // Left unattended, the repair fails: back to the wrench, to start again.
                dotTimer -= dt;
                if (dotTimer <= 0f)
                {
                    Phase = RepairPhase.Wrench;
                    Dwell = 0f;
                    lost = 0f;
                    popTime = 0f;
                    OnTimedOut?.Invoke();
                    Draw(headLocal);
                    return false;
                }
                if (Stare(dt, gaze, DotLocal, DotRadius, DotSeconds))
                {
                    OnDotCleared?.Invoke(DotIndex);
                    DotIndex++;
                    if (DotIndex >= DotCount)
                    {
                        Phase = RepairPhase.Hidden;
                        Hide();
                        return true;
                    }
                    PickDot();
                    DotLocal = carLocal + dotOffset;
                }
            }
            Draw(headLocal);
            return false;
        }

        /// <summary>Counts a stare at a target, holding through a fifth of a second of lost gaze. True when it's done.</summary>
        bool Stare(float dt, Ray? gaze, Vector3 target, float radius, float seconds)
        {
            bool on = false;
            if (gaze.HasValue)
            {
                Vector3 d = gaze.Value.direction.normalized;
                Vector3 to = target - gaze.Value.origin;
                on = Vector3.Dot(to, d) > 0f && Vector3.Cross(d, to).magnitude < radius * (Dwell > 0f ? 1.35f : 1f);
            }
            if (on) { lost = 0f; Dwell += dt; }
            else if (Dwell > 0f && (lost += dt) > LoseGrace) Dwell = Mathf.Max(0f, Dwell - dt * 1.5f);
            if (Dwell < seconds) return false;
            Dwell = 0f;
            lost = 0f;
            return true;
        }

        /// <summary>A random spot around the jeep, well away from the last dot.</summary>
        void PickDot()
        {
            float angle = 0f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                angle = (float)(rng.NextDouble() * 300.0 - 150.0);
                if (DotIndex == 0 || Mathf.Abs(Mathf.DeltaAngle(angle, lastAngle)) > 70f) break;
            }
            lastAngle = angle;
            float dist = 2.2f + (float)rng.NextDouble() * 1.2f;
            float height = 0.9f + (float)rng.NextDouble() * 0.9f;
            float a = angle * Mathf.Deg2Rad;
            dotOffset = new Vector3(Mathf.Sin(a) * dist, height, Mathf.Cos(a) * dist);
            Dwell = 0f;
            popTime = 0f;
            dotTimer = DotTimeLimit;
        }

        void Draw(Vector3 headLocal)
        {
            bool wrenchUp = Phase == RepairPhase.Wrench;
            if (wrench.gameObject.activeSelf != wrenchUp) wrench.gameObject.SetActive(wrenchUp);
            if (dot.gameObject.activeSelf != !wrenchUp) dot.gameObject.SetActive(!wrenchUp);
            Vector3 at = wrenchUp ? WrenchLocal : DotLocal;
            Quaternion face = Quaternion.LookRotation(at - headLocal, Vector3.up);
            float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(popTime / 0.25f));
            if (wrenchUp)
            {
                wrench.localPosition = at + Vector3.up * Mathf.Sin(time * 3f) * 0.12f;
                wrench.localRotation = face * Quaternion.Euler(0f, 0f, badge != null ? Mathf.Sin(time * 2.2f) * 8f : 35f + Mathf.Sin(time * 2.2f) * 12f);
                wrench.localScale = Vector3.one * WrenchScale * pop * (1f + 0.06f * Mathf.Sin(time * 8f));
                Color c = WrenchColor * (1.3f + 0.5f * Mathf.Abs(Mathf.Sin(time * 4f))); c.a = 1f;
                mpb.SetColor("_Tint", c);
                wrenchRenderer.SetPropertyBlock(mpb);
            }
            else
            {
                dot.localPosition = at;
                dot.localScale = Vector3.one * pop * (1f + 0.12f * Mathf.Sin(time * 9f));
                Color c = DotColors[Mathf.Clamp(DotIndex, 0, DotColors.Length - 1)] * 1.6f; c.a = 1f;
                mpb.SetColor("_Tint", c);
                dotRenderer.SetPropertyBlock(mpb);
            }

            // A ring round the target that fills while you look, with a tick for each dot already cleared.
            ring.gameObject.SetActive(true);
            ring.localPosition = at;
            ring.localRotation = face;
            float seconds = wrenchUp ? WrenchSeconds : DotSeconds;
            float r = wrenchUp ? 1.05f : 1.15f;
            mb.Clear();
            mb.Arc(Vector3.zero, r, r + 0.06f, 1f, 40, new Color(1f, 1f, 1f, 0.35f));
            if (Dwell > 0.01f) mb.Arc(Vector3.zero, r - 0.05f, r + 0.22f, Dwell / seconds, 40, Color.white);
            // The dot's time limit: a thin outer ring that runs down, faint while there's plenty left.
            if (!wrenchUp)
            {
                float left = Mathf.Clamp01(dotTimer / DotTimeLimit);
                mb.Arc(Vector3.zero, r + 0.32f, r + 0.42f, left, 40, new Color(1f, 1f, 1f, left > 0.4f ? 0.55f : 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(time * 14f))));
            }
            if (!wrenchUp)
                for (int i = 0; i < DotCount; i++)
                {
                    var p = new Vector3(-0.5f + i * 0.5f, -r - 0.45f, 0f);
                    float s = i < DotIndex ? 0.16f : i == DotIndex ? 0.13f : 0.08f;
                    mb.Quad(p + new Vector3(-s, -s), p + new Vector3(-s, s), p + new Vector3(s, s), p + new Vector3(s, -s), new Color(1f, 1f, 1f, i <= DotIndex ? 1f : 0.4f));
                }
            mb.ToMesh(ringMesh.name, ringMesh);
            Color rc = (wrenchUp ? WrenchColor : DotColors[Mathf.Clamp(DotIndex, 0, DotColors.Length - 1)]) * 1.5f; rc.a = 1f;
            mpb.SetColor("_Tint", rc);
            ringRenderer.SetPropertyBlock(mpb);
        }
    }
}
