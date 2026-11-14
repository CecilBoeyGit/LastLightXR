using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// Roof turret powerup. For ten seconds a machine gun on the roof picks the nearest shooter zombie
    /// anywhere in the proximity region, lit or not, swings onto it and fires a fast stream of tracers.
    /// Placeholder cubes; the real model can replace the meshes built here.
    /// </summary>
    public class TurretGun
    {
        public const float Duration = 10f;
        public const float FireRate = 14f;          // rounds per second
        public const float TurnRate = 540f;         // degrees per second
        public const float AimTolerance = 9f;       // degrees off target that still fires
        const float PopSeconds = 0.3f;

        public float TimeLeft { get; private set; }
        public bool Active => TimeLeft > 0f;
        public ShooterZombie Target { get; private set; }
        public int ShotsFired { get; private set; }
        public int Kills { get; private set; }
        public Transform Root => root;

        class Tracer { public Transform t; public float life; }

        readonly Transform root, yawPivot, pitchPivot, muzzle;
        readonly MeshRenderer flash;
        readonly List<Tracer> tracers = new List<Tracer>();
        readonly Material glowMat;
        static Mesh tracerMesh;
        float yaw, pitch, fireTimer, flashTimer, pop, idle;

        public TurretGun(Transform mount, Material solidMat, Material glowMat)
        {
            this.glowMat = glowMat;
            var mb = new MeshBuilder();
            root = new GameObject("Turret").transform;
            root.SetParent(mount, false);

            mb.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.16f, 0.62f), Palette.Metal);
            mb.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.12f, 0.3f), Palette.Metal * 0.8f);
            MeshBuilder.Spawn("Turret Base", root, mb.ToMesh("LL_TurretBase"), solidMat);
            mb.Clear();

            yawPivot = new GameObject("Turret Yaw").transform;
            yawPivot.SetParent(root, false);
            yawPivot.localPosition = new Vector3(0f, 0.36f, 0f);
            mb.Box(new Vector3(0f, 0f, -0.04f), new Vector3(0.5f, 0.3f, 0.56f), Palette.Metal * 1.15f);
            mb.Box(new Vector3(0f, 0.17f, -0.04f), new Vector3(0.4f, 0.05f, 0.4f), Palette.TurretRed);
            mb.Box(new Vector3(-0.3f, 0f, 0f), new Vector3(0.08f, 0.36f, 0.4f), Palette.Metal * 0.7f); // shield plates
            mb.Box(new Vector3(0.3f, 0f, 0f), new Vector3(0.08f, 0.36f, 0.4f), Palette.Metal * 0.7f);
            MeshBuilder.Spawn("Turret Head", yawPivot, mb.ToMesh("LL_TurretHead"), solidMat);
            mb.Clear();

            pitchPivot = new GameObject("Turret Pitch").transform;
            pitchPivot.SetParent(yawPivot, false);
            pitchPivot.localPosition = new Vector3(0f, 0.02f, 0.2f);
            mb.Box(new Vector3(-0.07f, 0f, 0.4f), new Vector3(0.07f, 0.07f, 0.8f), Palette.Tyre);
            mb.Box(new Vector3(0.07f, 0f, 0.4f), new Vector3(0.07f, 0.07f, 0.8f), Palette.Tyre);
            mb.Box(new Vector3(0f, 0f, 0.1f), new Vector3(0.24f, 0.14f, 0.22f), Palette.Metal);
            MeshBuilder.Spawn("Turret Barrels", pitchPivot, mb.ToMesh("LL_TurretBarrels"), solidMat);
            mb.Clear();

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(pitchPivot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.85f);
            mb.Orb(Vector3.zero, 0.22f, new Color(1f, 0.85f, 0.4f, 1f));
            mb.Orb(Vector3.zero, 0.42f, new Color(1f, 0.6f, 0.2f, 0.4f));
            flash = MeshBuilder.Spawn("Muzzle Flash", muzzle, mb.ToMesh("LL_MuzzleFlash"), glowMat);
            flash.gameObject.SetActive(false);

            if (tracerMesh == null)
            {
                mb.Clear();
                mb.Box(new Vector3(0f, 0f, 0.5f), new Vector3(0.07f, 0.07f, 1f), new Color(1f, 0.85f, 0.45f, 0.95f));
                tracerMesh = mb.ToMesh("LL_Tracer");
            }
            root.gameObject.SetActive(false);
        }

        public void SetMount(Vector3 localPos) => root.localPosition = localPos;

        public void Activate()
        {
            TimeLeft = Duration;
            if (!root.gameObject.activeSelf) pop = 0f;
            root.gameObject.SetActive(true);
        }

        public void Reset()
        {
            TimeLeft = 0f;
            Target = null;
            pop = 0f;
            yaw = pitch = 0f;
            root.gameObject.SetActive(false);
            foreach (var t in tracers) if (t.t != null) Object.DestroyImmediate(t.t.gameObject);
            tracers.Clear();
        }

        /// <summary>True for a living shooter close enough to engage: on the disk or out in the scouting region ahead.</summary>
        public static bool InRange(ShooterZombie z, Vehicle car)
        {
            if (z == null || !z.alive) return false;
            Vector3 p = z.DioramaLocal;
            return p.z > car.Z - 2f && p.z < Diorama.Radius + Diorama.ScoutRange - 1f && Mathf.Abs(p.x - car.ScreenX) < 26f;
        }

        /// <summary>Aims and fires. Returns the zombie killed this frame, if any.</summary>
        public ShooterZombie Tick(float dt, float time, IList<ShooterZombie> shooters, Vehicle car, Effects fx, Transform worldRoot, Diorama diorama, System.Action onShot)
        {
            TickTracers(dt);
            if (!root.gameObject.activeSelf) return null;

            if (TimeLeft > 0f) { TimeLeft -= dt; pop = Mathf.Min(1f, pop + dt / PopSeconds); }
            else
            {
                pop -= dt / PopSeconds;
                if (pop <= 0f) { root.gameObject.SetActive(false); Target = null; }
                root.localScale = Vector3.one * Mathf.Max(pop, 0f);
                return null;
            }
            root.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, pop);

            // Keep the current target while it lives and stays in range; otherwise take the nearest.
            if (!InRange(Target, car))
            {
                Target = null;
                float best = float.MaxValue;
                Vector3 carLocal = new Vector3(car.ScreenX, 0f, car.Z);
                foreach (var z in shooters)
                {
                    if (!InRange(z, car)) continue;
                    float d = (z.DioramaLocal - carLocal).sqrMagnitude;
                    if (d < best) { best = d; Target = z; }
                }
            }

            float wantYaw, wantPitch;
            if (Target != null)
            {
                Vector3 local = yawPivot.parent.InverseTransformPoint(diorama.ToWorld(Target.CenterDioramaLocal));
                Vector3 from = yawPivot.localPosition;
                Vector3 d = local - from;
                wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                wantPitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            else
            {
                idle += dt;
                wantYaw = Mathf.Sin(idle * 1.2f) * 50f;
                wantPitch = 4f;
            }
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, TurnRate * dt);
            pitch = Mathf.MoveTowardsAngle(pitch, Mathf.Clamp(wantPitch, -30f, 25f), TurnRate * dt);
            yawPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);

            ShooterZombie killed = null;
            bool aligned = Target != null && Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < AimTolerance;
            if (aligned)
            {
                fireTimer += dt;
                float interval = 1f / FireRate;
                while (fireTimer >= interval && Target != null && Target.alive)
                {
                    fireTimer -= interval;
                    if (Fire(Target, fx, worldRoot, diorama)) { killed = Target; Kills++; Target = null; }
                    onShot?.Invoke();
                }
            }
            else fireTimer = 0f;

            // Recoil kick and muzzle flash.
            if (flashTimer > 0f) flashTimer -= dt;
            flash.gameObject.SetActive(flashTimer > 0f);
            float kick = flashTimer > 0f ? 4f : 0f;
            pitchPivot.localRotation = Quaternion.Euler(pitch - kick, 0f, 0f);
            return killed;
        }

        /// <summary>One round: a tracer to the target and a hit. Returns true if it killed the zombie.</summary>
        bool Fire(ShooterZombie z, Effects fx, Transform worldRoot, Diorama diorama)
        {
            ShotsFired++;
            flashTimer = 0.035f;
            Vector3 from = diorama.ToLocal(muzzle.position);
            Vector3 to = z.CenterDioramaLocal + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.3f, 0.3f), 0f);
            var tr = MeshBuilder.Spawn("Tracer", worldRoot, tracerMesh, glowMat).transform;
            tr.localPosition = from;
            Vector3 d = to - from;
            tr.localRotation = Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : Vector3.forward);
            tr.localScale = new Vector3(1f, 1f, d.magnitude);
            tracers.Add(new Tracer { t = tr, life = 0.05f });

            z.health--;
            z.hitFlash = 0.08f;
            fx.Splash(z.chunk.root, to - z.chunk.root.localPosition, new Color(1f, 0.8f, 0.4f, 1f), 2, 3f, 0.14f, 0.25f);
            return z.health <= 0;
        }

        void TickTracers(float dt)
        {
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                tracers[i].life -= dt;
                if (tracers[i].life > 0f && tracers[i].t != null) continue;
                if (tracers[i].t != null) Object.DestroyImmediate(tracers[i].t.gameObject);
                tracers.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Front shovel powerup: a wide plough blade on the front bumper. It smashes fallen trees and boulders
    /// out of the way without slowing the jeep, for five hits, then breaks apart. Placeholder cubes.
    /// </summary>
    public class Shovel
    {
        public const int MaxHits = 5;
        public const float HalfWidth = 1.25f;       // the blade is wider than the jeep
        public const float Reach = 0.55f;           // how far it sticks out ahead of the bumper

        public int HitsLeft { get; private set; }
        public bool Active => HitsLeft > 0;
        public Transform Root => root;

        readonly Transform root, blade;
        readonly Transform[] pips = new Transform[MaxHits];
        float bump, pop;

        public Shovel(Transform mount, Material solidMat)
        {
            root = new GameObject("Shovel").transform;
            root.SetParent(mount, false);
            var mb = new MeshBuilder();
            // Arms from the bumper to the blade.
            mb.Box(new Vector3(-0.45f, 0.42f, -0.15f), new Vector3(0.12f, 0.12f, 0.5f), Palette.Metal);
            mb.Box(new Vector3(0.45f, 0.42f, -0.15f), new Vector3(0.12f, 0.12f, 0.5f), Palette.Metal);
            MeshBuilder.Spawn("Shovel Arms", root, mb.ToMesh("LL_ShovelArms"), solidMat);
            mb.Clear();

            blade = new GameObject("Shovel Blade").transform;
            blade.SetParent(root, false);
            blade.localPosition = new Vector3(0f, 0.5f, 0.15f);
            blade.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            mb.Box(Vector3.zero, new Vector3(HalfWidth * 2f, 0.95f, 0.12f), Palette.ShovelSteel);
            mb.Box(new Vector3(0f, -0.42f, 0.08f), new Vector3(HalfWidth * 2f + 0.06f, 0.14f, 0.1f), Palette.ShovelSteel * 1.25f); // cutting edge
            mb.Box(new Vector3(0f, 0.36f, 0.07f), new Vector3(HalfWidth * 2f, 0.12f, 0.04f), Palette.ShovelYellow);
            mb.Box(new Vector3(-HalfWidth, 0f, -0.04f), new Vector3(0.1f, 0.95f, 0.25f), Palette.Metal); // side plates
            mb.Box(new Vector3(HalfWidth, 0f, -0.04f), new Vector3(0.1f, 0.95f, 0.25f), Palette.Metal);
            MeshBuilder.Spawn("Blade", blade, mb.ToMesh("LL_ShovelBlade"), solidMat);
            mb.Clear();

            // Five lamps on top of the blade count the hits left.
            mb.Box(Vector3.zero, new Vector3(0.22f, 0.1f, 0.1f), Palette.BoostOrange * 2f);
            Mesh pipMesh = mb.ToMesh("LL_ShovelPip");
            for (int i = 0; i < MaxHits; i++)
            {
                var pr = MeshBuilder.Spawn("Hit Lamp", blade, pipMesh, solidMat);
                pr.transform.localPosition = new Vector3((i - (MaxHits - 1) * 0.5f) * 0.36f, 0.53f, 0f);
                pips[i] = pr.transform;
            }
            root.gameObject.SetActive(false);
        }

        public void SetMount(Vector3 localPos) => root.localPosition = localPos;

        public void Activate()
        {
            HitsLeft = MaxHits;
            if (!root.gameObject.activeSelf) pop = 0f;
            root.gameObject.SetActive(true);
            for (int i = 0; i < MaxHits; i++) pips[i].gameObject.SetActive(true);
        }

        public void Reset()
        {
            HitsLeft = 0;
            root.gameObject.SetActive(false);
        }

        /// <summary>Spends one hit. Returns true when that was the last one and the shovel broke off.</summary>
        public bool UseHit(Effects fx, Transform debrisParent, Vector3 debrisLocal, Vector3 push)
        {
            if (HitsLeft <= 0) return false;
            HitsLeft--;
            bump = 1f;
            for (int i = 0; i < MaxHits; i++) pips[i].gameObject.SetActive(i < HitsLeft);
            if (HitsLeft > 0) return false;
            fx.Burst(debrisParent, debrisLocal, DebrisKind.Metal, 8, 1f, push, 1.2f);
            root.gameObject.SetActive(false);
            return true;
        }

        public void Tick(float dt)
        {
            if (!root.gameObject.activeSelf) return;
            pop = Mathf.Min(1f, pop + dt / 0.25f);
            bump = Mathf.Max(0f, bump - dt * 5f);
            root.localScale = new Vector3(Mathf.SmoothStep(0f, 1f, pop), 1f, 1f);
            blade.localPosition = new Vector3(0f, 0.5f, 0.15f - bump * 0.18f);
        }
    }
}
