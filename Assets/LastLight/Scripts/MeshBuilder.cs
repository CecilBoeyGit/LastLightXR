using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastLight
{
    /// <summary>
    /// Builds flat-shaded, vertex-coloured meshes from simple primitives.
    /// Every triangle gets its own vertices so the low-poly look stays crisp.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris = new List<int>();

        static readonly Vector3[] OctaVerts = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        static readonly int[,] OctaFaces = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };

        public int VertexCount => verts.Count;

        public void Clear() { verts.Clear(); norms.Clear(); cols.Clear(); tris.Clear(); }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            cols.Add(col); cols.Add(col); cols.Add(col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Triangle whose winding is corrected to face away from <paramref name="inside"/>.</summary>
        public void TriOut(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            Vector3 centroid = (a + b + c) / 3f;
            if (Vector3.Dot(n, centroid - inside) < 0f) Tri(a, c, b, col); else Tri(a, b, c, col);
        }

        /// <summary>Quad a-b-c-d, clockwise when seen from the front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Tri(a, b, c, col);
            Tri(a, c, d, col);
        }

        public void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 inside)
        {
            TriOut(a, b, c, col, inside);
            TriOut(a, c, d, col, inside);
        }

        public void Box(Vector3 center, Vector3 size, Color col) => Box(center, size, Quaternion.identity, col);

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col)
        {
            Vector3 h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var o = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                p[i] = center + rot * o;
            }
            QuadOut(p[0], p[1], p[3], p[2], col, center);
            QuadOut(p[4], p[5], p[7], p[6], col, center);
            QuadOut(p[0], p[2], p[6], p[4], col, center);
            QuadOut(p[1], p[3], p[7], p[5], col, center);
            QuadOut(p[0], p[1], p[5], p[4], col * 0.7f, center);
            QuadOut(p[2], p[3], p[7], p[6], col * 1.1f, center);
        }

        public void Cone(Vector3 baseCenter, float radius, float height, int sides, Color col, float twist = 0f)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            Vector3 inside = baseCenter + Vector3.up * (height * 0.25f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = twist + i * Mathf.PI * 2f / sides;
                float a1 = twist + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                TriOut(p0, p1, apex, col, inside);
                TriOut(p0, p1, baseCenter, col * 0.6f, apex);
            }
        }

        public void Cylinder(Vector3 baseCenter, float radius, float height, int sides, Color col, float topRadius = -1f, bool caps = true)
        {
            if (topRadius < 0f) topRadius = radius;
            Vector3 top = baseCenter + Vector3.up * height;
            Vector3 inside = baseCenter + Vector3.up * (height * 0.5f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = baseCenter + d0 * radius, b1 = baseCenter + d1 * radius;
                Vector3 t0 = top + d0 * topRadius, t1 = top + d1 * topRadius;
                QuadOut(b0, t0, t1, b1, col, inside);
                if (caps && topRadius > 0f) TriOut(t0, t1, top, col, inside);
                if (caps && radius > 0f) TriOut(b0, b1, baseCenter, col * 0.6f, inside);
            }
        }

        /// <summary>Low-poly rock: a jittered octahedron subdivided once.</summary>
        public void Rock(Vector3 center, float radius, int seed, Color col)
        {
            var rng = new System.Random(seed);
            var cache = new Dictionary<Vector3, Vector3>();
            Vector3 J(Vector3 v)
            {
                if (cache.TryGetValue(v, out var r)) return r;
                float k = 0.75f + (float)rng.NextDouble() * 0.5f;
                Vector3 j = new Vector3(v.x, v.y * 0.8f, v.z).normalized * radius * k;
                cache[v] = j;
                return j;
            }
            for (int f = 0; f < 8; f++)
            {
                Vector3 a = OctaVerts[OctaFaces[f, 0]], b = OctaVerts[OctaFaces[f, 1]], c = OctaVerts[OctaFaces[f, 2]];
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                Vector3[][] sub = { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, c }, new[] { ab, bc, ca } };
                foreach (var s in sub)
                {
                    float shade = 0.85f + (float)rng.NextDouble() * 0.3f;
                    TriOut(center + J(s[0]), center + J(s[1]), center + J(s[2]), col * shade, center);
                }
            }
        }

        /// <summary>Glow orb: octahedron subdivided once and normalised.</summary>
        public void Orb(Vector3 center, float radius, Color col)
        {
            for (int f = 0; f < 8; f++)
            {
                Vector3 a = OctaVerts[OctaFaces[f, 0]], b = OctaVerts[OctaFaces[f, 1]], c = OctaVerts[OctaFaces[f, 2]];
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                Vector3[][] sub = { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, c }, new[] { ab, bc, ca } };
                foreach (var s in sub) TriOut(center + s[0] * radius, center + s[1] * radius, center + s[2] * radius, col, center);
            }
        }

        /// <summary>Flat ring in the XZ plane (for glow markers and plates).</summary>
        public void Ring(Vector3 center, float inner, float outer, int sides, Color col)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                QuadOut(center + d0 * inner, center + d1 * inner, center + d1 * outer, center + d0 * outer, col, center + Vector3.down);
            }
        }

        /// <summary>Flat disc whose colour fades from centre to edge (vertex colours carry alpha).</summary>
        public void SoftDisc(Vector3 center, float radius, int sides, Color inner, Color outer)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = center + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                int idx = verts.Count;
                verts.Add(center); verts.Add(p1); verts.Add(p0);
                norms.Add(Vector3.up); norms.Add(Vector3.up); norms.Add(Vector3.up);
                cols.Add(inner); cols.Add(outer); cols.Add(outer);
                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
            }
        }

        /// <summary>
        /// A convex polygon in the XY plane extruded along Z, with winding handled for both faces.
        /// Used for the flat pickup icons.
        /// </summary>
        public void Prism(IList<Vector2> outline, float depth, Color col)
        {
            float h = depth * 0.5f;
            Vector3 center = Vector3.zero;
            foreach (var p in outline) center += new Vector3(p.x, p.y, 0f);
            center /= outline.Count;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Vector3 af = new Vector3(a.x, a.y, -h), bf = new Vector3(b.x, b.y, -h);
                Vector3 ab = new Vector3(a.x, a.y, h), bb = new Vector3(b.x, b.y, h);
                Vector3 cf = center + new Vector3(0f, 0f, -h), cb = center + new Vector3(0f, 0f, h);
                TriOut(af, bf, cf, col, center + Vector3.forward);
                TriOut(ab, bb, cb, col, center + Vector3.back);
                QuadOut(af, bf, bb, ab, col * 0.7f, center);
            }
        }

        /// <summary>A partial ring in the XY plane starting at 12 o'clock and running clockwise. Fraction is 0 to 1.</summary>
        public void Arc(Vector3 center, float inner, float outer, float fraction, int segments, Color col)
        {
            int count = Mathf.CeilToInt(segments * Mathf.Clamp01(fraction));
            for (int i = 0; i < count; i++)
            {
                float a0 = Mathf.PI * 0.5f - i * Mathf.PI * 2f / segments;
                float a1 = Mathf.PI * 0.5f - (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                QuadOut(center + d0 * inner, center + d1 * inner, center + d1 * outer, center + d0 * outer, col, center + Vector3.forward);
            }
        }

        /// <summary>A flat strip along a polyline in the XZ plane (ghost path).</summary>
        public void Strip(IList<Vector3> points, float width, Color col)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                Vector3 dir = (b - a); dir.y = 0;
                if (dir.sqrMagnitude < 1e-6f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
                QuadOut(a - side, b - side, b + side, a + side, col, (a + b) * 0.5f + Vector3.down);
            }
        }

        public Mesh ToMesh(string name, Mesh reuse = null)
        {
            Mesh m = reuse != null ? reuse : new Mesh();
            m.Clear();
            m.name = name;
            m.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Creates a child object with a mesh renderer.</summary>
        public static MeshRenderer Spawn(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }
}
