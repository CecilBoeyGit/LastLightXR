# Last Light changelog

Builds are release APKs for PICO Swan (Android, ARM64), package `com.lastlight.prototype`.
Version history before git: 0.1.1 to 0.1.5 were built and installed but not committed, so the first
commit holds the 0.1.6 source.

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
