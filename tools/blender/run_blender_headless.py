#!/usr/bin/env python3
"""Headless runner for Blender 5.2 enforcing NoWinGuard (CREATE_NO_WINDOW = 0x08000000)."""
from __future__ import annotations

import os
import pathlib
import subprocess
import sys

BLENDER_EXE = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
CREATE_NO_WINDOW = 0x08000000


def guard_line_count() -> int:
    p = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "CodexTools" / "NoWinGuard" / "guard.log"
    if not p.exists():
        return 0
    return len(p.read_text(encoding="utf-8", errors="ignore").splitlines())


def run_blender_script(script_path: str, script_args: list[str] | None = None, timeout: int = 600) -> int:
    if not os.path.exists(BLENDER_EXE):
        raise FileNotFoundError(f"Blender 5.2 not found at {BLENDER_EXE}")
    before_guard = guard_line_count()
    cmd = [
        BLENDER_EXE,
        "-b",
        "--factory-startup",
        "-P",
        os.path.abspath(script_path),
    ]
    if script_args:
        cmd.append("--")
        cmd.extend(script_args)

    startupinfo = subprocess.STARTUPINFO()
    startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startupinfo.wShowWindow = 0  # SW_HIDE

    proc = subprocess.run(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=CREATE_NO_WINDOW,
        startupinfo=startupinfo,
        timeout=timeout,
    )
    sys.stdout.write(proc.stdout)
    after_guard = guard_line_count()
    if after_guard > before_guard:
        sys.stderr.write(f"[WARN] NoWinGuard log grew from {before_guard} to {after_guard} lines!\n")
    else:
        print(f"[NoWinGuard] OK (guard.log unchanged at {after_guard} lines)")
    return proc.returncode


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python run_blender_headless.py <script.py> [args...]")
        sys.exit(2)
    rc = run_blender_script(sys.argv[1], sys.argv[2:])
    sys.exit(rc)
