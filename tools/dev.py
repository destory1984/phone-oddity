"""Dev harness for Oddity Phone (Unity 6 + dotnet rules harness).

    python tools/dev.py check                  dotnet build + Unity batchmode compile; fail on error CSxxxx
    python tools/dev.py test [NAME,NAME]       dotnet tests, then Unity EditMode and PlayMode; exit code = failures
    python tools/dev.py test --dotnet-only     skip the Unity half
    python tools/dev.py shot [SCENE ...]       screenshots into shots/SCENE.png (800px wide); default: all scenes
    python tools/dev.py scenes                 list shot scene names (unity/Assets/Oddity/Editor/ShotScenes.txt)
    python tools/dev.py baseline [SCENE ...]   copy current shots to shots/baseline/ for later diffs
    python tools/dev.py kill                   close the Unity editor that has this project open (not the Hub)
    python tools/dev.py previs SCENE [--level 1,2,3,4] [--day] [--motion]   low-quality mp4 with voice and sounds -> shots/previs/SCENE_L{level}.mp4 (needs ffmpeg on PATH)
                                               (--day: night look off, file SCENE_L{level}_day.mp4)
    python tools/dev.py roomtest               18 s look test of a painted room with a distant 3D Harin -> shots/room_test/room_test.mp4 (1920x1080, 30 fps) + sheet.png
    python tools/dev.py ship -m "msg" [--quick a,b]   check, test (only a,b with --quick), commit here. Never pushes.
    python tools/dev.py build --pc [--demo-voice]   Windows dev exe -> unity/Build/pc/oddity-proto.exe (Mono, 540x960 window)
    python tools/dev.py build [--demo-voice]   Android dev apk -> unity/Build/oddity-proto.apk, then active target back to Windows
                                               (--demo-voice: let karina voice files in; private apk oddity-proto-DEMO-private.apk)

Every command that starts Unity first closes this project's editor. Only one Unity can open the project.
Paths: UNITY_EXE (default C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Unity.exe),
DOTNET_EXE (default C:/Program Files/dotnet/dotnet.exe).
"""
import argparse
import getpass
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UNITY_PROJECT = ROOT / "unity"
LOGS = UNITY_PROJECT / "Logs"
SHOTS = ROOT / "shots"
BASE = SHOTS / "baseline"
PREVIS_OUT = SHOTS / "previs"
PREVIS_TMP = LOGS / "previs"
PREVIS_SCENES = ["d3_night"]
PREVIS_MAX_MB = 30
SCENES_TXT = UNITY_PROJECT / "Assets" / "Oddity" / "Editor" / "ShotScenes.txt"
DOTNET_TESTS = ROOT / "harness" / "Oddity.Core.Tests"
UNITY_EXE = Path(os.environ.get("UNITY_EXE", "C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Unity.exe"))
DOTNET_EXE = Path(os.environ.get("DOTNET_EXE", "C:/Program Files/dotnet/dotnet.exe"))
STEP_TIMEOUT = 1500
EMAIL = "13573570+destory1984@users.noreply.github.com"
TRAILER = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
CS_ERROR = re.compile(r"error CS\d+")


def run(cmd, timeout=120, cwd=ROOT):
    return subprocess.run([str(c) for c in cmd], cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout)


def kill():
    """Close the Unity editor(s) that have this project open. The Hub's own unity.exe stays."""
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
          "Where-Object { $_.CommandLine -match 'oddity-phone' -and $_.CommandLine -notmatch ' serve' } | "
          "ForEach-Object { $_.ProcessId }")
    pids = run(["powershell", "-NoProfile", "-Command", ps]).stdout.split()
    for pid in pids:
        run(["taskkill", "/PID", pid, "/F", "/T"])
    return len(pids)


def licensing_running():
    out = run(["tasklist", "/FI", "IMAGENAME eq Unity.Licensing.Client.exe", "/NH"]).stdout
    return "Unity.Licensing.Client" in out


def ensure_license(wait=60):
    """Unity needs the licensing client (started by Unity Hub). Start the Hub if it is missing and wait for it."""
    if licensing_running():
        return True
    ps = "(Get-AppxPackage UnityTechnologies.UnityHub).PackageFamilyName"
    family = run(["powershell", "-NoProfile", "-Command", ps]).stdout.strip()
    if not family:
        print("Unity Hub is not installed; open Unity Hub and sign in")
        return False
    print("starting Unity Hub for the license client...")
    subprocess.Popen(["explorer.exe", "shell:AppsFolder" + chr(92) + family + "!UnityHub"])
    end = time.time() + wait
    while time.time() < end:
        if licensing_running():
            time.sleep(3)
            return True
        time.sleep(2)
    print("Unity license client did not start in 60s; open Unity Hub and sign in")
    return False


