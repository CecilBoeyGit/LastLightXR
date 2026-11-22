"""
The run family's shared score facts: tempo, loop length, chords and the lead melody.

The run loop (render_bgm.py) and its two danger variants (render_bgm_lowhealth.py, render_bgm_horde.py) all use
these, so the three loops are exactly the same length with the same chords bar for bar. The game plays them
together in sync and crossfades between them.
"""
from synth import SR

BPM = 105
BEAT = SR * 60 // BPM          # 25200 samples
BAR = BEAT * 4
BARS = 32
N = BAR * BARS                 # loop length in samples
STEP = BEAT // 4               # a 16th note

CHORDS = {  # root (bass octave) and triad tones (for pad and arp)
    "Dm": ("D2", ["D", "F", "A"]),
    "Bb": ("Bb1", ["Bb", "D", "F"]),
    "Gm": ("G1", ["G", "Bb", "D"]),
    "A":  ("A1", ["A", "C#", "E"]),
    "F":  ("F1", ["F", "A", "C"]),
}
PROG_MAIN = ["Dm", "Bb", "Gm", "A"]
PROG_C = ["Bb", "F", "Gm", "A"]


def chord_at(bar):
    prog = PROG_C if 16 <= bar < 24 else PROG_MAIN
    return prog[(bar % 8) // 2]


def at(bar, step=0):
    return bar * BAR + step * STEP


# Lead melody over C (bars 16-23): (note, start beat within the section, length in beats)
LEAD = [
    ("F5", 0, 2), ("D5", 2, 1), ("Bb4", 3, 1), ("C5", 4, 1.5), ("D5", 5.5, 0.5), ("F5", 6, 2),
    ("A5", 8, 3), ("G5", 11, 1), ("F5", 12, 2), ("E5", 14, 1), ("C5", 15, 1),
    ("D5", 16, 2), ("Bb4", 18, 1), ("D5", 19, 1), ("G5", 20, 3), ("F5", 23, 1),
    ("E5", 24, 2), ("C#5", 26, 1), ("E5", 27, 1), ("A5", 28, 4),
]
