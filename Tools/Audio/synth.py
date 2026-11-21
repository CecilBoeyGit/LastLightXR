"""
Shared offline synth for Last Light's music: note helpers, a stereo mix bus with a reverb send, instruments,
and the seamless-loop master (anything ringing past the loop end is folded back onto the start).

Used by render_bgm.py (the run loop) and render_menu_bgm.py (the main menu loop). Needs numpy and scipy.
"""
import os

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100

# One shared generator, so a track renders identically every time. Call reseed() at the top of each track.
rng = np.random.default_rng(1105)


def reseed(seed):
    global rng
    rng = np.random.default_rng(seed)


# ---------------------------------------------------------------- notes
NOTE = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6,
        "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}


def midi(name):
    """'D4' -> 62, 'Bb3' -> 58, 'C#5' -> 73."""
    pitch, octave = name[:-1], int(name[-1])
    return 12 * (octave + 1) + NOTE[pitch]


def hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def voicing(tones, low):
    """Pitch classes (names) as midi notes, each the lowest at or above `low`."""
    out = []
    for p in tones:
        m = low - (low % 12) + NOTE[p]
        while m < low:
            m += 12
        out.append(m)
    return sorted(out)


# ---------------------------------------------------------------- the mix bus
class Mix:
    """Dry and reverb-send stereo buses for a loop of `n` samples, with `tail` samples rendered past the end."""

    def __init__(self, n, tail=SR * 5):
        self.n, self.tail = n, tail
        self.dry = np.zeros((2, n + tail))
        self.send = np.zeros((2, n + tail))

    def place(self, sig, start, pan=0.0, gain=1.0, rev=0.0):
        """Adds a mono or stereo signal at sample `start`, with equal-power pan and a reverb send."""
        if sig.ndim == 1:
            a = (pan + 1.0) * np.pi / 4.0
            sig = np.vstack([sig * np.cos(a), sig * np.sin(a)])
        end = min(start + sig.shape[1], self.n + self.tail)
        sig = sig[:, : end - start] * gain
        self.dry[:, start:end] += sig
        if rev > 0.0:
            self.send[:, start:end] += sig * rev

    def master(self, rt60=2.1, ir_seconds=2.4, wet_gain=0.55, ir_lowpass=5200, drive=1.15):
        """Circular reverb, folds the tail onto the start, low cut, soft saturation, -1 dBFS peak. Returns [2, n]."""
        n, tail = self.n, self.tail
        send = self.send.copy()
        send[:, :tail] += send[:, n:]
        send = send[:, :n]
        ir = make_ir(ir_seconds, rt60, lp=ir_lowpass)
        wet = np.vstack([fftconvolve(np.concatenate([send[c], send[c]]), ir[c])[n: 2 * n] for c in range(2)])
        mix = self.dry.copy()
        mix[:, :tail] += mix[:, n:]
        mix = mix[:, :n] + wet * wet_gain
        mix = highpass(np.concatenate([mix, mix], axis=1), 28, 2)[:, n:]
        mix = np.tanh(mix / np.abs(mix).max() * drive)
        return mix * (10 ** (-1 / 20) / np.abs(mix).max())


def write(path, mix):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    wavfile.write(path, SR, (mix.T * 32767).astype(np.int16))


def make_ir(seconds=2.4, rt60=2.1, predelay=0.022, lp=5200):
    n = int(seconds * SR)
    tt = np.arange(n) / SR
    decay = np.exp(-6.91 * tt / rt60)
    ir = rng.standard_normal((2, n)) * decay
    ir = lowpass(ir, lp, 2)
    ir = highpass(ir, 180, 2)
    pd = int(predelay * SR)
    ir = np.concatenate([np.zeros((2, pd)), ir], axis=1)
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


# ---------------------------------------------------------------- building blocks
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


