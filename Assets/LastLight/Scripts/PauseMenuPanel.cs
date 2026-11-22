using UnityEngine;

namespace LastLight
{
    public enum PauseChoice { None, Resume, Restart, MainMenu }

    /// <summary>
    /// The pause menu, opened from the wrist MENU button. It floats in front of the player and lazily follows their
    /// head, like the run-over panel, while the run stands still behind it. Look at a button to focus it and pinch
    /// to press it, as on the main menu: Resume carries on, Restart starts a fresh run, Main Menu goes to the title.
    /// </summary>
    public sealed class PauseMenuPanel
    {
        public const float Width = 13f;
        public const float Height = Width * 720f / 1200f;
        public const int ResumeIndex = 0, RestartIndex = 1, MainMenuIndex = 2;
        const float PanelScale = 0.72f;         // laid out in disk units, then shrunk to this, like the run-over panel
        static readonly Vector2 ButtonSize = new Vector2(6.2f, 1.55f);

        public readonly Transform root;
        readonly UISprite cursor;
        readonly Label stats;
        readonly GazeButton[] buttons;
        readonly GazeFocus focus = new GazeFocus();
        readonly HeadFollow follow = new HeadFollow();

        public int Focused => focus.Focused;

        public PauseMenuPanel(Transform parent, Material alphaMat, Material addMat)
        {
            root = new GameObject("Pause Menu").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * PanelScale;
            new UISprite("Background", root, UISprite.Load("panel_wide"), new Vector2(Width, Height), alphaMat, 0);
            new UISprite("Title", root, UISprite.Load("title_paused"), new Vector2(7.2f, 7.2f * 160f / 1024f), alphaMat, 2)
            {
                Position = new Vector2(0f, Height * 0.5f - 0.9f)
            };
            stats = new Label(root, "Stats", "", 0.42f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(0f, 2.05f));
            buttons = new[]
            {
                new GazeButton(root, "Resume", "btn_resume", new Vector2(0f, 0.75f), ButtonSize, alphaMat),
                new GazeButton(root, "Restart", "btn_restart", new Vector2(0f, -0.95f), ButtonSize, alphaMat),
                new GazeButton(root, "Main Menu", "btn_mainmenu", new Vector2(0f, -2.65f), ButtonSize, alphaMat),
            };
            cursor = new UISprite("Gaze Cursor", root, UISprite.Load("spot"), new Vector2(0.9f, 0.9f), addMat, 8)
            {
                Color = new Color(1f, 0.72f, 0.3f, 0.75f)
            };
            cursor.Visible = false;
            root.gameObject.SetActive(false);
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set
            {
                root.gameObject.SetActive(value);
                if (!value) { focus.Clear(); follow.Reset(); }
            }
        }

        public Vector3 ButtonWorldCenter(int index) => root.TransformPoint(buttons[index].Center);

        public void SetContent(float distance, int runCoins) =>
            stats.Text = $"{distance:0} M      {runCoins} COIN{(runCoins == 1 ? "" : "S")} THIS RUN";

        public void Follow(Transform head, float dt, bool snap) => follow.Follow(root, head, dt, snap);

        public PauseChoice Tick(float dt, Ray? gaze, bool select, bool acceptInput)
        {
            Vector2 point = default;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out point)
                && Mathf.Abs(point.x) <= Width * 0.5f && Mathf.Abs(point.y) <= Height * 0.5f;
            focus.Update(dt, onPanel ? point : (Vector2?)null, buttons);
            for (int i = 0; i < buttons.Length; i++) buttons[i].Refresh(focus.Focused == i, dt);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;

            if (!select || !acceptInput || focus.Focused < 0) return PauseChoice.None;
            return focus.Focused switch
            {
                ResumeIndex => PauseChoice.Resume,
                RestartIndex => PauseChoice.Restart,
                _ => PauseChoice.MainMenu
            };
        }
    }
}
