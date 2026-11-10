# Last Light: changes since 0.1.1

All changes from build 0.1.1 to build 0.1.6, by version. Where a later version replaced something, the note says so.

## 0.1.2: mixed reality and hands

* The black background is now the headset's camera passthrough, tinted 30% black.
* PICO's own hand models now show your hands, with their pointer rays turned off.

## 0.1.3: gameplay redesign

* The three fixed lanes are gone. The jeep now moves smoothly anywhere across an open road, following the beacon as you drag it.
* Either hand can now grab the beacon, sweep rocks or brake. Braking was removed in 0.1.6.
* The road now curves. Curves push the jeep outward, and scraping an edge slows it to 60% speed.
* Coins appeared, collected by staring at them for 1.5 seconds. Changed in 0.1.6.
* Three gaze powerups appeared:

  * **Booster:** moves the jeep forward for 5 seconds.
  * **Shield:** cancels one hit, or one catch by the horde.
  * **Flashlight:** reveals the scouting region for 5 seconds.
* Split roads appeared. Early ones have an obstacle branch and a clear branch. Later ones can hide a walled dead end that traps the jeep.
* Fallen trees now stall the jeep for 1.2 seconds instead of 2.2.
* Collision on curves is fixed, so trees no longer hit a jeep that is lined up to pass.
* The scout preview now shows the road brighter than the forest.

## 0.1.4: scouting, dead ends and zombies

* The scout light's radius grew from 5.5 to 8.5 units.
* Powerups moved off the road onto the terrain beside it.
* Dead ends got a narrow, marked gap leading to a crawl-speed escape track. The crawl costs lead against the horde.
* Zombies now stay on the road surface and divide correctly at splits.

## 0.1.5: gaze, pickups and speed

* All powerup stares now take 1.5 seconds, including the shield and flashlight.
* The gaze light is smoother:

  * **Filter:** new filtering that holds steady during a stare and moves quickly on a glance.
  * **Out-of-range gaze:** looking past the range or above the horizon no longer freezes the light.
  * **Lost eye data:** the light falls back to head aim after 0.5 seconds without eye data.
* Pickups now stay until collected or until they pass behind the disk, so you can still collect them after the jeep passes.
* Speed rises by 5 km/h every 100 m, up to 120 km/h, with a speed readout on the panel.
* The game logs gaze quality to the headset every 5 seconds, for troubleshooting.

## 0.1.6: cliffs and coins

* The brake is gone: plate, palm gesture, keyboard and controller.
* Powerups now start at about 120 m and stay rare early on.
* Coins are now collected by driving through them, laid in trails along the road. Gaze no longer affects them.
* Cliff roads now appear later in a run, starting from about 360 m:

  * **The drop:** the road becomes a thin strip over a black abyss.
  * **Narrowing:** each cliff road is narrower than the last, from 6.0 down to 2.5 units wide.
  * **Falling:** going over the edge ends the run, and a shield doesn't save you.

