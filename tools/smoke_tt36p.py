"""
Crusher Core V1 — Windowless Android Emulator (`tt36p`) Smoke Runner.
Strictly enforces CREATE_NO_WINDOW (0x08000000) and SW_HIDE for NoWinGuard compliance.

Workflow:
  1. Boots headless AVD `tt36p` (`-no-window -no-audio -no-boot-anim -gpu swiftshader_indirect`) on dedicated port.
  2. Waits for `sys.boot_completed=1` + PackageManager ready, sets `wm size 1080x2400` (`density 420`).
  3. Installs APK (`adb install -r`), clears logcat, launches `com.crushercore.hypershredder`.
  4. Verifies live PID (`pidof`), executes upgrade/UI taps, captures 3 genuine 1080x2400 PNG screenshots (`shot_01..03.png`).
  5. Audits `logcat -d` for zero `FATAL EXCEPTION` / `AndroidRuntime` / native `CRASH` / unhandled Unity exceptions.
  6. Restores `wm size/density` and cleanly shuts down via `adb emu kill`.

Usage:
  python tools/smoke_tt36p.py --apk Builds/CrusherCore-smoke-universal.apk --shots-out web/shots
  python tools/smoke_tt36p.py --self-test
"""
import argparse
import json
import os
import struct
import subprocess
import sys
import time

CREATE_NO_WINDOW = 0x08000000
PNG_SIG = b"\x89PNG\r\n\x1a\n"

SDK_ROOT = r"C:\Android\Sdk"
EMULATOR_EXE = os.path.join(SDK_ROOT, "emulator", "emulator.exe")
ADB_EXE = os.path.join(SDK_ROOT, "platform-tools", "adb.exe")
AVD_HOME = os.path.expanduser(r"~\.android\avd")
DEFAULT_AVD = "tt36p"
DEFAULT_PKG = "com.crushercore.hypershredder"
DEFAULT_ACTIVITY = "com.unity3d.player.UnityPlayerActivity"


def _hidden_startupinfo():
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0  # SW_HIDE
    return si


def _sdk_env():
    env = os.environ.copy()
    env["ANDROID_SDK_ROOT"] = SDK_ROOT
    env["ANDROID_HOME"] = SDK_ROOT
    env["ANDROID_AVD_HOME"] = AVD_HOME
    return env


def run_adb(serial: str, *args: str, timeout: float = 120.0, binary: bool = False):
    cmd = [ADB_EXE]
    if serial:
        cmd.extend(["-s", serial])
    cmd.extend(args)
    proc = subprocess.run(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout,
        env=_sdk_env(),
        creationflags=CREATE_NO_WINDOW,
        startupinfo=_hidden_startupinfo(),
    )
    if binary:
        return proc.returncode, proc.stdout, proc.stderr.decode("utf-8", errors="replace")
    return (
        proc.returncode,
        proc.stdout.decode("utf-8", errors="replace"),
        proc.stderr.decode("utf-8", errors="replace"),
    )


def parse_png_dimensions(png_bytes: bytes) -> tuple[int, int]:
    if len(png_bytes) < 32 or png_bytes[:8] != PNG_SIG:
        raise ValueError(f"Invalid PNG signature (len={len(png_bytes)})")
    chunk_len, chunk_type = struct.unpack(">I4s", png_bytes[8:16])
    if chunk_type != b"IHDR" or chunk_len < 8:
        raise ValueError(f"Missing IHDR chunk ({chunk_type!r})")
    width, height = struct.unpack(">II", png_bytes[16:24])
    return width, height


def analyze_logcat_for_crashes(log_text: str, pkg: str, pid: str) -> list[str]:
    issues = []
    unity_exc_tokens = (
        "NullReferenceException:",
        "IndexOutOfRangeException:",
        "MissingReferenceException:",
        "InvalidOperationException:",
        "ArgumentException:",
        "DivideByZeroException:",
        "DllNotFoundException:",
    )
    in_our_fatal = False
    for line in log_text.splitlines():
        if "FATAL EXCEPTION" in line:
            in_our_fatal = True
            issues.append(line.strip()[:280])
            continue
        if in_our_fatal and "Process:" in line:
            if pkg not in line:
                # Fatal belonged to an unrelated background system service
                issues.pop()
            in_our_fatal = False

        if "AndroidRuntime" in line and pkg in line and ("FATAL" in line or "Exception" in line):
            issues.append(line.strip()[:280])
        elif (" CRASH " in line or "F DEBUG" in line or "libc    : Fatal signal" in line) and (
            pkg in line or (pid and f"pid: {pid}" in line)
        ):
            issues.append(line.strip()[:280])
        elif " E Unity " in line or " E/Unity" in line or " W Unity " in line or "Unity   :" in line:
            if any(tok in line for tok in unity_exc_tokens):
                issues.append(line.strip()[:280])
            elif "GLSL link error" in line or "SetConstantBuffer: The current renderer does not support" in line:
                issues.append(line.strip()[:280])

    return issues


