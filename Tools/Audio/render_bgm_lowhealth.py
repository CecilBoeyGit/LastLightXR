#!/usr/bin/env python3
"""
Renders the run loop's low-health variant: "Last Light (Last Bar)", played when the jeep has one bar of health.

Same tempo, length and chords as the run loop (run_theme.py), so the game can play both in sync and crossfade
between them at any moment. Where the run loop drives, this one holds its breath: a racing muffled heartbeat,
a clock ticking every beat, a sustained sub, a dark pad with a minor second rubbing against it, a faint high
ringing, the arpeggio muted to 8ths, and the run's lead melody turned fragile on a music-box bell.
Every element keeps to the same chord bar for bar, so the crossfade never clashes.

Needs numpy and scipy:  python3 Tools/Audio/render_bgm_lowhealth.py [out.wav]
Default output: Assets/LastLight/Resources/LastLightAudio/LL_BGM_LowHealth.wav
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from synth import *  # noqa: E402,F401,F403
import synth  # noqa: E402
from run_theme import *  # noqa: E402,F401,F403

synth.reseed(3101)
mix = Mix(N)


def tick(vel=1.0, tock=False):
    """A clock tick: a very short bandpassed click; the tock sits lower."""
    n = int(0.035 * SR)
    t = np.arange(n) / SR
    lo, hi = (1400, 3200) if tock else (2600, 6500)
    return bandpass(synth.rng.standard_normal(n), lo, hi) * np.exp(-t * 260) * 0.55 * vel


def heartbeat(vel=1.0):
    return lowpass(kick(vel), 320, 2) * 1.3


# Heartbeat: lub-dub twice a bar, about 105 beats a minute, like a pulse under stress.
for bar in range(BARS):
    for s0 in (0, 8):
        mix.place(heartbeat(0.95), at(bar, s0), gain=1.0)
        mix.place(heartbeat(0.6), at(bar, s0 + 3), gain=1.0)

# A clock ticking every beat, tick left and tock right.
for bar in range(BARS):
    for beat in range(4):
        mix.place(tick(0.55 if beat % 2 == 0 else 0.45, tock=beat % 2 == 1), at(bar, beat * 4),
                  pan=-0.6 if beat % 2 == 0 else 0.6, rev=0.15)

# Sustained sub under each chord, and the dark pad with a minor second rubbing on top of the root
# (b2 over Dm, b9 over A): the sound of something about to give.
for bar in range(0, BARS, 2):
    ch = chord_at(bar)
    root = midi(CHORDS[ch][0])
    mix.place(sub_note(root, BAR * 2 - STEP), at(bar), gain=1.05)
    notes = voicing(CHORDS[ch][1], midi("D3"))
    mix.place(pad_chord(notes, BAR * 2, cutoff=850), at(bar), gain=0.95, rev=0.5)
    rub = voicing([CHORDS[ch][1][0]], midi("D4"))[0] + 1
    mix.place(lead_note(rub, BAR * 2 - STEP, vel=0.22), at(bar), pan=0.3, rev=0.6)

# The arpeggio, muted to 8ths and pulled low and dark.
ARP8 = [0, 1, 2, 1, 3, 2, 1, 2]
for bar in range(BARS):
    tones = voicing(CHORDS[chord_at(bar)][1], midi("A3"))
    tones = tones + [tones[0] + 12]
    for i in range(8):
        note = lowpass(arp_note(tones[ARP8[i]], 0.55 if i % 4 == 0 else 0.38), 1300, 2)
        mix.place(note, at(bar, i * 2), pan=-0.4 if i % 2 == 0 else 0.4, rev=0.4)

# The run's lead melody, fragile on a bell, where the run loop has it (bars 16-23); its opening phrase drifts
# in once more over bars 0-3 and 24-27, quieter.
for name, beat, length in LEAD:
    mix.place(celesta_note(midi(name), 0.75), at(16) + int(beat * BEAT), pan=0.15, rev=0.6)
for first in (0, 24):
    for name, beat, length in LEAD[:6]:
        mix.place(celesta_note(midi(name), 0.45), at(first) + int(beat * BEAT), pan=-0.2, rev=0.7)

# A faint high ringing (A6) that swells and fades eight times a loop, so it loops cleanly.
t = np.arange(N + mix.tail) / SR
swell = 0.5 - 0.5 * np.cos(2 * np.pi * 8 * t / (N / SR))
ring = np.sin(2 * np.pi * hz(midi("A6")) * t) * swell ** 2 * 0.012
mix.place(ring, 0, pan=0.0, rev=0.2)

drone, wind = drone_and_wind(N, mix.tail, drone_gain=0.14, wind_gain=0.06, wind_band=(200, 1100))
mix.place(drone, 0)
mix.place(wind, 0, rev=0.3)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "LastLight", "Resources", "LastLightAudio",
    "LL_BGM_LowHealth.wav")
write(out, mix.master(rt60=2.6, ir_seconds=3.0, wet_gain=0.6, ir_lowpass=4200, drive=1.1))
print(f"Wrote {os.path.abspath(out)}: {N / SR:.2f} s, {N} samples, {BARS} bars at {BPM} BPM")
