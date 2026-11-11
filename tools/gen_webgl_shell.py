"""
Crusher Core V2 Ultra — Industrial Fullscreen WebGL Shell Generator (`Builds/WebGL/index.html`).
Wraps Unity 6 WebGL output (`Build/*.loader.js` + `.unityweb`) into a responsive 100vw x 100vh
dark-metallic shell (16:9 Desktop + 9:20 Mobile) with:
  - Reactor preloader (0%..100%)
  - Thin 36px top telemetry/control bar (hotkeys hint, Fullscreen button, Android APK + QR modal)
  - Automatic browser AudioContext unlock & canvas keyboard focus
  - `.nojekyll` marker for GitHub Pages compatibility

Usage:
  python tools/gen_webgl_shell.py --build-dir Builds/WebGL
  python tools/gen_webgl_shell.py --self-test
"""
import argparse
import hashlib
import html
import json
import os
import sys
import tempfile

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from gen_landing import encode_qr_matrix, qr_matrix_to_svg, verify_and_decode_qr_matrix  # noqa: E402

DEFAULT_APK_URL = "https://github.com/Mrak0be5/crusher-core/releases/download/v1.0.0/CrusherCore-v1.0.0-arm64.apk"
DEFAULT_APK_SHA256 = "7f9e22f46d8dc145334e0df5caa81e970f189a72838dfa5e351d39a52361b789"
DEFAULT_APK_SIZE_MB = 34.77


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(1024 * 1024)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def discover_unity_webgl_files(build_root: str) -> dict[str, str]:
    sub = os.path.join(build_root, "Build")
    if not os.path.isdir(sub):
        raise FileNotFoundError(f"Unity WebGL Build subfolder not found: {sub}")

    entries = sorted(os.listdir(sub))

    def pick(suffixes: tuple[str, ...], kind: str) -> str:
        for sfx in suffixes:
            matches = [e for e in entries if e.endswith(sfx)]
            if matches:
                # Prefer shortest prefix match if multiple exist
                matches.sort(key=len)
                return f"Build/{matches[0]}"
        raise FileNotFoundError(f"Could not find WebGL {kind} file in {sub} (files: {entries})")

    loader = pick((".loader.js",), "loader")
    data = pick((".data.unityweb", ".data.br", ".data.gz", ".data"), "data")
    framework = pick(
        (".framework.js.unityweb", ".framework.js.br", ".framework.js.gz", ".framework.js"),
        "framework",
    )
    wasm = pick((".wasm.unityweb", ".wasm.br", ".wasm.gz", ".wasm"), "wasm")

    return {
        "loaderUrl": loader,
        "dataUrl": data,
        "frameworkUrl": framework,
        "codeUrl": wasm,
    }


