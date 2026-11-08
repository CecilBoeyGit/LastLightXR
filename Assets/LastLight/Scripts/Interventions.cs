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
    /// </summary>
    public class Interventions
    {
        const float GrabRadius = 0.07f;         // metres, generous on purpose
        const float SweepReach = 0.085f;        // metres, plus the rock's own radius
        const float SweepSpeed = 0.45f;         // m/s of palm motion
        const float KeyboardSteerRate = 9f;     // game units per second

        class HandState
        {
            public bool dragging;
            public bool wasPinching;
        }

        public bool Dragging => dragHand >= 0;
        public bool BeaconHovered { get; private set; }

        public System.Action OnGrab;
        public System.Action OnRelease;
        public System.Action<Hazard, float> OnSweep;

        readonly Diorama diorama;
        readonly MeshRenderer beacon, targetRing;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        readonly HandState[] hands = { new HandState(), new HandState() };

        int dragHand = -1;
        float dragAnchor;
        float time;

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
        }

        public void Reset()
        {
            foreach (var h in hands)
            {
                h.dragging = false;
                h.wasPinching = false;
            }
            dragHand = -1;
        }

        public void Tick(float dt, HandFrame left, HandFrame right, bool handsActive, Vehicle car, IList<Hazard> hazards, IntentFrame intents, bool running)
        {
            time += dt;
            Vector3 beaconWorld = diorama.ToWorld(new Vector3(car.ScreenX, 4.2f, car.Z));
            BeaconHovered = false;

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
                else if (intents.steer != 0f) car.targetX += intents.steer * KeyboardSteerRate * dt;
                if (intents.sweep) SweepNearest(car, hazards);
            }
            UpdateVisuals(car);
        }

        void TickHand(int index, HandState state, HandFrame hand, Vehicle car, Vector3 beaconWorld, IList<Hazard> hazards)
        {
            float pinchRel = diorama.ToLocal(hand.pinchPoint).x - car.screenOffset;
            bool pinchStarted = hand.pinching && !state.wasPinching;
            bool hovered = Vector3.Distance(hand.pinchPoint, beaconWorld) < GrabRadius;
            if (hovered) BeaconHovered = true;

            if (state.dragging)
            {
                // The jeep follows the hand directly, and curves push against a steady hand too.
                dragAnchor -= car.lastDrift;
                car.targetX = pinchRel - dragAnchor;
                if (!hand.pinching)
                {
                    state.dragging = false;
                    dragHand = -1;
                    OnRelease?.Invoke();
                }
            }
            else if (pinchStarted && hovered && dragHand < 0)
            {
                state.dragging = true;
                dragHand = index;
                dragAnchor = pinchRel - car.targetX;
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

        void UpdateVisuals(Vehicle car)
        {
            bool held = dragHand >= 0;
            float pulse = 0.75f + 0.25f * Mathf.Sin(time * 4f);
            float beaconX = held ? car.TargetScreenX : car.ScreenX;
            beacon.transform.localPosition = new Vector3(beaconX, 4.2f + Mathf.Sin(time * 2f) * 0.15f, car.Z);
            float b = held ? 1.6f : BeaconHovered ? 1.3f : pulse;
            mpb.SetColor(TintId, new Color(b, b, b, 1f));
            beacon.SetPropertyBlock(mpb);
            if (beacon.gameObject.activeSelf == car.falling) beacon.gameObject.SetActive(!car.falling);

            targetRing.gameObject.SetActive(held);
            if (held)
            {
                targetRing.transform.localPosition = new Vector3(car.TargetScreenX, 0.12f, car.Z + 2.2f);
                Color rc = Palette.Route; rc.a = 0.9f;
                mpb.SetColor(TintId, rc);
                targetRing.SetPropertyBlock(mpb);
            }
        }
    }
}
