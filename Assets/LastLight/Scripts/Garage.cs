using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum GarageAction { None, Back, Changed }

    /// <summary>
    /// The garage screen. A mini floating garage takes the forest island's place, with the chosen vehicle
    /// turning on a turntable inside. Beside it floats a scrolling panel of vehicle cards, each with its
    /// own small turning 3D model, plus colour swatches and an Unlock / Drive button.
    /// All models are placeholders built from boxes.
    /// </summary>
    public sealed class Garage
    {
        // ----- Layout, in diorama units -----
        public static readonly Vector3 StagePosition = new Vector3(-2.5f, 0.5f, 4f);
        const float StageScale = 0.55f;             // a mini garage, about the island's size
        public static readonly Vector3 PanelPosition = new Vector3(14.5f, 9.5f, 1.5f);
        const float StageVehicleScale = 2.1f;

        // ----- Panel layout, in panel units -----
        public const float PanelWidth = 12.5f;
        public const float PanelHeight = 18f;
        static readonly Vector2 CardSize = new Vector2(11f, 3.0f);
        const float CardSpacing = 3.25f;
        const float ListTop = 3.3f;                  // centre of the first visible card slot
        const int VisibleCards = 2;
        const float ModelScale = 1.05f;

        class Card
        {
            public Transform root;
            public GazeButton button;
            public Label name, status;
            public UISprite lockIcon, coin;
            public Transform model;
            public MeshFilter modelFilter;
        }

        public readonly Transform stageRoot, panelRoot;
        public int Preview { get; private set; }
        public float Scroll => scroll;

        readonly Progress progress;
        readonly Transform turntable;
        readonly MeshFilter stageVehicle;
        readonly Transform lockedSign;
        readonly Card[] cards;
        readonly GazeButton up, down, action, back;
        readonly GazeButton[] swatches = new GazeButton[4];
        readonly UISprite[] swatchFills = new UISprite[4];
        readonly UISprite[] swatchLocks = new UISprite[4];
        readonly UISprite swatchRing, cursor, actionCoin;
        readonly Label coinCount, colorName, actionCost, vehicleName, blurb;
        readonly List<GazeButton> focusables = new List<GazeButton>();
        readonly GazeFocus focus = new GazeFocus();
        float scroll, scrollTarget, time;
        int previewColor;

        public int Focused => focus.Focused;

        public Garage(Transform parent, Material solidMat, Material glowMat, Material alphaMat, Material addMat, Progress progress, Vector3 headLocal)
        {
            this.progress = progress;

            // ---------- The floating garage ----------
            stageRoot = new GameObject("Garage Stage").transform;
            stageRoot.SetParent(parent, false);
            stageRoot.localPosition = StagePosition;
            stageRoot.localRotation = Quaternion.Euler(0f, -12f, 0f);
            stageRoot.localScale = Vector3.one * StageScale;
            MeshBuilder.Spawn("Garage", stageRoot, BuildGarageMesh(), solidMat);
            MeshBuilder.Spawn("Garage Lights", stageRoot, BuildGarageLights(), glowMat);

            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(stageRoot, false);
            turntable.localPosition = new Vector3(0f, 0.25f, 0.5f);
            var mb = new MeshBuilder();
            mb.Cylinder(Vector3.zero, 4.4f, 0.22f, 32, Palette.Metal * 0.8f);
            mb.Ring(new Vector3(0f, 0.23f, 0f), 3.9f, 4.2f, 32, Palette.UIAmber * 0.9f);
            MeshBuilder.Spawn("Turntable Plate", turntable, mb.ToMesh("LL_Turntable"), solidMat);
            var sv = MeshBuilder.Spawn("Display Vehicle", turntable, VehicleCatalog.GetMesh(0, 0), solidMat);
            sv.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            sv.transform.localScale = Vector3.one * StageVehicleScale;
            stageVehicle = sv.GetComponent<MeshFilter>();

            mb.Clear();
            mb.Ring(Vector3.zero, 1.2f, 1.45f, 24, new Color(1f, 0.45f, 0.3f, 0.9f));
            mb.Box(new Vector3(0f, 0f, 0f), new Vector3(1.9f, 0.12f, 0.02f), Quaternion.Euler(0f, 0f, 0f), new Color(1f, 0.45f, 0.3f, 0.9f));
            lockedSign = MeshBuilder.Spawn("Locked Sign", stageRoot, mb.ToMesh("LL_LockedSign"), glowMat).transform;
            lockedSign.localPosition = new Vector3(0f, 6.2f, 0.5f);

            // ---------- The side panel ----------
            panelRoot = new GameObject("Garage Panel").transform;
            panelRoot.SetParent(parent, false);
            panelRoot.localPosition = PanelPosition;
            panelRoot.localRotation = Quaternion.LookRotation(PanelPosition - headLocal, Vector3.up);
            new UISprite("Background", panelRoot, UISprite.Load("panel_tall"), new Vector2(PanelWidth, PanelHeight), alphaMat, 0);
            new UISprite("Title", panelRoot, UISprite.Load("title_garage"), new Vector2(7.5f, 7.5f * 160f / 1024f), alphaMat, 2)
            {
                Position = new Vector2(-1.6f, PanelHeight * 0.5f - 1.3f)
            };
            new UISprite("Coin", panelRoot, UISprite.Load("coin"), new Vector2(0.9f, 0.9f), alphaMat, 3) { Position = new Vector2(2.7f, PanelHeight * 0.5f - 1.3f) };
            coinCount = new Label(panelRoot, "Coins", "0", 0.6f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(3.25f, PanelHeight * 0.5f - 1.27f));

            up = new GazeButton(panelRoot, "Scroll Up", "btn_up", new Vector2(0f, ListTop + CardSpacing * 0.5f + 0.95f), new Vector2(1.25f, 1.25f), alphaMat);
            down = new GazeButton(panelRoot, "Scroll Down", "btn_down", new Vector2(0f, ListTop - CardSpacing * (VisibleCards - 0.5f) - 0.95f), new Vector2(1.25f, 1.25f), alphaMat);

            cards = new Card[VehicleCatalog.Count];
            for (int i = 0; i < cards.Length; i++)
            {
                var c = new Card { root = new GameObject("Card " + i).transform };
                c.root.SetParent(panelRoot, false);
                // The card's hit area lives in panel space; Refresh moves it along with the card.
                c.button = new GazeButton(panelRoot, "Card " + i, "card", Vector2.zero, CardSize, alphaMat, 1);
                c.name = new Label(c.root, "Name", VehicleCatalog.All[i].name, 0.62f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(-1.3f, 0.55f), 22);
                c.status = new Label(c.root, "Status", "", 0.46f, TextAnchor.MiddleLeft, Palette.UIAmber, new Vector2(-0.6f, -0.45f), 22);
                c.lockIcon = new UISprite("Lock", c.root, UISprite.Load("lock"), new Vector2(0.75f, 0.75f), alphaMat, 4) { Position = new Vector2(-1.0f, -0.45f) };
                c.coin = new UISprite("Coin", c.root, UISprite.Load("coin"), new Vector2(0.6f, 0.6f), alphaMat, 4) { Position = new Vector2(1.55f, -0.45f) };
                var model = MeshBuilder.Spawn("Model", c.root, VehicleCatalog.GetMesh(i, 0), solidMat);
                c.model = model.transform;
                c.model.localPosition = new Vector3(-3.6f, -0.75f, -1.3f);
                c.model.localScale = Vector3.one * ModelScale;
                c.modelFilter = model.GetComponent<MeshFilter>();
                cards[i] = c;
            }

            // Colour swatches.
            float swY = ListTop - CardSpacing * (VisibleCards - 0.5f) - 2.35f;
            new Label(panelRoot, "Colour Caption", "COLOUR", 0.46f, TextAnchor.MiddleLeft, Palette.UIAmber, new Vector2(-5.4f, swY + 0.02f));
            Texture2D dot = UISprite.Load("dot");
            for (int i = 0; i < swatches.Length; i++)
            {
                var pos = new Vector2(-1.7f + i * 1.55f, swY);
                swatches[i] = new GazeButton(panelRoot, "Swatch " + i, "ring", pos, new Vector2(1.25f, 1.25f), alphaMat, 3);
                swatchFills[i] = new UISprite("Fill", swatches[i].root, dot, new Vector2(1.0f, 1.0f), alphaMat, 2);
                swatchLocks[i] = new UISprite("Lock", swatches[i].root, UISprite.Load("lock"), new Vector2(0.5f, 0.5f), alphaMat, 5);
            }
            swatchRing = new UISprite("Selected Colour", panelRoot, UISprite.Load("ring"), new Vector2(1.6f, 1.6f), alphaMat, 4) { Color = Palette.UIAmber };
            colorName = new Label(panelRoot, "Colour Name", "", 0.4f, TextAnchor.MiddleRight, Palette.UIIvory, new Vector2(5.5f, swY - 0.95f));

            vehicleName = new Label(panelRoot, "Vehicle", "", 0.5f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(-5.4f, swY - 0.95f));
            blurb = new Label(panelRoot, "Blurb", "", 0.36f, TextAnchor.MiddleLeft, new Color(0.8f, 0.76f, 0.66f), new Vector2(-5.4f, swY - 1.6f));

            float actY = swY - 3.0f;
            action = new GazeButton(panelRoot, "Action", "btn_drive", new Vector2(-1.4f, actY), new Vector2(6.6f, 1.65f), alphaMat);
            actionCoin = new UISprite("Price Coin", action.root, UISprite.Load("coin"), new Vector2(0.72f, 0.72f), alphaMat, 5) { Position = new Vector2(1.25f, 0f) };
            actionCost = new Label(action.root, "Price", "", 0.55f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(1.7f, 0.02f), 24);
            back = new GazeButton(panelRoot, "Back", "btn_back", new Vector2(4.1f, actY), new Vector2(3.4f, 1.65f * 3.4f / 6.6f * 1.55f), alphaMat);

            cursor = new UISprite("Gaze Cursor", panelRoot, UISprite.Load("spot"), new Vector2(0.9f, 0.9f), addMat, 8) { Color = new Color(1f, 0.72f, 0.3f, 0.75f) };
            cursor.Visible = false;

            focusables.Add(up);
            focusables.Add(down);
            foreach (var c in cards) focusables.Add(c.button);
            focusables.AddRange(swatches);
            focusables.Add(action);
            focusables.Add(back);

            Visible = false;
        }

        public bool Visible
        {
            get => stageRoot.gameObject.activeSelf;
            set
            {
                stageRoot.gameObject.SetActive(value);
                panelRoot.gameObject.SetActive(value);
                if (!value) focus.Clear();
            }
        }

        /// <summary>Opens on the vehicle being driven.</summary>
        public void Open()
        {
            Visible = true;
            SetPreview(progress.Vehicle);
            scroll = scrollTarget;
            Refresh(0f);
        }

        public Vector3 CardWorldCenter(int i) => cards[i].root.position;
        public Vector3 ActionWorldCenter => action.root.position;
        public Vector3 BackWorldCenter => back.root.position;
        public Vector3 SwatchWorldCenter(int i) => swatches[i].root.position;
        public Vector3 ScrollDownWorldCenter => down.root.position;
        public Vector3 LookPoint => Vector3.Lerp(stageRoot.position, panelRoot.position, 0.5f);
        public bool CardVisible(int i) => cards[i].button.Visible;

        void SetPreview(int vehicle)
        {
            Preview = Mathf.Clamp(vehicle, 0, VehicleCatalog.Count - 1);
            previewColor = progress.ColorOf(Preview);
            // Scroll so the previewed card is in view.
            float maxScroll = Mathf.Max(0, VehicleCatalog.Count - VisibleCards);
            if (Preview < scrollTarget) scrollTarget = Preview;
            if (Preview > scrollTarget + VisibleCards - 1) scrollTarget = Preview - VisibleCards + 1;
            scrollTarget = Mathf.Clamp(scrollTarget, 0f, maxScroll);
        }

        public void ScrollBy(int step)
        {
            float maxScroll = Mathf.Max(0, VehicleCatalog.Count - VisibleCards);
            scrollTarget = Mathf.Clamp(Mathf.Round(scrollTarget) + step, 0f, maxScroll);
        }

        public void PreviewNext(int step) => SetPreview((Preview + step + VehicleCatalog.Count) % VehicleCatalog.Count);

        /// <summary>Gaze focus, presses, scrolling and animation. Returns what the press did.</summary>
        public GarageAction Tick(float dt, Ray? gaze, bool select, System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            time += dt;
            scroll = Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-10f * dt));
            Refresh(dt);

            Vector2 point = default;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(panelRoot, gaze.Value, out point)
                && Mathf.Abs(point.x) <= PanelWidth * 0.5f && Mathf.Abs(point.y) <= PanelHeight * 0.5f;
            focus.Update(dt, onPanel ? point : (Vector2?)null, focusables);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;

            int f = focus.Focused;
            up.Refresh(f == 0, dt);
            down.Refresh(f == 1, dt);
            for (int i = 0; i < cards.Length; i++)
            {
                bool lit = f == 2 + i || Preview == i;
                cards[i].button.Refresh(lit, dt, cards[i].button.sprite.Alpha);
            }
            int swBase = 2 + cards.Length;
            bool owned = progress.IsUnlocked(Preview);
            for (int i = 0; i < swatches.Length; i++) swatches[i].Refresh(f == swBase + i, dt);
            action.Refresh(f == swBase + swatches.Length, dt);
            back.Refresh(f == swBase + swatches.Length + 1, dt);

            if (!select || f < 0) return GarageAction.None;
            if (f == 0) { ScrollBy(-1); return GarageAction.Changed; }
            if (f == 1) { ScrollBy(1); return GarageAction.Changed; }
            if (f < swBase)
            {
                SetPreview(f - 2);
                log?.Invoke($"Garage: viewing {VehicleCatalog.All[Preview].name}");
                return GarageAction.Changed;
            }
            if (f < swBase + swatches.Length)
            {
                int color = f - swBase;
                if (!owned) { refused = true; log?.Invoke("Garage: colours unlock with the vehicle"); return GarageAction.Changed; }
                previewColor = color;
                progress.SetColor(Preview, color);
                log?.Invoke($"Garage: {VehicleCatalog.All[Preview].name} painted {VehicleCatalog.All[Preview].colorNames[color]}");
                return GarageAction.Changed;
            }
            if (f == swBase + swatches.Length) return PressAction(log, out purchased, out refused);
            log?.Invoke("Garage: back to the menu");
            return GarageAction.Back;
        }

        /// <summary>Unlock if locked and affordable, otherwise drive the previewed vehicle.</summary>
        public GarageAction PressAction(System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            var def = VehicleCatalog.All[Preview];
            if (!progress.IsUnlocked(Preview))
            {
                if (progress.TryUnlock(Preview))
                {
                    purchased = true;
                    previewColor = progress.ColorOf(Preview);
                    log?.Invoke($"Garage: unlocked {def.name} for {def.cost} coins, {progress.Coins} left");
                }
                else
                {
                    refused = true;
                    log?.Invoke($"Garage: {def.name} costs {def.cost}, only {progress.Coins} coins");
                }
                return GarageAction.Changed;
            }
            if (progress.Vehicle != Preview)
            {
                progress.Equip(Preview);
                log?.Invoke($"Garage: now driving {def.name}");
            }
            return GarageAction.Changed;
        }

        void Refresh(float dt)
        {
            coinCount.Text = progress.Coins.ToString("N0");
            var def = VehicleCatalog.All[Preview];
            bool owned = progress.IsUnlocked(Preview);
            int shownColor = owned ? previewColor : 0;

            // Turntable vehicle.
            stageVehicle.sharedMesh = VehicleCatalog.GetMesh(Preview, shownColor);
            turntable.localRotation = Quaternion.Euler(0f, time * 28f + 150f, 0f);
            lockedSign.gameObject.SetActive(!owned);
            lockedSign.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2f) * 8f);
            lockedSign.localPosition = new Vector3(0f, 6.2f + Mathf.Sin(time * 1.6f) * 0.15f, 0.5f);

            // Cards: slide with the scroll, fade at the window's edges, models turn slowly.
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                float slot = i - scroll;
                float y = ListTop - slot * CardSpacing;
                c.root.localPosition = new Vector3(0f, y, 0f);
                c.button.Center = new Vector2(0f, y);
                float edge = Mathf.Min(slot + 0.5f, VisibleCards - 0.5f - slot);   // >0 inside the window
                float a = Mathf.Clamp01(edge * 2.5f);
                bool show = a > 0.02f;
                c.root.gameObject.SetActive(show);
                c.button.Visible = show;
                c.button.Focusable = a > 0.6f;
                c.button.sprite.Alpha = a;
                c.name.Alpha = a;
                c.status.Alpha = a;
                c.lockIcon.Alpha = a;
                c.coin.Alpha = a;
                c.model.localScale = Vector3.one * ModelScale * Mathf.SmoothStep(0f, 1f, a);
                c.model.localRotation = Quaternion.Euler(-18f, time * 40f + i * 90f, 0f);

                bool unlocked = progress.IsUnlocked(i);
                c.modelFilter.sharedMesh = VehicleCatalog.GetMesh(i, unlocked ? (i == Preview ? previewColor : progress.ColorOf(i)) : 0);
                c.lockIcon.Visible = !unlocked;
                c.coin.Visible = !unlocked;
                if (!unlocked)
                {
                    c.status.Text = VehicleCatalog.All[i].cost.ToString("N0");
                    c.status.Position = new Vector2(1.95f, -0.45f);
                    c.status.Color = progress.CanAfford(VehicleCatalog.All[i].cost) ? Palette.UIIvory : Palette.Danger;
                    c.name.Color = new Color(0.75f, 0.72f, 0.64f);
                }
                else
                {
                    c.status.Position = new Vector2(-1.3f, -0.45f);
                    c.status.Text = progress.Vehicle == i ? "DRIVING" : "OWNED";
                    c.status.Color = progress.Vehicle == i ? Palette.UIAmber : Palette.Route;
                    c.name.Color = Palette.UIIvory;
                }
            }
            up.Visible = scrollTarget > 0.01f;
            down.Visible = scrollTarget < VehicleCatalog.Count - VisibleCards - 0.01f;

            // Swatches.
            for (int i = 0; i < swatches.Length; i++)
            {
                bool exists = i < def.colors.Length;
                swatches[i].Visible = exists;
                if (!exists) continue;
                Color col = def.colors[i];
                // Swatches show display colours, so undo the linear conversion for the sprite.
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) col = col.gamma;
                col.a = owned ? 1f : 0.35f;
                swatchFills[i].Color = col;
                swatchLocks[i].Visible = !owned;
                swatches[i].Enabled = owned;
            }
            swatchRing.Visible = owned;
            if (owned) swatchRing.Position = swatches[Mathf.Clamp(previewColor, 0, swatches.Length - 1)].Center;
            colorName.Text = owned ? def.colorNames[Mathf.Clamp(previewColor, 0, def.colorNames.Length - 1)].ToUpperInvariant() : "";
            vehicleName.Text = def.name;
            blurb.Text = def.blurb;

            // Action button: Unlock (with price), Drive This, or Equipped.
            if (!owned)
            {
                SetActionArt("btn_unlock");
                actionCoin.Visible = true;
                actionCost.Visible = true;
                actionCost.Text = def.cost.ToString("N0");
                bool afford = progress.CanAfford(def.cost);
                actionCost.Color = afford ? Palette.UIIvory : Palette.Danger;
                action.Enabled = afford;
            }
            else
            {
                SetActionArt(progress.Vehicle == Preview ? "btn_equipped" : "btn_drive");
                actionCoin.Visible = false;
                actionCost.Visible = false;
                action.Enabled = progress.Vehicle != Preview;
            }
        }

        string actionArt;
        void SetActionArt(string name)
        {
            if (actionArt == name) return;
            actionArt = name;
            action.SetArt(name);
        }

        // ---------- Garage geometry (placeholder) ----------

        static Mesh BuildGarageMesh()
        {
            var mb = new MeshBuilder();
            const float W = 9.5f, D = 7f, H = 7.5f;
            Color concrete = new Color(0.42f, 0.42f, 0.4f), wall = new Color(0.36f, 0.3f, 0.26f), trim = Palette.Ochre * 0.8f;
            // Floating slab with a rocky underside, like the island it replaces.
            mb.Box(new Vector3(0f, -0.6f, 0f), new Vector3(W * 2f + 1f, 1.2f, D * 2f + 1f), concrete);
            mb.Box(new Vector3(0f, 0.02f, -D - 0.2f), new Vector3(W * 2f + 1f, 0.06f, 0.3f), trim);   // painted lip along the open front
            mb.Cylinder(new Vector3(0f, -5.2f, 0f), 3.2f, 4.6f, 10, Palette.Stone, 8.5f, false);
            var rng = new System.Random(17);
            for (int i = 0; i < 12; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 3f + (float)rng.NextDouble() * 5f;
                mb.Rock(new Vector3(Mathf.Cos(a) * r, -1.8f - (float)rng.NextDouble() * 3f, Mathf.Sin(a) * r * 0.7f), 0.8f + (float)rng.NextDouble(), rng.Next(), Palette.Stone * 1.2f);
            }
            // Back wall and side walls; the front and most of the roof stay open so you can see in.
            mb.Box(new Vector3(0f, H * 0.5f, D), new Vector3(W * 2f, H, 0.5f), wall);
            mb.Box(new Vector3(-W, H * 0.5f, 0f), new Vector3(0.5f, H, D * 2f), wall * 0.9f);
            mb.Box(new Vector3(W, H * 0.5f, 0f), new Vector3(0.5f, H, D * 2f), wall * 0.9f);
            // Roof over the back third only, so you can look down into the garage.
            mb.Box(new Vector3(0f, H + 0.2f, D - 2f), new Vector3(W * 2f + 0.6f, 0.4f, 4.6f), Palette.Cabin);
            mb.Box(new Vector3(0f, H + 0.15f, D - 5.4f), new Vector3(W * 2f + 0.6f, 0.3f, 0.3f), Palette.Trunk);
            // Door posts at the open front, with low walls either side.
            mb.Box(new Vector3(-W + 0.4f, H * 0.5f, -D), new Vector3(0.7f, H, 0.7f), trim);
            mb.Box(new Vector3(W - 0.4f, H * 0.5f, -D), new Vector3(0.7f, H, 0.7f), trim);
            // Hazard stripes on the floor.
            for (int i = 0; i < 9; i++) mb.Box(new Vector3(-W + 1.5f + i * 2.1f, 0.02f, -D + 0.9f), new Vector3(1f, 0.04f, 0.5f), Palette.ShovelYellow);
            // Workbench, tool wall, tyre stack, oil drums.
            mb.Box(new Vector3(-6.2f, 1.0f, D - 1.2f), new Vector3(4.5f, 0.2f, 1.5f), Palette.Trunk * 1.2f);
            mb.Box(new Vector3(-8.0f, 0.5f, D - 1.2f), new Vector3(0.2f, 1f, 1.3f), Palette.Trunk);
            mb.Box(new Vector3(-4.4f, 0.5f, D - 1.2f), new Vector3(0.2f, 1f, 1.3f), Palette.Trunk);
            mb.Box(new Vector3(-6.2f, 3.2f, D - 0.3f), new Vector3(4.5f, 2.6f, 0.1f), new Color(0.25f, 0.27f, 0.25f));
            for (int i = 0; i < 6; i++) mb.Box(new Vector3(-7.8f + i * 0.65f, 3.2f + (i % 2) * 0.4f, D - 0.4f), new Vector3(0.12f, 1.1f, 0.1f), Palette.Metal * 1.3f);
            for (int i = 0; i < 4; i++) mb.Cylinder(new Vector3(7.2f, i * 0.42f, D - 1.6f), 0.85f, 0.4f, 10, Palette.Tyre, 0.85f);
            mb.Cylinder(new Vector3(7.6f, 0f, -3.5f), 0.6f, 1.4f, 10, Palette.Tail * 0.8f);
            mb.Cylinder(new Vector3(6.4f, 0f, -4.3f), 0.6f, 1.4f, 10, Palette.ShieldBlue * 0.6f);
            return mb.ToMesh("LL_Garage");
        }

        static Mesh BuildGarageLights()
        {
            var mb = new MeshBuilder();
            Color lamp = new Color(1f, 0.85f, 0.55f, 0.95f);
            mb.Box(new Vector3(-4.5f, 7.2f, 3.5f), new Vector3(3f, 0.12f, 0.3f), lamp);
            mb.Box(new Vector3(4.5f, 7.2f, 3.5f), new Vector3(3f, 0.12f, 0.3f), lamp);
            mb.Box(new Vector3(0f, 7.2f, 0f), new Vector3(3.5f, 0.12f, 0.3f), lamp);
            mb.SoftDisc(new Vector3(0f, 0.3f, 0.5f), 6f, 32, new Color(1f, 0.75f, 0.4f, 0.22f), new Color(1f, 0.6f, 0.3f, 0f));
            return mb.ToMesh("LL_GarageLights");
        }
    }
}
