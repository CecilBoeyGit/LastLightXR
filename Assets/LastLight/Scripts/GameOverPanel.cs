using UnityEngine;

namespace LastLight
{
    public enum GameOverChoice { None, Retry, Respawn, MainMenu }

    /// <summary>
    /// The run-over panel. It floats in front of the player and lazily follows their head: it stays put
    /// for small head turns, then glides back in front once they look well away. Retry starts a fresh run,
    /// Respawn spends coins to carry on this run, Main Menu goes back to the title. A coin counter sits on
    /// the panel's side so the respawn price can be read against the bank.
    /// </summary>
    public sealed class GameOverPanel
    {
        public const float Width = 17f;
        public const float Height = Width * 720f / 1200f;
        const float FollowDistance = 0.62f;         // metres in front of the head, nearer than the disk's far side
        const float PanelScale = 0.72f;             // the panel is laid out in disk units, then shrunk to this
        const float DeadZoneDegrees = 16f;          // head turns smaller than this leave the panel where it is
        const float SettleDegrees = 3f;
        const float FollowRate = 3.2f;              // exponential catch-up rate while moving
        // How far the panel may sit below or above eye level. Kept shallow so it floats above the disk,
        // where the terrain can't cover it, even when the player is looking down at the road.
        const float MaxDown = 16f, MaxUp = 10f;
        static readonly Vector2 ButtonSize = new Vector2(6.2f, 1.55f);

        public readonly Transform root;
        readonly UISprite cursor, coinIcon, sideBg;
        readonly Label reason, stats, cost, coinCount, coinCaption;
        readonly GazeButton retry, respawn, menu;
        readonly GazeButton[] buttons;
        readonly GazeFocus focus = new GazeFocus();
        Vector3 worldPos;
        bool moving, placed;
        float time;

        public int Focused => focus.Focused;
        public bool RespawnEnabled => respawn.Enabled;

        public GameOverPanel(Transform parent, Material alphaMat, Material addMat)
        {
            root = new GameObject("Game Over Panel").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * PanelScale;
            new UISprite("Background", root, UISprite.Load("panel_wide"), new Vector2(Width, Height), alphaMat, 0);
            new UISprite("Title", root, UISprite.Load("title_runover"), new Vector2(8.4f, 8.4f * 160f / 1024f), alphaMat, 2)
            {
                Position = new Vector2(-1.6f, Height * 0.5f - 1.25f)
            };
            reason = new Label(root, "Reason", "", 0.62f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(-1.6f, 2.15f));
            stats = new Label(root, "Stats", "", 0.5f, TextAnchor.MiddleCenter, Palette.UIIvory, new Vector2(-1.6f, 1.2f));

            float bx = -1.6f;
            retry = new GazeButton(root, "Retry", "btn_retry", new Vector2(bx, -0.25f), ButtonSize, alphaMat);
            respawn = new GazeButton(root, "Respawn", "btn_respawn", new Vector2(bx, -2.0f), ButtonSize, alphaMat);
            menu = new GazeButton(root, "Main Menu", "btn_mainmenu", new Vector2(bx, -3.75f), ButtonSize, alphaMat);
            buttons = new[] { retry, respawn, menu };

            // The respawn price sits in the right part of the respawn button, after a coin.
            var priceCoin = new UISprite("Price Coin", respawn.root, UISprite.Load("coin"), new Vector2(0.7f, 0.7f), alphaMat, 5)
            {
                Position = new Vector2(1.45f, 0f)
            };
            cost = new Label(respawn.root, "Price", "25", 0.55f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(1.9f, 0.02f), 24);

            // Coin bank on the panel's right side.
            sideBg = new UISprite("Coin Bank", root, UISprite.Load("panel_small"), new Vector2(4.3f, 3.6f), alphaMat, 1)
            {
                Position = new Vector2(Width * 0.5f - 2.85f, -1.0f)
            };
            coinCaption = new Label(root, "Coin Caption", "COINS", 0.42f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(Width * 0.5f - 2.85f, 0.15f));
            coinIcon = new UISprite("Coin Icon", root, UISprite.Load("coin"), new Vector2(1.1f, 1.1f), alphaMat, 3)
            {
                Position = new Vector2(Width * 0.5f - 3.95f, -1.35f)
            };
            coinCount = new Label(root, "Coin Count", "0", 0.75f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(Width * 0.5f - 3.25f, -1.3f));

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
                if (!value) { focus.Clear(); placed = false; }
            }
        }

        public Vector3 ButtonWorldCenter(int index) => root.TransformPoint(buttons[index].Center);

        public void SetContent(string why, float distance, int runCoins, int bank, int respawnCost, int respawnsUsed)
        {
            reason.Text = why.ToUpperInvariant();
            stats.Text = respawnsUsed > 0
                ? $"{distance:0} M      {runCoins} COINS THIS RUN      {respawnsUsed} RESPAWN{(respawnsUsed > 1 ? "S" : "")}"
                : $"{distance:0} M      {runCoins} COINS THIS RUN";
            cost.Text = respawnCost.ToString();
            coinCount.Text = bank.ToString("N0");
            respawn.Enabled = bank >= respawnCost;
            cost.Color = respawn.Enabled ? Palette.UIIvory : Palette.Danger;
        }

        /// <summary>
        /// Lazy follow. The panel holds its place until the head turns past the dead zone, then glides
        /// smoothly back in front and settles. Snap places it immediately (when it first appears).
        /// </summary>
        public void Follow(Transform head, float dt, bool snap)
        {
            if (head == null) return;
            Vector3 fwd = head.forward;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg, -MaxUp, MaxDown);
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 1e-4f) flat = head.up; // looking straight down: use the head's up as forward
            flat.y = 0f;
            Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Vector3 dir = yaw * Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
            Vector3 target = head.position + dir * FollowDistance;

            if (snap || !placed)
            {
                worldPos = target;
                placed = true;
                moving = false;
            }
            else
            {
                Vector3 current = (worldPos - head.position).normalized;
                float off = Vector3.Angle(current, dir);
                if (off > DeadZoneDegrees) moving = true;
                if (moving)
                {
                    worldPos = Vector3.Lerp(worldPos, target, 1f - Mathf.Exp(-FollowRate * dt));
                    if (off < SettleDegrees) moving = false;
                }
                // Hold the distance steady even while the head moves.
                Vector3 toPanel = worldPos - head.position;
                worldPos = head.position + toPanel.normalized * Mathf.Lerp(toPanel.magnitude, FollowDistance, 1f - Mathf.Exp(-4f * dt));
            }
            root.position = worldPos;
            Vector3 face = worldPos - head.position;
            if (face.sqrMagnitude > 1e-6f)
                root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(face, Vector3.up), snap ? 1f : 1f - Mathf.Exp(-8f * dt));
        }

        public GameOverChoice Tick(float dt, Ray? gaze, bool select, bool acceptInput)
        {
            time += dt;
            Vector2 point = default;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out point)
                && Mathf.Abs(point.x) <= Width * 0.5f && Mathf.Abs(point.y) <= Height * 0.5f;
            focus.Update(dt, onPanel ? point : (Vector2?)null, buttons);
            for (int i = 0; i < buttons.Length; i++) buttons[i].Refresh(focus.Focused == i, dt);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;
            coinIcon.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 2.2f) * 25f, 0f);

            if (!select || !acceptInput || focus.Focused < 0) return GameOverChoice.None;
            return focus.Focused switch
            {
                0 => GameOverChoice.Retry,
                1 => GameOverChoice.Respawn,
                _ => GameOverChoice.MainMenu
            };
        }
    }
}
