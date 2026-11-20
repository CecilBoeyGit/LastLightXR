# Last Light changelog

Builds are release APKs for PICO Swan (Android, ARM64), package `com.lastlight.prototype`.
Version history before git: 0.1.1 to 0.1.5 were built and installed but not committed, so the first
commit holds the 0.1.6 source.

## 0.1.12

- Gaze timings: powerups still take 1.5 s of staring and stored items 1 s, but both now hold through a fifth of a
  second of eye jitter or a blink, and the target you're on keeps a wider margin, so the count stops restarting.
- Dashboard: the speedometer and the horde dial are gone. Health is now the big dial in the middle: a ring split
  into three segments that fill and drain radially, green at full, amber at two, pulsing red on the last bar. A
  smaller digital weather dial sits on its left, with an icon for the current weather (moon when clear, fog bars,
  storm cloud with lightning, snowflake), a ring that runs down with the time left, and the seconds. The storage
  compartment is built into the dash on the right as a 2 x 2 grid. Distance and the coin bank moved to the ends of
  the lamp strip; a HORDE CLOSE! alert replaces the horde dial's warning.
- Weather: starts later, from 520 m, with longer gaps between encounters (38-58 s, 28-42 s late on), and never the
  same weather twice in a row.
- Storm: every surface wet with puddles on the road. The jeep no longer slips; instead broken branches of three
  sizes blow across the road from either side and hit like fallen trees. They come every 3.2 s or so at first,
  down to every 1.3 s by about 1.7 km.
- Snow and ice, a third weather: falling snow settles white on everything, the road ices over and the jeep leaves
  tyre tracks. The slipperiness moved here: amplified steering, a slower-answering jeep and random skids.
- Flashlights are much more common (about 22% of powerups in every stage, up from 20-28% of a rarer roll), powerups
  overall are a little more common, and one flashlight always waits before the weather can start.
- Repairs powerup (green gear): gives back one health bar. As common as the flashlight. Also sold in the garage.
- Repair QTE: on the last health bar a wrench floats over the jeep. Look at it for half a second, then look at
  three dots that appear one at a time round the jeep, half a second each, to get a bar back.
- Garage shop: prices tripled (shovel 180, speed boost 120, turret 270, flashlight 150); repairs cost the most at 420.
- Storage: a fourth slot (900 coins), for sale once you own any vehicle past the Scout Jeep.
- Rhino APC starts every run (and respawn) with a shovel fitted, outside the compartment.
- New vehicle, Angel Transponder (2,500 coins, the most expensive): an emergency medical van whose first respawn in
  each run is free.
- Three-lane stretches from 300 m: dashed lane lines and a row of barricades. In the medium stage one lane is shut
  (sometimes two); late on two is usual, with a small chance all three are. Barricades hit like trees.
- Twin cliff roads from 600 m: two narrow strips over the abyss, one narrower than the other, joined halfway by a
  ridge where you can switch. The first is safe both ways; later ones usually have a strip that ends in a sheer
  drop (red posts and a striped board mark it, green chevrons point across at the ridge).
- Spitters: stare at one you can see for a second and the jeep hurls a rock that knocks it down.
- Traits panel in the garage (above the garage): Visibility (+15% / +30% gaze radius), Controls (15% / 50%
  steadier on snow) and Evasion (15% / 40% chance a crash without a shield or shovel costs no health). The second
  Controls and Evasion levels cost far more. Traits apply to the Scout Jeep for free; any other vehicle pays a
  one-time transfer fee (150 / 300 / 500), after which later upgrades carry over automatically.
- Editor keys: O snow, 4 uses the fourth stored item, X takes a hit of damage.
- Headless preview: checks and renders for all of the above.

## 0.1.11

- Dashboard: the rim gauges are replaced by a car-style instrument cluster floating upright beyond the far rim,
  at the top of your view and clear of the road and scouting region. A big speedometer with a needle and
  digital km/h readout sits in the middle; the left dial shows the horde's distance with three health pips;
  the right dial shows distance toward your best run with an inner ring of coins toward the next unlock.
  Warning lamps across the top light up for each active powerup (shield, boost, light, turret, shovel) and
  for fog or storm, with the time left.
- Jeep heading: the body now points along the road on curves (it used to turn only 70% of the way), so it
  lines up with the collision box and dodges look the way they play.
- Gaze: the dot floating over the scout light is hidden during runs, and the gaze is smoothed a little more,
  both the scout light and the gaze ray behind cursors, stares and the beacon.
- Tutorial: no time limit before a run. The steering and scouting card loops until you look at Start and
  pinch. How to Play from the menu alternates it with a new obstacles page (dodge rocks and logs, stay off a
  cliff road's edges) and has a Back button.
- Beacon: while held it sits right in your pinch, with a faint curved tether down to the jeep, and glides back
  over the jeep when let go. If the jeep is out of reach, stare at the beacon for half a second (a ring fills
  and it brightens), then pinch anywhere: it flies into your hand and follows it.
- Garage: picking another vehicle swaps the turntable model over about 2 seconds: a ragged cyan scan line
  climbs the old one and eats it away, then builds the new one, with a glowing ring riding the line.
- Weather, from 300 m: fog and storm encounters lasting a random 10 to 15 seconds, the first soon after the
  medium stage starts and closer together in the late stage. Fog rolls over the scouting region, so the scout
  light reveals nothing (the FOG lamp lights and the alert says scouting is blocked); a flashlight burns
  through it while it lasts. A storm brings rain, lightning and thunder, wet glossy surfaces, steering moves
  amplified by up to 70%, a slower-answering jeep and random skids left and right (gentler on cliff roads).
- Storage compartment: up to three slots on the player's right in a run, the first free, the second 300 and
  the third 600 coins. The garage's new Storage panel (left of the garage) unlocks slots in order and sells
  items: shovel 60, speed boost 40, turret 90, flashlight 50. Look at a stored item for a second in a run and it
  is used exactly like the powerup. Using one that's already active replaces it (the timer or blade starts
  fresh); different powerups stack.
- Editor keys: J fog, L storm, 1 to 3 use a stored item.
- Headless preview: checks for all of the above, plus renders of the dashboard, storage rack and panel,
  tutorial pages, beacon, garage swap, fog and storm.

## 0.1.10-test

- Textured road, dirt verges and grass, and the Tripo pines, cliff boulders and grass tufts along the road.

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
