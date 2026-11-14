using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// A spitter in the forest beside the road. It stands on the terrain off the road, rears back with a
    /// glowing mouth, and lobs acid at the jeep. Built with the terrain material, so past the rim it only
    /// shows where the scout light or flashlight falls; the glowing mouth gives the shot away in the dark.
    /// </summary>
    public class ShooterZombie : MonoBehaviour
    {
        public const int MaxHealth = 6;
        public const float WindupSeconds = 0.55f;
        public const float MouthHeight = 1.75f;

        public static Material GlowMaterial;

        public Chunk chunk;
        public bool alive = true;
        public int health = MaxHealth;
        public float cooldown;
        public float windup;
        public float hitFlash;
        public int shotsFired;

        Transform body;
        MeshRenderer bodyRenderer, mouth;
        float phase;
        static Mesh bodyMesh, mouthMesh;
        static readonly int TintId = Shader.PropertyToID("_Tint");
        MaterialPropertyBlock mpb;

        /// <summary>Position in diorama-local units.</summary>
        public Vector3 DioramaLocal => chunk.root.localPosition + transform.localPosition;
        public Vector3 MouthDioramaLocal => DioramaLocal + body.localRotation * new Vector3(0f, MouthHeight, 0.35f) * transform.localScale.x;
        public Vector3 CenterDioramaLocal => DioramaLocal + Vector3.up * 1.1f * transform.localScale.x;
        public bool Winding => windup > 0f;

        public static ShooterZombie Create(Chunk chunk, Material mat, Vector3 chunkLocal, System.Random rng)
        {
            BuildMeshes();
            var go = new GameObject("Shooter Zombie");
            go.transform.SetParent(chunk.root, false);
            go.transform.localPosition = chunkLocal;
            go.transform.localScale = Vector3.one * 1.4f;
            var z = go.AddComponent<ShooterZombie>();
            z.chunk = chunk;
            z.phase = (float)rng.NextDouble() * 10f;
            z.cooldown = 0.2f + (float)rng.NextDouble() * 0.9f;
            z.bodyRenderer = MeshBuilder.Spawn("Body", go.transform, bodyMesh, mat);
            z.body = z.bodyRenderer.transform;
            if (GlowMaterial != null)
            {
                z.mouth = MeshBuilder.Spawn("Acid Glow", z.body, mouthMesh, GlowMaterial);
                z.mouth.transform.localPosition = new Vector3(0f, MouthHeight, 0.33f);
                z.mouth.gameObject.SetActive(false);
            }
            z.mpb = new MaterialPropertyBlock();
            return z;
        }

        /// <summary>Faces the jeep and plays the idle sway, wind-up and hit flinch.</summary>
        public void Animate(float time, float dt, Vector3 carLocal)
        {
            Vector3 to = carLocal - DioramaLocal; to.y = 0f;
            float yaw = to.sqrMagnitude > 1e-4f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : 180f;
            float lean = Mathf.Sin(time * 2.1f + phase) * 5f;
            if (windup > 0f)
            {
                float k = 1f - windup / WindupSeconds;   // 0 at the start of the wind-up, 1 at the spit
                lean = -28f * Mathf.Sin(k * Mathf.PI * 0.5f);
            }
            if (hitFlash > 0f)
            {
                hitFlash -= dt;
                lean += 14f;
            }
            body.localRotation = Quaternion.Euler(lean, yaw, Mathf.Sin(time * 1.3f + phase) * 4f);

            mpb.Clear();
            mpb.SetColor(TintId, hitFlash > 0f ? new Color(2.2f, 1.6f, 1.4f, 1f) : Color.white);
            bodyRenderer.SetPropertyBlock(mpb);

            if (mouth != null)
            {
                bool show = windup > 0f;
                if (mouth.gameObject.activeSelf != show) mouth.gameObject.SetActive(show);
                if (show)
                {
                    float k = 1f - windup / WindupSeconds;
                    Color c = Palette.Acid; c.a = 0.4f + 0.6f * k;
                    mpb.Clear();
                    mpb.SetColor(TintId, c * (1f + k));
                    mouth.SetPropertyBlock(mpb);
                    mouth.transform.localScale = Vector3.one * (0.6f + 0.6f * k + Mathf.Sin(time * 40f) * 0.08f);
                }
            }
        }

        /// <summary>Hides the mouth glow, for when the zombie dies or its chunk is torn down.</summary>
        public void StopGlow()
        {
            if (mouth != null) mouth.gameObject.SetActive(false);
        }

        static void BuildMeshes()
        {
            if (bodyMesh != null) return;
            var mb = new MeshBuilder();
            Color skin = Palette.ShooterSkin, sack = Palette.ShooterSack, rags = Palette.ShooterRags;
            // Legs, slightly apart.
            mb.Box(new Vector3(-0.15f, 0.38f, 0f), new Vector3(0.2f, 0.76f, 0.22f), rags * 0.8f);
            mb.Box(new Vector3(0.15f, 0.38f, 0.05f), new Vector3(0.2f, 0.76f, 0.22f), rags * 0.8f);
            // Hunched torso.
            mb.Box(new Vector3(0f, 1.05f, 0.05f), new Vector3(0.6f, 0.7f, 0.38f), Quaternion.Euler(18f, 0f, 0f), rags);
            // Swollen acid sack on the back and a bloated throat.
            mb.Rock(new Vector3(0f, 1.2f, -0.32f), 0.42f, 77, sack);
            mb.Rock(new Vector3(0f, 1.45f, 0.24f), 0.2f, 78, sack * 1.2f);
            // Head pushed forward, with a dark open mouth.
            mb.Box(new Vector3(0f, 1.62f, 0.2f), new Vector3(0.36f, 0.36f, 0.36f), Quaternion.Euler(-10f, 0f, 0f), skin);
            mb.Box(new Vector3(0f, 1.55f, 0.39f), new Vector3(0.2f, 0.12f, 0.04f), new Color(0.05f, 0.08f, 0.03f));
            // Thin arms hanging low.
            mb.Box(new Vector3(-0.4f, 1.0f, 0.12f), new Vector3(0.13f, 0.7f, 0.13f), Quaternion.Euler(-20f, 0f, 8f), skin * 0.9f);
            mb.Box(new Vector3(0.4f, 1.0f, 0.12f), new Vector3(0.13f, 0.7f, 0.13f), Quaternion.Euler(-20f, 0f, -8f), skin * 0.9f);
            bodyMesh = mb.ToMesh("LL_ShooterZombie");
            mb.Clear();
            mb.Orb(Vector3.zero, 0.22f, new Color(1f, 1f, 1f, 1f));
            mb.Orb(Vector3.zero, 0.45f, new Color(1f, 1f, 1f, 0.35f));
            mouthMesh = mb.ToMesh("LL_AcidMouth");
        }
    }

    /// <summary>
    /// The shooters' acid. Each glob flies in the frame of the road chunk it will land on, so its landing
    /// spot scrolls toward the jeep with the road. A ring marks the spot. Landing leaves a short-lived
    /// puddle; touching the splash or the puddle slows the jeep.
    /// </summary>
    public class ShooterSystem
    {
        public const float FireNear = 7f;                // zombies stop spitting once this close ahead of the jeep
        public const int MaxInFlight = 2;
        public const float PuddleRadius = 1.15f;
        public const float PuddleSeconds = 2.2f;

        class Glob
        {
            public Transform t, marker;
            public Chunk chunk;
            public Vector3 from, to;      // chunk-local
            public float time, duration, height, s, xRel;
        }

        class Puddle
        {
            public Transform t;
            public MeshRenderer r;
            public Chunk chunk;
            public float age, s, xRel;
            public bool spent;
        }

        public float FireFar => Diorama.Radius + Diorama.ScoutRange - 6f;
        public int InFlight => globs.Count;
        public int PuddleCount => puddles.Count;
        public int ShotsFired { get; private set; }

        /// <summary>Called when acid touches the jeep. Return true if a shield absorbed it.</summary>
        public System.Func<bool> OnAcidHit;
        public System.Action<ShooterZombie> OnSpit;
        public System.Action OnSplash;

        readonly Transform worldRoot;
        readonly Material glowMat;
        readonly Effects fx;
        readonly List<Glob> globs = new List<Glob>();
        readonly List<Puddle> puddles = new List<Puddle>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static Mesh globMesh, markerMesh, puddleMesh;
        System.Random rng = new System.Random(911);

        public ShooterSystem(Transform worldRoot, Material glowMat, Effects fx)
        {
            this.worldRoot = worldRoot;
            this.glowMat = glowMat;
            this.fx = fx;
            BuildMeshes();
        }

        public void Reset()
        {
            foreach (var g in globs) { if (g.t != null) Object.DestroyImmediate(g.t.gameObject); if (g.marker != null) Object.DestroyImmediate(g.marker.gameObject); }
            foreach (var p in puddles) if (p.t != null) Object.DestroyImmediate(p.t.gameObject);
            globs.Clear();
            puddles.Clear();
            ShotsFired = 0;
        }

        /// <summary>Removes acid near the jeep, for a respawn.</summary>
        public void ClearNear(float carS, float range)
        {
            for (int i = globs.Count - 1; i >= 0; i--)
            {
                var g = globs[i];
                if (g.t != null) Object.DestroyImmediate(g.t.gameObject);
                if (g.marker != null) Object.DestroyImmediate(g.marker.gameObject);
                globs.RemoveAt(i);
            }
            foreach (var p in puddles)
                if (Mathf.Abs(p.s - carS) < range) p.spent = true;
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Tick(float dt, float time, bool running, TrackGenerator track, Vehicle car, float distance, GameStage stage)
        {
            float carS = distance + car.boostOffset;
            Vector3 carLocal = new Vector3(car.ScreenX, 0.6f, car.Z);
            float simDt = running ? dt : 0f;

            foreach (var z in track.shooters)
            {
                if (z == null || !z.alive) continue;
                z.Animate(time, simDt, carLocal);
                if (!running) continue;
                float lz = z.DioramaLocal.z;
                bool inWindow = lz > car.Z + FireNear && lz < FireFar;
                if (z.windup > 0f)
                {
                    z.windup -= dt;
                    if (z.windup <= 0f)
                    {
                        z.windup = 0f;
                        if (inWindow) Launch(z, track, car, carS);
                        z.cooldown = stage == GameStage.Late ? R(2.2f, 3.0f) : R(3.0f, 4.0f);
                    }
                    continue;
                }
                if (!inWindow) continue;
                // Each spitter gets a couple of shots in the medium stage, one more late on.
                if (z.shotsFired >= (stage == GameStage.Late ? 3 : 2)) continue;
                z.cooldown -= dt;
                if (z.cooldown <= 0f && globs.Count < MaxInFlight) z.windup = ShooterZombie.WindupSeconds;
            }

            TickGlobs(simDt, time, track, car, carS);
            TickPuddles(simDt, time, car, carS, running);
        }

        void Launch(ShooterZombie z, TrackGenerator track, Vehicle car, float carS)
        {
            float duration = R(1.1f, 1.4f);
            // Aim where the jeep will be when the glob lands, a little ahead, so the splash lands in its path.
            float s = carS + car.speed * duration + R(0.8f, 2.4f);
            Chunk c = track.ChunkAt(s);
            if (c == null) return;
            float half = Mathf.Max(track.RoadHalfAt(s) - 0.6f, 0.4f);
            float xRel = car.x + R(-0.5f, 0.5f);
            if (track.ForkAt(s) == null) xRel = Mathf.Clamp(xRel, -half, half);

            Vector3 toLocal = new Vector3(track.Center(s) + xRel, 0.05f, s - c.startS);
            Vector3 fromLocal = z.MouthDioramaLocal - c.root.localPosition;
            float span = new Vector2(toLocal.x - fromLocal.x, toLocal.z - fromLocal.z).magnitude;

            var g = new Glob
            {
                chunk = c,
                from = fromLocal,
                to = toLocal,
                duration = duration,
                height = 2.5f + span * 0.12f,
                s = s,
                xRel = xRel
            };
            g.t = MeshBuilder.Spawn("Acid Glob", c.root, globMesh, glowMat).transform;
            g.t.localPosition = fromLocal;
            g.marker = MeshBuilder.Spawn("Acid Target", c.root, markerMesh, glowMat).transform;
            g.marker.localPosition = toLocal + Vector3.up * 0.06f;
            globs.Add(g);
            z.shotsFired++;
            ShotsFired++;
            fx.Splash(z.chunk.root, z.MouthDioramaLocal - z.chunk.root.localPosition, Palette.Acid, 5, 3f, 0.18f, 0.4f);
            OnSpit?.Invoke(z);
        }

        void TickGlobs(float dt, float time, TrackGenerator track, Vehicle car, float carS)
        {
            for (int i = globs.Count - 1; i >= 0; i--)
            {
                var g = globs[i];
                if (g.t == null || g.chunk.root == null) { globs.RemoveAt(i); continue; }
                g.time += dt;
                float k = Mathf.Clamp01(g.time / g.duration);
                Vector3 p = Vector3.Lerp(g.from, g.to, k);
                p.y = Mathf.Lerp(g.from.y, g.to.y, k) + 4f * g.height * k * (1f - k);
                g.t.localPosition = p;
                g.t.localScale = Vector3.one * (1f + Mathf.Sin(time * 30f) * 0.12f);

                // The landing ring tightens and brightens as the glob comes down.
                Color mc = Palette.Acid; mc.a = 0.25f + 0.6f * k;
                mpb.Clear();
                mpb.SetColor(TintId, mc);
                if (g.marker != null)
                {
                    g.marker.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                    g.marker.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, k);
                }
                if (dt > 0f && rng.NextDouble() < 0.35) fx.Splash(g.chunk.root, p, Palette.Acid * 0.8f, 1, 0.6f, 0.12f, 0.35f, 0.2f);

                if (k < 1f) continue;
                Land(g, car, carS);
                if (g.t != null) Object.DestroyImmediate(g.t.gameObject);
                if (g.marker != null) Object.DestroyImmediate(g.marker.gameObject);
                globs.RemoveAt(i);
            }
        }

        void Land(Glob g, Vehicle car, float carS)
        {
            fx.Splash(g.chunk.root, g.to + Vector3.up * 0.2f, Palette.Acid, 14, 5f, 0.22f, 0.6f, 0.8f);
            OnSplash?.Invoke();
            var pr = MeshBuilder.Spawn("Acid Puddle", g.chunk.root, puddleMesh, glowMat);
            pr.transform.localPosition = g.to + Vector3.up * 0.04f;
            pr.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            var p = new Puddle { t = pr.transform, r = pr, chunk = g.chunk, s = g.s, xRel = g.xRel };
            puddles.Add(p);
            if (Touches(p, car, carS)) Hit(p);
        }

        void TickPuddles(float dt, float time, Vehicle car, float carS, bool running)
        {
            for (int i = puddles.Count - 1; i >= 0; i--)
            {
                var p = puddles[i];
                if (p.t == null || p.chunk.root == null) { puddles.RemoveAt(i); continue; }
                p.age += dt;
                if (p.age >= PuddleSeconds)
                {
                    Object.DestroyImmediate(p.t.gameObject);
                    puddles.RemoveAt(i);
                    continue;
                }
                float fade = 1f - Mathf.Clamp01((p.age - (PuddleSeconds - 0.7f)) / 0.7f);
                Color c = Palette.Acid * (p.spent ? 0.5f : 1f); c.a = (0.55f + 0.15f * Mathf.Sin(time * 6f + p.s)) * fade;
                mpb.Clear();
                mpb.SetColor(TintId, c);
                p.r.SetPropertyBlock(mpb);
                p.t.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(p.age / 0.25f));
                if (running && !p.spent && Touches(p, car, carS)) Hit(p);
            }
        }

        /// <summary>
        /// The road-relative X of the nearest acid about to land, or still wet, between the jeep and
        /// <paramref name="lookahead"/> units ahead. Lets the autopilot dodge like a player would.
        /// </summary>
        public bool NearestThreat(float carS, float lookahead, out float xRel)
        {
            xRel = 0f;
            float best = float.MaxValue;
            foreach (var g in globs)
            {
                float ds = g.s - carS;
                if (ds > -Vehicle.HalfLength && ds < lookahead && ds < best) { best = ds; xRel = g.xRel; }
            }
            foreach (var p in puddles)
            {
                float ds = p.s - carS;
                if (!p.spent && ds > -Vehicle.HalfLength && ds < lookahead && ds < best) { best = ds; xRel = p.xRel; }
            }
            return best < float.MaxValue;
        }

        static bool Touches(Puddle p, Vehicle car, float carS)
        {
            if (car.falling) return false;
            float reach = PuddleRadius * 0.7f;
            return Mathf.Abs(p.s - carS) < Vehicle.HalfLength + reach && Mathf.Abs(p.xRel - car.x) < Vehicle.HalfWidth + reach;
        }

        void Hit(Puddle p)
        {
            p.spent = true;
            fx.Splash(p.chunk.root, p.t.localPosition + Vector3.up * 0.5f, Palette.Acid, 10, 4f, 0.2f, 0.5f);
            OnAcidHit?.Invoke();
        }

        static void BuildMeshes()
        {
            if (globMesh != null) return;
            var mb = new MeshBuilder();
            mb.Orb(Vector3.zero, 0.32f, new Color(0.7f, 1f, 0.3f, 1f));
            mb.Orb(Vector3.zero, 0.6f, new Color(0.55f, 1f, 0.15f, 0.35f));
            globMesh = mb.ToMesh("LL_AcidGlob"); mb.Clear();
            mb.Ring(Vector3.zero, PuddleRadius * 0.85f, PuddleRadius, 28, Color.white);
            mb.Ring(Vector3.zero, 0.15f, 0.3f, 16, Color.white);
            markerMesh = mb.ToMesh("LL_AcidTarget"); mb.Clear();
            mb.SoftDisc(Vector3.zero, PuddleRadius, 20, new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0.15f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                mb.SoftDisc(new Vector3(Mathf.Cos(a), 0.01f, Mathf.Sin(a)) * PuddleRadius * 0.95f, 0.35f, 10, Color.white, new Color(1f, 1f, 1f, 0.1f));
            }
            puddleMesh = mb.ToMesh("LL_AcidPuddle");
        }
    }
}
