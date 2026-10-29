"""Dev harness for Oddity Phone (Unity 6 + dotnet rules harness).

    python tools/dev.py check                  dotnet build + Unity batchmode compile; fail on error CSxxxx
    python tools/dev.py test [NAME,NAME]       dotnet tests, then Unity EditMode and PlayMode; exit code = failures
    python tools/dev.py test --dotnet-only     skip the Unity half
    python tools/dev.py shot [SCENE ...]       screenshots into shots/SCENE.png (800px wide); default: all scenes
    python tools/dev.py scenes                 list shot scene names (unity/Assets/Oddity/Editor/ShotScenes.txt)
    python tools/dev.py baseline [SCENE ...]   copy current shots to shots/baseline/ for later diffs
    python tools/dev.py kill                   close the Unity editor that has this project open (not the Hub)
    python tools/dev.py ship -m "msg" [--quick a,b]   check, test (only a,b with --quick), commit here. Never pushes.
    python tools/dev.py build                  not built yet (Task 11)

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
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UNITY_PROJECT = ROOT / "unity"
LOGS = UNITY_PROJECT / "Logs"
SHOTS = ROOT / "shots"
BASE = SHOTS / "baseline"
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


def unity(args, log, timeout=STEP_TIMEOUT):
    """Run Unity with the project and a log file. Returns (exit code or None on timeout, log text)."""
    kill()
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
    ap.add_argument("cmd", choices=["check", "test", "shot", "scenes", "baseline", "kill", "ship", "build"])
    ap.add_argument("names", nargs="*")
    ap.add_argument("-m", "--message")
    ap.add_argument("--quick", help="ship: run only these tests (comma list)")
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
    if a.cmd == "build":
        print("build: Task 11에서 만든다")
        return 2
    if a.cmd == "ship":
        if not a.message:
            ap.error("ship needs -m MESSAGE")
        return ship(a.message, [n for n in (a.quick or "").split(",") if n] if a.quick is not None else None)


if __name__ == "__main__":
    sys.exit(main())
