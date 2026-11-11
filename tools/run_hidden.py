"""
NoWinGuard-safe child process runner for Crusher Core V1.
Always spawns child processes with CREATE_NO_WINDOW (0x08000000) and SW_HIDE.

Usage:
  python tools/run_hidden.py [--cwd DIR] [--timeout SEC] [--log FILE] -- <exe> [args...]
  python tools/run_hidden.py <exe> [args...]
"""
import argparse
import os
import subprocess
import sys

CREATE_NO_WINDOW = 0x08000000


def run_hidden(cmd, cwd=None, timeout=None, log_path=None, env=None):
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0  # SW_HIDE

    proc = subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=timeout,
        creationflags=CREATE_NO_WINDOW,
        startupinfo=si,
    )
    out_text = proc.stdout.decode("utf-8", errors="replace")
    if log_path:
        os.makedirs(os.path.dirname(os.path.abspath(log_path)), exist_ok=True)
        with open(log_path, "w", encoding="utf-8") as f:
            f.write(out_text)
    return proc.returncode, out_text


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    argv = sys.argv[1:]
    if "--" in argv:
        sep = argv.index("--")
        opt_args, cmd = argv[:sep], argv[sep + 1 :]
        parser = argparse.ArgumentParser(add_help=False)
        parser.add_argument("--cwd", default=None)
        parser.add_argument("--timeout", type=float, default=None)
        parser.add_argument("--log", default=None)
        opts = parser.parse_args(opt_args)
        cwd, timeout, log_path = opts.cwd, opts.timeout, opts.log
    else:
        cmd = argv
        cwd, timeout, log_path = None, None, None

    if not cmd:
        print("Usage: run_hidden.py [--cwd DIR] [--timeout SEC] [--log FILE] -- <exe> [args...]", file=sys.stderr)
        sys.exit(2)

    code, out = run_hidden(cmd, cwd=cwd, timeout=timeout, log_path=log_path)
    if out:
        sys.stdout.write(out)
        if not out.endswith("\n"):
            sys.stdout.write("\n")
    sys.exit(code)


if __name__ == "__main__":
    main()
