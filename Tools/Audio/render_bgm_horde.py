#!/usr/bin/env python3
"""
Renders the run loop's horde-chase variant: "Last Light (Horde)", played when the horde is close behind.

Same tempo, length and chords as the run loop (run_theme.py), so the game can play both in sync and crossfade
between them at any moment. This one never lets up: kicks on every beat with big low drum hits, 16th hats,
a driven 16th bass, syncopated 3-3-2 chord stabs, a siren of rising semitones (A to Bb) and the run's lead
melody on a harsh square lead, with a riser into every eighth bar. It holds the same high energy all the way
round, so it never dips into a breakdown mid-chase.

Needs numpy and scipy:  python3 Tools/Audio/render_bgm_horde.py [out.wav]
Default output: Assets/LastLight/Resources/LastLightAudio/LL_BGM_Horde.wav
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from synth import *  # noqa: E402,F401,F403
import synth  # noqa: E402
from run_theme import *  # noqa: E402,F401,F403

synth.reseed(4107)
mix = Mix(N)


def stab(notes, vel=1.0):
    """A short, driven saw chord, like low brass hitting hard."""
    n = int(0.3 * SR)
    s = np.zeros(n)
    for i, m in enumerate(notes):
        s += additive(hz(m), n, lambda k: 1.0 / k if k <= 14 else 0.0, lambda k: 5 + 1.6 * k,
                      phase_seed=7000 + m)
    s *= np.clip(np.arange(n) / (0.002 * SR), 0, 1)
    return np.tanh(s / len(notes) * 2.4) * 0.3 * vel


def square_lead(m, length, vel=1.0):
    """A harsh square lead with a fast, nervous vibrato."""
    n = length + int(0.2 * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.006 * np.sin(2 * np.pi * 6.5 * t)
    sig = additive(hz(m), n, lambda k: (1.0 / k) if (k % 2 == 1 and k <= 11) else 0.0, lambda k: 0.0,
                   vib=vib, phase_seed=8000 + m)
    sig = lowpass(sig, 3800, 2)
    return sig * env_adsr(n, 0.01, 0.1, 0.8, 0.15, sustain_len=length) * 0.2 * vel


# Drums: four on the floor with a pickup, backbeat snare with ghosts, 16th hats, big low hits on 1 and 3,
# a tom fill and a crash every four bars.
for bar in range(BARS):
    fill = bar % 4 == 3
    for s in (0, 4, 8, 10, 12):
        if fill and s >= 12:
            continue
        mix.place(kick(1.0 if s % 4 == 0 else 0.7), at(bar, s))
    for s in (0, 8):
        mix.place(tom(midi("D2"), 0.65), at(bar, s), pan=-0.15, rev=0.3)
    for s in (4, 12):
        if not (fill and s == 12):
            mix.place(snare(1.0), at(bar, s), pan=-0.05, rev=0.2)
    for s in (7, 15):
        if not (fill and s == 15):
            mix.place(snare(0.25), at(bar, s), pan=-0.05)
    for s in range(16):
        if fill and s >= 12:
            continue
        mix.place(hat(0.8 if s % 4 == 2 else 0.45, open_=(s == 14)), at(bar, s), pan=0.35, rev=0.06)
    if fill:
        for s, m in zip(range(12, 16), ["A3", "F3", "D3", "A2"]):
            mix.place(tom(midi(m), 0.85), at(bar, s), pan=0.4 - 0.25 * (s - 12), rev=0.2)
    if bar % 4 == 0:
        mix.place(crash(0.85), at(bar), rev=0.25)

# Risers into every eighth bar.
for bar in (6, 14, 22, 30):
    mix.place(riser(BAR * 2), at(bar), gain=0.9, rev=0.3)

# Driven 16th bass.
BASS16 = [0, 0, 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 0, 12, 7, 12]
for bar in range(BARS):
    root = midi(CHORDS[chord_at(bar)][0])
    for s, iv in enumerate(BASS16):
        note = bass_note(root + iv, STEP - 300, brightness=1.0, vel=1.0 if s % 4 == 0 else 0.8)
        mix.place(np.tanh(note * 2.2) * 0.6, at(bar, s), gain=0.95)

# Chord stabs in a 3-3-2 pattern.
for bar in range(BARS):
    notes = voicing(CHORDS[chord_at(bar)][1], midi("D3"))
    for s in (0, 3, 6):
        mix.place(stab(notes, 1.0 if s == 0 else 0.8), at(bar, s), pan=0.0, rev=0.2)

# Bright 16th arp all the way through.
ARP_SHAPE = [0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 4, 3, 2, 1]
for bar in range(BARS):
    tones = voicing(CHORDS[chord_at(bar)][1], midi("D4"))
    tones = tones + [tones[0] + 12, tones[1] + 12]
    for s in range(16):
        mix.place(arp_note(tones[ARP_SHAPE[s]], 0.85 if s % 4 == 0 else 0.6), at(bar, s),
                  pan=-0.6 if s % 2 == 0 else 0.6, rev=0.25)

# A low pad to glue it, kept back.
for bar in range(0, BARS, 2):
    notes = voicing(CHORDS[chord_at(bar)][1], midi("D3"))
    mix.place(pad_chord(notes, BAR * 2, cutoff=1500), at(bar), gain=0.4, rev=0.4)

# The siren, A5 rising to Bb5, in the second half of each 8 bars outside C; the run's lead melody,
# harsh and an octave up for the first phrase, through C (bars 16-23).
for bar in range(BARS):
    if 16 <= bar < 24 or bar % 8 < 4:
        continue
    mix.place(square_lead(midi("A5"), BEAT * 2 - 1500, 0.8), at(bar, 0), pan=0.25, rev=0.35)
    mix.place(square_lead(midi("Bb5"), BEAT * 2 - 1500, 0.9), at(bar, 8), pan=0.25, rev=0.35)
for name, beat, length in LEAD:
    mix.place(square_lead(midi(name), int(length * BEAT) - 900), at(16) + int(beat * BEAT), pan=-0.1, rev=0.35)

drone, wind = drone_and_wind(N, mix.tail, drone_gain=0.1, wind_gain=0.04)
mix.place(drone, 0)
mix.place(wind, 0, rev=0.3)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "LastLight", "Resources", "LastLightAudio", "LL_BGM_Horde.wav")
write(out, mix.master(rt60=1.8, ir_seconds=2.2, wet_gain=0.4, ir_lowpass=5500, drive=1.25))
print(f"Wrote {os.path.abspath(out)}: {N / SR:.2f} s, {N} samples, {BARS} bars at {BPM} BPM")
