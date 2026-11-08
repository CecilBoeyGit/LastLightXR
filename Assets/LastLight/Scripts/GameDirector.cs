using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace LastLight
{
    public enum RunState { Running, Paused, GameOver }

    /// <summary>How the test autopilot drives. Normal plays well; the others deliberately exercise failure paths.</summary>
    public enum AutopilotMode { Normal, NoSteering, TakeDeadEnds, EscapeDeadEnds, FallOffCliffs }

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
        const float RamThroughSeconds = 1.2f;   // the horde closes about 5.6 units a second during a stall

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
        public readonly List<string> EventLog = new List<string>();

        TrackGenerator track;
        Vehicle car;
        Horde horde;
        GazeScout scout;
        Interventions interventions;
        HandInput leftHand, rightHand;
        SoundKit sound;
        TextMesh hud, banner;
        Transform hudRoot;
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
        float reacquire, retryHold, recenterDelay = 0.6f, autopilotRetry;
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
        bool gripWas, buttonWas;

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
                head.position = c + new Vector3(0f, 0.6f, -0.8f);
                head.LookAt(c + new Vector3(0f, 0f, 0.15f));
            }
        }

        public void Boot()
        {
            if (booted) return;
            booted = true;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            Transform trackingSpace = head != null ? head.parent : null;

            pickupMaterial = new Material(baseMaterial);
            pickupMaterial.SetFloat("_Emission", 1.1f);

            BuildIsland();
            track = NewTrack(seed);
            car = new Vehicle(diorama.transform, terrainMaterial, glowMaterial);
            horde = new Horde(diorama.transform, terrainMaterial, 18, 99);
            scout = new GazeScout(diorama, head, glowMaterial);
            interventions = new Interventions(diorama, glowMaterial);
            interventions.OnGrab = () => sound?.Play(sound.grab);
            interventions.OnRelease = () => sound?.Play(sound.commit, 0.5f);
            interventions.OnSweep = (h, dir) => { h.Sweep(dir); sound?.Play(sound.sweep); Log("Swept boulder " + (dir > 0 ? "right" : "left")); };
            leftHand = new HandInput(trackingSpace);
            rightHand = new HandInput(trackingSpace);
            sound = Application.isPlaying ? new SoundKit(gameObject) : null;
            roadOffsetAtZ = z => track.Center(Distance + z - Diorama.CarZ) - track.Center(Distance);

            dwellBlock = new MaterialPropertyBlock();
            dwellMesh = new Mesh { name = "LL_DwellRing" };
            dwellRing = MeshBuilder.Spawn("Gaze Progress", diorama.transform, dwellMesh, glowMaterial);
            dwellRing.gameObject.SetActive(false);

            BuildHud();
            Best = PlayerPrefs.GetFloat(BestKey, 0f);
            Restart();
        }

        TrackGenerator NewTrack(int trackSeed)
        {
            var t = new TrackGenerator(diorama.transform, terrainMaterial, pickupMaterial, trackSeed);
            t.OnRockfallWarning = h => { sound?.Play(sound.rumble, 0.8f); Log("Rockfall warning"); };
            t.OnCliffPlanned = r => Log($"Cliff road planned at {r.startS:0} m: half width {r.halfWidth:0.00}");
            t.OnForkPlanned = f => Log($"Split planned at {f.startS:0} m: {(f.deadSide != 0 ? "has a dead end" : "has an obstacle branch")}");
            return t;
        }

        public void Restart()
        {
            runIndex++;
            int runSeed = fixedSeed ? seed : seed + runIndex * 7919;
            track.Reset();
            if (!fixedSeed) track = NewTrack(runSeed);
            car.Reset();
            car.health = startHealth;
            scout.Reset();
            scout.FlashlightActive = false;
            scout.ScriptedTarget = null;
            interventions.Reset();
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
            State = RunState.Running;
            endReason = "";
            retryHold = 0f;
            autopilotRetry = 0f;
            Log($"Run {runIndex} started, seed {runSeed}");
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
            else if (State == RunState.GameOver)
            {
                bool pinching = (left.tracked && left.pinching) || (right.tracked && right.pinching);
                retryHold = pinching ? retryHold + dt : 0f;
                if (retryHold > 0.6f) Restart();
            }

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

            Vector3 headLocal = new Vector3(car.ScreenX, 0.7f, car.Z + 1.4f);
            float reveal = flashTimer > 0f ? Mathf.Clamp01(flashTimer / 0.5f) : 0f;
            diorama.PushShaderGlobals(scout.Patch, GazeScout.PatchRadius, scout.Intensity, headLocal, new Vector3(0f, -0.12f, 1f), reveal);
            UpdateHud();
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
            return v;
        }

        void Simulate(float dt)
        {
            // Vehicle speed is integrated in car.Tick; use last frame's speed for the world scroll.
            float speed = car.speed;
            Distance += speed * dt;

            if (boostTimer > 0f) boostTimer -= dt;
            if (flashTimer > 0f) flashTimer -= dt;
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
                if (car.shield)
                {
                    car.shield = false;
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
                car.shield = false;
                car.hitFlash = 0.3f;
                sound?.Play(sound.shieldBreak);
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
            foreach (var h in track.hazards)
            {
                if (h == null || !h.IsObstacle) continue;
                Vector3 p = h.DioramaLocal;
                float dz = p.z - car.Z;
                float depth = h.kind == HazardKind.Boulder ? h.radius * 0.8f : 0.5f;
                if (dz > Vehicle.HalfLength + depth || dz < -Vehicle.HalfLength) continue;
                float hRel = p.x - roadOffsetAtZ(p.z);
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

                if (h.kind == HazardKind.Boulder)
                {
                    Damage("Hit boulder");
                    car.speed *= 0.3f;
                    h.Smash();
                    continue;
                }

                // Fallen tree: the car stalls against it while the horde keeps coming.
                car.speed = 0f;
                car.stallTimer = 0.25f;
                h.stallTime += dt;
                if (!h.hitCar)
                {
                    h.hitCar = true;
                    Log($"Tree impact: tree road x {hRel:0.00} half {h.HalfWidth:0.00}, jeep road x {car.x:0.00} target {car.targetX:0.00}, gap {Gap:0.0}");
                    Damage("Hit fallen tree, stalled");
                }
                if (h.stallTime > RamThroughSeconds)
                {
                    Damage("Rammed through fallen tree");
                    h.Smash();
                }
            }
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
                if (p.kind == PickupKind.Shield && car.shield) continue; // Only one shield at a time.
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
                _ => Palette.CoinGold
            };
            c.a = 1f;
            dwellBlock.SetColor("_Tint", c * 1.6f);
            dwellRing.SetPropertyBlock(dwellBlock);
        }

        void Collect(Pickup p)
        {
            switch (p.kind)
            {
                case PickupKind.Coin:
                    Coins++;
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
                    car.shield = true;
                    sound?.Play(sound.powerup);
                    Log("Shield collected");
                    break;
                case PickupKind.Flashlight:
                    flashTimer = FlashlightSeconds;
                    scout.FlashlightActive = true;
                    sound?.Play(sound.powerup);
                    Log("Flashlight collected");
                    break;
            }
            p.Consume();
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
            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.cKey.wasPressedThisFrame) recentered = false;
            if (kb.pKey.wasPressedThisFrame && State != RunState.GameOver)
                State = State == RunState.Paused ? RunState.Running : RunState.Paused;
        }

        /// <summary>
        /// Controller alternative from the proposal: either thumbstick steers, grip sweeps the
        /// nearest rock, the primary button retries.
        /// </summary>
        void ReadControllers()
        {
            float stickX = 0f;
            bool grip = false, button = false;
            foreach (var node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (!dev.isValid) continue;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis) && Mathf.Abs(axis.x) > Mathf.Abs(stickX)) stickX = axis.x;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g) && g) grip = true;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool b) && b) button = true;
            }
            if (Mathf.Abs(stickX) > 0.2f) pendingIntents.steer = stickX;
            if (grip && !gripWas) pendingIntents.sweep = true;
            if (button && !buttonWas && State == RunState.GameOver) Restart();
            gripWas = grip; buttonWas = button;
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
            if (autopilotMode == AutopilotMode.FallOffCliffs && track.OnCliffStretch(carS)) desired = 20f;
            i.setTarget = desired;
            return i;
        }

        void AutopilotGaze()
        {
            if (autoGazeTarget != null && (!autoGazeTarget.alive || autoGazeTarget.DioramaLocal.z < car.Z + 3f)) autoGazeTarget = null;
            if (autoGazeTarget == null)
            {
                float bestZ = float.MaxValue;
                foreach (var p in track.pickups)
                {
                    if (p == null || !p.alive) continue;
                    if (p.kind == PickupKind.Coin) continue;
                    if (p.kind == PickupKind.Shield && car.shield) continue;
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
            hudRoot = new GameObject("HUD").transform;
            hudRoot.SetParent(diorama.transform, false);
            hudRoot.localPosition = new Vector3(-17.5f, 6f, -2f);
            hudRoot.localRotation = Quaternion.Euler(10f, -38f, 0f);
            hud = MakeText(hudRoot, font, 0.16f, TextAnchor.UpperLeft, new Color(0.93f, 0.9f, 0.82f));

            var bannerGo = new GameObject("Banner").transform;
            bannerGo.SetParent(diorama.transform, false);
            bannerGo.localPosition = new Vector3(0f, 9f, 2f);
            bannerGo.localRotation = Quaternion.Euler(20f, 0f, 0f);
            banner = MakeText(bannerGo, font, 0.26f, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.6f));
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

        void UpdateHud()
        {
            int bars = Mathf.Clamp(Mathf.RoundToInt(Gap / maxGap * 12f), 0, 12);
            string gapBar = new string('|', bars) + new string('.', 12 - bars);
            string hp = new string('+', Mathf.Max(car.health, 0)) + new string('-', Mathf.Max(startHealth - car.health, 0));
            string action = car.trapped ? "TRAPPED"
                : car.Stalled ? "STALLED"
                : car.scraping ? "Scraping edge"
                : interventions.Dragging ? "Steering" : "";
            string effects = "";
            if (car.shield) effects += "SHIELD  ";
            if (boostTimer > 0f) effects += $"BOOST {Mathf.CeilToInt(boostTimer)}s  ";
            if (flashTimer > 0f) effects += $"LIGHT {Mathf.CeilToInt(flashTimer)}s";
            string src = scout.Source switch
            {
                GazeSource.EyeGaze => "eye gaze",
                GazeSource.Mouse => "mouse",
                GazeSource.Head => "head aim",
                GazeSource.Scripted => "autopilot",
                _ => "searching"
            };
            hud.text = $"DISTANCE  {Distance:0} m\nSPEED     {car.speed * 10f:0} km/h\nBEST      {Best:0} m\nCOINS     {Coins}\nHEALTH    {hp}\nGAP       {gapBar}\n{effects}\n{action}\nscout: {src}";

            switch (State)
            {
                case RunState.GameOver:
                    banner.text = $"{endReason}\n{Distance:0} m   {Coins} coins\nPinch and hold to retry  (R)";
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
            if (EventLog.Count > 1500) EventLog.RemoveAt(0);
        }
    }
}
