using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>One vehicle in the garage. Every vehicle shares the jeep's footprint, so handling is identical.</summary>
    public class VehicleDef
    {
        public string name;
        public string blurb;
        public int cost;
        /// <summary>Best-run distance in metres needed before the vehicle can be bought.</summary>
        public float requiredDistance;
        public string[] colorNames;
        /// <summary>The Tripo model's own body colour (see VehicleCatalog.PaintKey).</summary>
        public Vector4 paintKey;
        public Color[] colors;
        public float roofY;         // where the turret mounts
        public float frontZ;        // front bumper, where the shovel mounts
        /// <summary>One-line perk shown in the garage, or empty.</summary>
        public string perk = "";
        public System.Action<MeshBuilder, Color> build;
    }

    /// <summary>
    /// The vehicles the garage offers. Owning any vehicle past the Scout Jeep lets the storage panel sell a fourth
    /// compartment slot; the Rhino starts every run with a shovel fitted, and the Angel Transponder gets one free
    /// respawn per run. All are placeholder box models for now, built facing +Z within the
    /// jeep's 1.4 x 2.6 footprint. Swap the build functions for real models later.
    /// </summary>
    public static class VehicleCatalog
    {
        public static readonly VehicleDef[] All =
        {
            new VehicleDef
            {
                name = "SCOUT JEEP",
                blurb = "Old faithful. Always unlocked.",
                cost = 0,
                colorNames = new[] { "Ochre", "Olive", "Rust", "Arctic" },
                colors = new[] { Palette.Ochre, Hex("#6b7a3a"), Hex("#a8432f"), Hex("#d9d6cc") },
                paintKey = Key(34f, 0.70f, 0.52f, 15f),          // the Tripo model's ochre body, #845C27
                roofY = 1.4f,
                frontZ = 1.32f,
                build = BuildJeep
            },
            new VehicleDef
            {
                name = "HAULER PICKUP",
                blurb = "Long bed, big grille.",
                perk = "PERK: 4TH STORAGE SLOT",
                cost = 400,
                requiredDistance = 800f,
                colorNames = new[] { "Steel Blue", "Forest", "Charcoal", "Sunset" },
                colors = new[] { Hex("#3f6f9a"), Hex("#2f5a3a"), Hex("#3a3c40"), Hex("#e0702e") },
                paintKey = Key(186f, 0.54f, 0.35f, 18f),         // teal body, #29555A (the cream stripes stay)
                roofY = 1.42f,
                frontZ = 1.34f,
                build = BuildPickup
            },
            new VehicleDef
            {
                name = "RHINO APC",
                blurb = "Armoured plates, slit windows.",
                perk = "PERK: SHOVEL FITTED EVERY RUN, 4TH SLOT",
                cost = 1000,
                requiredDistance = 1500f,
                colorNames = new[] { "Gunmetal", "Desert", "Night", "Hazard" },
                colors = new[] { Hex("#5a6068"), Hex("#b89a62"), Hex("#22252a"), Hex("#e8c02e") },
                paintKey = Key(356f, 0.57f, 0.38f, 14f),         // maroon body, #622A2E (the gold stripes stay)
                roofY = 1.55f,
                frontZ = 1.36f,
                build = BuildApc
            },
            new VehicleDef
            {
                name = "ANGEL TRANSPONDER",
                blurb = "Emergency medical van.",
                perk = "PERK: 1 FREE RESPAWN PER RUN, 4TH SLOT",
                cost = 2500,
                requiredDistance = 3000f,
                colorNames = new[] { "Rescue White", "Signal Red", "Hi-Vis Lime", "Night Navy" },
                colors = new[] { Hex("#e9e6df"), Hex("#c8372d"), Hex("#c9e23a"), Hex("#25324a") },
                paintKey = new Vector4(0.075f, 0.15f, 0.84f, 0f), // off-white body, #D6C4B5 (the red stripe stays)
                roofY = 1.72f,
                frontZ = 1.34f,
                build = BuildAmbulance
            }
        };

        public const int Hauler = 1, Rhino = 2, Angel = 3;

        public static int Count => All.Length;

        /// <summary>The Tripo model for a vehicle (mesh and textured material), when the baked models are present.</summary>
        public static bool TryModel(int vehicle, out Mesh mesh, out Material material)
        {
            mesh = null; material = null;
            var models = SceneryModels.Active;
            if (models == null || !models.HasVehicle(vehicle)) return false;
            mesh = models.vehicleMeshes[vehicle];
            material = models.vehicleMaterials[vehicle];
            return true;
        }

        /// <summary>Where the roof turret mounts: the top of the Tripo model when there is one, else the box model's roof.</summary>
        public static float RoofY(int vehicle)
        {
            vehicle = Mathf.Clamp(vehicle, 0, All.Length - 1);
            return TryModel(vehicle, out Mesh m, out _) ? m.bounds.max.y - 0.05f : All[vehicle].roofY;
        }

        /// <summary>Where the shovel mounts: the front bumper of the Tripo model when there is one, else the box model's.</summary>
        public static float FrontZ(int vehicle)
        {
            vehicle = Mathf.Clamp(vehicle, 0, All.Length - 1);
            return TryModel(vehicle, out Mesh m, out _) ? m.bounds.max.z : All[vehicle].frontZ;
        }

        /// <summary>
        /// The paint job for a textured model, for the shader's _Paint (rgb colour, a amount). The first colour is the
        /// model's own paint, left alone; the others repaint the texture's light and shade in the swatch colour.
        /// </summary>
        public static Color Paint(int vehicle, int color)
        {
            vehicle = Mathf.Clamp(vehicle, 0, All.Length - 1);
            var def = All[vehicle];
            color = Mathf.Clamp(color, 0, def.colors.Length - 1);
            if (color == 0) return new Color(1f, 1f, 1f, 0f);
            Color c = def.colors[color];
            return new Color(c.r, c.g, c.b, 1f);
        }

        /// <summary>The vehicle's own body colour, for the shader's _PaintKey: the texels that the paint replaces.</summary>
        public static Vector4 PaintKey(int vehicle) => All[Mathf.Clamp(vehicle, 0, All.Length - 1)].paintKey;

        /// <summary>A coloured body's key: hue in degrees, saturation and value (sRGB), and how far off its hue still counts.</summary>
        static Vector4 Key(float hueDegrees, float saturation, float value, float toleranceDegrees) =>
            new Vector4(hueDegrees / 360f, saturation, value, toleranceDegrees / 360f);

        public const string PaintProperty = "_Paint";
        public const string PaintKeyProperty = "_PaintKey";

        static readonly Dictionary<long, Mesh> cache = new Dictionary<long, Mesh>();

        /// <summary>The body mesh for a vehicle in one of its colours. Meshes are cached and shared.</summary>
        public static Mesh GetMesh(int vehicle, int color)
        {
            vehicle = Mathf.Clamp(vehicle, 0, All.Length - 1);
            var def = All[vehicle];
            color = Mathf.Clamp(color, 0, def.colors.Length - 1);
            long key = vehicle * 100 + color;
            if (cache.TryGetValue(key, out Mesh m) && m != null) return m;
            var mb = new MeshBuilder();
            def.build(mb, def.colors[color]);
            m = mb.ToMesh($"LL_Vehicle_{vehicle}_{color}");
            cache[key] = m;
            return m;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        static void Wheels(MeshBuilder mb, float x, float z, float r = 0.31f)
        {
            for (int i = 0; i < 4; i++)
            {
                float wx = (i % 2 == 0 ? -1f : 1f) * x;
                float wz = (i < 2 ? -1f : 1f) * z;
                mb.Box(new Vector3(wx, r + 0.01f, wz), new Vector3(0.28f, r * 2f, r * 2f), Palette.Tyre);
            }
        }

        static void Lamps(MeshBuilder mb, float front, float back, float y)
        {
            mb.Box(new Vector3(-0.45f, y, front), new Vector3(0.28f, 0.18f, 0.04f), Palette.Lamp * 3f);
            mb.Box(new Vector3(0.45f, y, front), new Vector3(0.28f, 0.18f, 0.04f), Palette.Lamp * 3f);
            mb.Box(new Vector3(-0.55f, y - 0.02f, back), new Vector3(0.18f, 0.14f, 0.04f), Palette.Tail * 2f);
            mb.Box(new Vector3(0.55f, y - 0.02f, back), new Vector3(0.18f, 0.14f, 0.04f), Palette.Tail * 2f);
        }

        /// <summary>The original ochre jeep: body, cabin, roof rack, spare tyre.</summary>
        static void BuildJeep(MeshBuilder mb, Color body)
        {
            mb.Box(new Vector3(0f, 0.55f, 0f), new Vector3(1.4f, 0.6f, 2.6f), body);
            mb.Box(new Vector3(0f, 1.05f, -0.25f), new Vector3(1.25f, 0.5f, 1.4f), body * 0.95f);
            mb.Box(new Vector3(0f, 1.08f, 0.47f), new Vector3(1.15f, 0.4f, 0.06f), Palette.Cabin);
            mb.Box(new Vector3(0f, 1.36f, -0.25f), new Vector3(1.1f, 0.08f, 1.2f), Palette.Cabin);
            Wheels(mb, 0.72f, 0.85f);
            mb.Box(new Vector3(0f, 0.55f, -1.36f), new Vector3(0.5f, 0.5f, 0.12f), Palette.Tyre);
            Lamps(mb, 1.31f, -1.31f, 0.62f);
        }

        /// <summary>Pickup: short cab up front, open bed behind with a roll bar.</summary>
        static void BuildPickup(MeshBuilder mb, Color body)
        {
            mb.Box(new Vector3(0f, 0.5f, 0.05f), new Vector3(1.42f, 0.5f, 2.62f), body);
            mb.Box(new Vector3(0f, 1.0f, 0.35f), new Vector3(1.3f, 0.52f, 0.95f), body * 0.95f);
            mb.Box(new Vector3(0f, 1.02f, 0.84f), new Vector3(1.18f, 0.4f, 0.05f), Palette.Cabin);
            mb.Box(new Vector3(0f, 1.28f, 0.35f), new Vector3(1.2f, 0.06f, 0.85f), body * 0.85f);
            // Bed walls and floor.
            mb.Box(new Vector3(-0.66f, 0.88f, -0.7f), new Vector3(0.1f, 0.28f, 1.15f), body * 0.9f);
            mb.Box(new Vector3(0.66f, 0.88f, -0.7f), new Vector3(0.1f, 0.28f, 1.15f), body * 0.9f);
            mb.Box(new Vector3(0f, 0.88f, -1.27f), new Vector3(1.42f, 0.28f, 0.08f), body * 0.9f);
            mb.Box(new Vector3(0f, 0.77f, -0.7f), new Vector3(1.2f, 0.04f, 1.1f), Palette.Cabin * 0.8f);
            // Roll bar and big grille.
            mb.Box(new Vector3(0f, 1.25f, -0.2f), new Vector3(1.3f, 0.08f, 0.08f), Palette.Metal);
            mb.Box(new Vector3(-0.62f, 1.02f, -0.2f), new Vector3(0.08f, 0.5f, 0.08f), Palette.Metal);
            mb.Box(new Vector3(0.62f, 1.02f, -0.2f), new Vector3(0.08f, 0.5f, 0.08f), Palette.Metal);
            mb.Box(new Vector3(0f, 0.5f, 1.35f), new Vector3(0.7f, 0.32f, 0.05f), Palette.Metal * 1.3f);
            Wheels(mb, 0.73f, 0.9f, 0.33f);
            Lamps(mb, 1.33f, -1.33f, 0.6f);
        }

        /// <summary>Armoured personnel carrier: sloped nose, slab sides, slit windows, roof hatch.</summary>
        static void BuildApc(MeshBuilder mb, Color body)
        {
            mb.Box(new Vector3(0f, 0.6f, -0.1f), new Vector3(1.44f, 0.7f, 2.4f), body);
            mb.Box(new Vector3(0f, 0.62f, 1.12f), new Vector3(1.36f, 0.5f, 0.5f), Quaternion.Euler(30f, 0f, 0f), body * 1.08f);
            mb.Box(new Vector3(0f, 1.2f, -0.25f), new Vector3(1.3f, 0.5f, 1.8f), body * 0.92f);
            mb.Box(new Vector3(0f, 1.2f, 0.66f), new Vector3(1.0f, 0.1f, 0.04f), Palette.Cabin);
            mb.Box(new Vector3(-0.66f, 1.2f, -0.2f), new Vector3(0.04f, 0.1f, 1.0f), Palette.Cabin);
            mb.Box(new Vector3(0.66f, 1.2f, -0.2f), new Vector3(0.04f, 0.1f, 1.0f), Palette.Cabin);
            mb.Box(new Vector3(0f, 1.49f, -0.45f), new Vector3(0.5f, 0.08f, 0.5f), body * 0.75f);
            // Side skirt plates over the wheels.
            mb.Box(new Vector3(-0.74f, 0.45f, -0.05f), new Vector3(0.06f, 0.4f, 2.1f), body * 0.8f);
            mb.Box(new Vector3(0.74f, 0.45f, -0.05f), new Vector3(0.06f, 0.4f, 2.1f), body * 0.8f);
            Wheels(mb, 0.68f, 0.85f, 0.3f);
            Lamps(mb, 1.3f, -1.31f, 0.5f);
        }

        /// <summary>Ambulance van: tall box body behind a short cab, side stripes and crosses, a red and blue light bar.</summary>
        static void BuildAmbulance(MeshBuilder mb, Color body)
        {
            Color stripe = Luma(body) > 0.45f ? Hex("#c8372d") : Hex("#e9e6df");
            // Cab up front, tall box behind.
            mb.Box(new Vector3(0f, 0.55f, 0.85f), new Vector3(1.42f, 0.6f, 0.9f), body);
            mb.Box(new Vector3(0f, 1.0f, 0.78f), new Vector3(1.3f, 0.36f, 0.6f), body * 0.96f);
            mb.Box(new Vector3(0f, 1.0f, 1.1f), new Vector3(1.16f, 0.3f, 0.04f), Palette.Cabin);
            mb.Box(new Vector3(0f, 0.95f, -0.45f), new Vector3(1.44f, 1.4f, 1.75f), body);
            // Stripes along both sides and the back, with a cross on each side.
            mb.Box(new Vector3(-0.73f, 0.72f, -0.05f), new Vector3(0.03f, 0.16f, 2.5f), stripe);
            mb.Box(new Vector3(0.73f, 0.72f, -0.05f), new Vector3(0.03f, 0.16f, 2.5f), stripe);
            mb.Box(new Vector3(0f, 0.72f, -1.33f), new Vector3(1.44f, 0.16f, 0.03f), stripe);
            foreach (float side in new[] { -0.735f, 0.735f })
            {
                mb.Box(new Vector3(side, 1.2f, -0.45f), new Vector3(0.03f, 0.5f, 0.16f), stripe);
                mb.Box(new Vector3(side, 1.2f, -0.45f), new Vector3(0.03f, 0.16f, 0.5f), stripe);
            }
            // Light bar: red and blue lamps on the box's front edge.
            mb.Box(new Vector3(0f, 1.68f, 0.32f), new Vector3(1.0f, 0.06f, 0.2f), Palette.Metal);
            mb.Box(new Vector3(-0.3f, 1.76f, 0.32f), new Vector3(0.36f, 0.12f, 0.16f), Palette.Tail * 2.5f);
            mb.Box(new Vector3(0.3f, 1.76f, 0.32f), new Vector3(0.36f, 0.12f, 0.16f), Palette.ShieldBlue * 2.5f);
            // Rear doors seam.
            mb.Box(new Vector3(0f, 0.95f, -1.335f), new Vector3(0.03f, 1.2f, 0.02f), Palette.Cabin);
            Wheels(mb, 0.72f, 0.88f, 0.31f);
            Lamps(mb, 1.31f, -1.34f, 0.62f);
        }

        static float Luma(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
    }
}
