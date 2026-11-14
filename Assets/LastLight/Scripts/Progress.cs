using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// What the player keeps between runs: the coin bank, unlocked vehicles, the vehicle being driven and
    /// each vehicle's colour. Saved in PlayerPrefs unless persistence is off (the headless tests turn it off
    /// so they never touch a player's save).
    /// </summary>
    public class Progress
    {
        const string CoinsKey = "LastLight.Coins";
        const string VehicleKey = "LastLight.Vehicle";
        const string UnlockKey = "LastLight.Unlocked.";
        const string ColorKey = "LastLight.Color.";

        public readonly bool persist;
        public int Coins { get; private set; }
        public int Vehicle { get; private set; }

        readonly bool[] unlocked = new bool[VehicleCatalog.Count];
        readonly int[] colors = new int[VehicleCatalog.Count];

        public Progress(bool persist)
        {
            this.persist = persist;
            unlocked[0] = true;
            if (!persist) return;
            Coins = PlayerPrefs.GetInt(CoinsKey, 0);
            for (int i = 1; i < unlocked.Length; i++) unlocked[i] = PlayerPrefs.GetInt(UnlockKey + i, 0) == 1;
            for (int i = 0; i < colors.Length; i++) colors[i] = PlayerPrefs.GetInt(ColorKey + i, 0);
            Vehicle = Mathf.Clamp(PlayerPrefs.GetInt(VehicleKey, 0), 0, VehicleCatalog.Count - 1);
            if (!unlocked[Vehicle]) Vehicle = 0;
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
            PlayerPrefs.Save();
        }
    }
}
