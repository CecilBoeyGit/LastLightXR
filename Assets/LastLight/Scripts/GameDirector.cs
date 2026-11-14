using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace LastLight
{
    public enum RunState { Running, Paused, GameOver, Menu, Tutorial, Garage }

    /// <summary>How the test autopilot drives. Normal plays well; the others deliberately exercise failure paths.</summary>
    public enum AutopilotMode { Normal, NoSteering, TakeDeadEnds, EscapeDeadEnds, FallOffCliffs, IgnorePowerups }

    /// <summary>
    /// Last Light first playable, Direction 02 (eye gaze + hand tracking).
    /// Eyes scout past the rim and collect powerups by staring at them. Either hand steers the jeep
    /// across an open, curving road, through coins and past cliff edges, and sweeps rocks aside.
    /// The horde follows from behind.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        const float PickupGazeRadius = 2.6f;    // game units around the icon that count as looking at it
        const float BoostSeconds = 5f;
        const float BoostSpeedMultiplier = 1.4f;
        const float BoostGapBonus = 3f;
        const float FlashlightSeconds = 5f;
        const float ScrapeSpeedMultiplier = 0.6f;
        const float ShieldKnockback = 6f;
        const float EscapeCrawlFraction = 0.8f;  // of cruise speed: slower than the horde, so the track costs lead
        const float CliffDriftFraction = 0.6f;  // curves push less on a cliff road, where a mistake ends the run
        const float CoinReach = 0.7f;           // game units beyond the jeep's body that still collect a coin
        const float GameOverPanelDelay = 0.9f;  // let a crash or fall play before the panel appears
        const float GameOverInputDelay = 0.6f;  // and ignore pinches for a moment after it does
        const float RespawnGrace = 2.5f;        // seconds of blinking immunity after a respawn
        const float RespawnClearRange = 16f;    // obstacles this far ahead are cleared for a respawn

        [Header("Scene references")]
        public Diorama diorama;
        public Transform head;
        public Material terrainMaterial;
        public Material baseMaterial;
        public Material glowMaterial;

        [Header("Run")]
        public int seed = 1;
        [Tooltip("Keep the same seed every run, for matched playtests.")]
        public bool fixedSeed = true;
        [Tooltip("Drives steering, sweeps and gaze automatically. For testing only.")]
        public bool autopilot;
        public AutopilotMode autopilotMode = AutopilotMode.Normal;

        [Header("Tuning (game units per second)")]
        public float cruiseSpeed = 6f;
        [Tooltip("Cruise speed gained at each distance step.")]
        public float speedStep = 0.5f;
        [Tooltip("Metres of travel between speed steps.")]
        public float speedStepDistance = 100f;
        public float maxCruiseSpeed = 12f;
        public float hordeRatio = 0.94f;
        public float startGap = 8f;
        public float maxGap = 12f;
        public int startHealth = 3;
        [Tooltip("How strongly curves push the jeep toward the outside edge.")]
        public float curveDrift = 0.35f;

        [Header("Obstacles, acid and powerups")]
        [Tooltip("Fraction of its speed the jeep keeps after smashing through a rock or fallen tree.")]
        [Range(0f, 1f)] public float obstacleSpeedKeep = 0.3f;
        [Tooltip("Rocks and fallen trees also cost a point of health.")]
        public bool obstaclesDamage = true;
        [Tooltip("Fraction of its speed the jeep keeps the moment acid hits it.")]
        [Range(0f, 1f)] public float acidSpeedKeep = 0.5f;
        [Tooltip("While slowed by acid, the jeep's top speed is this fraction of cruise.")]
        [Range(0f, 1f)] public float acidSpeedCap = 0.6f;
        [Tooltip("Seconds the acid slow lasts.")]
        public float acidSeconds = 0.75f;

        [Header("Coins")]
        [Tooltip("Cost of the first respawn in a run. Each further respawn in the same run doubles it.")]
        public int respawnBaseCost = 25;
        [Tooltip("Save the coin bank, unlocks and garage choices between sessions. Tests turn this off.")]
        public bool persistProgress = true;

        [Header("Comfort")]
        public bool recenterOnStart = true;
        public Vector3 diskOffsetFromHead = new Vector3(0f, -0.62f, 0.55f);
        public float trackingLossPause = 1.2f;

        [Header("Mixed reality")]
        [Tooltip("Show the camera passthrough behind the disk on the headset.")]
        public bool passthrough = true;
        [Range(0f, 1f)]
        [Tooltip("How strongly the passthrough is tinted black. 0 is the untouched room, 1 is fully black.")]
        public float passthroughTint = 0.3f;

        [Header("Menu and tutorial")]
        [Tooltip("Show the title menu when the game starts.")]
        public bool showMenuOnBoot = true;
        [Tooltip("Play the 5-second how-to-play card before every run.")]
        public bool tutorialEachRun = true;
        [Tooltip("Where the menu and tutorial panel floats, in disk units: above and behind the far rim.")]
        public Vector3 uiPanelPosition = new Vector3(0f, 13f, 16f);

        public RunState State { get; private set; }
        public float Distance { get; private set; }
        public float Gap { get; private set; }
        public float Best { get; private set; }
        public int Coins { get; private set; }
        public float BoostTime => boostTimer;
        public float FlashlightTime => flashTimer;
        public Vehicle Car => car;
        public GazeScout Scout => scout;
        public TrackGenerator Track => track;
        public Interventions Hands => interventions;
        public Horde Crowd => horde;
        public StartMenu Menu => menu;
        public TutorialPanel Tutorial => tutorial;
        public GameOverPanel GameOverUI => gameOverPanel;
        public Garage GarageUI => garage;
        public Progress Save => progress;
        public ShooterSystem Shooters => shooterSystem;
        public TurretGun Turret => turret;
        public Shovel FrontShovel => shovel;
        public Effects Fx => fx;
        public RimGauges Gauges => gauges;
        public float AcidTime => car.acid;
        public int RespawnsThisRun => respawnsThisRun;
        public int NextRespawnCost => respawnBaseCost * (1 << Mathf.Min(respawnsThisRun, 20));
        public bool GameOverPanelShown => gameOverPanel.Visible;
        /// <summary>Everything that belongs to the forest world, hidden while the garage is open.</summary>
        public Transform WorldRoot => worldRoot;
        public float TutorialTime => tutorialTime;
        /// <summary>The player's head in disk units, where the disk is placed relative to it.</summary>
        public Vector3 HeadLocal => -diskOffsetFromHead / Mathf.Max(diorama.Scale, 1e-5f);
        /// <summary>Presses whatever the player is looking at, as a pinch would. For tests and other inputs.</summary>
        public void PressSelect() => pendingSelect = true;
        public readonly List<string> EventLog = new List<string>();

        TrackGenerator track;
        Vehicle car;
        Horde horde;
        GazeScout scout;
        Interventions interventions;
        HandInput leftHand, rightHand;
        SoundKit sound;
        TextMesh banner;
        RimGauges gauges;
        Material pickupMaterial;
        MeshRenderer dwellRing;
        Mesh dwellMesh;
        readonly MeshBuilder dwellBuilder = new MeshBuilder();
        MaterialPropertyBlock dwellBlock;
        System.Func<float, float> roadOffsetAtZ;
        bool booted;
        float time;
        int runIndex;
        string endReason = "";
        float reacquire, recenterDelay = 0.6f, autopilotRetry;
        float boostTimer, flashTimer, scrapeSoundTimer;
        bool wasScraping;
        bool onDeadBranch;
        bool autoEscaping;
        bool wasOnCliff;
        float lastCruise;
        Pickup autoGazeTarget;
        bool recentered;
        bool handEverSeen;
        IntentFrame pendingIntents;
        bool gripWas, buttonWas, triggerWas, stickWas;
        StartMenu menu;
        TutorialPanel tutorial;
        Transform uiRoot;
        float tutorialTime;
        bool tutorialFromMenu;
        bool pinchWas, pendingSelect, pendingStart, pendingHowTo, pendingGarage;
        int runSeed;
        Transform worldRoot;
        Effects fx;
        ShooterSystem shooterSystem;
        TurretGun turret;
        Shovel shovel;
        Progress progress;
        GameOverPanel gameOverPanel;
        Garage garage;
        float gameOverTime;
        int respawnsThisRun, shownBank;
        float acidGrace;
        int pendingGarageScroll;
        GameOverChoice pendingChoice;
        bool pendingBack;

        const string BestKey = "LastLight.BestDistance";

        void Start()
        {
            ApplyPassthrough();
            Boot();
        }

        /// <summary>
        /// The compositor shows passthrough wherever the eye buffer is transparent, so the clear colour's
        /// alpha sets the tint: black at 30% alpha darkens the room by 30%. Opaque game geometry is unaffected.
        /// Device only; the Editor keeps the scene's solid dark background.
        /// </summary>
        void ApplyPassthrough()
        {
            if (!passthrough || Application.platform != RuntimePlatform.Android || head == null) return;
            var cam = head.GetComponent<Camera>();
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, Mathf.Clamp01(passthroughTint));
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = true;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) progress?.Save();
        }

        void OnApplicationQuit() => progress?.Save();

        void Update()
        {
            if (!booted) return;
            ReadKeyboard();
            ReadControllers();
            Tick(Mathf.Min(Time.deltaTime, 0.05f));
        }

        void LateUpdate()
        {
            if (!booted || head == null) return;
            if (!XRSettings.isDeviceActive)
            {
                // Desktop view for Editor testing without a headset.
                Vector3 c = diorama.transform.position;
                if (State == RunState.Menu || State == RunState.Tutorial || State == RunState.Garage)
                {
                    head.position = diorama.ToWorld(HeadLocal);
                    head.LookAt(State == RunState.Garage ? garage.LookPoint : uiRoot.position);
                    return;
                }
                head.position = c + new Vector3(0f, 0.6f, -0.8f);
                head.LookAt(c + new Vector3(0f, 0f, 0.15f));
            }
            // The run-over panel lazily follows the head, after the camera has moved this frame.
            if (gameOverPanel.Visible) gameOverPanel.Follow(head, Time.deltaTime, false);
        }

        public void Boot()
        {
            if (booted) return;
            booted = true;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            Transform trackingSpace = head != null ? head.parent : null;

            pickupMaterial = new Material(baseMaterial);
            pickupMaterial.SetFloat("_Emission", 1.1f);
            progress = new Progress(persistProgress && Application.isPlaying);
            ShooterZombie.GlowMaterial = glowMaterial;

            // The forest world lives under one identity-transformed root, so the garage can hide it whole.
            worldRoot = new GameObject("World").transform;
            worldRoot.SetParent(diorama.transform, false);

            BuildIsland();
            track = NewTrack(seed);
            car = new Vehicle(worldRoot, terrainMaterial, glowMaterial);
            car.SetModel(progress.Vehicle, progress.ColorOf(progress.Vehicle));
            horde = new Horde(worldRoot, terrainMaterial, 18, 99);
            scout = new GazeScout(diorama, head, glowMaterial);
            interventions = new Interventions(diorama, glowMaterial);
            fx = new Effects(terrainMaterial, glowMaterial);
            shooterSystem = new ShooterSystem(worldRoot, glowMaterial, fx);
            shooterSystem.OnAcidHit = AcidHit;
            shooterSystem.OnSpit = z => { sound?.Play(sound.spit, 0.55f); Log($"Shooter spat acid from {z.DioramaLocal.z:0} units ahead"); };
            shooterSystem.OnSplash = () => sound?.Play(sound.splash, 0.5f);
            turret = new TurretGun(car.Body, terrainMaterial, glowMaterial);
            shovel = new Shovel(car.Body, terrainMaterial);
            MountGear();
            // Scout and steering visuals spawn under the disk itself; move them into the world too.
            foreach (Transform t in new List<Transform>(EnumerateChildren(diorama.transform)))
                if (t != worldRoot) t.SetParent(worldRoot, false);
            interventions.OnGrab = () => sound?.Play(sound.grab);
            interventions.OnRelease = () => sound?.Play(sound.commit, 0.5f);
            interventions.OnSweep = (h, dir) => { h.Sweep(dir); sound?.Play(sound.sweep); Log("Swept boulder " + (dir > 0 ? "right" : "left")); };
            leftHand = new HandInput(trackingSpace);
            rightHand = new HandInput(trackingSpace);
            sound = Application.isPlaying ? new SoundKit(gameObject) : null;
            roadOffsetAtZ = z => track.Center(Distance + z - Diorama.CarZ) - track.Center(Distance);

            dwellBlock = new MaterialPropertyBlock();
            dwellMesh = new Mesh { name = "LL_DwellRing" };
            dwellRing = MeshBuilder.Spawn("Gaze Progress", worldRoot, dwellMesh, glowMaterial);
            dwellRing.gameObject.SetActive(false);

            BuildHud();
            BuildPanels();
            Best = PlayerPrefs.GetFloat(BestKey, 0f);
            PrepareRun();
            if (showMenuOnBoot && !autopilot) OpenMenu();
            else BeginRun();
        }

        static IEnumerable<Transform> EnumerateChildren(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++) yield return parent.GetChild(i);
        }

        /// <summary>Puts the turret on the roof and the shovel on the front bumper of whichever vehicle is driven.</summary>
        void MountGear()
        {
            turret.SetMount(new Vector3(0f, car.RoofY, -0.2f));
            shovel.SetMount(new Vector3(0f, 0f, car.FrontZ + 0.2f));
        }

        TrackGenerator NewTrack(int trackSeed)
        {
            var t = new TrackGenerator(worldRoot, terrainMaterial, pickupMaterial, trackSeed);
            t.OnRockfallWarning = h => { sound?.Play(sound.rumble, 0.8f); Log("Rockfall warning"); };
            t.OnCliffPlanned = r => Log($"Cliff road planned at {r.startS:0} m: half width {r.halfWidth:0.00}");
            t.OnForkPlanned = f => Log($"Split planned at {f.startS:0} m: {(f.deadSide != 0 ? "has a dead end" : "has an obstacle branch")}");
            return t;
        }

        /// <summary>Resets the world for a new run, then plays the tutorial card or starts straight away.</summary>
        public void Restart()
        {
            PrepareRun();
            BeginRun();
        }

        void BeginRun()
        {
            if (tutorialEachRun) ShowTutorial(false);
            else StartRunning();
        }

        void StartRunning()
        {
            menu.Visible = false;
            tutorial.Visible = false;
            gameOverPanel.Visible = false;
            State = RunState.Running;
            Log($"Run {runIndex} started, seed {runSeed}, driving {VehicleCatalog.All[car.Model].name}");
        }

        void OpenMenu()
        {
            State = RunState.Menu;
            tutorial.Visible = false;
            gameOverPanel.Visible = false;
            garage.Visible = false;
            worldRoot.gameObject.SetActive(true);
            menu.Visible = true;
            Log("Start menu shown");
        }

        /// <summary>Swaps the forest island for the floating garage and its vehicle panel.</summary>
        void OpenGarage()
        {
            State = RunState.Garage;
            menu.Visible = false;
            worldRoot.gameObject.SetActive(false);
            garage.Open();
            Log("Garage opened");
        }

        void CloseGarage()
        {
            garage.Visible = false;
            // Whatever is now equipped is what the next run drives.
            car.SetModel(progress.Vehicle, progress.ColorOf(progress.Vehicle));
            MountGear();
            OpenMenu();
        }

        /// <summary>From the run-over panel back to the title, with the world reset behind it.</summary>
        public void ReturnToMenu()
        {
            PrepareRun();
            OpenMenu();
        }

        void ShowTutorial(bool fromMenu)
        {
            State = RunState.Tutorial;
            tutorialTime = 0f;
            tutorialFromMenu = fromMenu;
            menu.Visible = false;
            tutorial.Visible = true;
            tutorial.Show(0f);
            Log(fromMenu ? "How to play opened from the menu" : $"Tutorial shown before run {runIndex}");
        }

        /// <summary>Resets the track, jeep, horde and scores. The world stays frozen until the run starts.</summary>
        void PrepareRun()
        {
            runIndex++;
            runSeed = fixedSeed ? seed : seed + runIndex * 7919;
            track.Reset();
            if (!fixedSeed) track = NewTrack(runSeed);
            car.Reset();
            car.health = startHealth;
            scout.Reset();
            scout.FlashlightActive = false;
            scout.ScriptedTarget = null;
            interventions.Reset();
            shooterSystem.Reset();
            fx.Clear();
            turret.Reset();
            shovel.Reset();
            gameOverPanel.Visible = false;
            respawnsThisRun = 0;
            acidGrace = 0f;
            Distance = 0f;
            Gap = startGap;
            Coins = 0;
            boostTimer = flashTimer = 0f;
            wasScraping = false;
            onDeadBranch = false;
            wasOnCliff = false;
            autoGazeTarget = null;
            lastCruise = cruiseSpeed;
            dwellRing.gameObject.SetActive(false);
            endReason = "";
            autopilotRetry = 0f;
            track.Scroll(0f, 0f, 0, 20f);
        }

        /// <summary>Advances the whole simulation by dt. Public so tests can drive it in edit mode.</summary>
        public void Tick(float dt)
        {
            time += dt;
            if (Application.isPlaying) Recenter(dt);

            bool xr = XRSettings.isDeviceActive;
            HandFrame left = leftHand.Sample(Handedness.Left, dt);
            HandFrame right = rightHand.Sample(Handedness.Right, dt);
            if ((left.tracked && leftHand.SecondsLost == 0f) || (right.tracked && rightHand.SecondsLost == 0f)) handEverSeen = true;
            bool handsAvailable = xr && leftHand.Available;
            // Either hand can play, so only losing both pauses the run. Controllers work until a hand first appears.
            bool handsRequired = handsAvailable && !autopilot && handEverSeen;
            float bothLost = Mathf.Min(leftHand.SecondsLost, rightHand.SecondsLost);

            IntentFrame intents = pendingIntents;
            pendingIntents = default;
            if (autopilot) intents = Autopilot(dt);

            // A fresh pinch from either hand is a "select": it presses menu buttons and skips the tutorial.
            // Holding a pinch from the retry gesture does not count until it is released.
            bool pinchNow = (left.tracked && left.pinching) || (right.tracked && right.pinching);
            bool select = (pinchNow && !pinchWas) || pendingSelect;
            pinchWas = pinchNow;
            pendingSelect = false;
            if (State == RunState.Menu) TickMenu(dt, select);
            else if (State == RunState.Tutorial) TickTutorial(dt, select);
            else if (State == RunState.Garage) TickGarage(dt, select);
            pendingStart = pendingHowTo = pendingGarage = pendingBack = false;
            pendingGarageScroll = 0;

            // Usability pause: losing sight of both hands freezes car, horde and score together.
            if (State == RunState.Running && handsRequired && bothLost > trackingLossPause)
            {
                State = RunState.Paused;
                Log("Paused: both hands lost");
            }
            else if (State == RunState.Paused && handsRequired)
            {
                reacquire = bothLost == 0f ? reacquire + dt : 0f;
                if (reacquire > 0.4f) { State = RunState.Running; reacquire = 0f; Log("Resumed"); }
            }

            bool running = State == RunState.Running;
            interventions.Tick(dt, left, right, handsAvailable, car, track.hazards, intents, running);

            if (running) Simulate(dt);
            else if (State == RunState.GameOver) TickGameOver(dt, select);
            pendingChoice = GameOverChoice.None;

            float carS = Distance + car.boostOffset;
            float slope = track.Slope(carS);
            car.screenOffset = track.Center(carS) - track.Center(Distance);
            UpdateBranch(carS);
            track.Limits(carS, car.branch, out float min, out float max);
            if (running)
            {
                bool onCliff = track.OnCliffStretch(carS);
                car.Steer(dt, slope, onCliff ? curveDrift * CliffDriftFraction : curveDrift, min, max);
                UpdateScrape(dt);
                if (onCliff && track.OverAbyss(car.x, carS)) FallIntoAbyss();
                else if (wasOnCliff && !onCliff) Log("Crossed a cliff road");
                wasOnCliff = onCliff;
            }
            car.UpdateFall(dt);
            car.Tick(running ? dt : 0f, CurrentTargetSpeed(running), time, boostTimer > 0f, slope);
            horde.Tick(Gap, time, running, track, Distance);
            scout.Tick(running ? dt : dt * 0.25f, track.hazards, !xr);
            TickPickups(running ? dt : 0f);
            shooterSystem.Tick(dt, time, running, track, car, Distance, TrackGenerator.StageAt(Distance));
            TickGear(running ? dt : 0f);
            // Debris keeps settling on the run-over screen, so a final crash plays out.
            fx.Tick(running || State == RunState.GameOver ? dt : 0f);

            Vector3 headLocal = new Vector3(car.ScreenX, 0.7f, car.Z + 1.4f);
            float reveal = flashTimer > 0f ? Mathf.Clamp01(flashTimer / 0.5f) : 0f;
            diorama.PushShaderGlobals(scout.Patch, GazeScout.PatchRadius, scout.Intensity, headLocal, new Vector3(0f, -0.12f, 1f), reveal);
            UpdateHud(dt);
        }

        void TickMenu(float dt, bool select)
        {
            Ray? gaze = null;
            if (scout.TryGetLocalRay(out Vector3 o, out Vector3 d))
                gaze = new Ray(diorama.ToWorld(o), diorama.transform.TransformDirection(d));
            MenuAction action = menu.Tick(dt, gaze, select);
            if (pendingStart) action = MenuAction.StartRun;
            if (pendingHowTo) action = MenuAction.HowToPlay;
            if (pendingGarage) action = MenuAction.Garage;
            if (action == MenuAction.None) return;
            sound?.Play(sound.commit);
            if (action == MenuAction.StartRun)
            {
                Log("Start Run pressed");
                BeginRun();
            }
            else if (action == MenuAction.Garage) OpenGarage();
            else ShowTutorial(true);
        }

        Ray? WorldGazeRay()
        {
            if (!scout.TryGetLocalRay(out Vector3 o, out Vector3 d)) return null;
            return new Ray(diorama.ToWorld(o), diorama.transform.TransformDirection(d));
        }

        void TickGarage(float dt, bool select)
        {
            if (pendingGarageScroll != 0) garage.ScrollBy(pendingGarageScroll);
            GarageAction action = garage.Tick(dt, WorldGazeRay(), select, Log, out bool purchased, out bool refused);
            if (pendingBack) action = GarageAction.Back;
            if (purchased) sound?.Play(sound.powerup);
            else if (refused) sound?.Play(sound.cancel);
            else if (action != GarageAction.None) sound?.Play(sound.commit, 0.7f);
            if (action == GarageAction.Back) CloseGarage();
        }

        /// <summary>The run-over panel: shown once the crash has played out, then Retry, Respawn or Main Menu.</summary>
        void TickGameOver(float dt, bool select)
        {
            gameOverTime += dt;
            if (gameOverTime < GameOverPanelDelay) return;
            if (!gameOverPanel.Visible)
            {
                gameOverPanel.SetContent(endReason, Distance, Coins, progress.Coins, NextRespawnCost, respawnsThisRun);
                gameOverPanel.Visible = true;
                gameOverPanel.Follow(head, dt, true);
                Log($"Run-over panel shown: respawn costs {NextRespawnCost}, bank {progress.Coins}");
            }
            else if (progress.Coins != shownBank)
                gameOverPanel.SetContent(endReason, Distance, Coins, progress.Coins, NextRespawnCost, respawnsThisRun);
            shownBank = progress.Coins;
            bool accept = gameOverTime >= GameOverPanelDelay + GameOverInputDelay;
            GameOverChoice choice = gameOverPanel.Tick(dt, WorldGazeRay(), select, accept);
            if (pendingChoice != GameOverChoice.None && accept) choice = pendingChoice;
            switch (choice)
            {
                case GameOverChoice.Retry:
                    sound?.Play(sound.commit);
                    Log("Retry pressed");
                    Restart();
                    break;
                case GameOverChoice.Respawn:
                    if (!TryRespawn()) sound?.Play(sound.cancel);
                    break;
                case GameOverChoice.MainMenu:
                    sound?.Play(sound.commit);
                    Log("Main Menu pressed");
                    ReturnToMenu();
                    break;
            }
        }

        /// <summary>
        /// Spends coins to carry on the same run. Each respawn in a run costs twice the last.
        /// The jeep is put back on the road with full health and a full lead on the horde, the road just
        /// ahead is cleared, and it blinks through a short grace period.
        /// </summary>
        public bool TryRespawn()
        {
            int cost = NextRespawnCost;
            if (State != RunState.GameOver || !progress.Spend(cost))
            {
                Log($"Respawn refused: costs {cost}, bank {progress.Coins}");
                return false;
            }
            respawnsThisRun++;
            float carS = Distance + car.boostOffset;

            // Back onto the road: the escape gap of a dead end we were trapped in, otherwise the road centre.
            float roadX = car.x;
            Hazard wall = null;
            foreach (var h in track.hazards)
                if (h != null && h.alive && h.kind == HazardKind.DeadEnd && Mathf.Abs(h.DioramaLocal.z - car.Z) < 4f) wall = h;
            if (wall != null) roadX = wall.escapeX;
            else if (car.falling || track.OnCliffStretch(carS)) roadX = 0f;
            track.Limits(carS, car.branch, out float min, out float max);
            if (wall == null) roadX = Mathf.Clamp(roadX, min, max);
            car.Revive(roadX);
            car.health = startHealth;
            car.invulnerable = RespawnGrace;
            car.speed = CruiseNow * 0.8f;
            Gap = maxGap;
            boostTimer = 0f;

            // Clear rocks and trees just ahead, and any acid in the air.
            foreach (var h in track.hazards)
            {
                if (h == null || !h.alive || h.kind == HazardKind.DeadEnd) continue;
                float dz = h.DioramaLocal.z - car.Z;
                if (dz > -Vehicle.HalfLength && dz < RespawnClearRange) Shatter(h, Vector3.up * 2f);
            }
            shooterSystem.ClearNear(carS, RespawnClearRange);

            gameOverPanel.Visible = false;
            gameOverTime = 0f;
            State = RunState.Running;
            sound?.Play(sound.powerup);
            Log($"Respawned for {cost} coins (respawn {respawnsThisRun}), bank {progress.Coins}, next costs {NextRespawnCost}");
            return true;
        }

        void TickGear(float dt)
        {
            shovel.Tick(dt);
            ShooterZombie killed = turret.Tick(dt, time, track.shooters, car, fx, worldRoot, diorama, () => sound?.Play(sound.gun, 0.22f));
            if (killed != null) KillShooter(killed);
        }

        void KillShooter(ShooterZombie z)
        {
            z.alive = false;
            z.windup = 0f;
            z.StopGlow();
            Vector3 local = z.transform.localPosition + Vector3.up * 1.1f;
            fx.Burst(z.chunk.root, local, DebrisKind.Flesh, 7, 0.9f, Vector3.zero, 0.8f);
            fx.Splash(z.chunk.root, local, Palette.Acid, 10, 4f, 0.2f, 0.6f);
            fx.FadeOut(z.transform, 0.9f, Vector3.down * 0.8f, 160f);
            sound?.Play(sound.smash, 0.5f);
            Log($"Turret killed a shooter {z.DioramaLocal.z:0} units ahead{(Diorama.InsideDisk(z.DioramaLocal) || scout.IsLit(z.DioramaLocal) ? "" : " (unseen)")}");
        }

        /// <summary>Acid reached the jeep. A shield blocks it; otherwise the jeep slows hard for a moment.</summary>
        bool AcidHit()
        {
            // Respawn grace, and a short spell after a hit so overlapping splashes don't stack.
            if (car.invulnerable > 0f || acidGrace > 0f) return true;
            if (car.shield)
            {
                sound?.Play(sound.shieldBreak, 0.5f);
                Log("Shield blocked acid");
                return true;
            }
            car.speed *= acidSpeedKeep;
            car.acid = acidSeconds;
            acidGrace = acidSeconds + 0.8f;
            car.hitFlash = 0.4f;
            boostTimer = 0f;
            sound?.Play(sound.splash);
            Log($"Hit by acid: slowed to {car.speed * 10f:0} km/h");
            return false;
        }

        void TickTutorial(float dt, bool select)
        {
            tutorialTime += dt;
            tutorial.Show(tutorialTime);
            bool skipped = select && tutorialTime > 0.8f;
            if (tutorialTime < TutorialPanel.Duration && !skipped) return;
            Log(skipped ? $"Tutorial skipped at {tutorialTime:0.0} s" : "Tutorial finished");
            if (tutorialFromMenu) OpenMenu();
            else StartRunning();
        }

        /// <summary>Road-relative X of a disk-local point: 0 is the road centre at that point's place along the road.</summary>
        public float RoadRelativeX(Vector3 local) => local.x - roadOffsetAtZ(local.z);

        void UpdateBranch(float carS)
        {
            if (track.ForkAt(carS) == null)
            {
                if (car.branch != 0) Log(onDeadBranch ? "Escaped the dead end through the narrow route" : "Split road rejoined");
                car.branch = 0;
                onDeadBranch = false;
            }
            else if (car.branch == 0 && track.BranchesSeparated(carS))
            {
                car.branch = car.x < 0f ? -1 : 1;
                Fork f = track.ForkAt(carS);
                onDeadBranch = f.deadSide == car.branch;
                string detail = onDeadBranch ? "dead end" : f.obstacleSide == car.branch ? "obstacle branch" : "clear branch";
                Log($"Took the {(car.branch < 0 ? "left" : "right")} split: {detail}");
            }
        }

        void UpdateScrape(float dt)
        {
            scrapeSoundTimer -= dt;
            if (car.scraping && scrapeSoundTimer <= 0f)
            {
                sound?.Play(sound.scrape, 0.6f);
                scrapeSoundTimer = 0.3f;
            }
            if (car.scraping && !wasScraping) Log("Scraping the road edge, slowing down");
            wasScraping = car.scraping;
        }

        float CruiseNow => Mathf.Min(maxCruiseSpeed, cruiseSpeed + Mathf.Floor(Distance / speedStepDistance) * speedStep);
        public float CruiseSpeedNow => CruiseNow;

        float CurrentTargetSpeed(bool running)
        {
            if (!running || car.Stalled) return 0f;
            float v = CruiseNow;
            if (boostTimer > 0f) v *= BoostSpeedMultiplier;
            if (track.InEscapeRoute(Distance + car.boostOffset, car.branch)) return Mathf.Min(v, CruiseNow * EscapeCrawlFraction);
            if (car.scraping) v *= ScrapeSpeedMultiplier;
            if (car.acid > 0f) v = Mathf.Min(v, CruiseNow * acidSpeedCap);
            return v;
        }

        void Simulate(float dt)
        {
            // Vehicle speed is integrated in car.Tick; use last frame's speed for the world scroll.
            float speed = car.speed;
            Distance += speed * dt;

            if (boostTimer > 0f) boostTimer -= dt;
            if (flashTimer > 0f) flashTimer -= dt;
            if (acidGrace > 0f) acidGrace -= dt;
            scout.FlashlightActive = flashTimer > 0f;

            float cruise = CruiseNow;
            if (cruise > lastCruise + 0.01f)
            {
                sound?.Play(sound.powerup, 0.5f);
                Log($"Speed up: cruise {cruise:0.0} at {Distance:0} m");
            }
            lastCruise = cruise;

            float hordeSpeed = cruise * hordeRatio;
            Gap = Mathf.Min(maxGap, Gap + (speed - hordeSpeed) * dt);

            int difficulty = Mathf.FloorToInt(Distance / 120f);
            // Rocks start falling further ahead at speed, so they still land in front of the jeep.
            track.Scroll(Distance, car.targetX, difficulty, Mathf.Max(20f, cruise * 3.4f));

            foreach (var h in track.hazards)
            {
                if (h == null || !h.alive) continue;
                if (h.Tick(dt)) sound?.Play(sound.hit, 0.4f);
            }
            Collide(dt);

            if (Gap <= 0f)
            {
                if (car.invulnerable > 0f) Gap = 0.5f;
                else if (car.shield)
                {
                    Gap = ShieldKnockback;
                    sound?.Play(sound.shieldBreak);
                    Log("Shield stopped the horde and knocked it back");
                }
                else End(car.trapped ? "Trapped in a dead end" : "The horde caught you");
            }
            if (State == RunState.Running && car.health <= 0) End("The car is wrecked");
        }

        /// <summary>Applies damage unless a shield absorbs it. Returns true if the shield took it.</summary>
        bool Damage(string source)
        {
            if (car.shield)
            {
                sound?.Play(sound.shieldBreak, 0.5f);
                Log($"Shield absorbed {source}");
                return true;
            }
            car.health--;
            car.hitFlash = 0.6f;
            sound?.Play(sound.hit);
            Log($"{source}, health {car.health}");
            return false;
        }

        void Collide(float dt)
        {
            for (int i = 0; i < track.hazards.Count; i++)
            {
                Hazard h = track.hazards[i];
                if (h == null || !h.IsObstacle) continue;
                Vector3 p = h.DioramaLocal;
                float dz = p.z - car.Z;
                float depth = h.kind == HazardKind.Boulder ? h.radius * 0.8f : 0.5f;
                float hRel = p.x - roadOffsetAtZ(p.z);

                // The front shovel reaches further ahead and wider than the jeep, and ploughs rocks and trees aside.
                if (shovel.Active && h.kind != HazardKind.DeadEnd
                    && dz <= Vehicle.HalfLength + Shovel.Reach + depth && dz >= -Vehicle.HalfLength * 0.5f
                    && Mathf.Abs(car.x - hRel) < h.HalfWidth + Shovel.HalfWidth)
                {
                    float side = Mathf.Sign(hRel - car.x == 0f ? 1f : hRel - car.x);
                    Vector3 push = new Vector3(side * 7f, 2f, car.speed * 0.9f);
                    Shatter(h, push);
                    bool broke = shovel.UseHit(fx, h.chunk.root, h.transform.localPosition + Vector3.up * 0.5f, new Vector3(0f, 3f, car.speed * 0.6f));
                    sound?.Play(sound.smash);
                    Log($"Shovel smashed a {(h.kind == HazardKind.Boulder ? "boulder" : "fallen tree")}, {shovel.HitsLeft} hits left{(broke ? ": shovel broke" : "")}");
                    continue;
                }

                if (dz > Vehicle.HalfLength + depth || dz < -Vehicle.HalfLength) continue;
                if (!car.OverlapsRoadX(hRel, h.HalfWidth)) continue;

                if (h.kind == HazardKind.DeadEnd)
                {
                    // The wall has one narrow gap. Lined up with it, the jeep can squeeze through.
                    bool inGap = Mathf.Abs(car.x - h.escapeX) <= TrackGenerator.EscapeHalfWidth - Vehicle.HalfWidth;
                    if (inGap)
                    {
                        if (car.trapped)
                        {
                            car.trapped = false;
                            sound?.Play(sound.commit);
                            Log("Lined up with the escape gap");
                        }
                        continue;
                    }
                    car.speed = 0f;
                    if (!car.trapped)
                    {
                        car.trapped = true;
                        car.hitFlash = 0.6f;
                        boostTimer = 0f;
                        sound?.Play(sound.hit);
                        Log("Hit the dead-end wall: trapped");
                    }
                    continue;
                }

                // Rocks and fallen trees: the jeep smashes through, losing most of its speed. Rocks shatter,
                // trees break into pieces, and the pieces tumble and fade away.
                // A shield, like respawn grace, ploughs straight through with no slowdown and no damage.
                bool boulder = h.kind == HazardKind.Boulder;
                float before = car.speed;
                bool protectedHit = car.invulnerable > 0f || car.shield;
                if (!protectedHit)
                {
                    car.speed *= obstacleSpeedKeep;
                    car.hitFlash = 0.5f;
                }
                Shatter(h, new Vector3((hRel - car.x) * 2.5f, 1f, before * 0.75f));
                sound?.Play(sound.smash);
                string what = boulder ? "boulder" : "fallen tree";
                if (car.shield) Log($"Shield smashed through a {what} without slowing");
                else if (car.invulnerable > 0f) Log($"Smashed a {what} during respawn grace");
                else if (obstaclesDamage) Damage($"Smashed through a {what}, slowed to {car.speed * 10f:0} km/h");
                else Log($"Smashed through a {what}, slowed to {car.speed * 10f:0} km/h");
            }
        }

        /// <summary>Breaks a rock or fallen tree into tumbling pieces and removes it from the road.</summary>
        void Shatter(Hazard h, Vector3 push)
        {
            if (h == null || !h.alive) return;
            Transform parent = h.chunk.root;
            Vector3 at = h.transform.localPosition;
            if (h.kind == HazardKind.Boulder)
            {
                fx.Burst(parent, at, DebrisKind.Rock, 11, h.radius * 0.9f, push, 1.1f);
                fx.Splash(parent, at, new Color(0.5f, 0.45f, 0.38f, 0.35f), 6, 2.5f, 0.6f, 0.7f, 0.5f);
            }
            else
            {
                int pieces = Mathf.RoundToInt(5 + h.HalfWidth * 3f);
                fx.Burst(parent, at + Vector3.up * 0.3f, DebrisKind.Wood, pieces, 1f, push, h.HalfWidth * 0.8f);
            }
            if (h.marker != null) h.marker.gameObject.SetActive(false);
            h.Smash();
        }

        void FallIntoAbyss()
        {
            car.falling = true;
            car.scraping = false;
            car.speed = 0f;
            boostTimer = 0f;
            End("The jeep fell into the abyss");
        }

        void End(string reason)
        {
            State = RunState.GameOver;
            endReason = reason;
            gameOverTime = 0f;
            car.acid = 0f;
            progress.Save();
            if (Distance > Best)
            {
                Best = Distance;
                PlayerPrefs.SetFloat(BestKey, Best);
                PlayerPrefs.Save();
            }
            sound?.Play(sound.gameOver);
            Log($"Run over: {reason}. Distance {Distance:0} m, coins {Coins}");
        }

        // ---------- Gaze pickups ----------

        void TickPickups(float dt)
        {
            bool hasRay = scout.TryGetLocalRay(out Vector3 rayOrigin, out Vector3 rayDir);
            Pickup best = null;
            float bestDist = PickupGazeRadius;
            var pickups = track.pickups;
            for (int i = 0; i < pickups.Count; i++)
            {
                Pickup p = pickups[i];
                if (p == null || !p.alive) continue;
                Vector3 pos = p.DioramaLocal;
                if (pos.z < -Diorama.Radius - 1f)
                {
                    p.Consume(); // It has fallen off the back of the disk.
                    continue;
                }
                bool visible = Diorama.InsideDisk(pos) || scout.IsLit(pos);
                p.SetVisible(visible);
                p.Animate(time);
                if (p.kind == PickupKind.Coin)
                {
                    // Coins are collected by driving through them, never by looking at them.
                    if (dt > 0f && Mathf.Abs(pos.z - car.Z) < Vehicle.HalfLength + CoinReach
                        && Mathf.Abs(RoadRelativeX(pos) - car.x) < Vehicle.HalfWidth + CoinReach)
                        Collect(p);
                    continue;
                }
                if (!visible || !hasRay) continue;
                float dist = Vector3.Cross(rayDir, pos - rayOrigin).magnitude;
                if (dist < bestDist) { bestDist = dist; best = p; }
            }

            Pickup collected = null;
            for (int i = 0; i < pickups.Count; i++)
            {
                Pickup p = pickups[i];
                if (p == null || !p.alive) continue;
                if (p == best)
                {
                    p.dwell += dt;
                    if (p.dwell >= p.DwellSeconds) collected = p;
                }
                else p.dwell = Mathf.Max(0f, p.dwell - dt * 0.8f);
            }

            UpdateDwellRing(best);
            if (collected != null) Collect(collected);
        }

        void UpdateDwellRing(Pickup target)
        {
            bool show = target != null && target.Progress > 0.01f;
            if (dwellRing.gameObject.activeSelf != show) dwellRing.gameObject.SetActive(show);
            if (!show) return;
            dwellBuilder.Clear();
            dwellBuilder.Arc(Vector3.zero, 1.95f, 2.3f, target.Progress, 32, Color.white);
            dwellBuilder.ToMesh("LL_DwellRing", dwellMesh);
            dwellRing.transform.localPosition = target.DioramaLocal + Vector3.back * 0.3f;
            Color c = target.kind switch
            {
                PickupKind.Booster => Palette.BoostOrange,
                PickupKind.Shield => Palette.ShieldBlue,
                PickupKind.Flashlight => Palette.LightBeam,
                PickupKind.Turret => Palette.TurretRed,
                PickupKind.Shovel => Palette.ShovelYellow,
                _ => Palette.CoinGold
            };
            c.a = 1f;
            dwellBlock.SetColor("_Tint", c * 1.6f);
            dwellRing.SetPropertyBlock(dwellBlock);
        }

        void Collect(Pickup p)
        {
            Apply(p.kind);
            p.Consume();
        }

        /// <summary>Gives a powerup as if it had been collected. For tests and the Editor's debug keys.</summary>
        public void GrantPowerup(PickupKind kind) => Apply(kind);

        void Apply(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Coin:
                    Coins++;
                    progress.AddCoins(1);
                    sound?.Play(sound.coin);
                    Log($"Coin collected, total {Coins}");
                    break;
                case PickupKind.Booster:
                    boostTimer = BoostSeconds;
                    Gap = Mathf.Min(maxGap, Gap + BoostGapBonus);
                    sound?.Play(sound.powerup);
                    Log("Booster collected");
                    break;
                case PickupKind.Shield:
                    car.shieldTime = Vehicle.ShieldSeconds;
                    sound?.Play(sound.powerup);
                    Log("Shield collected");
                    break;
                case PickupKind.Flashlight:
                    flashTimer = FlashlightSeconds;
                    scout.FlashlightActive = true;
                    sound?.Play(sound.powerup);
                    Log("Flashlight collected");
                    break;
                case PickupKind.Turret:
                    turret.Activate();
                    sound?.Play(sound.powerup);
                    Log("Turret collected");
                    break;
                case PickupKind.Shovel:
                    shovel.Activate();
                    sound?.Play(sound.powerup);
                    Log("Shovel collected");
                    break;
            }
        }

        // ---------- Input fallbacks ----------

        void ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float steer = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            pendingIntents.steer = steer;
            if (kb.fKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) pendingIntents.sweep = true;
            if (kb.rKey.wasPressedThisFrame)
            {
                if (State == RunState.GameOver) pendingChoice = GameOverChoice.Retry;
                else if (State != RunState.Garage && State != RunState.Menu) Restart();
            }
            // Run over: N respawns, M returns to the menu. Menu: G opens the garage.
            if (kb.nKey.wasPressedThisFrame && State == RunState.GameOver) pendingChoice = GameOverChoice.Respawn;
            if (kb.mKey.wasPressedThisFrame && State == RunState.GameOver) pendingChoice = GameOverChoice.MainMenu;
            if (kb.gKey.wasPressedThisFrame && State == RunState.Menu) pendingGarage = true;
            // Editor-only test keys: T turret, Y shovel, U acid hit, K coins.
            if (Application.isEditor && State == RunState.Running)
            {
                if (kb.tKey.wasPressedThisFrame) GrantPowerup(PickupKind.Turret);
                if (kb.yKey.wasPressedThisFrame) GrantPowerup(PickupKind.Shovel);
                if (kb.uKey.wasPressedThisFrame) AcidHit();
            }
            if (Application.isEditor && kb.kKey.wasPressedThisFrame) { progress.AddCoins(100); Log($"Debug: +100 coins, bank {progress.Coins}"); }
            if (State == RunState.Garage)
            {
                if (kb.upArrowKey.wasPressedThisFrame) pendingGarageScroll = -1;
                if (kb.downArrowKey.wasPressedThisFrame) pendingGarageScroll = 1;
                if (kb.leftArrowKey.wasPressedThisFrame) garage.PreviewNext(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) garage.PreviewNext(1);
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) pendingBack = true;
            }
            if (kb.cKey.wasPressedThisFrame) recentered = false;
            if (kb.pKey.wasPressedThisFrame && (State == RunState.Running || State == RunState.Paused))
                State = State == RunState.Paused ? RunState.Running : RunState.Paused;
            // Menu: Enter starts, H opens How to Play. Tutorial: Enter or Space skips.
            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (State == RunState.Menu) pendingStart = true;
                else pendingSelect = true;
            }
            if (kb.hKey.wasPressedThisFrame && State == RunState.Menu) pendingHowTo = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) pendingSelect = true;
        }

        /// <summary>
        /// Controller alternative from the proposal: either thumbstick steers, grip sweeps the
        /// nearest rock, the primary button retries.
        /// </summary>
        void ReadControllers()
        {
            float stickX = 0f, stickY = 0f;
            bool grip = false, button = false, trigger = false;
            foreach (var node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (!dev.isValid) continue;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                {
                    if (Mathf.Abs(axis.x) > Mathf.Abs(stickX)) stickX = axis.x;
                    if (Mathf.Abs(axis.y) > Mathf.Abs(stickY)) stickY = axis.y;
                }
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g) && g) grip = true;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool b) && b) button = true;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool tr) && tr) trigger = true;
            }
            if (Mathf.Abs(stickX) > 0.2f) pendingIntents.steer = stickX;
            if (grip && !gripWas) pendingIntents.sweep = true;
            // Trigger or primary button acts like a pinch on every panel. On the run-over panel with nothing
            // focused, the primary button still retries, as before.
            bool press = (trigger && !triggerWas) || (button && !buttonWas);
            if (press && (State == RunState.Menu || State == RunState.Tutorial || State == RunState.Garage)) pendingSelect = true;
            if (press && State == RunState.GameOver && gameOverPanel.Visible)
            {
                if (gameOverPanel.Focused >= 0) pendingSelect = true;
                else if (button && !buttonWas) pendingChoice = GameOverChoice.Retry;
            }
            // The stick scrolls the garage list.
            if (State == RunState.Garage && Mathf.Abs(stickY) > 0.6f && !stickWas) pendingGarageScroll = stickY > 0f ? -1 : 1;
            stickWas = Mathf.Abs(stickY) > 0.6f;
            gripWas = grip; buttonWas = button; triggerWas = trigger;
        }

        IntentFrame Autopilot(float dt)
        {
            var i = new IntentFrame();
            if (State == RunState.GameOver)
            {
                autopilotRetry += dt;
                if (autopilotRetry > 2f) { autopilotRetry = 0f; Restart(); }
                return i;
            }

            AutopilotGaze();
            if (autopilotMode == AutopilotMode.NoSteering) return i;

            float carS = Distance + car.boostOffset;
            float desired = 0f;
            if (car.branch == 0) autoEscaping = false;
            Fork fork = track.NextFork(carS, carS + 20f);
            if (fork != null)
            {
                int side = car.branch;
                if (side == 0)
                {
                    bool seekDeadEnd = autopilotMode == AutopilotMode.TakeDeadEnds || autopilotMode == AutopilotMode.EscapeDeadEnds;
                    if (fork.deadSide != 0) side = seekDeadEnd ? fork.deadSide : -fork.deadSide;
                    else if (fork.obstacleSide != 0) side = -fork.obstacleSide;
                    else side = 1;
                }
                TrackGenerator.Branch(fork, Mathf.Clamp(carS + 6f, fork.startS, fork.EndS - 0.01f), out float b, out float w);
                desired = side * Mathf.Max(b, 2f);
                // Escape test: drive into the wall first, then steer to the gap and crawl out.
                if (car.trapped) autoEscaping = true;
                if (autopilotMode == AutopilotMode.EscapeDeadEnds && fork.deadSide != 0 && car.branch == fork.deadSide && autoEscaping)
                    desired = TrackGenerator.EscapeCenter(fork, Mathf.Max(carS + 1f, fork.WallS));
            }

            track.Limits(carS, car.branch, out float min, out float max);
            Hazard nearest = null;
            float nearestDz = float.MaxValue;
            foreach (var h in track.hazards)
            {
                if (h == null || !h.alive) continue;
                Vector3 p = h.DioramaLocal;
                float dz = p.z - car.Z;
                if (dz < -Vehicle.HalfLength - 0.5f || dz > 14f) continue;
                if (h.Sweepable)
                {
                    if (dz < 9f && Mathf.Abs(p.x - car.ScreenX) < 2.2f) i.sweep = true;
                    continue;
                }
                if (h.kind == HazardKind.FallenTree && dz < nearestDz) { nearestDz = dz; nearest = h; }
            }
            // Dodge one tree at a time, nearest first, the way a player would.
            if (nearest != null)
            {
                Vector3 p = nearest.DioramaLocal;
                float hRel = p.x - roadOffsetAtZ(p.z);
                float clear = nearest.HalfWidth + Vehicle.HalfWidth + 0.5f;
                if (Mathf.Abs(desired - hRel) < clear)
                {
                    float toRight = hRel + clear, toLeft = hRel - clear;
                    bool rightOk = toRight <= max, leftOk = toLeft >= min;
                    if (rightOk && (!leftOk || Mathf.Abs(toRight - car.x) <= Mathf.Abs(toLeft - car.x))) desired = toRight;
                    else if (leftOk) desired = toLeft;
                }
            }
            // Drive through coins when nothing else needs steering.
            if (nearest == null && fork == null && !track.OnCliffStretch(carS))
            {
                float coinDz = float.MaxValue;
                foreach (var p in track.pickups)
                {
                    if (p == null || !p.alive || p.kind != PickupKind.Coin) continue;
                    Vector3 pos = p.DioramaLocal;
                    float dz = pos.z - car.Z;
                    if (dz < 1f || dz > 10f || dz >= coinDz) continue;
                    coinDz = dz;
                    desired = RoadRelativeX(pos);
                }
            }
            // Steer clear of acid that's about to land or still wet, like a player would,
            // unless a fallen tree close ahead needs the steering first.
            if ((nearest == null || nearestDz > 9f) && shooterSystem.NearestThreat(carS, 16f, out float acidX))
            {
                float clear = ShooterSystem.PuddleRadius * 0.7f + Vehicle.HalfWidth + 0.7f;
                // On a cliff road the limits reach past the edge, so dodge only within the road itself.
                float safeMin = min, safeMax = max;
                if (track.OnCliffStretch(carS))
                {
                    float half = track.RoadHalfAt(carS) - Vehicle.HalfWidth;
                    safeMin = -half; safeMax = half;
                }
                if (Mathf.Abs(desired - acidX) < clear)
                {
                    float toRight = acidX + clear, toLeft = acidX - clear;
                    bool rightOk = toRight <= safeMax, leftOk = toLeft >= safeMin;
                    if (rightOk && (!leftOk || Mathf.Abs(toRight - car.x) <= Mathf.Abs(toLeft - car.x))) desired = toRight;
                    else if (leftOk) desired = toLeft;
                }
            }
            if (autopilotMode == AutopilotMode.FallOffCliffs && track.OnCliffStretch(carS)) desired = 20f;
            i.setTarget = desired;
            return i;
        }

        void AutopilotGaze()
        {
            if (autopilotMode == AutopilotMode.IgnorePowerups)
            {
                // Look at the back of the disk so nothing ahead gets stared at.
                scout.ScriptedTarget = new Vector3(0f, 0f, -Diorama.Radius);
                return;
            }
            if (autoGazeTarget != null && (!autoGazeTarget.alive || autoGazeTarget.DioramaLocal.z < car.Z + 3f)) autoGazeTarget = null;
            if (autoGazeTarget == null)
            {
                float bestZ = float.MaxValue;
                foreach (var p in track.pickups)
                {
                    if (p == null || !p.alive) continue;
                    if (p.kind == PickupKind.Coin) continue;
                    float z = p.DioramaLocal.z;
                    if (z < 8f || z > Diorama.Radius + Diorama.ScoutRange - 4f) continue;
                    if (z < bestZ) { bestZ = z; autoGazeTarget = p; }
                }
            }
            scout.ScriptedTarget = autoGazeTarget != null
                ? autoGazeTarget.DioramaLocal
                : new Vector3(Mathf.Sin(time * 0.7f) * 6f, 0f, Diorama.Radius + 8f + Mathf.Sin(time * 0.4f) * 4f);
        }

        // ---------- Placement ----------

        void Recenter(float dt)
        {
            if (!recenterOnStart || recentered || head == null || !XRSettings.isDeviceActive) return;
            recenterDelay -= dt;
            if (recenterDelay > 0f || head.localPosition.sqrMagnitude < 0.01f) return;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Quaternion yaw = Quaternion.LookRotation(fwd, Vector3.up);
            diorama.transform.SetPositionAndRotation(head.position + yaw * diskOffsetFromHead, yaw);
            recentered = true;
            recenterDelay = 0.6f;
            Log("Disk placed in front of the player");
        }

        // ---------- Static geometry and HUD ----------

        void BuildIsland()
        {
            var mb = new MeshBuilder();
            float r = Diorama.Radius;
            // Rim band and rocky underside. Rendered without disk clipping.
            // Open at the top: the terrain is the only surface there, so cliff drops can be seen into.
            mb.Cylinder(new Vector3(0f, -1.4f, 0f), r, 1.4f, 48, Palette.Earth, -1f, false);
            mb.Cylinder(new Vector3(0f, -7.5f, 0f), r * 0.25f, 6.1f, 14, Palette.Stone, r * 0.98f, false);
            var rng = new System.Random(3);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = r * (0.55f + (float)rng.NextDouble() * 0.4f);
                float y = -2.5f - (float)rng.NextDouble() * 4f * (1f - rad / r + 0.3f);
                mb.Rock(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad), 1f + (float)rng.NextDouble() * 1.5f, rng.Next(), Palette.Stone * 1.2f);
            }
            MeshBuilder.Spawn("Island Base", diorama.transform, mb.ToMesh("LL_IslandBase"), baseMaterial);
        }

        void BuildHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Quarter-circle gauges around the island's lower-left rim, in place of the old text readout.
            UISprite.LoadMaterials(out Material gaugeAlpha, out Material _);
            gauges = new RimGauges(diorama.transform, glowMaterial, gaugeAlpha, HeadLocal);

            var bannerGo = new GameObject("Banner").transform;
            bannerGo.SetParent(diorama.transform, false);
            bannerGo.localPosition = new Vector3(0f, 9f, 2f);
            bannerGo.localRotation = Quaternion.Euler(20f, 0f, 0f);
            banner = MakeText(bannerGo, font, 0.26f, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.6f));
        }

        /// <summary>The title menu and tutorial share one panel, floating above the far rim and facing the player.</summary>
        void BuildPanels()
        {
            UISprite.LoadMaterials(out Material alphaMat, out Material addMat);
            uiRoot = new GameObject("UI Panel").transform;
            uiRoot.SetParent(diorama.transform, false);
            uiRoot.localPosition = uiPanelPosition;
            uiRoot.localRotation = Quaternion.LookRotation(uiPanelPosition - HeadLocal, Vector3.up);
            menu = new StartMenu(uiRoot, alphaMat, addMat) { Visible = false };
            tutorial = new TutorialPanel(uiRoot, alphaMat, addMat) { Visible = false };
            gameOverPanel = new GameOverPanel(diorama.transform, alphaMat, addMat);
            garage = new Garage(diorama.transform, baseMaterial, glowMaterial, alphaMat, addMat, progress, HeadLocal);
        }

        static TextMesh MakeText(Transform parent, Font font, float size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMesh>();
            t.font = font;
            t.fontSize = 48;
            t.characterSize = size;
            t.anchor = anchor;
            t.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
            t.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return t;
        }

        void UpdateHud(float dt)
        {
            bool show = State != RunState.Menu && State != RunState.Tutorial && State != RunState.Garage;
            gauges.Visible = show;
            if (!show) return;

            float topSpeed = maxCruiseSpeed * BoostSpeedMultiplier;
            gauges.Set(Gauge.Speed, car.speed / topSpeed, $"{car.speed * 10f:0} KM/H");

            float gapFrac = Gap / maxGap;
            gauges.Set(Gauge.Horde, gapFrac, gapFrac > 0.6f ? "SAFE" : gapFrac > 0.3f ? "CLOSE" : "DANGER");

            gauges.Set(Gauge.Health, car.health / (float)Mathf.Max(startHealth, 1), $"{Mathf.Max(car.health, 0)}/{startHealth}");

            // Distance fills toward your best run, then turns gold once you pass it.
            bool newBest = Best > 0f && Distance > Best;
            gauges.Set(Gauge.Distance, Distance / Mathf.Max(Best, 300f), newBest ? $"{Distance:0} M  BEST!" : $"{Distance:0} M");
            gauges.SetColor(Gauge.Distance, newBest ? RimGauges.Colors[(int)Gauge.Coins] : RimGauges.Colors[(int)Gauge.Distance]);

            // Coins fill toward the cheapest vehicle still locked in the garage.
            int goal = 0;
            for (int i = 1; i < VehicleCatalog.Count; i++)
                if (!progress.IsUnlocked(i) && (goal == 0 || VehicleCatalog.All[i].cost < goal)) goal = VehicleCatalog.All[i].cost;
            gauges.Set(Gauge.Coins, goal > 0 ? progress.Coins / (float)goal : 1f, Coins > 0 ? $"{progress.Coins:N0}  +{Coins}" : $"{progress.Coins:N0}");

            string effects = "";
            if (car.shield) effects += $"SHIELD {Mathf.CeilToInt(car.shieldTime)}S   ";
            if (boostTimer > 0f) effects += $"BOOST {Mathf.CeilToInt(boostTimer)}S   ";
            if (flashTimer > 0f) effects += $"LIGHT {Mathf.CeilToInt(flashTimer)}S   ";
            if (turret.Active) effects += $"TURRET {Mathf.CeilToInt(turret.TimeLeft)}S   ";
            if (shovel.Active) effects += $"SHOVEL x{shovel.HitsLeft}";
            string alert = car.trapped ? "TRAPPED!"
                : car.Stalled ? "STALLED"
                : car.acid > 0f ? "ACID! SLOWED"
                : car.invulnerable > 0f ? "RESPAWNED"
                : car.scraping ? "SCRAPING THE EDGE" : "";
            gauges.SetStatus(effects.TrimEnd(), alert);
            gauges.Tick(dt);

            switch (State)
            {
                case RunState.GameOver:
                    // The run-over panel takes over once it appears; until then name what happened.
                    banner.text = gameOverPanel.Visible ? "" : endReason;
                    break;
                case RunState.Paused:
                    banner.text = XRSettings.isDeviceActive ? "Show a hand to continue" : "Paused  (P)";
                    break;
                default:
                    banner.text = "";
                    break;
            }
        }

        void Log(string msg)
        {
            string line = $"[{time,6:0.00}s] {msg}";
            EventLog.Add(line);
            if (EventLog.Count > 20000) EventLog.RemoveAt(0);
        }
    }
}
