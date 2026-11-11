"""
Headless NoWinGuard-safe GitHub source, Release APK, and GitHub Pages deployer for Crusher Core V1.
Spawns every child process with CREATE_NO_WINDOW (0x08000000) + SW_HIDE and non-interactive git/gh env.
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

CREATE_NO_WINDOW = 0x08000000

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
GIT_EXE = r"C:\Program Files\Git\cmd\git.exe"
GH_EXE = r"C:\Program Files\GitHub CLI\gh.exe"

REPO_SLUG = "Mrak0be5/crusher-core"
REPO_GIT_URL = f"https://github.com/{REPO_SLUG}.git"
PAGES_URL = "https://mrak0be5.github.io/crusher-core/"
RELEASE_TAG = "v1.0.0"
APK_NAME = "CrusherCore-v1.0.0-arm64.apk"
APK_REL_PATH = os.path.join(ROOT, "Builds", APK_NAME)
APK_URL = f"https://github.com/{REPO_SLUG}/releases/download/{RELEASE_TAG}/{APK_NAME}"

EXPECTED_APK_SIZE = 33865422
EXPECTED_APK_SHA256 = "f739f85c4c241eceac9a5ed9304650c03d7fcad5126b708c44f82433a3bbcead"

FORBIDDEN_PREFIXES = (
    "keystore/",
    "library/",
    "temp/",
    "obj/",
    ".utmp/",
    "build/",
    "builds/",
    "logs/",
    "staging/",
    "web/",
    "usersettings/",
    "memorycaptures/",
    "recordings/",
)
FORBIDDEN_SUFFIXES = (
    ".keystore",
    ".jks",
    ".apk",
    ".aab",
    "keystore_meta.json",
    ".env",
)


def run_cmd(args, cwd=ROOT, check=True):
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0  # SW_HIDE

    env = os.environ.copy()
    env["GIT_TERMINAL_PROMPT"] = "0"
    env["GCM_INTERACTIVE"] = "never"
    env["GH_PROMPT_DISABLED"] = "1"
    env["NO_COLOR"] = "1"

    proc = subprocess.run(
        args,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        creationflags=CREATE_NO_WINDOW,
        startupinfo=si,
    )
    out = proc.stdout.decode("utf-8", errors="replace").strip()
    if check and proc.returncode != 0:
        raise RuntimeError(f"Command failed ({proc.returncode}): {args[0]} {args[1:3]}\n{out}")
    return proc.returncode, out


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(1024 * 1024)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def configure_git_repo(cwd):
    gh_posix = GH_EXE.replace("\\", "/")
    helper = f"!'${{GH_DUMMY:-{gh_posix}}}' auth git-credential"
    # Direct single-quoted POSIX path works cleanly under Git's /bin/sh on Windows
    helper = f"!'{gh_posix}' auth git-credential"
    run_cmd([GIT_EXE, "config", "user.name", "Mrak0be5"], cwd=cwd)
    run_cmd([GIT_EXE, "config", "user.email", "Mrak0be5@users.noreply.github.com"], cwd=cwd)
    # Clear system/global credential helpers (e.g. GCM UI) in local config first
    run_cmd([GIT_EXE, "config", "--replace-all", "credential.helper", ""], cwd=cwd)
    run_cmd([GIT_EXE, "config", "--add", "credential.helper", helper], cwd=cwd)


def verify_local_inputs():
    assert os.path.isfile(GIT_EXE), f"Missing git: {GIT_EXE}"
    assert os.path.isfile(GH_EXE), f"Missing gh: {GH_EXE}"
    assert os.path.isfile(APK_REL_PATH), f"Missing release APK: {APK_REL_PATH}"
    actual_size = os.path.getsize(APK_REL_PATH)
    assert actual_size == EXPECTED_APK_SIZE, f"APK size mismatch: {actual_size} != {EXPECTED_APK_SIZE}"
    actual_sha = sha256_file(APK_REL_PATH)
    assert actual_sha == EXPECTED_APK_SHA256, f"APK SHA256 mismatch: {actual_sha} != {EXPECTED_APK_SHA256}"

    web_dir = os.path.join(ROOT, "web")
    assert os.path.isfile(os.path.join(web_dir, "index.html")), "Missing web/index.html"
    assert os.path.isfile(os.path.join(web_dir, ".nojekyll")), "Missing web/.nojekyll"
    req_shots = [
        "shot_01.png",
        "shot_02.png",
        "shot_03.png",
        "gate2_03_w45_blender_deathray.png",
        "gate2_04_w85_gt_bh_moltencore.png",
    ]
    for s in req_shots:
        sp = os.path.join(web_dir, "shots", s)
        assert os.path.isfile(sp) and os.path.getsize(sp) > 100_000, f"Missing/invalid web shot: {sp}"
    print(f"[1/6] Local APK & web inputs verified (size={actual_size}, sha256={actual_sha[:16]}...)")


def stage_and_verify_main():
    if not os.path.isdir(os.path.join(ROOT, ".git")):
        run_cmd([GIT_EXE, "init", "-b", "main"], cwd=ROOT)
    else:
        run_cmd([GIT_EXE, "checkout", "-B", "main"], cwd=ROOT)

    configure_git_repo(ROOT)
    run_cmd([GIT_EXE, "reset"], cwd=ROOT)
    run_cmd([GIT_EXE, "add", "-A"], cwd=ROOT)

    _, tracked_out = run_cmd([GIT_EXE, "ls-files"], cwd=ROOT)
    tracked_files = [line.strip() for line in tracked_out.splitlines() if line.strip()]
    assert len(tracked_files) > 20, f"Unexpectedly few tracked files: {len(tracked_files)}"

    violations = []
    for rel in tracked_files:
        low = rel.replace("\\", "/").lower()
        if any(low.startswith(p) for p in FORBIDDEN_PREFIXES) or any(low.endswith(s) for s in FORBIDDEN_SUFFIXES):
            violations.append(rel)
            continue
        full = os.path.join(ROOT, rel)
        if os.path.isfile(full) and os.path.getsize(full) > 25 * 1024 * 1024:
            violations.append(f"{rel} (>25MB)")

    if violations:
        run_cmd([GIT_EXE, "reset"], cwd=ROOT)
        raise AssertionError(f"Forbidden files staged in git: {violations}")

    rc_diff, _ = run_cmd([GIT_EXE, "diff", "--cached", "--quiet"], cwd=ROOT, check=False)
    rc_head, _ = run_cmd([GIT_EXE, "rev-parse", "--verify", "HEAD"], cwd=ROOT, check=False)
    if rc_diff != 0 or rc_head != 0:
        run_cmd(
            [GIT_EXE, "commit", "-m", "Initial release: Crusher Core V1 (Unity 6 URP, procedural 3D/shaders/SFX/UI)"],
            cwd=ROOT,
        )
    _, head_sha = run_cmd([GIT_EXE, "rev-parse", "--short", "HEAD"], cwd=ROOT)
    print(f"[2/6] Clean main commit ready: {head_sha} ({len(tracked_files)} tracked source files, 0 secrets/binaries)")


def ensure_repo_and_push_main():
    rc, info = run_cmd([GH_EXE, "repo", "view", REPO_SLUG, "--json", "name,isPrivate,url"], check=False)
    if rc != 0:
        run_cmd(
            [
                GH_EXE,
                "repo",
                "create",
                REPO_SLUG,
                "--public",
                "--description",
                "CRUSHER CORE: Hyper Shredder — 100% Procedural 3D Android Game (Unity 6 URP, IL2CPP ARM64)",
            ]
        )
    else:
        meta = json.loads(info)
        if meta.get("isPrivate"):
            run_cmd([GH_EXE, "repo", "edit", REPO_SLUG, "--visibility", "public", "--accept-visibility-change-consequences"])

    rc_rem, _ = run_cmd([GIT_EXE, "remote", "get-url", "origin"], cwd=ROOT, check=False)
    if rc_rem == 0:
        run_cmd([GIT_EXE, "remote", "set-url", "origin", REPO_GIT_URL], cwd=ROOT)
    else:
        run_cmd([GIT_EXE, "remote", "add", "origin", REPO_GIT_URL], cwd=ROOT)

    run_cmd([GIT_EXE, "push", "-u", "-f", "origin", "main"], cwd=ROOT)
    print(f"[3/6] Pushed main branch to https://github.com/{REPO_SLUG}")


def publish_release_apk():
    notes = (
        "## CRUSHER CORE: Hyper Shredder — Android v1.0.0\n\n"
        "- **Пакет:** `com.crushercore.hypershredder`\n"
        "- **Сборка:** `Unity 6000.4.7f1` URP Forward, `IL2CPP`, `ARM64-v8a`, `Vulkan + OpenGLES3`\n"
        f"- **Размер APK:** `{EXPECTED_APK_SIZE:,}` байт (`32.30 МБ`)\n"
        f"- **SHA-256:** `{EXPECTED_APK_SHA256}`\n"
        f"- **Витрина с QR-кодом:** {PAGES_URL}\n"
    )
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".md", delete=False) as tf:
        tf.write(notes)
        notes_path = tf.name

    try:
        rc, _ = run_cmd([GH_EXE, "release", "view", RELEASE_TAG, "--repo", REPO_SLUG], check=False)
        if rc != 0:
            run_cmd(
                [
                    GH_EXE,
                    "release",
                    "create",
                    RELEASE_TAG,
                    APK_REL_PATH,
                    "--repo",
                    REPO_SLUG,
                    "--target",
                    "main",
                    "--title",
                    "CRUSHER CORE: Hyper Shredder v1.0.0 (Android ARM64)",
                    "--notes-file",
                    notes_path,
                ]
            )
        else:
            run_cmd([GH_EXE, "release", "upload", RELEASE_TAG, APK_REL_PATH, "--repo", REPO_SLUG, "--clobber"])
            run_cmd([GH_EXE, "release", "edit", RELEASE_TAG, "--repo", REPO_SLUG, "--notes-file", notes_path])
    finally:
        if os.path.exists(notes_path):
            os.remove(notes_path)

    print(f"[4/6] Published GitHub Release {RELEASE_TAG}: {APK_URL}")


def deploy_gh_pages():
    web_src = os.path.join(ROOT, "web")
    with tempfile.TemporaryDirectory(prefix="crusher_ghpages_") as tmpdir:
        for item in os.listdir(web_src):
            s = os.path.join(web_src, item)
            d = os.path.join(tmpdir, item)
            if os.path.isdir(s):
                shutil.copytree(s, d)
            else:
                shutil.copy2(s, d)

        run_cmd([GIT_EXE, "init", "-b", "gh-pages"], cwd=tmpdir)
        configure_git_repo(tmpdir)
        run_cmd([GIT_EXE, "add", "-A"], cwd=tmpdir)
        run_cmd([GIT_EXE, "commit", "-m", "Deploy Crusher Core V1 landing page & QR showcase"], cwd=tmpdir)
        run_cmd([GIT_EXE, "push", "-f", REPO_GIT_URL, "gh-pages:gh-pages"], cwd=tmpdir)

    rc_pages, _ = run_cmd([GH_EXE, "api", f"repos/{REPO_SLUG}/pages"], check=False)
    if rc_pages != 0:
        run_cmd(
            [
                GH_EXE,
                "api",
                "-X",
                "POST",
                f"repos/{REPO_SLUG}/pages",
                "-f",
                "source[branch]=gh-pages",
                "-f",
                "source[path]=/",
            ]
        )
    else:
        run_cmd(
            [
                GH_EXE,
                "api",
                "-X",
                "PUT",
                f"repos/{REPO_SLUG}/pages",
                "-f",
                "source[branch]=gh-pages",
                "-f",
                "source[path]=/",
            ],
            check=False,
        )
        run_cmd([GH_EXE, "api", "-X", "POST", f"repos/{REPO_SLUG}/pages/builds"], check=False)

    status = "unknown"
    deadline = time.time() + 150
    while time.time() < deadline:
        rc, out = run_cmd([GH_EXE, "api", f"repos/{REPO_SLUG}/pages"], check=False)
        if rc == 0 and out:
            data = json.loads(out)
            status = data.get("status") or "pending"
            if status == "built":
                break
        time.sleep(4)

    print(f"[5/6] GitHub Pages gh-pages deployed (API status={status}): {PAGES_URL}")


def verify_public_urls():
    ua = {"User-Agent": "CrusherCoreVerifier/1.0"}

    # 1. Verify GitHub Pages HTML (allow brief CDN propagation)
    html_body = ""
    pages_code = 0
    deadline = time.time() + 90
    while time.time() < deadline:
        try:
            req = urllib.request.Request(PAGES_URL, headers=ua)
            with urllib.request.urlopen(req, timeout=15) as resp:
                pages_code = resp.getcode()
                html_body = resp.read().decode("utf-8", errors="replace")
                if pages_code == 200 and EXPECTED_APK_SHA256 in html_body:
                    break
        except urllib.error.URLError:
            pass
        time.sleep(3)

    assert pages_code == 200, f"Landing page did not return HTTP 200 (got {pages_code})"
    assert "CRUSHER CORE" in html_body and "<svg" in html_body, "Landing page missing title or inline QR SVG"
    assert EXPECTED_APK_SHA256 in html_body, "Landing page missing expected SHA-256"
    assert APK_URL in html_body, "Landing page missing direct APK URL"

    # 2. Verify live screenshot served from Pages
    shot_url = PAGES_URL + "shots/shot_01.png"
    req_shot = urllib.request.Request(shot_url, method="HEAD", headers=ua)
    with urllib.request.urlopen(req_shot, timeout=15) as resp_shot:
        shot_code = resp_shot.getcode()
        shot_len = int(resp_shot.headers.get("Content-Length", "0"))
    assert shot_code == 200 and shot_len == 1924058, f"Pages shot_01 check failed: HTTP {shot_code}, len={shot_len}"

    # 3. Verify direct GitHub Release APK link (HEAD + 4-byte Range magic check)
    req_head = urllib.request.Request(APK_URL, method="HEAD", headers=ua)
    with urllib.request.urlopen(req_head, timeout=20) as resp_head:
        apk_head_code = resp_head.getcode()
        apk_head_len = int(resp_head.headers.get("Content-Length", "0"))
    assert apk_head_code == 200, f"APK HEAD failed: HTTP {apk_head_code}"
    assert apk_head_len == EXPECTED_APK_SIZE, f"APK Content-Length mismatch: {apk_head_len} != {EXPECTED_APK_SIZE}"

    range_headers = dict(ua)
    range_headers["Range"] = "bytes=0-3"
    req_range = urllib.request.Request(APK_URL, headers=range_headers)
    with urllib.request.urlopen(req_range, timeout=20) as resp_range:
        range_code = resp_range.getcode()
        magic = resp_range.read(4)
        content_range = resp_range.headers.get("Content-Range", "")
    assert range_code in (200, 206), f"APK Range GET failed: HTTP {range_code}"
    assert magic == b"PK\x03\x04", f"APK magic header invalid: {magic!r}"

    print(
        f"[6/6] Public HTTP verified:\n"
        f"  PAGES_URL = {PAGES_URL} -> HTTP {pages_code} (HTML={len(html_body)} chars, shot_01={shot_len} B)\n"
        f"  APK_URL   = {APK_URL} -> HTTP {apk_head_code} (Content-Length={apk_head_len} B, Content-Range='{content_range}', magic=PK\\x03\\x04)"
    )


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    verify_local_inputs()
    stage_and_verify_main()
    ensure_repo_and_push_main()
    publish_release_apk()
    deploy_gh_pages()
    verify_public_urls()
    print("DEPLOY_GITHUB_PASS")


if __name__ == "__main__":
    main()
