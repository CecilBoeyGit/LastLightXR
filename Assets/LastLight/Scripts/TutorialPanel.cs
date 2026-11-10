using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// Five-second animated "How to play" card shown before every run.
    /// Left: a hand pinches the jeep's beacon and slides it left and right, and the jeep follows.
    /// Right: an eye sweeps a scout light across a dark fork, revealing trees and a fallen log.
    /// Show(t) poses everything for time t, so the animation is deterministic and testable.
    /// </summary>
    public sealed class TutorialPanel
    {
        public const float Duration = 5f;
        const float Px = StartMenu.Width / 1600f; // the panel art is 1600 x 900 pixels
        const float PinchStart = 0.6f, PinchEnd = 4.4f;
        const float SteerAmplitude = 160f;        // pixels either side of the road centre
        const float RoadCenter = 360f;
        const float SpotRadius = 120f;

        public readonly Transform root;
        readonly UISprite panel, hand, beacon, tether, jeep, arrowLeft, arrowRight, eye, beam, spot, log, bar;
        readonly UISprite[] dashes = new UISprite[4];
        readonly UISprite[] trees;
        readonly Texture2D handOpen, handPinch;
        readonly UISprite[] all;

        static readonly Vector2[] TreeSpots =
        {
            new Vector2(890f, 300f), new Vector2(930f, 600f), new Vector2(1075f, 640f), new Vector2(1180f, 320f),
            new Vector2(1285f, 640f), new Vector2(1440f, 290f), new Vector2(1470f, 570f), new Vector2(960f, 430f)
        };
        static readonly Vector3[] SpotKeys = // time, x, y in panel pixels
        {
            new Vector3(0f, 1180f, 640f), new Vector3(0.5f, 1180f, 640f), new Vector3(1.5f, 1080f, 410f),
            new Vector3(2.4f, 1080f, 410f), new Vector3(3.5f, 1335f, 300f), new Vector3(4.2f, 1335f, 300f),
            new Vector3(5f, 1180f, 470f)
        };
        static readonly Vector2 LogSpot = new Vector2(1075f, 405f); // across the left branch of the fork

        public TutorialPanel(Transform parent, Material alphaMat, Material addMat)
        {
            root = new GameObject("Tutorial").transform;
            root.SetParent(parent, false);
            Texture2D white = UISprite.Load("white");
            Texture2D spotTex = UISprite.Load("spot");
            Color ivory = Palette.UIIvory, amber = Palette.UIAmber;

            panel = new UISprite("Panel", root, UISprite.Load("tut_panel"), Size(1600f, 900f), alphaMat, 0);

            for (int i = 0; i < dashes.Length; i++)
                dashes[i] = new UISprite("Road Dash", root, white, Size(12f, 56f), alphaMat, 1) { Color = new Color(ivory.r, ivory.g, ivory.b, 0.5f) };
            jeep = new UISprite("Jeep", root, UISprite.Load("jeep"), Size(150f, 150f), alphaMat, 4);
            Texture2D arrow = UISprite.Load("arrow");
            arrowLeft = new UISprite("Arrow Left", root, arrow, Size(70f, 70f), alphaMat, 4);
            arrowLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            arrowRight = new UISprite("Arrow Right", root, arrow, Size(70f, 70f), alphaMat, 4);
            tether = new UISprite("Tether", root, white, Size(8f, 100f), addMat, 3) { Color = new Color(amber.r, amber.g, amber.b, 0.55f) };
            beacon = new UISprite("Beacon", root, spotTex, Size(120f, 120f), addMat, 6) { Color = new Color(1f, 0.7f, 0.3f, 0.9f) };
            handOpen = UISprite.Load("hand_open");
            handPinch = UISprite.Load("hand_pinch");
            hand = new UISprite("Hand", root, handOpen, Size(300f, 300f), alphaMat, 5);

            trees = new UISprite[TreeSpots.Length];
            for (int i = 0; i < trees.Length; i++)
                trees[i] = new UISprite("Ghost Tree", root, UISprite.Load("tree"), Size(110f, 110f), alphaMat, 2) { Position = P(TreeSpots[i]) };
            log = new UISprite("Fallen Log", root, UISprite.Load("log"), Size(210f, 105f), alphaMat, 2) { Position = P(LogSpot) };
            beam = new UISprite("Gaze Beam", root, UISprite.Load("beam"), Size(200f, 300f), addMat, 3) { Color = new Color(1f, 0.78f, 0.4f, 0.85f) };
            spot = new UISprite("Scout Light", root, spotTex, Size(SpotRadius * 2.6f, SpotRadius * 2.6f), addMat, 3) { Color = new Color(1f, 0.68f, 0.28f, 0.6f) };
            eye = new UISprite("Eye", root, UISprite.Load("eye"), Size(230f, 115f), alphaMat, 5);

            bar = new UISprite("Countdown", root, white, Size(10f, 14f), alphaMat, 2) { Color = amber };

            all = new[] { panel, hand, beacon, tether, jeep, arrowLeft, arrowRight, eye, beam, spot, log, bar, dashes[0], dashes[1], dashes[2], dashes[3] };
            System.Array.Resize(ref all, all.Length + trees.Length);
            trees.CopyTo(all, all.Length - trees.Length);
            Show(0f);
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set => root.gameObject.SetActive(value);
        }

        /// <summary>Hand pinch state at time t, for tests.</summary>
        public static bool PinchedAt(float t) => t >= PinchStart && t < PinchEnd;

        /// <summary>Jeep offset from the road centre at time t, in panel pixels.</summary>
        public static float JeepOffsetAt(float t) => SteerOffset(t - 0.15f);

        public void Show(float t)
        {
            float fade = Mathf.Min(Smooth(0f, 0.25f, t), 1f - Smooth(Duration - 0.25f, Duration, t));
            foreach (var s in all) s.Alpha = fade;

            // ----- Steering demo -----
            for (int i = 0; i < dashes.Length; i++)
            {
                float y = 160f + Mathf.Repeat(t * 320f + i * 150f, 600f);
                dashes[i].Position = P(RoadCenter, y);
                float edge = Mathf.Min(Smooth(160f, 220f, y), 1f - Smooth(680f, 740f, y));
                dashes[i].Alpha = fade * edge;
            }

            bool pinched = PinchedAt(t);
            float handOffset = SteerOffset(t);
            float handY = t < PinchStart ? Mathf.Lerp(350f, 430f, Smooth(0f, PinchStart, t))
                        : t >= PinchEnd ? Mathf.Lerp(430f, 360f, Smooth(PinchEnd, Duration, t)) : 430f;
            var pinchPoint = new Vector2(RoadCenter + handOffset, handY);
            hand.Texture = pinched ? handPinch : handOpen;
            // The pinch point of the hand art is 84 px left of and 16 px above the image centre.
            hand.Position = P(pinchPoint.x + 84f, pinchPoint.y + 16f);

            var jeepPx = new Vector2(RoadCenter + JeepOffsetAt(t), 655f);
            jeep.Position = P(jeepPx);
            beacon.Visible = pinched;
            beacon.Position = P(pinchPoint);
            beacon.Size = Size(110f, 110f) * (1f + 0.12f * Mathf.Sin(t * 14f));
            tether.Visible = pinched;
            Line(tether, P(pinchPoint), P(jeepPx.x, jeepPx.y - 70f), 8f * Px);

            float velocity = SteerOffset(t + 0.05f) - SteerOffset(t - 0.05f);
            float baseA = pinched ? 0.3f : 0f;
            arrowLeft.Position = P(jeepPx.x - 125f, jeepPx.y - 20f);
            arrowRight.Position = P(jeepPx.x + 125f, jeepPx.y - 20f);
            arrowLeft.Color = new Color(1f, 1f, 1f, velocity < -2f ? 1f : baseA);
            arrowRight.Color = new Color(1f, 1f, 1f, velocity > 2f ? 1f : baseA);

            // ----- Scouting demo -----
            Vector2 spotPx = SpotAt(t);
            spot.Position = P(spotPx);
            var eyePx = new Vector2(1180f + (spotPx.x - 1180f) * 0.06f, 215f);
            eye.Position = P(eyePx);
            Vector2 beamFrom = P(eyePx.x, eyePx.y + 30f), beamTo = P(spotPx);
            Line(beam, beamFrom, beamTo, SpotRadius * 1.7f * Px);

            for (int i = 0; i < trees.Length; i++)
            {
                float lit = Reveal(TreeSpots[i], spotPx);
                trees[i].Color = new Color(1f, 1f, 1f, Mathf.Lerp(0.14f, 1f, lit));
                trees[i].Size = Size(110f, 110f) * (1f + 0.08f * lit);
            }
            float logLit = Reveal(LogSpot, spotPx);
            log.Color = new Color(1f, 1f, 1f, logLit);

            // ----- Countdown bar along the bottom -----
            float progress = Mathf.Clamp01(t / Duration);
            float w = Mathf.Max(1468f * progress, 1f);
            bar.Size = Size(w, 14f);
            bar.Position = P(66f + w * 0.5f, 853f);
        }

        static float SteerOffset(float t)
        {
            if (t < PinchStart || t >= PinchEnd) return 0f;
            float u = (t - PinchStart) / (PinchEnd - PinchStart);
            return -SteerAmplitude * Mathf.Sin(u * Mathf.PI * 2f); // left first, then right, then back
        }

        static Vector2 SpotAt(float t)
        {
            for (int i = 1; i < SpotKeys.Length; i++)
            {
                if (t > SpotKeys[i].x) continue;
                Vector3 a = SpotKeys[i - 1], b = SpotKeys[i];
                float k = Smooth(a.x, b.x, t);
                return new Vector2(Mathf.Lerp(a.y, b.y, k), Mathf.Lerp(a.z, b.z, k));
            }
            Vector3 last = SpotKeys[SpotKeys.Length - 1];
            return new Vector2(last.y, last.z);
        }

        static float Reveal(Vector2 thing, Vector2 spotPx)
        {
            float d = Vector2.Distance(thing, spotPx);
            return 1f - Smooth(SpotRadius * 0.6f, SpotRadius * 1.3f, d);
        }

        /// <summary>Stretches a sprite between two panel points; the sprite's top edge points at "from".</summary>
        static void Line(UISprite s, Vector2 from, Vector2 to, float width)
        {
            Vector2 d = from - to;
            s.Position = (from + to) * 0.5f;
            s.Size = new Vector2(width, Mathf.Max(d.magnitude, 0.01f));
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
            s.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
        static Vector2 P(float px, float py) => new Vector2(px * Px - StartMenu.Width * 0.5f, StartMenu.Width * 900f / 1600f * 0.5f - py * Px);
        static Vector2 P(Vector2 px) => P(px.x, px.y);
        static Vector2 Size(float w, float h) => new Vector2(w * Px, h * Px);
    }
}
