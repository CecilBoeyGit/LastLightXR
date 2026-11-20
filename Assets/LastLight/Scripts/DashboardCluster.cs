using UnityEngine;

namespace LastLight
{
    public enum Gauge { Speed, Horde, Health, Distance, Coins }

    /// <summary>Indicator lamps along the top of the dashboard, one per powerup or weather.</summary>
    public enum Lamp { Shield, Boost, Light, Turret, Shovel, Fog, Storm }

    /// <summary>
    /// The run HUD, styled as a car's instrument cluster floating upright beyond the far rim, at the top of the
    /// player's view where it never covers the road or the scouting region. A large speedometer sits in the
    /// middle with a digital readout; the left dial shows the horde's distance (with the health pips under
    /// it), the right dial shows distance toward your best run with an inner ring of coins toward the next
    /// garage unlock. Warning lamps across the top light up for active powerups and weather, with time left.
    /// Everything draws over the scene, so tall terrain never hides it.
    /// </summary>
    public sealed class DashboardCluster
    {
        /// <summary>Where the cluster floats, in disk units: above and beyond the far rim.</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(0f, 14.2f, 19f);

        // Dial sweep: 225 degrees (lower left) clockwise round to -45 degrees (lower right), like a real gauge.
        const float SweepFrom = 225f, SweepTo = -45f;
        const int ArcSegments = 72;

        static readonly Vector2 SpeedAt = new Vector2(0f, 0.2f);
        static readonly Vector2 LeftAt = new Vector2(-6.7f, -0.4f);
        static readonly Vector2 RightAt = new Vector2(6.7f, -0.4f);
        const float SpeedR = 3.25f, SideR = 2.45f;

        static readonly string[] LampNames = { "SHIELD", "BOOST", "LIGHT", "TURRET", "SHOVEL", "FOG", "STORM" };
        static readonly Color[] LampColors =
        {
            new Color(0.3f, 0.6f, 1f, 1f), new Color(1f, 0.5f, 0.16f, 1f), new Color(1f, 0.93f, 0.6f, 1f),
            new Color(1f, 0.32f, 0.26f, 1f), new Color(0.95f, 0.8f, 0.2f, 1f), new Color(0.75f, 0.8f, 0.85f, 1f), new Color(0.55f, 0.75f, 1f, 1f)
        };

        public static readonly Color[] Colors =
        {
            new Color(1f, 0.5f, 0.16f, 1f),         // speed: orange
            new Color(1f, 0.38f, 0.31f, 1f),        // horde: coral
            new Color(0.49f, 0.85f, 0.34f, 1f),     // health: green
            new Color(0.36f, 0.75f, 0.92f, 1f),     // distance: sky blue
            new Color(0.95f, 0.8f, 0.2f, 1f),       // coins: gold
        };

        class Dial
        {
            public Vector2 center;
            public float r0, r1;
            public Mesh fillMesh;
            public MeshRenderer fill;
            public Transform needle;
            public float shown = -1f, target;
            public Color color;
            public Label value;
        }

        class LampView
        {
            public UISprite glow, dot;
            public Label name, value;
            public bool on;
        }

        public readonly Transform root;
        readonly Dial[] dials = new Dial[5];
        readonly UISprite[] healthPips = new UISprite[3];
        readonly LampView[] lamps = new LampView[LampNames.Length];
        readonly Label speedReadout, speedUnit, alert, coinValue;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        int health = 3, maxHealth = 3;
        float time;

        public DashboardCluster(Transform parent, Material glowMat, Material alphaMat, Material addMat, Vector3 headLocal)
        {
            root = new GameObject("Dashboard Cluster").transform;
            root.SetParent(parent, false);
            root.localPosition = DefaultPosition;
            root.localRotation = Quaternion.LookRotation(DefaultPosition - headLocal, Vector3.up);

            // Copies that skip the depth test. Glow draws after the sprite housing, text after both.
            var glowTop = new Material(glowMat) { name = "LL_DashGlow", renderQueue = 3020 };
            glowTop.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
            var spriteTop = new Material(alphaMat) { name = "LL_DashSprite" };
            spriteTop.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
            var addTop = new Material(addMat) { name = "LL_DashAdd", renderQueue = 3025 };
            addTop.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);

            Texture2D white = UISprite.Load("white"), dot = UISprite.Load("dot"), ring = UISprite.Load("ring");
            Color ink = Palette.UIInk; ink.a = 0.86f;
            Color ivory = Palette.UIIvory;

