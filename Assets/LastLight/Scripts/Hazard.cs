using UnityEngine;

namespace LastLight
{
    public enum HazardKind { FallenTree, Boulder, DeadEnd }
    public enum BoulderState { Warning, Falling, Landed, Swept, Smashed }

    /// <summary>
    /// A road hazard. Fallen trees block part of the road from the moment their chunk is generated.
    /// Boulders fall from a cliff toward the car, can be swept away, and block if left alone.
    /// Dead-end walls close a whole branch of a split road.
    /// Positions are chunk-local game units; the chunk scrolls, so hazards scroll with it.
    /// </summary>
    public class Hazard : MonoBehaviour
    {
        public const float WarningTime = 0.9f;
        public const float FallTime = 0.85f;

        public HazardKind kind;
        public Chunk chunk;
        public float halfWidth = 1.3f;
        public float escapeX;               // road-relative centre of a dead-end wall's narrow gap
        public float radius = 1f;
        public bool alive = true;

        // Boulder motion
        public BoulderState state = BoulderState.Landed;
        public float timer;
        public Vector3 fromLocal, toLocal;   // chunk-local fall arc
        public Vector3 velocity, spinAxis;
        public float spinSpeed;
        public Transform pebbles;

        // Scouting memory
        public bool discovered;
        public float memory;                // seconds of outline left
        public Renderer marker;

        // Collision bookkeeping
        public bool hitCar;
        public float stallTime;

        public float HalfWidth => kind == HazardKind.Boulder ? radius * 0.9f : halfWidth;

        public bool IsObstacle =>
            alive && (kind != HazardKind.Boulder || state == BoulderState.Landed || (state == BoulderState.Falling && timer > FallTime * 0.75f));

        public bool Sweepable => alive && kind == HazardKind.Boulder && (state == BoulderState.Falling || state == BoulderState.Landed);

        /// <summary>Hazard position in diorama-local units.</summary>
        public Vector3 DioramaLocal => chunk.root.localPosition + transform.localPosition;

        public void StartFall(Vector3 from, Vector3 to)
        {
            state = BoulderState.Warning;
            timer = 0f;
            fromLocal = from;
            toLocal = to;
            transform.localPosition = from;
        }

        public void Sweep(float dirX)
        {
            if (!Sweepable) return;
            state = BoulderState.Swept;
            timer = 0f;
            velocity = new Vector3(Mathf.Sign(dirX == 0f ? 1f : dirX) * 11f, 5.5f, 1.5f);
            spinAxis = new Vector3(0.3f, 0.2f, -Mathf.Sign(dirX)).normalized;
            spinSpeed = 720f;
        }

        public void Smash()
        {
            alive = false;
            state = BoulderState.Smashed;
            gameObject.SetActive(false);
        }

        /// <summary>Advances fall and sweep motion. Returns true when the boulder just landed.</summary>
        public bool Tick(float dt)
        {
            if (!alive || kind != HazardKind.Boulder) return false;
            timer += dt;
            switch (state)
            {
                case BoulderState.Warning:
                    transform.localPosition = fromLocal;
                    if (pebbles != null)
                    {
                        pebbles.gameObject.SetActive(true);
                        float t = (timer * 2.3f) % 1f;
                        pebbles.localPosition = Vector3.Lerp(Vector3.zero, (toLocal - fromLocal) * 0.6f + Vector3.down * 2f, t);
                    }
                    if (timer >= WarningTime) { state = BoulderState.Falling; timer = 0f; }
                    return false;
                case BoulderState.Falling:
                {
                    if (pebbles != null) pebbles.gameObject.SetActive(false);
                    float t = Mathf.Clamp01(timer / FallTime);
                    Vector3 p = Vector3.Lerp(fromLocal, toLocal, t);
                    p.y = Mathf.Lerp(fromLocal.y, toLocal.y, t * t) + Mathf.Sin(t * Mathf.PI) * 1.5f;
                    transform.localPosition = p;
                    transform.localRotation = Quaternion.AngleAxis(t * 260f, Vector3.right);
                    if (t >= 1f) { state = BoulderState.Landed; timer = 0f; return true; }
                    return false;
                }
                case BoulderState.Swept:
                    velocity += Vector3.down * 22f * dt;
                    transform.localPosition += velocity * dt;
                    transform.localRotation = Quaternion.AngleAxis(spinSpeed * dt, spinAxis) * transform.localRotation;
                    if (timer > 1.6f) { alive = false; gameObject.SetActive(false); }
                    return false;
            }
            return false;
        }
    }
}
