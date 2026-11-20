using UnityEngine;

namespace LastLight
{
    public enum Gauge { Health, Distance, Coins, Weather }

    /// <summary>Indicator lamps along the top of the dashboard, one per timed powerup.</summary>
    public enum Lamp { Shield, Boost, Light, Turret, Shovel }

    /// <summary>
    /// The run HUD, styled as a car's instrument cluster floating upright beyond the far rim, at the top of the
    /// player's view where it never covers the road or the scouting region.
    /// The big dial in the middle is health: a ring split into three segments that fill and drain radially,
    /// pulsing red on the last one. To its left a smaller digital dial shows the weather: an icon for the current
    /// weather inside a ring that runs down with the time left, and the seconds. To its right the storage
    /// compartment is built into the dash (see <see cref="StorageRack"/>). A lamp strip across the top lights up for
    /// active powerups, with the distance readout at its left end and the coin bank at its right.
    /// Everything except the storage grid draws over the scene, so tall terrain never hides it.
    /// </summary>
    public sealed class DashboardCluster
    {
        /// <summary>Where the cluster floats, in disk units: above and beyond the far rim.</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(0f, 14.2f, 19f);
        /// <summary>Where the storage grid sits on the dashboard, in dashboard units.</summary>
        public static readonly Vector2 StorageCenter = new Vector2(7.45f, -0.4f);

        static readonly Vector2 HealthAt = new Vector2(0f, 0.2f);
        static readonly Vector2 WeatherAt = new Vector2(-6.9f, -0.5f);
        const float HealthR0 = 2.45f, HealthR1 = 3.3f, HealthGapDeg = 7f;
        const float WeatherR0 = 1.72f, WeatherR1 = 2.0f;
        const float LampY = 5.35f;

        static readonly string[] LampNames = { "SHIELD", "BOOST", "LIGHT", "TURRET", "SHOVEL" };
        static readonly Color[] LampColors =
        {
            new Color(0.3f, 0.6f, 1f, 1f), new Color(1f, 0.5f, 0.16f, 1f), new Color(1f, 0.93f, 0.6f, 1f),
            new Color(1f, 0.32f, 0.26f, 1f), new Color(0.95f, 0.8f, 0.2f, 1f)
        };

        public static readonly Color HealthGreen = new Color(0.49f, 0.85f, 0.34f, 1f);
        public static readonly Color HealthAmber = new Color(0.98f, 0.76f, 0.22f, 1f);
        public static readonly Color HealthRed = new Color(1f, 0.32f, 0.26f, 1f);
        public static readonly Color DistanceBlue = new Color(0.36f, 0.75f, 0.92f, 1f);
        public static readonly Color CoinGold = new Color(0.95f, 0.8f, 0.2f, 1f);

        class LampView
        {
            public UISprite glow, dot;
            public Label name, value;
            public bool on;
        }

        public readonly Transform root;
        readonly LampView[] lamps = new LampView[LampNames.Length];
        readonly Label healthValue, healthCaption, distanceValue, coinValue, alert;
        readonly Label weatherName, weatherSeconds;
        readonly Mesh healthFill, weatherFill;
        readonly MeshRenderer healthFillRenderer, weatherFillRenderer, weatherTrack;
        readonly MeshRenderer[] weatherIcons = new MeshRenderer[4];
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        int health = 3, maxHealth = 3;
        float healthShown = -1f, weatherShown = -1f, weatherTarget, time;
        WeatherKind weather = WeatherKind.Clear;
        float weatherFlash;
        string distanceText = "0 M", coinText = "0";
        bool newBest;

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

            Texture2D white = UISprite.Load("white"), dot = UISprite.Load("dot");
            // Near-opaque, so the ivory rim behind doesn't show through and tint the plate brown.
            Color ink = Palette.UIInk; ink.a = 0.97f;
            Color ivory = Palette.UIIvory;

            // ----- Housing: a dark visor behind the weather and health dials, and the lamp strip over the whole dash -----
            new UISprite("Visor Rim", root, white, new Vector2(13.4f, 5.6f), spriteTop, 0) { Position = new Vector2(-2.6f, -0.9f), Color = new Color(ivory.r, ivory.g, ivory.b, 0.85f) };
            new UISprite("Visor", root, white, new Vector2(13.0f, 5.2f), spriteTop, 1) { Position = new Vector2(-2.6f, -0.9f), Color = ink };
            new UISprite("Lamp Strip", root, white, new Vector2(20.2f, 1.75f), spriteTop, 1) { Position = new Vector2(-0.2f, LampY - 0.45f), Color = ink };
            new UISprite("Lamp Strip Trim", root, white, new Vector2(20.2f, 0.08f), spriteTop, 2) { Position = new Vector2(-0.2f, LampY - 1.32f), Color = Palette.UIAmber };
            Face(HealthAt, HealthR1 + 0.55f, dot, spriteTop, ivory);
            Face(WeatherAt, WeatherR1 + 0.45f, dot, spriteTop, ivory);

