using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The pursuing horde. Its distance behind the car is the escape gap, so the player reads the
    /// danger from the crowd itself. Individual walkers shamble around that line.
    /// </summary>
    public class Horde
    {
        const float CrowdHalfWidth = 4.3f;

        /// <summary>How far any walker has strayed outside the road. Negative means all stayed on it. For tests.</summary>
        public float WorstRoadDistance { get; set; } = float.MinValue;

        readonly Transform root;
        readonly Transform[] walkers;
        readonly Vector3[] offsets;
        readonly float[] phase;

        public Transform Root => root;

        public Horde(Transform parent, Material mat, int count, int seed)
        {
            root = new GameObject("Horde").transform;
            root.SetParent(parent, false);

            var rng = new System.Random(seed);
            Color[] shirts = { Palette.Shirt1, Palette.Shirt2, Palette.Shirt3 };
            var meshes = new Mesh[3];
            for (int v = 0; v < 3; v++)
            {
                var mb = new MeshBuilder();
                Color shirt = shirts[v];
                mb.Box(new Vector3(-0.13f, 0.38f, 0f), new Vector3(0.18f, 0.76f, 0.2f), Palette.Shirt3 * 0.7f);
                mb.Box(new Vector3(0.13f, 0.38f, 0f), new Vector3(0.18f, 0.76f, 0.2f), Palette.Shirt3 * 0.7f);
                mb.Box(new Vector3(0f, 1.05f, 0f), new Vector3(0.56f, 0.66f, 0.32f), shirt);
                mb.Box(new Vector3(0f, 1.55f, 0.04f), new Vector3(0.34f, 0.36f, 0.34f), Palette.Skin);
                // Arms reaching forward
                mb.Box(new Vector3(-0.34f, 1.25f, 0.3f), new Vector3(0.14f, 0.14f, 0.62f), Quaternion.Euler(-12f, 0f, 0f), Palette.Skin * 0.95f);
                mb.Box(new Vector3(0.34f, 1.22f, 0.26f), new Vector3(0.14f, 0.14f, 0.58f), Quaternion.Euler(-4f, 0f, 0f), Palette.Skin * 0.95f);
                meshes[v] = mb.ToMesh("LL_Zombie_" + v);
            }

            walkers = new Transform[count];
            offsets = new Vector3[count];
            phase = new float[count];
            for (int i = 0; i < count; i++)
            {
                var r = MeshBuilder.Spawn("Walker " + i, root, meshes[i % 3], mat);
                walkers[i] = r.transform;
                float row = i / 6;
                offsets[i] = new Vector3(((float)rng.NextDouble() - 0.5f) * 8.6f, 0f, -row * 1.3f - (float)rng.NextDouble() * 0.9f);
                phase[i] = (float)rng.NextDouble() * 10f;
                float s = 0.85f + (float)rng.NextDouble() * 0.3f;
                walkers[i].localScale = new Vector3(s, s, s);
            }
        }

        /// <summary>Places the horde so its front line sits <paramref name="gap"/> units behind the car.</summary>
        public void Tick(float gap, float time, bool moving, TrackGenerator track, float distance)
        {
            float centerAtCar = track.Center(distance);
            // Map the gap onto the room behind the car so the horde never leaves the disk.
            const float maxGapVisual = 12f;
            float room = Diorama.Radius + Diorama.CarZ - Vehicle.HalfLength - 4.5f;
            float frontZ = Diorama.CarZ - Vehicle.HalfLength - 0.4f - Mathf.Clamp01(gap / maxGapVisual) * room;
            root.localPosition = new Vector3(0f, 0f, frontZ);
            float pace = moving ? 1f : 0.35f;
            for (int i = 0; i < walkers.Length; i++)
            {
                float t = time * (5.5f + (i % 4) * 0.4f) * pace + phase[i];
                Vector3 o = offsets[i];
                o.z += Mathf.Sin(t * 0.5f) * 0.25f;
                o.y = Mathf.Abs(Mathf.Sin(t)) * 0.12f;

                // Each walker stands on the road surface where it actually is, so bends and splits
                // never push one through the terrain.
                float s = distance + frontZ + o.z - Diorama.CarZ;
                float frac = o.x / CrowdHalfWidth + Mathf.Sin(t * 0.37f) * 0.06f;
                float roadX = track.WalkerRoadX(frac, s);
                WorstRoadDistance = Mathf.Max(WorstRoadDistance, track.RoadDistance(roadX, s));
                o.x = roadX + track.Center(s) - centerAtCar;
                walkers[i].localPosition = o;

                float bend = Mathf.Atan(track.Slope(s)) * Mathf.Rad2Deg;
                walkers[i].localRotation = Quaternion.Euler(8f + Mathf.Sin(t) * 6f, bend + Mathf.Sin(t * 0.3f) * 14f, Mathf.Sin(t) * 7f);
            }
        }
    }
}
