# Last Light changelog

Builds are release APKs for PICO Swan (Android, ARM64), package `com.lastlight.prototype`.
Version history before git: 0.1.1 to 0.1.5 were built and installed but not committed, so the first
commit holds the 0.1.6 source.

## 0.1.9

- Spitters ramp up: the first sighting, just past 300 m, is a single spitter. Groups then come closer together,
  from about 120 m apart down to 25-35 m, and grow from one to three spitters by about 1.1 km.
- Shield: now rare, about 8% of powerups, but one always waits at about 250 m, before the first spitters.
  It lasts 10 seconds and blocks all damage: rocks and fallen trees smash with no slowdown and no health
  lost, acid does nothing, and the horde gets knocked back. The bubble flickers in its last 2 seconds.
  Falling off a cliff road still ends the run.
- Shovel: medium rarity from the start through the medium stage (about 30% and 28% of powerups), and less
  common late (16%).
- New HUD: the floating text readout is replaced by five quarter-circle slider bars around the island's
  lower-left rim: speed (orange), horde distance (coral, pulses when close), health (green, 3 segments),
  distance toward your best run (blue, turns gold past it) and coins toward the next garage unlock (gold).
  A legend of icon badges and values sits by the bars' near ends, and active powerups and alerts float
  above them. The HUD draws over the terrain, so cliffs never hide it.
- Headless preview: checks for the spitter ramp, the shield's timing and protection, powerup shares and
  the HUD gauges, plus HUD renders. The test log no longer drops early events on long runs.

## 0.1.8

- Rocks and fallen trees: hitting one costs about 70% of your speed and a point of health. Rocks shatter and
  trees break into pieces, which tumble and fade away. The fallen-tree stall is gone.
- Shooter zombies from 300 m: they stand on the terrain beside the road, never on it, and show up only where
  you scout. Before each shot the mouth glows green. The acid flies at the jeep and a green ring marks where it
  will land. The splash and the puddle it leaves drop the jeep to half speed for a moment. A shield absorbs one
  hit. More shooters appear after 700 m.
- Turret powerup (from 300 m, medium-rare): a roof machine gun for 10 seconds. It aims at the nearest shooter
  on the disk or in the scouting region, even ones you haven't scouted, and fires tracers.
- Shovel powerup (all run, medium-rare): a plough blade on the front bumper. It smashes rocks and fallen
  trees for 5 hits without slowing the jeep, then breaks off. It doesn't break dead-end walls.
- Coin bank: coins are now saved between runs.
- Garage, from the new start-menu button: a floating mini garage replaces the island, with a scrolling panel
  of vehicle cards that each show a turning 3D model. The Scout Jeep is free; the Hauler Pickup (400 coins)
  and Rhino APC (1000 coins) unlock with coins. Each vehicle has 4 colours once unlocked. All three are
  placeholder box models with the same handling.
- Run-over panel: a floating panel that stays put for small head turns and glides back in front after big
  ones, with Retry, Respawn and Main Menu buttons and the coin bank beside them. Respawn continues the run with
  full health, a full lead on the horde and the road ahead cleared, plus 2.5 seconds of blinking immunity. It
  costs 25 coins, and each further respawn in the same run doubles the price.
- Keys: G opens the garage; on the run-over panel R retries, N respawns and M returns to the menu. Editor
  only: T gives a turret, Y a shovel, U an acid hit, K 100 coins.
- Headless preview: new checks for every feature above, plus close-up shots of the debris, turret, shovel and
  acid.

## 0.1.7

- Start menu: the retro comic title splash (the "eyes" version from the Codex session) floats above the disk,
  with Start Run and How to Play buttons. Look at a button to focus it, then pinch with either hand.
  Controller trigger, Enter and H also work.
- A 5-second animated How to Play card before every run, retries included: a hand pinches the jeep's beacon and
  slides it left and right, and an eye sweeps a scout light over a dark fork to reveal trees and a fallen log.
  A fresh pinch skips it after the first 0.8 seconds. The world stays frozen until the run starts.

## 0.1.6

- Removed braking entirely: the brake plate, the palm-down gesture, and the keyboard and controller brake.
- Powerups start at about 120 m and stay rare early on, becoming more common through a run.
- Coins are collected by driving through them, laid in short trails along the road. Gaze no longer affects them.
- Cliff roads from about 360 m: the road becomes a thin strip over a black abyss. Each cliff road is narrower
  than the last, from 6.0 to 2.5 units wide. Going over the edge ends the run; a shield doesn't help.
- The disk's top is open so drops are visible, and the scout preview shows drops as dark gaps.

## 0.1.5

- Every powerup stare takes 1.5 seconds, including the shield and flashlight.
- Smoother gaze: speed-adaptive filtering, gaze past the range or above the horizon follows the edge of the
  range instead of freezing, and lost or stale eye data falls back to head aim after 0.5 seconds.
- Pickups stay until collected or until they pass behind the disk.
- Cruise speed rises by 5 km/h every 100 m, up to 120 km/h, with a speed readout on the panel.
- Gaze quality is logged to logcat every 5 seconds.

## 0.1.4

- Scout light radius increased from 5.5 to 8.5 units.
- Powerups placed off the road on the terrain beside it.
- Dead-end walls have a narrow, marked gap leading to a crawl-speed escape track.
- Zombies stay on the road surface and divide correctly at splits.

## 0.1.3

- Open road with smooth steering in place of three fixed lanes.
- Either hand can steer, sweep or brake.
- Curving road; curves push the jeep outward and scraping an edge slows it.
- Gaze-collected coins and three gaze powerups: booster, shield and flashlight.
- Split roads: early ones with an obstacle branch, later ones with walled dead ends.
- Fallen-tree stall shortened from 2.2 to 1.2 seconds.
- Collision on curves compares road-relative positions.
- Scout preview shows the road brighter than the forest.

## 0.1.2

- Camera passthrough background with a 30% black tint.
- PICO hand models, with their pointer rays hidden.

## 0.1.1

- First release build of the first playable: floating forest disk, procedural road, jeep, horde,
  eye-gaze scouting, left-hand lane changes, sweeps and palm brake.