            // ----- Health: three ring segments, a dim track under a radial fill -----
            mb.Clear();
            for (int k = 0; k < 3; k++)
            {
                Color track = HealthGreen * 0.25f; track.a = 0.75f;
                HealthSegment(mb, k, 1f, HealthR0, HealthR1, track);
            }
            MeshBuilder.Spawn("Health Track", root, mb.ToMesh("LL_DashHealthTrack"), glowTop).sortingOrder = 10;
            healthFill = new Mesh { name = "LL_DashHealthFill" };
            healthFillRenderer = MeshBuilder.Spawn("Health Fill", root, healthFill, glowTop);
            healthFillRenderer.sortingOrder = 11;
            new UISprite("Health Icon", root, UISprite.Load("icon_health"), new Vector2(1.5f, 1.5f), spriteTop, 4) { Position = HealthAt + new Vector2(0f, 1.0f) };
            healthValue = new Label(root, "Health Value", "3/3", 1.0f, TextAnchor.MiddleCenter, ivory, HealthAt + new Vector2(0f, -0.45f), 26, true);
            healthCaption = new Label(root, "Health Caption", "HEALTH", 0.32f, TextAnchor.MiddleCenter, Palette.UIAmber, HealthAt + new Vector2(0f, -1.5f), 26, true);

            // ----- Weather: a thin ring that runs down with the time left, around the weather's icon -----
            mb.Clear();
            mb.Arc(V(WeatherAt), WeatherR0, WeatherR1, 1f, 64, new Color(0.6f, 0.7f, 0.8f, 0.3f));
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p0 = WeatherAt + d * (WeatherR0 - 0.22f), p1 = WeatherAt + d * (WeatherR0 - 0.06f);
                Vector2 side = new Vector2(-d.y, d.x) * 0.035f;
                mb.Quad(V(p0 - side), V(p1 - side), V(p1 + side), V(p0 + side), new Color(0.9f, 0.9f, 0.85f, 0.6f));
            }
            weatherTrack = MeshBuilder.Spawn("Weather Track", root, mb.ToMesh("LL_DashWeatherTrack"), glowTop);
            weatherTrack.sortingOrder = 10;
            weatherFill = new Mesh { name = "LL_DashWeatherFill" };
            weatherFillRenderer = MeshBuilder.Spawn("Weather Fill", root, weatherFill, glowTop);
            weatherFillRenderer.sortingOrder = 11;
            string[] iconNames = { "Clear", "Fog", "Storm", "Snow" };
            for (int i = 0; i < weatherIcons.Length; i++)
            {
                mb.Clear();
                BuildWeatherIcon(mb, (WeatherKind)i);
                var r = MeshBuilder.Spawn("Weather Icon " + iconNames[i], root, mb.ToMesh("LL_DashWeather_" + iconNames[i]), glowTop);
                r.transform.localPosition = new Vector3(WeatherAt.x, WeatherAt.y + 0.18f, -0.25f);
                r.transform.localScale = Vector3.one * 0.8f;
                r.sortingOrder = 12;
                weatherIcons[i] = r;
            }
            weatherName = new Label(root, "Weather Name", "CLEAR", 0.3f, TextAnchor.MiddleCenter, ivory, WeatherAt + new Vector2(0f, 1.1f), 26, true);
            weatherSeconds = new Label(root, "Weather Seconds", "", 0.48f, TextAnchor.MiddleCenter, ivory, WeatherAt + new Vector2(0f, -0.95f), 26, true);
            new Label(root, "Weather Caption", "WEATHER", 0.26f, TextAnchor.MiddleCenter, Palette.UIAmber, WeatherAt + new Vector2(0f, -WeatherR1 - 0.75f), 26, true);

