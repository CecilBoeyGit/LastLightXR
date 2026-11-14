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
            // Keeps the HUD text shader in builds; the rim gauge labels use it to draw over the terrain.
            MakeMaterial("LL_UIText", "LastLight/Text", m => m.SetColor("_Color", Color.white), UIDir);
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
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                // Editor code compiled for another platform skips the PICO SDK's VR manifest step, and the APK
                // opens as a flat window. Switch first and build after the reload (batch: pass -buildTarget Android).
                Debug.LogWarning("[LastLight] Switching to Android inside the build: the PICO VR manifest entries may be missing. Build again after the scripts recompile.");
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
            director.persistProgress = false;   // never touch a player's coins or unlocks
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
            int maxDebris = 0, earlyShooters = 0, shootersSpawned = 0, earlyTurrets = 0, seenLog = 0, debrisCountdown = 0;
            int firstSightingSpitters = 0, lateWindowSpitters = 0;
            float guaranteedShieldS = -1f, firstSpitterS = float.MaxValue;
            bool gaugesSane = true;
            string debrisName = "";
            float minShooterOffRoad = float.MaxValue;

            void Shot(string name)
            {
                if (!taken.Add(name)) return;
                // Player-height view, then a higher overview of the same moment.
                Render(cam, rt, d.position + d.rotation * new Vector3(0f, 0.62f, -0.8f), d.position + d.rotation * new Vector3(0f, 0f, 0.2f), Path.Combine(outDir, $"view_{name}.png"));
                Render(cam, rt, d.position + d.rotation * new Vector3(0.75f, 1.05f, -0.9f), d.position + d.rotation * new Vector3(0f, 0f, 0.35f), Path.Combine(outDir, $"overview_{name}.png"));
            }

            // Bookkeeping and pictures for the shooters, acid, debris and gear, every simulated frame.
            void TrackNewFeatures()
            {
                maxDebris = Mathf.Max(maxDebris, director.Fx.ActiveCount);
                earlyShooters = Mathf.Max(earlyShooters, director.Track.EarlyShooters);
                shootersSpawned = Mathf.Max(shootersSpawned, director.Track.ShootersSpawned);
                earlyTurrets = Mathf.Max(earlyTurrets, director.Track.TurretsSpawnedEarly);
                // Spitter ramp: how many in the first 150 m of the medium stage, and in a later 150 m window.
                var spawns = director.Track.ShooterSpawnS;
                if (spawns.Count > 0) firstSpitterS = Mathf.Min(firstSpitterS, spawns[0]);
                firstSightingSpitters = Mathf.Max(firstSightingSpitters, spawns.Count(s => s >= TrackGenerator.MediumStageFrom && s < TrackGenerator.MediumStageFrom + 150f));
                lateWindowSpitters = Mathf.Max(lateWindowSpitters, spawns.Count(s => s >= 900f && s < 1050f));
                if (director.Track.GuaranteedShieldS >= 0f) guaranteedShieldS = director.Track.GuaranteedShieldS;
                if (director.State == RunState.Running)
                    foreach (Gauge g in System.Enum.GetValues(typeof(Gauge)))
                    {
                        float f = director.Gauges.Fill(g);
                        if (f < 0f || f > 1f || string.IsNullOrEmpty(director.Gauges.Value(g))) gaugesSane = false;
                    }
                foreach (var z in director.Track.shooters)
                {
                    if (z == null || !z.alive) continue;
                    Vector3 pos = z.DioramaLocal;
                    float s = director.Distance + pos.z - Diorama.CarZ;
                    minShooterOffRoad = Mathf.Min(minShooterOffRoad, director.Track.RoadDistance(director.RoadRelativeX(pos), s));
                    if (Diorama.InsideDisk(pos) && pos.z > director.Car.Z + 3f) Shot("shooter_on_disk");
                    if (z.Winding && !Diorama.InsideDisk(pos)) Shot("shooter_spitting");
                }
                if (director.Shooters.InFlight > 0) Shot("acid_in_flight");
                if (director.AcidTime > 0.5f) Shot("acid_hit");
                if (director.Fx.ActiveCount > 8 && director.State == RunState.Running) Shot("shatter");
                if (director.Turret.Active && director.Turret.Target != null && director.Turret.ShotsFired > 3) Shot("turret_firing");
                if (director.FrontShovel.Active) Shot("shovel");
                // A few frames after a smash, look at the pieces flying.
                for (; seenLog < director.EventLog.Count; seenLog++)
                {
                    string l = director.EventLog[seenLog];
                    if (l.Contains("Smashed through")) { debrisCountdown = 5; debrisName = "debris"; }
                    else if (l.Contains("Shovel smashed")) { debrisCountdown = 5; debrisName = "debris_shovel"; }
                    else if (l.Contains("Turret killed")) { debrisCountdown = 3; debrisName = "turret_kill"; }
                }
                if (debrisCountdown > 0 && --debrisCountdown == 0) Close(debrisName);
                if (director.Turret.Active && director.Turret.ShotsFired > 6) Close("turret");
                if (director.FrontShovel.Active && director.FrontShovel.HitsLeft < Shovel.MaxHits) Close("shovel");
                if (director.AcidTime > 0.5f) Close("acid");
                foreach (var p in director.Track.pickups)
                {
                    if (p == null || !p.alive || p.kind == PickupKind.Coin) continue;
                    Vector3 pos = p.DioramaLocal;
                    if (p.Visible && Diorama.InsideDisk(pos)) powerupFramesOnDisk++;
                    if (pos.z < director.Car.Z - 1f) powerupFramesBehindJeep++;
                }
            }

            // A close look at the jeep from just behind and above it, for gear and debris.
            void Close(string name)
            {
                if (!taken.Add("close_" + name)) return;
                Vector3 car = director.Car.Root.position;
                Render(cam, rt, car + d.rotation * new Vector3(0.12f, 0.16f, -0.2f), car + d.rotation * new Vector3(0f, 0.02f, 0.08f), Path.Combine(outDir, $"close_{name}.png"));
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
                        if (t >= 32f && taken.Add("gauges"))
                        {
                            // The rim gauges, seen from the player's head.
                            Vector3 gaugeCentre = d.TransformPoint(new Vector3(Mathf.Cos(208f * Mathf.Deg2Rad) * 18f, 0.5f, Mathf.Sin(208f * Mathf.Deg2Rad) * 18f));
                            Vector3 eyes = d.TransformPoint(director.HeadLocal);
                            Render(cam, rt, eyes, gaugeCentre, Path.Combine(outDir, "view_gauges.png"));
                            Render(cam, rt, eyes, Vector3.Lerp(gaugeCentre, d.position, 0.45f), Path.Combine(outDir, "view_gauges_wide.png"));
                        }
                        if (director.BoostTime > 2.5f) Shot("boost");
                        if (director.Car.shield) Shot("shield");
                        if (director.FlashlightTime > 2.5f) Shot("flashlight");
                        if (director.Car.branch != 0) Shot("split");
                        if (director.Track.OnCliffStretch(director.Distance + 2f)) Shot(cliffWidths.Count > 3 ? "cliff_narrow" : "cliff");
                    }
                    if (mode == AutopilotMode.NoSteering && director.Car.scraping) Shot("scraping");
                    TrackNewFeatures();
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

            LookFor(director.Menu.ButtonWorldCenter(StartMenu.HowToIndex), 0.4f);
            bool howFocused = director.Menu.Focused == StartMenu.HowToIndex;
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_menu_focus_howto.png"));
            Press();
            bool howOpened = director.State == RunState.Tutorial;
            LookFor(panelCenter, TutorialPanel.Duration + 0.2f);
            bool howReturned = director.State == RunState.Menu;

            // ----- Garage: open it, unlock and equip the pickup, paint it, come back -----
            LookFor(director.Menu.ButtonWorldCenter(StartMenu.GarageIndex), 0.4f);
            bool garageFocused = director.Menu.Focused == StartMenu.GarageIndex;
            Render(cam, rt, headWorld, panelCenter, Path.Combine(outDir, "view_menu_focus_garage.png"));
            Press();
            var garage = director.GarageUI;
            bool garageOpened = director.State == RunState.Garage && garage.Visible && !director.WorldRoot.gameObject.activeSelf;
            LookFor(garage.LookPoint, 0.6f);
            Render(cam, rt, headWorld, garage.LookPoint, Path.Combine(outDir, "view_garage.png"));
            Render(cam, rt, headWorld, garage.panelRoot.position, Path.Combine(outDir, "view_garage_panel.png"));
            // A locked vehicle can't be bought without coins.
            LookFor(garage.CardWorldCenter(1), 0.4f);
            Press();
            bool previewLocked = garage.Preview == 1 && !director.Save.IsUnlocked(1);
            LookFor(garage.ActionWorldCenter, 0.4f);
            Press();
            bool refusedBroke = !director.Save.IsUnlocked(1);
            director.Save.AddCoins(1500);
            LookFor(garage.ActionWorldCenter, 0.4f);
            Press();
            bool unlocked = director.Save.IsUnlocked(1) && director.Save.Coins == 1500 - VehicleCatalog.All[1].cost;
            LookFor(garage.ActionWorldCenter, 0.4f);
            Press();
            bool equipped = director.Save.Vehicle == 1;
            LookFor(garage.SwatchWorldCenter(3), 0.4f);
            Press();
            bool painted = director.Save.ColorOf(1) == 3;
            // Scroll down to the third vehicle.
            LookFor(garage.ScrollDownWorldCenter, 0.4f);
            Press();
            LookFor(garage.panelRoot.position, 0.6f);
            bool scrolled = garage.Scroll > 0.9f && garage.CardVisible(2) && !garage.CardVisible(0);
            Render(cam, rt, headWorld, garage.LookPoint, Path.Combine(outDir, "view_garage_unlocked.png"));
            Render(cam, rt, headWorld, garage.panelRoot.position, Path.Combine(outDir, "view_garage_panel_scrolled.png"));
            LookFor(garage.BackWorldCenter, 0.4f);
            Press();
            bool garageClosed = director.State == RunState.Menu && !garage.Visible && director.WorldRoot.gameObject.activeSelf;
            bool runUsesVehicle = director.Car.Model == 1 && director.Car.ModelColor == 3;
            stats.AppendLine($"Garage: coins left {director.Save.Coins}, driving {VehicleCatalog.All[director.Car.Model].name} colour {director.Car.ModelColor}");

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
            if (!director.EventLog.Skip(trappedEnd).Any(l => l.Contains("Escaped the dead end")))
            {
                // Shields are rare now, so the horde can win the crawl. Hold it off and test the escape route itself.
                director.Restart();
                director.Car.invulnerable = 1e6f;
                for (float t = 0f; t < 240f && !director.EventLog.Skip(trappedEnd).Any(l => l.Contains("Escaped the dead end")); t += dt)
                {
                    director.Tick(dt);
                    if (director.State == RunState.GameOver) { director.Restart(); director.Car.invulnerable = 1e6f; }
                }
                stats.AppendLine("Escaping dead ends: retried with the horde held off");
            }

            int escapeEnd = director.EventLog.Count;
            Phase("Off the cliff edge", AutopilotMode.FallOffCliffs, 240f, () => director.EventLog.Skip(escapeEnd).Any(l => l.Contains("Run over: The jeep fell")) && fallFrames > 40);

            // ----- Shooters, acid, turret and shovel, driven so they always get exercised -----
            void Run(float seconds, System.Func<bool> stopWhen = null)
            {
                for (float t = 0f; t < seconds && (stopWhen == null || !stopWhen()); t += dt)
                {
                    director.Tick(dt);
                    TrackNewFeatures();
                }
            }
            director.autopilotMode = AutopilotMode.Normal;
            director.Restart();
            director.Car.invulnerable = 1e6f;   // reach the medium stage without dying on the way
            Run(200f, () => director.Distance > TrackGenerator.MediumStageFrom + 30f);
            float reachedMedium = director.Distance;
            Run(40f, () => director.Track.shooters.Any(z => TurretGun.InRange(z, director.Car)));
            director.GrantPowerup(PickupKind.Turret);
            bool turretUp = director.Turret.Active;
            Run(TurretGun.Duration + 0.6f);
            bool turretExpired = !director.Turret.Active;
            int turretShots = director.Turret.ShotsFired, turretKills = director.Turret.Kills;

            // Acid: stop dodging and let the shooters hit the jeep.
            director.autopilotMode = AutopilotMode.NoSteering;
            director.Car.invulnerable = 0f;
            director.Car.shield = false;
            director.Car.health = 99;
            int acidStart = director.EventLog.Count;
            Run(60f, () => director.EventLog.Skip(acidStart).Any(l => l.Contains("Hit by acid")) || director.State != RunState.Running);
            bool acidSlowed = director.EventLog.Skip(acidStart).Any(l => l.Contains("Hit by acid")) && director.AcidTime > 0f;
            float acidSpeed = director.Car.speed, acidCruise = director.CruiseSpeedNow;
            Run(0.4f);

            // Shovel: drive straight so the blade meets the trees.
            director.autopilotMode = AutopilotMode.NoSteering;
            if (director.State != RunState.Running) director.Restart();
            director.Car.invulnerable = 1e6f;
            director.GrantPowerup(PickupKind.Shovel);
            int shovelStart = director.EventLog.Count;
            Run(90f, () => !director.FrontShovel.Active);
            var shovelLog = director.EventLog.Skip(shovelStart).ToList();
            int shovelSmashes = shovelLog.Count(l => l.Contains("Shovel smashed"));
            bool shovelBroke = shovelLog.Any(l => l.Contains("shovel broke"));
            int brokeAt = shovelLog.FindIndex(l => l.Contains("shovel broke"));
            bool shovelNoSlowdown = !shovelLog.Take(brokeAt < 0 ? shovelLog.Count : brokeAt).Any(l => l.Contains("Smashed through") || l.Contains("respawn grace"));

            // Keep driving until a flashlight turns up and gets stared at, since the fixed seed may not reach one earlier.
            director.autopilotMode = AutopilotMode.Normal;
            if (director.State != RunState.Running) director.Restart();
            director.Car.invulnerable = 1e6f;
            int flashStart = director.EventLog.Count;
            // Also drive on past 1 km, so the later spitter window gets generated.
            Run(200f, () => director.EventLog.Skip(flashStart).Any(l => l.Contains("Flashlight collected")) && director.Distance > 1060f);

            // Shield: for ten seconds rocks, trees and acid can't hurt or slow the jeep.
            // A fresh run, so driving straight meets trees before any cliff road.
            director.autopilotMode = AutopilotMode.NoSteering;
            director.Restart();
            director.Car.invulnerable = 0f;
            director.Car.health = 3;
            director.FrontShovel.Reset();
            int shieldStart = director.EventLog.Count, shieldHealth = director.Car.health;
            // Keep it topped up until it has met at least one obstacle.
            for (float t = 0f; t < 60f && director.State == RunState.Running; t += dt)
            {
                if (director.Car.shieldTime < 1f) director.GrantPowerup(PickupKind.Shield);
                director.Tick(dt);
                TrackNewFeatures();
                if (director.EventLog.Skip(shieldStart).Any(l => l.Contains("Shield smashed through"))) break;
            }
            Run(0.3f);
            var shieldLog = director.EventLog.Skip(shieldStart).ToList();
            int shieldSmashes = shieldLog.Count(l => l.Contains("Shield smashed through"));
            bool shieldNoHarm = !shieldLog.Any(l => l.Contains("Smashed through a") || l.Contains("Hit by acid")) && director.Car.health == shieldHealth;
            // Then let a fresh one run out.
            director.autopilotMode = AutopilotMode.Normal;
            if (director.State != RunState.Running) director.Restart();
            director.GrantPowerup(PickupKind.Shield);
            director.Car.invulnerable = 1e6f;
            Run(Vehicle.ShieldSeconds - 0.3f);
            bool shieldTimed = director.Car.shield;
            Run(0.6f);
            shieldTimed = shieldTimed && !director.Car.shield;
            director.Car.invulnerable = 1e6f;

            // Hands off: nobody looks at the powerups, so they ride onto the disk and past the jeep.
            director.autopilotMode = AutopilotMode.IgnorePowerups;
            if (director.State != RunState.Running) director.Restart();
            director.Car.invulnerable = 1e6f;
            Run(60f, () => powerupFramesBehindJeep > 20 || director.State != RunState.Running);
            director.Scout.ScriptedTarget = null;

            // ----- Run-over panel: Respawn (paid, doubling), lazy follow, Retry, Main Menu -----
            const float PanelReady = 1.6f;      // panel delay plus input delay, with a margin
            Transform headT = director.head;
            void FaceDisk() { headT.position = headWorld; headT.LookAt(d.position); }
            bool CrashOut(float seconds)
            {
                if (director.State != RunState.Running) director.Restart();
                director.Car.invulnerable = 0f;
                director.Car.shield = false;
                director.autopilot = true;
                director.autopilotMode = AutopilotMode.NoSteering;
                for (float t = 0f; t < seconds && director.State != RunState.GameOver; t += dt) { director.Tick(dt); TrackNewFeatures(); }
                director.autopilot = false;     // the autopilot would press Retry on its own
                return director.State == RunState.GameOver;
            }
            void Hold(float seconds) { for (float t = 0f; t < seconds; t += dt) director.Tick(dt); }
            void LookAtPanel(int button, float seconds)
            {
                director.Scout.ScriptedWorldRay = new Ray(headT.position, director.GameOverUI.ButtonWorldCenter(button) - headT.position);
                Hold(seconds);
            }
            FaceDisk();
            bool crashed1 = CrashOut(240f);
            director.Save.Spend(director.Save.Coins);
            Hold(0.4f);
            bool panelWaits = !director.GameOverPanelShown;
            Hold(0.7f);
            bool panelShown = director.GameOverPanelShown;
            Render(cam, rt, headT.position, director.GameOverUI.root.position, Path.Combine(outDir, "view_gameover_panel.png"));
            LookAtPanel(1, 0.6f);
            bool respawnFocused = director.GameOverUI.Focused == 1;
            Press();
            bool refusedWithoutCoins = director.State == RunState.GameOver && !director.GameOverUI.RespawnEnabled;
            director.Save.AddCoins(300);
            Hold(0.1f);
            bool enabledWithCoins = director.GameOverUI.RespawnEnabled;
            Render(cam, rt, headT.position, director.GameOverUI.root.position, Path.Combine(outDir, "view_gameover_focus_respawn.png"));
            Press();
            bool respawn1 = director.State == RunState.Running && director.Save.Coins == 300 - director.respawnBaseCost
                && director.RespawnsThisRun == 1 && director.NextRespawnCost == director.respawnBaseCost * 2 && !director.Car.falling;
            Shot("respawned");
            Hold(1f);
            bool survivesGrace = director.State == RunState.Running;
            director.Scout.ScriptedWorldRay = null;

            bool crashed2 = CrashOut(240f);
            int bankBefore2 = director.Save.Coins;
            Hold(PanelReady);
            LookAtPanel(1, 0.4f);
            Press();
            bool respawn2 = director.State == RunState.Running && director.Save.Coins == bankBefore2 - director.respawnBaseCost * 2
                && director.NextRespawnCost == director.respawnBaseCost * 4;
            director.Scout.ScriptedWorldRay = null;

            // Lazy follow: small head turns leave the panel alone, a big one brings it back in front.
            bool crashed3 = CrashOut(240f);
            Hold(PanelReady);
            Vector3 panelBefore = director.GameOverUI.root.position;
            headT.rotation = Quaternion.AngleAxis(9f, Vector3.up) * headT.rotation;
            for (float t = 0f; t < 1f; t += dt) director.GameOverUI.Follow(headT, dt, false);
            float smallTurnMove = Vector3.Distance(panelBefore, director.GameOverUI.root.position);
            headT.rotation = Quaternion.AngleAxis(50f, Vector3.up) * headT.rotation;
            // Compare headings only: the panel deliberately floats a little higher than a downward look.
            float YawOff()
            {
                Vector3 a = director.GameOverUI.root.position - headT.position, b = headT.forward;
                a.y = 0f; b.y = 0f;
                return Vector3.Angle(a, b);
            }
            float midway = 0f;
            for (float t = 0f; t < 2.5f; t += dt)
            {
                director.GameOverUI.Follow(headT, dt, false);
                if (Mathf.Abs(t - 0.3f) < dt * 0.5f) midway = YawOff();
            }
            float settledOff = YawOff();
            Render(cam, rt, headT.position, director.GameOverUI.root.position, Path.Combine(outDir, "view_gameover_followed.png"));
            LookAtPanel(0, 0.4f);
            Press();
            bool retryWorks = director.State == RunState.Running && director.RespawnsThisRun == 0 && director.Distance < 1f;
            director.Scout.ScriptedWorldRay = null;
            FaceDisk();
            bool crashed4 = CrashOut(240f);
            Hold(PanelReady);
            LookAtPanel(2, 0.4f);
            Press();
            bool menuWorks = director.State == RunState.Menu && !director.GameOverPanelShown;
            director.Scout.ScriptedWorldRay = null;
            stats.AppendLine($"New features: reached {reachedMedium:0} m for the turret test, turret {turretShots} rounds / {turretKills} kills, acid slowed to {acidSpeed * 10f:0} km/h, shovel {shovelSmashes} smashes, debris peak {maxDebris}, shooters spawned {shootersSpawned}");
            stats.AppendLine($"Lazy follow: small turn moved the panel {smallTurnMove * 100f:0.0} cm, {midway:0} deg off after 0.3 s, settled {settledOff:0.0} deg off");

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
            Check($"Few powerups early ({earlyPowerups} in the first 300 m)", earlyPowerups <= 4);
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
            // 0.1.8 features
            Check($"Rocks and trees are smashed through with a slowdown ({count("Smashed through")} hits)", count("Smashed through") > 0 && !log.Any(l => l.Contains("stalled")));
            Check($"Smashed obstacles break into pieces that fade away (peak {maxDebris} pieces, {director.Fx.ActiveCount} left at the end)", maxDebris > 0);
            Check($"Shooter zombies appear ({shootersSpawned}), never before {TrackGenerator.MediumStageFrom:0} m ({earlyShooters} early)", shootersSpawned > 0 && earlyShooters == 0);
            Check($"Shooters stand off the road (closest {minShooterOffRoad:0.00} units from the edge)", minShooterOffRoad >= 1.5f);
            Check($"Shooters spit acid at the jeep ({count("Shooter spat acid")} shots)", count("Shooter spat acid") > 0);
            Check($"Acid slows the jeep hard ({acidSpeed * 10f:0} km/h)", acidSlowed && acidSpeed < acidCruise * director.acidSpeedCap + 0.01f);
            Check($"Turret appears, fires and kills shooters for 10 s ({turretShots} rounds, {turretKills} kills)", turretUp && turretShots > 0 && turretKills > 0 && turretExpired);
            Check($"No turret powerups before the medium stage ({earlyTurrets})", earlyTurrets == 0);
            Check($"Shovel smashes obstacles without slowing ({shovelSmashes} smashes)", shovelSmashes > 0 && shovelNoSlowdown);
            Check("Shovel breaks after 5 hits", !shovelBroke || shovelSmashes == Shovel.MaxHits);
            Check("Garage button focuses and opens the garage in place of the island", garageFocused && garageOpened);
            Check("Locked vehicle can't be bought without coins", previewLocked && refusedBroke);
            Check("Unlocking spends coins, then the vehicle can be driven and painted", unlocked && equipped && painted);
            Check("Garage list scrolls to the third vehicle", scrolled);
            Check("Back returns to the menu and the run uses the chosen vehicle and colour", garageClosed && runUsesVehicle);
            Check("Run-over panel appears after the crash plays out", crashed1 && panelWaits && panelShown);
            Check("Respawn is refused without coins", respawnFocused && refusedWithoutCoins && enabledWithCoins);
            Check($"Respawn costs {director.respawnBaseCost} and continues the run", respawn1 && survivesGrace);
            Check("Each further respawn in a run costs twice as much", crashed2 && respawn2);
            Check($"Panel ignores small head turns ({smallTurnMove * 100f:0.0} cm) and glides back after big ones ({settledOff:0.0} deg)", crashed3 && smallTurnMove < 0.005f && midway > 8f && settledOff < 6f);
            Check("Retry starts a fresh run", retryWorks);
            Check("Main Menu returns to the title", crashed4 && menuWorks);
            // 0.1.9 changes
            Check($"First spitter sighting is small ({firstSightingSpitters} in {TrackGenerator.MediumStageFrom:0}-{TrackGenerator.MediumStageFrom + 150f:0} m)", firstSightingSpitters >= 1 && firstSightingSpitters <= 3);
            Check($"Spitters grow in number later ({lateWindowSpitters} in 900-1050 m)", lateWindowSpitters > firstSightingSpitters);
            Check($"A shield waits before the first spitters (shield at {guaranteedShieldS:0} m, first spitter at {firstSpitterS:0} m)", guaranteedShieldS > 0f && guaranteedShieldS < firstSpitterS);
            Check("Shield is rare and the shovel common early on", ShieldShare(GameStage.Early) <= 0.1f && ShieldShare(GameStage.Medium) <= 0.1f && ShieldShare(GameStage.Late) <= 0.1f
                && Share(GameStage.Early, PickupKind.Shovel) >= 0.25f && Share(GameStage.Medium, PickupKind.Shovel) >= 0.25f);
            Check($"Shield ploughs through rocks and trees with no harm ({shieldSmashes} smashes)", shieldSmashes > 0 && shieldNoHarm);
            Check("Shield wears off after 10 s", shieldTimed);
            Check("Rim gauges replace the text readout and stay in range", gaugesSane && GameObject.Find("HUD") == null && director.Gauges != null);
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

        /// <summary>Fraction of powerup rolls in a stage that give this kind.</summary>
        static float Share(GameStage stage, PickupKind kind)
        {
            int hits = 0;
            for (int i = 0; i < 1000; i++) if (TrackGenerator.PowerupKind((i + 0.5) / 1000.0, stage) == kind) hits++;
            return hits / 1000f;
        }

        static float ShieldShare(GameStage stage) => Share(stage, PickupKind.Shield);

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
