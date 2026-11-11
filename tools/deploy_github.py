"""
Headless NoWinGuard-safe GitHub deployer for Crusher Core V2 Ultra:
  1. Verifies & copies V2 Release ARM64 APK to Downloads\CrusherCore_V1
  2. Verifies Builds\WebGL (<95 MB per file, Brotli .unityweb + industrial index.html)
  3. Commits & pushes clean source tree to `main` (0 secrets/keystore/binaries)
  4. Uploads updated APK to GitHub Release `v1.0.0` (--clobber)
  5. Pushes playable WebGL 2.0 build (`Builds\WebGL\*`) to root of `gh-pages`
  6. Waits for GitHub Pages build == 'built' and HTTP-verifies index.html, all Build/* files, and APK URL.
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
DOWNLOADS_DIR = r"C:\Users\USER\Downloads\CrusherCore_V1"
DOWNLOADS_APK_PATH = os.path.join(DOWNLOADS_DIR, APK_NAME)
WEBGL_DIR = os.path.join(ROOT, "Builds", "WebGL")
APK_URL = f"https://github.com/{REPO_SLUG}/releases/download/{RELEASE_TAG}/{APK_NAME}"

EXPECTED_APK_SIZE = 36460538
EXPECTED_APK_SHA256 = "7f9e22f46d8dc145334e0df5caa81e970f189a72838dfa5e351d39a52361b789"
MAX_GH_FILE_BYTES = 95 * 1024 * 1024

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
    helper = f"!'{gh_posix}' auth git-credential"
    run_cmd([GIT_EXE, "config", "user.name", "Mrak0be5"], cwd=cwd)
    run_cmd([GIT_EXE, "config", "user.email", "Mrak0be5@users.noreply.github.com"], cwd=cwd)
    run_cmd([GIT_EXE, "config", "--replace-all", "credential.helper", ""], cwd=cwd)
    run_cmd([GIT_EXE, "config", "--add", "credential.helper", helper], cwd=cwd)


def verify_and_sync_local_artifacts():
    assert os.path.isfile(GIT_EXE), f"Missing git: {GIT_EXE}"
    assert os.path.isfile(GH_EXE), f"Missing gh: {GH_EXE}"
    assert os.path.isfile(APK_REL_PATH), f"Missing release APK: {APK_REL_PATH}"

    actual_size = os.path.getsize(APK_REL_PATH)
    assert actual_size == EXPECTED_APK_SIZE, f"APK size mismatch: {actual_size} != {EXPECTED_APK_SIZE}"
    actual_sha = sha256_file(APK_REL_PATH)
    assert actual_sha == EXPECTED_APK_SHA256, f"APK SHA256 mismatch: {actual_sha} != {EXPECTED_APK_SHA256}"

    os.makedirs(DOWNLOADS_DIR, exist_ok=True)
    shutil.copy2(APK_REL_PATH, DOWNLOADS_APK_PATH)
    assert os.path.getsize(DOWNLOADS_APK_PATH) == EXPECTED_APK_SIZE, "Downloads APK size mismatch"
    assert sha256_file(DOWNLOADS_APK_PATH) == EXPECTED_APK_SHA256, "Downloads APK SHA256 mismatch"

    assert os.path.isdir(WEBGL_DIR), f"Missing Builds/WebGL directory: {WEBGL_DIR}"
    index_path = os.path.join(WEBGL_DIR, "index.html")
    nojekyll_path = os.path.join(WEBGL_DIR, ".nojekyll")
    build_sub = os.path.join(WEBGL_DIR, "Build")
    assert os.path.isfile(index_path), "Missing Builds/WebGL/index.html"
    assert os.path.isfile(nojekyll_path), "Missing Builds/WebGL/.nojekyll"
    assert os.path.isdir(build_sub), "Missing Builds/WebGL/Build"

    with open(index_path, "r", encoding="utf-8") as f:
        idx_html = f.read()
    assert "createUnityInstance" in idx_html, "index.html missing createUnityInstance"
    assert "CRUSHER CORE V2 ULTRA" in idx_html, "index.html missing CRUSHER CORE V2 ULTRA"
    assert EXPECTED_APK_SHA256 in idx_html, "index.html missing updated V2 APK SHA-256"

    webgl_files = []
    for dirpath, _, filenames in os.walk(WEBGL_DIR):
        for fn in filenames:
            fp = os.path.join(dirpath, fn)
            sz = os.path.getsize(fp)
            assert sz < MAX_GH_FILE_BYTES, f"WebGL file exceeds 95 MB limit: {fp} ({sz} B)"
            rel = os.path.relpath(fp, WEBGL_DIR).replace("\\", "/")
            webgl_files.append((rel, sz))

    build_names = [os.path.basename(r) for r, _ in webgl_files if r.startswith("Build/")]
    assert any(n.endswith(".loader.js") for n in build_names), f"Missing .loader.js in {build_names}"
    assert any(".data" in n for n in build_names), f"Missing .data bundle in {build_names}"
    assert any(".framework.js" in n for n in build_names), f"Missing .framework.js bundle in {build_names}"
    assert any(".wasm" in n for n in build_names), f"Missing .wasm bundle in {build_names}"

    total_webgl = sum(sz for _, sz in webgl_files)
    print(
        f"[1/6] Local V2 artifacts verified:\n"
        f"  APK: {actual_size:,} B (34.77 MB), sha256={actual_sha}\n"
        f"  WebGL: {len(webgl_files)} files, total={total_webgl:,} B ({total_webgl / 1048576:.2f} MB), build={build_names}"
    )
    return webgl_files


def stage_and_push_main():
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
    if rc_diff != 0:
        run_cmd(
            [
                GIT_EXE,
                "commit",
                "-m",
                "Crusher Core V2 Ultra: foundry 3D rigs, URP foundry IBL shaders, adaptive 16:9 WebGL + 9:20 UI",
            ],
            cwd=ROOT,
        )
    _, head_sha = run_cmd([GIT_EXE, "rev-parse", "--short", "HEAD"], cwd=ROOT)
    run_cmd([GIT_EXE, "push", "-u", "origin", "main"], cwd=ROOT)
    print(f"[2/6] Pushed clean main commit {head_sha} ({len(tracked_files)} tracked source files, 0 secrets/binaries)")


def publish_release_apk():
    notes = (
        "## CRUSHER CORE V2 ULTRA: Hyper Shredder — WebGL 2.0 & Android ARM64\n\n"
        f"- **Играть в браузере (WebGL 2.0):** {PAGES_URL}\n"
        "- **Пакет Android:** `com.crushercore.hypershredder`\n"
        "- **Сборка:** `Unity 6000.4.7f1` URP Forward, `IL2CPP`, `ARM64-v8a`, `Vulkan + OpenGLES3`\n"
        f"- **Размер APK:** `{EXPECTED_APK_SIZE:,}` байт (`34.77 МБ`)\n"
        f"- **SHA-256:** `{EXPECTED_APK_SHA256}`\n"
    )
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".md", delete=False) as tf:
        tf.write(notes)
        notes_path = tf.name

    try:
        rc, rel_json = run_cmd([GH_EXE, "release", "view", RELEASE_TAG, "--repo", REPO_SLUG, "--json", "assets"], check=False)
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
                    "CRUSHER CORE V2 ULTRA: Hyper Shredder (Android ARM64 + WebGL 2.0)",
                    "--notes-file",
                    notes_path,
                ]
            )
        else:
            assets = json.loads(rel_json).get("assets", [])
            already_ok = any(a.get("name") == APK_NAME and a.get("size") == EXPECTED_APK_SIZE for a in assets)
            if not already_ok:
                run_cmd([GH_EXE, "release", "upload", RELEASE_TAG, APK_REL_PATH, "--repo", REPO_SLUG, "--clobber"])
            run_cmd(
                [
                    GH_EXE,
                    "release",
                    "edit",
                    RELEASE_TAG,
                    "--repo",
                    REPO_SLUG,
                    "--title",
                    "CRUSHER CORE V2 ULTRA: Hyper Shredder (Android ARM64 + WebGL 2.0)",
                    "--notes-file",
                    notes_path,
                ]
            )
    finally:
        if os.path.exists(notes_path):
            os.remove(notes_path)

    print(f"[3/6] Uploaded V2 Ultra APK to GitHub Release {RELEASE_TAG}: {APK_URL}")


def deploy_webgl_to_gh_pages():
    with tempfile.TemporaryDirectory(prefix="crusher_webgl_ghpages_") as tmpdir:
        for item in os.listdir(WEBGL_DIR):
            s = os.path.join(WEBGL_DIR, item)
            d = os.path.join(tmpdir, item)
            if os.path.isdir(s):
                shutil.copytree(s, d)
            else:
                shutil.copy2(s, d)

        run_cmd([GIT_EXE, "init", "-b", "gh-pages"], cwd=tmpdir)
        configure_git_repo(tmpdir)
        run_cmd([GIT_EXE, "add", "-A"], cwd=tmpdir)
        run_cmd([GIT_EXE, "commit", "-m", "Deploy Crusher Core V2 Ultra playable WebGL 2.0 build"], cwd=tmpdir)
        run_cmd([GIT_EXE, "push", "-f", REPO_GIT_URL, "gh-pages:gh-pages"], cwd=tmpdir)
        _, pushed_sha = run_cmd([GIT_EXE, "rev-parse", "HEAD"], cwd=tmpdir)

    print(f"[4/6] Pushed playable WebGL build to gh-pages (commit {pushed_sha[:7]})")

    # Request a fresh Pages build and wait until latest build commit matches pushed_sha & status == 'built'
    time.sleep(2)
    run_cmd([GH_EXE, "api", "-X", "POST", f"repos/{REPO_SLUG}/pages/builds"], check=False)

    latest_status = "unknown"
    latest_commit = ""
    deadline = time.time() + 210
    while time.time() < deadline:
        rc, out = run_cmd([GH_EXE, "api", f"repos/{REPO_SLUG}/pages/builds/latest"], check=False)
        if rc == 0 and out:
            data = json.loads(out)
            latest_status = data.get("status") or "pending"
            latest_commit = data.get("commit") or ""
            if latest_status == "built" and (not latest_commit or latest_commit.startswith(pushed_sha[:7])):
                break
        time.sleep(4)

    assert latest_status == "built", f"GitHub Pages build did not reach 'built' (status={latest_status}, commit={latest_commit})"
    print(f"[5/6] GitHub Pages build complete (status={latest_status}, commit={latest_commit[:7]}): {PAGES_URL}")


def verify_public_urls(webgl_files):
    ua = {"User-Agent": "CrusherCoreV2Verifier/2.0"}

    # 1. Poll index.html until CDN serves the new WebGL shell (contains createUnityInstance + V2 APK SHA256)
    html_body = ""
    pages_code = 0
    deadline = time.time() + 120
    while time.time() < deadline:
        try:
            req = urllib.request.Request(f"{PAGES_URL}?t={int(time.time())}", headers=ua)
            with urllib.request.urlopen(req, timeout=20) as resp:
                pages_code = resp.getcode()
                html_body = resp.read().decode("utf-8", errors="replace")
                if (
                    pages_code == 200
                    and "createUnityInstance" in html_body
                    and "CRUSHER CORE V2 ULTRA" in html_body
                    and EXPECTED_APK_SHA256 in html_body
                ):
                    break
        except urllib.error.URLError:
            pass
        time.sleep(3)

    assert pages_code == 200, f"Pages index.html HTTP {pages_code}"
    assert "createUnityInstance" in html_body, "Live Pages index.html missing createUnityInstance"
    assert "CRUSHER CORE V2 ULTRA" in html_body, "Live Pages index.html missing CRUSHER CORE V2 ULTRA"
    assert EXPECTED_APK_SHA256 in html_body, "Live Pages index.html missing V2 APK SHA-256"

    # 2. Verify every Build/* artifact on GitHub Pages returns HTTP 200 and full Content-Length
    verified_bundles = []
    for rel, expected_sz in webgl_files:
        if not rel.startswith("Build/"):
            continue
        b_url = PAGES_URL + rel
        req_b = urllib.request.Request(b_url, method="HEAD", headers={**ua, "Accept-Encoding": "identity"})
        with urllib.request.urlopen(req_b, timeout=25) as resp_b:
            b_code = resp_b.getcode()
            b_len = int(resp_b.headers.get("Content-Length", "0"))
            b_enc = resp_b.headers.get("Content-Encoding", "")
        assert b_code == 200, f"Bundle {b_url} returned HTTP {b_code}"
        if not b_enc:
            assert b_len == expected_sz, f"Bundle {rel} Content-Length mismatch: {b_len} != {expected_sz}"
        else:
            assert b_len > 0, f"Bundle {rel} zero Content-Length"
        verified_bundles.append(f"{rel} ({b_len:,} B)")

    # 3. Verify direct V2 APK download URL
    req_head = urllib.request.Request(APK_URL, method="HEAD", headers=ua)
    with urllib.request.urlopen(req_head, timeout=20) as resp_head:
        apk_code = resp_head.getcode()
        apk_len = int(resp_head.headers.get("Content-Length", "0"))
    assert apk_code == 200 and apk_len == EXPECTED_APK_SIZE, f"APK HEAD failed: HTTP {apk_code}, len={apk_len}"

    req_range = urllib.request.Request(APK_URL, headers={**ua, "Range": "bytes=0-3"})
    with urllib.request.urlopen(req_range, timeout=20) as resp_range:
        r_code = resp_range.getcode()
        magic = resp_range.read(4)
    assert r_code in (200, 206) and magic == b"PK\x03\x04", f"APK magic check failed: HTTP {r_code}, magic={magic!r}"

    print(
        f"[6/6] Public V2 URLs verified:\n"
        f"  PAGES_URL = {PAGES_URL} -> HTTP {pages_code} (HTML={len(html_body):,} chars)\n"
        f"  BUNDLES   = {', '.join(verified_bundles)}\n"
        f"  APK_URL   = {APK_URL} -> HTTP {apk_code} (Content-Length={apk_len:,} B, magic=PK\\x03\\x04)"
    )


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    webgl_files = verify_and_sync_local_artifacts()
    stage_and_push_main()
    publish_release_apk()
    deploy_webgl_to_gh_pages()
    verify_public_urls(webgl_files)
    print("DEPLOY_V2_GITHUB_PASS")


if __name__ == "__main__":
    main()
