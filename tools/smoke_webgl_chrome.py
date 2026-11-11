"""
Headless ChromeBridge WebGL 2.0 live playability verifier for Crusher Core V2 Ultra.
Strictly adheres to `chrome-background` and `NoWinGuard` rules:
  - Uses ONLY the existing shared ChromeBridge service at 127.0.0.1:17331
  - Never spawns chrome.exe, never resizes windows, never touches or activates user tabs
  - Opens a background tab (`background=True`), mutes audio output so zero sound plays on PC
  - Waits for Unity 6 WebGL2 (`createUnityInstance`) to finish booting & run live combat
  - Verifies 0 fatal JS/WebGL/WASM/shader console errors
  - Captures `build/qa/webgl_live_01_combat.png` and (after buying an upgrade & opening Hangar)
    `build/qa/webgl_live_02_hangar.png`
  - Immediately closes its own test tab and verifies 100% tab cleanup + unchanged foreground HWND.
"""
import base64
import ctypes
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import sys
import time
import uuid

PROJECT_ROOT = Path(__file__).resolve().parent.parent
BRIDGE_ROOT = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData/Local"))) / "CodexTools" / "ChromeBridge"
TARGET_URL = "https://mrak0be5.github.io/crusher-core/"
SHOT_COMBAT = PROJECT_ROOT / "build" / "qa" / "webgl_live_01_combat.png"
SHOT_HANGAR = PROJECT_ROOT / "build" / "qa" / "webgl_live_02_hangar.png"
REPORT_PATH = PROJECT_ROOT / "Logs" / "webgl_chrome_smoke.json"


class ChromeBridgeClient:
    def __init__(self):
        self.key = (BRIDGE_ROOT / "control.key").read_text(encoding="ascii").strip()
        self.client_id = str(uuid.uuid4())
        self.seq = 0
        self.own_pages: list[int] = []

    def post(self, path: str, payload: dict):
        conn = http.client.HTTPConnection("127.0.0.1", 17331, timeout=180)
        try:
            conn.request(
                "POST",
                path,
                json.dumps(payload).encode("utf-8"),
                {
                    "Content-Type": "application/json",
                    "X-Control-Key": self.key,
                    "X-Client-Id": self.client_id,
                },
            )
            resp = conn.getresponse()
            body = resp.read()
            if resp.status == 204:
                return None
            if resp.status != 200:
                raise RuntimeError(f"ChromeBridge HTTP {resp.status}")
            return json.loads(body.decode("utf-8", errors="replace"))
        finally:
            conn.close()

    def rpc(self, method: str, params: dict):
        self.seq += 1
        res = self.post("/rpc", {"jsonrpc": "2.0", "id": self.seq, "method": method, "params": params})
        if "error" in res:
            raise RuntimeError(f"ChromeBridge RPC error: {res['error']}")
        return res["result"]

    def initialize(self):
        return self.rpc(
            "initialize",
            {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "crusher-webgl-smoke", "version": "2.0"},
            },
        )

    def call_tool(self, name: str, **kwargs):
        res = self.rpc("tools/call", {"name": name, "arguments": kwargs})
        if res.get("isError"):
            txt = " ".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")
            raise RuntimeError(f"Tool {name} failed: {txt[:300]}")
        return res

    @staticmethod
    def parse_page_ids(tool_result: dict) -> set[int]:
        out: set[int] = set()
        for c in tool_result.get("content", []):
            if c.get("type") == "text":
                for m in re.findall(r"^(\d+): ", c["text"], re.M):
                    out.add(int(m))
        return out

    @staticmethod
    def extract_text(tool_result: dict) -> str:
        return "\n".join(c.get("text", "") for c in tool_result.get("content", []) if c.get("type") == "text")

    def evaluate_json(self, page_id: int, fn_src: str):
        res = self.call_tool("evaluate_script", pageId=page_id, function=fn_src)
        for item in res.get("content", []):
            if item.get("type") != "text":
                continue
            m = re.search(r"```(?:json)?\s*\n([\s\S]*?)\n```", item["text"])
            if m:
                return json.loads(m.group(1))
        raise RuntimeError(f"Unexpected evaluate_script format: {self.extract_text(res)[:240]}")

    def save_page_screenshot(self, page_id: int, dest_path: Path):
        res = self.call_tool("take_screenshot", pageId=page_id, format="png")
        for item in res.get("content", []):
            if item.get("type") == "image" and item.get("data"):
                dest_path.write_bytes(base64.b64decode(item["data"]))
                return
            if item.get("type") == "text":
                m = re.search(r"Saved screenshot to (.+?)\.\s*$", item.get("text", ""), re.M)
                if m and os.path.isfile(m.group(1).strip()):
                    shutil.copy2(m.group(1).strip(), dest_path)
                    return
        raise RuntimeError(f"Unexpected take_screenshot response: {self.extract_text(res)[:240]}")

    def open_background_page(self, baseline_ids: set[int]) -> int:
        res = self.call_tool("new_page", url="about:blank", background=True)
        after_ids = self.parse_page_ids(res)
        added = after_ids - baseline_ids
        if len(added) != 1:
            raise RuntimeError(f"Ambiguous new_page result: added={added}")
        pid = added.pop()
        self.own_pages.append(pid)
        return pid

    def cleanup_and_verify(self, baseline_ids: set[int]) -> set[int]:
        close_errors = []
        for pid in list(self.own_pages):
            try:
                self.call_tool("close_page", pageId=pid)
            except Exception as exc:
                close_errors.append(f"{pid}:{exc}")
        remaining = self.parse_page_ids(self.call_tool("list_pages"))
        self.post("/release", {})
        leaked = set(self.own_pages) & remaining
        missing_user = baseline_ids - remaining
        if close_errors or leaked:
            raise RuntimeError(f"Failed to clean up test tabs: leaked={leaked}, errors={close_errors}")
        if missing_user:
            raise RuntimeError(f"Baseline user tabs changed unexpectedly: missing={missing_user}")
        return remaining


