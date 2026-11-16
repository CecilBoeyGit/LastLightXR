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