            // ----- Housing: a dark visor joining three round dial faces, with an ivory rim and amber trim -----
            new UISprite("Visor Rim", root, white, new Vector2(17.6f, 5.4f), spriteTop, 0) { Position = new Vector2(0f, -0.9f), Color = new Color(ivory.r, ivory.g, ivory.b, 0.85f) };
            new UISprite("Visor", root, white, new Vector2(17.2f, 5.0f), spriteTop, 1) { Position = new Vector2(0f, -0.9f), Color = ink };
            new UISprite("Lamp Strip", root, white, new Vector2(15.2f, 1.75f), spriteTop, 1) { Position = new Vector2(0f, 5.05f), Color = ink };
            new UISprite("Lamp Strip Trim", root, white, new Vector2(15.2f, 0.08f), spriteTop, 2) { Position = new Vector2(0f, 4.18f), Color = Palette.UIAmber };
            Face(SpeedAt, SpeedR + 0.6f, dot, ring, spriteTop, ink, ivory);
            Face(LeftAt, SideR + 0.6f, dot, ring, spriteTop, ink, ivory);
            Face(RightAt, SideR + 0.6f, dot, ring, spriteTop, ink, ivory);

            // ----- Dials -----
            dials[(int)Gauge.Speed] = MakeDial("Speed", SpeedAt, SpeedR - 0.45f, SpeedR, Colors[(int)Gauge.Speed], glowTop, 10, true);
            dials[(int)Gauge.Horde] = MakeDial("Horde", LeftAt, SideR - 0.38f, SideR, Colors[(int)Gauge.Horde], glowTop, 6, true);
            dials[(int)Gauge.Distance] = MakeDial("Distance", RightAt, SideR - 0.38f, SideR, Colors[(int)Gauge.Distance], glowTop, 6, true);
            dials[(int)Gauge.Coins] = MakeDial("Coins", RightAt, SideR - 0.78f, SideR - 0.56f, Colors[(int)Gauge.Coins], glowTop, 0, false);
            dials[(int)Gauge.Health] = new Dial { color = Colors[(int)Gauge.Health], center = LeftAt };

            // Speed numbers round the big dial, 0 to 160 km/h.
            for (int v = 0; v <= 160; v += 40)
            {
                float a = AngleAt(v / 168f) * Mathf.Deg2Rad;
                Vector2 p = SpeedAt + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (SpeedR - 1.0f);
                new Label(root, "Speed " + v, v.ToString(), 0.34f, TextAnchor.MiddleCenter, new Color(0.8f, 0.76f, 0.66f), p, 24, true);
            }
            speedReadout = new Label(root, "Speed Readout", "0", 1.15f, TextAnchor.MiddleCenter, ivory, SpeedAt + new Vector2(0f, -1.15f), 26, true);
            speedUnit = new Label(root, "Speed Unit", "KM/H", 0.36f, TextAnchor.MiddleCenter, Palette.UIAmber, SpeedAt + new Vector2(0f, -2.2f), 26, true);

            // Left dial: horde icon, state, and the health pips like a fuel gauge's segments.
            new UISprite("Horde Icon", root, UISprite.Load("icon_horde"), new Vector2(0.95f, 0.95f), spriteTop, 4) { Position = LeftAt + new Vector2(0f, 0.85f) };
            dials[(int)Gauge.Horde].value = new Label(root, "Horde Value", "", 0.46f, TextAnchor.MiddleCenter, ivory, LeftAt + new Vector2(0f, -0.15f), 26, true);
            new UISprite("Health Icon", root, UISprite.Load("icon_health"), new Vector2(0.6f, 0.6f), spriteTop, 4) { Position = LeftAt + new Vector2(-0.95f, -1.05f) };
            for (int i = 0; i < healthPips.Length; i++)
                healthPips[i] = new UISprite("Health Pip " + i, root, white, new Vector2(0.42f, 0.3f), spriteTop, 4) { Position = LeftAt + new Vector2(-0.35f + i * 0.52f, -1.05f) };
            dials[(int)Gauge.Health].value = new Label(root, "Health Value", "", 0.01f, TextAnchor.MiddleCenter, ivory, LeftAt, 26, true);
            dials[(int)Gauge.Health].value.Visible = false;

            // Right dial: distance in the middle, coins under it.
            new UISprite("Distance Icon", root, UISprite.Load("icon_distance"), new Vector2(0.9f, 0.9f), spriteTop, 4) { Position = RightAt + new Vector2(0f, 0.75f) };
            dials[(int)Gauge.Distance].value = new Label(root, "Distance Value", "", 0.42f, TextAnchor.MiddleCenter, ivory, RightAt + new Vector2(0f, -0.15f), 26, true);
            new UISprite("Coin Icon", root, UISprite.Load("coin"), new Vector2(0.5f, 0.5f), spriteTop, 4) { Position = RightAt + new Vector2(-0.75f, -0.95f) };
            coinValue = new Label(root, "Coin Value", "", 0.34f, TextAnchor.MiddleLeft, ivory, RightAt + new Vector2(-0.42f, -0.93f), 26, true);
            dials[(int)Gauge.Coins].value = coinValue;

