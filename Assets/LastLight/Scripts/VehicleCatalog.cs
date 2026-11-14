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
        public string[] colorNames;
        public Color[] colors;
        public float roofY;         // where the turret mounts
        public float frontZ;        // front bumper, where the shovel mounts
        public System.Action<MeshBuilder, Color> build;
    }

    /// <summary>
    /// The vehicles the garage offers. All are placeholder box models for now, built facing +Z within the
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
                roofY = 1.4f,
                frontZ = 1.32f,
                build = BuildJeep
            },
            new VehicleDef
            {
                name = "HAULER PICKUP",
                blurb = "Long bed, big grille.",
                cost = 400,
                colorNames = new[] { "Steel Blue", "Forest", "Charcoal", "Sunset" },
                colors = new[] { Hex("#3f6f9a"), Hex("#2f5a3a"), Hex("#3a3c40"), Hex("#e0702e") },
                roofY = 1.42f,
                frontZ = 1.34f,
                build = BuildPickup
            },
            new VehicleDef
            {
                name = "RHINO APC",
                blurb = "Armoured plates, slit windows.",
                cost = 1000,
                colorNames = new[] { "Gunmetal", "Desert", "Night", "Hazard" },
                colors = new[] { Hex("#5a6068"), Hex("#b89a62"), Hex("#22252a"), Hex("#e8c02e") },
                roofY = 1.55f,
                frontZ = 1.36f,
                build = BuildApc
            }
        };

        public static int Count => All.Length;

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
    }
}
