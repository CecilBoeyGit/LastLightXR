using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// Tyre tracks pressed into the snow behind the jeep. While snow lies on the road, the jeep's wheel line is
    /// recorded along the road (travel distance and road-relative X) and drawn as two dark ribbons that scroll
    /// away behind it, thinning out with age. They use a material copy that snow never settles on.
    /// </summary>
    public sealed class SnowTrails
    {
        const float Spacing = 0.45f;        // travel between recorded points
        const int MaxPoints = 80;
        const float TrackHalfSpacing = 0.56f;
        const float Width = 0.3f;

        struct Point { public float s, x; }

        readonly List<Point> points = new List<Point>();
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly MeshBuilder mb = new MeshBuilder();
        float lastS = float.NegativeInfinity;

        public int PointCount => points.Count;
        public bool Visible => renderer.gameObject.activeSelf;

        public SnowTrails(Transform parent, Material terrainMat)
        {
            var mat = new Material(terrainMat) { name = "LL_TyreTracks" };
            mat.SetFloat("_SnowCover", 0f);
            mesh = new Mesh { name = "LL_TyreTracks" };
            renderer = MeshBuilder.Spawn("Tyre Tracks", parent, mesh, mat);
            renderer.gameObject.SetActive(false);
        }

        public void Reset()
        {
            points.Clear();
            lastS = float.NegativeInfinity;
            mesh.Clear();
            renderer.gameObject.SetActive(false);
        }

        /// <param name="cover">How much snow lies on the road, 0 to 1. Tracks are laid above a little cover and fade with it.</param>
        /// <param name="carS">The jeep's travel distance.</param>
        /// <param name="carX">The jeep's road-relative X.</param>
        /// <param name="distance">The run's travel distance (the road under the disk's car line).</param>
        public void Tick(float cover, bool laying, float carS, float carX, float distance, TrackGenerator track)
        {
            if (laying && cover > 0.15f && carS - lastS >= Spacing)
            {
                points.Add(new Point { s = carS, x = carX });
                lastS = carS;
                if (points.Count > MaxPoints) points.RemoveAt(0);
            }
            bool show = cover > 0.05f && points.Count > 1;
            if (renderer.gameObject.activeSelf != show) renderer.gameObject.SetActive(show);
            if (!show) return;

            float center0 = track.Center(distance);
            mb.Clear();
            Color col = new Color(0.17f, 0.17f, 0.18f, 1f);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Point a = points[i], b = points[i + 1];
                if (b.s - a.s > Spacing * 4f) continue;    // a gap (respawn, stop) breaks the track
                float age = (points.Count - 1 - i) / (float)MaxPoints;
                float w = Width * Mathf.Clamp01(cover * 1.4f) * (1f - 0.7f * age) * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 pa = Local(a, side, center0, distance, track), pb = Local(b, side, center0, distance, track);
                    Vector3 dir = pb - pa; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    Vector3 n = Vector3.Cross(Vector3.up, dir.normalized) * w;
                    mb.QuadOut(pa - n, pb - n, pb + n, pa + n, col, (pa + pb) * 0.5f + Vector3.down);
                }
            }
            mb.ToMesh(mesh.name, mesh);
        }

        static Vector3 Local(Point p, int side, float center0, float distance, TrackGenerator track)
        {
            float x = track.Center(p.s) - center0 + p.x + side * TrackHalfSpacing;
            return new Vector3(x, 0.035f, Diorama.CarZ + p.s - distance);
        }
    }
}
