using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum ChunkType { Quiet, Blocked1, Blocked2, Rockfall, Fork, Cliff }

    /// <summary>How far into a run: early under 300 m, medium to 700 m, late after that.</summary>
    public enum GameStage { Early, Medium, Late }

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
        public readonly List<ShooterZombie> shooters = new List<ShooterZombie>();
        public bool rockfallTriggered;
        public Mesh terrainMesh;
        public readonly List<Mesh> propMeshes = new List<Mesh>();   // merged Tripo pines, rocks and grass
        public Transform puddles;                                     // shown while the road is wet
    }

    /// <summary>
    /// A split in the road. Two branches open around a wooded island and rejoin later.
    /// Early forks load one branch with obstacles; later forks can wall one branch off as a dead end.
    /// From the medium stage a split can have three branches (left, middle, right) round two islands, with
    /// barricades across some of them: first one of the three, later two, and further on all three.
    /// Branch sides are -1 left, +1 right and <see cref="Middle"/> for the middle one.
    /// </summary>
    public class Fork
    {
        public const int Middle = 2;
        public float startS, length;
        public int deadSide;        // -1 or +1 when that branch is a dead end
        public int obstacleSide;    // -1 or +1 when that branch holds extra obstacles
        public int ways = 2;        // 2 or 3 branches
        public readonly bool[] blocked = new bool[3];   // three-way: barricaded branches, indexed left, middle, right
        public float maxOffset = 7.5f;      // how far the outer branches open from the centreline
        public float branchHalf = 3f;       // half width of each branch once open
        public bool wallBroken;     // a shovel smashed the dead-end wall
        public int halfIndex = -1;  // three-way, all blocked: this branch (left, middle, right) only has half a barricade
        public int halfK;           // which side of that branch the half barricade stands on (-1 or +1); the gap is the other side
        public bool HalfBlocked(int side) => ThreeWay && side != 0 && halfIndex == Index(side);
        public bool ThreeWay => ways == 3;
        public static int Index(int side) => side == Middle ? 1 : side < 0 ? 0 : 2;
        public bool Blocked(int side) => ThreeWay && side != 0 && blocked[Index(side)];
        public int BlockedCount => (blocked[0] ? 1 : 0) + (blocked[1] ? 1 : 0) + (blocked[2] ? 1 : 0);
        /// <summary>Where a branch's centre sits for a given outer-branch offset.</summary>
        public static float BranchX(int side, float offset) => side == Middle ? 0f : side * offset;
        public float EndS => startS + length;
        public float WallS => startS + length * WallFraction;
        public const float WallFraction = 0.72f;
    }

    /// <summary>
    /// A broken cliff segment. The ground falls away into the abyss on both sides, leaving a thin strip
    /// of road. Each segment later in a run is narrower than the last.
    /// Later in a run a cliff road can be twin: two narrow strips side by side over the abyss, one narrower than
    /// the other, joined halfway along by a short ridge where the jeep can switch. The first twin is safe both
    /// ways; after that one strip is a dead end that stops in a sheer drop, so the ridge is the way out.
    /// </summary>
    public class CliffRun
    {
        public const float RampLength = 8f;
        public const float RidgeLength = 10f;
        public float startS, length, halfWidth;
        public float EndS => startS + length;

        public bool twin;
        public float laneOffset = 2.7f;     // each strip's centre sits this far either side of the centreline
        public float leftHalf, rightHalf;   // half widths of the left and right strips
        public int deadSide;                // -1 or +1 when that strip ends in a drop
        public float ridgeS;                // centre of the connecting ridge
        public float deadEndS;              // where the dead strip stops
        public int SafeSide => deadSide != 0 ? -deadSide : (leftHalf >= rightHalf ? -1 : 1);
        public float Half(int side) => side < 0 ? leftHalf : rightHalf;
        public bool OnRidge(float s) => twin && Mathf.Abs(s - ridgeS) <= RidgeLength * 0.5f;
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
        // Vertex-colour alpha on the terrain picks the ground shader's texture layer; 1 stays plain (stone, cliffs).
        public const float LayerRoad = 0.25f, LayerDirt = 0.5f, LayerGrass = 0.75f;
        const float CarMargin = Vehicle.HalfWidth;
        const int DeadEndsFromChunk = 18;
        const int FirstCliffChunk = 30;
        const float AbyssDepth = 6.5f;
        const float FirstCliffHalfWidth = 3f;
        const float CliffNarrowingStep = 0.35f;
        const float NarrowestCliffHalfWidth = 1.25f;
        const float FallTolerance = 0.25f;      // how far past the edge the jeep's centre can go before it drops
        const int FirstPowerupChunk = 10;
        /// <summary>Cliff roads from here on are twin strips.</summary>
        public const float TwinCliffsFrom = 600f;
        /// <summary>Three-way splits start with the medium stage.</summary>
        public const float ThreeWayFrom = MediumStageFrom;
        /// <summary>Three-way splits from here on usually have two of the three branches barricaded.</summary>
        public const float TwoBlockedFrom = 650f;
        /// <summary>And from here on often all three.</summary>
        public const float AllBlockedFrom = 1000f;
        const float ThreeWayOffset = 8.6f, ThreeWayHalf = 2.5f;
        public const float BarricadeHalfWidth = 1.32f;
        static readonly int GuaranteedFlashlightChunk = Mathf.CeilToInt(450f / ChunkLength);
        public const float MediumStageFrom = 300f;
        public const float LateStageFrom = 700f;
        public static readonly int FirstShooterChunk = Mathf.CeilToInt(MediumStageFrom / ChunkLength);
        static readonly int RampEndChunk = Mathf.CeilToInt(1100f / ChunkLength);    // spitters reach full strength here

        public static GameStage StageAt(float s) => s >= LateStageFrom ? GameStage.Late : s >= MediumStageFrom ? GameStage.Medium : GameStage.Early;

        public readonly List<Chunk> chunks = new List<Chunk>();
        public readonly List<Hazard> hazards = new List<Hazard>();
        public readonly List<Pickup> pickups = new List<Pickup>();
        public readonly List<Fork> forks = new List<Fork>();
        public readonly List<CliffRun> cliffs = new List<CliffRun>();
        public readonly List<ShooterZombie> shooters = new List<ShooterZombie>();

        /// <summary>Powerups generated in the first 300 m of this run. For tests.</summary>
        public int EarlyPowerups { get; private set; }

        /// <summary>Shooter zombies generated before the medium stage. Must stay zero. For tests.</summary>
        public int EarlyShooters { get; private set; }
        public int ShootersSpawned { get; private set; }
        /// <summary>Travel distance of every shooter generated this run, in order. For tests.</summary>
        public readonly List<float> ShooterSpawnS = new List<float>();
        /// <summary>Distance of the guaranteed shield placed before the first shooters, or -1. For tests.</summary>
        public float GuaranteedShieldS { get; private set; } = -1f;
        public int TurretsSpawnedEarly { get; private set; }
        /// <summary>Distance of the flashlight always placed before the weather can start, or -1. For tests.</summary>
        public float GuaranteedFlashlightS { get; private set; } = -1f;
        /// <summary>Three-way splits generated so far, counted by how many branches are barricaded (index 1 to 3). For tests.</summary>
        public readonly int[] ThreeWaySplits = new int[4];
        /// <summary>Distance where each three-way split starts, with how many branches it blocks. For tests.</summary>
        public readonly List<Vector2> ThreeWayPlan = new List<Vector2>();
        /// <summary>Fully barricaded three-way splits that got a half barricade (a narrow gap). For tests.</summary>
        public int ThreeWayHalfGaps { get; private set; }
        /// <summary>Twin cliff roads planned so far, and how many had a dead end. For tests.</summary>
        public int TwinCliffs { get; private set; }
        public int TwinDeadEnds { get; private set; }

        readonly Transform parent;
        readonly Material terrainMat, groundMat, pickupMat;
        readonly int seed;
        readonly float phase1, phase2;
        int nextIndex;
        int nextForkIndex;
        int forkUntilIndex;
        Fork currentFork;
        int nextCliffIndex, cliffUntilIndex, cliffCount, twinCount;
        bool flashlightPlaced;
        Material puddleMat;
        ChunkType lastType = ChunkType.Quiet;
        bool lastWasHazard;

        static Mesh logMesh1, logMesh2, fallenPineMesh, boulderMesh, pebbleMesh, wallGapLeftMesh, wallGapRightMesh, barricadeMesh;
        static Mesh[] branchMeshes;
        static readonly float[] intervals = new float[6];

        public System.Action<Hazard> OnRockfallWarning;
        public System.Action<Fork> OnForkPlanned;
        public System.Action<CliffRun> OnCliffPlanned;

        public TrackGenerator(Transform parent, Material terrainMat, Material pickupMat, int seed, Material groundMat = null)
        {
            this.parent = parent;
            this.terrainMat = terrainMat;
            this.groundMat = groundMat != null ? groundMat : terrainMat;
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

        /// <summary>The generated chunk that holds travel distance s, if any.</summary>
        public Chunk ChunkAt(float s)
        {
            for (int i = 0; i < chunks.Count; i++)
                if (s >= chunks[i].startS && s < chunks[i].startS + ChunkLength) return chunks[i];
            return null;
        }

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
            offset = f.maxOffset * open;
            halfWidth = Mathf.Lerp(Diorama.RoadHalfWidth, f.branchHalf, open);
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

        /// <summary>Half width of the road at s, ignoring splits. A twin cliff road reports the full width it spans.</summary>
        public float RoadHalfAt(float s)
        {
            CliffRun r = CliffAt(s);
            if (r == null || r.twin) return Diorama.RoadHalfWidth;
            return Mathf.Lerp(Diorama.RoadHalfWidth, r.halfWidth, CliffBlend(r, s));
        }

        /// <summary>True when a jeep at road-relative X has gone over the edge of a cliff road.</summary>
        public bool OverAbyss(float xRel, float s)
        {
            CliffRun r = CliffAt(s);
            if (r == null || CliffBlend(r, s) < 0.5f) return false;
            float dist = r.twin ? TwinDistance(r, xRel, s) : Mathf.Abs(xRel) - RoadHalfAt(s);
            return dist > FallTolerance;
        }

        /// <summary>The twin cliff road at s, if s is on one.</summary>
        public CliffRun TwinAt(float s)
        {
            CliffRun r = CliffAt(s);
            return r != null && r.twin ? r : null;
        }

        /// <summary>
        /// The road surface of a twin cliff road at s as up to three intervals of road-relative X: the left strip,
        /// the right strip and, along the ridge, the bridge between them. Where the road splits into the strips
        /// (and where the safe one widens back out) the intervals blend from the full road. Past the dead end the
        /// dead strip is gone. Returns how many intervals were written to <see cref="intervals"/> as (from, to) pairs;
        /// lanes counts only the strips.
        /// </summary>
        static int TwinIntervals(CliffRun r, float s, out int lanes)
        {
            float b = CliffBlend(r, s), H = Diorama.RoadHalfWidth;
            float l0 = -r.laneOffset - r.leftHalf, l1 = -r.laneOffset + r.leftHalf;
            float r0 = r.laneOffset - r.rightHalf, r1 = r.laneOffset + r.rightHalf;
            int n = 0;
            if (r.deadSide != 0 && s >= r.deadEndS)
            {
                // Only the safe strip carries on, widening back into the full road at the end.
                float a = r.deadSide < 0 ? r0 : l0, c = r.deadSide < 0 ? r1 : l1;
                intervals[n++] = Mathf.Lerp(-H, a, b); intervals[n++] = Mathf.Lerp(H, c, b);
                lanes = 1;
                return 1;
            }
            float la = Mathf.Lerp(-H, l0, b), lb = Mathf.Lerp(0f, l1, b);
            float ra = Mathf.Lerp(0f, r0, b), rb = Mathf.Lerp(H, r1, b);
            intervals[n++] = la; intervals[n++] = lb;
            intervals[n++] = ra; intervals[n++] = rb;
            lanes = 2;
            if (r.OnRidge(s)) { intervals[n++] = lb; intervals[n++] = ra; return 3; }
            return 2;
        }

        /// <summary>Signed distance from a road-relative X to the nearest road surface on a twin cliff road.</summary>
        static float TwinDistance(CliffRun r, float xRel, float s)
        {
            int n = TwinIntervals(r, s, out _);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++) best = Mathf.Min(best, Mathf.Max(intervals[i * 2] - xRel, xRel - intervals[i * 2 + 1]));
            return best;
        }

        /// <summary>Centre and half width of one strip of a twin cliff road at s (side -1 left, +1 right), or false if it's gone.</summary>
        public bool TwinLane(float s, int side, out float center, out float half)
        {
            center = half = 0f;
            CliffRun r = TwinAt(s);
            if (r == null) return false;
            int n = TwinIntervals(r, s, out int lanes);
            if (lanes == 1)
            {
                if (side == r.deadSide) return false;
                center = (intervals[0] + intervals[1]) * 0.5f; half = (intervals[1] - intervals[0]) * 0.5f;
                return true;
            }
            int i = side < 0 ? 0 : 1;
            center = (intervals[i * 2] + intervals[i * 2 + 1]) * 0.5f;
            half = (intervals[i * 2 + 1] - intervals[i * 2]) * 0.5f;
            return true;
        }

        /// <summary>A road-relative X that is safely on the road at s: the safe strip of a twin cliff road, else the centre.</summary>
        public float SafeRoadX(float s)
        {
            CliffRun r = TwinAt(s);
            if (r == null) return 0f;
            return TwinLane(s, r.SafeSide, out float c, out _) ? c : 0f;
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
            if (cliffs.Count > 0)
            {
                CliffRun cr = CliffAt(s);
                if (cr != null) return cr.twin ? TwinDistance(cr, xRel, s) : Mathf.Abs(xRel) - RoadHalfAt(s);
            }
            Fork f = ForkAt(s);
            if (f == null) return Mathf.Abs(xRel) - Diorama.RoadHalfWidth;
            Branch(f, s, out float b, out float w);
            float left = Mathf.Abs(xRel + b) - w;
            float right = Mathf.Abs(xRel - b) - w;
            if (f.ThreeWay) return Mathf.Min(Mathf.Min(left, right), Mathf.Abs(xRel) - w);
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
            CliffRun twin = TwinAt(s);
            if (twin != null)
            {
                // Each half of the crowd takes a strip; past a dead end they all squeeze onto the safe one.
                int lane = frac < 0f ? -1 : 1;
                float spot = Mathf.Abs(frac) * 2f - 1f;
                if (!TwinLane(s, lane, out float lc, out float lh)) { TwinLane(s, -lane, out lc, out lh); spot = frac; }
                return lc + spot * Mathf.Max(lh - BodyMargin, 0.1f);
            }
            float open = frac * Mathf.Max(RoadHalfAt(s) - BodyMargin, 0.25f);
            Fork f = ForkAt(s);
            if (f == null) return open;

            Branch(f, s, out float b, out float w);
            if (f.ThreeWay)
            {
                // Thirds of the crowd take the left, middle and right branches.
                int third = frac < -1f / 3f ? -1 : frac > 1f / 3f ? 1 : Fork.Middle;
                float local = third == Fork.Middle ? frac * 3f : (frac - third * 2f / 3f) * 3f;   // spot inside its branch, -1 to 1
                float inThird = Fork.BranchX(third, b) + Mathf.Clamp(local, -1f, 1f) * Mathf.Max(w - BodyMargin, 0.2f);
                return Mathf.Lerp(open, inThird, Mathf.Clamp01(b / f.maxOffset * 4f));
            }
            float side = frac < 0f ? -1f : 1f;
            float within = Mathf.Abs(frac) * 2f - 1f;   // spot inside this walker's own branch
            float inBranch = side * b + within * (w - BodyMargin);
            // Blend so the crowd neither pops at the split nor at the rejoin.
            float x = Mathf.Lerp(open, inBranch, Mathf.Clamp01(b / f.maxOffset * 4f));

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
            if (f.ThreeWay)
            {
                if (side != 0 && b > 2f * w)
                {
                    float bc = Fork.BranchX(side, b);
                    min = bc - w + CarMargin;
                    max = bc + w - CarMargin;
                }
                else
                {
                    min = -b - w + CarMargin;
                    max = b + w - CarMargin;
                }
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
            return f.ThreeWay ? b > 2f * w : b > w;
        }

        /// <summary>The branch a road-relative X falls in at s inside a fork: -1, +1, or Fork.Middle on a three-way split.</summary>
        public int BranchAt(float xRel, float s)
        {
            Fork f = ForkAt(s);
            if (f == null) return 0;
            if (!f.ThreeWay) return xRel < 0f ? -1 : 1;
            Branch(f, s, out float b, out _);
            return Mathf.Abs(xRel) < b * 0.5f ? Fork.Middle : xRel < 0f ? -1 : 1;
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

        int nextShooterIndex;
        System.Random shooterPlan;

        void ResetForkPlan()
        {
            shooterPlan = new System.Random(seed * 977 + 11);
            nextShooterIndex = FirstShooterChunk + shooterPlan.Next(2);
            var rng = new System.Random(seed * 131 + 3);
            nextForkIndex = 6 + rng.Next(4);
            forkUntilIndex = -1;
            currentFork = null;
            nextCliffIndex = FirstCliffChunk + rng.Next(4);
            cliffUntilIndex = -1;
            cliffCount = 0;
            twinCount = 0;
            flashlightPlaced = false;
        }

        public void Reset()
        {
            foreach (var c in chunks) DestroyChunk(c);
            chunks.Clear();
            hazards.Clear();
            pickups.Clear();
            shooters.Clear();
            forks.Clear();
            cliffs.Clear();
            EarlyPowerups = 0;
            EarlyShooters = 0;
            ShootersSpawned = 0;
            ShooterSpawnS.Clear();
            GuaranteedShieldS = -1f;
            TurretsSpawnedEarly = 0;
            GuaranteedFlashlightS = -1f;
            System.Array.Clear(ThreeWaySplits, 0, ThreeWaySplits.Length);
            ThreeWayPlan.Clear();
            ThreeWayHalfGaps = 0;
            TwinCliffs = TwinDeadEnds = 0;
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
            shooters.RemoveAll(z => z == null || !z.alive);
            forks.RemoveAll(f => f.EndS < distance - Diorama.Radius - ChunkLength * 2f);
            cliffs.RemoveAll(r => r.EndS < distance - Diorama.Radius - ChunkLength * 2f);
        }

        void DestroyChunk(Chunk c)
        {
            foreach (var h in c.hazards) hazards.Remove(h);
            foreach (var p in c.pickups) pickups.Remove(p);
            foreach (var z in c.shooters) shooters.Remove(z);
            if (c.terrainMesh != null) Object.DestroyImmediate(c.terrainMesh);
            foreach (var m in c.propMeshes) if (m != null) Object.DestroyImmediate(m);
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
            if (c.type == ChunkType.Cliff) BuildTwinMarks(mb, c);
            var props = SceneryModels.Active;
            var pines = props != null && props.HasPine ? new PropBatch(props.pineMesh) : null;
            var rocks = props != null && props.HasRock ? new PropBatch(props.rockMesh) : null;
            var grass = props != null && props.HasGrass ? new PropBatch(props.grassMesh) : null;
            BuildScenery(mb, c, rng, pines, rocks);
            if (grass != null) ScatterGrass(grass, c);
            c.terrainMesh = mb.ToMesh($"LL_Chunk_{index}");
            MeshBuilder.Spawn("Terrain", c.root, c.terrainMesh, groundMat);
            SpawnProps(c, pines, props != null ? props.pineMaterial : null, "Pines");
            SpawnProps(c, rocks, props != null ? props.rockMaterial : null, "Rocks");
            SpawnProps(c, grass, props != null ? props.grassMaterial : null, "Grass");

            BuildPuddles(c);
            if (c.type == ChunkType.Blocked1 || c.type == ChunkType.Blocked2) PlaceBlocker(c, rng);
            if (c.type == ChunkType.Fork) PlaceForkContents(c, rng);
            PlacePickups(c, rng);
            PlaceShooters(c, rng);

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
            if (f.startS >= ThreeWayFrom && rng.NextDouble() < 0.6) PlanThreeWay(f, rng);
            else if (index >= DeadEndsFromChunk && rng.NextDouble() < 0.6) f.deadSide = side;
            else f.obstacleSide = side;
            forks.Add(f);
            currentFork = f;
            OnForkPlanned?.Invoke(f);
            forkUntilIndex = index + chunksLong;
            nextForkIndex = forkUntilIndex + 6 + rng.Next(6);
            nextCliffIndex = Mathf.Max(nextCliffIndex, forkUntilIndex + 2);
        }

        /// <summary>
        /// A three-way split: in the medium stage one branch of three is barricaded; from 650 m usually two; from 1 km
        /// often all three, so the only ways through are a shovel, a shield or a crash.
        /// </summary>
        void PlanThreeWay(Fork f, System.Random rng)
        {
            f.ways = 3;
            f.maxOffset = ThreeWayOffset;
            f.branchHalf = ThreeWayHalf;
            double r = rng.NextDouble();
            int count = f.startS >= AllBlockedFrom ? (r < 0.6 ? 3 : 2)
                : f.startS >= TwoBlockedFrom ? (r < 0.75 ? 2 : 1)
                : 1;
            var order = new List<int> { 0, 1, 2 };
            for (int i = 0; i < order.Count; i++) { int j = rng.Next(i, order.Count); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < count; i++) f.blocked[order[i]] = true;
            if (count == 3)
            {
                // All three shut would leave no way through without a shovel or shield: one gets only half a barricade,
                // leaving a narrow gap the jeep can squeeze through.
                f.halfIndex = order[rng.Next(3)];
                f.halfK = rng.NextDouble() < 0.5 ? -1 : 1;
                ThreeWayHalfGaps++;
            }
            ThreeWaySplits[count]++;
            ThreeWayPlan.Add(new Vector2(f.startS, count));
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
            // From the middle-late stage, cliff roads split into two strips: the first is safe both ways, later ones
            // usually have one strip that ends in a drop.
            if (r.startS >= TwinCliffsFrom && (twinCount == 0 || rng.NextDouble() < 0.75))
            {
                chunksLong = 4 + rng.Next(2);
                r.length = chunksLong * ChunkLength;
                r.twin = true;
                int narrow = rng.NextDouble() < 0.5 ? -1 : 1;
                r.leftHalf = narrow < 0 ? 1.05f : 1.55f;
                r.rightHalf = narrow < 0 ? 1.55f : 1.05f;
                r.halfWidth = Diorama.RoadHalfWidth;
                r.ridgeS = r.startS + r.length * 0.45f;
                r.deadEndS = r.startS + r.length * 0.7f;
                if (twinCount > 0) r.deadSide = rng.NextDouble() < 0.5 ? -1 : 1;
                twinCount++;
                TwinCliffs++;
                if (r.deadSide != 0) TwinDeadEnds++;
            }
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
                    float layer = 1f;
                    if (dist < 0f)
                    {
                        float v = Mathf.PerlinNoise(xc * 0.7f, sc * 0.7f);
                        col = Palette.Road * (0.88f + 0.22f * v);
                        layer = LayerRoad;
                    }
                    else if (hAvg < -0.3f) col = Palette.Stone * (0.8f + 0.5f * Mathf.PerlinNoise(xc * 0.6f, sc * 0.6f));
                    else if (dist < 0.9f) { col = Palette.RoadDark; layer = LayerDirt; }
                    else if (slope > 1.4f) col = Palette.Cliff;
                    else
                    {
                        float v = Mathf.PerlinNoise(xc * 0.35f + 5f, sc * 0.35f);
                        col = Color.Lerp(Palette.FloorDark, Palette.Moss, v * 0.8f);
                        if (hAvg > 4f) col = Color.Lerp(col, Palette.Rock, 0.4f);
                        layer = LayerGrass;
                    }
                    col.a = layer;
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
                    CliffRun twin = TwinAt(s);
                    if (twin != null)
                    {
                        int n = TwinIntervals(twin, s, out int lanes);
                        for (int i = 0; i < lanes; i++) { AddPost(mb, cx + intervals[i * 2], z); AddPost(mb, cx + intervals[i * 2 + 1], z); }
                        continue;
                    }
                    float half = RoadHalfAt(s);
                    AddPost(mb, cx - half, z);
                    AddPost(mb, cx + half, z);
                    continue;
                }
                Branch(f, s, out float b, out float w);
                if (f.ThreeWay)
                {
                    AddPost(mb, cx - b - w, z);
                    AddPost(mb, cx + b + w, z);
                    if (b > 2f * w)
                    {
                        AddPost(mb, cx - b + w, z);
                        AddPost(mb, cx + b - w, z);
                        AddPost(mb, cx - w, z);
                        AddPost(mb, cx + w, z);
                    }
                    continue;
                }
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

        /// <summary>
        /// Twin cliff markings in a chunk: glowing-white chevron boards along the ridge pointing at the safe strip, and
        /// at a dead strip's end tall red posts with a striped board, so the drop reads from the scouting preview.
        /// </summary>
        void BuildTwinMarks(MeshBuilder mb, Chunk c)
        {
            foreach (var r in cliffs)
            {
                if (!r.twin) continue;
                float chunkEnd = c.startS + ChunkLength;
                if (r.deadSide != 0 && r.deadEndS >= c.startS && r.deadEndS < chunkEnd)
                {
                    float s = r.deadEndS - 0.4f;
                    float z = s - c.startS;
                    if (TwinLane(s, r.deadSide, out float lc, out float lh))
                    {
                        float x = Center(s) + lc;
                        mb.Box(new Vector3(x - lh, 1.0f, z), new Vector3(0.22f, 2f, 0.22f), Palette.WarningBoard * 1.4f);
                        mb.Box(new Vector3(x + lh, 1.0f, z), new Vector3(0.22f, 2f, 0.22f), Palette.WarningBoard * 1.4f);
                        mb.Box(new Vector3(x, 1.65f, z), new Vector3(lh * 2f + 0.3f, 0.36f, 0.1f), Palette.WarningBoard);
                        for (float bx = -lh + 0.2f; bx < lh; bx += 0.6f)
                            mb.Box(new Vector3(x + bx, 1.65f, z - 0.06f), new Vector3(0.26f, 0.36f, 0.03f), Palette.Barricade);
                    }
                }
                if (r.deadSide != 0 && Mathf.Abs(r.ridgeS - (c.startS + ChunkLength * 0.5f)) < ChunkLength * 0.5f + CliffRun.RidgeLength)
                {
                    // Chevrons on the dead strip's outer edge just before the ridge, pointing across to the safe strip.
                    for (float s = r.ridgeS - CliffRun.RidgeLength * 1.6f; s < r.ridgeS; s += 2f)
                    {
                        if (s < c.startS || s >= chunkEnd) continue;
                        if (!TwinLane(s, r.deadSide, out float lc, out float lh)) continue;
                        float z = s - c.startS;
                        float x = Center(s) + lc + r.deadSide * (lh + 0.25f);
                        float dir = -r.deadSide;
                        Vector3 p = new Vector3(x, 0.7f, z);
                        mb.Box(p + new Vector3(dir * 0.1f, 0.15f, 0f), new Vector3(0.12f, 0.42f, 0.08f), Quaternion.Euler(0f, 0f, dir * 40f), Palette.EscapeMarker);
                        mb.Box(p + new Vector3(dir * 0.1f, -0.15f, 0f), new Vector3(0.12f, 0.42f, 0.08f), Quaternion.Euler(0f, 0f, -dir * 40f), Palette.EscapeMarker);
                    }
                }
            }
        }

        static void SpawnProps(Chunk c, PropBatch batch, Material mat, string name)
        {
            if (batch == null || batch.Count == 0 || mat == null) return;
            var mesh = batch.ToMesh($"LL_{name}_{c.index}");
            c.propMeshes.Add(mesh);
            MeshBuilder.Spawn(name, c.root, mesh, mat);
        }

        /// <summary>Its own random stream, so decoration never shifts where hazards and pickups land.</summary>
        System.Random DecoRng(Chunk c) => new System.Random(MixSeed(seed + 7919, c.index));

        /// <summary>Tripo grass tufts on the forest floor, kept off the road, its verges and the cliffs.</summary>
        void ScatterGrass(PropBatch grass, Chunk c)
        {
            var rng = new System.Random(MixSeed(seed + 104729, c.index));
            int count = 14 + rng.Next(6);
            for (int i = 0; i < count; i++)
            {
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * (SideExtent - 1f);
                float z = (float)rng.NextDouble() * ChunkLength;
                float h = 0.45f + (float)rng.NextDouble() * 0.4f;
                float yaw = (float)rng.NextDouble() * 360f;
                float shade = 0.8f + (float)rng.NextDouble() * 0.3f;
                float s = c.startS + z;
                if (RoadDistance(xRel, s) < 1.2f) continue;
                if (CliffAt(s) != null) continue;
                float y = Height(xRel, s, CliffFactor(c, xRel, z));
                if (y < -0.3f) continue;
                grass.Add(new Vector3(Center(s) + xRel, y - 0.05f, z), h, h, Quaternion.Euler(0f, yaw, 0f), new Color(shade, shade, shade, 1f));
            }
        }

        void BuildScenery(MeshBuilder mb, Chunk c, System.Random rng, PropBatch pines = null, PropBatch rockProps = null)
        {
            var deco = DecoRng(c);
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
                bool dark = rng.NextDouble() < 0.5;
                var basePos = new Vector3(Center(s) + xRel, y - 0.1f, z);
                if (pines != null)
                {
                    // Same footprint as the cone pine: about 3 units tall at scale 1.
                    float yaw = (float)deco.NextDouble() * 360f;
                    float shade = dark ? 0.8f : 1f;
                    pines.Add(basePos, 3f * scale, 3f * scale, Quaternion.Euler(0f, yaw, 0f), new Color(shade, shade, shade, 1f));
                }
                else AddPine(mb, basePos, scale, dark);
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
                float radius = 0.4f + (float)rng.NextDouble() * 0.6f;
                int rockSeed = rng.Next();
                if (rockProps != null)
                {
                    // About as wide as the procedural rock, sunk slightly so it sits in the ground.
                    float h = radius * 1.6f;
                    float yaw = (float)deco.NextDouble() * 360f;
                    float shade = 0.85f + (float)deco.NextDouble() * 0.25f;
                    rockProps.Add(new Vector3(Center(s) + xRel, y - 0.12f * h, z), h, h, Quaternion.Euler(0f, yaw, 0f), new Color(shade, shade, shade, 1f));
                }
                else mb.Rock(new Vector3(Center(s) + xRel, y + 0.2f, z), radius, rockSeed, Palette.Rock);
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

            if (f.ThreeWay)
            {
                // A row of barricades across each blocked branch, where the branches are fully apart.
                float rowS = f.startS + f.length * 0.5f;
                if (rowS >= c.startS && rowS < chunkEnd)
                {
                    Branch(f, rowS, out float b, out float w);
                    float z = rowS - c.startS;
                    float yaw = Mathf.Atan(Slope(rowS)) * Mathf.Rad2Deg;
                    foreach (int side in new[] { -1, Fork.Middle, 1 })
                    {
                        if (!f.Blocked(side)) continue;
                        float bx = Fork.BranchX(side, b);
                        for (int k = -1; k <= 1; k += 2)
                        {
                            if (f.HalfBlocked(side) && k != f.halfK) continue;   // the narrow gap
                            var h = SpawnHazard(c, HazardKind.Barricade, barricadeMesh);
                            h.halfWidth = BarricadeHalfWidth;
                            h.transform.localPosition = new Vector3(Center(rowS) + bx + k * (w - BarricadeHalfWidth), 0f, z);
                            h.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        }
                    }
                }
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
                double chance = Mathf.Lerp(0.06f, 0.2f, Mathf.Clamp01((c.index - FirstPowerupChunk) / 50f));
                if (first || rng.NextDouble() < chance)
                {
                    double r = rng.NextDouble();
                    SpawnPowerup(c, first ? PickupKind.Booster : PowerupKind(r, StageAt(c.startS)), rng);
                }
            }
            if (c.index >= 1 && rng.NextDouble() < 0.75) SpawnCoinTrail(c, rng);
            // One shield is always waiting a little before the first shooters, so there's a chance to meet them shielded.
            if (c.index == GuaranteedShieldChunk)
            {
                SpawnPowerup(c, PickupKind.Shield, rng);
                GuaranteedShieldS = c.startS;
            }
            // And a flashlight waits a little before the weather can start, since it's the answer to fog.
            if (!flashlightPlaced && c.index >= GuaranteedFlashlightChunk && c.type != ChunkType.Cliff)
            {
                flashlightPlaced = true;
                SpawnPowerup(c, PickupKind.Flashlight, rng);
                GuaranteedFlashlightS = c.startS;
            }
        }

        static readonly int GuaranteedShieldChunk = FirstShooterChunk - 4;

        /// <summary>
        /// Which powerup a roll gives. Flashlights and repairs are common and equally likely all run. The shovel
        /// is a medium-rarity find, less common late on. The shield is rare all run. The turret only appears from
        /// the medium stage, when shooter zombies do.
        /// </summary>
        public static PickupKind PowerupKind(double r, GameStage stage)
        {
            switch (stage)
            {
                case GameStage.Early:   // booster 26%, shovel 22%, flashlight 22%, repair 22%, shield 8%
                    return r < 0.26 ? PickupKind.Booster : r < 0.48 ? PickupKind.Shovel : r < 0.70 ? PickupKind.Flashlight
                         : r < 0.92 ? PickupKind.Repair : PickupKind.Shield;
                case GameStage.Medium:  // booster 18%, shovel 18%, flashlight 21%, repair 21%, turret 14%, shield 8%
                    return r < 0.18 ? PickupKind.Booster : r < 0.36 ? PickupKind.Shovel : r < 0.57 ? PickupKind.Flashlight
                         : r < 0.78 ? PickupKind.Repair : r < 0.92 ? PickupKind.Turret : PickupKind.Shield;
                default:                // booster 18%, shovel 12%, flashlight 22%, repair 22%, turret 18%, shield 8%
                    return r < 0.18 ? PickupKind.Booster : r < 0.30 ? PickupKind.Shovel : r < 0.52 ? PickupKind.Flashlight
                         : r < 0.74 ? PickupKind.Repair : r < 0.92 ? PickupKind.Turret : PickupKind.Shield;
            }
        }

        /// <summary>
        /// Shooter zombies from the medium stage on: off the road on the terrain beside it, close enough to
        /// scout, never on the road or a cliff stretch. They ramp up slowly: the first sighting is a lone
        /// spitter, then groups come closer together and grow from one to three as the run goes on.
        /// Group sizes are drawn last from the chunk's generator, so everything else in the chunk stays as it was;
        /// the spacing comes from its own seeded plan.
        /// </summary>
        void PlaceShooters(Chunk c, System.Random rng)
        {
            if (c.index < nextShooterIndex || c.type == ChunkType.Cliff) return;   // a cliff defers the group
            float t = Mathf.Clamp01((c.index - FirstShooterChunk) / (float)(RampEndChunk - FirstShooterChunk));
            int count = 1;
            if (rng.NextDouble() < 0.55f * t) count++;
            if (t > 0.75f && rng.NextDouble() < 0.4f * t) count++;
            // About 10 chunks (120 m) between groups at first, down to 2 or 3 chunks after 1 km.
            int gap = Mathf.RoundToInt(Mathf.Lerp(9f, 2f, t)) + shooterPlan.Next(0, 2);
            nextShooterIndex = c.index + Mathf.Max(2, gap);
            for (int i = 0; i < count; i++)
            {
                float z = 1f + (float)rng.NextDouble() * (ChunkLength - 2f);
                float s = c.startS + z;
                if (CliffAt(s) != null) continue;
                float xRel = ShooterSpot(s, rng);
                if (float.IsNaN(xRel)) continue;
                float y = Height(xRel, s, CliffFactor(c, xRel, z));
                var zombie = ShooterZombie.Create(c, terrainMat, new Vector3(Center(s) + xRel, y, z), rng);
                c.shooters.Add(zombie);
                shooters.Add(zombie);
                ShootersSpawned++;
                ShooterSpawnS.Add(s);
                if (s < MediumStageFrom) EarlyShooters++;
            }
        }

        /// <summary>A road-relative X on the terrain 2 to 7 units off the road edge, or NaN if none was found.</summary>
        float ShooterSpot(float s, System.Random rng)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * 13f;
                float dist = RoadDistance(xRel, s);
                if (dist >= 2f && dist <= 7f) return xRel;
            }
            return float.NaN;
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
            if (kind == PickupKind.Turret && s < MediumStageFrom) TurretsSpawnedEarly++;
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

        /// <summary>Dark puddles on the road, hidden until the road is wet.</summary>
        void BuildPuddles(Chunk c)
        {
            var rng = new System.Random(MixSeed(seed + 31337, c.index));
            var water = new MeshBuilder();
            int count = 3 + rng.Next(4);
            for (int i = 0; i < count; i++)
            {
                float z = 0.6f + (float)rng.NextDouble() * (ChunkLength - 1.2f);
                float s = c.startS + z;
                float xRel = ((float)rng.NextDouble() * 2f - 1f) * (RoadHalfAt(s) - 1f);
                if (RoadDistance(xRel, s) > -0.7f) continue;
                float rx = 0.5f + (float)rng.NextDouble() * 0.7f, rz = rx * (0.6f + (float)rng.NextDouble() * 0.6f);
                var center = new Vector3(Center(s) + xRel, 0.025f, z);
                int n = 12;
                for (int k = 0; k < n; k++)
                {
                    float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                    float w0 = 0.85f + 0.3f * Mathf.PerlinNoise(i * 3.1f + k * 0.7f, 2.3f), w1 = 0.85f + 0.3f * Mathf.PerlinNoise(i * 3.1f + (k + 1) % n * 0.7f, 2.3f);
                    Vector3 p0 = center + new Vector3(Mathf.Cos(a0) * rx * w0, 0f, Mathf.Sin(a0) * rz * w0);
                    Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * rx * w1, 0f, Mathf.Sin(a1) * rz * w1);
                    water.TriOut(center, p1, p0, new Color(0.09f, 0.12f, 0.17f, 1f), center + Vector3.down);
                }
            }
            if (water.VertexCount == 0) return;
            c.puddles = new GameObject("Puddles").transform;
            c.puddles.SetParent(c.root, false);
            var wm = water.ToMesh($"LL_Puddles_{c.index}");
            c.propMeshes.Add(wm);
            if (puddleMat == null)
            {
                puddleMat = new Material(terrainMat) { name = "LL_Puddle" };
                puddleMat.SetFloat("_SnowCover", 0f);
            }
            // The wet-surface glints in the ground shader make the dark water shine; no glow layer, which wouldn't
            // be clipped to the disk.
            MeshBuilder.Spawn("Water", c.puddles, wm, puddleMat);
            c.puddles.gameObject.SetActive(false);
        }

        public bool PuddlesShown { get; private set; }

        /// <summary>Shows the puddles once the road is wet enough.</summary>
        public void SetWetness(float wet)
        {
            bool show = wet > 0.3f;
            PuddlesShown = show;
            foreach (var c in chunks)
                if (c.puddles != null && c.puddles.gameObject.activeSelf != show) c.puddles.gameObject.SetActive(show);
        }

        /// <summary>
        /// A broken branch blowing across the road in a storm, starting off one edge at travel distance s and
        /// tumbling to the other at <paramref name="speed"/>. It hits like a fallen tree. Returns null where there's
        /// no generated chunk or the road is a cliff.
        /// </summary>
        public Hazard SpawnBlownBranch(float s, int dir, float halfWidth, float speed, System.Random rng)
        {
            Chunk c = ChunkAt(s);
            if (c == null || c.type == ChunkType.Cliff || CliffAt(s) != null) return null;
            float z = s - c.startS;
            float edge = RoadHalfAt(s);
            var h = SpawnHazard(c, HazardKind.FallenTree, branchMeshes[rng.Next(branchMeshes.Length)]);
            h.halfWidth = halfWidth;
            h.transform.localScale = Vector3.one * (halfWidth / 1.3f);
            h.transform.localPosition = new Vector3(Center(s) - dir * (edge + 2.2f), 0f, z);
            h.driftX = dir * speed;
            h.driftEndX = Center(s) + dir * (edge + 3f);
            h.tumble = (float)(rng.NextDouble() - 0.5) * 30f;
            return h;
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

            // Barricade: a red and white striped board on two legs with a concrete foot and an amber lamp each end.
            float bw = BarricadeHalfWidth * 2f;
            mb.Box(new Vector3(0f, 0.18f, 0f), new Vector3(bw, 0.36f, 0.55f), Palette.Stone * 1.6f);
            mb.Box(new Vector3(-bw * 0.4f, 0.65f, 0f), new Vector3(0.12f, 0.7f, 0.12f), Palette.Metal * 1.3f);
            mb.Box(new Vector3(bw * 0.4f, 0.65f, 0f), new Vector3(0.12f, 0.7f, 0.12f), Palette.Metal * 1.3f);
            mb.Box(new Vector3(0f, 0.95f, -0.08f), new Vector3(bw, 0.42f, 0.1f), Palette.WarningBoard);
            for (float x = -bw * 0.5f + 0.25f; x < bw * 0.5f; x += 0.7f)
                mb.Box(new Vector3(x, 0.95f, -0.14f), new Vector3(0.3f, 0.42f, 0.03f), Quaternion.Euler(0f, 0f, 30f), Palette.Barricade);
            mb.Box(new Vector3(-bw * 0.5f + 0.1f, 1.26f, -0.08f), new Vector3(0.18f, 0.18f, 0.18f), Palette.UIAmber * 3f);
            mb.Box(new Vector3(bw * 0.5f - 0.1f, 1.26f, -0.08f), new Vector3(0.18f, 0.18f, 0.18f), Palette.UIAmber * 3f);
            barricadeMesh = mb.ToMesh("LL_Barricade"); mb.Clear();

            // Broken branches for storms: a snapped bough with twigs and a few clumps of needles, 2.6 long at scale 1.
            branchMeshes = new Mesh[3];
            for (int k = 0; k < branchMeshes.Length; k++)
            {
                var rng = new System.Random(500 + k);
                mb.Box(new Vector3(0f, 0.22f, 0f), new Vector3(2.6f, 0.26f, 0.26f), Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() - 0.5) * 8f), Palette.Trunk * 1.15f);
                mb.Box(new Vector3(-1.32f, 0.22f, 0f), new Vector3(0.12f, 0.34f, 0.34f), Palette.Trunk * 1.8f);   // pale snapped end
                int twigs = 3 + k;
                for (int i = 0; i < twigs; i++)
                {
                    float x = -0.9f + i * (1.9f / twigs) + (float)rng.NextDouble() * 0.2f;
                    float side = i % 2 == 0 ? 1f : -1f;
                    float len = 0.5f + (float)rng.NextDouble() * 0.5f;
                    var at = new Vector3(x + 0.18f, 0.24f, side * len * 0.4f);
                    mb.Box(at, new Vector3(0.08f, 0.08f, len), Quaternion.Euler(0f, side * 35f, 0f), Palette.Trunk);
                    mb.Cone(at + new Vector3(0.15f, -0.05f, side * len * 0.4f), 0.32f, 0.45f, 5, rng.NextDouble() < 0.5 ? Palette.Pine : Palette.PineDark, (float)rng.NextDouble());
                }
                branchMeshes[k] = mb.ToMesh("LL_Branch_" + k); mb.Clear();
            }
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
