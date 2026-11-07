"""Cut the long Korean cat recordings into short one-shots and purr loops with ffmpeg.

    python tools/cut_cat_sfx.py IN_DIR OUT_DIR [--noise-db -35]

IN_DIR holds the kr_*.mp3 recordings (git-ignored, see its CREDITS.txt), OUT_DIR gets <source>_<nn>.wav (mono, 48 kHz, 16 bit).
ffmpeg and ffprobe must be on PATH. It is not part of `check` or `test`.

Per source kind:
  vocal  (meows, hiss, kitten): silencedetect (default -35 dB, min 0.15 s) finds each sound, pieces of 0.3 to 2.5 s are kept (longer ones are cut
         at 2.5 s with a longer fade out), the loudest 6 by mean level in time order; noisy (mean below -50 dB) and clipped (peak above -0.3 dB) are skipped.
  chew   (dry food, water): the 3 loudest non-overlapping windows of 3 s.
  purr   : the 3 steadiest loud 5 s windows turned into seamless loops: the 0.5 s after the window is cross-faded onto its start.
All pieces get 30 ms fades and a peak of -3 dBFS.
"""
import argparse
import glob
import os
import re
import subprocess
import sys

KINDS = [  # (file stem prefix, kind, max pieces)
    ("kr_purr_", "purr", 3),
    ("kr_lap_water", "chew", 3),
    ("kr_eat_dry_food", "chew", 3),
    ("kr_", "vocal", 6),  # every other kr_* file: meows, hiss, kitten
]
FADE = 0.03
PEAK_DB = -3.0
MIN_LEN, MAX_LEN = 0.3, 2.5
NOISY_DB, CLIPPED_DB = -50.0, -0.3
PAD = 0.04


def run(cmd):
    return subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace").stdout


def duration(path):
    return float(run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path]).strip().splitlines()[-1])


def level(path, start, length):
    """(mean dB, max dB) of path[start:start+length]"""
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-ss", f"{start:.3f}", "-t", f"{length:.3f}", "-i", path, "-af", "aformat=channel_layouts=mono,volumedetect", "-f", "null", "-"])
    mean = re.search(r"mean_volume: (-?[\d.]+|-inf) dB", out)
    peak = re.search(r"max_volume: (-?[\d.]+|-inf) dB", out)
    if not mean or not peak:
        return -99.0, -99.0
    return float(mean.group(1).replace("-inf", "-99")), float(peak.group(1).replace("-inf", "-99"))


def sounds(path, noise_db, total):
    """voiced stretches between the silences: [(start, end)]"""
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", f"silencedetect=n={noise_db}dB:d=0.15", "-f", "null", "-"])
    starts = [float(m) for m in re.findall(r"silence_start: (-?[\d.]+)", out)]
    ends = [float(m) for m in re.findall(r"silence_end: (-?[\d.]+)", out)]
    # voiced = gaps between a silence end and the next silence start
    voiced, cursor = [], 0.0
    for s, e in zip(starts, ends + [total] * (len(starts) - len(ends))):
        s = max(0.0, s)
        if s - cursor > 0.05:
            voiced.append((cursor, s))
        cursor = e
    if total - cursor > 0.05 and (not starts or len(ends) >= len(starts)):
        voiced.append((cursor, total))
    return voiced


def write_piece(src, dst, start, length, gain, filt_extra=""):
    fade_out = min(0.2, length / 3) if length >= MAX_LEN - 1e-3 else FADE
    chain = f"aformat=channel_layouts=mono,{filt_extra}volume={gain:.2f}dB,afade=t=in:d={FADE},afade=t=out:st={max(0.0, length - fade_out):.3f}:d={fade_out:.3f}"
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-y", "-ss", f"{start:.3f}", "-t", f"{length:.3f}", "-i", src, "-af", chain, "-ar", "48000", "-ac", "1", "-c:a", "pcm_s16le", dst])
    if not os.path.exists(dst):
        raise RuntimeError("ffmpeg failed for " + dst + "\n" + out[-400:])


