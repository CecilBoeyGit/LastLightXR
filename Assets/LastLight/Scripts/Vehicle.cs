using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The ochre escape vehicle. Speed drives how fast the world scrolls past it.
    /// Sideways it moves smoothly to wherever it is steered on the open road. Curves push it toward
    /// the outside edge, and scraping an edge slows it down. A boost carries it forward along the disk.
    /// X positions are road-relative: 0 is the centreline at the car.
    /// </summary>
    public class Vehicle
    {
        public const float HalfLength = 1.4f;
        public const float HalfWidth = 0.7f;
        const float BoostForward = 8f;

        public float x;                 // current road-relative position
        public float targetX;           // where it is being steered
        public float speed;
        public int health;
        public float stallTimer;        // > 0 while stuck against a blocker
        public float hitFlash;
        public bool dragging;           // a hand holds the beacon
        public bool scraping;           // pressed against a road edge
        public bool trapped;            // ran into a dead-end wall
        public const float ShieldSeconds = 10f;
        public float shieldTime;        // seconds of shield left; while it lasts nothing can hurt or slow the jeep
        /// <summary>True while shielded. Setting it true tops the shield up to full; false drops it.</summary>
        public bool shield
        {
            get => shieldTime > 0f;
            set => shieldTime = value ? Mathf.Max(shieldTime, ShieldSeconds) : 0f;
        }
        public bool falling;            // went over a cliff edge
        public int branch;              // -1 or +1 while committed to a side of a split road
        public float boostOffset;       // how far forward along the disk the car currently sits
        public float lastDrift;         // sideways push from the curve this frame
        public float screenOffset;      // where the road centre at the car sits on the disk
        public float acid;              // seconds of acid slow left, drives the goo on the body
        public float invulnerable;      // seconds of respawn grace left; the body blinks meanwhile

        float xVel, boostVel;
        float fallY, fallVelocity, fallTilt;
        readonly Transform root;
        readonly Transform body;
        readonly MeshFilter bodyFilter;
        readonly MeshRenderer bodyRenderer;
        readonly GameObject shieldOrb;
        readonly MeshRenderer goo;
        readonly MaterialPropertyBlock gooBlock = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");

        public Transform Root => root;
        /// <summary>The visible body. Gear such as the turret and shovel mounts here, so it follows the body's lean.</summary>
        public Transform Body => body;
        public int Model { get; private set; }
        public int ModelColor { get; private set; }
        public float RoofY => VehicleCatalog.All[Model].roofY;
        public float FrontZ => VehicleCatalog.All[Model].frontZ;
        public bool Stalled => stallTimer > 0f || trapped;
        public float Z => Diorama.CarZ + boostOffset;
        public float ScreenX => x + screenOffset;
        public float TargetScreenX => targetX + screenOffset;

        public Vehicle(Transform parent, Material mat, Material glowMat)
        {
            var go = new GameObject("Vehicle");
            go.transform.SetParent(parent, false);
            root = go.transform;

            // Body, cabin, roof rack, wheels, lamps, facing +Z. The garage can swap in another model.
            bodyRenderer = MeshBuilder.Spawn("Body", root, VehicleCatalog.GetMesh(0, 0), mat);
            body = bodyRenderer.transform;
            bodyFilter = bodyRenderer.GetComponent<MeshFilter>();

            var mb = new MeshBuilder();
            mb.Orb(new Vector3(0f, 0.8f, 0f), 2.1f, new Color(0.25f, 0.6f, 1f, 0.28f));
            shieldOrb = MeshBuilder.Spawn("Shield", root, mb.ToMesh("LL_ShieldOrb"), glowMat).gameObject;
            shieldOrb.SetActive(false);

            // Acid goo dripping over the hood and roof while the jeep is slowed.
            mb.Clear();
            var rng = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.2f, 1.15f + (float)rng.NextDouble() * 0.45f, ((float)rng.NextDouble() - 0.4f) * 2.2f);
                mb.Orb(p, 0.14f + (float)rng.NextDouble() * 0.14f, new Color(0.6f, 1f, 0.2f, 0.9f));
            }
            goo = MeshBuilder.Spawn("Acid Goo", body, mb.ToMesh("LL_AcidGoo"), glowMat);
            goo.gameObject.SetActive(false);
        }

        /// <summary>Shows a vehicle from the garage in one of its colours.</summary>
        public void SetModel(int vehicle, int color)
        {
            Model = Mathf.Clamp(vehicle, 0, VehicleCatalog.Count - 1);
            ModelColor = color;
            bodyFilter.sharedMesh = VehicleCatalog.GetMesh(Model, color);
        }

        /// <summary>Puts a wrecked, trapped or fallen jeep back on its wheels for a respawn.</summary>
        public void Revive(float roadX)
        {
            falling = false;
            fallY = fallVelocity = fallTilt = 0f;
            body.gameObject.SetActive(true);
            trapped = false;
            scraping = false;
            stallTimer = 0f;
            hitFlash = 0f;
            acid = 0f;
            x = targetX = roadX;
            xVel = 0f;
        }

        public void Reset()
        {
            x = 0f;
            targetX = 0f;
            xVel = 0f;
            speed = 0f;
            health = 3;
            stallTimer = 0f;
            hitFlash = 0f;
            dragging = false;
            scraping = false;
            trapped = false;
            shield = false;
            falling = false;
            fallY = fallVelocity = fallTilt = 0f;
            body.gameObject.SetActive(true);
            acid = 0f;
            invulnerable = 0f;
            branch = 0;
            boostOffset = 0f;
            boostVel = 0f;
            lastDrift = 0f;
            screenOffset = 0f;
            Place(0f, 0f);
        }

        /// <summary>Applies curve drift and the road's limits, then eases the car toward its steering target.</summary>
        public void Steer(float dt, float slope, float driftFactor, float min, float max)
        {
            lastDrift = -slope * speed * driftFactor * dt;
            if (!dragging) targetX += lastDrift;

            scraping = speed > 0.5f && (targetX < min - 0.01f || targetX > max + 0.01f || x < min - 0.01f || x > max + 0.01f);
            targetX = Mathf.Clamp(targetX, min, max);
            x = Mathf.SmoothDamp(x, targetX, ref xVel, 0.16f, 30f, Mathf.Max(dt, 1e-4f));
            x = Mathf.Clamp(x, min, max);
        }

        public void Tick(float dt, float targetSpeed, float time, bool boosting, float slope)
        {
            float accel = targetSpeed > speed ? 5f : 12f;
            speed = Mathf.MoveTowards(speed, targetSpeed, accel * dt);
            boostOffset = Mathf.SmoothDamp(boostOffset, boosting ? BoostForward : 0f, ref boostVel, 0.7f, 30f, Mathf.Max(dt, 1e-4f));
            if (stallTimer > 0f) stallTimer -= dt;
            if (hitFlash > 0f) hitFlash -= dt;
            if (shieldTime > 0f) shieldTime = Mathf.Max(0f, shieldTime - dt);
            // The bubble flickers through its last two seconds.
            bool showOrb = shield && (shieldTime > 2f || Mathf.Repeat(time * 6f, 1f) > 0.3f);
            if (!falling && shieldOrb.activeSelf != showOrb) shieldOrb.SetActive(showOrb);
            if (acid > 0f) acid -= dt;
            if (invulnerable > 0f) invulnerable -= dt;
            bool showGoo = acid > 0f && !falling;
            if (goo.gameObject.activeSelf != showGoo) goo.gameObject.SetActive(showGoo);
            if (showGoo)
            {
                Color c = Palette.Acid; c.a = Mathf.Clamp01(acid / 0.4f) * (0.75f + 0.25f * Mathf.Sin(time * 9f));
                gooBlock.SetColor(TintId, c);
                goo.SetPropertyBlock(gooBlock);
            }
            // Respawn grace: the body blinks until it's over.
            bodyRenderer.enabled = invulnerable <= 0f || Mathf.Repeat(time * 8f, 1f) > 0.35f;
            Place(time, slope);
        }

        /// <summary>Drops the jeep into the abyss, tumbling, after it leaves a cliff road.</summary>
        public void UpdateFall(float dt)
        {
            if (!falling) return;
            fallVelocity += 24f * dt;
            fallY -= fallVelocity * dt;
            fallTilt += 150f * dt;
            if (fallY < -10f && body.gameObject.activeSelf)
            {
                body.gameObject.SetActive(false);
                shieldOrb.SetActive(false);
            }
        }

        void Place(float time, float slope)
        {
            root.localPosition = new Vector3(ScreenX, fallY, Z);
            if (falling)
            {
                body.localRotation = Quaternion.Euler(fallTilt, 0f, fallTilt * 0.6f * Mathf.Sign(x == 0f ? 1f : x));
                return;
            }
            float bob = Mathf.Sin(time * 17f) * 0.03f * Mathf.Clamp01(speed / 4f);
            float shake = hitFlash > 0f ? Mathf.Sin(time * 70f) * 0.08f * hitFlash : 0f;
            if (scraping) shake += Mathf.Sin(time * 55f) * 0.04f;
            body.localPosition = new Vector3(shake, bob, 0f);
            float yaw = Mathf.Atan(slope) * Mathf.Rad2Deg * 0.7f + Mathf.Clamp(xVel * 3.5f, -20f, 20f);
            body.localRotation = Quaternion.Euler(0f, yaw, Mathf.Clamp(-xVel * 1.5f, -6f, 6f));
        }

        /// <summary>
        /// Lateral overlap test against something at road-relative position hx with half width hw.
        /// Road-relative, because on a curve the car and an object just ahead sit on different parts of the bend.
        /// </summary>
        public bool OverlapsRoadX(float hx, float hw) => Mathf.Abs(x - hx) < hw + HalfWidth * 0.8f;
    }
}
