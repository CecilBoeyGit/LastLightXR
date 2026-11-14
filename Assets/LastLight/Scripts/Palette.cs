using UnityEngine;

namespace LastLight
{
    /// <summary>Colour palette from the pitch pack: midnight pine, charcoal, ochre vehicle, amber light, acid-green route, coral danger.</summary>
    public static class Palette
    {
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        public static readonly Color Road = Hex("#7a5e3e");
        public static readonly Color RoadDark = Hex("#5e4630");
        public static readonly Color LaneMark = Hex("#b9ad8c");
        public static readonly Color Floor = Hex("#31452c");
        public static readonly Color FloorDark = Hex("#22321f");
        public static readonly Color Moss = Hex("#47603a");
        public static readonly Color Pine = Hex("#2f5238");
        public static readonly Color PineDark = Hex("#1f3a29");
        public static readonly Color Trunk = Hex("#4c3626");
        public static readonly Color Rock = Hex("#77736a");
        public static readonly Color Cliff = Hex("#5f5b53");
        public static readonly Color Earth = Hex("#2a211a");
        public static readonly Color Stone = Hex("#3b3833");
        public static readonly Color Ochre = Hex("#d99b2b");
        public static readonly Color Cabin = Hex("#3b3a35");
        public static readonly Color Tyre = Hex("#1b1b1a");
        public static readonly Color Lamp = Hex("#fff1c9");
        public static readonly Color Tail = Hex("#d6463b");
        public static readonly Color Skin = Hex("#cfc8b2");
        public static readonly Color Shirt1 = Hex("#6c675a");
        public static readonly Color Shirt2 = Hex("#7d5a4e");
        public static readonly Color Shirt3 = Hex("#4f5a5c");
        public static readonly Color CoinGold = Hex("#e8b53a");
        public static readonly Color BoostOrange = Hex("#f0702c");
        public static readonly Color ShieldBlue = Hex("#3a8df0");
        public static readonly Color LightBody = Hex("#c9ccd2");
        public static readonly Color LightBeam = Hex("#ffeeb0");
        public static readonly Color WarningBoard = Hex("#d9463a");
        public static readonly Color EscapeMarker = Hex("#a6f03c") * 3f;
        public static readonly Color Amber = new Color(1f, 0.62f, 0.2f, 1f);
        public static readonly Color Route = new Color(0.62f, 0.95f, 0.25f, 1f);
        public static readonly Color Danger = new Color(1f, 0.38f, 0.3f, 1f);
        public static readonly Color Background = Hex("#0b120f");

        // Shooter zombies, acid and the new powerups.
        public static readonly Color ShooterSkin = Hex("#9fae7a");
        public static readonly Color ShooterSack = Hex("#6fae2a");
        public static readonly Color ShooterRags = Hex("#4a4436");
        public static readonly Color Acid = new Color(0.55f, 1f, 0.15f, 1f);
        public static readonly Color TurretRed = Hex("#d6463b");
        public static readonly Color ShovelSteel = Hex("#8d949c");
        public static readonly Color ShovelYellow = Hex("#e8b53a");
        public static readonly Color Metal = Hex("#55595e");

        // Retro comic menu palette, matched to the title splash.
        public static readonly Color UIInk = Hex("#0d1216");
        public static readonly Color UIIvory = Hex("#f1e4c4");
        public static readonly Color UIAmber = Hex("#f2a93b");
    }
}
