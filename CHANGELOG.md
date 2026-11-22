# Last Light changelog

Builds are release APKs for PICO Swan (Android, ARM64), package `com.lastlight.prototype`.
Version history before git: 0.1.1 to 0.1.5 were built and installed but not committed, so the first
commit holds the 0.1.6 source.

## 0.1.23

- The loose rocks that hung under and round the island are gone; they floated, unattached, in every view.
- Vehicle paint jobs now replace the body colour instead of tinting the whole model. Each vehicle knows its own
  body colour (Scout Jeep ochre, Hauler teal, Rhino maroon, Angel off-white); only texels close to it take the new
  colour, keeping their light and shade. Tyres, glass, lights, wheel hubs, trim and the Hauler's cream stripes, the
  Rhino's gold stripes and the Angel's red stripe and heartbeat logo keep their own colours. Very dark paints
  (Charcoal, Night, Night Navy) are held at a deep shade so the panels still read under the night lighting.

## 0.1.22

Everything in 0.1.21, plus the Tripo work from `feature/tripo-props-wired`, which 0.1.21 was missing:

- The new Scout Jeep made from its model sheet is the starting vehicle (vehicle slot 0), replacing the old
  roll-cage jeep.
- Garage: the turntable is a smooth steel disc, with the holes in the platform closed.
- Fix: garage vehicles now spin round the centre of the turntable. The garage model is centred on its outline,
  but its turntable sits about 1.25 units toward the open front, so every vehicle orbited off-centre.
- The Tripo props are in the game: hazards, cliff rock, coins, gear and powerup pickups.
- New Tripo roof turret, modelled on the turret icon.
- The plough sits fully ahead of every vehicle's bumper without clipping.
- Smoother, blended terrain with hand-painted textures to match the Tripo models.
- Storage compartment items trigger after a 0.5 s stare instead of 1 s. The wrist MENU button still takes a
  full second, so a passing glance never pauses the run.

## 0.1.21

- Wrist MENU button: during a run, turn either palm towards your face and a small MENU button floats over that
  wrist, like checking a watch. It shows on both wrists if both palms face you, and hides when the palm turns away
  or that hand pinches. Look at it for a second (it fills left to right as you look; glancing away resets it) to
  pause. It's a look rather than a pinch because pinching during a run grabs the steering beacon.
- Pause menu: the run freezes and a PAUSED panel floats up in front of you, following your head like the run-over
  panel, showing the distance and coins so far. Look at a button and pinch, as on the main menu: Resume carries on,
  Restart starts a fresh run, Main Menu goes back to the title. The dashboard steps aside while it's open, and the
  music drops to its paused level. A pinch in the first third of a second after it opens is ignored.
- Editor and controllers: Escape or Tab opens the pause menu (Escape again resumes); a controller's menu button
  does the same.

## 0.1.20

- Snow and ice only rolls in after 1,600 m; before that the weather is fog or storm.
- Flashlights are much rarer: 12% of powerups in every stage, down from about 22%. Repairs stay as likely as
  flashlights, so they're rarer too; boosters and shovels make up the difference. One flashlight still waits
  before the weather starts, as the answer to fog.