def unity(args, log, timeout=STEP_TIMEOUT):
    """Run Unity with the project and a log file. Returns (exit code or None on timeout, log text)."""
    kill()
    if not ensure_license():
        return 1, ""
    LOGS.mkdir(parents=True, exist_ok=True)
    logfile = LOGS / log
    logfile.unlink(missing_ok=True)
    cmd = [UNITY_EXE, "-batchmode", *args, "-projectPath", UNITY_PROJECT, "-logFile", logfile]
    try:
        code = run(cmd, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        kill()
        code = None
    text = logfile.read_text(encoding="utf-8", errors="replace") if logfile.exists() else ""
    if "No valid Unity Editor license" in text:
        print("Unity license missing: open Unity Hub and sign in, then run again")
        return 1, text
    return code, text


def cs_errors(text):
    seen = []
    for line in text.splitlines():
        if CS_ERROR.search(line) and line.strip() not in seen:
            seen.append(line.strip())
    return seen


def check():
    r = run([DOTNET_EXE, "build", DOTNET_TESTS, "--nologo", "-v", "q"], timeout=STEP_TIMEOUT)
    if r.returncode:
        print("\n".join(l for l in (r.stdout + r.stderr).splitlines() if "error" in l.lower())[-3000:])
        print("CHECK FAILED (dotnet build)")
        return 1
    code, text = unity(["-nographics", "-quit"], "check.log")
    errors = cs_errors(text)
    if code is None:
        print("CHECK FAILED (unity timed out)")
        return 1
    if errors or code != 0:
        print("\n".join(errors) or text[-2000:])
        print("CHECK FAILED (unity compile)")
        return 1
    print("check ok")
    return 0


def build(demo_voice=False):
    """Android development apk. Switches the active target to Android, so switch it back to Windows after."""
    apk = UNITY_PROJECT / "Build" / ("oddity-proto-DEMO-private.apk" if demo_voice else "oddity-proto.apk")
    if demo_voice:
        os.environ["ODDITY_DEMO_VOICE"] = "1"
    else:
        os.environ.pop("ODDITY_DEMO_VOICE", None)
    apk.unlink(missing_ok=True)
    t0 = time.time()
    code, text = unity(["-quit", "-executeMethod", "Oddity.EditorTools.BuildAndroid.Build"], "build.log", timeout=3000)
    secs = time.time() - t0
    # put the active target back so test/shot stay fast and unchanged
    unity(["-quit", "-buildTarget", "StandaloneWindows64"], "build-restore.log", timeout=900)
    if code == 0 and apk.exists():
        print(f"build ok {apk.relative_to(ROOT).as_posix()} {apk.stat().st_size / 1048576:.1f} MB, {secs:.0f}s")
        return 0
    errors = cs_errors(text) or [l.strip() for l in text.splitlines() if "BUILD FAILED" in l or "rror" in l][:8]
    print(chr(10).join(errors[:8]))
    print(f"BUILD FAILED (exit {code}, {secs:.0f}s); see unity/Logs/build.log")
    return 1


PC_BUILD_TOUCHED = [
    "unity/ProjectSettings/ProjectSettings.asset",
    "unity/Assets/Settings/PC_RPAsset.asset",
    "unity/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
]


def build_pc(demo_voice=False):
    """Run the PC build, then undo the side effects a build has on tracked settings files (only those clean before)."""
    clean = []
    for f in PC_BUILD_TOUCHED:
        r = subprocess.run(["git", "status", "--porcelain", "--", f], cwd=ROOT, capture_output=True, text=True)
        if r.returncode == 0 and not r.stdout.strip():
            clean.append(f)
    try:
        return _build_pc(demo_voice)
    finally:
        for f in clean:
            subprocess.run(["git", "checkout", "--", f], cwd=ROOT, capture_output=True)


def _build_pc(demo_voice=False):
    """Windows development exe (Mono, portrait window). Leaves the active target on Windows."""
    outdir = UNITY_PROJECT / "Build" / ("pc-DEMO-private" if demo_voice else "pc")
    exe = outdir / "oddity-proto.exe"
    if demo_voice:
        os.environ["ODDITY_DEMO_VOICE"] = "1"
    else:
        os.environ.pop("ODDITY_DEMO_VOICE", None)
    t0 = time.time()
    code, text = unity(["-quit", "-executeMethod", "Oddity.EditorTools.BuildWindows.Build"], "build-pc.log", timeout=3000)
    secs = time.time() - t0
    if code == 0 and exe.exists():
        total = sum(f.stat().st_size for f in outdir.rglob("*") if f.is_file())
        print(f"build ok {exe.relative_to(ROOT).as_posix()} folder {total / 1048576:.1f} MB, {secs:.0f}s")
        return 0
    errors = cs_errors(text) or [l.strip() for l in text.splitlines() if "BUILD FAILED" in l or "rror" in l][:8]
    print(chr(10).join(errors[:8]))
    print(f"BUILD FAILED (exit {code}, {secs:.0f}s); see unity/Logs/build-pc.log")
    return 1


def scene_names():
    if not SCENES_TXT.exists():
        return []
    return [l.strip() for l in SCENES_TXT.read_text(encoding="utf-8").splitlines() if l.strip()]


def first_line(text):
    for l in (text or "").strip().splitlines():
        if l.strip():
            return l.strip()
    return ""


def dotnet_results(trx):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    out = []
    for res in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", ns):
        ok = res.get("outcome") == "Passed"
        msg = res.find(".//t:ErrorInfo/t:Message", ns)
        out.append((res.get("testName"), ok, "" if ok else first_line(msg.text if msg is not None else "")))
    return out


def dotnet_test(names):
    trx = LOGS / "dotnet.trx"
    LOGS.mkdir(parents=True, exist_ok=True)
    trx.unlink(missing_ok=True)
    cmd = [DOTNET_EXE, "test", DOTNET_TESTS, "--nologo", "--logger", f"trx;LogFileName={trx}"]
    if names:
        cmd += ["--filter", "|".join(f"FullyQualifiedName~{n}" for n in names)]
    try:
        r = run(cmd, timeout=STEP_TIMEOUT)
    except subprocess.TimeoutExpired:
        print("TEST dotnet FAIL timed out")
        return [("dotnet", False, "timed out")]
    if not trx.exists():
        print("\n".join((r.stdout + r.stderr).splitlines()[-15:]))
        return [("dotnet", False, "no results (build failed?)")]
    return dotnet_results(trx)


def unity_results(xml_path):
    out = []
    for case in ET.parse(xml_path).getroot().iter("test-case"):
        ok = case.get("result") == "Passed"
        if case.get("result") == "Skipped" or case.get("result") == "Ignored":
            continue
        msg = case.find("./failure/message")
        out.append((case.get("fullname") or case.get("name"), ok, "" if ok else first_line(msg.text if msg is not None else "")))
    return out


def unity_test(mode, names):
    xml_path = LOGS / f"{mode}.xml"
    xml_path.unlink(missing_ok=True)
    args = ["-runTests", "-testPlatform", mode, "-testResults", xml_path]
    if names:
        args += ["-testFilter", ";".join(names)]
    code, text = unity(args, f"{mode}.log")
    if code is None:
        return [(mode, False, "timed out")]
    if not xml_path.exists():
        errors = cs_errors(text)
        if errors:
            return [(mode, False, errors[0])]
        if names or mode == "PlayMode":  # nothing matched, or no PlayMode tests exist yet
            return []
        return [(mode, False, "no results file; see unity/Logs/" + f"{mode}.log")]
    return unity_results(xml_path)


def test(names, dotnet_only=False):
    results = dotnet_test(names)
    if not dotnet_only:
        for mode in ("EditMode", "PlayMode"):
            results += unity_test(mode, names)
    for name, ok, msg in results:
        print(f"TEST {name} {'PASS' if ok else 'FAIL'} {msg}".rstrip())
    failed = sum(1 for r in results if not r[1])
    print(f"TESTS {len(results) - failed} passed, {failed} failed")
    return failed


def shot(names):
    known = scene_names()
    names = names or known
    bad = [n for n in names if n not in known]
    if bad:
        print(f"unknown scene {', '.join(bad)}; known: {', '.join(known)}")
        return 1
    SHOTS.mkdir(exist_ok=True)
    for n in names:
        (SHOTS / f"{n}.png").unlink(missing_ok=True)
    # no -nographics: the cameras have to draw
    code, text = unity(["-executeMethod", "Oddity.EditorTools.Shots.Run", "-shotScenes", ",".join(names),
                        "-shotDir", SHOTS.resolve()], "shot.log")
    failed = 0
    for line in text.splitlines():
        if line.startswith("SHOT "):
            print(line.strip())
    for n in names:
        out = SHOTS / f"{n}.png"
        if out.exists():
            print(f"shot {out}")
        else:
            print(f"shot FAILED for {n}")
            failed += 1
    if code is None:
        print("shot timed out")
        failed += 1
    elif code != 0 and not failed:
        print(f"unity exited with {code}")
        failed += 1
    return 1 if failed else 0


def ffprobe_info(ffprobe, path):
    """(duration seconds, number of audio streams) of a media file."""
    out = run([ffprobe, "-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "default=nw=1", path]).stdout
    dur = 0.0
    audio = 0
    for line in out.splitlines():
        if line.startswith("duration="):
            try:
                dur = float(line.split("=", 1)[1])
            except ValueError:
                pass
        elif line.strip() == "codec_type=audio":
            audio += 1
    return dur, audio


def previs_mix(ffmpeg, silent, events_tsv, seconds, out):
    """Put the sounds the game played (start ms, length ms, volume, wav) under the silent video at their start times."""
    events = []
    for line in Path(events_tsv).read_text(encoding="utf-8").splitlines():
        parts = line.split(chr(9))
        if len(parts) == 4:
            events.append((int(parts[0]), int(parts[1]), float(parts[2]), parts[3]))
    cmd = [ffmpeg, "-y", "-loglevel", "error", "-i", silent]
    parts = []
    for i, (start, dur, vol, wav) in enumerate(events):
        cmd += ["-i", wav]
        parts.append(f"[{i + 1}:a]atrim=0:{dur / 1000:.3f},asetpts=PTS-STARTPTS,aformat=sample_rates=44100:channel_layouts=stereo,"
                     f"volume={vol:.3f},adelay={start}|{start}[a{i}]")
    if events:
        labels = "".join(f"[a{i}]" for i in range(len(events)))
        parts.append(f"{labels}amix=inputs={len(events)}:normalize=0:duration=longest,apad=whole_dur={seconds:.3f}[aout]")
        cmd += ["-filter_complex", ";".join(parts), "-map", "0:v", "-map", "[aout]"]
    else:
        cmd += ["-f", "lavfi", "-t", f"{seconds:.3f}", "-i", "anullsrc=r=44100:cl=stereo", "-map", "0:v", "-map", f"{1}:a"]
    cmd += ["-c:v", "copy", "-c:a", "aac", "-b:a", "128k", "-t", f"{seconds:.3f}", "-movflags", "+faststart", out]
    r = run(cmd, timeout=600)
    if r.returncode:
        print((r.stdout + r.stderr)[-1500:])
        return False
    return True


def previs(names, levels, day=False, motion=False):
    ffmpeg, ffprobe = shutil.which("ffmpeg"), shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        print("PREVIS FAILED: ffmpeg and ffprobe are not on PATH; install ffmpeg (winget install Gyan.FFmpeg) and open a new shell")
        return 1
    if len(names) != 1 or names[0] not in PREVIS_SCENES:
        print(f"previs needs one scene name; known: {', '.join(PREVIS_SCENES)}")
        return 1
    scene = names[0]
    try:
        level_list = [int(x) for x in (levels or "1,2,3").split(",") if x.strip() != ""]
    except ValueError:
        level_list = []
    if not level_list or any(l < 0 or l > 4 for l in level_list):
        print("--level takes numbers 0 to 4, for example 2 or 1,2,3,4")
        return 1
    PREVIS_OUT.mkdir(parents=True, exist_ok=True)
    PREVIS_TMP.mkdir(parents=True, exist_ok=True)
    suffix = ("_day" if day else "") + ("_motion" if motion else "")
    for f in PREVIS_TMP.glob(f"{scene}_L*"):
        f.unlink()
    t0 = time.time()
    # no -nographics: the cameras have to draw
    code, text = unity(["-executeMethod", "Oddity.EditorTools.Previs.Run", "-previsScene", scene,
                        "-previsLevels", ",".join(str(l) for l in level_list), "-previsDir", PREVIS_TMP.resolve(),
                        "-previsFfmpeg", ffmpeg] + (["-previsDay"] if day else []) + (["-previsMotion"] if motion else []), "previs.log", timeout=3600)
    done = {}
    for line in text.splitlines():
        if line.startswith("PREVIS_DONE "):
            kv = dict(p.split("=", 1) for p in line.split() if "=" in p)
            done[int(kv["level"])] = kv
            print(line.strip().replace(str(ROOT.as_posix()) + "/", ""))
        elif "PREVIS FAIL" in line or line.startswith("PREVIS "):
            print(line.strip())
    failed = 0
    for level in level_list:
        kv = done.get(level)
        if kv is None:
            print(f"PREVIS level {level} FAILED; see unity/Logs/previs.log")
            failed += 1
            continue
        out = PREVIS_OUT / f"{scene}_L{level}{suffix}.mp4"
        out.unlink(missing_ok=True)
        seconds = float(kv["seconds"])
        if not previs_mix(ffmpeg, kv["video"], kv["events"], seconds, str(out)):
            failed += 1
            continue
        mb = out.stat().st_size / 1048576
        dur, audio = ffprobe_info(ffprobe, str(out))
        print(f"PREVIS {out.relative_to(ROOT).as_posix()} {mb:.1f} MB, {dur:.1f}s, audio tracks {audio}, game wall {kv['wall']}")
        if mb > PREVIS_MAX_MB:
            print(f"PREVIS {out.name} is over {PREVIS_MAX_MB} MB")
            failed += 1
        if audio != 1:
            print(f"PREVIS {out.name} has no audio track")
            failed += 1
        Path(kv["video"]).unlink(missing_ok=True)
        for w in PREVIS_TMP.glob(f"{scene}_L{level}{suffix}.a*.wav"):
            w.unlink()
    if code is None:
        print("previs timed out")
        failed += 1
    print(f"previs total {time.time() - t0:.0f}s")
    return 1 if failed else 0


ROOMTEST_OUT = SHOTS / "room_test"
ROOMTEST_SHEET_TIMES = [2, 5, 8, 10.8, 13.8, 15]


def roomtest():
    """Task 54: film the room test in batchmode, then cut a 3x2 contact sheet from the video."""
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        print("ROOMTEST FAILED: ffmpeg is not on PATH")
        return 1
    for art in ("room_clean.png", "cut_bored.png", "cut_smug.png"):
        src = ROOMTEST_OUT / art
        dst = UNITY_PROJECT / "Assets" / "Oddity" / "RoomTest" / "Art" / art
        if src.exists():
            dst.parent.mkdir(parents=True, exist_ok=True)
            if not dst.exists() or src.stat().st_mtime > dst.stat().st_mtime:
                shutil.copy2(src, dst)
    ROOMTEST_OUT.mkdir(parents=True, exist_ok=True)
    t0 = time.time()
    # no -nographics: the camera has to draw
    code, text = unity(["-executeMethod", "Oddity.RoomTest.RoomTest.Run", "-roomDir", ROOMTEST_OUT.resolve(), "-roomFfmpeg", ffmpeg],
                       "roomtest.log", timeout=1800)
    for line in text.splitlines():
        if line.startswith("ROOMTEST"):
            print(line.strip().replace(str(ROOMTEST_OUT.as_posix()) + "/", ""))
    silent = ROOMTEST_OUT / "room_test.silent.mp4"
    if code is None or "ROOMTEST_DONE" not in text or not silent.exists():
        print("ROOMTEST FAILED; see unity/Logs/roomtest.log")
        return 1
    video = ROOMTEST_OUT / "room_test.mp4"
    video.unlink(missing_ok=True)
    silent.replace(video)
    sel = "+".join(f"eq(n,{round(t * 30)})" for t in ROOMTEST_SHEET_TIMES)
    sheet = ROOMTEST_OUT / "sheet.png"
    sheet.unlink(missing_ok=True)
    r = run([ffmpeg, "-y", "-loglevel", "error", "-i", video, "-vf", f"select='{sel}',scale=640:360,tile=3x2", "-frames:v", "1", sheet])
    if r.returncode or not sheet.exists():
        print("ROOMTEST sheet failed: " + r.stderr[-500:])
        return 1
    print(f"ROOMTEST {video.relative_to(ROOT).as_posix()} {video.stat().st_size / 1048576:.1f} MB, {sheet.relative_to(ROOT).as_posix()}, {time.time() - t0:.0f}s")
    return 0


def baseline(names):
    BASE.mkdir(parents=True, exist_ok=True)
    files = [SHOTS / f"{n}.png" for n in names] if names else SHOTS.glob("*.png")
    for f in files:
        if f.exists():
            shutil.copy2(f, BASE / f.name)
            print(f"baseline {f.name}")
    return 0


def git(args):
    r = run(["git", *args])
    if r.returncode:
        raise SystemExit(f"git {' '.join(args)} failed:\n{r.stdout}{r.stderr}")
    return r.stdout


def private_needles():
    """Words that must not reach a public repo, built at run time so this file never spells them."""
    home = Path.home()
    found = [getpass.getuser(), home.as_posix(), str(home), str(home).replace("\\", "\\\\"), "@" + "gmail"]
    r = run(["git", "config", "--global", "user.email"])
    mail = r.stdout.strip()
    if mail and "noreply" not in mail:
        found.append(mail)
    return [n for n in dict.fromkeys(found) if n]


def ship(message, quick=None):
    if quick is not None and not quick:
        print("--quick needs the batch's test names (a,b,c)")
        return 1
    if check() or test(quick):
        print("SHIP STOPPED: fix the failures above first")
        return 1
    email = git(["config", "user.email"]).strip()
    if email != EMAIL:
        print(f"SHIP STOPPED: git user.email is {email!r}, expected the noreply address")
        return 1
    git(["add", "-A"])
    diff = git(["diff", "--cached", "--no-color", "-U0"])
    added = [l for l in diff.splitlines() if l.startswith("+") and not l.startswith("+++")]
    for needle in private_needles():
        hits = [l for l in added if needle.lower() in l.lower()]
        if hits:
            git(["reset", "-q"])
            print(f"SHIP STOPPED: staged diff contains {needle!r}, e.g. {hits[0][:120]}")
            return 1
    if not git(["diff", "--cached", "--name-only"]).strip():
        print("nothing to commit")
        return 0
    git(["commit", "-qm", f"{message}\n\n{TRAILER}"])
    print("shipped:", git(["log", "--oneline", "-1"]).strip())
    print("not pushed; push only when asked")
    return 0


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["check", "test", "shot", "scenes", "baseline", "kill", "ship", "build", "previs", "roomtest"])
    ap.add_argument("names", nargs="*")
    ap.add_argument("-m", "--message")
    ap.add_argument("--quick", help="ship: run only these tests (comma list)")
    ap.add_argument("--demo-voice", action="store_true", help="build: allow karina voice files (private demo apk)")
    ap.add_argument("--level", help="previs: conti strengths to film, 0 off, 1 weak, 2 mid, 3 strong, 4 close (comma list, default 1,2,3)")
    ap.add_argument("--day", action="store_true", help="previs: film with the night look off (file names get _day)")
    ap.add_argument("--motion", action="store_true", help="previs: play the Mixamo motion of the cues (file names get _motion)")
    ap.add_argument("--pc", action="store_true", help="build: Windows exe (unity/Build/pc) instead of Android apk")
    ap.add_argument("--dotnet-only", action="store_true", help="test: skip the Unity EditMode and PlayMode runs")
    a = ap.parse_args()
    names = [n for arg in a.names for n in arg.split(",") if n]
    if a.cmd == "kill":
        print(f"closed {kill()} Unity editor process(es)")
        return 0
    if a.cmd == "check":
        return check()
    if a.cmd == "scenes":
        print("\n".join(scene_names()))
        return 0
    if a.cmd == "test":
        return test(names, a.dotnet_only)
    if a.cmd == "shot":
        return shot(names)
    if a.cmd == "baseline":
        return baseline(names)
    if a.cmd == "previs":
        return previs(names, a.level, a.day, a.motion)
    if a.cmd == "roomtest":
        return roomtest()
    if a.cmd == "build":
        return build_pc(a.demo_voice) if a.pc else build(a.demo_voice)
    if a.cmd == "ship":
        if not a.message:
            ap.error("ship needs -m MESSAGE")
        return ship(a.message, [n for n in (a.quick or "").split(",") if n] if a.quick is not None else None)


if __name__ == "__main__":
    sys.exit(main())
