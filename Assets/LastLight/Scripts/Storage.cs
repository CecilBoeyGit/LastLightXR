using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The vehicle storage compartment: up to three slots (the first free), each holding one powerup bought in
    /// the garage. In a run they float on the player's right, and looking at one for a second uses it.
    /// </summary>
    public static class StorageCatalog
    {
        public const int MaxSlots = 4;
        public const float UseSeconds = 1f;

        /// <summary>Items the garage sells, in shop order.</summary>
        public static readonly PickupKind[] Items = { PickupKind.Shovel, PickupKind.Booster, PickupKind.Turret, PickupKind.Flashlight, PickupKind.Repair };

        public static bool IsItem(int kind)
        {
            foreach (var k in Items) if ((int)k == kind) return true;
            return false;
        }

        /// <summary>Shop prices. Repairs cost the most.</summary>
        public static int Price(PickupKind item) => item switch
        {
            PickupKind.Shovel => 180,
            PickupKind.Booster => 120,
            PickupKind.Turret => 270,
            PickupKind.Flashlight => 150,
            PickupKind.Repair => 420,
            _ => 9999
        };

        /// <summary>Cost to unlock slot index <paramref name="slot"/> (0-based). Slot 0 is free; slot 3 needs a second vehicle.</summary>
        public static int SlotCost(int slot) => slot switch { 0 => 0, 1 => 300, 2 => 600, _ => 900 };

        public static string Name(PickupKind item) => item switch
        {
            PickupKind.Shovel => "SHOVEL",
            PickupKind.Booster => "SPEED BOOST",
            PickupKind.Turret => "TURRET",
            PickupKind.Flashlight => "FLASHLIGHT",
            PickupKind.Repair => "REPAIRS",
            _ => item.ToString().ToUpperInvariant()
        };

        public static string Blurb(PickupKind item) => item switch
        {
            PickupKind.Shovel => "PLOUGHS THROUGH 5 ROCKS OR TREES",
            PickupKind.Booster => "5 S OF SPEED, PULLS AWAY FROM THE HORDE",
            PickupKind.Turret => "10 S OF ROOF GUN AGAINST SPITTERS",
            PickupKind.Flashlight => "5 S OF LIGHT AHEAD, CUTS THROUGH FOG",
            PickupKind.Repair => "FIXES 1 HEALTH BAR ON THE SPOT",
            _ => ""
        };

        public static Color Tint(PickupKind item) => item switch
        {
            PickupKind.Shovel => Palette.ShovelYellow,
            PickupKind.Booster => Palette.BoostOrange,
            PickupKind.Turret => Palette.TurretRed,
            PickupKind.Flashlight => Palette.LightBeam,
            PickupKind.Repair => Palette.RepairGreen,
            _ => Palette.UIIvory
        };
    }

    /// <summary>
    /// The garage's storage panel, floating on the other side of the garage from the vehicle panel.
    /// Four compartment slots across the top (pinch a locked one to unlock it; the fourth needs a second
    /// vehicle), then the item shop: pinch an item to buy it into the first empty slot.
    /// </summary>
    public sealed class StoragePanel
    {
        public static readonly Vector3 PanelPosition = new Vector3(-17.5f, 9.5f, 1.5f);
        public const float PanelWidth = 12.5f, PanelHeight = 20f;
        const float SlotY = 5.0f, SlotSpacing = 2.9f;
        static readonly Vector2 SlotSize = new Vector2(2.4f, 2.4f);
        static readonly Vector2 CardSize = new Vector2(11f, 2.0f);
        const float FirstCardY = 1.0f, CardSpacing = 2.15f;

        public readonly Transform root;
        readonly Progress progress;
        readonly GazeButton[] slots = new GazeButton[StorageCatalog.MaxSlots];
        readonly MeshFilter[] slotIcons = new MeshFilter[StorageCatalog.MaxSlots];
        readonly UISprite[] slotBadges = new UISprite[StorageCatalog.MaxSlots];
        readonly UISprite[] slotLocks = new UISprite[StorageCatalog.MaxSlots];
        readonly UISprite[] slotCoins = new UISprite[StorageCatalog.MaxSlots];
        readonly Label[] slotLabels = new Label[StorageCatalog.MaxSlots];
        readonly GazeButton[] cards = new GazeButton[StorageCatalog.Items.Length];
        readonly Label[] cardPrices = new Label[StorageCatalog.Items.Length];
        readonly Transform[] cardIcons = new Transform[StorageCatalog.Items.Length];
        readonly Label coinCount, feedback;
        readonly UISprite cursor;
        readonly List<GazeButton> focusables = new List<GazeButton>();
        readonly GazeFocus focus = new GazeFocus();
        float time, feedbackTime;

        public int Focused => focus.Focused;
        public Vector3 SlotWorldCenter(int i) => slots[i].root.position;
        public Vector3 CardWorldCenter(int i) => cards[i].root.position;
        public string Feedback => feedback.Text;

        public StoragePanel(Transform parent, Material solidMat, Material glowMat, Material alphaMat, Material addMat, Progress progress, Vector3 headLocal)
        {
            this.progress = progress;
            root = new GameObject("Storage Panel").transform;
            root.SetParent(parent, false);
            root.localPosition = PanelPosition;
            root.localRotation = Quaternion.LookRotation(PanelPosition - headLocal, Vector3.up);
            Color ivory = Palette.UIIvory, amber = Palette.UIAmber;

            new UISprite("Background", root, UISprite.Load("panel_tall"), new Vector2(PanelWidth, PanelHeight), alphaMat, 0);
            new Label(root, "Title", "STORAGE", 1.05f, TextAnchor.MiddleLeft, ivory, new Vector2(-5.3f, PanelHeight * 0.5f - 1.35f), 22);
            new UISprite("Coin", root, UISprite.Load("coin"), new Vector2(0.9f, 0.9f), alphaMat, 3) { Position = new Vector2(2.7f, PanelHeight * 0.5f - 1.3f) };
            coinCount = new Label(root, "Coins", "0", 0.6f, TextAnchor.MiddleLeft, ivory, new Vector2(3.25f, PanelHeight * 0.5f - 1.27f));
            new Label(root, "Slots Caption", "COMPARTMENT  -  LOOK AT AN ITEM FOR 1 S IN A RUN TO USE IT", 0.3f, TextAnchor.MiddleLeft, amber, new Vector2(-5.3f, SlotY + 2.0f), 22);

            Texture2D lockTex = UISprite.Load("lock"), coinTex = UISprite.Load("coin");
            for (int i = 0; i < slots.Length; i++)
            {
                var pos = new Vector2((i - 1.5f) * SlotSpacing, SlotY);
                slots[i] = new GazeButton(root, "Slot " + i, "ring", pos, SlotSize, alphaMat, 3);
                new UISprite("Slot Back", slots[i].root, UISprite.Load("dot"), SlotSize * 0.86f, alphaMat, 2) { Color = new Color(0.06f, 0.08f, 0.1f, 0.95f) };
                var icon = MeshBuilder.Spawn("Item", slots[i].root, Pickup.IconMesh(PickupKind.Shovel), solidMat);
                icon.transform.localPosition = new Vector3(0f, 0f, -0.6f);
                icon.transform.localScale = Vector3.one * 0.85f;
                slotIcons[i] = icon.GetComponent<MeshFilter>();
                slotBadges[i] = new UISprite("Badge", slots[i].root, null, SlotSize * 0.72f, alphaMat, 4);
                slotBadges[i].Visible = false;
                slotLocks[i] = new UISprite("Lock", slots[i].root, lockTex, new Vector2(1.1f, 1.1f), alphaMat, 5);
                slotCoins[i] = new UISprite("Price Coin", root, coinTex, new Vector2(0.5f, 0.5f), alphaMat, 4) { Position = pos + new Vector2(-0.55f, -1.6f) };
                slotLabels[i] = new Label(root, "Slot Label " + i, "", 0.32f, TextAnchor.MiddleCenter, ivory, pos + new Vector2(0f, -1.55f), 22);
            }

            new Label(root, "Shop Caption", "BUY ITEMS", 0.5f, TextAnchor.MiddleLeft, amber, new Vector2(-5.3f, FirstCardY + 1.75f), 22);
            feedback = new Label(root, "Feedback", "", 0.36f, TextAnchor.MiddleRight, ivory, new Vector2(5.3f, FirstCardY + 1.75f), 22);
            for (int i = 0; i < cards.Length; i++)
            {
                PickupKind item = StorageCatalog.Items[i];
                var pos = new Vector2(0f, FirstCardY - i * CardSpacing);
                cards[i] = new GazeButton(root, "Item " + item, "card", pos, CardSize, alphaMat, 1);
                Texture2D badge = Pickup.IconTexture(item);
                if (badge != null)
                {
                    var b = new UISprite("Badge", cards[i].root, badge, new Vector2(1.75f, 1.75f), alphaMat, 4) { Position = new Vector2(-4.35f, 0f) };
                    cardIcons[i] = b.transform;
                }
                else
                {
                    var icon = MeshBuilder.Spawn("Icon", cards[i].root, Pickup.IconMesh(item), solidMat);
                    icon.transform.localPosition = new Vector3(-4.35f, 0f, -0.6f);
                    icon.transform.localScale = Vector3.one * 0.62f;
                    cardIcons[i] = icon.transform;
                }
                new Label(cards[i].root, "Name", StorageCatalog.Name(item), 0.5f, TextAnchor.MiddleLeft, ivory, new Vector2(-3.4f, 0.32f), 22);
                new Label(cards[i].root, "Blurb", StorageCatalog.Blurb(item), 0.25f, TextAnchor.MiddleLeft, new Color(0.8f, 0.76f, 0.66f), new Vector2(-3.4f, -0.36f), 22);
                new UISprite("Coin", cards[i].root, coinTex, new Vector2(0.6f, 0.6f), alphaMat, 4) { Position = new Vector2(3.55f, 0.3f) };
                cardPrices[i] = new Label(cards[i].root, "Price", StorageCatalog.Price(item).ToString(), 0.5f, TextAnchor.MiddleLeft, ivory, new Vector2(3.95f, 0.32f), 24);
            }

            cursor = new UISprite("Gaze Cursor", root, UISprite.Load("spot"), new Vector2(0.9f, 0.9f), addMat, 8) { Color = new Color(1f, 0.72f, 0.3f, 0.75f) };
            cursor.Visible = false;
            focusables.AddRange(slots);
            focusables.AddRange(cards);
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

        /// <summary>Gaze focus and presses. Returns true if the gaze is on this panel (so the press was handled here).</summary>
        public bool Tick(float dt, Ray? gaze, bool select, System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            time += dt;
            Refresh();

            Vector2 point = default;
            bool onPanel = gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out point)
                && Mathf.Abs(point.x) <= PanelWidth * 0.5f && Mathf.Abs(point.y) <= PanelHeight * 0.5f;
            focus.Update(dt, onPanel ? point : (Vector2?)null, focusables);
            cursor.Visible = onPanel;
            if (onPanel) cursor.Position = point;
            int f = focus.Focused;
            for (int i = 0; i < slots.Length; i++) slots[i].Refresh(f == i, dt);
            for (int i = 0; i < cards.Length; i++) cards[i].Refresh(f == slots.Length + i, dt);
            if (feedbackTime > 0f && (feedbackTime -= dt) <= 0f) feedback.Text = "";

            if (!select || f < 0) return onPanel && f >= 0 && select;
            if (f < slots.Length)
            {
                if (f < progress.StorageSlots) { Say(progress.SlotItem(f) == null ? "EMPTY: BUY AN ITEM BELOW" : "USE IT IN A RUN", log, null); return true; }
                if (f != progress.StorageSlots) { refused = true; Say("UNLOCK THE SLOTS IN ORDER", log, "Storage: slots unlock in order"); return true; }
                if (f == StorageCatalog.MaxSlots - 1 && !progress.FourthSlotAllowed) { refused = true; Say("UNLOCK A 2ND VEHICLE FIRST", log, "Storage: the 4th slot needs a second vehicle"); return true; }
                int cost = StorageCatalog.SlotCost(f);
                if (progress.TryUnlockSlot())
                {
                    purchased = true;
                    Say($"SLOT {f + 1} UNLOCKED", log, $"Storage: unlocked slot {f + 1} for {cost} coins, {progress.Coins} left");
                }
                else
                {
                    refused = true;
                    Say("NOT ENOUGH COINS", log, $"Storage: slot {f + 1} costs {cost}, only {progress.Coins} coins");
                }
                return true;
            }
            PickupKind item = StorageCatalog.Items[f - slots.Length];
            BuyItem(item, log, out purchased, out refused);
            return true;
        }

        /// <summary>Buys an item into the first free slot, if there's room and enough coins.</summary>
        public void BuyItem(PickupKind item, System.Action<string> log, out bool purchased, out bool refused)
        {
            purchased = refused = false;
            int price = StorageCatalog.Price(item);
            if (progress.FreeSlot() < 0) { refused = true; Say("COMPARTMENT FULL", log, $"Storage: no free slot for a {StorageCatalog.Name(item).ToLowerInvariant()}"); return; }
            int slot = progress.TryBuyItem(item);
            if (slot < 0) { refused = true; Say("NOT ENOUGH COINS", log, $"Storage: {StorageCatalog.Name(item).ToLowerInvariant()} costs {price}, only {progress.Coins} coins"); return; }
            purchased = true;
            Say($"{StorageCatalog.Name(item)} STORED", log, $"Storage: bought a {StorageCatalog.Name(item).ToLowerInvariant()} for {price} coins into slot {slot + 1}, {progress.Coins} left");
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
            bool full = progress.FreeSlot() < 0;
            for (int i = 0; i < slots.Length; i++)
            {
                bool open = i < progress.StorageSlots;
                var item = progress.SlotItem(i);
                Texture2D badge = item.HasValue ? Pickup.IconTexture(item.Value) : null;
                slotIcons[i].gameObject.SetActive(item.HasValue && badge == null);
                slotBadges[i].Visible = badge != null;
                if (badge != null) slotBadges[i].Texture = badge;
                else if (item.HasValue)
                {
                    slotIcons[i].sharedMesh = Pickup.IconMesh(item.Value);
                    slotIcons[i].transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 1.4f + i) * 25f, 0f);
                }
                slotLocks[i].Visible = !open;
                bool next = i == progress.StorageSlots;
                bool barred = i == StorageCatalog.MaxSlots - 1 && !progress.FourthSlotAllowed;
                slotCoins[i].Visible = !open && next && !barred;
                slots[i].Enabled = open || next;
                if (open) { slotLabels[i].Text = item.HasValue ? StorageCatalog.Name(item.Value) : "EMPTY"; slotLabels[i].Position = slots[i].Center + new Vector2(0f, -1.6f); slotLabels[i].Color = item.HasValue ? Palette.UIIvory : new Color(0.6f, 0.58f, 0.52f); }
                else
                {
                    int cost = StorageCatalog.SlotCost(i);
                    slotLabels[i].Text = barred ? "2ND VEHICLE" : next ? cost.ToString("N0") : "LOCKED";
                    slotLabels[i].Position = slots[i].Center + new Vector2(next ? 0.2f : 0f, -1.6f);
                    slotLabels[i].Color = next && !progress.CanAfford(cost) ? Palette.Danger : new Color(0.75f, 0.72f, 0.64f);
                }
            }
            for (int i = 0; i < cards.Length; i++)
            {
                PickupKind item = StorageCatalog.Items[i];
                bool afford = progress.CanAfford(StorageCatalog.Price(item));
                cards[i].Enabled = afford && !full;
                cardPrices[i].Color = afford ? Palette.UIIvory : Palette.Danger;
                if (Pickup.IconTexture(item) == null) cardIcons[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 1.2f + i * 0.8f) * 25f, 0f);
            }
        }
    }

    /// <summary>
    /// The compartment in a run: a 2 x 2 grid of slots built into the dashboard, on the right of the health
    /// dial. Each shows its item; look at a stored item for a second (a ring fills round it) and it's used as if
    /// collected. Its own backing plate is depth-tested, so the 3D item icons show in front of it.
    /// </summary>
    public sealed class StorageRack
    {
        const float SlotRadius = 1.2f;
        const float HitPadding = 0.35f, KeepPadding = 0.8f;
        const float LoseGrace = 0.2f;       // a blink or a twitch of the eyes off the slot doesn't reset it
        static readonly Vector2[] SlotOffsets = { new Vector2(-1.45f, 1.6f), new Vector2(1.45f, 1.6f), new Vector2(-1.45f, -1.6f), new Vector2(1.45f, -1.6f) };

        public readonly Transform root;
        readonly Progress progress;
        readonly Transform[] slots = new Transform[StorageCatalog.MaxSlots];
        readonly MeshFilter[] icons = new MeshFilter[StorageCatalog.MaxSlots];
        readonly UISprite[] badges = new UISprite[StorageCatalog.MaxSlots];
        readonly UISprite[] rings = new UISprite[StorageCatalog.MaxSlots];
        readonly UISprite[] locks = new UISprite[StorageCatalog.MaxSlots];
        readonly UISprite[] flashes = new UISprite[StorageCatalog.MaxSlots];
        readonly Label[] labels = new Label[StorageCatalog.MaxSlots];
        readonly MeshRenderer[] dwellRings = new MeshRenderer[StorageCatalog.MaxSlots];
        readonly Mesh[] dwellMeshes = new Mesh[StorageCatalog.MaxSlots];
        readonly float[] dwell = new float[StorageCatalog.MaxSlots];
        readonly float[] flash = new float[StorageCatalog.MaxSlots];
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        float time, lost;

        /// <summary>The slot the eyes are on, or -1.</summary>
        public int Looking { get; private set; } = -1;
        public float Dwell(int slot) => dwell[slot];
        public Vector3 SlotWorldCenter(int i) => slots[i].position;

        /// <param name="parent">The dashboard root; the rack shares its plane.</param>
        /// <param name="center">Where the grid sits on the dashboard, in dashboard units.</param>
        public StorageRack(Transform parent, Vector2 center, Material iconMat, Material glowMat, Material alphaMat, Material addMat, Progress progress)
        {
            this.progress = progress;
            root = new GameObject("Storage Rack").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(center.x, center.y, 0f);
            root.localRotation = Quaternion.identity;
            Color ivory = Palette.UIIvory;
            Color ink = Palette.UIInk; ink.a = 0.97f;
            Texture2D white = UISprite.Load("white"), dot = UISprite.Load("dot"), ring = UISprite.Load("ring"), lockTex = UISprite.Load("lock"), spot = UISprite.Load("spot");
            new UISprite("Plate Rim", root, white, new Vector2(6.3f, 7.9f), alphaMat, 0) { Position = new Vector2(0f, 0.35f), Color = new Color(ivory.r, ivory.g, ivory.b, 0.85f) };
            new UISprite("Plate", root, white, new Vector2(5.95f, 7.55f), alphaMat, 1) { Position = new Vector2(0f, 0.35f), Color = ink };
            new Label(root, "Title", "STORAGE", 0.4f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(0f, 3.55f), 26, true);
            for (int i = 0; i < slots.Length; i++)
            {
                var t = new GameObject("Slot " + i).transform;
                t.SetParent(root, false);
                t.localPosition = new Vector3(SlotOffsets[i].x, SlotOffsets[i].y, 0f);
                slots[i] = t;
                new UISprite("Back", t, dot, Vector2.one * SlotRadius * 2f, alphaMat, 2) { Color = new Color(0.05f, 0.07f, 0.09f, 0.95f) };
                rings[i] = new UISprite("Rim", t, ring, Vector2.one * (SlotRadius * 2f + 0.2f), alphaMat, 3) { Color = ivory };
                var icon = MeshBuilder.Spawn("Item", t, Pickup.IconMesh(PickupKind.Shovel), iconMat);
                icon.transform.localPosition = new Vector3(0f, 0.12f, -0.7f);
                icon.transform.localScale = Vector3.one * 0.55f;
                icons[i] = icon.GetComponent<MeshFilter>();
                badges[i] = new UISprite("Badge", t, null, Vector2.one * SlotRadius * 1.6f, alphaMat, 4) { Position = new Vector2(0f, 0.1f) };
                badges[i].Visible = false;
                locks[i] = new UISprite("Lock", t, lockTex, new Vector2(0.8f, 0.8f), alphaMat, 4) { Color = new Color(1f, 1f, 1f, 0.6f) };
                flashes[i] = new UISprite("Use Flash", t, spot, Vector2.one * 3.6f, addMat, 5);
                labels[i] = new Label(t, "Label", "", 0.24f, TextAnchor.MiddleCenter, ivory, new Vector2(0f, -SlotRadius + 0.32f), 26, true);
                dwellMeshes[i] = new Mesh { name = "LL_StorageDwell_" + i };
                dwellRings[i] = MeshBuilder.Spawn("Dwell", t, dwellMeshes[i], glowMat);
                dwellRings[i].transform.localPosition = new Vector3(0f, 0f, -0.3f);
                dwellRings[i].gameObject.SetActive(false);
            }
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set { if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value); }
        }

        public void Reset()
        {
            for (int i = 0; i < dwell.Length; i++) { dwell[i] = 0f; flash[i] = 0f; }
            Looking = -1;
            lost = 0f;
        }

        /// <summary>
        /// Updates gaze dwell on the slots. Returns the slot whose item should be used this frame, or -1.
        /// Only stored items count; empty and locked slots ignore the gaze. The slot being looked at keeps a wider
        /// margin and survives a fifth of a second of lost gaze, so eye jitter doesn't restart the count.
        /// </summary>
        public int Tick(float dt, Ray? gaze, bool allowUse)
        {
            time += dt;
            int hit = -1;
            if (allowUse && gaze.HasValue && GazeFocus.Hit(root, gaze.Value, out Vector2 p))
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (progress.SlotItem(i) == null) continue;
                    Vector2 c = slots[i].localPosition;
                    float pad = Looking == i ? KeepPadding : HitPadding;
                    if ((p - c).magnitude < SlotRadius + pad) { hit = i; break; }
                }
            }
            bool counting = hit >= 0;
            if (hit >= 0) lost = 0f;
            else if (Looking >= 0 && allowUse && progress.SlotItem(Looking) != null && (lost += dt) <= LoseGrace) hit = Looking;  // hold
            else lost = 0f;
            Looking = hit;
            int use = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (i == hit) { if (counting) dwell[i] += dt; }
                else dwell[i] = Mathf.Max(0f, dwell[i] - dt * 1.5f);
                if (dwell[i] >= StorageCatalog.UseSeconds && use < 0) { use = i; dwell[i] = 0f; flash[i] = 0.6f; }
                if (progress.SlotItem(i) == null && i != use) dwell[i] = 0f;
            }
            if (use >= 0) { Looking = -1; lost = 0f; }
            Refresh(dt);
            return use;
        }

        void Refresh(float dt)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                bool open = i < progress.StorageSlots;
                var item = progress.SlotItem(i);
                Texture2D badge = item.HasValue ? Pickup.IconTexture(item.Value) : null;
                icons[i].gameObject.SetActive(item.HasValue && badge == null);
                badges[i].Visible = badge != null;
                float look = Looking == i ? 1f : 0f;
                if (badge != null)
                {
                    badges[i].Texture = badge;
                    badges[i].Size = Vector2.one * SlotRadius * (1.6f + 0.15f * look);
                }
                else if (item.HasValue)
                {
                    icons[i].sharedMesh = Pickup.IconMesh(item.Value);
                    icons[i].transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 1.4f + i) * 22f, 0f);
                    icons[i].transform.localScale = Vector3.one * (0.55f + 0.08f * look);
                }
                locks[i].Visible = !open;
                labels[i].Text = !open ? "" : item.HasValue ? StorageCatalog.Name(item.Value) : "EMPTY";
                labels[i].Alpha = item.HasValue ? 1f : 0.5f;
                rings[i].Color = !open ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : Looking == i ? Palette.UIAmber : Palette.UIIvory;

                if (flash[i] > 0f) flash[i] -= dt;
                Color fc = item.HasValue ? StorageCatalog.Tint(item.Value) : Palette.UIAmber;
                flashes[i].Color = new Color(fc.r, fc.g, fc.b, Mathf.Clamp01(flash[i] / 0.6f) * 0.9f);
                flashes[i].Visible = flash[i] > 0f;

                bool showDwell = dwell[i] > 0.02f && item.HasValue;
                if (dwellRings[i].gameObject.activeSelf != showDwell) dwellRings[i].gameObject.SetActive(showDwell);
                if (showDwell)
                {
                    mb.Clear();
                    mb.Arc(Vector3.zero, SlotRadius + 0.1f, SlotRadius + 0.38f, dwell[i] / StorageCatalog.UseSeconds, 40, Color.white);
                    mb.ToMesh(dwellMeshes[i].name, dwellMeshes[i]);
                    Color c = StorageCatalog.Tint(item.Value) * 1.6f; c.a = 1f;
                    mpb.SetColor("_Tint", c);
                    dwellRings[i].SetPropertyBlock(mpb);
                }
            }
        }
    }
}
