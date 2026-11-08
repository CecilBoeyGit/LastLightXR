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
        public bool shield;
        public bool falling;            // went over a cliff edge
        public int branch;              // -1 or +1 while committed to a side of a split road
        public float boostOffset;       // how far forward along the disk the car currently sits
        public float lastDrift;         // sideways push from the curve this frame
        public float screenOffset;      // where the road centre at the car sits on the disk

        float xVel, boostVel;
        float fallY, fallVelocity, fallTilt;
        readonly Transform root;
        readonly Transform body;
        readonly GameObject shieldOrb;

        public Transform Root => root;
        public bool Stalled => stallTimer > 0f || trapped;
        public float Z => Diorama.CarZ + boostOffset;
        public float ScreenX => x + screenOffset;
        public float TargetScreenX => targetX + screenOffset;

        public Vehicle(Transform parent, Material mat, Material glowMat)
        {
            var go = new GameObject("Vehicle");
            go.transform.SetParent(parent, false);
            root = go.transform;

            var mb = new MeshBuilder();
            // Body, cabin, roof rack, wheels, lamps. Facing +Z.
            mb.Box(new Vector3(0f, 0.55f, 0f), new Vector3(1.4f, 0.6f, 2.6f), Palette.Ochre);
            mb.Box(new Vector3(0f, 1.05f, -0.25f), new Vector3(1.25f, 0.5f, 1.4f), Palette.Ochre * 0.95f);
            mb.Box(new Vector3(0f, 1.08f, 0.47f), new Vector3(1.15f, 0.4f, 0.06f), Palette.Cabin);
            mb.Box(new Vector3(0f, 1.36f, -0.25f), new Vector3(1.1f, 0.08f, 1.2f), Palette.Cabin);
            for (int i = 0; i < 4; i++)
            {
                float wx = (i % 2 == 0 ? -1f : 1f) * 0.72f;
                float wz = (i < 2 ? -1f : 1f) * 0.85f;
                mb.Box(new Vector3(wx, 0.32f, wz), new Vector3(0.28f, 0.62f, 0.62f), Palette.Tyre);
            }
            mb.Box(new Vector3(0f, 0.55f, -1.36f), new Vector3(0.5f, 0.5f, 0.12f), Palette.Tyre); // spare tyre
            mb.Box(new Vector3(-0.45f, 0.62f, 1.31f), new Vector3(0.28f, 0.18f, 0.04f), Palette.Lamp * 3f);
            mb.Box(new Vector3(0.45f, 0.62f, 1.31f), new Vector3(0.28f, 0.18f, 0.04f), Palette.Lamp * 3f);
            mb.Box(new Vector3(-0.55f, 0.6f, -1.31f), new Vector3(0.18f, 0.14f, 0.04f), Palette.Tail * 2f);
            mb.Box(new Vector3(0.55f, 0.6f, -1.31f), new Vector3(0.18f, 0.14f, 0.04f), Palette.Tail * 2f);
            body = MeshBuilder.Spawn("Body", root, mb.ToMesh("LL_Jeep"), mat).transform;

            mb.Clear();
            mb.Orb(new Vector3(0f, 0.8f, 0f), 2.1f, new Color(0.25f, 0.6f, 1f, 0.28f));
            shieldOrb = MeshBuilder.Spawn("Shield", root, mb.ToMesh("LL_ShieldOrb"), glowMat).gameObject;
            shieldOrb.SetActive(false);
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
            if (!falling && shieldOrb.activeSelf != shield) shieldOrb.SetActive(shield);
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