def render_webgl_shell_html(
    unity_files: dict[str, str],
    apk_url: str = DEFAULT_APK_URL,
    apk_size_mb: float = DEFAULT_APK_SIZE_MB,
    apk_sha256: str = DEFAULT_APK_SHA256,
    product_version: str = "2.0.0",
) -> str:
    grid, qr_ver, _ = encode_qr_matrix(apk_url)
    if verify_and_decode_qr_matrix(grid) != apk_url:
        raise RuntimeError("QR matrix round-trip verification failed")
    qr_svg = qr_matrix_to_svg(grid, quiet=4, pixel_size=196)

    esc_apk_url = html.escape(apk_url, quote=True)
    esc_sha = html.escape(apk_sha256.lower())
    size_str = f"{apk_size_mb:.2f}"

    loader_js_lit = json.dumps(unity_files["loaderUrl"])
    data_js_lit = json.dumps(unity_files["dataUrl"])
    framework_js_lit = json.dumps(unity_files["frameworkUrl"])
    code_js_lit = json.dumps(unity_files["codeUrl"])
    ver_js_lit = json.dumps(product_version)

    return f"""<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover">
  <title>CRUSHER CORE V2 ULTRA — WebGL Playable Build</title>
  <style>
    :root {{
      --bg-0: #07090d;
      --bg-1: #10151f;
      --bg-2: #182030;
      --line: #2a364d;
      --amber: #ff9d00;
      --amber-hi: #ffc44d;
      --ember: #ff4800;
      --ok: #2ee59d;
      --text: #eef2f8;
      --muted: #93a0b8;
      --bar-h: 36px;
    }}
    * {{ box-sizing: border-box; margin: 0; padding: 0; }}
    html, body {{
      width: 100vw;
      height: 100vh;
      height: 100dvh;
      overflow: hidden;
      background: var(--bg-0);
      color: var(--text);
      font-family: "Segoe UI", Roboto, system-ui, -apple-system, sans-serif;
      user-select: none;
      -webkit-user-select: none;
      touch-action: none;
    }}
    #cc-root {{
      display: flex;
      flex-direction: column;
      width: 100%;
      height: 100%;
      background: var(--bg-0);
    }}
    #cc-topbar {{
      flex: 0 0 var(--bar-h);
      height: var(--bar-h);
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 10px;
      padding: 0 12px;
      background: linear-gradient(180deg, #141a26 0%, #0c1018 100%);
      border-bottom: 1px solid var(--line);
      z-index: 20;
    }}
    .tb-left {{
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 0;
    }}
    .tb-dot {{
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--amber);
      box-shadow: 0 0 8px var(--amber);
      flex-shrink: 0;
    }}
    .tb-badge {{
      font-size: 12px;
      font-weight: 900;
      letter-spacing: 0.08em;
      color: var(--amber-hi);
      background: rgba(255, 157, 0, 0.12);
      border: 1px solid rgba(255, 157, 0, 0.42);
      padding: 2px 9px;
      white-space: nowrap;
      clip-path: polygon(6px 0, 100% 0, calc(100% - 6px) 100%, 0 100%);
    }}
    .tb-hotkeys {{
      font-size: 11.5px;
      color: var(--muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      letter-spacing: 0.01em;
    }}
    .tb-hotkeys b {{
      color: #d7e0f0;
      font-weight: 700;
    }}
    .tb-actions {{
      display: flex;
      align-items: center;
      gap: 8px;
      flex-shrink: 0;
    }}
    .tb-btn {{
      appearance: none;
      border: 1px solid var(--line);
      background: var(--bg-2);
      color: var(--text);
      font-size: 11.5px;
      font-weight: 700;
      padding: 4px 10px;
      cursor: pointer;
      white-space: nowrap;
      transition: border-color 0.14s ease, background 0.14s ease, color 0.14s ease;
    }}
    .tb-btn:hover {{
      border-color: var(--amber);
      color: var(--amber-hi);
      background: #202a3f;
    }}
    .tb-btn-accent {{
      border-color: rgba(255, 157, 0, 0.55);
      background: linear-gradient(90deg, rgba(255, 157, 0, 0.2), rgba(255, 72, 0, 0.2));
      color: var(--amber-hi);
    }}
    #unity-container {{
      position: relative;
      flex: 1 1 auto;
      width: 100%;
      height: calc(100vh - var(--bar-h));
      height: calc(100dvh - var(--bar-h));
      background: radial-gradient(circle at 50% 42%, #131926 0%, #07090d 75%);
      overflow: hidden;
    }}
    #unity-canvas {{
      width: 100%;
      height: 100%;
      display: block;
      outline: none;
      background: #07090d;
    }}
    #cc-preloader {{
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background:
        radial-gradient(circle at 50% 38%, rgba(255, 145, 0, 0.14), transparent 56%),
        linear-gradient(180deg, #090c12 0%, #06080c 100%);
      z-index: 15;
      transition: opacity 0.28s ease;
    }}
    #cc-preloader.cc-hidden {{
      opacity: 0;
      pointer-events: none;
    }}
    .pl-card {{
      width: min(460px, 92vw);
      background: linear-gradient(155deg, #121824, #0b0f17);
      border: 1px solid var(--line);
      border-top: 3px solid var(--amber);
      padding: 24px 22px 20px;
      box-shadow: 0 24px 64px rgba(0, 0, 0, 0.72);
    }}
    .pl-hazard {{
      height: 6px;
      margin-bottom: 16px;
      background: repeating-linear-gradient(
        -45deg,
        var(--amber),
        var(--amber) 10px,
        #111622 10px,
        #111622 20px
      );
    }}
    .pl-title {{
      font-size: 15px;
      font-weight: 900;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--amber-hi);
      margin-bottom: 6px;
    }}
    .pl-sub {{
      font-size: 12.5px;
      color: var(--muted);
      margin-bottom: 16px;
    }}
    .pl-bar-track {{
      position: relative;
      width: 100%;
      height: 14px;
      background: #090c12;
      border: 1px solid var(--line);
      overflow: hidden;
      margin-bottom: 8px;
    }}
    .pl-bar-fill {{
      width: 2%;
      height: 100%;
      background: linear-gradient(90deg, var(--amber) 0%, var(--ember) 100%);
      box-shadow: 0 0 14px rgba(255, 157, 0, 0.55);
      transition: width 0.12s linear;
    }}
    .pl-meta {{
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      font-family: Consolas, monospace;
      color: #c9d4e8;
    }}
    #unity-warning {{
      position: absolute;
      top: 10px;
      left: 50%;
      transform: translateX(-50%);
      z-index: 18;
      max-width: min(640px, 92vw);
      font-size: 12.5px;
      display: none;
    }}
    #cc-apk-modal {{
      position: fixed;
      inset: 0;
      background: rgba(4, 6, 10, 0.82);
      backdrop-filter: blur(4px);
      display: none;
      align-items: center;
      justify-content: center;
      padding: 16px;
      z-index: 50;
    }}
    #cc-apk-modal.cc-open {{
      display: flex;
    }}
    .md-box {{
      width: min(460px, 94vw);
      background: linear-gradient(155deg, #131926, #0c1019);
      border: 1px solid var(--line);
      border-top: 3px solid var(--amber);
      padding: 20px;
      box-shadow: 0 24px 70px rgba(0, 0, 0, 0.8);
    }}
    .md-head {{
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 12px;
    }}
    .md-head h2 {{
      font-size: 15px;
      font-weight: 900;
      letter-spacing: 0.05em;
      text-transform: uppercase;
      color: var(--amber-hi);
    }}
    .md-close {{
      background: transparent;
      border: 1px solid var(--line);
      color: var(--muted);
      width: 28px;
      height: 28px;
      cursor: pointer;
      font-weight: 900;
    }}
    .md-close:hover {{ color: var(--text); border-color: var(--amber); }}
    .md-body {{
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      gap: 12px;
    }}
    .md-qr {{
      background: #ffffff;
      padding: 8px;
      border: 2px solid var(--amber);
      line-height: 0;
    }}
    .md-dl {{
      display: inline-block;
      width: 100%;
      padding: 12px 16px;
      background: linear-gradient(90deg, var(--amber), var(--ember));
      color: #090b10;
      font-weight: 900;
      font-size: 14px;
      text-decoration: none;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }}
    .md-sha {{
      width: 100%;
      background: #080b10;
      border: 1px solid var(--line);
      padding: 8px;
      font-family: Consolas, monospace;
      font-size: 11px;
      color: var(--ok);
      word-break: break-all;
      user-select: text;
      -webkit-user-select: text;
    }}
    @media (max-width: 960px) {{
      .tb-hotkeys {{ display: none; }}
    }}
  </style>
</head>
<body>
  <div id="cc-root">
    <header id="cc-topbar">
      <div class="tb-left">
        <span class="tb-dot" id="cc-status-dot"></span>
        <span class="tb-badge">CRUSHER CORE V2 ULTRA</span>
      </div>
      <div class="tb-hotkeys" title="Горячие клавиши ПК">
        <b>[1-3]</b> Вкладки • <b>[Q]</b> Кратность • <b>[Space]</b> Скорость • <b>[Tab]</b> Ангар • <b>[M]</b> Звук • <b>[Колесо]</b> Зум
      </div>
      <div class="tb-actions">
        <button type="button" class="tb-btn" id="btn-fullscreen" tabindex="-1">⛶ На весь экран</button>
        <button type="button" class="tb-btn tb-btn-accent" id="btn-apk-modal" tabindex="-1">📱 Android APK + QR</button>
      </div>
    </header>

    <main id="unity-container">
      <canvas id="unity-canvas" tabindex="-1"></canvas>

      <div id="cc-preloader" role="status" aria-live="polite">
        <div class="pl-card">
          <div class="pl-hazard"></div>
          <div class="pl-title">ЗАПУСК РЕАКТОРА CRUSHER CORE V2 ULTRA...</div>
          <div class="pl-sub" id="pl-stage">Инициализация WebGL 2.0 • Загрузка WASM и шейдеров цеха...</div>
          <div class="pl-bar-track">
            <div class="pl-bar-fill" id="pl-bar-fill"></div>
          </div>
          <div class="pl-meta">
            <span id="pl-detail">ПОТОК ДАННЫХ</span>
            <span id="pl-pct">0%</span>
          </div>
        </div>
      </div>

      <div id="unity-warning"></div>
    </main>
  </div>

  <div id="cc-apk-modal" role="dialog" aria-modal="true" aria-labelledby="md-title">
    <div class="md-box">
      <div class="md-head">
        <h2 id="md-title">Android-версия (ARM64 APK)</h2>
        <button type="button" class="md-close" id="btn-modal-close" aria-label="Закрыть">✕</button>
      </div>
      <div class="md-body">
        <div class="md-qr">{qr_svg}</div>
        <div style="font-size:12.5px;color:var(--muted);">
          Наведите камеру смартфона (QR v{qr_ver}-L) или скачайте подписанный APK напрямую:
        </div>
        <a class="md-dl" href="{esc_apk_url}" download>⬇ Скачать APK ({size_str} МБ)</a>
        <div class="md-sha">SHA-256: {esc_sha}</div>
      </div>
    </div>
  </div>

  <script>
    (function() {{
      // Track any AudioContext created by Unity WebGL so first user gesture unlocks audio cleanly.
      var trackedContexts = [];
      var NativeAudioCtx = window.AudioContext || window.webkitAudioContext;
      if (NativeAudioCtx) {{
        var PatchedAudioCtx = function() {{
          var ctx = new (Function.prototype.bind.apply(NativeAudioCtx, [null].concat(Array.prototype.slice.call(arguments))))();
          trackedContexts.push(ctx);
          return ctx;
        }};
        PatchedAudioCtx.prototype = NativeAudioCtx.prototype;
        try {{
          if (window.AudioContext) window.AudioContext = PatchedAudioCtx;
          if (window.webkitAudioContext) window.webkitAudioContext = PatchedAudioCtx;
        }} catch (_) {{}}
      }}

      var canvas = document.getElementById("unity-canvas");
      var preloader = document.getElementById("cc-preloader");
      var barFill = document.getElementById("pl-bar-fill");
      var pctLabel = document.getElementById("pl-pct");
      var stageLabel = document.getElementById("pl-stage");
      var statusDot = document.getElementById("cc-status-dot");
      var warningBanner = document.getElementById("unity-warning");
      var apkModal = document.getElementById("cc-apk-modal");
      var unityInstanceRef = null;

      function unlockAudioAndFocus(ev) {{
        for (var i = 0; i < trackedContexts.length; i++) {{
          var c = trackedContexts[i];
          if (c && c.state === "suspended" && typeof c.resume === "function") {{
            c.resume().catch(function() {{}});
          }}
        }}
        if (!apkModal.classList.contains("cc-open")) {{
          if (!ev || !ev.target || !ev.target.closest || !ev.target.closest("#cc-topbar")) {{
            canvas.focus();
          }}
        }}
      }}

      ["pointerdown", "keydown", "touchstart", "click"].forEach(function(evtName) {{
        window.addEventListener(evtName, unlockAudioAndFocus, {{ capture: true, passive: true }});
      }});

      function unityShowBanner(msg, type) {{
        var div = document.createElement("div");
        div.textContent = String(msg || "");
        div.style.padding = "10px 14px";
        div.style.marginBottom = "6px";
        div.style.border = "1px solid #ff4800";
        div.style.background = type === "error" ? "#4a0f14" : "#3a2a08";
        div.style.color = "#eef2f8";
        warningBanner.appendChild(div);
        warningBanner.style.display = "block";
        if (type !== "error") {{
          setTimeout(function() {{
            if (div.parentNode) div.parentNode.removeChild(div);
            if (!warningBanner.children.length) warningBanner.style.display = "none";
          }}, 6000);
        }}
      }}

      document.getElementById("btn-fullscreen").addEventListener("click", function() {{
        this.blur();
        var rootEl = document.getElementById("cc-root");
        if (!document.fullscreenElement) {{
          if (rootEl.requestFullscreen) {{
            rootEl.requestFullscreen().catch(function() {{
              if (unityInstanceRef) unityInstanceRef.SetFullscreen(1);
            }});
          }} else if (unityInstanceRef) {{
            unityInstanceRef.SetFullscreen(1);
          }}
        }} else if (document.exitFullscreen) {{
          document.exitFullscreen().catch(function() {{}});
        }}
        canvas.focus();
      }});

      document.getElementById("btn-apk-modal").addEventListener("click", function() {{
        this.blur();
        apkModal.classList.add("cc-open");
      }});
      document.getElementById("btn-modal-close").addEventListener("click", function() {{
        this.blur();
        apkModal.classList.remove("cc-open");
        canvas.focus();
      }});
      apkModal.addEventListener("click", function(e) {{
        if (e.target === apkModal) {{
          apkModal.classList.remove("cc-open");
          canvas.focus();
        }}
      }});
      window.addEventListener("keydown", function(e) {{
        if (e.key === "Tab" && !apkModal.classList.contains("cc-open")) {{
          e.preventDefault();
          canvas.focus();
        }} else if (e.key === "Escape" && apkModal.classList.contains("cc-open")) {{
          apkModal.classList.remove("cc-open");
          canvas.focus();
        }}
      }});

      var dpr = Math.min(window.devicePixelRatio || 1, 2);
      var config = {{
        arguments: [],
        dataUrl: {data_js_lit},
        frameworkUrl: {framework_js_lit},
        codeUrl: {code_js_lit},
        streamingAssetsUrl: "StreamingAssets",
        companyName: "CrusherCore",
        productName: "CRUSHER CORE: Hyper Shredder",
        productVersion: {ver_js_lit},
        showBanner: unityShowBanner,
        devicePixelRatio: dpr,
        autoSyncPersistentDataPath: true
      }};

      var loaderScript = document.createElement("script");
      loaderScript.src = {loader_js_lit};
      loaderScript.onload = function() {{
        if (typeof createUnityInstance !== "function") {{
          stageLabel.textContent = "Ошибка: загрузчик WebGL не экспортировал createUnityInstance.";
          return;
        }}
        createUnityInstance(canvas, config, function(progress) {{
          var pct = Math.max(0, Math.min(100, Math.round(progress * 100)));
          barFill.style.width = Math.max(2, pct) + "%";
          pctLabel.textContent = pct + "%";
          if (pct >= 90) {{
            stageLabel.textContent = "Компиляция WebAssembly и прогрев конвейера URP...";
          }}
        }}).then(function(instance) {{
          unityInstanceRef = instance;
          window.__crusherUnityInstance = instance;
          barFill.style.width = "100%";
          pctLabel.textContent = "100%";
          statusDot.style.background = "#2ee59d";
          statusDot.style.boxShadow = "0 0 8px #2ee59d";
          preloader.classList.add("cc-hidden");
          setTimeout(function() {{ preloader.style.display = "none"; }}, 320);
          canvas.focus();
        }}).catch(function(err) {{
          stageLabel.textContent = "Сбой инициализации WebGL: " + err;
          unityShowBanner(String(err), "error");
        }});
      }};
      loaderScript.onerror = function() {{
        stageLabel.textContent = "Не удалось загрузить файл " + {loader_js_lit};
        unityShowBanner("Failed to load " + {loader_js_lit}, "error");
      }};
      document.body.appendChild(loaderScript);
    }})();
  </script>
</body>
</html>
"""


