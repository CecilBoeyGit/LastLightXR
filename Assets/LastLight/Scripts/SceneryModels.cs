using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// The Tripo scenery props (pine, boulder, grass tuft), baked by the editor's Configure Scenery Models step
    /// into normalised meshes: base centre at the origin, exactly 1 unit tall, readable so road chunks can merge
    /// them. When the asset or a prop is missing, the track falls back to its procedural cones and rocks.
    /// </summary>
    public class SceneryModels : ScriptableObject
    {
        public const string ResourcePath = "LastLightModels/SceneryModels";

        public Mesh pineMesh;
        public Material pineMaterial;
        public Mesh rockMesh;
        public Material rockMaterial;
        public Mesh grassMesh;
        public Material grassMaterial;

        /// <summary>The Tripo vehicles, in VehicleCatalog order (jeep, pickup, APC, ambulance), baked facing +Z within the
        /// jeep's 1.4 x 2.6 footprint with the wheels on y = 0. A missing entry falls back to the box model.</summary>
        public Mesh[] vehicleMeshes = new Mesh[4];
        public Material[] vehicleMaterials = new Material[4];
        /// <summary>The Tripo garage diorama, baked with its turntable centred on the origin and the turntable's top at y = 0.</summary>
        public Mesh garageMesh;
        public Material garageMaterial;
        /// <summary>A clean steel disc covering the garage's turntable (same space as the garage mesh), with its own material.</summary>
        public Mesh garagePlatformMesh;
        public Material garagePlatformMaterial;
        public bool HasGaragePlatform => garagePlatformMesh != null && garagePlatformMaterial != null;

        /// <summary>A baked Tripo model and its LastLight/DioramaTextured material.</summary>
        [System.Serializable]
        public struct Model
        {
            public Mesh mesh;
            public Material material;
            public bool Ready => mesh != null && material != null;
        }

        /// <summary>
        /// The second batch of Tripo props, baked by Configure Prop Models at their gameplay size. Road hazards (branch,
        /// log, wide tree, barricade) lie along X with their base on y = 0 and are as long as the hazard they replace;
        /// the falling boulder (2 across) and the coin (1.7 across, standing in the XY plane) are centred on the origin;
        /// the cliff rock is 1 unit tall with its base centre at the origin, for merging into chunks; the turret and the
        /// plough face +Z with their base on y = 0. Any that isn't ready falls back to its procedural mesh.
        /// </summary>
        public Model branch, fallingBoulder, barricade, fallenLog, wideTree, cliffRock, coin, turret, shovel;

        /// <summary>The model picked by <paramref name="pick"/> when Tripo models are on and it is baked, otherwise an empty one.</summary>
        public static Model Get(System.Func<SceneryModels, Model> pick)
        {
            var p = Active;
            return p != null ? pick(p) : default;
        }

        public bool HasVehicle(int i) => vehicleMeshes != null && vehicleMaterials != null && i >= 0 && i < vehicleMeshes.Length && i < vehicleMaterials.Length
            && vehicleMeshes[i] != null && vehicleMaterials[i] != null;
        public bool HasGarage => garageMesh != null && garageMaterial != null;

        public bool HasPine => pineMesh != null && pineMaterial != null;
        public bool HasRock => rockMesh != null && rockMaterial != null;
        public bool HasGrass => grassMesh != null && grassMaterial != null;

        static SceneryModels cached;
        static bool loaded;

        public static SceneryModels Load()
        {
            if (!loaded || cached == null)
            {
                cached = Resources.Load<SceneryModels>(ResourcePath);
                loaded = true;
            }
            return cached;
        }

        /// <summary>Set false to force the procedural scenery (for comparisons and tests).</summary>
        public static bool Enabled = true;

        public static SceneryModels Active => Enabled ? Load() : null;
    }

    /// <summary>
    /// Merges many placed copies of one prop mesh into a single mesh, so a road chunk draws all its pines (or rocks,
    /// or grass) in one call. Each copy gets a vertex colour, which the material multiplies in for variety.
    /// </summary>
    public class PropBatch
    {
        class Source { public Vector3[] v, n; public Vector2[] uv; public int[] tri; }
        static readonly Dictionary<Mesh, Source> sources = new Dictionary<Mesh, Source>();

        readonly Source src;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris = new List<int>();

        public int Count { get; private set; }

        public PropBatch(Mesh mesh)
        {
            if (!sources.TryGetValue(mesh, out src))
            {
                src = new Source { v = mesh.vertices, n = mesh.normals, uv = mesh.uv, tri = mesh.triangles };
                sources[mesh] = src;
            }
        }

        /// <summary>Adds one copy standing at <paramref name="basePos"/>, <paramref name="height"/> units tall.</summary>
        public void Add(Vector3 basePos, float height, float width, Quaternion rot, Color tint)
        {
            int start = verts.Count;
            var m = Matrix4x4.TRS(basePos, rot, new Vector3(width, height, width));
            var nm = m.inverse.transpose;
            for (int i = 0; i < src.v.Length; i++)
            {
                verts.Add(m.MultiplyPoint3x4(src.v[i]));
                norms.Add(nm.MultiplyVector(src.n.Length > i ? src.n[i] : Vector3.up).normalized);
                uvs.Add(src.uv.Length > i ? src.uv[i] : Vector2.zero);
                cols.Add(tint);
            }
            for (int i = 0; i < src.tri.Length; i++) tris.Add(start + src.tri[i]);
            Count++;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
