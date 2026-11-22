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
        /// <summary>0 dry to 1 full storm: the jeep answers the steering more sluggishly and slides about.</summary>
        public float slip;
        /// <summary>Recovery trait: 0.15 or 0.35 extra acceleration while getting back up to speed after a slow-down.</summary>
        public float recoveryBoost;
        /// <summary>True from a crash or acid hit until the jeep is back up to the speed it's aiming for.</summary>
        public bool recovering;

        /// <summary>Degrees the body is turned from straight up the disk: the road's own heading plus the steering.</summary>
        public float Heading { get; private set; }
        /// <summary>Degrees the road at the jeep is turned from straight up the disk.</summary>
        public float RoadHeading { get; private set; }
        /// <summary>Degrees of extra turn from sideways steering, on top of the road's heading.</summary>
        public float SteerHeading { get; private set; }
        public bool Skidding => skidTime > 0f;

        float xVel, boostVel;
        float skidVel, skidTime, skidLength;
        float fallY, fallVelocity, fallTilt;
        readonly Transform root;
        readonly Transform body;
        readonly MeshFilter bodyFilter;
        readonly MeshRenderer bodyRenderer;
        readonly Material boxMaterial;
        readonly MaterialPropertyBlock bodyBlock = new MaterialPropertyBlock();
        readonly GameObject shieldOrb;
        readonly MeshRenderer goo;
        readonly MaterialPropertyBlock gooBlock = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");

        public Transform Root => root;
        /// <summary>The visible body. Gear such as the turret and shovel mounts here, so it follows the body's lean.</summary>
        public Transform Body => body;
        public int Model { get; private set; }
        public int ModelColor { get; private set; }
        public float RoofY => VehicleCatalog.RoofY(Model);
        public float FrontZ => VehicleCatalog.FrontZ(Model);
        /// <summary>True when the body shows the Tripo model rather than the box placeholder.</summary>
        public bool UsesModel { get; private set; }
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
            boxMaterial = mat;

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
            // The Tripo model when it's baked in, painted with a tint; otherwise the box placeholder in its colour.
            UsesModel = VehicleCatalog.TryModel(Model, out Mesh model, out Material modelMat);
            bodyFilter.sharedMesh = UsesModel ? model : VehicleCatalog.GetMesh(Model, color);
            bodyRenderer.sharedMaterial = UsesModel ? modelMat : boxMaterial;
            bodyBlock.Clear();
            if (UsesModel) bodyBlock.SetColor(VehicleCatalog.PaintProperty, VehicleCatalog.Paint(Model, color));
            if (UsesModel) bodyBlock.SetVector(VehicleCatalog.PaintKeyProperty, VehicleCatalog.PaintKey(Model));
            bodyRenderer.SetPropertyBlock(bodyBlock);
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
            skidTime = 0f;
        }

        /// <summary>Starts a sideways slide at <paramref name="velocity"/> units per second that dies away over the given time.</summary>
        public void Skid(float velocity, float seconds)
        {
            skidVel = velocity;
            skidTime = skidLength = Mathf.Max(seconds, 0.05f);
        }

        public void Reset()
        {
            recovering = false;
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
            slip = 0f;
            skidTime = 0f;
            Place(0f, 0f);
        }

        /// <summary>
        /// Applies curve drift, any skid and the road's limits, then eases the car toward its steering target.
        /// A skid shoves the body sideways straight away and carries the steering target with it, so a held
        /// beacon has to be pulled back to correct it.
        /// </summary>
        public void Steer(float dt, float slope, float driftFactor, float min, float max)
        {
            lastDrift = -slope * speed * driftFactor * dt;
            if (skidTime > 0f)
            {
                float k = skidTime / skidLength;
                float push = skidVel * k * dt;
                lastDrift += push;
                x += push;
                skidTime -= dt;
            }
            if (!dragging) targetX += lastDrift;

            scraping = speed > 0.5f && (targetX < min - 0.01f || targetX > max + 0.01f || x < min - 0.01f || x > max + 0.01f);
            targetX = Mathf.Clamp(targetX, min, max);
            // Wet roads: the jeep takes longer to answer the wheel.
            float response = Mathf.Lerp(0.16f, 0.3f, Mathf.Clamp01(slip));
            x = Mathf.SmoothDamp(x, targetX, ref xVel, response, 30f, Mathf.Max(dt, 1e-4f));
            x = Mathf.Clamp(x, min, max);
        }

        public void Tick(float dt, float targetSpeed, float time, bool boosting, float slope)
        {
            float accel = targetSpeed > speed ? 5f * (recovering ? 1f + recoveryBoost : 1f) : 12f;
            if (recovering && speed >= targetSpeed - 0.05f && targetSpeed > 0.5f) recovering = false;
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
            // Point straight along the road, so the body lines up with the road-relative collision box on
            // curves too, then turn a little more into a sideways move (and flick the tail out in a skid).
            RoadHeading = Mathf.Atan(slope) * Mathf.Rad2Deg;
            float lateral = xVel + (skidTime > 0f ? skidVel * (skidTime / skidLength) * 0.6f : 0f);
            SteerHeading = Mathf.Clamp(Mathf.Atan2(lateral, Mathf.Max(speed, 3f)) * Mathf.Rad2Deg, -22f, 22f);
            Heading = RoadHeading + SteerHeading;
            body.localRotation = Quaternion.Euler(0f, Heading, Mathf.Clamp(-xVel * 1.5f, -6f, 6f));
        }

        /// <summary>
        /// Lateral overlap test against something at road-relative position hx with half width hw.
        /// Road-relative, because on a curve the car and an object just ahead sit on different parts of the bend.
        /// </summary>
        public bool OverlapsRoadX(float hx, float hw) => Mathf.Abs(x - hx) < hw + HalfWidth * 0.8f;
    }
}