            // ----- Lamp strip: distance on the left, powerup lamps in the middle, coins on the right -----
            new UISprite("Distance Icon", root, UISprite.Load("icon_distance"), new Vector2(0.75f, 0.75f), spriteTop, 4) { Position = new Vector2(-9.55f, LampY - 0.4f) };
            distanceValue = new Label(root, "Distance Value", "0 M", 0.46f, TextAnchor.MiddleLeft, ivory, new Vector2(-9.05f, LampY - 0.38f), 26, true);
            for (int i = 0; i < lamps.Length; i++)
            {
                float x = -4.2f + i * 2.1f;
                var l = new LampView();
                l.glow = new UISprite("Lamp Glow " + LampNames[i], root, UISprite.Load("spot"), new Vector2(1.6f, 1.6f), addTop, 5) { Position = new Vector2(x, LampY) };
                l.dot = new UISprite("Lamp " + LampNames[i], root, dot, new Vector2(0.55f, 0.55f), spriteTop, 4) { Position = new Vector2(x, LampY) };
                l.name = new Label(root, "Lamp Name " + LampNames[i], LampNames[i], 0.3f, TextAnchor.MiddleCenter, ivory, new Vector2(x, LampY - 0.6f), 26, true);
                l.value = new Label(root, "Lamp Value " + LampNames[i], "", 0.28f, TextAnchor.MiddleCenter, Palette.UIAmber, new Vector2(x, LampY - 0.94f), 26, true);
                lamps[i] = l;
            }
            new UISprite("Coin Icon", root, UISprite.Load("coin"), new Vector2(0.6f, 0.6f), spriteTop, 4) { Position = new Vector2(6.0f, LampY - 0.4f) };
            coinValue = new Label(root, "Coin Value", "0", 0.42f, TextAnchor.MiddleLeft, ivory, new Vector2(6.45f, LampY - 0.38f), 26, true);