def capture_screenshot(serial: str, out_path: str, expected_w: int = 1080, expected_h: int = 2400) -> dict:
    rc, raw_png, err = run_adb(serial, "exec-out", "screencap", "-p", timeout=60.0, binary=True)
    if rc != 0 or not raw_png:
        raise RuntimeError(f"screencap failed (rc={rc}): {err}")

    # Normalize accidental CRLF only if stream lacked PNG signature cleanly
    if raw_png[:8] != PNG_SIG and raw_png.startswith(b"\x89PNG\r\r\n\x1a\n"):
        raw_png = raw_png.replace(b"\r\n", b"\n")

    w, h = parse_png_dimensions(raw_png)
    if (w, h) != (expected_w, expected_h):
        raise RuntimeError(f"Screenshot {out_path} dimensions {w}x{h} != expected {expected_w}x{expected_h}")
    if len(raw_png) < 12000:
        raise RuntimeError(f"Screenshot {out_path} suspiciously small ({len(raw_png)} B — likely blank screen)")

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(raw_png)

    return {"path": os.path.abspath(out_path), "width": w, "height": h, "bytes": len(raw_png)}


def tap_norm(serial: str, nx: float, ny: float, w: int = 1080, h: int = 2400, pause: float = 0.45):
    x = max(1, min(w - 1, round(nx * w)))
    y = max(1, min(h - 1, round(ny * h)))
    run_adb(serial, "shell", "input", "tap", str(x), str(y), timeout=20.0)
    if pause > 0:
        time.sleep(pause)


def wait_for_boot(serial: str, timeout_sec: float = 420.0):
    t0 = time.time()
    run_adb(serial, "wait-for-device", timeout=min(180.0, timeout_sec))
    while time.time() - t0 < timeout_sec:
        _, boot_out, _ = run_adb(serial, "shell", "getprop", "sys.boot_completed", timeout=15.0)
        if boot_out.strip() == "1":
            _, pm_out, _ = run_adb(serial, "shell", "pm", "path", "android", timeout=15.0)
            if "package:" in pm_out:
                return round(time.time() - t0, 1)
        time.sleep(3.0)
    raise TimeoutError(f"Emulator {serial} did not finish boot within {timeout_sec}s")


def get_app_pid(serial: str, pkg: str) -> str:
    _, out, _ = run_adb(serial, "shell", "pidof", pkg, timeout=15.0)
    tokens = out.strip().split()
    return tokens[0] if tokens else ""


