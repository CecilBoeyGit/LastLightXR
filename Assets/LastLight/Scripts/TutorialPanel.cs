using UnityEngine;

namespace LastLight
{
    public enum TutorialMode { BeforeRun, FromMenu }

    /// <summary>
    /// The animated "How to play" card. Before every run it loops its steering and scouting page with no time
    /// limit, until the player looks at Start and pinches. From the menu it alternates that page with a second
    /// one about obstacles, and a Back button returns to the menu.
    /// Page 1, left: a hand pinches the jeep's beacon and slides it left and right, and the jeep follows.
    /// Page 1, right: an eye sweeps a scout light across a dark fork, revealing trees and a fallen log.
    /// Page 2, left: rocks and logs come down the road and the jeep weaves round them.
    /// Page 2, right: a cliff road over the abyss; drifting onto its edge flashes a warning.
    /// Show(t) poses everything for time t, so the animation is deterministic and testable.
    /// </summary>
    public sealed class TutorialPanel
    {
        /// <summary>Length of one loop of a page's animation.</summary>
        public const float Duration = 5f;
        const float Px = StartMenu.Width / 1600f; // the panel art is 1600 x 900 pixels
        const float PinchStart = 0.6f, PinchEnd = 4.4f;
        const float SteerAmplitude = 160f;        // pixels either side of the road centre
        const float RoadCenter = 360f;
        const float SpotRadius = 120f;

        public readonly Transform root;
        readonly Transform page1, page2;
        readonly UISprite panel, hand, beacon, tether, jeep, arrowLeft, arrowRight, eye, beam, spot, log;
        readonly UISprite[] dashes = new UISprite[4];
        readonly UISprite[] trees;
        readonly Texture2D handOpen, handPinch, runArt, obstacleArt;
        readonly UISprite[] all;
        // Page 2
        readonly UISprite obJeep, obHand, obBeacon, obTether, cliffJeep, edgeFlash, warn;
        readonly UISprite[] obstacles = new UISprite[3];
        readonly UISprite[] obDashes = new UISprite[4];
        readonly UISprite[] page2All;
        readonly Label captionLeft, captionRight, hint;
        readonly UISprite[] pageDots = new UISprite[2];
        readonly GazeButton start, back;
        readonly GazeButton[] buttons;
        readonly GazeFocus focus = new GazeFocus();
        readonly UISprite cursor;

        public TutorialMode Mode { get; private set; }
        /// <summary>0 for steering and scouting, 1 for obstacles (from the menu only).</summary>
        public int Page { get; private set; }
        public int Focused => focus.Focused;
        public Vector3 ButtonWorldCenter => (Mode == TutorialMode.BeforeRun ? start : back).root.position;

        // Page 2 obstacles: road-relative pixel offset and the time each reaches the jeep.
        static readonly Vector3[] ObstacleKeys = { new Vector3(-95f, 1.3f, 0f), new Vector3(85f, 2.9f, 1f), new Vector3(-30f, 4.5f, 0f) };

        static readonly Vector2[] TreeSpots =
        {
            new Vector2(890f, 300f), new Vector2(930f, 600f), new Vector2(1075f, 640f), new Vector2(1180f, 320f),
            new Vector2(1285f, 640f), new Vector2(1440f, 290f), new Vector2(1470f, 570f), new Vector2(960f, 430f)
        };
        static readonly Vector3[] SpotKeys = // time, x, y in panel pixels
        {
            new Vector3(0f, 1180f, 640f), new Vector3(0.5f, 1180f, 640f), new Vector3(1.5f, 1080f, 410f),
            new Vector3(2.4f, 1080f, 410f), new Vector3(3.5f, 1335f, 300f), new Vector3(4.2f, 1335f, 300f),
            new Vector3(5f, 1180f, 640f)
        };
        static readonly Vector2 LogSpot = new Vector2(1075f, 405f); // across the left branch of the fork

