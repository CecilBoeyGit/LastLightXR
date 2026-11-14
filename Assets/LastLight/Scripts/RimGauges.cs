using UnityEngine;

namespace LastLight
{
    public enum Gauge { Speed, Horde, Health, Distance, Coins }

    /// <summary>
    /// The run HUD: five quarter-circle slider bars hugging the island's rim on the player's lower left.
    /// Each bar has its own colour, an icon badge at its near end and a value beside the icon. Fills grow
    /// from the near end, under the player's gaze, around the rim toward the far left. A status line above
    /// the arcs names active powerups, and an alert line flags acid, traps and respawn grace.
    /// Everything draws over the scene, so tall terrain at the rim never hides it.
    /// </summary>
    public sealed class RimGauges
    {
        // Angles in the disk's XZ plane: 180 is straight left, 270 is the near edge under the player.
        public const float StartDeg = 236f;         // near end: where every fill starts
        public const float EndDeg = 146f;           // far end: where a full bar finishes
        const float InnerRadius = Diorama.Radius + 0.6f;
        const float BarWidth = 0.8f;
        const float BarStep = 1.2f;
        const float Height = 0.12f;
        const int SegmentsPerQuarter = 48;

        static readonly string[] Icons = { "icon_speed", "icon_horde", "icon_health", "icon_distance", "coin" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.5f, 0.16f, 1f),         // speed: orange
            new Color(1f, 0.38f, 0.31f, 1f),        // horde: coral
            new Color(0.49f, 0.85f, 0.34f, 1f),     // health: green
            new Color(0.36f, 0.75f, 0.92f, 1f),     // distance: sky blue
            new Color(0.95f, 0.8f, 0.2f, 1f),       // coins: gold
        };

        class Bar
        {
            public Mesh fillMesh;
            public MeshRenderer fill;
            public Label value;
            public UISprite icon;
            public float shown = -1f, target;
            public int segments;
            public Color color;
        }

        public readonly Transform root;
        readonly Bar[] bars = new Bar[5];
        readonly Label status, alert;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        float time;

        public RimGauges(Transform parent, Material glowMat, Material alphaMat, Vector3 headLocal)
        {
            root = new GameObject("Rim Gauges").transform;
            root.SetParent(parent, false);

            // Copies of the glow and sprite materials that skip the depth test.
            var glowTop = new Material(glowMat) { name = "LL_GaugeGlow" };
            glowTop.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
            var spriteTop = new Material(alphaMat) { name = "LL_GaugeSprite" };
            spriteTop.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);

            for (int i = 0; i < bars.Length; i++)
            {
                var b = new Bar { color = Colors[i], segments = i == (int)Gauge.Health ? 3 : 0 };
                float r0 = InnerRadius + i * BarStep, r1 = r0 + BarWidth;

                // Dim track for the whole quarter, with a brighter edge line along its outer rim.
                mb.Clear();
                Color track = b.color * 0.3f; track.a = 0.6f;
                Ring(mb, r0, r1, 0f, 1f, track, b.segments);
                Color edge = b.color; edge.a = 0.5f;
                Ring(mb, r1 - 0.07f, r1, 0f, 1f, edge, 0);
                MeshBuilder.Spawn("Track " + (Gauge)i, root, mb.ToMesh("LL_GaugeTrack_" + i), glowTop);

                b.fillMesh = new Mesh { name = "LL_GaugeFill_" + i };
                b.fill = MeshBuilder.Spawn("Fill " + (Gauge)i, root, b.fillMesh, glowTop);

                bars[i] = b;
            }

            // A legend beside the arcs' near ends: one row per bar, icon then value, innermost bar on top
            // (the inner arcs sit higher in the player's view). It faces the player square-on.
            float outerR = InnerRadius + (bars.Length - 1) * BarStep + BarWidth;
            Vector3 legendPos = OnRing(outerR + 3.4f, StartDeg - 5f, 1.4f);
            var legend = new GameObject("Legend").transform;
            legend.SetParent(root, false);
            legend.localPosition = legendPos;
            legend.localRotation = Face(legendPos, headLocal);
            for (int i = 0; i < bars.Length; i++)
            {
                var b = bars[i];
                float y = (2 - i) * 1.25f;
                b.icon = new UISprite("Icon " + (Gauge)i, legend, UISprite.Load(Icons[i]), new Vector2(1.1f, 1.1f), spriteTop, 4) { Position = new Vector2(0f, y) };
                b.value = new Label(legend, "Value " + (Gauge)i, "", 0.62f, TextAnchor.MiddleLeft, Palette.UIIvory, new Vector2(0.8f, y + 0.03f), 24, true);
                bars[i] = b;
            }

