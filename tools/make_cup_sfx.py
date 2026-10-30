"""Synthesizes the cup sound of day 3 night: a porcelain cup set down three times, "dalgak, dalgak, tok".

Output: unity/Assets/Resources/Sfx/cup_voice_message.wav (44100 Hz, 16 bit, mono, 2.70 s, peak -6 dB).
The file is tracked (Resources is not git-ignored).

    python tools/make_cup_sfx.py
"""
import wave
from pathlib import Path

import numpy as np

RATE = 44100
SECONDS = 2.70
PEAK_DB = -6.0
SEED = 20261031

# (start second, kind): "clack" has a small bounce, "tok" is clearer and shorter
HITS = [(0.30, "clack"), (1.10, "clack"), (2.00, "tok")]

OUT = Path(__file__).resolve().parents[1] / "unity/Assets/Resources/Sfx/cup_voice_message.wav"


def ring(rng, n, freqs, decay_s, amps):
    """A bundle of damped sine waves (inharmonic partials) plus a very short noise attack."""
    t = np.arange(n) / RATE
    out = np.zeros(n)
    for f, a in zip(freqs, amps):
        phase = rng.uniform(0, 2 * np.pi)
        out += a * np.sin(2 * np.pi * f * t + phase) * np.exp(-t / decay_s)
    noise = rng.standard_normal(n)
    noise = np.diff(noise, prepend=0.0)  # brighter: a click
    out += 0.6 * noise * np.exp(-t / 0.0015)
    return out


def table(n):
    """Faint low wooden tabletop thump, 180 Hz sliding up to 260 Hz."""
    t = np.arange(n) / RATE
    f = 180.0 + 80.0 * np.minimum(t / 0.08, 1.0)
    phase = 2 * np.pi * np.cumsum(f) / RATE
    return np.sin(phase) * np.exp(-t / 0.05)


def add(buf, start, sig, gain):
    i = int(round(start * RATE))
    j = min(len(buf), i + len(sig))
    buf[i:j] += gain * sig[: j - i]


def main():
    rng = np.random.default_rng(SEED)
    buf = np.zeros(int(RATE * SECONDS))
    for start, kind in HITS:
        if kind == "clack":
            n = int(RATE * 0.35)
            main_hit = ring(rng, n, [2210, 3710, 5930], 0.055, [1.0, 0.7, 0.35])
            add(buf, start, main_hit, 0.9)
            bounce = ring(rng, n, [2260, 3790, 6010], 0.035, [1.0, 0.6, 0.3])
            add(buf, start + 0.045, bounce, 0.4)  # the small rattle of the cup settling
            add(buf, start, table(int(RATE * 0.3)), 0.25)
        else:
            n = int(RATE * 0.30)
            hit = ring(rng, n, [2480, 4130, 6370], 0.035, [1.0, 0.45, 0.2])
            add(buf, start, hit, 0.8)
            add(buf, start, table(int(RATE * 0.3)), 0.18)
    peak = np.max(np.abs(buf))
    buf *= (10 ** (PEAK_DB / 20.0)) / peak
    pcm = np.round(buf * 32767).astype("<i2")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print(f"wrote {OUT.name}: {len(pcm) / RATE:.2f} s, {OUT.stat().st_size // 1024} KB, peak {20 * np.log10(np.max(np.abs(buf))):.1f} dB")


if __name__ == "__main__":
    main()