def generate_shell(
    build_dir: str,
    out_html: str | None = None,
    apk_url: str = DEFAULT_APK_URL,
    apk_path: str | None = None,
    product_version: str = "2.0.0",
) -> str:
    unity_files = discover_unity_webgl_files(build_dir)
    size_mb = DEFAULT_APK_SIZE_MB
    sha_hex = DEFAULT_APK_SHA256
    if apk_path and os.path.isfile(apk_path):
        size_mb = os.path.getsize(apk_path) / (1024.0 * 1024.0)
        sha_hex = sha256_file(apk_path)

    target_html = out_html or os.path.join(build_dir, "index.html")
    html_text = render_webgl_shell_html(
        unity_files=unity_files,
        apk_url=apk_url,
        apk_size_mb=size_mb,
        apk_sha256=sha_hex,
        product_version=product_version,
    )
    os.makedirs(os.path.dirname(os.path.abspath(target_html)), exist_ok=True)
    with open(target_html, "w", encoding="utf-8", newline="\n") as f:
        f.write(html_text)

    nojekyll_path = os.path.join(os.path.dirname(os.path.abspath(target_html)), ".nojekyll")
    if not os.path.exists(nojekyll_path):
        with open(nojekyll_path, "w", encoding="utf-8") as f:
            f.write("")

    return target_html


