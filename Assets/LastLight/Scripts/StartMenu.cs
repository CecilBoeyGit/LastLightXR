using UnityEngine;

namespace LastLight
{
    public enum MenuAction { None, StartRun, Garage, HowToPlay }

    /// <summary>
    /// Title screen: the retro comic splash with live buttons in its dark patch below the title.
    /// Look at a button to focus it (amber outline), then pinch with either hand to press it.
    /// </summary>
    public sealed class StartMenu
    {
        // Splash art is 1672 x 941. Buttons sit where the art leaves room for them:
        // roughly 7-30% across and 53-83% down from the upper left.
        public const float Width = 26f;
        public const float Height = Width * 941f / 1672f;
        public const int StartIndex = 0, GarageIndex = 1, HowToIndex = 2;
        static readonly Vector2 ButtonSize = new Vector2(5.8f, 1.38f);
        static readonly Vector2[] ButtonCenters = { new Vector2(-8.2f, -1.0f), new Vector2(-8.2f, -2.55f), new Vector2(-8.2f, -4.1f) };
        const float FocusPadding = 0.3f;     // extra hit margin around a button
        const float KeepFocusPadding = 0.7f; // larger margin once focused, so eye jitter doesn't drop it
        const float FocusGrace = 0.35f;      // seconds focus survives a glance away or a blink

        public readonly Transform root;
        readonly UISprite splash, cursor, hint;
        readonly UISprite[] buttons = new UISprite[3];
        readonly Texture2D[] normal = new Texture2D[3], focusedTex = new Texture2D[3];
        float lostTimer, time;

        /// <summary>Index of the focused button: 0 Start Run, 1 Garage, 2 How to Play, -1 none.</summary>
        public int Focused { get; private set; } = -1;

        public StartMenu(Transform parent, Material alphaMat, Material addMat)
        {
            root = new GameObject("Start Menu").transform;
            root.SetParent(parent, false);
            Texture2D white = UISprite.Load("white");

            new UISprite("Frame Ink", root, white, new Vector2(Width + 0.8f, Height + 0.8f), alphaMat, 0) { Color = Palette.UIInk };
            new UISprite("Frame Ivory", root, white, new Vector2(Width + 0.4f, Height + 0.4f), alphaMat, 1) { Color = Palette.UIIvory };
            splash = new UISprite("Splash", root, UISprite.Load("splash"), new Vector2(Width, Height), alphaMat, 2);

            string[] names = { "start", "garage", "howto" };
            for (int i = 0; i < names.Length; i++)
            {
                normal[i] = UISprite.Load("btn_" + names[i]);
                focusedTex[i] = UISprite.Load("btn_" + names[i] + "_focus");
                buttons[i] = new UISprite("Button " + names[i], root, normal[i], ButtonSize, alphaMat, 3) { Position = ButtonCenters[i] };
            }
            hint = new UISprite("Hint", root, UISprite.Load("menu_hint"), new Vector2(7.4f, 7.4f * 96f / 1024f), alphaMat, 3)
            {
                Position = new Vector2(-8.2f, -5.3f)
            };
            cursor = new UISprite("Gaze Cursor", root, UISprite.Load("spot"), new Vector2(0.9f, 0.9f), addMat, 6)
            {
                Color = new Color(1f, 0.72f, 0.3f, 0.75f)
            };
            cursor.Visible = false;
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set
            {
                root.gameObject.SetActive(value);
                if (!value) { Focused = -1; lostTimer = 0f; }
            }
        }

        public Vector3 ButtonWorldCenter(int index) => root.TransformPoint(ButtonCenters[index]);

        /// <summary>Updates focus from a world-space gaze ray. Returns the action a select press triggers this frame.</summary>
        public MenuAction Tick(float dt, Ray? gaze, bool select)
        {
            time += dt;
            Vector2 point = default;
            bool onPanel = gaze.HasValue && Hit(gaze.Value, out point)
                && Mathf.Abs(point.x) <= Width * 0.5f && Mathf.Abs(point.y) <= Height * 0.5f;

            int hit = -1;
            if (onPanel)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    float pad = Focused == i ? KeepFocusPadding : FocusPadding;
                    Vector2 d = point - ButtonCenters[i];
                    if (Mathf.Abs(d.x) <= ButtonSize.x * 0.5f + pad && Mathf.Abs(d.y) <= ButtonSize.y * 0.5f + pad) { hit = i; break; }
                }
            }
            if (hit >= 0) { Focused = hit; lostTimer = 0f; }
            else if (Focused >= 0)
            {
                lostTimer += dt;
                if (lostTimer > FocusGrace) Focused = -1;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                bool f = Focused == i;
                buttons[i].Texture = f ? focusedTex[i] : normal[i];
                float target = f ? 1.06f : 1f;
                float s = Mathf.Lerp(buttons[i].Size.x / ButtonSize.x, target, 1f - Mathf.Exp(-14f * dt));
                buttons[i].Size = ButtonSize * s;
            }
            hint.Alpha = 0.65f + 0.35f * Mathf.Sin(time * 3f);

            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;

            if (!select || Focused < 0) return MenuAction.None;
            return Focused switch
            {
                StartIndex => MenuAction.StartRun,
                GarageIndex => MenuAction.Garage,
                _ => MenuAction.HowToPlay
            };
        }

        /// <summary>Intersects a world ray with the panel plane; the point is in panel units.</summary>
        bool Hit(Ray ray, out Vector2 point)
        {
            point = default;
            Vector3 o = root.InverseTransformPoint(ray.origin);
            Vector3 d = root.InverseTransformDirection(ray.direction);
            if (d.z <= 1e-5f) return false; // looking away from the panel
            float t = -o.z / d.z;
            if (t <= 0f) return false;
            Vector3 p = o + d * t;
            point = new Vector2(p.x, p.y);
            return true;
        }
    }
}
