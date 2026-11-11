#!/usr/bin/env python3
"""Master headless pipeline: builds all 14 FBX models and runs Cycles STOP-GATE 1B."""
from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from run_blender_headless import run_blender_script  # noqa: E402


def main() -> int:
    steps = [
        "build_hero_and_uws.py",
        "build_enemies_and_debris.py",
        "qa_gate_runner.py",
    ]
    for step in steps:
        print(f"\n>>> Running {step} ...")
        rc = run_blender_script(os.path.join(HERE, step))
        if rc != 0:
            print(f"[FAIL] {step} exited with code {rc}")
            return rc
    print("\n[OK] All 14 FBX models generated and STOP-GATE 1B passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