def run_smoke(args):
    if not os.path.isfile(args.apk):
        raise FileNotFoundError(f"APK not found: {args.apk}")
    if not os.path.isfile(EMULATOR_EXE) or not os.path.isfile(ADB_EXE):
        raise FileNotFoundError(f"Android SDK tools missing ({EMULATOR_EXE} / {ADB_EXE})")

    os.makedirs(args.logs_dir, exist_ok=True)
    os.makedirs(args.shots_out, exist_ok=True)
    emu_log_path = os.path.join(args.logs_dir, "emulator_tt36p.log")
    logcat_path = os.path.join(args.logs_dir, "smoke_logcat.txt")
    report_path = os.path.join(args.logs_dir, "smoke_report.json")

    serial = f"emulator-{args.port}"
    run_adb("", "start-server", timeout=30.0)

    emu_cmd = [
        EMULATOR_EXE,
        "-avd", args.avd,
        "-port", str(args.port),
        "-no-window",
        "-no-audio",
        "-no-boot-anim",
        "-no-snapshot-save",
        "-gpu", "host",
        "-feature", "Vulkan",
    ]

    emu_log_fh = open(emu_log_path, "w", encoding="utf-8", errors="replace")
    emu_proc = subprocess.Popen(
        emu_cmd,
        stdout=emu_log_fh,
        stderr=subprocess.STDOUT,
        env=_sdk_env(),
        creationflags=CREATE_NO_WINDOW,
        startupinfo=_hidden_startupinfo(),
    )

    wm_overridden = False
    try:
        boot_sec = wait_for_boot(serial, timeout_sec=args.boot_timeout)
        print(f"[smoke_tt36p] {serial} booted in {boot_sec}s")

        run_adb(serial, "shell", "input", "keyevent", "82", timeout=15.0)
        run_adb(serial, "shell", "wm", "dismiss-keyguard", timeout=15.0)
        run_adb(serial, "shell", "wm", "size", "1080x2400", timeout=15.0)
        run_adb(serial, "shell", "wm", "density", "420", timeout=15.0)
        wm_overridden = True
        time.sleep(1.5)

        rc, inst_out, inst_err = run_adb(serial, "install", "-r", "-g", os.path.abspath(args.apk), timeout=240.0)
        if rc != 0 or "Success" not in inst_out:
            raise RuntimeError(f"adb install failed (rc={rc}): {inst_out} {inst_err}")
        print(f"[smoke_tt36p] Installed {args.apk} -> Success")

        run_adb(serial, "logcat", "-c", timeout=15.0)

        comp = f"{args.pkg}/{args.activity}"
        _, start_out, _ = run_adb(serial, "shell", "am", "start", "-W", "-n", comp, timeout=60.0)
        if "Error" in start_out:
            run_adb(
                serial, "shell", "monkey", "-p", args.pkg,
                "-c", "android.intent.category.LAUNCHER", "1", timeout=30.0
            )

        pid = ""
        for _ in range(20):
            time.sleep(1.0)
            pid = get_app_pid(serial, args.pkg)
            if pid:
                break
        if not pid:
            raise RuntimeError(f"Process {args.pkg} did not start (pidof empty)")
        print(f"[smoke_tt36p] App running with PID={pid}, waiting {args.warmup_sec}s for initial combat...")

        time.sleep(args.warmup_sec)
        if not get_app_pid(serial, args.pkg):
            raise RuntimeError(f"App {args.pkg} exited prematurely during warmup!")

        shots = []
        shots.append(capture_screenshot(serial, os.path.join(args.shots_out, "shot_01.png")))
        print(f"[smoke_tt36p] Captured shot_01.png ({shots[-1]['bytes']} B)")

        # Tap upgrade cards & Defense tab (row 0 at ny=0.65, tabs at ny=0.94 per #39)
        tap_plan_1 = [(0.26, 0.65), (0.74, 0.65), (0.38, 0.94), (0.26, 0.65), (0.74, 0.65)]
        if args.tap_plan_json and os.path.isfile(args.tap_plan_json):
            with open(args.tap_plan_json, "r", encoding="utf-8") as f:
                plan_data = json.load(f)
            tap_plan_1 = [tuple(pt) for pt in plan_data.get("phase1", tap_plan_1)]
        for nx, ny in tap_plan_1:
            tap_norm(serial, nx, ny)

        time.sleep(args.mid_sec)
        if not get_app_pid(serial, args.pkg):
            raise RuntimeError(f"App {args.pkg} crashed after phase 1 taps!")
        shots.append(capture_screenshot(serial, os.path.join(args.shots_out, "shot_02.png")))
        print(f"[smoke_tt36p] Captured shot_02.png ({shots[-1]['bytes']} B)")

        # Tap HANGAR button in header below Android status-bar SafeArea (nx=0.86, ny=0.108)
        tap_plan_2 = [(0.86, 0.108)]
        if args.tap_plan_json and os.path.isfile(args.tap_plan_json):
            with open(args.tap_plan_json, "r", encoding="utf-8") as f:
                plan_data = json.load(f)
            tap_plan_2 = [tuple(pt) for pt in plan_data.get("phase2", tap_plan_2)]
        for nx, ny in tap_plan_2:
            tap_norm(serial, nx, ny)

        time.sleep(args.final_sec)
        alive_pid = get_app_pid(serial, args.pkg)
        if not alive_pid:
            raise RuntimeError(f"App {args.pkg} crashed before shot_03!")
        shots.append(capture_screenshot(serial, os.path.join(args.shots_out, "shot_03.png")))
        print(f"[smoke_tt36p] Captured shot_03.png ({shots[-1]['bytes']} B)")

        _, logcat_txt, _ = run_adb(serial, "logcat", "-d", timeout=60.0)
        with open(logcat_path, "w", encoding="utf-8", errors="replace") as f:
            f.write(logcat_txt)

        crashes = analyze_logcat_for_crashes(logcat_txt, args.pkg, alive_pid)
        report = {
            "passed": len(crashes) == 0,
            "avd": args.avd,
            "serial": serial,
            "package": args.pkg,
            "pid": alive_pid,
            "boot_seconds": boot_sec,
            "screenshots": shots,
            "crashes": crashes,
        }
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

        if crashes:
            raise RuntimeError(f"Logcat crash/exception gate failed ({len(crashes)}): {crashes[:3]}")

        print(f"SMOKE_TT36P_PASS | PID={alive_pid} | Shots=3 (1080x2400) | Crashes=0 | Report={report_path}")
    finally:
        if wm_overridden:
            try:
                run_adb(serial, "shell", "wm", "size", "reset", timeout=10.0)
                run_adb(serial, "shell", "wm", "density", "reset", timeout=10.0)
            except Exception:
                pass
        try:
            run_adb(serial, "emu", "kill", timeout=20.0)
        except Exception:
            pass
        try:
            emu_proc.wait(timeout=20.0)
        except subprocess.TimeoutExpired:
            try:
                emu_proc.terminate()
                emu_proc.wait(timeout=5.0)
            except Exception:
                try:
                    emu_proc.kill()
                except Exception:
                    pass
        emu_log_fh.close()


