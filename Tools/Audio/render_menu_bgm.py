#!/usr/bin/env python3
"""
Renders Last Light's main menu BGM loop: "Last Light (Dusk)".

A seamless 32-bar loop in E minor, 3/4 time at 84 BPM (68.6 s). 84 BPM gives exactly 31500 samples per beat at
44.1 kHz, so the loop length is exact. Where the run loop is a chase, this one is the calm before it: a felt
piano lullaby in waltz time, a music-box bell, a bowed low string and a wide pad, with its own melody.

The melody (bars 8-15), over Em C G D | Am Em B B:
  B4. A4 G4 | E5 - D5 | D5 B4 G4 | A4 - - | C5. B4 A4 | G4 B4 E5 | F#5 - D#5 | B4 - -
It rises to F#5 over B major and comes to rest a fifth above the tonic, so it never quite settles.

Form (8 bars each):
  A  bars  0-7   Em C G D | Am Em B B   piano arpeggios and pad; the bell hints at the melody's second half
  B  bars  8-15  same                   the melody on piano, bowed bass enters
  C  bars 16-23  C G D Em | C G B B     the melody's answer, an octave up, doubled by a soft string; bell counterline
  D  bars 24-31  Em C G D | Am Em B B   the bell recalls the opening, then just piano, with a faint heartbeat
The B major chord at bar 31 resolves to Em at bar 0, so the loop seam lands on the home chord.

Needs numpy and scipy:  python3 Tools/Audio/render_menu_bgm.py [out.wav]
Default output: Assets/LastLight/Resources/LastLightAudio/LL_BGM_Menu.wav
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from synth import *  # noqa: E402,F401,F403
import synth  # noqa: E402

synth.reseed(2084)

BPM = 84
BEAT = SR * 60 // BPM          # 31500 samples
BAR = BEAT * 3                 # 3/4
BARS = 32
N = BAR * BARS
EIGHTH = BEAT // 2
mix = Mix(N, tail=SR * 7)

CHORDS = {  # bass root and chord tones; `third` is the interval above the root for the arpeggio
    "Em": ("E2", ["E", "G", "B"], 3),
    "C":  ("C2", ["C", "E", "G"], 4),
    "G":  ("G1", ["G", "B", "D"], 4),
    "D":  ("D2", ["D", "F#", "A"], 4),
    "Am": ("A1", ["A", "C", "E"], 3),
    "B":  ("B1", ["B", "D#", "F#"], 4),
}
PROG_MAIN = ["Em", "C", "G", "D", "Am", "Em", "B", "B"]
PROG_C = ["C", "G", "D", "Em", "C", "G", "B", "B"]


def chord_at(bar):
    return (PROG_C if 16 <= bar < 24 else PROG_MAIN)[bar % 8]


def at(bar, beat=0.0):
    return bar * BAR + int(round(beat * BEAT))


# The melody and its answer: (note, start beat within the 8-bar phrase, length in beats).
THEME = [
    ("B4", 0, 1.5), ("A4", 1.5, 0.5), ("G4", 2, 1),
    ("E5", 3, 2), ("D5", 5, 1),
    ("D5", 6, 1), ("B4", 7, 1), ("G4", 8, 1),
    ("A4", 9, 3),
    ("C5", 12, 1.5), ("B4", 13.5, 0.5), ("A4", 14, 1),
    ("G4", 15, 1), ("B4", 16, 1), ("E5", 17, 1),
    ("F#5", 18, 2), ("D#5", 20, 1),
    ("B4", 21, 3),
]
ANSWER = [
    ("G5", 0, 1.5), ("F#5", 1.5, 0.5), ("E5", 2, 1),
    ("D5", 3, 2), ("B4", 5, 1),
    ("A4", 6, 1), ("D5", 7, 1), ("F#5", 8, 1),
    ("E5", 9, 3),
    ("E5", 12, 1.5), ("D5", 13.5, 0.5), ("C5", 14, 1),
    ("B4", 15, 1), ("D5", 16, 1), ("G5", 17, 1),
    ("F#5", 18, 2), ("A5", 20, 1),
    ("F#5", 21, 2), ("D#5", 23, 1),
]


def play(phrase, first_bar, voice, shift=0, beats=None):
    """Places a phrase starting at `first_bar`. `voice(m, length_samples, beat_in_phrase)` places one note."""
    for name, beat, length in phrase:
        if beats is not None and not (beats[0] <= beat < beats[1]):
            continue
        voice(midi(name) + shift, int(length * BEAT), first_bar * BAR + int(round(beat * BEAT)), beat)


# ---------------------------------------------------------------- piano arpeggios (every bar)
for bar in range(BARS):
    ch = chord_at(bar)
    root_name, tones, third = CHORDS[ch]
    root = midi(root_name)
    sec = bar // 8
    r3 = root + 12 if root >= midi("C2") else root + 24     # arpeggio root around E3
    pattern = [0, 7, 12, 12 + third, 12 + 7, 12]
    level = {0: 0.5, 1: 0.42, 2: 0.4, 3: 0.5}[sec]
    if sec == 3 and bar >= 28:
        level = 0.46 - 0.02 * (bar - 28)
    # Left hand: the bass note on the downbeat, held through the bar.
    mix.place(piano_note(root, BAR - 600, vel=level * 1.15), at(bar), pan=-0.25, rev=0.4)
    for i, iv in enumerate(pattern):
        if i == 0:
            continue  # the left hand has the root
        ring = BAR - i * EIGHTH + EIGHTH   # notes ring on, as with the sustain pedal, to just past the bar
        vel = level * (0.85 if i in (2, 3) else 0.7)
        mix.place(piano_note(r3 + iv, ring, vel=vel), at(bar, i * 0.5), pan=-0.1 + 0.08 * i, rev=0.45)

# ---------------------------------------------------------------- pad (every two bars, louder in A and D)
bar = 0
while bar < BARS:
    ch = chord_at(bar)
    span = 1
    while bar + span < BARS and chord_at(bar + span) == ch and (bar + span) % 8 != 0:
        span += 1   # a repeated chord (B B) is held, not struck twice
    sec = bar // 8
    gain = {0: 0.55, 1: 0.42, 2: 0.6, 3: 0.55}[sec]
    cutoff = {0: 1100, 1: 1300, 2: 1900, 3: 1000}[sec]
    notes = voicing(CHORDS[ch][1], midi("G3"))
    mix.place(pad_chord(notes, BAR * span, cutoff, attack=0.7, release=1.0), at(bar), gain=gain, rev=0.6)
    bar += span

# ---------------------------------------------------------------- bowed bass (B and C)
for bar in range(8, 24):
    root = midi(CHORDS[chord_at(bar)][0])
    if root < midi("C2"):
        root += 12
    mix.place(cello_note(root, BAR - 1500, vel=0.85), at(bar), pan=-0.2, rev=0.35)

# ---------------------------------------------------------------- melody
def piano_voice(vel):
    return lambda m, length, start, beat: mix.place(piano_note(m, length - 400, vel=vel), start, pan=0.12, rev=0.5)


def bell_voice(vel, pan=0.35):
    return lambda m, length, start, beat: mix.place(celesta_note(m, vel), start, pan=pan, rev=0.6)


def string_voice(vel):
    return lambda m, length, start, beat: mix.place(lead_note(m, length - 600, vel=vel), start, pan=-0.15, rev=0.6)


# A: the bell hints at the second half of the theme, an octave up, quietly.
play(THEME, 0, bell_voice(0.55), shift=12, beats=(12, 24))
# B: the theme on piano.
play(THEME, 8, piano_voice(0.82))
# C: the answer on piano, doubled softly by a string an octave below; the bell rings a counterline of
#    chord thirds on beat two.
play(ANSWER, 16, piano_voice(0.8))
play(ANSWER, 16, string_voice(0.45), shift=-12)
for bar in range(16, 24):
    _, tones, _ = CHORDS[chord_at(bar)]
    third = voicing([tones[1]], midi("C6"))[0]
    mix.place(celesta_note(third, 0.4), at(bar, 1), pan=0.5, rev=0.6)
    if bar % 2 == 1:
        fifth = voicing([tones[2]], midi("C6"))[0]
        mix.place(celesta_note(fifth, 0.3), at(bar, 2.5), pan=0.55, rev=0.6)
# D: the bell recalls the opening of the theme.
play(THEME, 24, bell_voice(0.6, pan=0.25), shift=12, beats=(0, 12))

# A faint heartbeat under the last four bars: a hint of what waits on the road.
for bar in range(28, 32):
    vel = 0.18 + 0.03 * (bar - 28)
    mix.place(lowpass(kick(vel), 400), at(bar), gain=0.9)
    mix.place(lowpass(kick(vel * 0.65), 400), at(bar, 0.5), gain=0.9)

# ---------------------------------------------------------------- atmosphere
drone, wind = drone_and_wind(N, mix.tail, root="E1", fifth="B1", drone_gain=0.07, wind_gain=0.04,
                             wind_band=(200, 1200))
mix.place(drone, 0)
mix.place(wind, 0, rev=0.35)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "LastLight", "Resources", "LastLightAudio", "LL_BGM_Menu.wav")
write(out, mix.master(rt60=3.2, ir_seconds=3.6, wet_gain=0.7, ir_lowpass=4500, drive=1.05))
print(f"Wrote {os.path.abspath(out)}: {N / SR:.2f} s, {N} samples, {BARS} bars of 3/4 at {BPM} BPM")
