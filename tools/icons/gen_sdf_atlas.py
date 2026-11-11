#!/usr/bin/env python3
"""
Procedural Vector/SDF Industrial Icon & Atlas Generator for CRUSHER CORE V1.
Zero Asset Store & Zero external image dependencies: pure numpy + stdlib (zlib/struct).
Renders 54 crisp 256x256 RGBA icons (with 2x SSAA at 512x512) + 2048x2048 atlas + JSON UV map.
"""

from __future__ import annotations
import json
import math
import struct
import sys
import zlib
from pathlib import Path
import numpy as np

SS_SIZE = 512   # 2x supersampled internal resolution
OUT_SIZE = 256  # final per-icon size


def write_png_rgba(path: Path, rgba: np.ndarray) -> None:
    """Write uint8 (H, W, 4) array to PNG using stdlib zlib + struct."""
    assert rgba.dtype == np.uint8 and rgba.ndim == 3 and rgba.shape[2] == 4
    h, w, _ = rgba.shape
    # Prepend filter byte 0 per scanline
    scanlines = np.concatenate([np.zeros((h, 1), dtype=np.uint8), rgba.reshape(h, w * 4)], axis=1)
    raw = scanlines.tobytes()

    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    png_bytes = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, level=6))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png_bytes)


def make_grid(n: int = SS_SIZE) -> tuple[np.ndarray, np.ndarray]:
    lin = np.linspace(-1.0, 1.0, n, dtype=np.float32)
    x, y = np.meshgrid(lin, -lin)  # +Y is up!
    return x, y


def smoothstep(e0: float, e1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sdf_mask(d: np.ndarray, aa: float = 0.008) -> np.ndarray:
    return 1.0 - smoothstep(-aa, aa, d)


def sd_circle(x: np.ndarray, y: np.ndarray, cx: float, cy: float, r: float) -> np.ndarray:
    return np.hypot(x - cx, y - cy) - r


def sd_ring(x: np.ndarray, y: np.ndarray, cx: float, cy: float, r: float, half_thick: float) -> np.ndarray:
    return np.abs(np.hypot(x - cx, y - cy) - r) - half_thick


def sd_box(x: np.ndarray, y: np.ndarray, cx: float, cy: float, hx: float, hy: float, rot: float = 0.0) -> np.ndarray:
    px = x - cx
    py = y - cy
    if abs(rot) > 1e-5:
        c, s = math.cos(rot), math.sin(rot)
        px, py = px * c + py * s, -px * s + py * c
    dx = np.abs(px) - hx
    dy = np.abs(py) - hy
    return np.hypot(np.maximum(dx, 0.0), np.maximum(dy, 0.0)) + np.minimum(np.maximum(dx, dy), 0.0)


def sd_chamfer_box(x: np.ndarray, y: np.ndarray, hx: float, hy: float, ch: float) -> np.ndarray:
    dx = np.abs(x) - hx
    dy = np.abs(y) - hy
    box_d = np.hypot(np.maximum(dx, 0.0), np.maximum(dy, 0.0)) + np.minimum(np.maximum(dx, dy), 0.0)
    cut_d = (np.abs(x) + np.abs(y) - (hx + hy - ch)) * 0.70710678
    return np.maximum(box_d, cut_d)


def sd_segment(x: np.ndarray, y: np.ndarray, x0: float, y0: float, x1: float, y1: float, r: float) -> np.ndarray:
    pax, pay = x - x0, y - y0
    bax, bay = x1 - x0, y1 - y0
    denom = max(1e-6, bax * bax + bay * bay)
    h = np.clip((pax * bax + pay * bay) / denom, 0.0, 1.0)
    return np.hypot(pax - bax * h, pay - bay * h) - r


def sd_polygon(x: np.ndarray, y: np.ndarray, pts: list[tuple[float, float]]) -> np.ndarray:
    n = len(pts)
    vx0, vy0 = pts[0]
    d2 = (x - vx0) ** 2 + (y - vy0) ** 2
    sgn = np.ones_like(x)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        ex, ey = xj - xi, yj - yi
        wx, wy = x - xi, y - yi
        ee = max(1e-7, ex * ex + ey * ey)
        t = np.clip((wx * ex + wy * ey) / ee, 0.0, 1.0)
        bx, by = wx - ex * t, wy - ey * t
        d2 = np.minimum(d2, bx * bx + by * by)

        c1 = y >= yi
        c2 = y < yj
        c3 = (ex * wy) > (ey * wx)
        flip = (c1 & c2 & c3) | ((~c1) & (~c2) & (~c3))
        sgn = np.where(flip, -sgn, sgn)
        j = i
    return sgn * np.sqrt(d2)


def sd_gear(
    x: np.ndarray,
    y: np.ndarray,
    r_base: float,
    tooth_h: float,
    n_teeth: int,
    hole_r: float = 0.16,
    saw_skew: float = 0.0,
) -> np.ndarray:
    r = np.hypot(x, y)
    ang = np.arctan2(y, x)
    phase = ang * n_teeth
    if abs(saw_skew) > 1e-4:
        # Asymmetric circular saw tooth profile
        frac_p = (phase / (2.0 * math.pi)) % 1.0
        wave = np.where(frac_p < 0.35, frac_p / 0.35, 1.0 - (frac_p - 0.35) / 0.65)
        outer = r - (r_base + tooth_h * wave)
    else:
        wave = np.clip(0.5 + 0.68 * np.cos(phase), 0.0, 1.0)
        outer = r - (r_base + tooth_h * wave)
    if hole_r > 0.0:
        return np.maximum(outer, -(r - hole_r))
    return outer


def hex_rgb(h: str) -> tuple[float, float, float]:
    h = h.lstrip("#")
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0)


