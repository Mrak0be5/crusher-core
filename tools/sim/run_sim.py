import os
import subprocess
import sys
from pathlib import Path

CREATE_NO_WINDOW = 0x08000000

ROOT = Path(r"C:\Users\USER\.gemini\antigravity\scratch\CrusherCore")
SIM_DIR = ROOT / "tools" / "sim"
BIN_DIR = SIM_DIR / "bin"
DOTNET_EXE = Path(r"C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Data\NetCoreRuntime\dotnet.exe")
CSC_DLL = Path(r"C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Data\DotNetSdkRoslyn\csc.dll")
REF_DIR = Path(r"C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Data\NetCoreRuntime\shared\Microsoft.NETCore.App\8.0.16")


def main() -> int:
    BIN_DIR.mkdir(parents=True, exist_ok=True)
    out_dll = BIN_DIR / "HeadlessRunner.dll"
    rt_cfg = BIN_DIR / "HeadlessRunner.runtimeconfig.json"

    rt_cfg.write_text(
        '{\n'
        '  "runtimeOptions": {\n'
        '    "tfm": "net8.0",\n'
        '    "framework": {\n'
        '      "name": "Microsoft.NETCore.App",\n'
        '      "version": "8.0.0"\n'
        '    },\n'
        '    "rollForward": "Minor"\n'
        '  }\n'
        '}\n',
        encoding="utf-8",
    )

    cs_files = [
        str(ROOT / "Assets" / "Scripts" / "Config" / "GameIdsAndScale.cs"),
        str(ROOT / "staging" / "sim_src" / "Config" / "BalanceTables.cs"),
        str(ROOT / "staging" / "sim_src" / "Sim" / "CombatSim.cs"),
        str(ROOT / "staging" / "sim_src" / "Sim" / "GameSession.cs"),
        str(ROOT / "staging" / "sim_src" / "Core" / "SaveSystem.cs"),
        str(SIM_DIR / "Program.cs"),
    ]

    ref_dlls = [
        "System.Private.CoreLib.dll",
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Threading.dll",
        "System.Threading.Tasks.Parallel.dll",
        "System.IO.FileSystem.dll",
        "System.Runtime.Extensions.dll",
        "netstandard.dll",
    ]
    refs = [f"-r:{REF_DIR / r}" for r in ref_dlls if (REF_DIR / r).exists()]

    compile_cmd = [
        str(DOTNET_EXE),
        str(CSC_DLL),
        "-nologo",
        "-optimize+",
        "-target:exe",
        f"-out:{out_dll}",
        *refs,
        *cs_files,
    ]

    res = subprocess.run(
        compile_cmd,
        capture_output=True,
        text=True,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode != 0:
        print("CSC COMPILE ERROR:")
        print(res.stdout)
        print(res.stderr)
        return res.returncode

    if res.stdout.strip():
        print(res.stdout.strip())

    run_res = subprocess.run(
        [str(DOTNET_EXE), str(out_dll)],
        capture_output=True,
        text=True,
        creationflags=CREATE_NO_WINDOW,
    )
    print(run_res.stdout)
    if run_res.stderr.strip():
        print("STDERR:", run_res.stderr)
    return run_res.returncode


if __name__ == "__main__":
    sys.exit(main())
