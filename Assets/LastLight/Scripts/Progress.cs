using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// What the player keeps between runs: the coin bank, unlocked vehicles, the vehicle being driven,
    /// each vehicle's colour, the storage compartment (unlocked slots and the item in each) and the traits
    /// (levels, and which vehicles have paid to carry them). Saved in PlayerPrefs unless persistence is off
    /// (the headless tests turn it off so they never touch a player's save).
    /// </summary>
    public class Progress
    {
        const string CoinsKey = "LastLight.Coins";
        const string VehicleKey = "LastLight.Vehicle";
        const string UnlockKey = "LastLight.Unlocked.";
        const string ColorKey = "LastLight.Color.";
        const string SlotsKey = "LastLight.StorageSlots";
        const string SlotItemKey = "LastLight.StorageItem.";
        const string TraitKey = "LastLight.Trait.";
        const string LinkKey = "LastLight.TraitsLinked.";
        public const int Empty = -1;

        public readonly bool persist;
        public int Coins { get; private set; }
        public int Vehicle { get; private set; }

        readonly bool[] unlocked = new bool[VehicleCatalog.Count];
        readonly int[] colors = new int[VehicleCatalog.Count];
        readonly int[] slotItems = { Empty, Empty, Empty, Empty };
        readonly int[] traits = new int[TraitCatalog.Count];
        readonly bool[] linked = new bool[VehicleCatalog.Count];

        /// <summary>Storage compartment slots unlocked, 1 to 4. The first is free; the fourth needs a second vehicle.</summary>
        public int StorageSlots { get; private set; } = 1;

        public Progress(bool persist)
        {
            this.persist = persist;
            unlocked[0] = true;
            linked[0] = true;   // the Scout Jeep carries the traits for free
            if (!persist) return;
            Coins = PlayerPrefs.GetInt(CoinsKey, 0);
            for (int i = 1; i < unlocked.Length; i++) unlocked[i] = PlayerPrefs.GetInt(UnlockKey + i, 0) == 1;
            for (int i = 0; i < colors.Length; i++) colors[i] = PlayerPrefs.GetInt(ColorKey + i, 0);
            Vehicle = Mathf.Clamp(PlayerPrefs.GetInt(VehicleKey, 0), 0, VehicleCatalog.Count - 1);
            if (!unlocked[Vehicle]) Vehicle = 0;
            StorageSlots = Mathf.Clamp(PlayerPrefs.GetInt(SlotsKey, 1), 1, StorageCatalog.MaxSlots);
            for (int i = 0; i < slotItems.Length; i++)
            {
                int item = PlayerPrefs.GetInt(SlotItemKey + i, Empty);
                slotItems[i] = i < StorageSlots && StorageCatalog.IsItem(item) ? item : Empty;
            }
            for (int i = 0; i < traits.Length; i++) traits[i] = Mathf.Clamp(PlayerPrefs.GetInt(TraitKey + i, 0), 0, TraitCatalog.MaxLevel);
            for (int i = 1; i < linked.Length; i++) linked[i] = PlayerPrefs.GetInt(LinkKey + i, 0) == 1;
        }

        // ---------- Traits ----------

        /// <summary>The level bought for a trait, 0 to 2, whatever is being driven.</summary>
        public int TraitLevel(Trait t) => traits[(int)t];

        /// <summary>True once a vehicle carries the traits: the Scout Jeep always does, others after the transfer fee.</summary>
        public bool TraitsLinked(int vehicle) => vehicle >= 0 && vehicle < linked.Length && linked[vehicle];

        /// <summary>The level that applies in a run: the bought level if the driven vehicle carries the traits, else 0.</summary>
        public int ActiveTraitLevel(Trait t) => TraitsLinked(Vehicle) ? traits[(int)t] : 0;

        /// <summary>Buys the next level of a trait. Only while driving a vehicle that carries the traits.</summary>
        public bool TryUpgradeTrait(Trait t)
        {
            int level = traits[(int)t];
            if (level >= TraitCatalog.MaxLevel || !TraitsLinked(Vehicle)) return false;
            if (!Spend(TraitCatalog.Cost(t, level + 1))) return false;
            traits[(int)t] = level + 1;
            Save();
            return true;
        }

        /// <summary>Pays a vehicle's one-time fee to carry the traits, now and for every later upgrade.</summary>
        public bool TryLinkTraits(int vehicle)
        {
            if (!IsUnlocked(vehicle)) return false;
            if (TraitsLinked(vehicle)) return true;
            if (!Spend(TraitCatalog.TransferFee(vehicle))) return false;
            linked[vehicle] = true;
            Save();
            return true;
        }

        /// <summary>True once any vehicle past the Scout Jeep is owned, which opens the fourth compartment slot.</summary>
        public bool FourthSlotAllowed
        {
            get
            {
                for (int i = 1; i < unlocked.Length; i++) if (unlocked[i]) return true;
                return false;
            }
        }

        // ---------- Storage compartment ----------

        /// <summary>The powerup stored in a slot, or null if it is empty or locked.</summary>
        public PickupKind? SlotItem(int slot)
        {
            if (slot < 0 || slot >= StorageSlots || slotItems[slot] == Empty) return null;
            return (PickupKind)slotItems[slot];
        }

        public int FreeSlot()
        {
            for (int i = 0; i < StorageSlots; i++) if (slotItems[i] == Empty) return i;
            return -1;
        }

        /// <summary>Unlocks the next storage slot, in order, if there are coins for it.</summary>
        public bool TryUnlockSlot()
        {
            if (StorageSlots >= StorageCatalog.MaxSlots) return false;
            if (StorageSlots == StorageCatalog.MaxSlots - 1 && !FourthSlotAllowed) return false;
            if (!Spend(StorageCatalog.SlotCost(StorageSlots))) return false;
            StorageSlots++;
            Save();
            return true;
        }

        /// <summary>Buys an item into the first empty slot. Returns the slot, or -1 if there's no room or not enough coins.</summary>
        public int TryBuyItem(PickupKind item)
        {
            int slot = FreeSlot();
            if (slot < 0 || !StorageCatalog.IsItem((int)item)) return -1;
            if (!Spend(StorageCatalog.Price(item))) return -1;
            slotItems[slot] = (int)item;
            Save();
            return slot;
        }

        /// <summary>Takes the item out of a slot to use it. The slot is left empty.</summary>
        public PickupKind? TakeItem(int slot)
        {
            var item = SlotItem(slot);
            if (item == null) return null;
            slotItems[slot] = Empty;
            Save();
            return item;
        }

        public bool IsUnlocked(int vehicle) => vehicle >= 0 && vehicle < unlocked.Length && unlocked[vehicle];
        public int ColorOf(int vehicle) => colors[Mathf.Clamp(vehicle, 0, colors.Length - 1)];
        public bool CanAfford(int cost) => Coins >= cost;

        public void AddCoins(int amount)
        {
            Coins = Mathf.Max(0, Coins + amount);
        }

        /// <summary>Spends coins if there are enough. Saves straight away, since it's a purchase.</summary>
        public bool Spend(int cost)
        {
            if (cost < 0 || Coins < cost) return false;
            Coins -= cost;
            Save();
            return true;
        }

        public bool TryUnlock(int vehicle)
        {
            if (IsUnlocked(vehicle)) return true;
            if (!Spend(VehicleCatalog.All[vehicle].cost)) return false;
            unlocked[vehicle] = true;
            Save();
            return true;
        }

        public void Equip(int vehicle)
        {
            if (!IsUnlocked(vehicle)) return;
            Vehicle = vehicle;
            Save();
        }

        public void SetColor(int vehicle, int color)
        {
            if (!IsUnlocked(vehicle)) return;
            colors[vehicle] = Mathf.Clamp(color, 0, VehicleCatalog.All[vehicle].colors.Length - 1);
            Save();
        }

        public void Save()
        {
            if (!persist) return;
            PlayerPrefs.SetInt(CoinsKey, Coins);
            PlayerPrefs.SetInt(VehicleKey, Vehicle);
            for (int i = 1; i < unlocked.Length; i++) PlayerPrefs.SetInt(UnlockKey + i, unlocked[i] ? 1 : 0);
            for (int i = 0; i < colors.Length; i++) PlayerPrefs.SetInt(ColorKey + i, colors[i]);
            PlayerPrefs.SetInt(SlotsKey, StorageSlots);
            for (int i = 0; i < slotItems.Length; i++) PlayerPrefs.SetInt(SlotItemKey + i, slotItems[i]);
            for (int i = 0; i < traits.Length; i++) PlayerPrefs.SetInt(TraitKey + i, traits[i]);
            for (int i = 1; i < linked.Length; i++) PlayerPrefs.SetInt(LinkKey + i, linked[i] ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