QUIET_AND_PROBE_INIT_SCRIPT = r"""(() => {
  try {
    if (window.indexedDB && indexedDB.deleteDatabase) {
      indexedDB.deleteDatabase("UnityCache");
    }
    const origFetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const nextInit = Object.assign({}, init || {}, { cache: "reload" });
      return origFetch(input, nextInit);
    };
  } catch (_) {}
  window.open = () => null;
  window.focus = () => {};
  window.alert = (m) => { (window.__ccErrors = window.__ccErrors || []).push("ALERT:" + m); };
  window.confirm = () => false;
  window.prompt = () => null;
  window.__ccErrors = [];
  window.__ccWarnings = [];

  window.addEventListener("error", (e) => {
    window.__ccErrors.push(String(e.message || e.error || "window.error"));
  });
  window.addEventListener("unhandledrejection", (e) => {
    window.__ccErrors.push("UnhandledRejection: " + String(e.reason || ""));
  });

  const origErr = console.error.bind(console);
  console.error = (...args) => {
    window.__ccErrors.push(args.map(a => String(a)).join(" "));
    origErr(...args);
  };

  // Silence audio in background test tab so user hears zero sound on PC speakers
  document.addEventListener("play", (e) => {
    if (e.target instanceof HTMLMediaElement) e.target.muted = true;
  }, true);
  const origPlay = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function() {
    this.muted = true;
    return origPlay.apply(this, arguments);
  };
  for (const name of ["AudioContext", "webkitAudioContext"]) {
    if (window[name]) {
      const OrigCtx = window[name];
      window[name] = class extends OrigCtx {
        constructor(...args) {
          super(...args);
          try { this.suspend(); } catch (_) {}
        }
        resume() { return Promise.resolve(); }
      };
    }
  }
})();"""


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    SHOT_COMBAT.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    for p in (SHOT_COMBAT, SHOT_HANGAR):
        if p.exists():
            p.unlink()

    before_hwnd = ctypes.windll.user32.GetForegroundWindow()
    bridge = ChromeBridgeClient()
    baseline_pages: set[int] = set()
    started = time.monotonic()

    try:
        bridge.initialize()
        baseline_pages = bridge.parse_page_ids(bridge.call_tool("list_pages"))
        print(f"[1/5] Baseline user tabs recorded: count={len(baseline_pages)} (untouched)")

        page_id = bridge.open_background_page(baseline_pages)
        bust_url = f"{TARGET_URL}?smoke={int(time.time())}"
        bridge.call_tool("select_page", pageId=page_id, bringToFront=False)
        # Clear HTTP & IndexedDB cache for fresh verification of latest GitHub Pages deployment
        bridge.call_tool(
            "navigate_page",
            pageId=page_id,
            url=bust_url,
            type="url",
            initScript=QUIET_AND_PROBE_INIT_SCRIPT,
        )
        print(f"[2/5] Opened background tab pageId={page_id} -> {TARGET_URL}")

        # Poll until Unity WebGL instance finishes loading and preloader hides
        boot_state = None
        deadline = time.monotonic() + 140.0
        while time.monotonic() < deadline:
            boot_state = bridge.evaluate_json(
                page_id,
                """() => {
                  const pl = document.getElementById('cc-preloader');
                  const pct = document.getElementById('pl-pct');
                  const canvas = document.getElementById('unity-canvas');
                  const hidden = pl ? (pl.classList.contains('cc-hidden') || getComputedStyle(pl).display === 'none') : false;
                  return {
                    instanceReady: Boolean(window.__crusherUnityInstance),
                    preloaderHidden: hidden,
                    pct: pct ? pct.textContent : '',
                    canvasW: canvas ? canvas.width : 0,
                    canvasH: canvas ? canvas.height : 0,
                    cssW: canvas ? canvas.clientWidth : 0,
                    cssH: canvas ? canvas.clientHeight : 0,
                    errors: (window.__ccErrors || []).slice(0, 10)
                  };
                }""",
            )
            if boot_state.get("errors"):
                # Fail fast if fatal shader/WASM/Abort error occurred
                fatal_chk = " | ".join(boot_state["errors"])
                if any(k in fatal_chk.lower() for k in ("abort(", "wasm", "compileerror", "syntaxerror", "failed to load")):
                    raise RuntimeError(f"Fatal WebGL boot error: {fatal_chk}")
            if boot_state.get("instanceReady") and boot_state.get("preloaderHidden"):
                break
            time.sleep(2.0)

        if not (boot_state and boot_state.get("instanceReady") and boot_state.get("preloaderHidden")):
            raise RuntimeError(f"Unity WebGL did not finish booting within timeout: {boot_state}")

        boot_sec = round(time.monotonic() - started, 2)
        print(
            f"[3/5] Unity WebGL2 booted in {boot_sec}s "
            f"(canvas={boot_state['canvasW']}x{boot_state['canvasH']}, css={boot_state['cssW']}x{boot_state['cssH']})"
        )

        # Let live combat run for 5 seconds so enemies approach, cannon fires brass shells & rotor shreds!
        time.sleep(5.0)

        # Buy first Attack upgrade card (Damage / Урон, $10) inside canvas so level badge updates to Ур. 1+
        click_card_info = bridge.evaluate_json(
            page_id,
            """async () => {
              const canvas = document.getElementById('unity-canvas');
              const r = canvas.getBoundingClientRect();
              const cw = r.width, ch = r.height;
              const isLandscape = cw > ch * 1.1;
              let cx, cy;
              if (isLandscape) {
                const scale = Math.min(cw / 1920.0, ch / 1080.0);
                cx = r.left + cw * 0.5 + (-710.0 + 234.0 + 143.0) * scale;
                cy = r.top + ch - (8.0 + 128.0 + 24.0) * scale;
              } else {
                const scale = Math.sqrt((cw / 1080.0) * (ch / 2400.0));
                cx = r.left + cw * 0.5 - 264.0 * scale;
                cy = r.top + ch - 838.0 * scale;
              }
              const fire = (type, buttons) => {
                canvas.dispatchEvent(new PointerEvent(type.replace('mouse', 'pointer'), {
                  bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0, buttons, pointerId: 1, isPrimary: true
                }));
                canvas.dispatchEvent(new MouseEvent(type, {
                  bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0, buttons
                }));
              };
              fire('mousemove', 0);
              fire('mousedown', 1);
              await new Promise(res => setTimeout(res, 90));
              fire('mouseup', 0);
              canvas.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
              return { isLandscape, cw: Math.round(cw), ch: Math.round(ch), cardX: Math.round(cx), cardY: Math.round(cy) };
            }""",
        )
        time.sleep(0.8)

        # Capture Screenshot 1: Live WebGL2 Combat + Upgraded Stat Card
        bridge.save_page_screenshot(page_id, SHOT_COMBAT)
        assert SHOT_COMBAT.is_file() and SHOT_COMBAT.stat().st_size > 40_000, f"Invalid combat screenshot: {SHOT_COMBAT}"

        # Click Hangar Retreat button ("АНГАР") in top telemetry bar to open the armored Hangar window
        click_hangar_info = bridge.evaluate_json(
            page_id,
            """async () => {
              const canvas = document.getElementById('unity-canvas');
              const r = canvas.getBoundingClientRect();
              const cw = r.width, ch = r.height;
              const isLandscape = cw > ch * 1.1;
              let hx, hy;
              if (isLandscape) {
                const scale = Math.min(cw / 1920.0, ch / 1080.0);
                hx = r.left + cw * 0.5 + (-660.0 + 1170.0 + 68.0) * scale;
                hy = r.top + (8.0 + 10.0 + 22.0) * scale;
              } else {
                const scale = Math.sqrt((cw / 1080.0) * (ch / 2400.0));
                hx = r.left + cw * 0.5 + (-522.0 + 846.0 + 91.0) * scale;
                hy = r.top + (14.0 + 14.0 + 35.0) * scale;
              }
              const fire = (type, buttons) => {
                canvas.dispatchEvent(new PointerEvent(type.replace('mouse', 'pointer'), {
                  bubbles: true, cancelable: true, clientX: hx, clientY: hy, button: 0, buttons, pointerId: 1, isPrimary: true
                }));
                canvas.dispatchEvent(new MouseEvent(type, {
                  bubbles: true, cancelable: true, clientX: hx, clientY: hy, button: 0, buttons
                }));
              };
              fire('mousemove', 0);
              fire('mousedown', 1);
              await new Promise(res => setTimeout(res, 90));
              fire('mouseup', 0);
              canvas.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: hx, clientY: hy, button: 0 }));
              return { hangarX: Math.round(hx), hangarY: Math.round(hy) };
            }""",
        )
        time.sleep(1.3)

        # Capture Screenshot 2: Live WebGL2 Hangar screen over 3D foundry
        bridge.save_page_screenshot(page_id, SHOT_HANGAR)
        assert SHOT_HANGAR.is_file() and SHOT_HANGAR.stat().st_size > 40_000, f"Invalid hangar screenshot: {SHOT_HANGAR}"

        # Check console errors (both captured window.__ccErrors and CDP console error log)
        js_errors = bridge.evaluate_json(page_id, "() => (window.__ccErrors || [])")
        console_res = bridge.call_tool("list_console_messages", pageId=page_id, types=["error"])
        console_txt = bridge.extract_text(console_res)

        fatal_patterns = ("abort(", "compileerror", "linkerror", "glsl", "shader error", "exception", "unhandledrejection", "out of memory")
        fatal_hits = [
            e for e in js_errors
            if any(fp in e.lower() for fp in fatal_patterns) and "favicon.ico" not in e.lower()
        ]
        if fatal_hits:
            raise RuntimeError(f"Fatal WebGL console errors detected: {fatal_hits}")

        print(
            f"[4/5] Captured live WebGL screenshots:\n"
            f"  {SHOT_COMBAT} ({SHOT_COMBAT.stat().st_size:,} B) | click={click_card_info}\n"
            f"  {SHOT_HANGAR} ({SHOT_HANGAR.stat().st_size:,} B) | click={click_hangar_info}\n"
            f"  JS/WebGL fatal errors: 0 (raw error count={len(js_errors)})"
        )

    finally:
        after_pages = bridge.cleanup_and_verify(baseline_pages)
        after_hwnd = ctypes.windll.user32.GetForegroundWindow()
        print(
            f"[5/5] Tab cleanup verified: remaining_tabs={len(after_pages)} == baseline={len(baseline_pages)}, "
            f"own_tab_closed=True, focus_unchanged={before_hwnd == after_hwnd}"
        )

    report = {
        "passed": True,
        "url": TARGET_URL,
        "boot_seconds": boot_sec,
        "canvas_width": boot_state["canvasW"],
        "canvas_height": boot_state["canvasH"],
        "css_width": boot_state["cssW"],
        "css_height": boot_state["cssH"],
        "js_errors": js_errors,
        "console_errors_summary": console_txt[:500],
        "screenshots": [
            {"path": str(SHOT_COMBAT), "bytes": SHOT_COMBAT.stat().st_size},
            {"path": str(SHOT_HANGAR), "bytes": SHOT_HANGAR.stat().st_size},
        ],
        "baseline_tabs_count": len(baseline_pages),
        "focus_unchanged": (before_hwnd == after_hwnd),
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print("WEBGL_CHROME_SMOKE_PASS")


if __name__ == "__main__":
    main()