        public TutorialPanel(Transform parent, Material alphaMat, Material addMat)
        {
            root = new GameObject("Tutorial").transform;
            root.SetParent(parent, false);
            Texture2D white = UISprite.Load("white");
            Texture2D spotTex = UISprite.Load("spot");
            Color ivory = Palette.UIIvory, amber = Palette.UIAmber;

            runArt = UISprite.Load("tut_panel_run");
            obstacleArt = UISprite.Load("tut_panel_obstacles");
            panel = new UISprite("Panel", root, runArt, Size(1600f, 900f), alphaMat, 0);
            page1 = new GameObject("Page Steer And Scout").transform;
            page1.SetParent(root, false);
            page2 = new GameObject("Page Obstacles").transform;
            page2.SetParent(root, false);
            Transform pr = page1;

            for (int i = 0; i < dashes.Length; i++)
                dashes[i] = new UISprite("Road Dash", pr, white, Size(12f, 56f), alphaMat, 1) { Color = new Color(ivory.r, ivory.g, ivory.b, 0.5f) };
            jeep = new UISprite("Jeep", pr, UISprite.Load("jeep"), Size(150f, 150f), alphaMat, 4);
            Texture2D arrow = UISprite.Load("arrow");
            arrowLeft = new UISprite("Arrow Left", pr, arrow, Size(70f, 70f), alphaMat, 4);
            arrowLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            arrowRight = new UISprite("Arrow Right", pr, arrow, Size(70f, 70f), alphaMat, 4);
            tether = new UISprite("Tether", pr, white, Size(8f, 100f), addMat, 3) { Color = new Color(amber.r, amber.g, amber.b, 0.55f) };
            beacon = new UISprite("Beacon", pr, spotTex, Size(120f, 120f), addMat, 6) { Color = new Color(1f, 0.7f, 0.3f, 0.9f) };
            handOpen = UISprite.Load("hand_open");
            handPinch = UISprite.Load("hand_pinch");
            hand = new UISprite("Hand", pr, handOpen, Size(300f, 300f), alphaMat, 5);

            trees = new UISprite[TreeSpots.Length];
            for (int i = 0; i < trees.Length; i++)
                trees[i] = new UISprite("Ghost Tree", pr, UISprite.Load("tree"), Size(110f, 110f), alphaMat, 2) { Position = P(TreeSpots[i]) };
            log = new UISprite("Fallen Log", pr, UISprite.Load("log"), Size(210f, 105f), alphaMat, 2) { Position = P(LogSpot) };
            beam = new UISprite("Gaze Beam", pr, UISprite.Load("beam"), Size(200f, 300f), addMat, 3) { Color = new Color(1f, 0.78f, 0.4f, 0.85f) };
            spot = new UISprite("Scout Light", pr, spotTex, Size(SpotRadius * 2.6f, SpotRadius * 2.6f), addMat, 3) { Color = new Color(1f, 0.68f, 0.28f, 0.6f) };
            eye = new UISprite("Eye", pr, UISprite.Load("eye"), Size(230f, 115f), alphaMat, 5);

            all = new[] { hand, beacon, tether, jeep, arrowLeft, arrowRight, eye, beam, spot, log, dashes[0], dashes[1], dashes[2], dashes[3] };
            System.Array.Resize(ref all, all.Length + trees.Length);
            trees.CopyTo(all, all.Length - trees.Length);

            // ----- Page 2: obstacles on the road, and the edges of a cliff road -----
            Transform p2 = page2;
            for (int i = 0; i < obDashes.Length; i++)
                obDashes[i] = new UISprite("Road Dash", p2, white, Size(12f, 56f), alphaMat, 1) { Color = new Color(ivory.r, ivory.g, ivory.b, 0.5f) };
            Texture2D rock = UISprite.Load("rock"), logTex = UISprite.Load("log");
            for (int i = 0; i < obstacles.Length; i++)
                obstacles[i] = new UISprite(i == 1 ? "Log" : "Rock", p2, i == 1 ? logTex : rock, i == 1 ? Size(200f, 100f) : Size(130f, 130f), alphaMat, 2);
            obJeep = new UISprite("Jeep", p2, UISprite.Load("jeep"), Size(150f, 150f), alphaMat, 4);
            obTether = new UISprite("Tether", p2, white, Size(8f, 100f), addMat, 3) { Color = new Color(amber.r, amber.g, amber.b, 0.55f) };
            obBeacon = new UISprite("Beacon", p2, spotTex, Size(110f, 110f), addMat, 6) { Color = new Color(1f, 0.7f, 0.3f, 0.9f) };
            obHand = new UISprite("Hand", p2, handPinch, Size(300f, 300f), alphaMat, 5);
            edgeFlash = new UISprite("Edge Warning", p2, spotTex, Size(240f, 240f), addMat, 3) { Color = new Color(1f, 0.3f, 0.2f, 0f) };
            cliffJeep = new UISprite("Cliff Jeep", p2, UISprite.Load("jeep"), Size(120f, 120f), alphaMat, 4);
            warn = new UISprite("Warning", p2, UISprite.Load("arrow"), Size(70f, 70f), alphaMat, 5);
            page2All = new[] { obJeep, obTether, obBeacon, obHand, edgeFlash, cliffJeep, warn, obstacles[0], obstacles[1], obstacles[2], obDashes[0], obDashes[1], obDashes[2], obDashes[3] };
            captionLeft = new Label(page2, "Caption Left", "DODGE ROCKS + LOGS", 0.85f, TextAnchor.MiddleCenter, ivory, P(420f, 785f), 22);
            captionRight = new Label(page2, "Caption Right", "STAY OFF THE EDGES", 0.85f, TextAnchor.MiddleCenter, ivory, P(1180f, 785f), 22);

            // ----- Buttons along the bottom edge -----
            Vector2 buttonAt = P(800f, 868f);
            var buttonSize = new Vector2(5.4f, 1.35f);
            start = new GazeButton(root, "Start", "btn_start", buttonAt, buttonSize, alphaMat, 7);
            back = new GazeButton(root, "Back", "btn_back", buttonAt, buttonSize, alphaMat, 7);
            buttons = new[] { start, back };
            hint = new Label(root, "Hint", "LOOK AT START, THEN PINCH", 0.42f, TextAnchor.MiddleRight, amber, P(1550f, 70f), 22);
            Texture2D dot = UISprite.Load("dot");
            for (int i = 0; i < pageDots.Length; i++)
                pageDots[i] = new UISprite("Page Dot " + i, root, dot, Size(26f, 26f), alphaMat, 6) { Position = P(1460f + i * 44f, 866f) };
            cursor = new UISprite("Gaze Cursor", root, spotTex, new Vector2(0.9f, 0.9f), addMat, 9) { Color = new Color(1f, 0.72f, 0.3f, 0.75f) };
            cursor.Visible = false;
            SetMode(TutorialMode.BeforeRun);
            Show(0f);
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set
            {
                root.gameObject.SetActive(value);
                if (!value) focus.Clear();
            }
        }