def additive(freq, n, amp, decay, detune_cents=0.0, vib=None, phase_seed=0, ratio=None):
    """
    Band-limited additive tone. amp(k) and decay(k) give each partial's level and its exponential decay rate
    (1/s, 0 = steady). `vib` is an optional per-sample pitch multiplier. `ratio(k)` stretches partial k away
    from k * freq (for inharmonic tones); by default the partials are harmonic.
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
    while k <= 64:
        rk = k if ratio is None else ratio(k)
        if rk * f >= 15000:
            break
        g = amp(k)
        if g > 1e-4:
            h = np.sin(rk * base + r.uniform(0, 2 * np.pi))
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


# ---------------------------------------------------------------- drums
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


def riser(length):
    n = length
    t = np.linspace(0, 1, n)
    x = rng.standard_normal(n)
    out = np.zeros(n)
    chunks = 8
    for c in range(chunks):
        a, b = c * n // chunks, (c + 1) * n // chunks
        out[a:b] = bandpass(x[a:b], 400 + 600 * c, 2000 + 1500 * c)
    return out * t ** 2.2 * 0.25


# ---------------------------------------------------------------- pitched instruments
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


def pad_chord(notes, length, cutoff=1800, attack=0.9, release=1.2):
    """Warm detuned-saw pad, three voices per note spread left and right."""
    n = length + int(release * SR)
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
    e = env_adsr(n, attack, 0.5, 0.8, release, sustain_len=length)
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


def piano_note(m, length, vel=1.0):
    """
    Soft felt piano: two slightly detuned strings with stretched (inharmonic) partials, a fast and a slow decay
    stage, upper partials dying sooner, a damper at the end of `length`, and a little hammer thump.
    """
    n = length + int(0.5 * SR)
    t = np.arange(n) / SR
    f = hz(m)
    inharm = 0.00035 * (440.0 / max(f, 60.0)) ** 0.5
    bright = 0.68 + 0.3 * vel
    amp = lambda k: (1.0 / k ** 1.1) * (bright ** (k - 1)) if k <= 18 else 0.0
    ratio = lambda k: k * np.sqrt(1 + inharm * k * k)
    lowness = np.clip((72 - m) / 36.0, 0, 1)   # low notes ring longer
    slow = lambda k: (0.55 + 0.35 * k) * (1.0 - 0.45 * lowness)
    fast = lambda k: (3.5 + 1.2 * k)
    s = np.zeros(n)
    for j, cents in enumerate((-1.2, 1.2)):
        s += 0.65 * additive(f, n, amp, slow, detune_cents=cents, ratio=ratio, phase_seed=4000 + 2 * m + j)
        s += 0.35 * additive(f, n, amp, fast, detune_cents=cents, ratio=ratio, phase_seed=4100 + 2 * m + j)
    s /= 2.0
    damper = np.ones(n)
    rel = t >= length / SR
    damper[rel] = np.exp(-(t[rel] - length / SR) * 14)
    attack = np.clip(t / 0.003, 0, 1)
    thump = lowpass(rng.standard_normal(n), 900) * np.exp(-t * 90) * 0.05
    return (s * damper * attack + thump) * 0.32 * vel


def celesta_note(m, vel=1.0):
    """Music-box bell: a few inharmonic partials, the high ones gone almost at once."""
    n = int(2.2 * SR)
    sig = additive(hz(m), n, lambda k: {1: 1.0, 2: 0.3, 3: 0.12}.get(k, 0.0),
                   lambda k: {1: 2.2, 2: 7.0, 3: 14.0}.get(k, 20.0),
                   ratio=lambda k: {1: 1.0, 2: 3.01, 3: 5.93}.get(k, k * 2.0), phase_seed=5000 + m)
    t = np.arange(n) / SR
    return sig * np.clip(t / 0.0015, 0, 1) * 0.22 * vel


def cello_note(m, length, vel=1.0):
    """Bowed low string: slow swell, gentle vibrato, a warm lowpassed saw."""
    n = length + int(0.6 * SR)
    t = np.arange(n) / SR
    depth = np.clip((t - 0.3) / 0.6, 0, 1) * 0.004
    vib = 1 + depth * np.sin(2 * np.pi * 4.8 * t)
    sig = additive(hz(m), n, lambda k: 1.0 / k if k <= 16 else 0.0, lambda k: 0.0, vib=vib, phase_seed=6000 + m)
    sig = lowpass(sig, 1100, 2)
    e = env_adsr(n, 0.35, 0.3, 0.85, 0.6, sustain_len=length)
    return sig * e * 0.28 * vel


def drone_and_wind(n, tail, root="D1", fifth="A1", drone_gain=0.12, wind_gain=0.05, wind_band=(250, 1600)):
    """A low drone and stereo wind whose modulation repeats a whole number of times per loop, so it loops too."""
    t = np.arange(n + tail) / SR
    loop_phase = 2 * np.pi * t / (n / SR)
    drone = (np.sin(2 * np.pi * hz(midi(root)) * t) * 0.5 + np.sin(2 * np.pi * hz(midi(fifth)) * t) * 0.18)
    drone *= drone_gain * (0.75 + 0.25 * np.sin(loop_phase * 4))
    wind = rng.standard_normal((2, n + tail))
    wind[:, n:] = wind[:, :tail]  # the noise itself repeats across the seam
    wind = bandpass(wind, wind_band[0], wind_band[1], 2)
    wind *= wind_gain * (0.6 + 0.4 * np.sin(loop_phase * 3 + np.array([[0.0], [1.3]])))
    return drone, wind
