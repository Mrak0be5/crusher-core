"""Master headless runner for Crusher Core V2 Ultra (Wave A2):
1) Generates SM_ArenaFoundryEnv + upgraded Hero, Satellites & Projectile -> staging/models_v2/
2) Generates upgraded Bestiary, Boss_ATM, Voronoi Debris & Gold Ingot -> staging/models_v2/
3) Runs Cycles showcase render (build/qa/v2_models_showcase.png), contact sheets & 15-FBX round-trip QA
Strictly uses CREATE_NO_WINDOW (0x08000000) via run_blender_headless.py and checks NoWinGuard."""
from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from run_blender_headless import run_blender_script  # noqa: E402


def main():
    steps = [
        "build_v2_foundry_and_hero.py",
        "build_v2_enemies_and_debris.py",
        "qa_v2_runner.py",
    ]
    for st in steps:
        path = os.path.join(HERE, st)
        print(f"\n>>> RUNNING HEADLESS V2 BLENDER STEP: {st} <<<", flush=True)
        rc = run_blender_script(path, timeout=900)
        if rc != 0:
            print(f"[FATAL] Step {st} failed with exit code {rc}", file=sys.stderr)
            sys.exit(rc)
    print("\n=== ALL V2 ULTRA BLENDER ASSETS & SHOWCASE QA COMPLETED CLEANLY ===")


if __name__ == "__main__":
    main()