def blend_over(dst: np.ndarray, src_rgb: np.ndarray | tuple[float, float, float], src_a: np.ndarray) -> np.ndarray:
    """Alpha-over compositing on float32 premultiplied-safe RGBA."""
    a = np.clip(src_a, 0.0, 1.0)[..., None]
    if isinstance(src_rgb, tuple):
        rgb = np.empty((dst.shape[0], dst.shape[1], 3), dtype=np.float32)
        rgb[..., 0], rgb[..., 1], rgb[..., 2] = src_rgb
    else:
        rgb = src_rgb
    out = dst.copy()
    dst_a = dst[..., 3:4]
    out_a = a + dst_a * (1.0 - a)
    safe_a = np.maximum(out_a, 1e-6)
    out[..., :3] = (rgb * a + dst[..., :3] * dst_a * (1.0 - a)) / safe_a
    out[..., 3:4] = out_a
    return out


def render_badge_frame(x: np.ndarray, y: np.ndarray, accent_hex: str, style: str = "panel") -> np.ndarray:
    canvas = np.zeros((x.shape[0], x.shape[1], 4), dtype=np.float32)
    acc = hex_rgb(accent_hex)

    if style == "currency":
        # Clean soft radial glow behind freestanding currency emblem
        r = np.hypot(x, y)
        halo = np.exp(-r * 2.8) * 0.42 * (1.0 - smoothstep(0.75, 0.96, r))
        return blend_over(canvas, acc, halo)

    if style == "card":
        d_outer = sd_chamfer_box(x, y, 0.78, 0.88, 0.22)
    elif style == "hex":
        pts = [(0.86 * math.cos(math.radians(60 * k + 30)), 0.86 * math.sin(math.radians(60 * k + 30))) for k in range(6)]
        d_outer = sd_polygon(x, y, pts)
    else:
        d_outer = sd_chamfer_box(x, y, 0.84, 0.84, 0.26)

    outer_m = sdf_mask(d_outer, 0.008)
    inner_m = sdf_mask(d_outer + 0.085, 0.008)
    rim_m = np.clip(outer_m - inner_m, 0.0, 1.0)

    # Dark brushed gunmetal interior with subtle vertical sheen + inner accent glow
    grad = np.clip(0.5 + 0.5 * y, 0.0, 1.0)
    bg_rgb = np.zeros((x.shape[0], x.shape[1], 3), dtype=np.float32)
    bg_rgb[..., 0] = 0.08 + 0.09 * grad
    bg_rgb[..., 1] = 0.10 + 0.11 * grad
    bg_rgb[..., 2] = 0.14 + 0.14 * grad

    # Radial inner accent glow
    r_glow = np.exp(-np.hypot(x, y + 0.15) * 1.9) * 0.36
    bg_rgb[..., 0] += acc[0] * r_glow
    bg_rgb[..., 1] += acc[1] * r_glow
    bg_rgb[..., 2] += acc[2] * r_glow

    canvas = blend_over(canvas, np.clip(bg_rgb, 0.0, 1.0), outer_m * 0.96)

    # Metallic chamfered border tinted with category accent + top highlight
    rim_rgb = np.zeros_like(bg_rgb)
    spec = np.clip(0.35 + 0.75 * y, 0.15, 1.2)
    rim_rgb[..., 0] = np.clip(acc[0] * 0.82 * spec + 0.14 * grad, 0.0, 1.0)
    rim_rgb[..., 1] = np.clip(acc[1] * 0.82 * spec + 0.16 * grad, 0.0, 1.0)
    rim_rgb[..., 2] = np.clip(acc[2] * 0.82 * spec + 0.18 * grad, 0.0, 1.0)
    canvas = blend_over(canvas, rim_rgb, rim_m)

    # 4 industrial corner rivets for standard square panels
    if style == "panel":
        rivet_d = np.minimum(
            np.minimum(sd_circle(x, y, -0.66, -0.66, 0.032), sd_circle(x, y, 0.66, -0.66, 0.032)),
            np.minimum(sd_circle(x, y, -0.66, 0.66, 0.032), sd_circle(x, y, 0.66, 0.66, 0.032)),
        )
        canvas = blend_over(canvas, (0.75, 0.82, 0.90), sdf_mask(rivet_d))

    return canvas