            // ----- Warning lamps -----
            for (int i = 0; i < lamps.Length; i++)
            {
                float x = -6.3f + i * 2.1f;
                var l = new LampView();
                l.glow = new UISprite("Lamp Glow " + LampNames[i], root, UISprite.Load("spot"), new Vector2(1.6f, 1.6f), addTop, 5) { Position = new Vector2(x, 5.5f) };
                l.dot = new UISprite("Lamp " + LampNames[i], root, dot, new Vector2(0.55f, 0.55f), spriteTop, 4) { Position = new Vector2(x, 5.5f) };
                l.name = new Label(root, "Lamp Name " + LampNames[i], LampNames[i], 0.3f, TextAnchor.MiddleCenter, ivory, new Vector2(x, 4.9f), 26, true);
                l.value = new Label(root, "Lamp Value " + LampNames[i], "", 0.28f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(x, 4.56f), 26, true);
                lamps[i] = l;
            }

            alert = new Label(root, "Alert", "", 0.7f, TextAnchor.MiddleCenter, Colors[(int)Gauge.Horde], new Vector2(0f, -4.15f), 26, true);
        }

        void Face(Vector2 at, float radius, Texture2D dot, Texture2D ring, Material mat, Color ink, Color ivory)
        {
            new UISprite("Dial Rim", root, dot, Vector2.one * radius * 2f + Vector2.one * 0.32f, mat, 2) { Position = at, Color = new Color(ivory.r, ivory.g, ivory.b, 0.9f) };
            new UISprite("Dial Face", root, dot, Vector2.one * radius * 2f, mat, 3) { Position = at, Color = new Color(0.06f, 0.08f, 0.1f, 0.97f) };
        }