- Storage countdown, fixed: the ring and the radial fill now draw above the slot's backing plate and the item badge
  (they were drawn underneath, so on the headset you couldn't see them). The fill is a bright white wipe so it
  reads on any badge colour.
- Repair QTE timer: each dot gives you 2.5 seconds, shown as a thin ring running down round it (flashing near the
  end). Leave a dot too long and the repair fails: the wrench comes back and you start again.
- Fix: the Evasion trait's roll could repeat the same result run after run with a pinned seed.

## 0.1.19

- Every run now builds a fresh random road: bends, splits, cliff roads, hazards, powerups, spitters and weather.
  Until now the scene had "Fixed Seed" on (seed 1), so every run was the same road; and even with it off, each
  launch replayed the same series. Runs now draw a random seed, seeded from the clock at start-up. The headless
  test still pins seed 1 so its checks stay comparable.
- New speed boost icon: the booster now has its comic badge too, beside the road, in storage and in the shop.

## 0.1.18

- New trait, Recovery: after crashing into an obstacle or being hit by spitter acid, the jeep gets back up to speed
  15% faster at level 1 (500 coins) and 35% faster at level 2 (1,300), and the acid's slow-down wears off that much
  sooner. It's the fourth column on the garage's Traits panel and carries over like the other traits.

## 0.1.17

- Vehicles now also need a long enough best run before they can be bought: Hauler Pickup 800 m, Rhino APC 1,500 m,
  Angel Transponder 3,000 m. Until then the garage card shows "REACH 800 M" (and so on) instead of the price, the
  Unlock button shows the target and stays off, and the blurb shows your best run. Your existing best carries over.

## 0.1.16

- Storage: while you look at a stored item, a radial countdown fill sweeps clockwise across it, in the item's colour.
- Three-way splits: when all three branches are barricaded, one of them now has only half a barricade, leaving a
  narrow gap the jeep can squeeze through without damage.
- Turret kills on spitters pay 20 coins, like rock hits, with the same gold "+20".
- New turret icon.
- Scraping a road edge throws orange sparks off that side of the jeep.
- Rock throws: the stare at a spitter is now 0.7 s (was 1 s).

## 0.1.15

- Tripo models: the Scout Jeep, Hauler Pickup, Rhino APC and Angel Transponder are now their Tripo models in runs,
  on the garage turntable and on the garage cards, in place of the box placeholders. Each is fitted to the old
  footprint, facing up the road, with the turret on its roof and the shovel on its front bumper. The garage is now
  the Tripo diorama (a floating octagonal workshop with its own turntable), open side toward you.
- Paint: each vehicle's first colour is its own Tripo paint; the others repaint the model's light and shade in the
  swatch colour. The garage's scan-line swap works on the textured models too.
- The project moved out of iCloud Drive to ~/Developer/LastLightXR, which also fixed the APK doubling in size from
  iCloud's duplicate files (0.1.14 is now 68 MB, down from 185 MB).
- Builds refuse to run if PICO hand tracking, eye tracking or passthrough has been switched off in the project
  settings, and check the APK still asks for hand and eye tracking.

## 0.1.14

- Powerup icons: the shovel, turret, shield, flashlight and repairs powerups now show the new comic-style badges
  (round, retro American comic look) instead of the 3D shapes: floating beside the road, where the badges turn to
  face you, in the dashboard storage slots, and in the garage shop. The speed boost has no badge yet and keeps its
  3D chevrons. The repair QTE shows the new wrench badge over the jeep.

## 0.1.13

- Three-way splits replace the three-lane main road from 0.1.12, which is gone. From the medium stage a split can
  open into three branches round two wooded islands, with barricades across some of them: one of the three until
  650 m, usually two after that, and from 1 km often all three, so only a shovel, a shield or a crash gets through.
- Shovel: now also smashes a split road's dead-end wall. The jeep carries on through the escape track at full
  speed instead of crawling. It still smashes barricades, rocks and trees.
- Powerup stares: 1 second at the start of a run, 0.05 s quicker every 150 m, never under 0.5 s (reached at 1.5 km).
- Storm branches: more of them (every 1.9 s or so, down to 0.9 s late on), bigger, cartwheeling and hopping as they
  go, and each is timed to cross the road on the disk just ahead of the jeep, with a whoosh. Before, they mostly
  passed behind the jeep while still at the roadside.
- Repair QTE: the wrench is about 40% smaller and sits lower, and the three dots appear closer round the jeep.
- Rock hits: knocking a spitter down with a thrown rock pays 20 coins, with a gold "+20" rising over it.
- Respawn cost: the first paid respawn in a run costs 100, the next 2x that (200), then 3x the last (600), then 4x
  (2,400), 5x (12,000) and so on. The Angel Transponder's free respawn doesn't count toward the climb.
- Fix: a jeep trapped at a dead-end wall right after a booster could get stuck for good, because the cancelled boost
  eased it back off the wall where lining up with the gap no longer freed it. Lining up now frees it anywhere just
  short of the wall.

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
- Three-lane stretches from 300 m (removed again in 0.1.13): dashed lane lines and a row of barricades. In the medium stage one lane is shut
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
