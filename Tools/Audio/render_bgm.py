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

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
BPM = 105
BEAT = SR * 60 // BPM          # 25200 samples
BAR = BEAT * 4
BARS = 32
N = BAR * BARS                 # loop length in samples
TAIL = SR * 5                  # rendered past the end, then folded onto the start
STEP = BEAT // 4               # a 16th note

rng = np.random.default_rng(1105)

# Two buses: dry and the reverb send. Both stereo, [2, N + TAIL].
dry = np.zeros((2, N + TAIL))
send = np.zeros((2, N + TAIL))


# ---------------------------------------------------------------- notes and chords
NOTE = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6,
        "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}


def midi(name):
    """'D4' -> 62, 'Bb3' -> 58, 'C#5' -> 73."""
    pitch, octave = name[:-1], int(name[-1])
    return 12 * (octave + 1) + NOTE[pitch]


def hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


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


def voicing(chord, low):
    """Triad tones as midi notes, each the lowest at or above `low`."""
    out = []
    for p in CHORDS[chord][1]:
        m = low - (low % 12) + NOTE[p]
        while m < low:
            m += 12
        out.append(m)
    return sorted(out)


# ---------------------------------------------------------------- mixing helpers
def place(sig, start, pan=0.0, gain=1.0, rev=0.0):
    """Adds a mono or stereo signal at sample `start`, with equal-power pan and a reverb send."""
    if sig.ndim == 1:
        a = (pan + 1.0) * np.pi / 4.0
        sig = np.vstack([sig * np.cos(a), sig * np.sin(a)])
    end = min(start + sig.shape[1], N + TAIL)
    sig = sig[:, : end - start] * gain
    dry[:, start:end] += sig
    if rev > 0.0:
        send[:, start:end] += sig * rev


def env_adsr(n, a, d, s, r, sustain_len=None):
    """Linear ADSR in seconds. The note holds for `sustain_len` samples (default: until the release fits)."""
    a, d, r = int(a * SR), int(d * SR), int(r * SR)
    hold = (n - r) if sustain_len is None else sustain_len
    hold = max(hold, 1)
    e = np.full(n, s)
    t = np.arange(n)
    e[: min(a, n)] = np.linspace(0, 1, a)[: min(a, n)] if a > 0 else 1.0
    da = slice(a, min(a + d, n))
    if a + d > a:
        e[da] = np.linspace(1, s, d)[: da.stop - da.start]
    rel = t >= hold
    if rel.any():
        level = e[hold - 1] if hold - 1 < n else s
        e[rel] = level * np.clip(1 - (t[rel] - hold) / max(r, 1), 0, 1)
    return e


def additive(freq, n, amp, decay, detune_cents=0.0, vib=None, phase_seed=0):
    """
    Band-limited additive tone. amp(k) and decay(k) give each harmonic's level and its exponential decay rate
    (1/s, 0 = steady). `vib` is an optional per-sample pitch multiplier.
    """
    t = np.arange(n) / SR
    f = freq * 2.0 ** (detune_cents / 1200.0)
    if vib is None:
        base = 2 * np.pi * f * t
    else:
        base = 2 * np.pi * np.cumsum(f * vib) / SR
    r = np.random.default_rng(phase_seed)
    out = np.zeros(n)
    k = 1
    while k * f < 15000 and k <= 64:
        g = amp(k)
        if g > 1e-4:
            h = np.sin(k * base + r.uniform(0, 2 * np.pi))
            dk = decay(k)
            out += g * (h * np.exp(-dk * t) if dk > 0 else h)
        k += 1
    return out


def lowpass(x, cutoff, order=2):
    sos = butter(order, min(cutoff, SR * 0.45), "low", fs=SR, output="sos")
    return sosfilt(sos, x, axis=-1)


def highpass(x, cutoff, order=2):
    sos = butter(order, cutoff, "high", fs=SR, output="sos")
    return sosfilt(sos, x, axis=-1)


def bandpass(x, lo, hi, order=2):
    sos = butter(order, [lo, hi], "band", fs=SR, output="sos")
    return sosfilt(sos, x, axis=-1)


# ---------------------------------------------------------------- instruments
def kick(vel=1.0):
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    f = 46 + 95 * np.exp(-t * 28)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 7.5)
    click = lowpass(rng.standard_normal(n), 3500) * np.exp(-t * 180) * 0.35
    return np.tanh((body + click) * 1.6) * 0.9 * vel


