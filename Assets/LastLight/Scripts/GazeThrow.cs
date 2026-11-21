using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// Manual gaze attack on spitters. Stare at a spitter you can see (on the disk, or lit by the scout light or
    /// flashlight) for a second, and the jeep hurls a rock at it at high speed that knocks it down.
    /// A ring fills round the spitter while you stare. Positions are disk-local.
    /// </summary>
    public sealed class RockThrower
    {
        public const float StareSeconds = 1f;
        const float StareRadius = 1.9f;         // game units around the spitter's body that count as looking
        const float LoseGrace = 0.2f;
        const float RockSpeed = 42f;            // game units per second
        const float Cooldown = 0.3f;

        class Rock
        {
            public Transform t, glow;
            public ShooterZombie target;
            public Vector3 from;
            public float age, duration, arc;
        }

        /// <summary>The spitter being stared at, or null.</summary>
        public ShooterZombie Target { get; private set; }
        public float Dwell { get; private set; }
        public int InFlight => rocks.Count;
        public int Thrown { get; private set; }

        public System.Action<ShooterZombie> OnThrow;
        public System.Action<ShooterZombie> OnHit;

        readonly Transform parent;
        readonly Material rockMat, glowMat;
        readonly List<Rock> rocks = new List<Rock>();
        readonly Transform ring;
        readonly MeshRenderer ringRenderer;
        readonly Mesh ringMesh;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh rockMesh, glowMesh;
        float lost, cooldown;

        class Pop { public Transform t; public TextMesh text; public float age; }
        readonly List<Pop> pops = new List<Pop>();
        const float PopSeconds = 1.2f;

        /// <summary>A floating gold label (the coin reward) that rises and fades over the knocked-down spitter.</summary>
        public void Popup(Vector3 at, string label, Vector3 headLocal)
        {
            var text = UIText.Make(parent, "Reward", label, 1.1f, TextAnchor.MiddleCenter, Palette.CoinGold, 30, true);
            text.transform.localPosition = at;
            text.transform.localRotation = Quaternion.LookRotation(at - headLocal, Vector3.up);
            pops.Add(new Pop { t = text.transform, text = text });
        }

        void TickPops(float dt)
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                p.age += dt;
                if (p.t == null || p.age >= PopSeconds)
                {
                    if (p.t != null) Kill(p.t.gameObject);
                    pops.RemoveAt(i);
                    continue;
                }
                p.t.localPosition += Vector3.up * 2f * dt;
                Color c = Palette.CoinGold; c.a = 1f - Mathf.Clamp01((p.age - PopSeconds * 0.5f) / (PopSeconds * 0.5f));
                p.text.color = c;
            }
        }

        public RockThrower(Transform parent, Material rockMat, Material glowMat)
        {
            this.parent = parent;
            this.rockMat = rockMat;
            this.glowMat = glowMat;
            if (rockMesh == null)
            {
                var b = new MeshBuilder();
                b.Rock(Vector3.zero, 0.38f, 909, Palette.Rock * 1.3f);
                rockMesh = b.ToMesh("LL_ThrownRock");
                b.Clear();
                b.Orb(Vector3.zero, 0.6f, new Color(1f, 0.8f, 0.5f, 0.35f));
                glowMesh = b.ToMesh("LL_ThrownRockGlow");
            }
            ringMesh = new Mesh { name = "LL_StareRing" };
            ringRenderer = MeshBuilder.Spawn("Spitter Stare Ring", parent, ringMesh, glowMat);
            ring = ringRenderer.transform;
            ring.gameObject.SetActive(false);
        }

        public void Reset()
        {
            foreach (var r in rocks) if (r.t != null) Kill(r.t.gameObject);
            rocks.Clear();
            Target = null;
            Dwell = 0f;
            lost = 0f;
            cooldown = 0f;
            Thrown = 0;
            ring.gameObject.SetActive(false);
            foreach (var p in pops) if (p.t != null) Kill(p.t.gameObject);
            pops.Clear();
        }

        /// <param name="visible">Whether a disk-local point can be seen right now (on the disk or lit).</param>
        /// <param name="gaze">Disk-local gaze ray.</param>
        /// <param name="launch">Where rocks leave the jeep, disk-local.</param>
        public void Tick(float dt, bool running, IList<ShooterZombie> shooters, System.Func<Vector3, bool> visible, Ray? gaze, Vector3 launch, Vector3 headLocal)
        {
            if (cooldown > 0f) cooldown -= dt;
            ShooterZombie hit = null;
            if (running && gaze.HasValue)
            {
                Vector3 d = gaze.Value.direction.normalized;
                float best = float.MaxValue;
                for (int i = 0; i < shooters.Count; i++)
                {
                    var z = shooters[i];
                    if (z == null || !z.alive || Targeted(z)) continue;
                    Vector3 p = z.CenterDioramaLocal;
                    if (!visible(p)) continue;
                    Vector3 to = p - gaze.Value.origin;
                    if (Vector3.Dot(to, d) <= 0f) continue;
                    float dist = Vector3.Cross(d, to).magnitude;
                    float limit = StareRadius * (z == Target ? 1.35f : 1f);
                    if (dist < limit && dist < best) { best = dist; hit = z; }
                }
            }
            if (hit != null && hit == Target) { lost = 0f; Dwell += dt; }
            else if (hit != null) { Target = hit; Dwell = dt; lost = 0f; }
            else if (Target != null && Target.alive && running && (lost += dt) <= LoseGrace) { /* hold through a blink */ }
            else { Target = null; Dwell = 0f; lost = 0f; }

            if (Target != null && Dwell >= StareSeconds && cooldown <= 0f)
            {
                Throw(Target, launch);
                Target = null;
                Dwell = 0f;
            }
            TickRocks(running ? dt : 0f);
            TickPops(dt);
            DrawRing(headLocal);
        }

        bool Targeted(ShooterZombie z)
        {
            foreach (var r in rocks) if (r.target == z) return true;
            return false;
        }

        void Throw(ShooterZombie z, Vector3 launch)
        {
            var r = MeshBuilder.Spawn("Thrown Rock", parent, rockMesh, rockMat);
            var g = MeshBuilder.Spawn("Glow", r.transform, glowMesh, glowMat);
            float dist = Vector3.Distance(launch, z.CenterDioramaLocal);
            var rock = new Rock { t = r.transform, glow = g.transform, target = z, from = launch, duration = Mathf.Max(0.12f, dist / RockSpeed), arc = Mathf.Min(3f, dist * 0.08f) };
            r.transform.localPosition = launch;
            rocks.Add(rock);
            Thrown++;
            cooldown = Cooldown;
            OnThrow?.Invoke(z);
        }

        void TickRocks(float dt)
        {
            for (int i = rocks.Count - 1; i >= 0; i--)
            {
                var r = rocks[i];
                r.age += dt;
                float t = Mathf.Clamp01(r.age / r.duration);
                // Homes on the spitter (it scrolls with the road) along a shallow arc.
                Vector3 to = r.target != null ? r.target.CenterDioramaLocal : r.from;
                Vector3 p = Vector3.Lerp(r.from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * r.arc;
                r.t.localPosition = p;
                r.t.localRotation = Quaternion.Euler(r.age * 900f, r.age * 500f, 0f);
                if (t >= 1f)
                {
                    if (r.target != null && r.target.alive) OnHit?.Invoke(r.target);
                    Kill(r.t.gameObject);
                    rocks.RemoveAt(i);
                }
            }
        }

        static void Kill(GameObject g)
        {
            if (Application.isPlaying) Object.Destroy(g); else Object.DestroyImmediate(g);
        }

        void DrawRing(Vector3 headLocal)
        {
            bool show = Target != null && Dwell > 0.02f;
            if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
            if (!show) return;
            Vector3 at = Target.CenterDioramaLocal;
            ring.localPosition = at;
            ring.localRotation = Quaternion.LookRotation(at - headLocal, Vector3.up);
            mb.Clear();
            mb.Arc(Vector3.zero, 1.55f, 1.62f, 1f, 36, new Color(1f, 1f, 1f, 0.4f));
            mb.Arc(Vector3.zero, 1.5f, 1.8f, Dwell / StareSeconds, 36, Color.white);
            mb.ToMesh(ringMesh.name, ringMesh);
            Color c = new Color(1f, 0.45f, 0.25f, 1f) * 1.8f; c.a = 1f;
            mpb.SetColor("_Tint", c);
            ringRenderer.SetPropertyBlock(mpb);
        }
    }
}