        /// <summary>Before a run: one looping page and a Start button. From the menu: two alternating pages and Back.</summary>
        public void SetMode(TutorialMode mode)
        {
            Mode = mode;
            focus.Clear();
            start.Visible = mode == TutorialMode.BeforeRun;
            back.Visible = mode == TutorialMode.FromMenu;
            hint.Text = mode == TutorialMode.BeforeRun ? "LOOK AT START, THEN PINCH" : "LOOK AT BACK, THEN PINCH";
            foreach (var d in pageDots) d.Visible = mode == TutorialMode.FromMenu;
        }

        /// <summary>
        /// Gaze focus and presses for the panel's button. Returns true when the focused button is pinched:
        /// Start begins the run, Back returns to the menu.
        /// </summary>
        public bool Tick(float dt, Ray? gaze, bool select)
        {
            Vector2 point = default;
            float halfW = StartMenu.Width * 0.5f, halfH = StartMenu.Width * 900f / 1600f * 0.5f + 1f;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out point) && Mathf.Abs(point.x) <= halfW && Mathf.Abs(point.y) <= halfH;
            focus.Update(dt, onPanel ? point : (Vector2?)null, buttons);
            start.Refresh(focus.Focused == 0, dt);
            back.Refresh(focus.Focused == 1, dt);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;
            return select && focus.Focused >= 0;
        }

        /// <summary>Hand pinch state at time t, for tests.</summary>
        public static bool PinchedAt(float t) => t >= PinchStart && t < PinchEnd;

        /// <summary>Jeep offset from the road centre at time t, in panel pixels.</summary>
        public static float JeepOffsetAt(float t) => SteerOffset(t - 0.15f);

        /// <summary>Which page shows at time t: before a run always the first, from the menu they alternate.</summary>
        public int PageAt(float t) => Mode == TutorialMode.FromMenu ? Mathf.FloorToInt(t / Duration) % 2 : 0;

        public void Show(float time)
        {
            Page = PageAt(time);
            float t = Mathf.Repeat(time, Duration);
            bool firstLoop = time < Duration;
            // Fade in once; from the menu, also dip briefly between pages.
            float fade = firstLoop ? Smooth(0f, 0.25f, t) : 1f;
            if (Mode == TutorialMode.FromMenu) fade = Mathf.Min(Smooth(0f, 0.25f, t), 1f - Smooth(Duration - 0.2f, Duration, t));
            panel.Alpha = firstLoop ? Smooth(0f, 0.25f, t) : 1f;
            panel.Texture = Page == 1 ? obstacleArt : runArt;
            if (page1.gameObject.activeSelf != (Page == 0)) page1.gameObject.SetActive(Page == 0);
            if (page2.gameObject.activeSelf != (Page == 1)) page2.gameObject.SetActive(Page == 1);
            for (int i = 0; i < pageDots.Length; i++) pageDots[i].Color = i == Page ? Palette.UIAmber : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            hint.Alpha = 0.6f + 0.4f * Mathf.Sin(time * 3f);
            if (Page == 1) { ShowObstacles(t, fade); return; }
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
        }