def snare(vel=1.0):
    n = int(0.32 * SR)
    t = np.arange(n) / SR
    tone = (np.sin(2 * np.pi * 185 * t) + 0.5 * np.sin(2 * np.pi * 330 * t)) * np.exp(-t * 26)
    noise = bandpass(rng.standard_normal(n), 1400, 9000) * np.exp(-t * 15)
    return (tone * 0.45 + noise * 0.75) * vel


def hat(vel=1.0, open_=False):
    n = int((0.32 if open_ else 0.07) * SR)
    t = np.arange(n) / SR
    x = highpass(rng.standard_normal(n), 7500, 4)
    return x * np.exp(-t * (11 if open_ else 70)) * 0.45 * vel


def tom(m, vel=1.0):
    n = int(0.5 * SR)
    t = np.arange(n) / SR
    f0 = hz(m)
    f = f0 * (1 + 0.6 * np.exp(-t * 30))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 8)
    skin = bandpass(rng.standard_normal(n), 300, 3000) * np.exp(-t * 40) * 0.2
    return np.tanh((body + skin) * 1.3) * 0.8 * vel


def crash(vel=1.0):
    n = int(2.6 * SR)
    t = np.arange(n) / SR
    l = highpass(rng.standard_normal(n), 4000, 2) * np.exp(-t * 1.9)
    r = highpass(rng.standard_normal(n), 4000, 2) * np.exp(-t * 1.9)
    return np.vstack([l, r]) * 0.28 * vel


def bass_note(m, length, brightness=1.0, vel=1.0):
    """Plucked saw bass: the harmonics decay faster the higher they are. `brightness` 0..1 opens the filter."""
    n = length + int(0.06 * SR)
    f = hz(m)
    hk = 4 + int(30 * brightness)
    sig = additive(f, n, lambda k: (1.0 / k) if k <= hk else 0.0,
                   lambda k: 2.0 + k * (3.2 - 1.8 * brightness), phase_seed=m)
    sub = np.sin(2 * np.pi * f * np.arange(n) / SR) * 0.55
    e = env_adsr(n, 0.004, 0.12, 0.7, 0.05, sustain_len=length)
    return np.tanh((sig + sub) * e * 1.3) * 0.55 * vel


def sub_note(m, length):
    n = length + int(0.4 * SR)
    t = np.arange(n) / SR
    s = np.sin(2 * np.pi * hz(m) * t) + 0.25 * np.sin(4 * np.pi * hz(m) * t)
    return s * env_adsr(n, 0.08, 0.2, 0.85, 0.4, sustain_len=length) * 0.5


def pad_chord(notes, length, cutoff=1800):
    """Warm detuned-saw pad, three voices per note spread left and right."""
    n = length + int(1.2 * SR)
    left = np.zeros(n)
    right = np.zeros(n)
    for i, m in enumerate(notes):
        for j, (cents, pan) in enumerate(((-9, -0.8), (0, 0.0), (9, 0.8))):
            v = additive(hz(m), n, lambda k: 1.0 / k if k <= 14 else 0.0, lambda k: 0.0,
                         detune_cents=cents, phase_seed=1000 + 10 * m + j)
            a = (pan + 1) * np.pi / 4
            left += v * np.cos(a)
            right += v * np.sin(a)
    st = np.vstack([left, right]) / (len(notes) * 2.2)
    st = lowpass(st, cutoff, 2)
    e = env_adsr(n, 0.9, 0.5, 0.8, 1.2, sustain_len=length)
    return st * e


def arp_note(m, vel=1.0):
    n = int(0.42 * SR)
    sig = additive(hz(m), n, lambda k: (1.0 / k) * (1.0 if k % 2 else 0.55), lambda k: 7 + k * 2.6,
                   phase_seed=2000 + m)
    e = env_adsr(n, 0.002, 0.08, 0.5, 0.25)
    return sig * e * 0.32 * vel


def lead_note(m, length, vel=1.0):
    """Mellow lead: sine-ish with a little body, delayed vibrato."""
    n = length + int(0.35 * SR)
    t = np.arange(n) / SR
    depth = np.clip((t - 0.25) / 0.4, 0, 1) * 0.0045
    vib = 1 + depth * np.sin(2 * np.pi * 5.4 * t)
    sig = additive(hz(m), n, lambda k: {1: 1.0, 2: 0.32, 3: 0.18, 4: 0.07, 5: 0.05}.get(k, 0.0),
                   lambda k: 0.0, vib=vib, phase_seed=3000 + m)
    e = env_adsr(n, 0.045, 0.2, 0.82, 0.3, sustain_len=length)
    return sig * e * 0.30 * vel