        Dial MakeDial(string name, Vector2 center, float r0, float r1, Color color, Material glow, int ticks, bool needle)
        {
            var d = new Dial { center = center, r0 = r0, r1 = r1, color = color };
            // Dim track, tick marks, and the fill on top.
            mb.Clear();
            Color track = color * 0.28f; track.a = 0.75f;
            Sector(mb, center, r0, r1, 0f, 1f, track);
            for (int i = 0; i <= ticks && ticks > 0; i++)
            {
                float a = AngleAt(i / (float)ticks) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p0 = center + dir * (r0 - 0.32f), p1 = center + dir * (r0 - 0.08f);
                Vector2 side = new Vector2(-dir.y, dir.x) * 0.04f;
                mb.Quad(V(p0 - side), V(p1 - side), V(p1 + side), V(p0 + side), new Color(0.95f, 0.9f, 0.8f, 0.8f));
            }
            // Transparent sorting puts sortingOrder ahead of the render queue, so the glow is ordered above the
            // sprite housing (orders 0-5) and below the text (24 and up) explicitly.
            MeshBuilder.Spawn(name + " Track", root, mb.ToMesh("LL_DashTrack_" + name), glow).sortingOrder = 10;
            d.fillMesh = new Mesh { name = "LL_DashFill_" + name };
            d.fill = MeshBuilder.Spawn(name + " Fill", root, d.fillMesh, glow);
            d.fill.sortingOrder = 11;
            if (needle)
            {
                mb.Clear();
                float len = r1 - 0.1f;
                mb.Quad(new Vector3(-0.07f, 0f, 0f), new Vector3(-0.025f, len, 0f), new Vector3(0.025f, len, 0f), new Vector3(0.07f, 0f, 0f), new Color(1f, 0.95f, 0.85f, 1f));
                for (int k = 0; k < 12; k++)
                {
                    float a0 = k * Mathf.PI / 6f, a1 = (k + 1) * Mathf.PI / 6f;
                    mb.Tri(Vector3.zero, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.2f, new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.2f, new Color(1f, 0.95f, 0.85f, 1f));
                }
                var n = MeshBuilder.Spawn(name + " Needle", root, mb.ToMesh("LL_DashNeedle_" + name), glow);
                n.transform.localPosition = new Vector3(center.x, center.y, -0.3f);
                n.sortingOrder = 12;
                d.needle = n.transform;
            }
            return d;
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set { if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value); }
        }

        /// <summary>Sets a dial's fill (0 to 1) and its value text. Fills and needles ease toward the new value.</summary>
        public void Set(Gauge g, float fraction, string value)
        {
            var d = dials[(int)g];
            d.target = Mathf.Clamp01(fraction);
            if (g == Gauge.Health)
            {
                d.shown = d.target;
                health = Mathf.RoundToInt(d.target * maxHealth);
            }
            if (g == Gauge.Speed) speedReadout.Text = value.Replace(" KM/H", "");
            if (d.value != null) d.value.Text = value;
        }

        public float Fill(Gauge g) => dials[(int)g].shown;
        public string Value(Gauge g) => g == Gauge.Speed ? speedReadout.Text + " KM/H" : dials[(int)g].value.Text;
        public void SetColor(Gauge g, Color c) => dials[(int)g].color = c;

        /// <summary>Lights a warning lamp, with an optional value such as seconds left.</summary>
        public void SetLamp(Lamp lamp, bool on, string value = "")
        {
            var l = lamps[(int)lamp];
            l.on = on;
            l.value.Text = on ? value : "";
        }

        public bool LampOn(Lamp lamp) => lamps[(int)lamp].on;

        public void SetAlert(string text) => alert.Text = text;
        public string AlertText => alert.Text;

        public void Tick(float dt)
        {
            time += dt;
            foreach (Gauge g in new[] { Gauge.Speed, Gauge.Horde, Gauge.Distance, Gauge.Coins })
            {
                var d = dials[(int)g];
                float next = d.shown < 0f ? d.target : Mathf.Lerp(d.shown, d.target, 1f - Mathf.Exp(-9f * Mathf.Max(dt, 0f)));
                if (Mathf.Abs(next - d.target) < 0.001f) next = d.target;
                if (Mathf.Abs(next - d.shown) > 0.0005f || d.shown < 0f)
                {
                    d.shown = next;
                    mb.Clear();
                    if (d.shown > 0.001f)
                    {
                        Sector(mb, d.center, d.r0 + 0.05f, d.r1 - 0.05f, 0f, d.shown, Color.white);
                        Sector(mb, d.center, d.r0, d.r1, Mathf.Max(0f, d.shown - 0.012f), d.shown, Color.white * 1.6f);
                    }
                    mb.ToMesh(d.fillMesh.name, d.fillMesh);
                    if (d.needle != null) d.needle.localRotation = Quaternion.Euler(0f, 0f, AngleAt(d.shown) - 90f);
                }
                Color c = d.color;
                bool low = g == Gauge.Horde && d.target <= 0.34f;
                c *= low ? 1.1f + 0.6f * Mathf.Abs(Mathf.Sin(time * 6f)) : 1.25f;
                c.a = 0.95f;
                mpb.SetColor(TintId, c);
                d.fill.SetPropertyBlock(mpb);
                if (d.value != null) d.value.Color = low ? Colors[(int)Gauge.Horde] : Palette.UIIvory;
            }

            // Health pips: lit green while you have them, pulsing red on the last one.
            for (int i = 0; i < healthPips.Length; i++)
            {
                bool lit = i < health;
                Color c = lit ? (health <= 1 ? Color.Lerp(Colors[(int)Gauge.Horde], Color.white, 0.3f * Mathf.Abs(Mathf.Sin(time * 6f))) : Colors[(int)Gauge.Health]) : new Color(0.25f, 0.27f, 0.25f, 1f);
                healthPips[i].Color = c;
            }

            for (int i = 0; i < lamps.Length; i++)
            {
                var l = lamps[i];
                Color c = LampColors[i];
                float flicker = l.on ? 0.85f + 0.15f * Mathf.Sin(time * 9f + i) : 0f;
                l.dot.Color = l.on ? c : new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, 0.9f);
                l.glow.Color = new Color(c.r, c.g, c.b, 0.75f * flicker);
                l.name.Alpha = l.on ? 1f : 0.35f;
            }
            alert.Alpha = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(time * 5f));
        }

        static float AngleAt(float fraction) => Mathf.Lerp(SweepFrom, SweepTo, Mathf.Clamp01(fraction));

        static Vector3 V(Vector2 p) => new Vector3(p.x, p.y, -0.2f);

        /// <summary>A flat ring sector in the panel's XY plane between two fractions of the dial's sweep.</summary>
        static void Sector(MeshBuilder mb, Vector2 center, float r0, float r1, float from, float to, Color col)
        {
            float a0d = AngleAt(from), a1d = AngleAt(to);
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(a1d - a0d) / 270f * ArcSegments));
            for (int k = 0; k < n; k++)
            {
                float a0 = Mathf.Lerp(a0d, a1d, k / (float)n) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(a0d, a1d, (k + 1) / (float)n) * Mathf.Deg2Rad;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                mb.Quad(V(center + d0 * r0), V(center + d1 * r0), V(center + d1 * r1), V(center + d0 * r1), col);
            }
        }
    }
}
