using UnityEngine;

namespace LastLight
{
    public enum WeatherKind { Clear, Fog, Storm, Snow }

    /// <summary>
    /// Weather encounters from about half a kilometre in, each lasting a random 10 to 15 seconds, with long
    /// gaps between them that shorten a little in the late stage. Never the same weather twice in a row.
    /// Fog: a thick bank rolls over the scouting region beyond the rim, so the scout light can't reveal
    /// anything; only a flashlight burns through it for a while.
    /// Storm: rain and lightning, every surface wet and glossy with puddles on the road, and broken branches
    /// blowing across it (GameDirector spawns those).
    /// Snow: falling snow settles white on everything and the road ices over: the jeep gets slippery, steering
    /// moves are amplified and it skids left and right at random (GameDirector applies the handling).
    /// Visuals and shader globals live here.
    /// </summary>
    public sealed class WeatherSystem
    {
        public const float MinSeconds = 10f, MaxSeconds = 15f;
        const float RampSeconds = 1.5f;
        /// <summary>Metres into a run before the first weather can roll in.</summary>
        public const float FirstFrom = 520f;
        const float FirstDelayMin = 4f, FirstDelayMax = 12f;       // seconds after passing FirstFrom
        const float MediumGapMin = 38f, MediumGapMax = 58f;
        const float LateGapMin = 28f, LateGapMax = 42f;
        const float WetUpRate = 1f / 3f, WetDryRate = 1f / 6f;
        const float SnowUpRate = 1f / 4f, SnowMeltRate = 1f / 10f;
        const int RainStreaks = 260, SnowFlakes = 320;
        const float RainHeight = 14f, RainSpeed = 26f, SnowSpeed = 3.4f;
        static readonly int WeatherId = Shader.PropertyToID("_LL_Weather");
        static readonly int TintId = Shader.PropertyToID("_Tint");

        public WeatherKind Kind { get; private set; }
        /// <summary>0 to 1, easing in and out at the ends of an encounter.</summary>
        public float Intensity { get; private set; }
        public float TimeLeft { get; private set; }
        public float Duration { get; private set; }
        /// <summary>How wet the surfaces are, 0 to 1. Rises in a storm and dries off slowly afterwards.</summary>
        public float Wetness { get; private set; }
        /// <summary>0 to 1 brightness of the current lightning flash.</summary>
        public float Flash { get; private set; }
        /// <summary>How much snow has settled, 0 to 1. Builds in a snowfall and melts slowly afterwards.</summary>
        public float SnowCover { get; private set; }
        public int Strikes { get; private set; }
        public bool Active => Kind != WeatherKind.Clear;
        public float FogAmount => Kind == WeatherKind.Fog ? Intensity : 0f;
        public float StormAmount => Kind == WeatherKind.Storm ? Intensity : 0f;
        public float SnowAmount => Kind == WeatherKind.Snow ? Intensity : 0f;
        public bool RainVisible => rainA.gameObject.activeSelf;
        public bool SnowVisible => snowA.gameObject.activeSelf;
        /// <summary>Seconds until the next scheduled encounter, or -1 before it's drawn. For tests.</summary>
        public float NextIn => nextIn;
        public bool FogVisible => fog.gameObject.activeSelf;
        /// <summary>True for an encounter started by Force (tests and debug keys) rather than the schedule.</summary>
        public bool Forced => forced;

        public System.Action<WeatherKind, float> OnStart;
        public System.Action<WeatherKind> OnEnd;
        public System.Action OnLightning;
        public System.Action OnThunder;

        readonly Transform root, fog, lightning;
        readonly MeshRenderer fogRenderer, rainA, rainB, snowA, snowB, boltRenderer;
        readonly Mesh boltMesh;
        readonly MeshBuilder mb = new MeshBuilder();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        System.Random rng;
        float nextIn = -1f;
        bool hadWeather;
        float strikeIn, thunderIn = -1f, flashTime;
        bool forced;
        WeatherKind last = WeatherKind.Clear;

