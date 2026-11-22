#!/usr/bin/env python3
"""
Renders Last Light's in-game BGM loop: "Last Light (Run)".

A seamless 32-bar loop in D minor at 105 BPM (73.1 s). 105 BPM gives exactly 25200 samples per beat at 44.1 kHz,
so every note lands on a whole sample and the loop length is exact. Anything that rings past the last bar
(reverb, pad releases, the final fill) is folded back onto the start, so the seam is inaudible.

Form (8 bars each, the progression changes for C):
  A  bars  0-7   Dm Bb Gm A   heartbeat kick, bass pulse opening up, pad, offbeat hats from bar 4
  B  bars  8-15  Dm Bb Gm A   full drums, 16th arp enters, snare roll into C
  C  bars 16-23  Bb F  Gm A   peak: lead melody on top, tom fill into D
  D  bars 24-31  Dm Bb Gm A   breakdown (pad, arp, sub), then the heartbeat and pulse return
The loop seam lands on medium energy (end of D into A), and the A chord at bar 31 resolves to Dm at bar 0.

Needs numpy and scipy:  python3 Tools/Audio/render_bgm.py [out.wav]
Default output: Assets/LastLight/Resources/LastLightAudio/LL_BGM_Run.wav
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from synth import *  # noqa: E402,F401,F403
import synth  # noqa: E402

synth.reseed(1105)

from run_theme import *  # noqa: E402,F401,F403

mix = Mix(N)


# ---------------------------------------------------------------- the score
# Drums
for bar in range(BARS):
    sec = bar // 8
    if sec == 0 or (sec == 3 and bar >= 28):
        # Heartbeat: lub-dub on 1, again on 3 in the second half of A.
        mix.place(kick(0.85), at(bar, 0), gain=0.95)
        mix.place(kick(0.55), at(bar, 3), gain=0.95)
        if bar % 8 >= 4 or bar >= 28:
            mix.place(kick(0.6), at(bar, 8), gain=0.95)
            mix.place(kick(0.4), at(bar, 11), gain=0.95)
        if bar % 8 >= 4 or bar >= 28:
            for s in (2, 6, 10, 14):
                mix.place(hat(0.6), at(bar, s), pan=0.35, rev=0.1)
        if bar >= 30:
            mix.place(snare(0.35), at(bar, 12), pan=-0.05, rev=0.25)
    elif sec in (1, 2):
        kicks = [0, 8, 10] if sec == 1 else [0, 3, 8, 10, 11]
        for s in kicks:
            mix.place(kick(1.0 if s in (0, 8) else 0.7), at(bar, s))
        fill = (bar == 15) or (bar == 23)
        for s in (4, 12):
            if not (fill and s == 12):
                mix.place(snare(1.0), at(bar, s), pan=-0.05, rev=0.22)
        for s in range(0, 16, 2):
            mix.place(hat(0.85 if s % 4 == 2 else 0.6, open_=(s % 8 == 6 and sec == 2)), at(bar, s), pan=0.35, rev=0.08)
        for s in (3, 7, 11, 15):
            mix.place(hat(0.3), at(bar, s), pan=0.4)
        if bar == 15:  # snare roll into C
            for s in range(8, 16):
                mix.place(snare(0.35 + 0.08 * (s - 8)), at(bar, s), pan=-0.05, rev=0.22)
        if bar == 23:  # tom fill into the breakdown
            for s, m in zip(range(8, 16), ["A3", "A3", "F3", "F3", "D3", "D3", "A2", "A2"]):
                mix.place(tom(midi(m), 0.6 + 0.04 * (s - 8)), at(bar, s), pan=0.4 - 0.1 * (s - 8), rev=0.2)
    else:
        # Breakdown bars 24-27: just a distant floor tom on 1.
        mix.place(tom(midi("D2"), 0.45), at(bar, 0), pan=-0.2, rev=0.5)
for bar in (8, 16):
    mix.place(crash(1.0), at(bar), rev=0.25)
mix.place(crash(0.55), at(0), rev=0.3)
mix.place(riser(BAR * 2), at(30), pan=0.0, gain=0.8, rev=0.3)

# Bass
for bar in range(BARS):
    root = midi(CHORDS[chord_at(bar)][0])
    sec = bar // 8
    if sec == 3 and bar < 28:
        if bar % 2 == 0:
            mix.place(sub_note(root, BAR * 2 - STEP), at(bar), gain=0.9)
        continue
    if sec == 0:
        bright = 0.15 + 0.6 * (bar / 7)
    elif sec == 3:
        bright = 0.3 + 0.12 * (bar - 28)
    else:
        bright = 0.85 if sec == 1 else 1.0
    pattern = [0, 0, 12, 0, 0, 12, 0, 7] if bar % 2 == 0 else [0, 0, 12, 0, 0, 12, 7, 12]
    for i, iv in enumerate(pattern):
        vel = 1.0 if i in (0, 4) else 0.78
        mix.place(bass_note(root + iv, STEP * 2 - 600, brightness=bright, vel=vel), at(bar, i * 2), pan=0.0, gain=0.95)

# Pad
for bar in range(0, BARS, 2):
    ch = chord_at(bar)
    notes = voicing(CHORDS[ch][1], midi("D3"))
    notes.append(notes[0] + 12)
    sec = bar // 8
    gain = {0: 0.85, 1: 0.6, 2: 0.6, 3: 1.05}[sec]
    cutoff = {0: 1300, 1: 2000, 2: 2400, 3: 1600}[sec]
    mix.place(pad_chord(notes, BAR * 2, cutoff), at(bar), gain=gain, rev=0.45)

# Arp: 16ths across bars 8-31 (filtered back in the breakdown), ping-ponging left and right.
ARP_SHAPE = [0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 4, 3, 2, 1]
for bar in range(8, BARS):
    ch = chord_at(bar)
    tones = voicing(CHORDS[ch][1], midi("A3"))
    tones = tones + [tones[0] + 12, tones[1] + 12]
    sec = bar // 8
    level = {1: 0.8, 2: 0.9, 3: 0.75}[sec]
    for s in range(16):
        m = tones[ARP_SHAPE[s]]
        vel = level * (1.0 if s % 4 == 0 else 0.7)
        pan = -0.55 if s % 2 == 0 else 0.55
        mix.place(arp_note(m, vel), at(bar, s), pan=pan, rev=0.35)

for name, beat, length in LEAD:
    start = at(16) + int(beat * BEAT)
    mix.place(lead_note(midi(name), int(length * BEAT) - 800), start, pan=-0.1, rev=0.5)
# A quiet echo of the opening phrase in the breakdown, an octave down.
for name, beat, length in LEAD[:6]:
    m = midi(name) - 12
    mix.place(lead_note(m, int(length * BEAT) - 800, vel=0.5), at(24) + int(beat * BEAT), pan=0.25, rev=0.7)

drone, wind = drone_and_wind(N, mix.tail)
mix.place(drone, 0, gain=1.0)
mix.place(wind, 0, rev=0.3)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "LastLight", "Resources", "LastLightAudio", "LL_BGM_Run.wav")
write(out, mix.master())
print(f"Wrote {os.path.abspath(out)}: {N / SR:.2f} s, {N} samples, {BARS} bars at {BPM} BPM")