        /// <summary>Page 2: weaving round obstacles on the road, and keeping off a cliff road's edge.</summary>
        void ShowObstacles(float t, float fade)
        {
            foreach (var s in page2All) s.Alpha = fade;
            captionLeft.Alpha = fade;
            captionRight.Alpha = fade;

            // Left: the road scrolls, obstacles come down it, and the pinched beacon pulls the jeep aside.
            for (int i = 0; i < obDashes.Length; i++)
            {
                float y = 160f + Mathf.Repeat(t * 320f + i * 150f, 600f);
                obDashes[i].Position = P(RoadCenter, y);
                obDashes[i].Alpha = fade * Mathf.Min(Smooth(160f, 220f, y), 1f - Smooth(680f, 740f, y));
            }
            for (int i = 0; i < obstacles.Length; i++)
            {
                Vector3 k = ObstacleKeys[i];
                // Each obstacle reaches the jeep (y 600) at its key time, travelling 320 px a second.
                float y = 600f - (k.y - t) * 320f;
                bool on = y > 150f && y < 760f;
                obstacles[i].Visible = on;
                if (!on) continue;
                float persp = Mathf.Lerp(0.72f, 1.08f, Mathf.InverseLerp(160f, 700f, y));
                obstacles[i].Position = P(RoadCenter + k.x * persp, y);
                obstacles[i].Size = (k.z > 0.5f ? Size(200f, 100f) : Size(130f, 130f)) * persp;
                obstacles[i].Alpha = fade * Mathf.Min(Smooth(150f, 200f, y), 1f - Smooth(720f, 760f, y));
            }
            float dodge = DodgeOffset(t);
            var jeepPx = new Vector2(RoadCenter + dodge, 655f);
            obJeep.Position = P(jeepPx);
            var pinch = new Vector2(jeepPx.x, 430f);
            obBeacon.Position = P(pinch);
            obBeacon.Size = Size(110f, 110f) * (1f + 0.12f * Mathf.Sin(t * 14f));
            obHand.Position = P(pinch.x + 84f, pinch.y + 16f);
            Line(obTether, P(pinch), P(jeepPx.x, jeepPx.y - 70f), 8f * Px);

            // Right: the jeep follows the cliff road's curve, drifts onto the edge, flashes a warning, recovers.
            float cy = 600f;
            float roadX = CliffCentre(cy);
            float drift = 70f * Smooth(1.8f, 2.6f, t) * (1f - Smooth(3.3f, 4.1f, t));
            var cliffPx = new Vector2(roadX + drift, cy);
            cliffJeep.Position = P(cliffPx);
            float danger = Smooth(2.2f, 2.6f, t) * (1f - Smooth(3.2f, 3.6f, t));
            edgeFlash.Position = P(cliffPx.x + 55f, cy);
            edgeFlash.Color = new Color(1f, 0.3f, 0.2f, danger * (0.55f + 0.45f * Mathf.Sin(t * 20f)));
            warn.Position = P(cliffPx.x - 105f, cy - 20f);
            warn.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            warn.Color = new Color(1f, 1f, 1f, danger);
        }

        /// <summary>Page 2 jeep offset at time t, in panel pixels: it steps aside before each obstacle arrives.</summary>
        public static float DodgeOffset(float t)
        {
            float x = 0f;
            float prev = 0f;
            foreach (var k in ObstacleKeys)
            {
                float side = k.x < 0f ? 110f : -110f;   // pass on the far side of the obstacle
                x = Mathf.Lerp(prev, side, Smooth(k.y - 0.9f, k.y - 0.35f, t));
                if (t < k.y + 0.2f) return x;
                prev = side;
            }
            return Mathf.Lerp(prev, 0f, Smooth(4.7f, 5f, t));
        }

        /// <summary>True if the page 2 jeep, at time t, is clear of every obstacle it is passing.</summary>
        public static bool DodgesEveryObstacle()
        {
            foreach (var k in ObstacleKeys)
                if (Mathf.Abs(DodgeOffset(k.y) - k.x) < 120f) return false;
            return true;
        }

        static float CliffCentre(float y) => 1180f + 70f * Mathf.Sin((y - 160f) / 574f * Mathf.PI * 1.4f);

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
