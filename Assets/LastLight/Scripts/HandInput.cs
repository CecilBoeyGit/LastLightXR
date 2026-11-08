using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace LastLight
{
    /// <summary>One frame of hand data in world space (metres).</summary>
    public struct HandFrame
    {
        public bool tracked;
        public Vector3 palm;
        public Vector3 palmNormal;     // points out of the palm
        public Vector3 pinchPoint;
        public float pinchDistance;
        public bool pinching;
        public bool open;
        public Vector3 velocity;       // palm velocity, smoothed
    }

    /// <summary>
    /// Reads hand joints through the Unity XR Hands API, which the PICO XR loader backs with its own
    /// hand subsystem. Joint poses arrive in tracking space and are moved into world space using the
    /// XR rig's camera-offset transform.
    /// </summary>
    public class HandInput
    {
        const float PinchStart = 0.018f;
        const float PinchEnd = 0.035f;
        const float OpenThreshold = 0.07f;
        const float LostGrace = 0.25f;

        readonly Transform trackingSpace;
        XRHandSubsystem subsystem;
        static readonly List<XRHandSubsystem> Found = new List<XRHandSubsystem>();

        HandFrame last;
        float lostTimer;
        Vector3 prevPalm;
        bool hasPrev;

        public bool Available => subsystem != null && subsystem.running;
        public float SecondsLost => lostTimer;

        public HandInput(Transform trackingSpace)
        {
            this.trackingSpace = trackingSpace;
        }

        bool EnsureSubsystem()
        {
            if (subsystem != null && subsystem.running) return true;
            Found.Clear();
            SubsystemManager.GetSubsystems(Found);
            foreach (var s in Found)
            {
                if (!s.running) s.Start();
                if (s.running) { subsystem = s; return true; }
            }
            return false;
        }

        /// <summary>
        /// Samples one hand. Brief dropouts (under a quarter second) return the last stable frame
        /// marked as tracked, so a flicker never cancels a drag or a brake.
        /// </summary>
        public HandFrame Sample(Handedness handedness, float dt)
        {
            if (!EnsureSubsystem()) { lostTimer += dt; last.tracked = false; return last; }
            XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked || !TryJoint(hand, XRHandJointID.Palm, out Pose palm))
            {
                lostTimer += dt;
                var held = last;
                held.tracked = lostTimer < LostGrace && last.tracked;
                held.velocity = Vector3.zero;
                if (!held.tracked) hasPrev = false;
                return held;
            }
            lostTimer = 0f;

            var f = new HandFrame { tracked = true };
            f.palm = palm.position;
            f.palmNormal = palm.rotation * Vector3.down;

            TryJoint(hand, XRHandJointID.ThumbTip, out Pose thumb);
            TryJoint(hand, XRHandJointID.IndexTip, out Pose index);
            f.pinchPoint = (thumb.position + index.position) * 0.5f;
            f.pinchDistance = Vector3.Distance(thumb.position, index.position);
            f.pinching = last.pinching ? f.pinchDistance < PinchEnd : f.pinchDistance < PinchStart;

            float spread = 0f;
            int n = 0;
            foreach (var id in new[] { XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip })
            {
                if (TryJoint(hand, id, out Pose tip)) { spread += Vector3.Distance(tip.position, f.palm); n++; }
            }
            f.open = !f.pinching && n > 0 && spread / n > OpenThreshold;

            Vector3 v = hasPrev && dt > 1e-4f ? (f.palm - prevPalm) / dt : Vector3.zero;
            f.velocity = Vector3.Lerp(last.velocity, v, 0.5f);
            prevPalm = f.palm;
            hasPrev = true;
            last = f;
            return f;
        }

        bool TryJoint(XRHand hand, XRHandJointID id, out Pose world)
        {
            world = default;
            if (!hand.GetJoint(id).TryGetPose(out Pose p)) return false;
            if (trackingSpace != null)
            {
                world.position = trackingSpace.TransformPoint(p.position);
                world.rotation = trackingSpace.rotation * p.rotation;
            }
            else world = p;
            return true;
        }
    }
}