def stamp_symbol(
    canvas: np.ndarray,
    x: np.ndarray,
    y: np.ndarray,
    d_sym: np.ndarray,
    top_hex: str,
    bot_hex: str,
    outline_hex: str = "#090C10",
) -> np.ndarray:
    # Drop shadow
    d_sh = np.roll(np.roll(d_sym, 10, axis=0), 6, axis=1)
    canvas = blend_over(canvas, (0.02, 0.03, 0.05), sdf_mask(d_sh, 0.020) * 0.62)

    # Crisp dark outline around symbol
    canvas = blend_over(canvas, hex_rgb(outline_hex), sdf_mask(d_sym - 0.028, 0.008) * 0.92)

    # Vertical metallic gradient inside symbol + inner bevel highlight
    t = np.clip(0.5 + 0.75 * y, 0.0, 1.0)
    c_top = hex_rgb(top_hex)
    c_bot = hex_rgb(bot_hex)
    sym_rgb = np.zeros((x.shape[0], x.shape[1], 3), dtype=np.float32)
    for k in range(3):
        sym_rgb[..., k] = c_bot[k] * (1.0 - t) + c_top[k] * t

    bevel = smoothstep(-0.055, -0.010, d_sym) * np.clip(0.3 + 0.7 * y, 0.0, 1.0) * 0.28
    sym_rgb = np.clip(sym_rgb + bevel[..., None], 0.0, 1.0)
    canvas = blend_over(canvas, sym_rgb, sdf_mask(d_sym, 0.008))
    return canvas