def riser(length):
    n = length
    t = np.linspace(0, 1, n)
    x = rng.standard_normal(n)
    # Sweep the noise brighter in four chunks.
    out = np.zeros(n)
    chunks = 8
    for c in range(chunks):
        a, b = c * n // chunks, (c + 1) * n // chunks
        out[a:b] = bandpass(x[a:b], 400 + 600 * c, 2000 + 1500 * c)
    return out * t ** 2.2 * 0.25


# ---------------------------------------------------------------- the score
def at(bar, step=0):
    return bar * BAR + step * STEP


# Drums
for bar in range(BARS):
    sec = bar // 8
    if sec == 0 or (sec == 3 and bar >= 28):
        # Heartbeat: lub-dub on 1, again on 3 in the second half of A.
        place(kick(0.85), at(bar, 0), gain=0.95)
        place(kick(0.55), at(bar, 3), gain=0.95)
        if bar % 8 >= 4 or bar >= 28:
            place(kick(0.6), at(bar, 8), gain=0.95)
            place(kick(0.4), at(bar, 11), gain=0.95)
        if bar % 8 >= 4 or bar >= 28:
            for s in (2, 6, 10, 14):
                place(hat(0.6), at(bar, s), pan=0.35, rev=0.1)
        if bar >= 30:
            place(snare(0.35), at(bar, 12), pan=-0.05, rev=0.25)
    elif sec in (1, 2):
        kicks = [0, 8, 10] if sec == 1 else [0, 3, 8, 10, 11]
        for s in kicks:
            place(kick(1.0 if s in (0, 8) else 0.7), at(bar, s))
        fill = (bar == 15) or (bar == 23)
        for s in (4, 12):
            if not (fill and s == 12):
                place(snare(1.0), at(bar, s), pan=-0.05, rev=0.22)
        for s in range(0, 16, 2):
            place(hat(0.85 if s % 4 == 2 else 0.6, open_=(s % 8 == 6 and sec == 2)), at(bar, s), pan=0.35, rev=0.08)
        for s in (3, 7, 11, 15):
            place(hat(0.3), at(bar, s), pan=0.4)
        if bar == 15:  # snare roll into C
            for s in range(8, 16):
                place(snare(0.35 + 0.08 * (s - 8)), at(bar, s), pan=-0.05, rev=0.22)
        if bar == 23:  # tom fill into the breakdown
            for s, m in zip(range(8, 16), ["A3", "A3", "F3", "F3", "D3", "D3", "A2", "A2"]):
                place(tom(midi(m), 0.6 + 0.04 * (s - 8)), at(bar, s), pan=0.4 - 0.1 * (s - 8), rev=0.2)
    else:
        # Breakdown bars 24-27: just a distant floor tom on 1.
        place(tom(midi("D2"), 0.45), at(bar, 0), pan=-0.2, rev=0.5)
for bar in (8, 16):
    place(crash(1.0), at(bar), rev=0.25)
place(crash(0.55), at(0), rev=0.3)
place(riser(BAR * 2), at(30), pan=0.0, gain=0.8, rev=0.3)

# Bass
for bar in range(BARS):
    root = midi(CHORDS[chord_at(bar)][0])
    sec = bar // 8
    if sec == 3 and bar < 28:
        if bar % 2 == 0:
            place(sub_note(root, BAR * 2 - STEP), at(bar), gain=0.9)
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
        place(bass_note(root + iv, STEP * 2 - 600, brightness=bright, vel=vel), at(bar, i * 2), pan=0.0, gain=0.95)

# Pad
for bar in range(0, BARS, 2):
    ch = chord_at(bar)
    notes = voicing(ch, midi("D3"))
    notes.append(notes[0] + 12)
    sec = bar // 8
    gain = {0: 0.85, 1: 0.6, 2: 0.6, 3: 1.05}[sec]
    cutoff = {0: 1300, 1: 2000, 2: 2400, 3: 1600}[sec]
    place(pad_chord(notes, BAR * 2, cutoff), at(bar), gain=gain, rev=0.45)

