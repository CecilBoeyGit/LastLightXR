using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum ChunkType { Quiet, Blocked1, Blocked2, Rockfall, Fork, Cliff }

    /// <summary>One authored, seeded stretch of road. Once generated it never rerolls.</summary>
    public class Chunk
    {
        public int index;
        public float startS;
        public ChunkType type;
        public int cliffSide;           // -1 left, +1 right, 0 none
        public Transform root;
        public readonly List<Hazard> hazards = new List<Hazard>();
        public readonly List<Pickup> pickups = new List<Pickup>();
        public bool rockfallTriggered;
        public Mesh terrainMesh;
    }

    /// <summary>
    /// A split in the road. Two branches open around a wooded island and rejoin later.
    /// Early forks load one branch with obstacles; later forks can wall one branch off as a dead end.
    /// </summary>
    public class Fork
    {
        public float startS, length;
        public int deadSide;        // -1 or +1 when that branch is a dead end
        public int obstacleSide;    // -1 or +1 when that branch holds extra obstacles
        public float EndS => startS + length;
        public float WallS => startS + length * WallFraction;
        public const float WallFraction = 0.72f;
    }

    /// <summary>
    /// A broken cliff segment. The ground falls away into the abyss on both sides, leaving a thin strip
    /// of road. Each segment later in a run is narrower than the last.
    /// </summary>
    public class CliffRun
    {
        public const float RampLength = 8f;
        public float startS, length, halfWidth;
        public float EndS => startS + length;
    }

    /// <summary>
    /// Generates the endless forest road from seeded chunks and scrolls it past the car.
    /// All road geometry is defined relative to a curving centreline Center(s). The world shifts
    /// sideways so the road under the car stays centred on the disk, and bends show up ahead and behind.
    /// Chunks are generated beyond the scout range, so a revealed chunk keeps its contents.
    /// </summary>
    public class TrackGenerator
    {
        public const float ChunkLength = 12f;
        public const float BranchHalfWidth = 3f;
        public const float EscapeHalfWidth = 1.45f;
        const float EscapeGapOffset = BranchHalfWidth - EscapeHalfWidth;
        const float MaxBranchOffset = 7.5f;
        const float SideExtent = 22f;
        const float CellX = 1.1f;
        const float CellZ = 1.2f;
        const float CarMargin = Vehicle.HalfWidth;
        const int DeadEndsFromChunk = 18;
        const int FirstCliffChunk = 30;
        const float AbyssDepth = 6.5f;
        const float FirstCliffHalfWidth = 3f;
        const float CliffNarrowingStep = 0.35f;
        const float NarrowestCliffHalfWidth = 1.25f;
        const float FallTolerance = 0.25f;      // how far past the edge the jeep's centre can go before it drops
        const int FirstPowerupChunk = 10;

        public readonly List<Chunk> chunks = new List<Chunk>();
        public readonly List<Hazard> hazards = new List<Hazard>();
        public readonly List<Pickup> pickups = new List<Pickup>();
        public readonly List<Fork> forks = new List<Fork>();
        public readonly List<CliffRun> cliffs = new List<CliffRun>();

        /// <summary>Powerups generated in the first 300 m of this run. For tests.</summary>
        public int EarlyPowerups { get; private set; }

        readonly Transform parent;
        readonly Material terrainMat, pickupMat;
        readonly int seed;
        readonly float phase1, phase2;
        int nextIndex;
        int nextForkIndex;
        int forkUntilIndex;
        Fork currentFork;
        int nextCliffIndex, cliffUntilIndex, cliffCount;
        ChunkType lastType = ChunkType.Quiet;
        bool lastWasHazard;

        static Mesh logMesh1, logMesh2, fallenPineMesh, boulderMesh, pebbleMesh, wallGapLeftMesh, wallGapRightMesh;

        public System.Action<Hazard> OnRockfallWarning;
        public System.Action<Fork> OnForkPlanned;
        public System.Action<CliffRun> OnCliffPlanned;

        public TrackGenerator(Transform parent, Material terrainMat, Material pickupMat, int seed)
        {
            this.parent = parent;
            this.terrainMat = terrainMat;
            this.pickupMat = pickupMat;
            this.seed = seed;
            var rng = new System.Random(seed * 31 + 7);
            phase1 = (float)rng.NextDouble() * Mathf.PI * 2f;
            phase2 = (float)rng.NextDouble() * Mathf.PI * 2f;
            BuildSharedMeshes();
            ResetForkPlan();
        }

        // ---------- Road shape ----------

        /// <summary>Lateral position of the road centreline at travel distance s. Starts straight, then curves.</summary>
        public float Center(float s)
        {
            float ramp = Smooth(25f, 130f, s);
            return ramp * (6f * Mathf.Sin(s * Mathf.PI * 2f / 90f + phase1) + 2.2f * Mathf.Sin(s * Mathf.PI * 2f / 38f + phase2));
        }

        /// <summary>Sideways change of the road per unit travelled. Positive bends toward +X.</summary>
        public float Slope(float s) => (Center(s + 0.5f) - Center(s - 0.5f));

        public Fork ForkAt(float s)
        {
            for (int i = 0; i < forks.Count; i++)
                if (s >= forks[i].startS && s < forks[i].EndS) return forks[i];
            return null;
        }

        /// <summary>The first fork starting between two distances, for look-ahead.</summary>
        public Fork NextFork(float fromS, float toS)
        {
            for (int i = 0; i < forks.Count; i++)
                if (forks[i].EndS > fromS && forks[i].startS < toS) return forks[i];
            return null;
        }

        /// <summary>How far each branch sits from the centreline, and its half width, at s inside a fork.</summary>
        public static void Branch(Fork f, float s, out float offset, out float halfWidth)
        {
            float t = Mathf.Clamp01((s - f.startS) / f.length);
            float open = Smooth(0f, 0.25f, t) * (1f - Smooth(0.8f, 1f, t));
            offset = MaxBranchOffset * open;
            halfWidth = Mathf.Lerp(Diorama.RoadHalfWidth, BranchHalfWidth, open);
        }

        /// <summary>
        /// Centre of the narrow escape track behind a dead-end wall. It starts at the wall's gap, on the
        /// branch's outer side, and eases back onto the road where the branches rejoin.
        /// </summary>
        public static float EscapeCenter(Fork f, float s)
        {
            Branch(f, s, out float b, out float w);
            float t = Mathf.Clamp01((s - f.startS) / f.length);
            float ease = 1f - Smooth(Fork.WallFraction, 1f, t);
            return f.deadSide * (b + EscapeGapOffset * ease);
        }

        /// <summary>True while the car at s is on the narrow track behind a dead-end wall.</summary>
        public bool InEscapeRoute(float s, int side)
        {
            Fork f = ForkAt(s);
            return f != null && f.deadSide != 0 && side == f.deadSide && s > f.WallS;
        }

        public CliffRun CliffAt(float s)
        {
            for (int i = 0; i < cliffs.Count; i++)
                if (s >= cliffs[i].startS && s < cliffs[i].EndS) return cliffs[i];
            return null;
        }

        /// <summary>How fully a cliff segment has taken hold at s: 0 at its ends, 1 along its thin stretch.</summary>
        static float CliffBlend(CliffRun r, float s)
        {
            float into = s - r.startS, left = r.EndS - s;
            return Smooth(0f, CliffRun.RampLength, into) * Smooth(0f, CliffRun.RampLength, left);
        }

        /// <summary>Half width of the road at s, ignoring splits.</summary>
        public float RoadHalfAt(float s)
        {
            CliffRun r = CliffAt(s);
            return r == null ? Diorama.RoadHalfWidth : Mathf.Lerp(Diorama.RoadHalfWidth, r.halfWidth, CliffBlend(r, s));
        }

        /// <summary>True when a jeep at road-relative X has gone over the edge of a cliff road.</summary>
        public bool OverAbyss(float xRel, float s)
        {
            CliffRun r = CliffAt(s);
            return r != null && CliffBlend(r, s) >= 0.5f && Mathf.Abs(xRel) > RoadHalfAt(s) + FallTolerance;
        }

        /// <summary>True along the thin, dangerous stretch of a cliff segment.</summary>
        public bool OnCliffStretch(float s)
        {
            CliffRun r = CliffAt(s);
            return r != null && CliffBlend(r, s) >= 0.5f;
        }

        /// <summary>
        /// Slides a terrain grid column so the road's edges always fall exactly on grid lines, however
        /// narrow the road gets. Everywhere the road is full width this changes nothing.
        /// </summary>
        float ColumnToRoadX(float column, float s)
        {
            float half = RoadHalfAt(s);
            if (Mathf.Approximately(half, Diorama.RoadHalfWidth)) return column;
            float abs = Mathf.Abs(column);
            float mapped = abs <= Diorama.RoadHalfWidth ? abs * half / Diorama.RoadHalfWidth : half + abs - Diorama.RoadHalfWidth;
            return column < 0f ? -mapped : mapped;
        }

        /// <summary>Signed distance from a road-relative X to the road surface. Negative means on the road.</summary>
        public float RoadDistance(float xRel, float s)
        {
            if (cliffs.Count > 0 && CliffAt(s) != null) return Mathf.Abs(xRel) - RoadHalfAt(s);
            Fork f = ForkAt(s);
            if (f == null) return Mathf.Abs(xRel) - Diorama.RoadHalfWidth;
            Branch(f, s, out float b, out float w);
            float left = Mathf.Abs(xRel + b) - w;
            float right = Mathf.Abs(xRel - b) - w;
            if (f.deadSide != 0 && s > f.WallS)
            {
                // Past the wall the dead branch narrows to the escape track through the forest.
                float live = f.deadSide < 0 ? right : left;
                float escape = Mathf.Abs(xRel - EscapeCenter(f, s)) - EscapeHalfWidth;
                return Mathf.Min(live, escape);
            }
            return Mathf.Min(left, right);
        }

        /// <summary>
        /// Maps a walker's spot in the crowd (-1 to 1) onto the road surface at s, so the horde spreads across
        /// the road, divides at a split without crossing the island, and squeezes through an escape track.
        /// </summary>
        public float WalkerRoadX(float frac, float s)
        {
            const float BodyMargin = 0.9f;
            frac = Mathf.Clamp(frac, -1f, 1f);
            float open = frac * Mathf.Max(RoadHalfAt(s) - BodyMargin, 0.25f);
            Fork f = ForkAt(s);
            if (f == null) return open;

            Branch(f, s, out float b, out float w);
            float side = frac < 0f ? -1f : 1f;
            float within = Mathf.Abs(frac) * 2f - 1f;   // spot inside this walker's own branch
            float inBranch = side * b + within * (w - BodyMargin);
            // Blend so the crowd neither pops at the split nor at the rejoin.
            float x = Mathf.Lerp(open, inBranch, Mathf.Clamp01(b / MaxBranchOffset * 4f));

            if (f.deadSide != 0 && side == f.deadSide)
            {
                float blend = Smooth(f.WallS - 3f, f.WallS, s);
                if (blend > 0f)
                {
                    float escape = EscapeCenter(f, Mathf.Max(s, f.WallS)) + within * Mathf.Max(EscapeHalfWidth - BodyMargin, 0.2f);
                    x = Mathf.Lerp(x, escape, blend);
                    // Spread back out as the track rejoins the road, so the crowd doesn't pop at the end.
                    x = Mathf.Lerp(x, open, Smooth(f.EndS - 2f, f.EndS, s));
                }
            }
            return x;
        }

        /// <summary>The road-relative range the car's centre may occupy at s. Side picks a branch once they separate.</summary>
        public void Limits(float s, int side, out float min, out float max)
        {
            CliffRun cliff = CliffAt(s);
            if (cliff != null)
            {
                // On the thin stretch nothing holds the jeep on the road: steering past the edge means falling.
                float half = RoadHalfAt(s);
                float reach = CliffBlend(cliff, s) >= 0.5f ? half + FallTolerance + 1f : half - CarMargin;
                min = -reach;
                max = reach;
                return;
            }
            Fork f = ForkAt(s);
            if (f == null)
            {
                min = -Diorama.RoadHalfWidth + CarMargin;
                max = Diorama.RoadHalfWidth - CarMargin;
                return;
            }
            Branch(f, s, out float b, out float w);
            if (InEscapeRoute(s, side))
            {
                float c = EscapeCenter(f, s);
                float room = EscapeHalfWidth - CarMargin;
                min = c - room;
                max = c + room;
                return;
            }
            bool useBranch = side != 0 && (b > w || f.deadSide != 0);
            if (useBranch)
            {
                float c = side * b;
                min = c - w + CarMargin;
                max = c + w - CarMargin;
            }
            else
            {
                min = -b - w + CarMargin;
                max = b + w - CarMargin;
            }
        }

        /// <summary>True once the branches at s are physically separated, so the car must be committed to one.</summary>
        public bool BranchesSeparated(float s)
        {
            Fork f = ForkAt(s);
            if (f == null) return false;
            Branch(f, s, out float b, out float w);
            return b > w;
        }

        float Height(float xRel, float s, float cliff)
        {
            float dist = RoadDistance(xRel, s);
            float shoulder = Smooth(0f, 2.6f, dist);
            float n = Mathf.PerlinNoise(xRel * 0.11f + 100f, s * 0.11f + 37f);
            float h = shoulder * (0.3f + 1.5f * n);
            CliffRun run = CliffAt(s);
            if (run != null)
            {
                if (dist <= 0f) return 0f;
                float drop = AbyssDepth * (0.8f + 0.4f * Mathf.PerlinNoise(xRel * 0.3f + 9f, s * 0.3f)) * Smooth(0f, 1f, dist);
                return Mathf.Lerp(h, -drop, CliffBlend(run, s));
            }
            if (cliff > 0f)
            {
                float wall = Smooth(1.2f, 3.2f, dist);
                h += wall * cliff * (4.2f + 1.2f * Mathf.PerlinNoise(xRel * 0.3f, s * 0.3f));
            }
            return h;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float CliffFactor(Chunk c, float xRel, float zInChunk)
        {
            if (c.cliffSide == 0 || Mathf.Sign(xRel) != c.cliffSide) return 0f;
            return Mathf.Pow(Mathf.Sin(Mathf.Clamp01(zInChunk / ChunkLength) * Mathf.PI), 0.6f);
        }

        // ---------- Lifecycle ----------

        void ResetForkPlan()
        {
            var rng = new System.Random(seed * 131 + 3);
            nextForkIndex = 6 + rng.Next(4);
            forkUntilIndex = -1;
            currentFork = null;
            nextCliffIndex = FirstCliffChunk + rng.Next(4);
            cliffUntilIndex = -1;
            cliffCount = 0;
        }

        public void Reset()
        {
            foreach (var c in chunks) DestroyChunk(c);
            chunks.Clear();
            hazards.Clear();
            pickups.Clear();
            forks.Clear();
            cliffs.Clear();
            EarlyPowerups = 0;
            nextIndex = 0;
            lastType = ChunkType.Quiet;
            lastWasHazard = false;
            ResetForkPlan();
        }

        /// <summary>Spawns, scrolls and retires chunks for the current travelled distance.</summary>
        public void Scroll(float distance, float carTargetX, int difficultyStep, float rockfallLead)
        {
            float farLocalZ = Diorama.Radius + Diorama.ScoutRange + 6f;
            while (nextIndex * ChunkLength - distance + Diorama.CarZ < farLocalZ)
                chunks.Add(CreateChunk(nextIndex++, difficultyStep));

            float shift = -Center(distance);
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                var c = chunks[i];
                float z = Diorama.CarZ + c.startS - distance;
                c.root.localPosition = new Vector3(shift, 0f, z);
                if (z + ChunkLength < -Diorama.Radius - 8f)
                {
                    DestroyChunk(c);
                    chunks.RemoveAt(i);
                    continue;
                }
                if (c.type == ChunkType.Rockfall && !c.rockfallTriggered)
                {
                    float triggerS = c.startS + ChunkLength * 0.5f;
                    if (triggerS - distance <= rockfallLead) TriggerRockfall(c, distance, carTargetX, rockfallLead);
                }
            }
            hazards.RemoveAll(h => h == null || (!h.alive && !h.gameObject.activeSelf));
            pickups.RemoveAll(p => p == null || !p.alive);
            forks.RemoveAll(f => f.EndS < distance - Diorama.Radius - ChunkLength * 2f);
            cliffs.RemoveAll(r => r.EndS < distance - Diorama.Radius - ChunkLength * 2f);
        }

        void DestroyChunk(Chunk c)
        {
            foreach (var h in c.hazards) hazards.Remove(h);
            foreach (var p in c.pickups) pickups.Remove(p);
            if (c.terrainMesh != null) Object.DestroyImmediate(c.terrainMesh);
            if (c.root != null) Object.DestroyImmediate(c.root.gameObject);
        }

        // ---------- Generation ----------

        Chunk CreateChunk(int index, int difficultyStep)
        {
            var rng = new System.Random(MixSeed(seed, index));
            var c = new Chunk { index = index, startS = index * ChunkLength };
            c.type = PickType(index, rng, difficultyStep);
            if (c.type == ChunkType.Rockfall) c.cliffSide = rng.NextDouble() < 0.5 ? -1 : 1;
            else if (c.type != ChunkType.Fork && c.type != ChunkType.Cliff && rng.NextDouble() < 0.15) c.cliffSide = rng.NextDouble() < 0.5 ? -1 : 1;

            var go = new GameObject($"Chunk {index} ({c.type})");
            go.transform.SetParent(parent, false);
            c.root = go.transform;

            var mb = new MeshBuilder();
            BuildTerrain(mb, c);
            BuildEdgePosts(mb, c);
            BuildScenery(mb, c, rng);
            c.terrainMesh = mb.ToMesh($"LL_Chunk_{index}");
            MeshBuilder.Spawn("Terrain", c.root, c.terrainMesh, terrainMat);

            if (c.type == ChunkType.Blocked1 || c.type == ChunkType.Blocked2) PlaceBlocker(c, rng);
            if (c.type == ChunkType.Fork) PlaceForkContents(c, rng);
            PlacePickups(c, rng);

            lastType = c.type;
            lastWasHazard = c.type != ChunkType.Quiet;
            return c;
        }

        /// <summary>Scrambles seed and chunk index, because System.Random's first values track similar seeds closely.</summary>
        static int MixSeed(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return (int)(h & 0x7fffffff);
            }
        }

        ChunkType PickType(int index, System.Random rng, int difficultyStep)
        {
            if (index < forkUntilIndex) return ChunkType.Fork;
            if (index < cliffUntilIndex) return ChunkType.Cliff;
            if (index >= nextCliffIndex)
            {
                StartCliff(index, rng);
                return ChunkType.Cliff;
            }
            if (index >= nextForkIndex)
            {
                StartFork(index, rng);
                return ChunkType.Fork;
            }
            if (index < 3) return ChunkType.Quiet;
            float breather = Mathf.Lerp(0.75f, 0.35f, Mathf.Clamp01(difficultyStep / 8f));
            if (lastWasHazard && rng.NextDouble() < breather) return ChunkType.Quiet;
            double r = rng.NextDouble();
            bool allowWide = index > 8;
            if (r < 0.25) return ChunkType.Quiet;
            if (r < 0.60) return ChunkType.Blocked1;
            if (r < 0.78 && allowWide) return ChunkType.Blocked2;
            return lastType == ChunkType.Rockfall ? ChunkType.Blocked1 : ChunkType.Rockfall;
        }

        void StartFork(int index, System.Random rng)
        {
            int chunksLong = 3 + rng.Next(2);
            var f = new Fork { startS = index * ChunkLength, length = chunksLong * ChunkLength };
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            if (index >= DeadEndsFromChunk && rng.NextDouble() < 0.6) f.deadSide = side;
            else f.obstacleSide = side;
            forks.Add(f);
            currentFork = f;
            OnForkPlanned?.Invoke(f);
            forkUntilIndex = index + chunksLong;
            nextForkIndex = forkUntilIndex + 6 + rng.Next(6);
            nextCliffIndex = Mathf.Max(nextCliffIndex, forkUntilIndex + 2);
        }

        void StartCliff(int index, System.Random rng)
        {
            int chunksLong = 3 + rng.Next(2);
            var r = new CliffRun
            {
                startS = index * ChunkLength,
                length = chunksLong * ChunkLength,
                halfWidth = Mathf.Max(NarrowestCliffHalfWidth, FirstCliffHalfWidth - cliffCount * CliffNarrowingStep)
            };
            cliffs.Add(r);
            cliffCount++;
            OnCliffPlanned?.Invoke(r);
            cliffUntilIndex = index + chunksLong;
            nextCliffIndex = cliffUntilIndex + 12 + rng.Next(8);
            nextForkIndex = Mathf.Max(nextForkIndex, cliffUntilIndex + 2);
        }

        void BuildTerrain(MeshBuilder mb, Chunk c)
        {
            int nx = Mathf.CeilToInt(2f * SideExtent / CellX);
            int nz = Mathf.CeilToInt(ChunkLength / CellZ);
            float dz = ChunkLength / nz;
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float x0 = -SideExtent + ix * CellX, x1 = x0 + CellX;
                    float z0 = iz * dz, z1 = z0 + dz;
                    Vector3 a = P(c, x0, z0), b = P(c, x0, z1), cc = P(c, x1, z1), d = P(c, x1, z0);
                    float sc = c.startS + (z0 + z1) * 0.5f;
                    float xc = ColumnToRoadX((x0 + x1) * 0.5f, sc);
                    float hAvg = (a.y + b.y + cc.y + d.y) * 0.25f;
                    float slope = Mathf.Max(Mathf.Abs(a.y - cc.y), Mathf.Abs(b.y - d.y));
                    float dist = RoadDistance(xc, sc);
                    Color col;
                    if (dist < 0f)
                    {
                        float v = Mathf.PerlinNoise(xc * 0.7f, sc * 0.7f);
                        col = Palette.Road * (0.88f + 0.22f * v);
                    }
                    else if (hAvg < -0.3f) col = Palette.Stone * (0.8f + 0.5f * Mathf.PerlinNoise(xc * 0.6f, sc * 0.6f));
                    else if (dist < 0.9f) col = Palette.RoadDark;
                    else if (slope > 1.4f) col = Palette.Cliff;
                    else
                    {
                        float v = Mathf.PerlinNoise(xc * 0.35f + 5f, sc * 0.35f);
                        col = Color.Lerp(Palette.FloorDark, Palette.Moss, v * 0.8f);
                        if (hAvg > 4f) col = Color.Lerp(col, Palette.Rock, 0.4f);
                    }
                    col.a = 1f;
                    mb.Quad(a, b, cc, d, col);
                }
            }
        }

        Vector3 P(Chunk c, float xRel, float z)
        {
            float s = c.startS + z;
            xRel = ColumnToRoadX(xRel, s);
            return new Vector3(Center(s) + xRel, Height(xRel, s, CliffFactor(c, xRel, z)), z);
        }

        /// <summary>Pale reflector posts along every road edge, so bends and splits read at a glance.</summary>
        void BuildEdgePosts(MeshBuilder mb, Chunk c)
        {
            for (float z = 1f; z < ChunkLength; z += 3f)
            {
                float s = c.startS + z;
                float cx = Center(s);
                Fork f = ForkAt(s);
                if (f == null)
                {
                    float half = RoadHalfAt(s);
                    AddPost(mb, cx - half, z);
                    AddPost(mb, cx + half, z);
                    continue;
                }
                Branch(f, s, out float b, out float w);
                bool deadGone = f.deadSide != 0 && s > f.WallS;
                if (!(deadGone && f.deadSide < 0)) AddPost(mb, cx - b - w, z);
                if (!(deadGone && f.deadSide > 0)) AddPost(mb, cx + b + w, z);
                if (b > w)
                {
                    if (!(deadGone && f.deadSide < 0)) AddPost(mb, cx - b + w, z);
                    if (!(deadGone && f.deadSide > 0)) AddPost(mb, cx + b - w, z);
                }
                if (deadGone)
                {
                    float e = EscapeCenter(f, s);
                    AddPost(mb, cx + e - EscapeHalfWidth, z);
                    AddPost(mb, cx + e + EscapeHalfWidth, z);
                }
            }
        }

        static void AddPost(MeshBuilder mb, float x, float z)
        {
            mb.Box(new Vector3(x, 0.28f, z), new Vector3(0.14f, 0.56f, 0.14f), Palette.LaneMark);
        }

        void BuildScenery(MeshBuilder mb, Chunk c, System.Random rng)
        {
            int trees = c.type == ChunkType.Fork ? 20 + rng.Next(6) : 12 + rng.Next(6);
            for (int i = 0; i < trees; i++)
            {
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * (SideExtent - 1f);
                float z = (float)rng.NextDouble() * ChunkLength;
                float s = c.startS + z;
                if (RoadDistance(xRel, s) < 1.5f) continue;
                if (CliffAt(s) != null) continue;
                float y = Height(xRel, s, CliffFactor(c, xRel, z));
                float scale = 0.8f + (float)rng.NextDouble() * 0.6f;
                AddPine(mb, new Vector3(Center(s) + xRel, y - 0.1f, z), scale, rng.NextDouble() < 0.5);
            }
            int rocks = 3 + rng.Next(4);
            for (int i = 0; i < rocks; i++)
            {
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * 16f;
                float z = (float)rng.NextDouble() * ChunkLength;
                float s = c.startS + z;
                if (RoadDistance(xRel, s) < 0.8f) continue;
                if (CliffAt(s) != null) continue;
                float y = Height(xRel, s, CliffFactor(c, xRel, z));
                mb.Rock(new Vector3(Center(s) + xRel, y + 0.2f, z), 0.4f + (float)rng.NextDouble() * 0.6f, rng.Next(), Palette.Rock);
            }
            if (c.type == ChunkType.Cliff)
            {
                // Broken rock hanging over the drop, so the abyss reads as a shattered cliff.
                int shards = 6 + rng.Next(5);
                for (int i = 0; i < shards; i++)
                {
                    float z = (float)rng.NextDouble() * ChunkLength;
                    float s = c.startS + z;
                    float xRel = (rng.NextDouble() < 0.5 ? -1f : 1f) * (RoadHalfAt(s) + 1.5f + (float)rng.NextDouble() * 9f);
                    float y = -1.5f - (float)rng.NextDouble() * 3f;
                    mb.Rock(new Vector3(Center(s) + xRel, y, z), 0.7f + (float)rng.NextDouble() * 1.1f, rng.Next(), Palette.Cliff);
                }
            }
            if (c.type == ChunkType.Rockfall)
            {
                // Loose boulders on the cliff edge: the readable "rockfall here" signal when scouted.
                for (int i = 0; i < 4; i++)
                {
                    float xRel = c.cliffSide * (Diorama.RoadHalfWidth + 3.6f + (float)rng.NextDouble() * 2f);
                    float z = ChunkLength * 0.3f + (float)rng.NextDouble() * ChunkLength * 0.4f;
                    float s = c.startS + z;
                    float y = Height(xRel, s, CliffFactor(c, xRel, z));
                    mb.Rock(new Vector3(Center(s) + xRel, y + 0.5f, z), 0.6f + (float)rng.NextDouble() * 0.4f, rng.Next(), Palette.Rock * 1.1f);
                }
            }
        }

        static void AddPine(MeshBuilder mb, Vector3 basePos, float scale, bool dark)
        {
            Color crown = dark ? Palette.PineDark : Palette.Pine;
            mb.Cylinder(basePos, 0.16f * scale, 0.8f * scale, 5, Palette.Trunk, 0.12f * scale);
            mb.Cone(basePos + Vector3.up * 0.6f * scale, 1.0f * scale, 1.7f * scale, 6, crown, 0.3f);
            mb.Cone(basePos + Vector3.up * 1.5f * scale, 0.72f * scale, 1.5f * scale, 6, crown * 1.08f, 0.8f);
        }

        /// <summary>A fallen tree on the open road. It always leaves a gap wide enough for the car on one side.</summary>
        void PlaceBlocker(Chunk c, System.Random rng)
        {
            bool wide = c.type == ChunkType.Blocked2;
            float halfWidth = wide ? 2.6f : 1.3f;
            float maxOffset = Diorama.RoadHalfWidth - halfWidth;
            float offsetFrac = wide ? (rng.NextDouble() < 0.5 ? -1f : 1f) : (float)rng.NextDouble() * 2f - 1f;
            float xRel = offsetFrac * maxOffset;
            float z = ChunkLength * 0.5f;
            SpawnBlocker(c, wide ? logMesh2 : (rng.NextDouble() < 0.5 ? logMesh1 : fallenPineMesh), xRel, z, halfWidth, rng);
        }

        void SpawnBlocker(Chunk c, Mesh mesh, float xRel, float z, float halfWidth, System.Random rng)
        {
            var h = SpawnHazard(c, HazardKind.FallenTree, mesh);
            h.halfWidth = halfWidth;
            h.transform.localPosition = new Vector3(Center(c.startS + z) + xRel, 0f, z);
            h.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 14f, 0f);
        }

        void PlaceForkContents(Chunk c, System.Random rng)
        {
            Fork f = currentFork;
            if (f == null) return;
            float chunkEnd = c.startS + ChunkLength;

            if (f.deadSide != 0 && f.WallS >= c.startS && f.WallS < chunkEnd)
            {
                Branch(f, f.WallS, out float b, out float w);
                float z = f.WallS - c.startS;
                var wall = SpawnHazard(c, HazardKind.DeadEnd, f.deadSide < 0 ? wallGapLeftMesh : wallGapRightMesh);
                wall.halfWidth = w + 1f;
                wall.escapeX = EscapeCenter(f, f.WallS);
                wall.transform.localPosition = new Vector3(Center(f.WallS) + f.deadSide * b, 0f, z);
            }

            if (f.obstacleSide != 0)
            {
                float[] spots = { 0.4f, 0.52f, 0.64f };
                for (int i = 0; i < spots.Length; i++)
                {
                    float s = f.startS + f.length * spots[i];
                    if (s < c.startS || s >= chunkEnd) continue;
                    Branch(f, s, out float b, out float w);
                    float xRel = f.obstacleSide * b + (i % 2 == 0 ? 1.5f : -1.5f);
                    SpawnBlocker(c, rng.NextDouble() < 0.5 ? logMesh1 : fallenPineMesh, xRel, s - c.startS, 1.3f, rng);
                }
            }
        }

        void PlacePickups(Chunk c, System.Random rng)
        {
            // Powerups start late and stay rare at first, then get a little more common as a run goes on.
            if (c.type != ChunkType.Cliff && c.index >= FirstPowerupChunk)
            {
                bool first = c.index == FirstPowerupChunk;
                double chance = Mathf.Lerp(0.04f, 0.16f, Mathf.Clamp01((c.index - FirstPowerupChunk) / 50f));
                if (first || rng.NextDouble() < chance)
                {
                    double r = rng.NextDouble();
                    SpawnPowerup(c, first ? PickupKind.Booster : r < 0.4 ? PickupKind.Booster : r < 0.7 ? PickupKind.Shield : PickupKind.Flashlight, rng);
                }
            }
            if (c.index >= 1 && rng.NextDouble() < 0.75) SpawnCoinTrail(c, rng);
        }

        /// <summary>A short curving line of coins at jeep height, laid along the road to drive through.</summary>
        void SpawnCoinTrail(Chunk c, System.Random rng)
        {
            int count = 3 + rng.Next(4);
            const float Spacing = 2f;
            float z = (float)rng.NextDouble() * Mathf.Max(0.5f, ChunkLength - count * Spacing);
            float frac = ((float)rng.NextDouble() * 2f - 1f) * 0.8f;
            float drift = ((float)rng.NextDouble() - 0.5f) * 0.14f;
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            for (int i = 0; i < count && z < ChunkLength; i++, z += Spacing, frac += drift)
            {
                float s = c.startS + z;
                float half = RoadHalfAt(s) - 0.9f;
                float xRel = Mathf.Clamp(frac, -1f, 1f) * Mathf.Max(half, 0.2f);
                Fork f = ForkAt(s);
                if (f != null)
                {
                    Branch(f, s, out float b, out float w);
                    if (b > w) xRel = side * b + Mathf.Clamp(frac, -1f, 1f) * (w - 0.9f);
                }
                if (RoadDistance(xRel, s) > -0.3f) continue;
                var p = Pickup.Create(PickupKind.Coin, c, pickupMat, new Vector3(Center(s) + xRel, 1f, z), z);
                c.pickups.Add(p);
                pickups.Add(p);
            }
        }

        void SpawnPowerup(Chunk c, PickupKind kind, System.Random rng)
        {
            float z = (float)rng.NextDouble() * ChunkLength;
            float s = c.startS + z;
            // Powerups sit off the road on the terrain beside it, so finding them takes scouting.
            float xRel = OffRoadSpot(s, rng);
            float y = Height(xRel, s, CliffFactor(c, xRel, z)) + 2.4f;
            var p = Pickup.Create(kind, c, pickupMat, new Vector3(Center(s) + xRel, y, z), (float)rng.NextDouble() * 6f);
            c.pickups.Add(p);
            pickups.Add(p);
            if (s < 300f) EarlyPowerups++;
        }

        /// <summary>A road-relative X clearly off the road but still close enough to scout, on either side.</summary>
        float OffRoadSpot(float s, System.Random rng)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * 15f;
                float dist = RoadDistance(xRel, s);
                if (dist >= 2.5f && dist <= 8f) return xRel;
            }
            return (rng.NextDouble() < 0.5 ? -1f : 1f) * (Diorama.RoadHalfWidth + 5f);
        }

        void TriggerRockfall(Chunk c, float distance, float carTargetX, float rockfallLead)
        {
            c.rockfallTriggered = true;
            var h = SpawnHazard(c, HazardKind.Boulder, boulderMesh);
            h.radius = 1.0f;
            float zInChunk = distance + rockfallLead - c.startS;
            float s = c.startS + zInChunk;
            float cx = Center(s);
            float cliffRel = c.cliffSide * (Diorama.RoadHalfWidth + 4f);
            float targetRel = Mathf.Clamp(carTargetX, -Diorama.RoadHalfWidth + 1f, Diorama.RoadHalfWidth - 1f);
            Vector3 from = new Vector3(cx + cliffRel, Height(cliffRel, s, 1f) + 1.5f, zInChunk);
            Vector3 to = new Vector3(cx + targetRel, h.radius * 0.8f, zInChunk);
            var peb = MeshBuilder.Spawn("Pebbles", h.transform, pebbleMesh, terrainMat);
            peb.gameObject.SetActive(false);
            h.pebbles = peb.transform;
            h.StartFall(from, to);
            OnRockfallWarning?.Invoke(h);
        }

        Hazard SpawnHazard(Chunk c, HazardKind kind, Mesh mesh)
        {
            var r = MeshBuilder.Spawn(kind.ToString(), c.root, mesh, terrainMat);
            var h = r.gameObject.AddComponent<Hazard>();
            h.kind = kind;
            h.chunk = c;
            h.state = kind == HazardKind.Boulder ? BoulderState.Warning : BoulderState.Landed;
            c.hazards.Add(h);
            hazards.Add(h);
            return h;
        }

        // ---------- Shared meshes ----------

        static void BuildSharedMeshes()
        {
            if (logMesh1 != null) return;
            var mb = new MeshBuilder();
            Quaternion lie = Quaternion.Euler(0f, 0f, 90f);

            mb.Box(new Vector3(0f, 0.35f, 0f), new Vector3(0.5f, 2.6f, 0.5f), lie, Palette.Trunk);
            mb.Cone(new Vector3(0.9f, 0.3f, 0f), 0.55f, 0.9f, 5, Palette.PineDark, 0.2f);
            mb.Cone(new Vector3(-0.6f, 0.3f, 0.25f), 0.45f, 0.7f, 5, Palette.Pine, 0.5f);
            fallenPineMesh = mb.ToMesh("LL_FallenPine"); mb.Clear();

            mb.Box(new Vector3(0f, 0.32f, 0f), new Vector3(0.55f, 2.6f, 0.55f), lie, Palette.Trunk * 1.1f);
            mb.Rock(new Vector3(0.9f, 0.3f, -0.4f), 0.35f, 11, Palette.Rock);
            logMesh1 = mb.ToMesh("LL_Log"); mb.Clear();

            mb.Box(new Vector3(0f, 0.38f, 0f), new Vector3(0.65f, 5.2f, 0.65f), lie, Palette.Trunk);
            mb.Cone(new Vector3(2.2f, 0.4f, 0.1f), 0.8f, 1.1f, 6, Palette.PineDark, 0.1f);
            mb.Cone(new Vector3(1.3f, 0.4f, -0.2f), 0.6f, 0.9f, 6, Palette.Pine, 0.6f);
            mb.Rock(new Vector3(-2.3f, 0.3f, 0.4f), 0.45f, 23, Palette.Rock);
            logMesh2 = mb.ToMesh("LL_FallenTreeWide"); mb.Clear();

            mb.Rock(Vector3.zero, 1.0f, 5, Palette.Rock * 1.15f);
            boulderMesh = mb.ToMesh("LL_Boulder"); mb.Clear();

            for (int i = 0; i < 5; i++)
                mb.Rock(new Vector3((i - 2) * 0.35f, (i % 2) * 0.5f, (i % 3) * 0.3f), 0.16f, 40 + i, Palette.Rock);
            pebbleMesh = mb.ToMesh("LL_Pebbles"); mb.Clear();

            wallGapRightMesh = BuildWallMesh(mb, 1, "LL_DeadEndWall_GapRight"); mb.Clear();
            wallGapLeftMesh = BuildWallMesh(mb, -1, "LL_DeadEndWall_GapLeft"); mb.Clear();
        }

        /// <summary>
        /// Dead-end barricade: a rock pile with striped warning boards closing the branch, except for one
        /// narrow gap on the outer side, marked by two bright green posts.
        /// </summary>
        static Mesh BuildWallMesh(MeshBuilder mb, int gapSide, string name)
        {
            float gapInner = EscapeGapOffset - EscapeHalfWidth; // gap edge nearest the wall, in wall-local X
            float gapOuter = EscapeGapOffset + EscapeHalfWidth;
            float wallFar = -BranchHalfWidth - 1f;
            float wallWidth = gapInner - wallFar;
            float wallMid = (gapInner + wallFar) * 0.5f;
            int rocks = 5;
            for (int i = 0; i < rocks; i++)
            {
                float t = (i + 0.5f) / rocks;
                float x = Mathf.Lerp(wallFar + 0.4f, gapInner - 0.6f, t);
                mb.Rock(new Vector3(gapSide * x, 0.8f + (i % 2) * 0.5f, (i % 3 - 1) * 0.35f), 0.95f, 60 + i, Palette.Cliff * 1.2f);
            }
            for (int i = 0; i < 2; i++)
            {
                mb.Box(new Vector3(gapSide * wallMid, 2.6f + i * 0.75f, -0.85f), new Vector3(wallWidth, 0.42f, 0.14f), Palette.WarningBoard);
                for (float x = wallFar + 0.5f; x < gapInner - 0.3f; x += 1.2f)
                    mb.Box(new Vector3(gapSide * x, 2.6f + i * 0.75f, -0.94f), new Vector3(0.5f, 0.42f, 0.04f), Palette.Trunk);
            }
            // A low boulder closes the sliver between the gap and the forest on the outer side.
            mb.Rock(new Vector3(gapSide * (gapOuter + 0.8f), 0.6f, 0f), 0.8f, 71, Palette.Cliff * 1.2f);
            // Gap markers.
            mb.Box(new Vector3(gapSide * gapInner, 1.5f, -0.5f), new Vector3(0.22f, 3f, 0.22f), Palette.EscapeMarker);
            mb.Box(new Vector3(gapSide * gapOuter, 1.5f, -0.5f), new Vector3(0.22f, 3f, 0.22f), Palette.EscapeMarker);
            return mb.ToMesh(name);
        }
    }
}
