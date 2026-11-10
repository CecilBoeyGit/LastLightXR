using System.IO;
using System.Linq;
using System.Text;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastLight.EditorTools
{
    /// <summary>
    /// Builds the Last Light prototype scene from the PICO XR template scene, and runs a headless
    /// autopilot playthrough that writes preview images and an event log.
    /// Batch mode: -executeMethod LastLight.EditorTools.PrototypeBuilder.BuildBatch
    ///             -executeMethod LastLight.EditorTools.PrototypeBuilder.PreviewBatch
    /// </summary>
    public static class PrototypeBuilder
    {
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string ScenePath = "Assets/LastLight/Scenes/LastLight_Prototype.unity";
        const string MatDir = "Assets/LastLight/Materials";
        const string UIDir = "Assets/LastLight/Resources/LastLightUI";

        [MenuItem("Last Light/Build Prototype Scene")]
        public static void Build()
        {
            EnablePicoFeatures();
            var terrain = MakeMaterial("LL_Terrain", "LastLight/Diorama", m => m.SetFloat("_ClipToDisk", 1f));
            var island = MakeMaterial("LL_IslandBase", "LastLight/Diorama", m => m.SetFloat("_ClipToDisk", 0f));
            var glow = MakeMaterial("LL_Glow", "LastLight/Glow", m => m.SetColor("_Tint", Color.white));
            // Loaded at runtime from Resources, which also keeps the sprite shader in builds.
            MakeMaterial("LL_UISprite", "LastLight/Sprite", m => m.SetColor("_Color", Color.white), UIDir);
            ConfigureUITextures();

            var scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);

            var pxr = Object.FindFirstObjectByType<PXR_Manager>();
            if (pxr != null)
            {
                var so = new SerializedObject(pxr);
                so.FindProperty("eyeTracking").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else Debug.LogWarning("[LastLight] No PXR_Manager in the template scene; eye tracking flag not set.");

            Camera cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.CompareTag("MainCamera"));
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Palette.Background;
                cam.nearClipPlane = 0.02f;
                cam.allowHDR = false; // HDR would drop the alpha channel passthrough relies on.
                if (cam.transform.parent == null) throw new System.Exception("Main Camera has no Camera Offset parent for the hands.");
                MountHands(cam.transform.parent);
            }
            else throw new System.Exception("No MainCamera in the template scene.");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { l.intensity = 0.3f; l.color = new Color(0.6f, 0.68f, 0.85f); }
            }
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.07f, 0.07f);

            var root = new GameObject("Last Light");
            var dioramaGo = new GameObject("Diorama");
            dioramaGo.transform.SetParent(root.transform, false);
            dioramaGo.transform.localPosition = new Vector3(0f, 0.92f, 0.6f);
            dioramaGo.transform.localScale = Vector3.one * 0.04f;
            var diorama = dioramaGo.AddComponent<Diorama>();

            var director = root.AddComponent<GameDirector>();
            director.diorama = diorama;
            director.head = cam != null ? cam.transform : null;
            director.terrainMaterial = terrain;
            director.baseMaterial = island;
            director.glowMaterial = glow;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[LastLight] Prototype scene built at " + ScenePath);
        }

        static void EnablePicoFeatures()
        {
            var cfg = PXR_ProjectSetting.GetProjectConfig();
            if (cfg == null) { Debug.LogWarning("[LastLight] PXR_ProjectSetting not found."); return; }
            cfg.handTracking = true;
            cfg.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            cfg.eyeTracking = true;
            cfg.videoSeeThrough = true;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log("[LastLight] PICO hand tracking, eye tracking and passthrough enabled in PXR_ProjectSetting.");
        }

        /// <summary>
        /// Mounts the PICO SDK's skinned hand models under Camera Offset, where their joint data is
        /// expressed. The prefabs' pointing ray is switched off: it would cross the disk during play,
        /// and this game reads pinches directly rather than through ray interaction.
        /// </summary>
        static void MountHands(Transform cameraOffset)
        {
            foreach (string prefabName in new[] { "HandLeft", "HandRight" })
            {
                string path = $"Packages/com.bytedance.pico.xr/Resources/Prefabs/{prefabName}.prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new System.Exception("PICO hand prefab not found: " + path);
                var hand = (GameObject)PrefabUtility.InstantiatePrefab(asset, cameraOffset);
                hand.transform.localPosition = Vector3.zero;
                hand.transform.localRotation = Quaternion.identity;
                hand.transform.localScale = Vector3.one;
                var ray = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DefaultRay");
                if (ray != null) ray.gameObject.SetActive(false);
                else Debug.LogWarning($"[LastLight] {prefabName} has no DefaultRay child to hide.");
            }
        }

        /// <summary>
        /// Menu and tutorial art: trilinear with mipmaps so text stays clean at panel distance, clamped edges,
        /// high-quality compression, and no rescaling of the non-power-of-two splash.
        /// </summary>
        static void ConfigureUITextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UIDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaIsTransparency = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 4;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        static Material MakeMaterial(string name, string shaderName, System.Action<Material> setup, string dir = MatDir)
        {
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- Batch entry points ----------

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void PreviewBatch()
        {
            try { Preview(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>
        /// Batch: -executeMethod LastLight.EditorTools.PrototypeBuilder.BuildAndroidBatch [-llArch arm64|x86_64|both] [-llRelease]
        /// Writes Builds/LastLight.apk. Development build by default so logcat shows script logs.
        /// </summary>
        public static void BuildAndroidBatch()
        {
            try
            {
                var args = System.Environment.GetCommandLineArgs();
                string arch = "arm64";
                bool release = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-llArch" && i + 1 < args.Length) arch = args[i + 1];
                    if (args[i] == "-llRelease") release = true;
                    if (args[i] == "-llVersion" && i + 1 < args.Length) PlayerSettings.bundleVersion = args[i + 1];
                    if (args[i] == "-llVersionCode" && i + 1 < args.Length && int.TryParse(args[i + 1], out int code))
                        PlayerSettings.Android.bundleVersionCode = code;
                }
                bool ok = BuildAndroid(arch, release);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Last Light/Build Android APK (arm64)")]
        public static void BuildAndroidMenu() => BuildAndroid("arm64", false);

        public static bool BuildAndroid(string arch, bool release)
        {
            if (!File.Exists(ScenePath)) Build();
            var target = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.productName = "Last Light";
            PlayerSettings.SetApplicationIdentifier(target, "com.lastlight.prototype");
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = arch switch
            {
                "x86_64" => AndroidArchitecture.X86_64,
                "both" => AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64,
                _ => AndroidArchitecture.ARM64
            };
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, release ? $"LastLight-{PlayerSettings.bundleVersion}.apk" : "LastLight.apk"),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = release ? BuildOptions.None : BuildOptions.Development
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[LastLight] Android build {s.result}: {s.outputPath} ({s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, arch {arch})");
            return s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        }

        [MenuItem("Last Light/Run Headless Preview")]
        public static void Preview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var director = Object.FindFirstObjectByType<GameDirector>();
            if (director == null) throw new System.Exception("GameDirector missing from scene");
            // Boot as a player would, into the start menu. The autopilot phases switch it on later.
            director.autopilot = false;
            director.showMenuOnBoot = true;
            director.tutorialEachRun = true;
            director.Boot();

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LastLightPreview"));
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("Preview Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 55f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Transform d = director.diorama.transform;

            float dt = 1f / 30f;
            var stats = new StringBuilder();
            var taken = new System.Collections.Generic.HashSet<string>();
            float minPowerupOffRoad = float.MaxValue;
            int fallFrames = 0;
            int powerupFramesOnDisk = 0, powerupFramesBehindJeep = 0, pickupFramesPastBack = 0;
            float topCruise = 0f;
            int coinFramesStaredAt = 0, earlyPowerups = 0;
            var cliffWidths = new System.Collections.Generic.List<float>();

            void Shot(string name)
            {
                if (!taken.Add(name)) return;
                // Player-height view, then a higher overview of the same moment.
                Render(cam, rt, d.position + d.rotation * new Vector3(0f, 0.62f, -0.8f), d.position + d.rotation * new Vector3(0f, 0f, 0.2f), Path.Combine(outDir, $"view_{name}.png"));
                Render(cam, rt, d.position + d.rotation * new Vector3(0.75f, 1.05f, -0.9f), d.position + d.rotation * new Vector3(0f, 0f, 0.35f), Path.Combine(outDir, $"overview_{name}.png"));
            }

            void Phase(string name, AutopilotMode mode, float seconds, System.Func<bool> stopWhen)
            {
                director.autopilotMode = mode;
                director.Restart();
                float t = 0f;
                float minGap = float.MaxValue;
                float maxDistance = 0f;
                int maxCoins = 0;
                while (t < seconds && (stopWhen == null || !stopWhen()))
                {
                    director.Tick(dt);
                    t += dt;
                    minGap = Mathf.Min(minGap, director.Gap);
                    maxDistance = Mathf.Max(maxDistance, director.Distance);
                    maxCoins = Mathf.Max(maxCoins, director.Coins);
                    topCruise = Mathf.Max(topCruise, director.CruiseSpeedNow);
                    earlyPowerups = Mathf.Max(earlyPowerups, director.Track.EarlyPowerups);
                    foreach (var cr in director.Track.cliffs)
                        if (!cliffWidths.Contains(cr.halfWidth)) cliffWidths.Add(cr.halfWidth);
                    if (mode == AutopilotMode.Normal)
                    {
                        if (t >= 3f) Shot("start");
                        if (t >= 30f) Shot("curves");
                        if (director.BoostTime > 2.5f) Shot("boost");
                        if (director.Car.shield) Shot("shield");
                        if (director.FlashlightTime > 2.5f) Shot("flashlight");
                        if (director.Car.branch != 0) Shot("split");
                        if (director.Track.OnCliffStretch(director.Distance + 2f)) Shot(cliffWidths.Count > 3 ? "cliff_narrow" : "cliff");
                    }
                    if (mode == AutopilotMode.NoSteering && director.Car.scraping) Shot("scraping");
                    if (mode == AutopilotMode.TakeDeadEnds && director.Car.trapped) Shot("trapped");
                    if (director.Car.falling && t > 0f) fallFrames++;
                    if (fallFrames == 10) Shot("falling");
                    if (mode == AutopilotMode.EscapeDeadEnds && director.Track.InEscapeRoute(director.Distance + director.Car.boostOffset, director.Car.branch)) Shot("escape");
                    if (director.Track.BranchesSeparated(director.Distance - 7f)) Shot("horde_in_split");

                    // Every powerup must sit clearly off the road, on either side or on the split's island.
                    foreach (var p in director.Track.pickups)
                    {
                        if (p == null || !p.alive) continue;
                        Vector3 pos = p.DioramaLocal;
                        if (pos.z < -Diorama.Radius - 2f) pickupFramesPastBack++;
                        if (p.kind == PickupKind.Coin)
                        {
                            if (p.dwell > 0f) coinFramesStaredAt++;
                            continue;
                        }
                        float s = director.Distance + pos.z - Diorama.CarZ;
                        minPowerupOffRoad = Mathf.Min(minPowerupOffRoad, director.Track.RoadDistance(director.RoadRelativeX(pos), s));
                        if (p.Visible && Diorama.InsideDisk(pos)) powerupFramesOnDisk++;
                        if (pos.z < director.Car.Z - 1f) powerupFramesBehindJeep++;
                    }
                }
                stats.AppendLine($"{name}: {t:0.0} s, furthest {maxDistance:0} m, coins {maxCoins}, lowest gap {minGap:0.00}, state {director.State}");
            }

            // ----- Start menu and tutorial, driven by a scripted gaze ray and simulated pinches -----
            Vector3 headWorld = d.TransformPoint(director.HeadLocal);
            bool bootedToMenu = director.State == RunState.Menu;
            float frozenDistance = 0f;
            void LookFor(Vector3 target, float seconds)
            {
                director.Scout.ScriptedWorldRay = new Ray(headWorld, target - headWorld);
                for (float s = 0f; s < seconds; s += dt)
                {
                    director.Tick(dt);
                    if (director.State == RunState.Menu || director.State == RunState.Tutorial)
                        frozenDistance = Mathf.Max(frozenDistance, director.Distance);
                }
            }
            void Press() { director.PressSelect(); director.Tick(dt); }
            Vector3 panelCenter = director.Menu.root.position;
            LookFor(panelCenter, 0.5f);
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_menu.png"));

            LookFor(director.Menu.ButtonWorldCenter(1), 0.4f);
            bool howFocused = director.Menu.Focused == 1;
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_menu_focus_howto.png"));
            Press();
            bool howOpened = director.State == RunState.Tutorial;
            LookFor(panelCenter, TutorialPanel.Duration + 0.2f);
            bool howReturned = director.State == RunState.Menu;

            LookFor(director.Menu.ButtonWorldCenter(0), 0.4f);
            bool startFocused = director.Menu.Focused == 0;
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_menu_focus_start.png"));
            Press();
            bool tutorialBeforeRun = director.State == RunState.Tutorial;
            LookFor(panelCenter, 2.0f);
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_tutorial_2s.png"));
            LookFor(panelCenter, 1.4f);
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_tutorial_3s.png"));
            int guard = 0;
            while (director.State == RunState.Tutorial && guard++ < 300) LookFor(panelCenter, dt);
            float tutorialSeconds = director.TutorialTime;
            bool runStartedAfterTutorial = director.State == RunState.Running;
            for (int i = 0; i < 30; i++) director.Tick(dt);
            bool worldMovesAfterTutorial = director.Distance > 0f;

            // A retry also shows the card, and a fresh pinch skips it once it has been up for a moment.
            director.Restart();
            bool retryShowsTutorial = director.State == RunState.Tutorial;
            LookFor(panelCenter, 0.3f);
            Press();
            bool earlyPinchIgnored = director.State == RunState.Tutorial;
            LookFor(panelCenter, 0.8f);
            Press();
            bool skipWorks = director.State == RunState.Running;
            director.Scout.ScriptedWorldRay = null;
            stats.AppendLine($"Menu and tutorial: booted to menu {bootedToMenu}, tutorial ran {tutorialSeconds:0.00} s, world moved during menu/tutorial {frozenDistance:0.00} m");

            director.tutorialEachRun = false;
            director.autopilot = true;

            Phase("Normal driving", AutopilotMode.Normal, 150f, null);
            int normalEnd = director.EventLog.Count;
            Phase("No steering", AutopilotMode.NoSteering, 45f, null);
            int noSteerEnd = director.EventLog.Count;
            Phase("Into dead ends", AutopilotMode.TakeDeadEnds, 240f, () => director.EventLog.Skip(noSteerEnd).Any(l => l.Contains("Run over: Trapped")));
            int trappedEnd = director.EventLog.Count;
            Phase("Escaping dead ends", AutopilotMode.EscapeDeadEnds, 240f, () => director.EventLog.Skip(trappedEnd).Any(l => l.Contains("Escaped the dead end")));

            int escapeEnd = director.EventLog.Count;
            Phase("Off the cliff edge", AutopilotMode.FallOffCliffs, 240f, () => director.EventLog.Skip(escapeEnd).Any(l => l.Contains("Run over: The jeep fell")) && fallFrames > 40);

            // Gaze sweep: a ray moves side to side and rises above the horizon, through the real ray handling.
            // The light must keep following it and stay lit, never freezing in place.
            director.autopilot = false;
            director.Restart();
            Vector3 eye = d.position + d.rotation * new Vector3(0f, 0.62f, -0.55f);
            Vector3 lastPatch = director.Scout.Patch;
            int frozenRun = 0, longestFrozen = 0, aboveHorizonFrames = 0;
            float minIntensity = 1f, sweepTime = 0f;
            while (sweepTime < 8f)
            {
                float yaw = Mathf.Sin(sweepTime * 1.3f) * 40f;
                float pitch = -12f + Mathf.Sin(sweepTime * 0.7f) * 24f; // negative is up in Euler X, so this crosses the horizon
                if (pitch < 0f) aboveHorizonFrames++;
                Vector3 dir = d.rotation * (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
                director.Scout.ScriptedWorldRay = new Ray(eye, dir);
                director.Tick(dt);
                sweepTime += dt;
                if (sweepTime < 0.6f) { lastPatch = director.Scout.Patch; continue; }
                minIntensity = Mathf.Min(minIntensity, director.Scout.Intensity);
                frozenRun = (director.Scout.Patch - lastPatch).sqrMagnitude < 1e-6f ? frozenRun + 1 : 0;
                longestFrozen = Mathf.Max(longestFrozen, frozenRun);
                lastPatch = director.Scout.Patch;
            }
            director.Scout.ScriptedWorldRay = null;
            stats.AppendLine($"Gaze sweep: {aboveHorizonFrames} frames looking above the horizon, longest freeze {longestFrozen} frames, lowest light {minIntensity:0.00}");

            var log = director.EventLog;
            System.Func<string, int> count = key => log.Count(l => l.Contains(key));

            var sb = new StringBuilder();
            sb.AppendLine("Last Light headless autopilot test");
            sb.AppendLine();
            sb.Append(stats);
            sb.AppendLine();
            sb.AppendLine("Feature checks:");
            void Check(string label, bool ok) => sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
            Check("Game boots to the start menu", bootedToMenu);
            Check("Looking at How to Play focuses it", howFocused);
            Check("Pinching How to Play opens the tutorial, which returns to the menu", howOpened && howReturned);
            Check("Looking at Start Run and pinching starts the tutorial", startFocused && tutorialBeforeRun);
            Check($"Tutorial lasts 5 s, then the run starts ({tutorialSeconds:0.00} s)", runStartedAfterTutorial && Mathf.Abs(tutorialSeconds - TutorialPanel.Duration) < 0.1f && worldMovesAfterTutorial);
            Check($"World stays frozen during the menu and tutorial ({frozenDistance:0.00} m moved)", frozenDistance == 0f);
            Check("A retry shows the tutorial again", retryShowsTutorial);
            Check("A fresh pinch skips the tutorial, but not in its first 0.8 s", earlyPinchIgnored && skipWorks);
            Check("Tutorial hand pinches and slides the jeep left, then right", TutorialPanel.PinchedAt(2f) && !TutorialPanel.PinchedAt(0.3f)
                && TutorialPanel.JeepOffsetAt(2f) < -50f && TutorialPanel.JeepOffsetAt(3.6f) > 50f && Mathf.Abs(TutorialPanel.JeepOffsetAt(4.9f)) < 1f);
            Check("Coins collected by driving through them", count("Coin collected") > 0);
            Check("Coins never respond to gaze", coinFramesStaredAt == 0);
            Check($"Few powerups early ({earlyPowerups} in the first 300 m)", earlyPowerups <= 3);
            Check("Brake plate removed", GameObject.Find("Brake Plate") == null);
            Check($"Cliff roads narrow as a run goes on ({string.Join(", ", cliffWidths.Select(w => (w * 2f).ToString("0.0")))} wide)", cliffWidths.Count >= 2 && cliffWidths[cliffWidths.Count - 1] < cliffWidths[0]);
            Check("A steered jeep crosses a cliff road safely", log.Take(normalEnd).Count(l => l.Contains("Crossed a cliff road")) > 0 && !log.Take(normalEnd).Any(l => l.Contains("fell into the abyss")));
            Check("Leaving a cliff road ends the run", count("Run over: The jeep fell into the abyss") > 0);
            Check("Booster collected by staring", count("Booster collected") > 0);
            Check("Shield collected by staring", count("Shield collected") > 0);
            Check("Flashlight collected by staring", count("Flashlight collected") > 0);
            Check("Split road taken", count("split:") > 0);
            Check("Clear branch of an obstacle split taken", count("clear branch") > 0);
            Check("Split road rejoined", count("Split road rejoined") > 0);
            Check("Curves push an unsteered jeep into the edge", log.Skip(normalEnd).Take(noSteerEnd - normalEnd).Any(l => l.Contains("Scraping")));
            Check("Dead-end branch taken", count("split: dead end") > 0);
            Check("Dead-end wall traps the jeep", count("Hit the dead-end wall: trapped") > 0);
            Check("Trapped run ends", count("Run over: Trapped") > 0);
            Check("Jeep at the wall can steer to the gap", count("Lined up with the escape gap") > 0);
            Check("Narrow route gets the jeep out of a dead end", count("Escaped the dead end") > 0);
            Check($"Every zombie stays on the road (worst {director.Crowd.WorstRoadDistance:0.00}, must be 0 or below)", director.Crowd.WorstRoadDistance <= 0f);
            Check($"Powerups sit off the road (closest {minPowerupOffRoad:0.00} units from the edge)", minPowerupOffRoad >= 2f);
            Check($"Powerups stay visible once on the disk ({powerupFramesOnDisk} frames)", powerupFramesOnDisk > 0);
            Check($"Uncollected powerups stay after passing the jeep ({powerupFramesBehindJeep} frames)", powerupFramesBehindJeep > 0);
            Check($"Pickups are removed once past the back of the disk ({pickupFramesPastBack} frames late)", pickupFramesPastBack == 0);
            Check($"Every stare takes {Pickup.StareSeconds:0.0} s", Mathf.Approximately(Pickup.StareSeconds, 1.5f));
            Check($"Speed rises with distance (top cruise reached {topCruise:0.0}, {count("Speed up")} steps)", count("Speed up") > 0 && topCruise > director.cruiseSpeed);
            Check($"Gaze light keeps following past range and above the horizon (longest freeze {longestFrozen} frames)", aboveHorizonFrames > 0 && longestFrozen <= 2 && minIntensity > 0.9f);
            Check($"Scout radius is {GazeScout.PatchRadius:0.0} units", GazeScout.PatchRadius > 5.5f);
            sb.AppendLine();
            sb.AppendLine($"Splits generated: {count("has a dead end")} with a dead end, {count("has an obstacle branch")} with an obstacle branch");
            sb.AppendLine($"Counts: coins {count("Coin collected")}, boosters {count("Booster collected")}, shields {count("Shield collected")}, flashlights {count("Flashlight collected")}, shield saves {count("Shield absorbed") + count("Shield stopped")}, scrapes {count("Scraping")}, sweeps {count("Swept boulder")}, damage events {count(", health ")}, splits {count("split:")}");
            sb.AppendLine();
            sb.AppendLine("Event log:");
            foreach (var line in log) sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outDir, "run.txt"), sb.ToString());
            PlayerPrefs.DeleteKey("LastLight.BestDistance");

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Debug.Log("[LastLight] Preview written to " + outDir);
        }

        static void Render(Camera cam, RenderTexture rt, Vector3 pos, Vector3 lookAt, string file)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
