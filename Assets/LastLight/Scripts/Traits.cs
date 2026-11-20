using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum Trait { Visibility, Controls, Evasion }

    /// <summary>
    /// Vehicle traits bought in the garage, two levels each. Levels are bought once and apply to every vehicle
    /// that carries the traits: the Scout Jeep always does, and any other vehicle once its one-time transfer fee
    /// is paid. After that, later upgrades carry over to it automatically.
    /// </summary>
    public static class TraitCatalog
    {
        public const int Count = 3;
        public const int MaxLevel = 2;

        public static readonly Trait[] All = { Trait.Visibility, Trait.Controls, Trait.Evasion };

        /// <summary>Price of reaching <paramref name="level"/> (1 or 2). The second Controls and Evasion levels cost much more.</summary>
        public static int Cost(Trait t, int level) => (t, level) switch
        {
            (Trait.Visibility, 1) => 450,
            (Trait.Visibility, _) => 1000,
            (Trait.Controls, 1) => 400,
            (Trait.Controls, _) => 1600,
            (Trait.Evasion, 1) => 550,
            _ => 2200
        };

        /// <summary>One-time fee for a vehicle to carry the traits. The Scout Jeep carries them for free.</summary>
        public static int TransferFee(int vehicle) => vehicle switch
        {
            0 => 0,
            VehicleCatalog.Hauler => 150,
            VehicleCatalog.Rhino => 300,
            _ => 500
        };

        /// <summary>Gaze (scout light) radius multiplier: +15% at level 1, +30% at level 2.</summary>
        public static float VisibilityScale(int level) => level >= 2 ? 1.3f : level == 1 ? 1.15f : 1f;

        /// <summary>How much of the snow's slipperiness is cancelled: 15% at level 1, 50% at level 2.</summary>
        public static float ControlsStability(int level) => level >= 2 ? 0.5f : level == 1 ? 0.15f : 0f;

        /// <summary>Chance a crash into an obstacle without a shield or shovel costs no health: 15%, then 40%.</summary>
        public static float EvasionChance(int level) => level >= 2 ? 0.4f : level == 1 ? 0.15f : 0f;

        public static string Name(Trait t) => t switch
        {
            Trait.Visibility => "VISIBILITY",
            Trait.Controls => "CONTROLS",
            Trait.Evasion => "EVASION",
            _ => ""
        };

        public static string LevelText(Trait t, int level) => (t, level) switch
        {
            (Trait.Visibility, 1) => "LV1  +15% GAZE RADIUS",
            (Trait.Visibility, _) => "LV2  +30% GAZE RADIUS",
            (Trait.Controls, 1) => "LV1  15% STEADIER ON SNOW",
            (Trait.Controls, _) => "LV2  50% STEADIER ON SNOW",
            (Trait.Evasion, 1) => "LV1  15% CHANCE: NO CRASH DAMAGE",
            _ => "LV2  40% CHANCE: NO CRASH DAMAGE"
        };

        public static Color Tint(Trait t) => t switch
        {
            Trait.Visibility => new Color(1f, 0.72f, 0.3f, 1f),
            Trait.Controls => Palette.IceBlue,
            _ => Palette.RepairGreen
        };
    }

    /// <summary>
    /// The garage's Traits panel, floating above the garage. Three columns (Visibility, Controls, Evasion), each
    /// with two level pips and an Upgrade button. While driving a vehicle that doesn't carry the traits yet, the
    /// upgrades are locked and a Transfer button offers the one-time fee instead.
    /// </summary>
    public sealed class TraitsPanel
    {
        public static readonly Vector3 PanelPosition = new Vector3(-2.5f, 16.5f, 15f);
        public const float PanelWidth = 17f, PanelHeight = 8.4f;
        const float ColumnSpacing = 5.5f;
        static readonly Vector2 UpgradeSize = new Vector2(4.8f, 1.25f);
        static readonly Vector2 TransferSize = new Vector2(9.5f, 1.15f);

        public readonly Transform root;
        readonly Progress progress;
        readonly GazeButton[] upgrades = new GazeButton[TraitCatalog.Count];
        readonly Label[] upgradeText = new Label[TraitCatalog.Count];
        readonly Label[] upgradePrice = new Label[TraitCatalog.Count];
        readonly UISprite[] upgradeCoins = new UISprite[TraitCatalog.Count];
        readonly UISprite[,] pips = new UISprite[TraitCatalog.Count, TraitCatalog.MaxLevel];
        readonly Label[,] levelLines = new Label[TraitCatalog.Count, TraitCatalog.MaxLevel];
        readonly GazeButton transfer;
        readonly Label transferText, transferPrice, status, coinCount, feedback;
        readonly UISprite transferCoin, cursor;
        readonly List<GazeButton> focusables = new List<GazeButton>();
        readonly GazeFocus focus = new GazeFocus();
        float feedbackTime;

        public int Focused => focus.Focused;
        public Vector3 UpgradeWorldCenter(Trait t) => upgrades[(int)t].root.position;
        public Vector3 TransferWorldCenter => transfer.root.position;
        public bool TransferVisible => transfer.Visible;
        public string Feedback => feedback.Text;
        public string Status => status.Text;

        public TraitsPanel(Transform parent, Material alphaMat, Material addMat, Progress progress, Vector3 headLocal)
        {
            this.progress = progress;
            root = new GameObject("Traits Panel").transform;
            root.SetParent(parent, false);
            root.localPosition = PanelPosition;
            root.localRotation = Quaternion.LookRotation(PanelPosition - headLocal, Vector3.up);
            Color ivory = Palette.UIIvory, amber = Palette.UIAmber, dim = new Color(0.8f, 0.76f, 0.66f);
            float top = PanelHeight * 0.5f;

            new UISprite("Background", root, UISprite.Load("panel_wide"), new Vector2(PanelWidth, PanelHeight), alphaMat, 0);
            new Label(root, "Title", "TRAITS", 0.85f, TextAnchor.MiddleLeft, ivory, new Vector2(-PanelWidth * 0.5f + 0.9f, top - 0.85f), 22);
            status = new Label(root, "Status", "", 0.3f, TextAnchor.MiddleLeft, amber, new Vector2(-PanelWidth * 0.5f + 4.4f, top - 0.82f), 22);
            new UISprite("Coin", root, UISprite.Load("coin"), new Vector2(0.75f, 0.75f), alphaMat, 3) { Position = new Vector2(PanelWidth * 0.5f - 2.9f, top - 0.85f) };
            coinCount = new Label(root, "Coins", "0", 0.5f, TextAnchor.MiddleLeft, ivory, new Vector2(PanelWidth * 0.5f - 2.4f, top - 0.83f), 22);

            Texture2D dot = UISprite.Load("dot"), coin = UISprite.Load("coin");
            for (int i = 0; i < TraitCatalog.Count; i++)
            {
                Trait t = TraitCatalog.All[i];
                float x = (i - 1) * ColumnSpacing;
                new Label(root, "Name " + t, TraitCatalog.Name(t), 0.5f, TextAnchor.MiddleCenter, TraitCatalog.Tint(t), new Vector2(x, top - 2.0f), 22);
                for (int l = 0; l < TraitCatalog.MaxLevel; l++)
                {
                    pips[i, l] = new UISprite("Pip", root, dot, new Vector2(0.42f, 0.42f), alphaMat, 3) { Position = new Vector2(x - 0.32f + l * 0.64f, top - 2.65f) };
                    levelLines[i, l] = new Label(root, "Level " + (l + 1), TraitCatalog.LevelText(t, l + 1), 0.24f, TextAnchor.MiddleCenter, dim, new Vector2(x, top - 3.2f - l * 0.42f), 22);
                }
                upgrades[i] = new GazeButton(root, "Upgrade " + t, "card", new Vector2(x, top - 4.9f), UpgradeSize, alphaMat, 1);
                upgradeText[i] = new Label(upgrades[i].root, "Text", "", 0.36f, TextAnchor.MiddleLeft, ivory, new Vector2(-1.9f, 0.02f), 24);
                upgradeCoins[i] = new UISprite("Coin", upgrades[i].root, coin, new Vector2(0.5f, 0.5f), alphaMat, 4) { Position = new Vector2(0.15f, 0f) };
                upgradePrice[i] = new Label(upgrades[i].root, "Price", "", 0.36f, TextAnchor.MiddleLeft, ivory, new Vector2(0.5f, 0.02f), 24);
            }

            transfer = new GazeButton(root, "Transfer", "card", new Vector2(0f, -top + 1.05f), TransferSize, alphaMat, 1);
            transferText = new Label(transfer.root, "Text", "", 0.34f, TextAnchor.MiddleLeft, ivory, new Vector2(-4.2f, 0.02f), 24);
            transferCoin = new UISprite("Coin", transfer.root, coin, new Vector2(0.5f, 0.5f), alphaMat, 4) { Position = new Vector2(2.7f, 0f) };
            transferPrice = new Label(transfer.root, "Price", "", 0.34f, TextAnchor.MiddleLeft, ivory, new Vector2(3.05f, 0.02f), 24);
            feedback = new Label(root, "Feedback", "", 0.32f, TextAnchor.MiddleCenter, ivory, new Vector2(0f, -top + 2.0f), 22);

            cursor = new UISprite("Gaze Cursor", root, UISprite.Load("spot"), new Vector2(0.9f, 0.9f), addMat, 8) { Color = new Color(1f, 0.72f, 0.3f, 0.75f) };
            cursor.Visible = false;
            focusables.AddRange(upgrades);
            focusables.Add(transfer);
            Visible = false;
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set
            {
                if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value);
                if (!value) focus.Clear();
            }
        }

        /// <summary>Gaze focus and presses. Returns true if the gaze is on this panel and a press was handled here.</summary>
        public bool Tick(float dt, Ray? gaze, bool select, System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            Refresh();
            Vector2 point = default;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out point)
                && Mathf.Abs(point.x) <= PanelWidth * 0.5f && Mathf.Abs(point.y) <= PanelHeight * 0.5f;
            focus.Update(dt, onPanel ? point : (Vector2?)null, focusables);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;
            int f = focus.Focused;
            for (int i = 0; i < upgrades.Length; i++) upgrades[i].Refresh(f == i, dt);
            transfer.Refresh(f == upgrades.Length, dt);
            if (feedbackTime > 0f && (feedbackTime -= dt) <= 0f) feedback.Text = "";

            if (!select || f < 0) return false;
            if (f < upgrades.Length) Upgrade(TraitCatalog.All[f], log, out purchased, out refused);
            else Transfer(log, out purchased, out refused);
            return true;
        }

        /// <summary>Buys the next level of a trait for every vehicle that carries the traits.</summary>
        public void Upgrade(Trait t, System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            int level = progress.TraitLevel(t);
            string name = TraitCatalog.Name(t);
            if (level >= TraitCatalog.MaxLevel) { refused = true; Say($"{name} IS MAXED", log, null); return; }
            if (!progress.TraitsLinked(progress.Vehicle)) { refused = true; Say("TRANSFER THE TRAITS TO THIS VEHICLE FIRST", log, $"Traits: {VehicleCatalog.All[progress.Vehicle].name} hasn't paid the transfer fee"); return; }
            int cost = TraitCatalog.Cost(t, level + 1);
            if (!progress.TryUpgradeTrait(t)) { refused = true; Say("NOT ENOUGH COINS", log, $"Traits: {name.ToLowerInvariant()} level {level + 1} costs {cost}, only {progress.Coins} coins"); return; }
            purchased = true;
            Say($"{name} LEVEL {level + 1}", log, $"Traits: {name.ToLowerInvariant()} upgraded to level {level + 1} for {cost} coins, {progress.Coins} left");
        }

        /// <summary>Pays the driven vehicle's one-time fee to carry the traits.</summary>
        public void Transfer(System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            int v = progress.Vehicle;
            var def = VehicleCatalog.All[v];
            if (progress.TraitsLinked(v)) { Say("TRAITS ALREADY CARRIED", log, null); return; }
            int fee = TraitCatalog.TransferFee(v);
            if (!progress.TryLinkTraits(v)) { refused = true; Say("NOT ENOUGH COINS", log, $"Traits: transfer to {def.name} costs {fee}, only {progress.Coins} coins"); return; }
            purchased = true;
            Say($"TRAITS TRANSFERRED TO {def.name}", log, $"Traits: transferred to {def.name} for {fee} coins, {progress.Coins} left");
        }

        void Say(string text, System.Action<string> log, string logLine)
        {
            feedback.Text = text;
            feedbackTime = 2.5f;
            if (logLine != null) log?.Invoke(logLine);
        }

        void Refresh()
        {
            coinCount.Text = progress.Coins.ToString("N0");
            int v = progress.Vehicle;
            bool linkedNow = progress.TraitsLinked(v);
            string vehicle = VehicleCatalog.All[v].name;
            status.Text = linkedNow ? $"ACTIVE ON {vehicle}" : $"NOT CARRIED BY {vehicle}";
            status.Color = linkedNow ? Palette.Route : Palette.Danger;
            for (int i = 0; i < upgrades.Length; i++)
            {
                Trait t = TraitCatalog.All[i];
                int level = progress.TraitLevel(t);
                for (int l = 0; l < TraitCatalog.MaxLevel; l++)
                {
                    bool have = l < level;
                    Color tint = TraitCatalog.Tint(t);
                    pips[i, l].Color = have ? tint : new Color(0.28f, 0.28f, 0.28f, 1f);
                    levelLines[i, l].Alpha = have ? 1f : 0.55f;
                }
                bool maxed = level >= TraitCatalog.MaxLevel;
                int cost = maxed ? 0 : TraitCatalog.Cost(t, level + 1);
                bool afford = progress.CanAfford(cost);
                upgradeText[i].Text = maxed ? "MAXED" : !linkedNow ? "LOCKED" : $"BUY LV{level + 1}";
                upgradeText[i].Color = maxed || !linkedNow ? new Color(0.7f, 0.68f, 0.6f) : Palette.UIIvory;
                upgradeCoins[i].Visible = !maxed && linkedNow;
                upgradePrice[i].Text = !maxed && linkedNow ? cost.ToString("N0") : "";
                upgradePrice[i].Color = afford ? Palette.UIIvory : Palette.Danger;
                upgrades[i].Enabled = !maxed && linkedNow && afford;
            }
            transfer.Visible = !linkedNow;
            if (!linkedNow)
            {
                int fee = TraitCatalog.TransferFee(v);
                transferText.Text = "TRANSFER TRAITS TO THIS VEHICLE";
                transferPrice.Text = fee.ToString("N0");
                transferPrice.Color = progress.CanAfford(fee) ? Palette.UIIvory : Palette.Danger;
                transfer.Enabled = progress.CanAfford(fee);
            }
        }
    }
}