            // Status and alert lines float above the middle of the arcs.
            float outer = InnerRadius + (bars.Length - 1) * BarStep + BarWidth;
            Vector3 statusPos = OnRing(outer + 1.2f, (StartDeg + EndDeg) * 0.5f, 2.4f);
            status = new Label(root, "Status", "", 0.62f, TextAnchor.MiddleCenter, Palette.UIIvory, Vector2.zero, 24, true);
            status.transform.localPosition = statusPos;
            status.transform.localRotation = Face(statusPos, headLocal);
            Vector3 alertPos = statusPos + Vector3.down * 1.0f;
            alert = new Label(root, "Alert", "", 0.68f, TextAnchor.MiddleCenter, Colors[(int)Gauge.Horde], Vector2.zero, 24, true);
            alert.transform.localPosition = alertPos;
            alert.transform.localRotation = Face(alertPos, headLocal);
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set { if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value); }
        }

        /// <summary>Sets a bar's fill (0 to 1) and its value text. Fills ease toward the new value.</summary>
        public void Set(Gauge g, float fraction, string value)
        {
            var b = bars[(int)g];
            b.target = Mathf.Clamp01(fraction);
            b.value.Text = value;
        }

        public float Fill(Gauge g) => bars[(int)g].shown;
        public string Value(Gauge g) => bars[(int)g].value.Text;
        public void SetColor(Gauge g, Color c) => bars[(int)g].color = c;

        public void SetStatus(string text, string alertText)
        {
            status.Text = text;
            alert.Text = alertText;
        }

        public void Tick(float dt)
        {
            time += dt;
            for (int i = 0; i < bars.Length; i++)
            {
                var b = bars[i];
                float next = b.shown < 0f ? b.target : Mathf.Lerp(b.shown, b.target, 1f - Mathf.Exp(-10f * Mathf.Max(dt, 0f)));
                if (Mathf.Abs(next - b.target) < 0.001f) next = b.target;
                if (Mathf.Abs(next - b.shown) > 0.0005f || b.shown < 0f)
                {
                    b.shown = next;
                    float r0 = InnerRadius + i * BarStep, r1 = r0 + BarWidth;
                    mb.Clear();
                    float fill = b.segments > 0 ? Mathf.Round(b.shown * b.segments) / b.segments : b.shown;
                    if (fill > 0.001f)
                    {
                        Ring(mb, r0 + 0.06f, r1 - 0.06f, 0f, fill, Color.white, b.segments);
                        // A brighter cap marks the leading edge of a continuous bar.
                        if (b.segments == 0) Ring(mb, r0, r1, Mathf.Max(0f, fill - 0.014f), fill, Color.white * 1.6f, 0);
                    }
                    mb.ToMesh(b.fillMesh.name, b.fillMesh);
                }
                // Low bars pulse so danger reads at a glance.
                Color c = b.color;
                bool low = (i == (int)Gauge.Horde || i == (int)Gauge.Health) && b.target <= 0.34f;
                c *= low ? 1.1f + 0.6f * Mathf.Abs(Mathf.Sin(time * 6f)) : 1.25f;
                c.a = 0.95f;
                mpb.SetColor(TintId, c);
                b.fill.SetPropertyBlock(mpb);
                b.value.Color = low ? Colors[(int)Gauge.Horde] : Palette.UIIvory;
            }
            alert.Alpha = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(time * 5f));
        }

        static float AngleAt(float fraction) => Mathf.Lerp(StartDeg, EndDeg, fraction);

        static Vector3 OnRing(float radius, float deg, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
        }

        /// <summary>
        /// Turned square-on to the player's eyes, tilted back as well as round, so text reads flat even though
        /// the player looks down at it. Panels face -Z toward their viewer, so look away from the head.
        /// </summary>
        static Quaternion Face(Vector3 pos, Vector3 headLocal)
        {
            Vector3 away = pos - headLocal;
            return Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward, Vector3.up);
        }

        /// <summary>
        /// A flat ring sector in the XZ plane, facing up, between two fractions of the quarter. With segments,
        /// the quarter is split into that many pieces with small gaps, clipped to the range.
        /// </summary>
        static void Ring(MeshBuilder mb, float r0, float r1, float from, float to, Color col, int segments)
        {
            if (segments <= 0)
            {
                Sector(mb, r0, r1, AngleAt(from), AngleAt(to), col);
                return;
            }
            const float gap = 0.018f;   // of the quarter
            for (int s = 0; s < segments; s++)
            {
                float a = (float)s / segments + (s > 0 ? gap * 0.5f : 0f);
                float b = (float)(s + 1) / segments - (s < segments - 1 ? gap * 0.5f : 0f);
                a = Mathf.Max(a, from);
                b = Mathf.Min(b, to);
                if (b <= a) continue;
                Sector(mb, r0, r1, AngleAt(a), AngleAt(b), col);
            }
        }

        static void Sector(MeshBuilder mb, float r0, float r1, float fromDeg, float toDeg, Color col)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 90f * SegmentsPerQuarter));
            Vector3 up = Vector3.up * Height;
            for (int k = 0; k < n; k++)
            {
                float a0 = Mathf.Lerp(fromDeg, toDeg, k / (float)n) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(fromDeg, toDeg, (k + 1) / (float)n) * Mathf.Deg2Rad;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                mb.QuadOut(d0 * r0 + up, d1 * r0 + up, d1 * r1 + up, d0 * r1 + up, col, up + Vector3.down);
            }
        }
    }
}
