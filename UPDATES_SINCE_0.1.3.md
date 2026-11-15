# Last Light: updates since 0.1.3

Everything that changed after version 0.1.3, newest first. The full details are in `CHANGELOG.md`.
Builds are release APKs for PICO Swan (package `com.lastlight.prototype`). The latest is
`Builds/LastLight-0.1.9.apk`.

## Not released yet (branch `feature/tripo-zombie-runner`)

- **Runner zombie model:** the horde walkers are now a rigged 3D zombie with a running animation, made on
  studio.tripo3d.ai from the front/side/back model sheet (about 1,800 triangles, 1k textures).
- **Spitter zombie model:** the shooter zombies are now a rigged spitter with idle and spit animations. Its
  lunge peaks as the acid glob launches, and the acid glow follows its head.
- **New shader, LastLight/DioramaTextured:** textured models clip to the disk the same way the rest of the
  world does.
- The old box meshes are kept as a fallback. Headless preview: all 68 checks pass, 2 of them new.

## 0.1.9

- **Spitter ramp:** a single spitter just past 300 m, then groups that grow from 1 to 3 spitters and come
  closer together (from about 120 m apart down to 25-35 m) by about 1.1 km.
- **Shield reworked:** rare (about 8% of powerups), but one always appears at about 250 m. It lasts
  10 seconds and blocks all damage (rocks, trees, acid, the horde), and flickers in its last 2 seconds.
  It still doesn't save you from falling off a cliff road.
- **Shovel odds:** about 30% of powerups early, 28% mid-run and 16% late.
- **New rim HUD:** five quarter-circle bars around the island's lower-left rim show speed, horde distance,
  health, progress toward your best run and coins toward the next garage unlock. They come with icon badges
  and powerup alerts, and draw over the terrain.
- Headless preview checks for all of the above.

## 0.1.8

- **Rocks and fallen trees:** a hit costs about 70% of your speed and 1 health. They shatter and fade
  away. The fallen-tree stall is gone.
- **Shooter zombies from 300 m:** they stand beside the road and spit acid. Their mouth glows before each
  shot and a green ring marks where the acid will land. The puddle halves your speed for a moment.
- **Turret powerup:** a roof machine gun for 10 seconds that targets the nearest shooter.
- **Shovel powerup:** a plough blade that smashes 5 rocks or trees without slowing you down.
- **Coin bank:** coins are now saved between runs.
- **Garage:** Scout Jeep (free), Hauler Pickup (400 coins) and Rhino APC (1,000 coins), each with 4 colours
  once unlocked. They're placeholder models for now and all drive the same.
- **Run-over panel:** Retry, Respawn and Main Menu buttons. Respawning costs 25 coins and doubles in price
  each time in the same run. It gives full health, a cleared road and 2.5 seconds of immunity.
- New keyboard shortcuts for the garage, the run-over panel and editor testing.

## 0.1.7

- **Start menu:** a retro comic title splash with Start Run and How to Play buttons. Look at a button to
  focus it, then pinch with either hand.
- **5-second How to Play card** before every run, showing steering and scouting. A pinch skips it.

## 0.1.6

- **Braking removed** entirely: the plate, the gesture, the keyboard and the controller.
- **Powerups** start at about 120 m, rare at first and more common later in the run.
- **Coins** are now collected by driving through short trails of them (no more gaze).
- **Cliff roads from about 360 m:** a thin strip over an abyss, each narrower than the last (6.0 down to
  2.5 units). Falling off ends the run.
- The disk's top is open so you can see the drops, and the scout preview shows them as dark gaps.

## 0.1.5

- Every powerup takes 1.5 seconds of staring to collect.
- **Smoother gaze:** speed-adaptive filtering, better handling at the edges, and a fallback to head aim
  when eye tracking drops out.
- Pickups stay until you collect them or they pass behind the disk.
- **Speed ramp:** cruise speed rises 5 km/h every 100 m, up to 120 km/h, with a speed readout.
- Gaze quality is logged every 5 seconds.

## 0.1.4

- Scout light radius increased from 5.5 to 8.5 units.
- Powerups sit on the terrain beside the road.
- Dead-end walls have a narrow, marked gap leading to a slow escape track.
- Zombies stay on the road and split correctly at forks.
