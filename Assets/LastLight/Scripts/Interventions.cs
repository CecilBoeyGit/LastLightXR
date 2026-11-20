using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>Requests from the keyboard, controllers or the autopilot.</summary>
    public struct IntentFrame
    {
        public float steer;             // -1 to 1, continuous
        public float? setTarget;        // road-relative steering target (autopilot)
        public bool sweep;
    }

    /// <summary>
    /// Both hands can act on the jeep. Pinch the beacon and drag it anywhere across the road to steer,
    /// or sweep an open hand through a falling rock. Only one hand holds the beacon at a time, so the
    /// other stays free to sweep.
    /// While held, the beacon sits in the pinch itself and a faint curved tether runs down to the jeep.
    /// If the jeep has run out of reach, stare at the beacon for half a second to arm it, then pinch
    /// anywhere: it flies to the pinch and follows it from there.
    /// </summary>
    public class Interventions
    {
        const float GrabRadius = 0.07f;         // metres, generous on purpose
        const float SweepReach = 0.085f;        // metres, plus the rock's own radius
        const float SweepSpeed = 0.45f;         // m/s of palm motion
        const float KeyboardSteerRate = 9f;     // game units per second
        public const float GazeArmSeconds = 0.5f;
        const float GazeRadius = 1.7f;          // game units around the beacon that count as looking at it
        const float ArmedGrace = 1.2f;          // seconds an armed beacon stays armed after the eyes move on
        const float FlySeconds = 0.18f;         // flight from the jeep into a gaze-grabbing pinch
        const float RestHeight = 4.2f;
        const int TetherSegments = 18;

        class HandState
        {
            public bool dragging;
            public bool wasPinching;
        }

        public bool Dragging => dragHand >= 0;
        public bool BeaconHovered { get; private set; }
        /// <summary>Seconds the eyes have rested on the beacon, toward arming it.</summary>
        public float GazeDwell { get; private set; }
        /// <summary>True once a stare has armed the beacon, so the next pinch anywhere grabs it.</summary>
        public bool GazeArmed { get; private set; }
        /// <summary>True while a gaze-grabbed beacon is still flying into the pinch.</summary>
        public bool Flying => flyT < 1f && dragHand >= 0;
        /// <summary>The beacon's current position, disk-local.</summary>
        public Vector3 BeaconLocal => beacon.transform.localPosition;
        public bool TetherVisible => tether.gameObject.activeSelf;
        /// <summary>How much a hand movement is multiplied into steering. Storms raise it.</summary>
        public float SteerGain = 1f;

        public System.Action OnGrab;
        public System.Action OnRelease;
        public System.Action OnGazeArmed;
        /// <summary>Set while something else near the jeep (the repair wrench) owns the gaze, so a stare there can't arm the beacon.</summary>
        public bool GazeArmSuppressed;
        public System.Action<Hazard, float> OnSweep;

        readonly Diorama diorama;
        readonly MeshRenderer beacon, targetRing, tether, gazeRing;
        readonly Mesh tetherMesh, gazeRingMesh;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        readonly HandState[] hands = { new HandState(), new HandState() };

        int dragHand = -1;
        float dragBase, dragPinch0, dragGain = 1f;
        float time;
        float armedLeft;
        float flyT = 1f;
        Vector3 flyFrom, beaconPos, beaconVel, pinchLocal;
        bool beaconPlaced;
        bool gazeGrab;

        public Interventions(Diorama diorama, Material glowMat)
        {
            this.diorama = diorama;
            var mb = new MeshBuilder();
            mb.Orb(Vector3.zero, 0.55f, new Color(1f, 0.72f, 0.3f, 1f));
            mb.Ring(Vector3.zero, 0.85f, 1.05f, 24, new Color(1f, 0.62f, 0.2f, 0.8f));
            beacon = MeshBuilder.Spawn("Beacon", diorama.transform, mb.ToMesh("LL_Beacon"), glowMat);

            mb.Clear();
            mb.Ring(Vector3.zero, 0.75f, 0.95f, 24, Color.white);
            targetRing = MeshBuilder.Spawn("Steering Target", diorama.transform, mb.ToMesh("LL_SteerRing"), glowMat);
            targetRing.gameObject.SetActive(false);

            tetherMesh = new Mesh { name = "LL_BeaconTether" };
            tether = MeshBuilder.Spawn("Beacon Tether", diorama.transform, tetherMesh, glowMat);
            tether.gameObject.SetActive(false);
            gazeRingMesh = new Mesh { name = "LL_BeaconGazeRing" };
            gazeRing = MeshBuilder.Spawn("Beacon Gaze Ring", beacon.transform, gazeRingMesh, glowMat);
            gazeRing.gameObject.SetActive(false);
        }

        public void Reset()
        {
            foreach (var h in hands)
            {
                h.dragging = false;
                h.wasPinching = false;
            }
            dragHand = -1;
            GazeDwell = 0f;
            GazeArmed = false;
            armedLeft = 0f;
            flyT = 1f;
            beaconPlaced = false;
            gazeGrab = false;
        }

        /// <param name="gazeLocal">The gaze ray in disk-local units, if there is one, for staring at the beacon.</param>
        public void Tick(float dt, HandFrame left, HandFrame right, bool handsActive, Vehicle car, IList<Hazard> hazards, IntentFrame intents, bool running, Ray? gazeLocal = null)
        {
            time += dt;
            BeaconHovered = false;
            UpdateGazeArm(dt, gazeLocal, running && !car.falling && dragHand < 0 && !GazeArmSuppressed);
            Vector3 beaconWorld = diorama.ToWorld(beacon.transform.localPosition);

            for (int i = 0; i < 2; i++)
            {
                HandFrame frame = i == 0 ? left : right;
                HandState state = hands[i];
                if (handsActive && frame.tracked && running) TickHand(i, state, frame, car, beaconWorld, hazards);
                else DropHand(i, state);
            }

            car.dragging = dragHand >= 0;
            if (running && dragHand < 0)
            {
                if (intents.setTarget.HasValue) car.targetX = intents.setTarget.Value;
                else if (intents.steer != 0f) car.targetX += intents.steer * KeyboardSteerRate * SteerGain * dt;
                if (intents.sweep) SweepNearest(car, hazards);
            }
            UpdateVisuals(dt, car);
        }

        /// <summary>Half a second of looking at the beacon arms it; it stays armed for a moment after the eyes move on.</summary>
        void UpdateGazeArm(float dt, Ray? gaze, bool allowed)
        {
            bool looking = false;
            if (allowed && gaze.HasValue)
            {
                Vector3 to = beacon.transform.localPosition - gaze.Value.origin;
                looking = Vector3.Dot(to, gaze.Value.direction) > 0f && Vector3.Cross(gaze.Value.direction.normalized, to).magnitude < GazeRadius;
            }
            if (!allowed) { GazeDwell = 0f; GazeArmed = false; armedLeft = 0f; return; }
            if (looking)
            {
                GazeDwell = Mathf.Min(GazeArmSeconds, GazeDwell + dt);
                if (GazeDwell >= GazeArmSeconds)
                {
                    if (!GazeArmed) OnGazeArmed?.Invoke();
                    GazeArmed = true;
                    armedLeft = ArmedGrace;
                }
            }
            else
            {
                GazeDwell = Mathf.Max(0f, GazeDwell - dt * 1.5f);
                if (GazeArmed && (armedLeft -= dt) <= 0f) GazeArmed = false;
            }
        }

        void TickHand(int index, HandState state, HandFrame hand, Vehicle car, Vector3 beaconWorld, IList<Hazard> hazards)
        {
            Vector3 local = diorama.ToLocal(hand.pinchPoint);
            float pinchRel = local.x - car.screenOffset;
            bool pinchStarted = hand.pinching && !state.wasPinching;
            bool hovered = Vector3.Distance(hand.pinchPoint, beaconWorld) < GrabRadius;
            if (hovered) BeaconHovered = true;

            if (state.dragging)
            {
                // The jeep follows the hand, and curves (or a skid) push against a steady hand too.
                // A storm multiplies hand movement into steering; a change of gain re-bases so nothing jumps.
                dragBase += car.lastDrift;
                if (!Mathf.Approximately(dragGain, SteerGain))
                {
                    dragBase = car.targetX - (pinchRel - dragPinch0) * SteerGain;
                    dragGain = SteerGain;
                }
                car.targetX = dragBase + (pinchRel - dragPinch0) * dragGain;
                pinchLocal = local;
                if (!hand.pinching)
                {
                    state.dragging = false;
                    dragHand = -1;
                    OnRelease?.Invoke();
                }
            }
            else if (pinchStarted && dragHand < 0 && (hovered || GazeArmed))
            {
                state.dragging = true;
                dragHand = index;
                dragPinch0 = pinchRel;
                dragBase = car.targetX;
                dragGain = SteerGain;
                pinchLocal = local;
                // Grabbed from a distance by gaze: the beacon flies into the pinch first.
                gazeGrab = !hovered;
                flyT = gazeGrab ? 0f : 1f;
                flyFrom = beacon.transform.localPosition;
                GazeArmed = false;
                GazeDwell = 0f;
                OnGrab?.Invoke();
            }
            else TrySweep(hand, car, hazards);

            state.wasPinching = hand.pinching;
        }

        void DropHand(int index, HandState state)
        {
            // Hand gone: the jeep keeps its current target.
            if (dragHand == index) { dragHand = -1; OnRelease?.Invoke(); }
            state.dragging = false;
            state.wasPinching = false;
        }

        void TrySweep(HandFrame hand, Vehicle car, IList<Hazard> hazards)
        {
            if (!hand.open) return;
            Vector3 v = hand.velocity; v.y = 0f;
            if (v.magnitude < SweepSpeed) return;
            foreach (var h in hazards)
            {
                if (h == null || !h.Sweepable) continue;
                Vector3 hw = diorama.ToWorld(h.DioramaLocal);
                if (Vector3.Distance(hand.palm, hw) > SweepReach + h.radius * diorama.Scale) continue;
                Vector3 lv = diorama.DirToLocal(hand.velocity);
                float dir = Mathf.Abs(lv.x) > 0.2f * lv.magnitude ? Mathf.Sign(lv.x) : DefaultSweepDir(h, car);
                OnSweep?.Invoke(h, dir);
                return;
            }
        }

        void SweepNearest(Vehicle car, IList<Hazard> hazards)
        {
            Hazard best = null;
            float bestZ = float.MaxValue;
            foreach (var h in hazards)
            {
                if (h == null || !h.Sweepable) continue;
                float z = h.DioramaLocal.z;
                if (z < car.Z - 2f || z > Diorama.Radius) continue;
                if (z < bestZ) { bestZ = z; best = h; }
            }
            if (best != null) OnSweep?.Invoke(best, DefaultSweepDir(best, car));
        }

        /// <summary>The forgiving default: push the rock away from the car.</summary>
        static float DefaultSweepDir(Hazard h, Vehicle car)
        {
            float rel = h.DioramaLocal.x - car.ScreenX;
            if (Mathf.Abs(rel) < 0.3f) return car.x <= 0f ? 1f : -1f;
            return Mathf.Sign(rel);
        }

        void UpdateVisuals(float dt, Vehicle car)
        {
            bool held = dragHand >= 0;
            float pulse = 0.75f + 0.25f * Mathf.Sin(time * 4f);
            Vector3 rest = new Vector3(car.ScreenX, RestHeight + Mathf.Sin(time * 2f) * 0.15f, car.Z);
            float step = Mathf.Max(dt, 1e-4f);
            if (held)
            {
                // In the hand: right at the pinch, after a quick flight if it was grabbed by gaze.
                if (flyT < 1f)
                {
                    flyT = Mathf.Min(1f, flyT + step / FlySeconds);
                    float k = Mathf.SmoothStep(0f, 1f, flyT);
                    Vector3 arc = Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                    beaconPos = Vector3.Lerp(flyFrom, pinchLocal, k) + arc;
                }
                else beaconPos = pinchLocal;
                beaconVel = Vector3.zero;
            }
            else if (!beaconPlaced) beaconPos = rest;
            else beaconPos = Vector3.SmoothDamp(beaconPos, rest, ref beaconVel, 0.12f, 400f, step);   // let go: glide back over the jeep
            beaconPlaced = true;
            beacon.transform.localPosition = beaconPos;

            float b = held ? 1.6f : GazeArmed ? 1.45f + 0.25f * Mathf.Sin(time * 14f) : BeaconHovered ? 1.3f : pulse;
            mpb.SetColor(TintId, new Color(b, b, b, 1f));
            beacon.SetPropertyBlock(mpb);
            if (beacon.gameObject.activeSelf == car.falling) beacon.gameObject.SetActive(!car.falling);

            // The gaze ring fills round the beacon while you stare, and stays full while armed.
            float fill = GazeArmed ? 1f : GazeDwell / GazeArmSeconds;
            bool showRing = !held && !car.falling && fill > 0.02f;
            if (gazeRing.gameObject.activeSelf != showRing) gazeRing.gameObject.SetActive(showRing);
            if (showRing)
            {
                mb.Clear();
                mb.Arc(Vector3.zero, 1.25f, 1.5f, fill, 32, Color.white);
                mb.ToMesh(gazeRingMesh.name, gazeRingMesh);
                Color gc = Palette.UIAmber * (GazeArmed ? 1.8f : 1.2f); gc.a = 1f;
                mpb.SetColor(TintId, gc);
                gazeRing.SetPropertyBlock(mpb);
            }

            targetRing.gameObject.SetActive(held);
            if (held)
            {
                targetRing.transform.localPosition = new Vector3(car.TargetScreenX, 0.12f, car.Z + 2.2f);
                Color rc = Palette.Route; rc.a = 0.9f;
                mpb.SetColor(TintId, rc);
                targetRing.SetPropertyBlock(mpb);
            }

            if (tether.gameObject.activeSelf != held) tether.gameObject.SetActive(held);
            if (held) BuildTether(beaconPos, new Vector3(car.ScreenX, car.RoofY + 0.15f, car.Z));
        }

        /// <summary>
        /// A faint curve from the beacon down to the jeep's roof: a quadratic Bezier that bows upward, drawn as
        /// two crossed ribbons so it reads from any angle, fading toward the jeep.
        /// </summary>
        void BuildTether(Vector3 from, Vector3 to)
        {
            mb.Clear();
            Vector3 mid = (from + to) * 0.5f + Vector3.up * (1.2f + 0.15f * Vector3.Distance(from, to));
            Vector3 prev = from;
            for (int i = 1; i <= TetherSegments; i++)
            {
                float t = i / (float)TetherSegments;
                Vector3 p = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * mid + t * t * to;
                Vector3 dir = p - prev;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    float a0 = Mathf.Lerp(0.55f, 0.18f, (i - 1) / (float)TetherSegments), a1 = Mathf.Lerp(0.55f, 0.18f, t);
                    Color c0 = new Color(1f, 0.72f, 0.35f, a0), c1 = new Color(1f, 0.72f, 0.35f, a1);
                    Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                    side = side.normalized * 0.07f;
                    Vector3 up = Vector3.Cross(side, dir.normalized).normalized * 0.07f;
                    Ribbon(prev, p, side, c0, c1);
                    Ribbon(prev, p, up, c0, c1);
                }
                prev = p;
            }
            mb.ToMesh(tetherMesh.name, tetherMesh);
        }

        void Ribbon(Vector3 a, Vector3 b, Vector3 side, Color ca, Color cb)
        {
            // Two triangles with per-end colours, so the fade runs along the tether.
            mb.Tri(a - side, b - side, b + side, ca);
            mb.Tri(a - side, b + side, a + side, ca);
        }
    }
}