def run_self_test():
    assert os.path.isfile(EMULATOR_EXE), f"Missing {EMULATOR_EXE}"
    assert os.path.isfile(ADB_EXE), f"Missing {ADB_EXE}"
    avd_ini = os.path.join(AVD_HOME, f"{DEFAULT_AVD}.avd", "config.ini")
    assert os.path.isfile(avd_ini), f"Missing {avd_ini}"

    # Verify PNG header parser on synthetic 1080x2400 IHDR
    fake_ihdr = PNG_SIG + struct.pack(">I4sII", 13, b"IHDR", 1080, 2400) + b"\x08\x06\x00\x00\x00" + b"\x00" * 16
    assert parse_png_dimensions(fake_ihdr) == (1080, 2400)

    # Verify logcat crash detector
    clean_log = (
        "11-11 03:00:01.100  4321  4350 I Unity   : [Crusher] Arena booted OK\n"
        "11-11 03:00:02.100  1100  1105 E AndroidRuntime: FATAL EXCEPTION: main\n"
        "11-11 03:00:02.101  1100  1105 E AndroidRuntime: Process: com.google.android.gms, PID: 1100\n"
    )
    assert analyze_logcat_for_crashes(clean_log, DEFAULT_PKG, "4321") == []

    crash_log = (
        "11-11 03:00:05.000  4321  4350 E AndroidRuntime: FATAL EXCEPTION: UnityMain\n"
        f"11-11 03:00:05.001  4321  4350 E AndroidRuntime: Process: {DEFAULT_PKG}, PID: 4321\n"
        "11-11 03:00:06.000  4321  4350 E Unity   : NullReferenceException: Object reference not set\n"
    )
    found = analyze_logcat_for_crashes(crash_log, DEFAULT_PKG, "4321")
    assert len(found) == 2, f"Expected 2 detected issues, got {found}"

    print(f"SMOKE_TT36P_SELF_TEST_PASS | SDK={SDK_ROOT} | AVD={avd_ini} | ParserAndGates=OK")


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Headless Android emulator tt36p smoke test runner.")
    parser.add_argument("--self-test", action="store_true", help="Verify SDK/AVD paths and logcat/PNG gates without starting emulator.")
    parser.add_argument("--apk", default="Builds/CrusherCore-smoke-universal.apk", help="Path to universal smoke APK.")
    parser.add_argument("--avd", default=DEFAULT_AVD, help="AVD name (default: tt36p).")
    parser.add_argument("--port", type=int, default=5576, help="Dedicated even console port for headless emulator.")
    parser.add_argument("--pkg", default=DEFAULT_PKG, help="Android applicationId.")
    parser.add_argument("--activity", default=DEFAULT_ACTIVITY, help="Launch activity class.")
    parser.add_argument("--shots-out", default="web/shots", help="Directory for shot_01.png..shot_03.png.")
    parser.add_argument("--logs-dir", default="Logs", help="Directory for emulator/logcat/report outputs.")
    parser.add_argument("--tap-plan-json", default="", help="Optional JSON file with normalized tap coordinates.")
    parser.add_argument("--boot-timeout", type=float, default=420.0)
    parser.add_argument("--warmup-sec", type=float, default=8.0)
    parser.add_argument("--mid-sec", type=float, default=5.0)
    parser.add_argument("--final-sec", type=float, default=6.0)
    args = parser.parse_args()

    if args.self_test:
        run_self_test()
        return

    run_smoke(args)


if __name__ == "__main__":
    main()
