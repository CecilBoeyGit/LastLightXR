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
        const float WalkerHeight = 1.75f;       // the box walkers' height; the Tripo runner is scaled to match

        /// <summary>How far any walker has strayed outside the road. Negative means all stayed on it. For tests.</summary>
        public float WorstRoadDistance { get; set; } = float.MinValue;

        readonly Transform root;
        readonly Transform[] walkers;
        readonly Vector3[] offsets;
        readonly float[] phase;
        readonly ZombieRig[] rigs;              // null when the Tripo runner isn't configured

        public Transform Root => root;
        /// <summary>True when the walkers are the rigged Tripo runner rather than box meshes. For tests.</summary>
        public bool UsesModels => rigs != null;

        public Horde(Transform parent, Material mat, int count, int seed)
        {
            root = new GameObject("Horde").transform;
            root.SetParent(parent, false);

            var rng = new System.Random(seed);
            var models = ZombieModels.Active;
            if (models != null && models.HasRunner)
                rigs = new ZombieRig[count];
            var tintRng = new System.Random(seed + 7);   // separate, so the crowd layout matches the box version
            Color[] shirts = { Palette.Shirt1, Palette.Shirt2, Palette.Shirt3 };
            var meshes = new Mesh[3];
            for (int v = 0; v < 3 && rigs == null; v++)
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
                if (rigs != null)
                {
                    walkers[i] = new GameObject("Walker " + i).transform;
                    walkers[i].SetParent(root, false);
                    rigs[i] = ZombieRig.Spawn(models.runnerPrefab, models.runnerMaterial, walkers[i], WalkerHeight, models.runnerHeight, models.runnerRun);
                    // A little variety in skin and cloth brightness so the crowd doesn't read as clones.
                    float b = 0.82f + (float)tintRng.NextDouble() * 0.3f;
                    rigs[i].SetTint(new Color(b, b * (0.96f + (float)tintRng.NextDouble() * 0.08f), b, 1f));
                }
                else walkers[i] = MeshBuilder.Spawn("Walker " + i, root, meshes[i % 3], mat).transform;
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
                if (rigs != null)
                {
                    // The run clip carries the bob and lean; only weave a little on top of it.
                    o.y = 0f;
                    walkers[i].localPosition = o;
                    walkers[i].localRotation = Quaternion.Euler(3f, bend + Mathf.Sin(t * 0.3f) * 10f, Mathf.Sin(t * 0.5f) * 3f);
                    // Each walker runs at its own cadence; the clip is 1.25 s, which matches the box bob at full pace.
                    float cadence = 0.9f + (i % 4) * 0.06f;
                    rigs[i].Pose(0, time * cadence * pace + phase[i]);
                }
                else walkers[i].localRotation = Quaternion.Euler(8f + Mathf.Sin(t) * 6f, bend + Mathf.Sin(t * 0.3f) * 14f, Mathf.Sin(t) * 7f);
            }
        }
    }
}