            alert = new Label(root, "Alert", "", 0.7f, TextAnchor.MiddleCenter, HealthRed, new Vector2(-1.2f, -4.45f), 26, true);
        }

        void Face(Vector2 at, float radius, Texture2D dot, Material mat, Color ivory)
        {
            new UISprite("Dial Rim", root, dot, Vector2.one * radius * 2f + Vector2.one * 0.32f, mat, 2) { Position = at, Color = new Color(ivory.r, ivory.g, ivory.b, 0.9f) };
            new UISprite("Dial Face", root, dot, Vector2.one * radius * 2f, mat, 3) { Position = at, Color = new Color(0.06f, 0.08f, 0.1f, 0.97f) };
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set { if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value); }
        }

        /// <summary>Sets the health shown, in whole bars. The segments fill and drain toward it.</summary>
        public void SetHealth(int value, int max)
        {
            maxHealth = Mathf.Max(1, max);
            health = Mathf.Clamp(value, 0, maxHealth);
            healthValue.Text = $"{health}/{maxHealth}";
        }

        /// <summary>The weather dial: which weather, the fraction of it left, and seconds left.</summary>
        public void SetWeather(WeatherKind kind, float fractionLeft, float secondsLeft)
        {
            if (kind != weather) weatherFlash = 0.6f;
            weather = kind;
            weatherTarget = kind == WeatherKind.Clear ? 0f : Mathf.Clamp01(fractionLeft);
            weatherName.Text = kind switch { WeatherKind.Fog => "FOG", WeatherKind.Storm => "STORM", WeatherKind.Snow => "SNOW", _ => "CLEAR" };
            weatherSeconds.Text = kind == WeatherKind.Clear ? "" : $"{Mathf.CeilToInt(Mathf.Max(secondsLeft, 0f))}S";
        }

        public void SetDistance(string text, bool best)
        {
            distanceText = text;
            newBest = best;
            distanceValue.Text = text;
        }

        public void SetCoins(string text)
        {
            coinText = text;
            coinValue.Text = text;
        }

        /// <summary>A gauge's fill, 0 to 1. For tests.</summary>
        public float Fill(Gauge g) => g switch
        {
            Gauge.Health => Mathf.Clamp01(healthShown < 0f ? health / (float)maxHealth : healthShown / maxHealth),
            Gauge.Weather => Mathf.Clamp01(Mathf.Max(weatherShown, 0f)),
            _ => 1f
        };

        public string Value(Gauge g) => g switch
        {
            Gauge.Health => healthValue.Text,
            Gauge.Distance => distanceText,
            Gauge.Coins => coinText,
            _ => weather == WeatherKind.Clear ? weatherName.Text : $"{weatherName.Text} {weatherSeconds.Text}"
        };

        public WeatherKind WeatherShown => weather;
        public bool WeatherIconVisible(WeatherKind k) => weatherIcons[(int)k].gameObject.activeSelf;

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
            float k = 1f - Mathf.Exp(-6f * Mathf.Max(dt, 0f));

            // Health: segments fill or drain radially toward the health, at about a bar every third of a second.
            float target = health;
            float next = healthShown < 0f ? target : Mathf.MoveTowards(healthShown, target, dt * 3f);
            if (!Mathf.Approximately(next, healthShown))
            {
                healthShown = next;
                mb.Clear();
                for (int s = 0; s < 3; s++)
                {
                    float f = Mathf.Clamp01(healthShown - s);
                    if (f > 0.001f) HealthSegment(mb, s, f, HealthR0 + 0.04f, HealthR1 - 0.04f, Color.white);
                }
                mb.ToMesh(healthFill.name, healthFill);
            }
            Color hc = health >= 3 ? HealthGreen : health == 2 ? HealthAmber : HealthRed;
            float pulse = health <= 1 ? 0.9f + 0.7f * Mathf.Abs(Mathf.Sin(time * 6f)) : 1.25f;
            hc *= pulse; hc.a = 0.95f;
            mpb.SetColor(TintId, hc);
            healthFillRenderer.SetPropertyBlock(mpb);
            healthValue.Color = health <= 1 ? Color.Lerp(HealthRed, Color.white, 0.3f * Mathf.Abs(Mathf.Sin(time * 6f))) : Palette.UIIvory;

            // Weather: the ring runs down with the time left, and the dial flashes when the weather changes.
            float wnext = weatherShown < 0f ? weatherTarget : Mathf.Lerp(weatherShown, weatherTarget, k);
            if (Mathf.Abs(wnext - weatherTarget) < 0.002f) wnext = weatherTarget;
            if (Mathf.Abs(wnext - weatherShown) > 0.0005f || weatherShown < 0f)
            {
                weatherShown = wnext;
                mb.Clear();
                if (weatherShown > 0.001f) mb.Arc(V(WeatherAt), WeatherR0 - 0.04f, WeatherR1 + 0.06f, weatherShown, 64, Color.white);
                mb.ToMesh(weatherFill.name, weatherFill);
            }
            if (weatherFlash > 0f) weatherFlash -= dt;
            Color wc = WeatherColor(weather) * (1.3f + 1.4f * Mathf.Clamp01(weatherFlash / 0.6f));
            wc.a = 0.95f;
            mpb.SetColor(TintId, wc);
            weatherFillRenderer.SetPropertyBlock(mpb);
            for (int i = 0; i < weatherIcons.Length; i++)
            {
                bool show = i == (int)weather;
                if (weatherIcons[i].gameObject.activeSelf != show) weatherIcons[i].gameObject.SetActive(show);
            }
            Color ic = weather == WeatherKind.Clear ? new Color(0.55f, 0.6f, 0.7f, 0.6f) : new Color(1.2f, 1.2f, 1.2f, 1f);
            if (weather == WeatherKind.Storm) ic *= 1f + 0.5f * Mathf.Max(0f, Mathf.Sin(time * 3.1f) * Mathf.Sin(time * 7.7f));
            mpb.SetColor(TintId, ic);
            weatherIcons[(int)weather].SetPropertyBlock(mpb);
            weatherName.Color = weather == WeatherKind.Clear ? new Color(0.7f, 0.72f, 0.75f) : WeatherColor(weather);

            distanceValue.Color = newBest ? CoinGold : Palette.UIIvory;
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

        public static Color WeatherColor(WeatherKind k) => k switch
        {
            WeatherKind.Fog => new Color(0.78f, 0.82f, 0.86f, 1f),
            WeatherKind.Storm => new Color(0.5f, 0.72f, 1f, 1f),
            WeatherKind.Snow => Palette.IceBlue,
            _ => new Color(0.5f, 0.55f, 0.6f, 1f)
        };

        static Vector3 V(Vector2 p) => new Vector3(p.x, p.y, -0.2f);

        /// <summary>
        /// One of the health ring's three segments, filled clockwise from its start by <paramref name="fill"/>.
        /// Segment 0 starts at 12 o'clock; each spans a third of the ring less a small gap.
        /// </summary>
        static void HealthSegment(MeshBuilder mb, int segment, float fill, float r0, float r1, Color col)
        {
            float start = 90f - segment * 120f - HealthGapDeg * 0.5f;
            float span = 120f - HealthGapDeg;
            float end = start - span * Mathf.Clamp01(fill);
            int n = Mathf.Max(1, Mathf.CeilToInt(span * fill / 4f));
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.Lerp(start, end, i / (float)n) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(start, end, (i + 1) / (float)n) * Mathf.Deg2Rad;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                mb.Quad(V(HealthAt + d0 * r0), V(HealthAt + d1 * r0), V(HealthAt + d1 * r1), V(HealthAt + d0 * r1), col);
            }
        }

        /// <summary>Flat glowing weather icons in the panel's XY plane, about a unit across.</summary>
        static void BuildWeatherIcon(MeshBuilder mb, WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Fog:
                {
                    Color c = new Color(0.85f, 0.88f, 0.92f, 0.95f);
                    float[] ys = { 0.42f, 0.05f, -0.32f };
                    float[] xs = { -0.12f, 0.12f, -0.05f };
                    float[] ws = { 1.15f, 1.3f, 1.0f };
                    for (int i = 0; i < 3; i++) Bar(mb, new Vector2(xs[i], ys[i]), ws[i], 0.17f, c);
                    break;
                }
                case WeatherKind.Storm:
                {
                    Color cloud = new Color(0.72f, 0.8f, 0.95f, 0.95f);
                    Disc(mb, new Vector2(-0.32f, 0.3f), 0.3f, cloud);
                    Disc(mb, new Vector2(0.05f, 0.45f), 0.4f, cloud);
                    Disc(mb, new Vector2(0.4f, 0.28f), 0.28f, cloud);
                    Bar(mb, new Vector2(0.03f, 0.13f), 1.1f, 0.24f, cloud);
                    Color bolt = new Color(1f, 0.88f, 0.3f, 1f);
                    Vector3 a = new Vector3(0.12f, 0.02f), b = new Vector3(-0.2f, -0.42f), c = new Vector3(0.0f, -0.42f), d = new Vector3(-0.16f, -0.85f), e = new Vector3(0.28f, -0.28f), f = new Vector3(0.06f, -0.28f), g = new Vector3(0.3f, 0.02f);
                    mb.Tri(a, b, c, bolt); mb.Tri(a, c, f, bolt); mb.Tri(a, f, g, bolt);
                    mb.Tri(c, d, e, bolt); mb.Tri(c, e, f, bolt);
                    break;
                }
                case WeatherKind.Snow:
                {
                    Color c = new Color(0.8f, 0.92f, 1f, 1f);
                    for (int i = 0; i < 3; i++)
                    {
                        Quaternion r = Quaternion.Euler(0f, 0f, i * 60f);
                        mb.Box(Vector3.zero, new Vector3(0.11f, 1.3f, 0.01f), r, c);
                        foreach (float end in new[] { 0.42f, -0.42f })
                        {
                            Vector3 at = r * new Vector3(0f, end, 0f);
                            mb.Box(at + r * new Vector3(0.1f, Mathf.Sign(end) * 0.08f, 0f), new Vector3(0.07f, 0.3f, 0.01f), r * Quaternion.Euler(0f, 0f, -Mathf.Sign(end) * 45f), c);
                            mb.Box(at + r * new Vector3(-0.1f, Mathf.Sign(end) * 0.08f, 0f), new Vector3(0.07f, 0.3f, 0.01f), r * Quaternion.Euler(0f, 0f, Mathf.Sign(end) * 45f), c);
                        }
                    }
                    break;
                }
                default:
                {
                    // Clear night: a crescent moon and a couple of stars.
                    Color moon = new Color(0.9f, 0.88f, 0.75f, 0.9f);
                    int n = 20;
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = Mathf.Lerp(110f, 330f, i / (float)n) * Mathf.Deg2Rad, a1 = Mathf.Lerp(110f, 330f, (i + 1) / (float)n) * Mathf.Deg2Rad;
                        float t0 = Mathf.Sin(i / (float)n * Mathf.PI), t1 = Mathf.Sin((i + 1) / (float)n * Mathf.PI);
                        var o0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.5f; var o1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.5f;
                        var i0 = o0 * (1f - 0.5f * t0); var i1 = o1 * (1f - 0.5f * t1);
                        mb.Quad(i0, i1, o1, o0, moon);
                    }
                    Disc(mb, new Vector2(0.45f, 0.4f), 0.07f, moon);
                    Disc(mb, new Vector2(0.25f, -0.05f), 0.05f, moon);
                    break;
                }
            }
        }

        static void Bar(MeshBuilder mb, Vector2 c, float w, float h, Color col)
        {
            mb.Quad(new Vector3(c.x - w * 0.5f, c.y - h * 0.5f), new Vector3(c.x - w * 0.5f, c.y + h * 0.5f), new Vector3(c.x + w * 0.5f, c.y + h * 0.5f), new Vector3(c.x + w * 0.5f, c.y - h * 0.5f), col);
            Disc(mb, new Vector2(c.x - w * 0.5f, c.y), h * 0.5f, col);
            Disc(mb, new Vector2(c.x + w * 0.5f, c.y), h * 0.5f, col);
        }

        static void Disc(MeshBuilder mb, Vector2 c, float r, Color col)
        {
            int n = 14;
            var center = new Vector3(c.x, c.y);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                mb.Tri(center, center + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1)) * r, center + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0)) * r, col);
            }
        }
    }
}