def run_self_test() -> None:
    with tempfile.TemporaryDirectory(prefix="crusher_webgl_shell_test_") as tmp:
        bdir = os.path.join(tmp, "Build")
        os.makedirs(bdir, exist_ok=True)
        for fname in (
            "WebGL.loader.js",
            "WebGL.data.unityweb",
            "WebGL.framework.js.unityweb",
            "WebGL.wasm.unityweb",
        ):
            with open(os.path.join(bdir, fname), "w", encoding="utf-8") as f:
                f.write("// mock")

        out_path = generate_shell(tmp, apk_path=None)
        assert os.path.isfile(out_path), "index.html was not generated"
        assert os.path.isfile(os.path.join(tmp, ".nojekyll")), ".nojekyll was not generated"

        with open(out_path, "r", encoding="utf-8") as f:
            doc = f.read()

        required_snippets = [
          "ЗАПУСК РЕАКТОРА CRUSHER CORE V2 ULTRA...",
          "CRUSHER CORE V2 ULTRA",
          "[1-3]",
          "[Q]",
          "[Space]",
          "[Tab]",
          "[M]",
          "[Колесо]",
          "⛶ На весь экран",
          "📱 Android APK + QR",
          'tabindex="-1"',
          'e.key === "Tab"',
          "this.blur()",
          "Math.min(window.devicePixelRatio || 1, 2)",
          "AudioContext",
          "createUnityInstance",
          "Build/WebGL.loader.js",
          "Build/WebGL.data.unityweb",
          "Build/WebGL.framework.js.unityweb",
          "Build/WebGL.wasm.unityweb",
          DEFAULT_APK_URL,
          DEFAULT_APK_SHA256,
          "<svg",
          "--bar-h: 36px",
        ]
        for snip in required_snippets:
            assert snip in doc, f"Missing required snippet in WebGL shell HTML: {snip!r}"

    print("GEN_WEBGL_SHELL_SELFTEST_PASS")


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Generate industrial WebGL HTML shell for Crusher Core V2 Ultra.")
    parser.add_argument("--build-dir", default="Builds/WebGL", help="Unity WebGL output folder containing Build/")
    parser.add_argument("--out", default=None, help="Output HTML path (defaults to <build-dir>/index.html)")
    parser.add_argument("--apk-url", default=DEFAULT_APK_URL)
    parser.add_argument("--apk-path", default="Builds/CrusherCore-v1.0.0-arm64.apk")
    parser.add_argument("--version", default="2.0.0")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        run_self_test()
        return

    out = generate_shell(
        build_dir=args.build_dir,
        out_html=args.out,
        apk_url=args.apk_url,
        apk_path=args.apk_path,
        product_version=args.version,
    )
    print(f"Generated WebGL shell: {out} ({os.path.getsize(out)} bytes)")


if __name__ == "__main__":
    main()
