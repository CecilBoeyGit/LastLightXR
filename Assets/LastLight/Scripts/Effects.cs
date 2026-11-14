using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum DebrisKind { Rock, Wood, Metal, Flesh }

    /// <summary>
    /// Short-lived pieces: shattered rocks, broken branches, a spent shovel, a downed shooter, acid splashes
    /// and tracer sparks. Solid pieces tumble, bounce on the ground and dissolve with the terrain shader's
    /// dithered fade; glow pieces fade their tint. Pieces are parented to a road chunk where possible, so
    /// they scroll away with the road. Ticked by the director, so they freeze when the game does.
    /// </summary>
    public class Effects
    {
        const float Gravity = 22f;

        class Piece
        {
            public Transform t;
            public MeshRenderer r;
            public Vector3 vel, spinAxis;
            public float spin, age, life, fadeTime, baseScale;
            public bool glow, bounce;
            public Color color;
        }

        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static Mesh[] rockShards, woodPieces, metalPieces, fleshPieces;
        static Mesh glowBlob, needleClump;

        readonly Material solidMat, glowMat;
        readonly List<Piece> pieces = new List<Piece>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        System.Random rng = new System.Random(4242);

        public int ActiveCount => pieces.Count;

        public Effects(Material solidMat, Material glowMat)
        {
            this.solidMat = solidMat;
            this.glowMat = glowMat;
            BuildMeshes();
        }

        public void Clear()
        {
            foreach (var p in pieces)
                if (p.t != null) Object.DestroyImmediate(p.t.gameObject);
            pieces.Clear();
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>
        /// Breaks something into tumbling pieces at a parent-local position. Push is added to every piece,
        /// for example the jeep's forward motion or a shovel's sideways shove.
        /// </summary>
        public void Burst(Transform parent, Vector3 localPos, DebrisKind kind, int count, float size, Vector3 push, float spread = 1f)
        {
            if (parent == null) return;
            Mesh[] set = kind switch
            {
                DebrisKind.Wood => woodPieces,
                DebrisKind.Metal => metalPieces,
                DebrisKind.Flesh => fleshPieces,
                _ => rockShards
            };
            for (int i = 0; i < count; i++)
            {
                Mesh mesh = kind == DebrisKind.Wood && i % 3 == 2 ? needleClump : set[rng.Next(set.Length)];
                var r = MeshBuilder.Spawn("Debris", parent, mesh, solidMat);
                float scale = size * R(0.7f, 1.25f);
                Vector3 offset = new Vector3(R(-0.6f, 0.6f) * spread, R(0.1f, 0.6f), R(-0.4f, 0.4f) * spread) * size;
                r.transform.localPosition = localPos + offset;
                r.transform.localRotation = Random3(i);
                r.transform.localScale = Vector3.one * scale;
                Vector3 dir = new Vector3(offset.x, 0f, offset.z).normalized;
                pieces.Add(new Piece
                {
                    t = r.transform,
                    r = r,
                    vel = push + dir * R(2f, 5.5f) * spread + Vector3.up * R(3.5f, 8f),
                    spinAxis = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized,
                    spin = R(240f, 720f),
                    life = R(1.6f, 2.3f),
                    fadeTime = 0.9f,
                    baseScale = scale,
                    bounce = true
                });
            }
        }

        /// <summary>Glowing droplets or sparks, fading out. Used for acid splashes, muzzle sparks and hit flashes.</summary>
        public void Splash(Transform parent, Vector3 localPos, Color color, int count, float speed, float size = 0.25f, float life = 0.7f, float upBias = 1f)
        {
            if (parent == null) return;
            for (int i = 0; i < count; i++)
            {
                var r = MeshBuilder.Spawn("Spark", parent, glowBlob, glowMat);
                float scale = size * R(0.6f, 1.3f);
                r.transform.localPosition = localPos;
                r.transform.localScale = Vector3.one * scale;
                Vector3 dir = new Vector3(R(-1f, 1f), R(0.2f, 1f) * upBias, R(-1f, 1f)).normalized;
                var p = new Piece
                {
                    t = r.transform,
                    r = r,
                    vel = dir * speed * R(0.5f, 1.2f),
                    life = life * R(0.7f, 1.2f),
                    fadeTime = life,
                    baseScale = scale,
                    glow = true,
                    bounce = false,
                    color = color
                };
                pieces.Add(p);
                ApplyGlow(p, 1f);
            }
        }

        /// <summary>Takes over an existing object: it keeps its pose, sinks and tumbles a little, and dissolves.</summary>
        public void FadeOut(Transform t, float seconds, Vector3 velocity, float spin = 90f)
        {
            if (t == null) return;
            var r = t.GetComponentInChildren<MeshRenderer>();
            pieces.Add(new Piece
            {
                t = t,
                r = r,
                vel = velocity,
                spinAxis = Vector3.right,
                spin = spin,
                life = seconds,
                fadeTime = seconds,
                baseScale = t.localScale.x,
                bounce = false
            });
        }

        public void Tick(float dt)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                if (p.t == null) { pieces.RemoveAt(i); continue; }
                p.age += dt;
                if (p.age >= p.life)
                {
                    Object.DestroyImmediate(p.t.gameObject);
                    pieces.RemoveAt(i);
                    continue;
                }
                if (!p.glow) p.vel += Vector3.down * Gravity * dt;
                else p.vel *= Mathf.Exp(-2.5f * dt);
                Vector3 pos = p.t.localPosition + p.vel * dt;
                if (p.bounce && pos.y < 0.08f * p.baseScale)
                {
                    pos.y = 0.08f * p.baseScale;
                    if (p.vel.y < 0f) p.vel.y = -p.vel.y * 0.35f;
                    p.vel.x *= 0.6f;
                    p.vel.z *= 0.6f;
                    p.spin *= 0.6f;
                }
                p.t.localPosition = pos;
                if (p.spin != 0f) p.t.localRotation = Quaternion.AngleAxis(p.spin * dt, p.spinAxis) * p.t.localRotation;

                float fade = Mathf.Clamp01((p.life - p.age) / Mathf.Max(p.fadeTime, 1e-3f));
                if (p.glow)
                {
                    ApplyGlow(p, fade);
                    p.t.localScale = Vector3.one * p.baseScale * (0.6f + 0.4f * fade);
                }
                else if (p.r != null)
                {
                    mpb.Clear();
                    mpb.SetFloat(FadeId, fade);
                    foreach (var rr in p.t.GetComponentsInChildren<MeshRenderer>()) rr.SetPropertyBlock(mpb);
                    if (fade < 0.4f) p.t.localScale = Vector3.one * p.baseScale * (0.5f + 0.5f * fade / 0.4f);
                }
            }
        }

        void ApplyGlow(Piece p, float fade)
        {
            if (p.r == null) return;
            Color c = p.color;
            c.a *= fade;
            mpb.Clear();
            mpb.SetColor(TintId, c);
            p.r.SetPropertyBlock(mpb);
        }

        Quaternion Random3(int i) => Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));

        // ---------- Shared meshes ----------

        static void BuildMeshes()
        {
            if (rockShards != null) return;
            var mb = new MeshBuilder();
            rockShards = new Mesh[3];
            for (int i = 0; i < 3; i++)
            {
                mb.Rock(Vector3.zero, 0.42f, 300 + i * 7, Palette.Rock * (1.05f + i * 0.08f));
                rockShards[i] = mb.ToMesh("LL_RockShard_" + i); mb.Clear();
            }

            woodPieces = new Mesh[3];
            mb.Box(Vector3.zero, new Vector3(0.42f, 0.42f, 1.1f), Quaternion.Euler(0f, 0f, 12f), Palette.Trunk * 1.1f);
            mb.Box(new Vector3(0f, 0f, 0.58f), new Vector3(0.36f, 0.36f, 0.08f), new Color(0.75f, 0.6f, 0.4f)); // pale broken end
            woodPieces[0] = mb.ToMesh("LL_LogPiece"); mb.Clear();
            mb.Box(Vector3.zero, new Vector3(0.12f, 0.12f, 0.9f), Quaternion.Euler(20f, 0f, 0f), Palette.Trunk);
            mb.Box(new Vector3(0.1f, 0f, 0.2f), new Vector3(0.08f, 0.08f, 0.45f), Quaternion.Euler(0f, 40f, 0f), Palette.Trunk * 0.9f);
            woodPieces[1] = mb.ToMesh("LL_Branch"); mb.Clear();
            mb.Box(Vector3.zero, new Vector3(0.28f, 0.28f, 0.7f), Quaternion.Euler(0f, 0f, -8f), Palette.Trunk * 0.95f);
            woodPieces[2] = mb.ToMesh("LL_LogChunk"); mb.Clear();

            mb.Cone(Vector3.zero, 0.35f, 0.55f, 5, Palette.Pine, 0.3f);
            needleClump = mb.ToMesh("LL_NeedleClump"); mb.Clear();

            metalPieces = new Mesh[2];
            mb.Box(Vector3.zero, new Vector3(0.7f, 0.08f, 0.45f), new Color(0.55f, 0.57f, 0.6f));
            metalPieces[0] = mb.ToMesh("LL_MetalPlate"); mb.Clear();
            mb.Box(Vector3.zero, new Vector3(0.12f, 0.12f, 0.6f), new Color(0.35f, 0.36f, 0.38f));
            metalPieces[1] = mb.ToMesh("LL_MetalBar"); mb.Clear();

            fleshPieces = new Mesh[2];
            mb.Box(Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), Palette.ShooterSkin);
            fleshPieces[0] = mb.ToMesh("LL_FleshChunk"); mb.Clear();
            mb.Box(Vector3.zero, new Vector3(0.22f, 0.4f, 0.22f), Palette.ShooterSack);
            fleshPieces[1] = mb.ToMesh("LL_SackChunk"); mb.Clear();

            mb.Orb(Vector3.zero, 1f, Color.white);
            glowBlob = mb.ToMesh("LL_GlowBlob"); mb.Clear();
        }
    }
}