def write_loop(src, dst, start, body, xfade, gain):
    """body seconds from start, its first xfade seconds mixed with the xfade seconds after it so the wrap is seamless"""
    fc = (f"[0:a]aformat=channel_layouts=mono,atrim=start={body:.3f}:end={body + xfade:.3f},asetpts=N/SR/TB[a];"
          f"[0:a]aformat=channel_layouts=mono,atrim=start=0:end={body:.3f},asetpts=N/SR/TB[b];"
          f"[a][b]acrossfade=d={xfade:.3f}:c1=qsin:c2=qsin,volume={gain:.2f}dB[o]")
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-y", "-ss", f"{start:.3f}", "-t", f"{body + xfade:.3f}", "-i", src, "-filter_complex", fc, "-map", "[o]", "-ar", "48000", "-ac", "1", "-c:a", "pcm_s16le", dst])
    if not os.path.exists(dst):
        raise RuntimeError("ffmpeg failed for " + dst + "\n" + out[-400:])


def windows(path, total, length, step):
    """non-overlapping windows ranked by mean level: [(mean, start)]"""
    scored = []
    t = 0.0
    while t + length <= total + 1e-6:
        mean, peak = level(path, t, length)
        scored.append((mean, peak, t))
        t += step
    return scored


def pick_windows(scored, length, count):
    chosen = []
    for mean, peak, t in sorted(scored, key=lambda s: -s[0]):
        if mean < NOISY_DB or peak > CLIPPED_DB:
            continue
        if all(abs(t - c[2]) >= length for c in chosen):
            chosen.append((mean, peak, t))
        if len(chosen) == count:
            break
    return sorted(chosen, key=lambda s: s[2])


def process(path, out_dir, noise_db):
    stem = os.path.splitext(os.path.basename(path))[0]
    kind, limit = next((k, n) for prefix, k, n in KINDS if stem.startswith(prefix))
    total = duration(path)
    made = []
    if kind == "vocal":
        cands = []
        for s, e in sounds(path, noise_db, total):
            s = max(0.0, s - PAD)
            e = min(total, e + PAD)
            length = e - s
            if length < MIN_LEN:
                continue
            length = min(length, MAX_LEN)
            mean, peak = level(path, s, length)
            if mean < NOISY_DB or peak > CLIPPED_DB:
                continue
            cands.append((mean, peak, s, length))
        best = sorted(cands, key=lambda c: -c[0])[:limit]
        for n, (mean, peak, s, length) in enumerate(sorted(best, key=lambda c: c[2]), 1):
            dst = os.path.join(out_dir, f"{stem}_{n:02d}.wav")
            write_piece(path, dst, s, length, PEAK_DB - peak)
            made.append(dst)
    elif kind == "chew":
        for n, (mean, peak, s) in enumerate(pick_windows(windows(path, total, 3.0, 1.0), 3.0, limit), 1):
            dst = os.path.join(out_dir, f"{stem}_{n:02d}.wav")
            write_piece(path, dst, s, 3.0, PEAK_DB - peak)
            made.append(dst)
    else:  # purr loops: 5 s body + 0.5 s cross-fade tail
        body, xfade = 5.0, 0.5
        for n, (mean, peak, s) in enumerate(pick_windows(windows(path, total, body + xfade, 1.0), body + xfade, limit), 1):
            dst = os.path.join(out_dir, f"{stem}_{n:02d}.wav")
            write_loop(path, dst, s, body, xfade, PEAK_DB - peak)
            made.append(dst)
    return made


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("in_dir")
    ap.add_argument("out_dir")
    ap.add_argument("--noise-db", type=float, default=-35.0)
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)
    sources = sorted(glob.glob(os.path.join(a.in_dir, "kr_*.mp3")))
    if not sources:
        sys.exit("no kr_*.mp3 in " + a.in_dir)
    for old in glob.glob(os.path.join(a.out_dir, "kr_*.wav")):
        os.remove(old)  # a rerun replaces the pieces
    for path in sources:
        made = process(path, a.out_dir, a.noise_db)
        print(f"{os.path.basename(path)}: {len(made)} pieces")
        for m in made:
            print(f"  {os.path.basename(m)} {duration(m):.2f} s")


if __name__ == "__main__":
    main()