# Arp: 16ths across bars 8-31 (filtered back in the breakdown), ping-ponging left and right.
ARP_SHAPE = [0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 4, 3, 2, 1]
for bar in range(8, BARS):
    ch = chord_at(bar)
    tones = voicing(ch, midi("A3"))
    tones = tones + [tones[0] + 12, tones[1] + 12]
    sec = bar // 8
    level = {1: 0.8, 2: 0.9, 3: 0.75}[sec]
    for s in range(16):
        m = tones[ARP_SHAPE[s]]
        vel = level * (1.0 if s % 4 == 0 else 0.7)
        pan = -0.55 if s % 2 == 0 else 0.55
        place(arp_note(m, vel), at(bar, s), pan=pan, rev=0.35)

# Lead melody over C (bars 16-23): (note, start beat within the section, length in beats)
LEAD = [
    ("F5", 0, 2), ("D5", 2, 1), ("Bb4", 3, 1), ("C5", 4, 1.5), ("D5", 5.5, 0.5), ("F5", 6, 2),
    ("A5", 8, 3), ("G5", 11, 1), ("F5", 12, 2), ("E5", 14, 1), ("C5", 15, 1),
    ("D5", 16, 2), ("Bb4", 18, 1), ("D5", 19, 1), ("G5", 20, 3), ("F5", 23, 1),
    ("E5", 24, 2), ("C#5", 26, 1), ("E5", 27, 1), ("A5", 28, 4),
]
for name, beat, length in LEAD:
    start = at(16) + int(beat * BEAT)
    place(lead_note(midi(name), int(length * BEAT) - 800), start, pan=-0.1, rev=0.5)
# A quiet echo of the opening phrase in the breakdown, an octave down.
for name, beat, length in LEAD[:6]:
    m = midi(name) - 12
    place(lead_note(m, int(length * BEAT) - 800, vel=0.5), at(24) + int(beat * BEAT), pan=0.25, rev=0.7)

# Atmosphere: a low drone and wind whose modulation repeats a whole number of times per loop, so it loops too.
t = np.arange(N + TAIL) / SR
loop_phase = 2 * np.pi * t / (N / SR)
drone = (np.sin(2 * np.pi * hz(midi("D1")) * t) * 0.5 + np.sin(2 * np.pi * hz(midi("A1")) * t) * 0.18)
drone *= 0.12 * (0.75 + 0.25 * np.sin(loop_phase * 4))
place(drone, 0, gain=1.0)
wind = rng.standard_normal((2, N + TAIL))
wind[:, N:] = wind[:, : TAIL]  # the noise itself repeats across the seam
wind = bandpass(wind, 250, 1600, 2)
wind *= 0.05 * (0.6 + 0.4 * np.sin(loop_phase * 3 + np.array([[0.0], [1.3]])))
place(wind, 0, rev=0.3)


# ---------------------------------------------------------------- reverb, fold, master
def make_ir(seconds=2.4, rt60=2.1, predelay=0.022):
    n = int(seconds * SR)
    tt = np.arange(n) / SR
    decay = np.exp(-6.91 * tt / rt60)
    ir = rng.standard_normal((2, n)) * decay
    ir = lowpass(ir, 5200, 2)
    ir = highpass(ir, 180, 2)
    pd = int(predelay * SR)
    ir = np.concatenate([np.zeros((2, pd)), ir], axis=1)
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


# Fold the send's tail onto its start before reverb, so the reverb itself is circular too.
send[:, :TAIL] += send[:, N:]
send = send[:, :N]
ir = make_ir()
wet = np.vstack([fftconvolve(np.concatenate([send[c], send[c]]), ir[c])[N: 2 * N] for c in range(2)])

mix = dry.copy()
mix[:, :TAIL] += mix[:, N:]
mix = mix[:, :N] + wet * 0.55

# Gentle master: a touch of low cut, soft saturation, normalised to -1 dBFS.
mix = highpass(np.concatenate([mix, mix], axis=1), 28, 2)[:, N:]
mix = np.tanh(mix / np.abs(mix).max() * 1.15)
mix *= 10 ** (-1 / 20) / np.abs(mix).max()

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "LastLight", "Resources", "LastLightAudio", "LL_BGM_Run.wav")
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
wavfile.write(out, SR, (mix.T * 32767).astype(np.int16))
print(f"Wrote {os.path.abspath(out)}: {N / SR:.2f} s, {N} samples, {BARS} bars at {BPM} BPM")