        public WeatherSystem(Transform parent, Material glowMat, int seed)
        {
            root = new GameObject("Weather").transform;
            root.SetParent(parent, false);
            rng = new System.Random(seed);

            // Fog: soft banks of mist over the scouting region ahead, at a few heights, drifting slowly.
            var r = new System.Random(11);
            for (int i = 0; i < 26; i++)
            {
                float x = -20f + (float)r.NextDouble() * 40f;
                float z = Diorama.Radius - 3f + (float)r.NextDouble() * (Diorama.ScoutRange + 6f);
                float y = 0.4f + (float)r.NextDouble() * 3.2f;
                float rad = 5f + (float)r.NextDouble() * 6f;
                float a = 0.11f + (float)r.NextDouble() * 0.08f;
                mb.SoftDisc(new Vector3(x, y, z), rad, 28, new Color(0.62f, 0.68f, 0.74f, a), new Color(0.6f, 0.66f, 0.72f, 0f));
            }
            // A low wall of mist hugging the far rim, so the bank reads as starting at the edge of the disk.
            for (int i = 0; i < 9; i++)
            {
                float ang = Mathf.Lerp(25f, 155f, i / 8f) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (Diorama.Radius + 2.5f) + Vector3.up * 1.2f;
                mb.SoftDisc(c, 6.5f, 24, new Color(0.62f, 0.68f, 0.74f, 0.16f), new Color(0.6f, 0.66f, 0.72f, 0f));
            }
            fogRenderer = MeshBuilder.Spawn("Fog Bank", root, mb.ToMesh("LL_FogBank"), glowMat);
            fog = fogRenderer.transform;
            fog.gameObject.SetActive(false);

            // Rain: thin streaks over the disk, two stacked layers that scroll down and wrap.
            mb.Clear();
            var rr = new System.Random(23);
            for (int i = 0; i < RainStreaks; i++)
            {
                float ang = (float)rr.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)rr.NextDouble()) * (Diorama.Radius - 0.5f);
                var p = new Vector3(Mathf.Cos(ang) * rad, (float)rr.NextDouble() * RainHeight, Mathf.Sin(ang) * rad);
                float len = 0.7f + (float)rr.NextDouble() * 0.5f;
                Vector3 slant = new Vector3(0.18f, len, 0.05f);
                Vector3 side = new Vector3(0.025f, 0f, 0f);
                Color col = new Color(0.72f, 0.82f, 1f, 0.32f + (float)rr.NextDouble() * 0.2f);
                mb.Quad(p - side, p - side + slant, p + side + slant, p + side, col);
            }
            Mesh rain = mb.ToMesh("LL_Rain");
            rainA = MeshBuilder.Spawn("Rain A", root, rain, glowMat);
            rainB = MeshBuilder.Spawn("Rain B", root, rain, glowMat);
            rainA.gameObject.SetActive(false);
            rainB.gameObject.SetActive(false);