def sym_crosshair(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    d = np.minimum(sd_ring(x, y, 0, 0, 0.36, 0.045), sd_circle(x, y, 0, 0, 0.09))
    ticks = np.minimum(
        np.minimum(sd_segment(x, y, -0.52, 0, -0.22, 0, 0.042), sd_segment(x, y, 0.22, 0, 0.52, 0, 0.042)),
        np.minimum(sd_segment(x, y, 0, -0.52, 0, -0.22, 0.042), sd_segment(x, y, 0, 0.22, 0, 0.52, 0.042)),
    )
    return np.minimum(d, ticks)


def sym_shield(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    pts = [(0.0, 0.52), (0.44, 0.36), (0.38, -0.12), (0.0, -0.54), (-0.38, -0.12), (-0.44, 0.36)]
    return sd_polygon(x, y, pts)


def sym_bolt(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    pts = [(0.10, 0.56), (-0.32, -0.02), (-0.02, -0.02), (-0.14, -0.56), (0.34, 0.05), (0.04, 0.05)]
    return sd_polygon(x, y, pts)


def sym_bullet(x: np.ndarray, y: np.ndarray, ox: float = 0.0, oy: float = 0.0, rot: float = 0.55) -> np.ndarray:
    px, py = x - ox, y - oy
    c, s = math.cos(rot), math.sin(rot)
    rx, ry = px * c + py * s, -px * s + py * c
    body = sd_box(rx, ry, 0.0, -0.12, 0.11, 0.22)
    tip = sd_polygon(rx, ry, [(-0.11, 0.10), (0.11, 0.10), (0.0, 0.38)])
    groove = sd_box(rx, ry, 0.0, -0.26, 0.13, 0.03)
    return np.minimum(np.minimum(body, tip), groove)


def sym_heart_core(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    pts = [(0.0, -0.46), (0.46, 0.02), (0.30, 0.42), (0.0, 0.22), (-0.30, 0.42), (-0.46, 0.02)]
    return sd_polygon(x, y, pts)


def sym_ingot(x: np.ndarray, y: np.ndarray, ox: float = 0.0, oy: float = 0.0) -> np.ndarray:
    px, py = x - ox, y - oy
    pts = [(-0.46, -0.22), (0.46, -0.22), (0.32, 0.22), (-0.32, 0.22)]
    return sd_polygon(px, py, pts)


def sym_flask(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    bulb = sd_polygon(x, y, [(-0.14, 0.14), (0.14, 0.14), (0.44, -0.44), (-0.44, -0.44)])
    neck = sd_box(x, y, 0.0, 0.28, 0.11, 0.16)
    lip = sd_box(x, y, 0.0, 0.45, 0.18, 0.04)
    return np.minimum(np.minimum(bulb, neck), lip)


def sym_magnet(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    arc = np.maximum(sd_ring(x, y, 0.0, 0.04, 0.32, 0.10), y - 0.04)
    legs = np.minimum(sd_box(x, y, -0.32, 0.22, 0.10, 0.20), sd_box(x, y, 0.32, 0.22, 0.10, 0.20))
    return np.minimum(arc, legs)


def sym_double_chevron(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    c1 = np.minimum(sd_segment(x, y, -0.34, 0.34, 0.02, 0.0, 0.065), sd_segment(x, y, -0.34, -0.34, 0.02, 0.0, 0.065))
    c2 = np.minimum(sd_segment(x, y, 0.02, 0.34, 0.38, 0.0, 0.065), sd_segment(x, y, 0.02, -0.34, 0.38, 0.0, 0.065))
    return np.minimum(c1, c2)


def sym_dollar(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    top_c = np.maximum(sd_ring(x, y, 0.0, 0.16, 0.17, 0.055), (x - 0.06) * (y - 0.14) * 0.0 - 1.0)
    # Clean S-bars using geometric segments + arcs
    a1 = np.maximum(sd_ring(x, y, 0.0, 0.16, 0.16, 0.055), np.minimum(x - 0.18, -(y - 0.02)) * 0.0)
    # Explicit crisp industrial '$' glyph from segments
    s1 = sd_segment(x, y, 0.20, 0.30, -0.18, 0.30, 0.055)
    s2 = sd_segment(x, y, -0.20, 0.30, -0.20, 0.03, 0.055)
    s3 = sd_segment(x, y, -0.20, 0.03, 0.20, 0.03, 0.055)
    s4 = sd_segment(x, y, 0.20, 0.03, 0.20, -0.28, 0.055)
    s5 = sd_segment(x, y, 0.20, -0.28, -0.20, -0.28, 0.055)
    bar = sd_segment(x, y, 0.0, -0.46, 0.0, 0.48, 0.042)
    return np.minimum(np.minimum(np.minimum(s1, s2), np.minimum(s3, s4)), np.minimum(s5, bar))


def build_all_icons(x: np.ndarray, y: np.ndarray) -> list[tuple[str, np.ndarray]]:
    icons: list[tuple[str, np.ndarray]] = []

    def add(name: str, style: str, frame_hex: str, d: np.ndarray, top_hex: str, bot_hex: str, extra_fn=None) -> None:
        c = render_badge_frame(x, y, frame_hex, style=style)
        c = stamp_symbol(c, x, y, d, top_hex, bot_hex)
        if extra_fn is not None:
            c = extra_fn(c)
        icons.append((name, c))

    # --- 1. CURRENCIES (5) ---
    add("icon_cur_cash", "currency", "#2EE59D", sym_dollar(x, y), "#A7F3D0", "#10B981")
    d_coin = np.minimum(sd_circle(x, y, 0, 0, 0.56), sd_ring(x, y, 0, 0, 0.42, 0.03))
    add(
        "icon_cur_coins",
        "currency",
        "#F59E0B",
        sd_circle(x, y, 0, 0, 0.54),
        "#FEF08A",
        "#D97706",
        lambda c: stamp_symbol(c, x, y, sd_gear(x, y, 0.22, 0.07, 8, 0.09), "#FFFBEB", "#B45309"),
    )
    d_gem = sd_polygon(x, y, [(0.0, 0.62), (0.46, 0.18), (0.28, -0.58), (-0.28, -0.58), (-0.46, 0.18)])
    add("icon_cur_gems", "currency", "#C084FC", d_gem, "#F3E8FF", "#9333EA")
    d_stone = sd_polygon(x, y, [(0.0, 0.60), (0.50, 0.25), (0.42, -0.38), (0.0, -0.60), (-0.42, -0.38), (-0.50, 0.25)])
    add(
        "icon_cur_stones",
        "currency",
        "#10B981",
        d_stone,
        "#6EE7B7",
        "#047857",
        lambda c: stamp_symbol(c, x, y, sd_ring(x, y, 0, 0, 0.22, 0.045), "#ECFDF5", "#10B981"),
    )
    d_cell = np.minimum(sd_chamfer_box(x, y, 0.36, 0.52, 0.14), sd_box(x, y, 0, 0.56, 0.16, 0.06))
    add(
        "icon_cur_cells",
        "currency",
        "#00E5FF",
        d_cell,
        "#A5F3FC",
        "#0284C7",
        lambda c: stamp_symbol(c, x, y, sym_bolt(x * 1.6, y * 1.6), "#FFFFFF", "#38BDF8"),
    )

    # --- 2. NAVIGATION TABS (7) ---
    add("icon_tab_attack", "panel", "#FF5E2B", sym_bullet(x, y, 0, 0, -0.65), "#FED7AA", "#EA580C")
    add("icon_tab_defense", "panel", "#29B6F6", sym_shield(x, y), "#BAE6FD", "#0284C7")
    add("icon_tab_utility", "panel", "#FBBF24", sym_ingot(x, y, 0, -0.05), "#FEF08A", "#D97706")
    add("icon_tab_workshop", "panel", "#94A3B8", sd_gear(x, y, 0.34, 0.13, 8, 0.15), "#F1F5F9", "#64748B")
    d_cards_tab = np.minimum(sd_box(x, y, -0.10, 0.04, 0.24, 0.34, 0.18), sd_box(x, y, 0.12, -0.04, 0.24, 0.34, -0.12))
    add("icon_tab_cards", "panel", "#A855F7", d_cards_tab, "#E9D5FF", "#7E22CE")
    add("icon_tab_labs", "panel", "#00E5FF", sym_flask(x, y), "#CFFAFE", "#0891B2")
    add("icon_tab_uw", "panel", "#F59E0B", sd_gear(x, y, 0.28, 0.18, 6, 0.10, saw_skew=1.0), "#FEF3C7", "#D97706")

    # --- 3. ALL 22 STAT IDs ---
    # Attack (7)
    add("stat_Damage", "panel", "#FF5E2B", sym_bullet(x, y, 0.0, 0.0, -0.55), "#FFEDD5", "#EA580C")
    d_aspd = np.minimum(sym_bullet(x, y, -0.18, 0.10, -0.65), sym_bullet(x, y, 0.18, -0.10, -0.65))
    add("stat_AttackSpeed", "panel", "#FF5E2B", d_aspd, "#FFEDD5", "#F97316")
    add("stat_CritChance", "panel", "#FF5E2B", sym_crosshair(x, y), "#FECACA", "#DC2626")
    d_cfac = np.minimum(
        sd_polygon(x, y, [(0, 0.54), (0.14, 0.14), (0.54, 0), (0.14, -0.14), (0, -0.54), (-0.14, -0.14), (-0.54, 0), (-0.14, 0.14)]),
        sd_circle(x, y, 0, 0, 0.16),
    )
    add("stat_CritFactor", "panel", "#FF5E2B", d_cfac, "#FEF08A", "#EA580C")
    d_rng = np.minimum(sd_ring(x, y, 0, 0, 0.44, 0.045), np.minimum(sd_ring(x, y, 0, 0, 0.22, 0.035), sd_circle(x, y, 0, 0, 0.07)))
    add("stat_Range", "panel", "#FF5E2B", d_rng, "#FDE68A", "#D97706")
    d_msc = np.minimum(
        sd_segment(x, y, 0, -0.42, -0.38, 0.38, 0.05),
        np.minimum(sd_segment(x, y, 0, -0.42, 0.0, 0.44, 0.05), sd_segment(x, y, 0, -0.42, 0.38, 0.38, 0.05)),
    )
    add("stat_MultishotChance", "panel", "#FF5E2B", d_msc, "#FFEDD5", "#EA580C")
    d_mst = np.minimum(d_msc, np.minimum(sd_circle(x, y, -0.38, 0.38, 0.10), np.minimum(sd_circle(x, y, 0, 0.44, 0.10), sd_circle(x, y, 0.38, 0.38, 0.10))))
    add("stat_MultishotTargets", "panel", "#FF5E2B", d_mst, "#FED7AA", "#C2410C")

    # Defense (10)
    add("stat_Health", "panel", "#29B6F6", sym_heart_core(x, y), "#BAE6FD", "#0284C7")
    d_regen = np.minimum(sd_box(x, y, 0, 0, 0.14, 0.44), sd_box(x, y, 0, 0, 0.44, 0.14))
    add("stat_HealthRegen", "panel", "#29B6F6", d_regen, "#A7F3D0", "#059669")
    d_defabs = np.minimum(sym_shield(x, y), sd_chamfer_box(x, y, 0.22, 0.22, 0.08))
    add("stat_DefenseAbsolute", "panel", "#29B6F6", d_defabs, "#E0F2FE", "#0369A1")
    add(
        "stat_DefensePercent",
        "panel",
        "#29B6F6",
        sym_shield(x, y),
        "#BAE6FD",
        "#0284C7",
        lambda c: stamp_symbol(c, x, y, sd_segment(x, y, -0.18, -0.18, 0.18, 0.18, 0.045), "#FFFFFF", "#38BDF8"),
    )
    add("stat_Thorns", "panel", "#29B6F6", sd_gear(x, y, 0.28, 0.22, 10, 0.12, saw_skew=1.0), "#FED7AA", "#EA580C")
    d_kbc = np.minimum(sd_ring(x, y, -0.15, 0, 0.28, 0.045), sym_double_chevron(x - 0.12, y))
    add("stat_KnockbackChance", "panel", "#29B6F6", d_kbc, "#E0F2FE", "#0284C7")
    d_kbf = np.minimum(sd_box(x, y, -0.32, 0, 0.08, 0.38), sym_double_chevron(x - 0.10, y))
    add("stat_KnockbackForce", "panel", "#29B6F6", d_kbf, "#BAE6FD", "#0369A1")
    d_orbs = np.minimum(
        sd_ring(x, y, 0, 0, 0.36, 0.025),
        np.minimum(sd_gear(x - 0.36, y, 0.11, 0.05, 8, 0.03, 1.0), sd_gear(x + 0.36, y, 0.11, 0.05, 8, 0.03, 1.0)),
    )
    add("stat_OrbsCount", "panel", "#29B6F6", d_orbs, "#A5F3FC", "#0891B2")
    add("stat_OrbSpeed", "panel", "#29B6F6", sd_gear(x, y, 0.30, 0.14, 12, 0.14, saw_skew=1.0), "#67E8F9", "#0E7490")
    d_life = np.minimum(sym_heart_core(x * 1.25, (y - 0.06) * 1.25), sd_ring(x, y, 0, 0, 0.46, 0.035))
    add("stat_Lifesteal", "panel", "#29B6F6", d_life, "#FDA4AF", "#E11D48")

    # Utility (5)
    add("stat_CashBonus", "panel", "#2EE59D", sym_dollar(x, y), "#D1FAE5", "#059669")
    d_cpw = np.minimum(sym_dollar(x * 1.25 + 0.18, y * 1.25), sym_double_chevron(x - 0.22, y))
    add("stat_CashPerWave", "panel", "#2EE59D", d_cpw, "#A7F3D0", "#10B981")
    add("stat_CoinsPerKill", "panel", "#FBBF24", sym_ingot(x, y, 0, 0), "#FEF08A", "#D97706")
    d_coinw = np.minimum(sym_ingot(x, y, 0, -0.16), sym_ingot(x, y, 0, 0.22))
    add("stat_CoinsPerWave", "panel", "#FBBF24", d_coinw, "#FDE047", "#B45309")
    d_free = np.minimum(sd_gear(x, y, 0.34, 0.10, 8, 0.20), sd_polygon(x, y, [(0.0, 0.22), (0.18, -0.06), (0.07, -0.06), (0.07, -0.22), (-0.07, -0.22), (-0.07, -0.06), (-0.18, -0.06)]))
    add("stat_FreeUpgrades", "panel", "#2EE59D", d_free, "#CCFBF1", "#0D9488")

    # --- 4. ALL 8 CARDS (CardId) ---
    d_eb = np.minimum(sd_chamfer_box(x + 0.24, y - 0.14, 0.18, 0.18, 0.05), np.minimum(sd_chamfer_box(x - 0.24, y - 0.14, 0.18, 0.18, 0.05), sym_dollar(x * 1.4, (y + 0.24) * 1.4)))
    add("card_EnemyBalance", "card", "#38BDF8", d_eb, "#BAE6FD", "#0284C7")
    add("card_Coins", "card", "#38BDF8", d_coinw, "#FEF08A", "#D97706")
    add("card_Damage", "card", "#38BDF8", sym_bullet(x, y, 0, 0, -0.55), "#FED7AA", "#EA580C")
    add("card_Health", "card", "#38BDF8", sym_heart_core(x, y), "#BAE6FD", "#0284C7")
    add("card_ExtraOrb", "card", "#A855F7", sd_gear(x, y, 0.32, 0.16, 12, 0.12, saw_skew=1.0), "#E9D5FF", "#9333EA")
    d_plasma = np.minimum(sd_ring(x, y, 0, 0, 0.38, 0.05), sd_circle(x, y, 0, 0, 0.18))
    add("card_PlasmaCannon", "card", "#A855F7", d_plasma, "#A5F3FC", "#0284C7")
    d_dray = np.minimum(sd_segment(x, y, -0.45, -0.52, 0.45, 0.52, 0.09), sd_ring(x, y, 0, 0, 0.30, 0.035))
    add("card_DeathRay", "card", "#F59E0B", d_dray, "#FEF3C7", "#EA580C")
    add("card_WaveAccelerator", "card", "#F59E0B", sym_double_chevron(x, y), "#FDE68A", "#D97706")

    # --- 5. ALL 6 LABS (LabId) ---
    add("lab_GameSpeed", "hex", "#00E5FF", sym_double_chevron(x, y), "#CFFAFE", "#0284C7")
    add("lab_MagneticGarlic", "hex", "#00E5FF", sym_magnet(x, y), "#FED7AA", "#EA580C")
    add("lab_CoinsPerKill", "hex", "#00E5FF", sym_ingot(x, y, 0, 0), "#FEF08A", "#D97706")
    add("lab_CarbideOverdrive", "hex", "#00E5FF", sd_gear(x, y, 0.30, 0.18, 10, 0.11, saw_skew=1.0), "#FECACA", "#DC2626")
    add("lab_OrbAndRotorSpeed", "hex", "#00E5FF", d_orbs, "#A5F3FC", "#0891B2")
    d_bhcoin = np.minimum(sd_ring(x, y, 0, 0, 0.38, 0.06), sym_ingot(x * 1.4, y * 1.4, 0, 0))
    add("lab_BlackHoleCoinBonus", "hex", "#00E5FF", d_bhcoin, "#E9D5FF", "#9333EA")

    # --- 6. ULTIMATE WEAPONS, BOTS & SKIN (6) ---
    d_gt = np.minimum(sd_gear(x, y, 0.34, 0.16, 12, 0.18), sd_circle(x, y, 0, 0, 0.12))
    add("uw_GoldenTower", "hex", "#F59E0B", d_gt, "#FEF9C3", "#D97706")
    d_bh = np.minimum(sd_ring(x, y, 0, 0, 0.40, 0.07), sd_ring(x, y, 0.04, -0.03, 0.22, 0.05))
    add("uw_BlackHole", "hex", "#A855F7", d_bh, "#A5F3FC", "#7E22CE")
    add("uw_ChainLightning", "hex", "#38BDF8", sym_bolt(x, y), "#E0F2FE", "#0284C7")

    d_gbot = np.minimum(sd_circle(x, y, 0, 0, 0.30), sd_ring(x, y, 0, 0, 0.46, 0.04))
    add("bot_GoldenBot", "panel", "#FBBF24", d_gbot, "#FEF08A", "#D97706")
    add("bot_GemDrone", "panel", "#C084FC", d_gem, "#F3E8FF", "#9333EA")
    add("skin_MoltenCore", "hex", "#EF4444", sd_gear(x, y, 0.32, 0.18, 8, 0.13, saw_skew=1.0), "#FDE047", "#B91C1C")

    return icons


def downsample_2x(img_f32: np.ndarray) -> np.ndarray:
    """2x2 box filter on premultiplied RGBA -> crisp uint8 256x256 output."""
    rgb_p = img_f32[..., :3] * img_f32[..., 3:4]
    ap = img_f32[..., 3:4]
    prem = np.concatenate([rgb_p, ap], axis=-1)
    h, w, c = prem.shape
    ds = prem.reshape(h // 2, 2, w // 2, 2, c).mean(axis=(1, 3))
    out = np.zeros_like(ds)
    safe_a = np.maximum(ds[..., 3:4], 1e-6)
    out[..., :3] = np.where(ds[..., 3:4] > 1e-4, ds[..., :3] / safe_a, 0.0)
    out[..., 3:4] = ds[..., 3:4]
    return np.clip(np.round(out * 255.0), 0, 255).astype(np.uint8)


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    out_dir = root / "staging" / "shaders_audio" / "Icons"
    out_dir.mkdir(parents=True, exist_ok=True)

    x, y = make_grid(SS_SIZE)
    icons = build_all_icons(x, y)

    cols = 8
    rows = 8
    atlas = np.zeros((rows * OUT_SIZE, cols * OUT_SIZE, 4), dtype=np.uint8)
    uv_entries: dict[str, dict] = {}

    # Also build a visual QA contact sheet on split light/dark background
    qa_tile = 128
    qa_rows = math.ceil(len(icons) / cols)
    qa_sheet = np.zeros((qa_rows * qa_tile, cols * qa_tile, 4), dtype=np.uint8)

    for idx, (name, f32_img) in enumerate(icons):
        rgba = downsample_2x(f32_img)
        assert rgba.shape == (OUT_SIZE, OUT_SIZE, 4)
        assert int(rgba[..., 3].max()) > 230, f"{name}: icon alpha too faint"
        assert int(rgba[0, 0, 3]) == 0, f"{name}: outer corner must be transparent"

        write_png_rgba(out_dir / f"{name}.png", rgba)

        r_idx = idx // cols
        c_idx = idx % cols
        y0 = r_idx * OUT_SIZE
        x0 = c_idx * OUT_SIZE
        atlas[y0 : y0 + OUT_SIZE, x0 : x0 + OUT_SIZE] = rgba

        # Unity UV (origin bottom-left) + pixel rect (origin top-left)
        atlas_h = rows * OUT_SIZE
        atlas_w = cols * OUT_SIZE
        uv_entries[name] = {
            "index": idx,
            "px_x": x0,
            "px_y": y0,
            "px_w": OUT_SIZE,
            "px_h": OUT_SIZE,
            "u0": round(x0 / atlas_w, 6),
            "v0": round(1.0 - (y0 + OUT_SIZE) / atlas_h, 6),
            "u1": round((x0 + OUT_SIZE) / atlas_w, 6),
            "v1": round(1.0 - y0 / atlas_h, 6),
        }

        # Downsample 256 -> 128 for QA preview sheet over light concrete (#CFD6DF) / dark HUD (#141922) split
        th = rgba.reshape(qa_tile, 2, qa_tile, 2, 4).mean(axis=(1, 3))
        bg = np.zeros((qa_tile, qa_tile, 3), dtype=np.float32)
        bg[:, : qa_tile // 2] = [207, 214, 223]  # bright arena concrete
        bg[:, qa_tile // 2 :] = [20, 25, 34]     # dark HUD console
        a = th[..., 3:4] / 255.0
        comp = np.clip(th[..., :3] * a + bg * (1.0 - a), 0, 255).astype(np.uint8)
        tile_rgba = np.concatenate([comp, np.full((qa_tile, qa_tile, 1), 255, dtype=np.uint8)], axis=-1)
        qy, qx = (idx // cols) * qa_tile, (idx % cols) * qa_tile
        qa_sheet[qy : qy + qa_tile, qx : qx + qa_tile] = tile_rgba

    write_png_rgba(out_dir / "atlas_icons.png", atlas)
    write_png_rgba(out_dir / "_qa_icons_preview.png", qa_sheet)
    meta = {
        "status": "PASS",
        "icon_size": OUT_SIZE,
        "atlas_width": cols * OUT_SIZE,
        "atlas_height": rows * OUT_SIZE,
        "count": len(icons),
        "icons": uv_entries,
    }
    (out_dir / "atlas_icons.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
