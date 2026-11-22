using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// A MENU button that floats over either wrist while that palm faces the player's head, like checking a watch.
    /// Looking at it fills it left to right; a full second's look opens the pause menu. It is a look, not a pinch,
    /// because a pinch during a run grabs the steering beacon. Both wrists can show a button at once.
    /// </summary>
    public sealed class WristMenu
    {
        public const float DwellSeconds = 1f;           // a full second, so a passing glance at the wrist never pauses
        public const float MetresPerUnit = 0.02f;       // the button is laid out in panel units, then sized to this
        public static readonly Vector2 ButtonSize = new Vector2(4f, 1f);   // 8 cm by 2 cm
        // Palm-to-head facing, as the cosine of the angle between the palm normal and the direction to the head.
        // It appears within about 50 degrees and stays until about 65, so it doesn't flicker at the edge.
        public const float ShowFacing = 0.64f, HideFacing = 0.42f;
        const float HitPad = 0.5f;          // a centimetre of slack around the button for eye jitter
        const float GazeGrace = 0.2f;       // a blink or a flick away keeps the fill
        const float LiftOffWrist = 0.035f;  // metres out of the palm side of the wrist
        const float TowardElbow = 0.025f;   // metres back from the wrist, where a watch face would be

        sealed class Side
        {
            public Transform root;
            public GazeButton button;
            public UISprite fill;
            public bool shown, placed, gazed;
            public float dwell, lost, pop;
            public Vector3 pos;
        }

        readonly Side[] sides = new Side[2];

        public WristMenu(Transform parent, Material alphaMat, Material addMat)
        {
            for (int i = 0; i < 2; i++)
            {
                var s = new Side();
                s.root = new GameObject(i == 0 ? "Wrist Menu Left" : "Wrist Menu Right").transform;
                s.root.SetParent(parent, false);
                s.button = new GazeButton(s.root, "Menu", "btn_menu", Vector2.zero, ButtonSize, alphaMat, 30);
                s.fill = new UISprite("Gaze Fill", s.root, UISprite.Load("white"), new Vector2(0f, ButtonSize.y * 0.78f), addMat, 31)
                {
                    Color = new Color(1f, 0.72f, 0.3f, 0.55f)
                };
                s.root.gameObject.SetActive(false);
                sides[i] = s;
            }
        }

        /// <summary>Whether the button on a wrist (0 left, 1 right) is showing.</summary>
        public bool Shown(int hand) => sides[hand].shown;
        public bool AnyShown => sides[0].shown || sides[1].shown;
        /// <summary>How far the look has filled a wrist's button, 0 to 1.</summary>
        public float Progress(int hand) => Mathf.Clamp01(sides[hand].dwell / DwellSeconds);
        public bool Gazed(int hand) => sides[hand].gazed;
        public Vector3 ButtonWorldCenter(int hand) => sides[hand].root.position;
        public Transform Root(int hand) => sides[hand].root;

        public void Hide()
        {
            foreach (var s in sides) SetShown(s, false);
        }

        /// <summary>
        /// Shows, places and fills the wrist buttons. Returns the hand (0 left, 1 right) whose button a full look
        /// has just pressed, or -1. Nothing shows unless <paramref name="enabled"/> (a run is under way).
        /// </summary>
        public int Tick(float dt, HandFrame left, HandFrame right, Vector3 head, Ray? gaze, bool enabled)
        {
            int pressed = -1;
            for (int i = 0; i < 2; i++)
            {
                var s = sides[i];
                HandFrame f = i == 0 ? left : right;
                float facing = Facing(f, head);
                bool want = enabled && f.tracked && !f.pinching && facing >= (s.shown ? HideFacing : ShowFacing);
                if (want != s.shown) SetShown(s, want);
                if (!s.shown) continue;

                Place(s, f, head, dt);
                s.gazed = gaze.HasValue && GazeFocus.Hit(s.root, gaze.Value, out Vector2 p) && s.button.Contains(p, HitPad);
                if (s.gazed) { s.lost = 0f; s.dwell += dt; }
                else if ((s.lost += dt) > GazeGrace) s.dwell = 0f;

                s.button.Refresh(s.gazed, dt);
                float k = Progress(i);
                float full = ButtonSize.x * 0.89f;      // the art's inner face, inside its border
                s.fill.Visible = k > 0f;
                s.fill.Size = new Vector2(full * k, ButtonSize.y * 0.78f);
                s.fill.Position = new Vector2(-full * 0.5f + full * k * 0.5f, 0f);

                if (s.dwell >= DwellSeconds && pressed < 0)
                {
                    pressed = i;
                    s.dwell = 0f;
                }
            }
            return pressed;
        }

        /// <summary>How squarely a palm faces the head: 1 straight at it, 0 side on, negative away.</summary>
        public static float Facing(HandFrame f, Vector3 head)
        {
            if (!f.tracked || f.palmNormal.sqrMagnitude < 1e-6f) return -1f;
            Vector3 toHead = head - f.palm;
            if (toHead.sqrMagnitude < 1e-6f) return -1f;
            return Vector3.Dot(f.palmNormal.normalized, toHead.normalized);
        }

        void Place(Side s, HandFrame f, Vector3 head, float dt)
        {
            Vector3 fwd = f.handForward.sqrMagnitude > 1e-6f ? f.handForward.normalized : Vector3.up;
            Vector3 wrist = f.wrist.sqrMagnitude > 1e-6f ? f.wrist : f.palm - fwd * 0.04f;
            Vector3 target = wrist + f.palmNormal.normalized * LiftOffWrist - fwd * TowardElbow;
            // Light smoothing takes the shake out of hand tracking without making the button lag behind the arm.
            s.pos = s.placed ? Vector3.Lerp(s.pos, target, 1f - Mathf.Exp(-25f * dt)) : target;
            s.placed = true;
            s.root.position = s.pos;
            Vector3 face = s.pos - head;
            if (face.sqrMagnitude > 1e-6f) s.root.rotation = Quaternion.LookRotation(face, Vector3.up);
            // A quick pop in when it appears.
            s.pop = Mathf.MoveTowards(s.pop, 1f, dt / 0.12f);
            float lossy = s.root.parent != null ? Mathf.Max(s.root.parent.lossyScale.x, 1e-6f) : 1f;
            s.root.localScale = Vector3.one * (MetresPerUnit / lossy * Mathf.Lerp(0.6f, 1f, s.pop));
        }

        static void SetShown(Side s, bool on)
        {
            s.shown = on;
            s.root.gameObject.SetActive(on);
            s.dwell = 0f;
            s.lost = 0f;
            s.gazed = false;
            if (!on) { s.placed = false; s.pop = 0f; }
        }
    }
}