            // Snow: small soft flakes in two layers that drift down slowly and sway.
            mb.Clear();
            var sr = new System.Random(29);
            for (int i = 0; i < SnowFlakes; i++)
            {
                float ang = (float)sr.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)sr.NextDouble()) * (Diorama.Radius + 2f);
                var p = new Vector3(Mathf.Cos(ang) * rad, (float)sr.NextDouble() * RainHeight, Mathf.Sin(ang) * rad);
                // Keep the flakes ahead of the player: the head sits above the back of the disk, inside the volume.
                if (p.z < -7f) p.z = -7f + (float)sr.NextDouble() * (Diorama.Radius + 9f);
                float size = 0.045f + (float)sr.NextDouble() * 0.05f;
                Color col = new Color(0.92f, 0.96f, 1f, 0.5f + (float)sr.NextDouble() * 0.35f);
                mb.Quad(p + new Vector3(-size, -size, 0f), p + new Vector3(-size, size, 0f), p + new Vector3(size, size, 0f), p + new Vector3(size, -size, 0f), col);
                mb.Quad(p + new Vector3(0f, -size, -size), p + new Vector3(0f, size, -size), p + new Vector3(0f, size, size), p + new Vector3(0f, -size, size), col);
            }
            Mesh snow = mb.ToMesh("LL_Snow");
            snowA = MeshBuilder.Spawn("Snow A", root, snow, glowMat);
            snowB = MeshBuilder.Spawn("Snow B", root, snow, glowMat);
            snowA.gameObject.SetActive(false);
            snowB.gameObject.SetActive(false);

            boltMesh = new Mesh { name = "LL_Lightning" };
            boltRenderer = MeshBuilder.Spawn("Lightning", root, boltMesh, glowMat);
            lightning = boltRenderer.transform;
            lightning.gameObject.SetActive(false);
            Push();
        }

        /// <summary>Clears the weather for a new run, with the schedule seeded for it.</summary>
        public void Reset(int seed)
        {
            rng = new System.Random(seed);
            Kind = WeatherKind.Clear;
            Intensity = 0f;
            TimeLeft = Duration = 0f;
            Wetness = 0f;
            SnowCover = 0f;
            last = WeatherKind.Clear;
            Flash = 0f;
            Strikes = 0;
            nextIn = -1f;
            hadWeather = false;
            thunderIn = -1f;
            forced = false;
            fog.gameObject.SetActive(false);
            rainA.gameObject.SetActive(false);
            rainB.gameObject.SetActive(false);
            snowA.gameObject.SetActive(false);
            snowB.gameObject.SetActive(false);
            lightning.gameObject.SetActive(false);
            Push();
        }

        /// <summary>Starts an encounter straight away. For tests and debug keys.</summary>
        public void Force(WeatherKind kind, float seconds = -1f)
        {
            forced = true;
            if (kind == WeatherKind.Clear) { End(); return; }
            Begin(kind, seconds > 0f ? seconds : Range(MinSeconds, MaxSeconds));
        }

        /// <summary>Strikes lightning now. For tests.</summary>
        public void StrikeNow() { if (Kind == WeatherKind.Storm) Strike(); }

        /// <param name="running">The world only weathers while the run is going.</param>
        /// <param name="distance">Metres into the run: nothing rolls in before <see cref="FirstFrom"/>.</param>
        /// <param name="flashlight">0 to 1 flashlight reveal: it burns the fog away while it lasts.</param>
        public void Tick(float dt, bool running, float distance, GameStage stage, float time, float flashlight)
        {
            if (running)
            {
                if (Kind == WeatherKind.Clear && distance >= FirstFrom && !forced)
                {
                    // The first encounter comes soon after FirstFrom; later gaps are long, a little shorter in the late stage.
                    if (nextIn < 0f)
                        nextIn = !hadWeather ? Range(FirstDelayMin, FirstDelayMax)
                            : stage == GameStage.Late ? Range(LateGapMin, LateGapMax) : Range(MediumGapMin, MediumGapMax);
                    nextIn -= dt;
                    if (nextIn <= 0f) Begin(PickKind(), Range(MinSeconds, MaxSeconds));
                }
                else if (Kind != WeatherKind.Clear)
                {
                    TimeLeft -= dt;
                    float age = Duration - TimeLeft;
                    Intensity = Mathf.Clamp01(Mathf.Min(age / RampSeconds, TimeLeft / RampSeconds));
                    if (TimeLeft <= 0f) End();
                }

                SnowCover = Kind == WeatherKind.Snow ? Mathf.MoveTowards(SnowCover, 1f, dt * SnowUpRate)
                    : Mathf.MoveTowards(SnowCover, 0f, dt * SnowMeltRate);

                // Storm effects: wetness, lightning and thunder.
                if (Kind == WeatherKind.Storm)
                {
                    Wetness = Mathf.MoveTowards(Wetness, Intensity, dt * WetUpRate);
                    strikeIn -= dt;
                    if (strikeIn <= 0f && Intensity > 0.5f) Strike();
                }
                else Wetness = Mathf.MoveTowards(Wetness, 0f, dt * WetDryRate);
                if (thunderIn >= 0f && (thunderIn -= dt) < 0f) OnThunder?.Invoke();
            }

            // Lightning: a double flash, the second a little weaker.
            if (flashTime > 0f) flashTime -= dt;
            float f = 0f;
            if (flashTime > 0f)
            {
                float t = 0.5f - flashTime;
                f = t < 0.08f ? 1f : t < 0.16f ? 0.15f : t < 0.24f ? 0.7f : Mathf.Clamp01(1f - (t - 0.24f) / 0.26f) * 0.3f;
            }
            Flash = f;
            bool boltOn = flashTime > 0.26f;
            if (lightning.gameObject.activeSelf != boltOn) lightning.gameObject.SetActive(boltOn);

            // Visuals.
            float fogShown = FogAmount * (1f - Mathf.Clamp01(flashlight));
            bool fogOn = fogShown > 0.01f;
            if (fog.gameObject.activeSelf != fogOn) fog.gameObject.SetActive(fogOn);
            if (fogOn)
            {
                fog.localPosition = new Vector3(Mathf.Sin(time * 0.13f) * 2.2f, 0f, Mathf.Sin(time * 0.09f) * 1.2f);
                mpb.SetColor(TintId, new Color(1f, 1f, 1f, fogShown));
                fogRenderer.SetPropertyBlock(mpb);
            }
            float rain = StormAmount;
            bool rainOn = rain > 0.01f;
            if (rainA.gameObject.activeSelf != rainOn) { rainA.gameObject.SetActive(rainOn); rainB.gameObject.SetActive(rainOn); }
            if (rainOn)
            {
                float fall = Mathf.Repeat(time * RainSpeed, RainHeight);
                rainA.transform.localPosition = new Vector3(0f, -fall, 0f);
                rainB.transform.localPosition = new Vector3(0f, RainHeight - fall, 0f);
                mpb.SetColor(TintId, new Color(1f + Flash, 1f + Flash, 1f + Flash, rain));
                rainA.SetPropertyBlock(mpb);
                rainB.SetPropertyBlock(mpb);
            }
            float snow = SnowAmount;
            bool snowOn = snow > 0.01f;
            if (snowA.gameObject.activeSelf != snowOn) { snowA.gameObject.SetActive(snowOn); snowB.gameObject.SetActive(snowOn); }
            if (snowOn)
            {
                float fall = Mathf.Repeat(time * SnowSpeed, RainHeight);
                float sway = Mathf.Sin(time * 0.8f) * 0.9f;
                snowA.transform.localPosition = new Vector3(sway, -fall, 0f);
                snowB.transform.localPosition = new Vector3(-sway * 0.7f, RainHeight - fall, 0.6f);
                mpb.SetColor(TintId, new Color(1f, 1f, 1f, snow));
                snowA.SetPropertyBlock(mpb);
                snowB.SetPropertyBlock(mpb);
            }
            Push();
        }

        /// <summary>Picks the next weather at random, never the same as the last one.</summary>
        WeatherKind PickKind()
        {
            var options = new System.Collections.Generic.List<WeatherKind> { WeatherKind.Fog, WeatherKind.Storm, WeatherKind.Snow };
            options.Remove(last);
            return options[rng.Next(options.Count)];
        }

        void Begin(WeatherKind kind, float seconds)
        {
            if (Kind != WeatherKind.Clear) OnEnd?.Invoke(Kind);
            Kind = kind;
            last = kind;
            Duration = TimeLeft = seconds;
            Intensity = 0f;
            strikeIn = Range(1.5f, 3f);
            OnStart?.Invoke(kind, seconds);
        }

        void End()
        {
            if (Kind == WeatherKind.Clear) return;
            var was = Kind;
            Kind = WeatherKind.Clear;
            Intensity = 0f;
            TimeLeft = 0f;
            nextIn = -1f;           // the gap to the next encounter is drawn, by stage, on the next tick
            hadWeather = true;
            forced = false;
            OnEnd?.Invoke(was);
        }

        void Strike()
        {
            Strikes++;
            flashTime = 0.5f;
            strikeIn = Range(2.5f, 5f);
            thunderIn = Range(0.25f, 0.7f);
            BuildBolt();
            OnLightning?.Invoke();
        }

        /// <summary>A jagged bolt from the sky down to the forest beyond the far rim, with a branch.</summary>
        void BuildBolt()
        {
            mb.Clear();
            float x = Range(-16f, 16f), z = Range(Diorama.Radius + 10f, Diorama.Radius + 26f);
            var p = new Vector3(x, 42f, z);
            Vector3 prev = p;
            Vector3 branchFrom = Vector3.zero;
            int steps = 14;
            for (int i = 1; i <= steps; i++)
            {
                float y = Mathf.Lerp(42f, 0f, i / (float)steps);
                var next = new Vector3(prev.x + Range(-2.4f, 2.4f), y, prev.z + Range(-0.8f, 0.8f));
                Segment(prev, next, 0.32f, new Color(0.85f, 0.9f, 1f, 1f));
                Segment(prev, next, 1.1f, new Color(0.5f, 0.6f, 1f, 0.25f));
                if (i == 5) branchFrom = next;
                prev = next;
            }
            prev = branchFrom;
            for (int i = 1; i <= 5; i++)
            {
                var next = prev + new Vector3(Range(1f, 2.6f), -Range(2f, 3.5f), Range(-0.5f, 0.5f));
                Segment(prev, next, 0.18f, new Color(0.8f, 0.86f, 1f, 0.8f));
                prev = next;
            }
            mb.ToMesh(boltMesh.name, boltMesh);
        }

        void Segment(Vector3 a, Vector3 b, float width, Color col)
        {
            Vector3 side = Vector3.Cross((b - a).normalized, Vector3.forward).normalized * width * 0.5f;
            mb.Quad(a - side, b - side, b + side, a + side, col);
        }

        void Push() => Shader.SetGlobalVector(WeatherId, new Vector4(FogAmount, Wetness, Flash, SnowCover));

        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}
