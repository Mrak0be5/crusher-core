"""
Crusher Core V1 — Self-contained GitHub Pages landing generator (`web/index.html`).
Includes a zero-dependency ISO/IEC 18004 QR Code encoder (Byte mode, ECC Level L, V1..V10)
with built-in Annex C vector checks and full matrix -> RS syndrome -> UTF-8 round-trip verifier.

Usage:
  python tools/gen_landing.py --apk-url https://github.com/Mrak0be5/crusher-core/releases/download/v1.0.0/CrusherCore-v1.0.0-arm64.apk --apk-path Builds/CrusherCore-v1.0.0-arm64.apk --shots-dir Logs/smoke_shots --out web/index.html
  python tools/gen_landing.py --self-test
"""
import argparse
import hashlib
import html
import os
import shutil
import sys
import tempfile

# ISO/IEC 18004 Table 9 — ECC Level L (format bits = 0b01):
# version: (total_codewords, ec_per_block, g1_blocks, g1_data_cw, g2_blocks, g2_data_cw)
QR_LEVEL_L_SPECS = {
    1: (26, 7, 1, 19, 0, 0),
    2: (44, 10, 1, 34, 0, 0),
    3: (70, 15, 1, 55, 0, 0),
    4: (100, 20, 1, 80, 0, 0),
    5: (134, 26, 1, 108, 0, 0),
    6: (172, 18, 2, 68, 0, 0),
    7: (196, 20, 2, 78, 0, 0),
    8: (242, 24, 2, 97, 0, 0),
    9: (292, 30, 2, 116, 0, 0),
    10: (346, 18, 2, 68, 2, 69),
}

QR_ALIGNMENT_CENTERS = {
    1: [],
    2: [6, 18],
    3: [6, 22],
    4: [6, 26],
    5: [6, 30],
    6: [6, 34],
    7: [6, 22, 38],
    8: [6, 24, 42],
    9: [6, 26, 46],
    10: [6, 28, 50],
}

# GF(256) tables with primitive polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D)
GF_EXP = [0] * 512
GF_LOG = [0] * 256


def _init_gf256():
    x = 1
    for i in range(255):
        GF_EXP[i] = x
        GF_LOG[x] = i
        x <<= 1
        if x & 0x100:
            x ^= 0x11D
    for i in range(255, 512):
        GF_EXP[i] = GF_EXP[i - 255]


_init_gf256()


def gf_mul(a: int, b: int) -> int:
    if a == 0 or b == 0:
        return 0
    return GF_EXP[GF_LOG[a] + GF_LOG[b]]


def gf_poly_mul(p: list[int], q: list[int]) -> list[int]:
    res = [0] * (len(p) + len(q) - 1)
    for i, a in enumerate(p):
        for j, b in enumerate(q):
            res[i + j] ^= gf_mul(a, b)
    return res


def rs_generator_poly(ec_len: int) -> list[int]:
    g = [1]
    for i in range(ec_len):
        g = gf_poly_mul(g, [1, GF_EXP[i]])
    return g


def rs_encode(data: list[int], ec_len: int) -> list[int]:
    gen = rs_generator_poly(ec_len)
    reg = list(data) + [0] * ec_len
    for i in range(len(data)):
        coef = reg[i]
        if coef != 0:
            for j in range(1, len(gen)):
                reg[i + j] ^= gf_mul(gen[j], coef)
    return reg[len(data) :]


def rs_syndromes_zero(codeword_block: list[int], ec_len: int) -> bool:
    for i in range(ec_len):
        root = GF_EXP[i]
        acc = 0
        for byte_val in codeword_block:
            acc = gf_mul(acc, root) ^ byte_val
        if acc != 0:
            return False
    return True


def qr_format_bits(ec_indicator: int, mask_id: int) -> int:
    data = ((ec_indicator & 0x3) << 3) | (mask_id & 0x7)
    rem = data << 10
    for bit in range(14, 9, -1):
        if rem & (1 << bit):
            rem ^= 0x537 << (bit - 10)
    return ((data << 10) | rem) ^ 0x5412


def qr_version_bits(version: int) -> int:
    rem = version << 12
    for bit in range(17, 11, -1):
        if rem & (1 << bit):
            rem ^= 0x1F25 << (bit - 12)
    return (version << 12) | rem


def qr_mask_bit(mask_id: int, r: int, c: int) -> bool:
    if mask_id == 0:
        return (r + c) % 2 == 0
    if mask_id == 1:
        return r % 2 == 0
    if mask_id == 2:
        return c % 3 == 0
    if mask_id == 3:
        return (r + c) % 3 == 0
    if mask_id == 4:
        return (r // 2 + c // 3) % 2 == 0
    if mask_id == 5:
        return ((r * c) % 2) + ((r * c) % 3) == 0
    if mask_id == 6:
        return (((r * c) % 2) + ((r * c) % 3)) % 2 == 0
    if mask_id == 7:
        return (((r + c) % 2) + ((r * c) % 3)) % 2 == 0
    raise ValueError(f"Invalid QR mask {mask_id}")


def _place_finder(grid: list[list[int]], is_func: list[list[bool]], top: int, left: int):
    size = len(grid)
    for dr in range(-1, 8):
        for dc in range(-1, 8):
            r, c = top + dr, left + dc
            if 0 <= r < size and 0 <= c < size:
                in_outer = 0 <= dr <= 6 and 0 <= dc <= 6
                on_border = dr in (0, 6) or dc in (0, 6)
                in_core = 2 <= dr <= 4 and 2 <= dc <= 4
                grid[r][c] = 1 if (in_outer and (on_border or in_core)) else 0
                is_func[r][c] = True


def _place_alignment(grid: list[list[int]], is_func: list[list[bool]], cr: int, cc: int):
    for dr in range(-2, 3):
        for dc in range(-2, 3):
            r, c = cr + dr, cc + dc
            dist = max(abs(dr), abs(dc))
            grid[r][c] = 1 if dist in (0, 2) else 0
            is_func[r][c] = True


def _write_format_info(grid: list[list[int]], is_func: list[list[bool]], fmt_word: int):
    size = len(grid)
    tl_coords = [
        (8, 0), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 7), (8, 8),
        (7, 8), (5, 8), (4, 8), (3, 8), (2, 8), (1, 8), (0, 8),
    ]
    split_coords = [
        (size - 1, 8), (size - 2, 8), (size - 3, 8), (size - 4, 8),
        (size - 5, 8), (size - 6, 8), (size - 7, 8),
        (8, size - 8), (8, size - 7), (8, size - 6), (8, size - 5),
        (8, size - 4), (8, size - 3), (8, size - 2), (8, size - 1),
    ]
    for idx in range(15):
        bit = (fmt_word >> (14 - idx)) & 1
        r1, c1 = tl_coords[idx]
        r2, c2 = split_coords[idx]
        grid[r1][c1] = bit
        grid[r2][c2] = bit
        is_func[r1][c1] = True
        is_func[r2][c2] = True

    grid[size - 8][8] = 1
    is_func[size - 8][8] = True


def _build_base_matrix(version: int):
    size = 17 + 4 * version
    grid = [[0] * size for _ in range(size)]
    is_func = [[False] * size for _ in range(size)]

    _place_finder(grid, is_func, 0, 0)
    _place_finder(grid, is_func, 0, size - 7)
    _place_finder(grid, is_func, size - 7, 0)

    centers = QR_ALIGNMENT_CENTERS[version]
    for cr in centers:
        for cc in centers:
            if (cr == 6 and cc == 6) or (cr == 6 and cc == size - 7) or (cr == size - 7 and cc == 6):
                continue
            _place_alignment(grid, is_func, cr, cc)

    for i in range(8, size - 8):
        val = 1 if (i % 2 == 0) else 0
        if not is_func[6][i]:
            grid[6][i] = val
            is_func[6][i] = True
        if not is_func[i][6]:
            grid[i][6] = val
            is_func[i][6] = True

    _write_format_info(grid, is_func, 0)

    if version >= 7:
        vword = qr_version_bits(version)
        for i in range(18):
            bit = (vword >> i) & 1
            r = i // 3
            c = (size - 11) + (i % 3)
            grid[r][c] = bit
            is_func[r][c] = True
            grid[c][r] = bit
            is_func[c][r] = True

    return grid, is_func


def _zigzag_coords(size: int, is_func: list[list[bool]]) -> list[tuple[int, int]]:
    coords = []
    col = size - 1
    upward = True
    while col > 0:
        if col == 6:
            col -= 1
        rows = range(size - 1, -1, -1) if upward else range(size)
        for r in rows:
            for dc in (0, 1):
                c = col - dc
                if not is_func[r][c]:
                    coords.append((r, c))
        upward = not upward
        col -= 2
    return coords


def _penalty_score(grid: list[list[int]]) -> int:
    n = len(grid)
    score = 0

    for r in range(n):
        run_val = grid[r][0]
        run_len = 1
        for c in range(1, n):
            if grid[r][c] == run_val:
                run_len += 1
            else:
                if run_len >= 5:
                    score += 3 + (run_len - 5)
                run_val = grid[r][c]
                run_len = 1
        if run_len >= 5:
            score += 3 + (run_len - 5)

    for c in range(n):
        run_val = grid[0][c]
        run_len = 1
        for r in range(1, n):
            if grid[r][c] == run_val:
                run_len += 1
            else:
                if run_len >= 5:
                    score += 3 + (run_len - 5)
                run_val = grid[r][c]
                run_len = 1
        if run_len >= 5:
            score += 3 + (run_len - 5)

    for r in range(n - 1):
        for c in range(n - 1):
            v = grid[r][c]
            if grid[r][c + 1] == v and grid[r + 1][c] == v and grid[r + 1][c + 1] == v:
                score += 3

    pat_a = [1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0]
    pat_b = [0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1]
    for r in range(n):
        row = grid[r]
        for c in range(n - 10):
            seg = row[c : c + 11]
            if seg == pat_a or seg == pat_b:
                score += 40
    for c in range(n):
        col = [grid[r][c] for r in range(n)]
        for r in range(n - 10):
            seg = col[r : r + 11]
            if seg == pat_a or seg == pat_b:
                score += 40

    dark = sum(sum(row) for row in grid)
    total = n * n
    k = abs(dark * 20 - total * 10) // total
    score += k * 10
    return score


def encode_qr_matrix(text: str) -> tuple[list[list[int]], int, int]:
    payload = text.encode("utf-8")
    chosen_ver = None
    for ver, spec in QR_LEVEL_L_SPECS.items():
        _, _, g1_b, g1_d, g2_b, g2_d = spec
        data_cap = g1_b * g1_d + g2_b * g2_d
        count_bits = 8 if ver <= 9 else 16
        needed_bytes = (4 + count_bits + len(payload) * 8 + 7) // 8
        if needed_bytes <= data_cap:
            chosen_ver = ver
            break

    if chosen_ver is None:
        raise ValueError(f"QR payload too long for V1..V10-L ({len(payload)} bytes)")

    _, ec_len, g1_b, g1_d, g2_b, g2_d = QR_LEVEL_L_SPECS[chosen_ver]
    data_cap = g1_b * g1_d + g2_b * g2_d
    count_bits = 8 if chosen_ver <= 9 else 16

    bits = [0, 1, 0, 0]  # Byte mode (0100)
    for b in range(count_bits - 1, -1, -1):
        bits.append((len(payload) >> b) & 1)
    for byte_val in payload:
        for b in range(7, -1, -1):
            bits.append((byte_val >> b) & 1)

    max_data_bits = data_cap * 8
    term = min(4, max_data_bits - len(bits))
    bits.extend([0] * term)
    while len(bits) % 8 != 0:
        bits.append(0)

    data_cw = []
    for i in range(0, len(bits), 8):
        val = 0
        for b in bits[i : i + 8]:
            val = (val << 1) | b
        data_cw.append(val)

    pad_cycle = [0xEC, 0x11]
    pad_idx = 0
    while len(data_cw) < data_cap:
        data_cw.append(pad_cycle[pad_idx % 2])
        pad_idx += 1

    blocks_data = []
    blocks_ec = []
    offset = 0
    for _ in range(g1_b):
        blk = data_cw[offset : offset + g1_d]
        offset += g1_d
        blocks_data.append(blk)
        blocks_ec.append(rs_encode(blk, ec_len))
    for _ in range(g2_b):
        blk = data_cw[offset : offset + g2_d]
        offset += g2_d
        blocks_data.append(blk)
        blocks_ec.append(rs_encode(blk, ec_len))

    interleaved = []
    max_blk_len = max(len(b) for b in blocks_data)
    for i in range(max_blk_len):
        for blk in blocks_data:
            if i < len(blk):
                interleaved.append(blk[i])
    for i in range(ec_len):
        for ec in blocks_ec:
            interleaved.append(ec[i])

    base_grid, is_func = _build_base_matrix(chosen_ver)
    coords = _zigzag_coords(len(base_grid), is_func)

    stream_bits = []
    for byte_val in interleaved:
        for b in range(7, -1, -1):
            stream_bits.append((byte_val >> b) & 1)
    while len(stream_bits) < len(coords):
        stream_bits.append(0)

    best_grid = None
    best_mask = 0
    best_score = None

    for mask_id in range(8):
        cand = [row[:] for row in base_grid]
        for idx, (r, c) in enumerate(coords):
            bit = stream_bits[idx]
            if qr_mask_bit(mask_id, r, c):
                bit ^= 1
            cand[r][c] = bit
        fmt = qr_format_bits(0b01, mask_id)
        _write_format_info(cand, [[row[:] for row in is_func]][0], fmt)
        sc = _penalty_score(cand)
        if best_score is None or sc < best_score:
            best_score = sc
            best_grid = cand
            best_mask = mask_id

    return best_grid, chosen_ver, best_mask


def verify_and_decode_qr_matrix(grid: list[list[int]]) -> str:
    size = len(grid)
    assert (size - 17) % 4 == 0, "Invalid QR matrix dimension"
    version = (size - 17) // 4
    assert version in QR_LEVEL_L_SPECS, f"Unsupported version {version}"

    assert grid[size - 8][8] == 1, "Dark module missing"

    tl_coords = [
        (8, 0), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 7), (8, 8),
        (7, 8), (5, 8), (4, 8), (3, 8), (2, 8), (1, 8), (0, 8),
    ]
    split_coords = [
        (size - 1, 8), (size - 2, 8), (size - 3, 8), (size - 4, 8),
        (size - 5, 8), (size - 6, 8), (size - 7, 8),
        (8, size - 8), (8, size - 7), (8, size - 6), (8, size - 5),
        (8, size - 4), (8, size - 3), (8, size - 2), (8, size - 1),
    ]
    fmt1 = 0
    fmt2 = 0
    for r, c in tl_coords:
        fmt1 = (fmt1 << 1) | grid[r][c]
    for r, c in split_coords:
        fmt2 = (fmt2 << 1) | grid[r][c]
    assert fmt1 == fmt2, f"Format copies mismatch: {fmt1:#06x} != {fmt2:#06x}"

    unmasked_fmt = fmt1 ^ 0x5412
    rem = unmasked_fmt
    for bit in range(14, 9, -1):
        if rem & (1 << bit):
            rem ^= 0x537 << (bit - 10)
    assert rem == 0, f"Non-zero BCH syndrome on format info: {rem}"

    ec_ind = (unmasked_fmt >> 13) & 0x3
    mask_id = (unmasked_fmt >> 10) & 0x7
    assert ec_ind == 0b01, f"Expected Level L (01), got {ec_ind:02b}"

    _, is_func = _build_base_matrix(version)
    coords = _zigzag_coords(size, is_func)
    raw_bits = []
    for r, c in coords:
        bit = grid[r][c]
        if qr_mask_bit(mask_id, r, c):
            bit ^= 1
        raw_bits.append(bit)

    total_cw, ec_len, g1_b, g1_d, g2_b, g2_d = QR_LEVEL_L_SPECS[version]
    interleaved = []
    for i in range(total_cw):
        val = 0
        for b in raw_bits[i * 8 : (i + 1) * 8]:
            val = (val << 1) | b
        interleaved.append(val)

    num_blocks = g1_b + g2_b
    block_lens = [g1_d] * g1_b + [g2_d] * g2_b
    blocks_data = [[] for _ in range(num_blocks)]
    blocks_ec = [[] for _ in range(num_blocks)]

    idx = 0
    max_d = max(block_lens)
    for col_i in range(max_d):
        for b_i in range(num_blocks):
            if col_i < block_lens[b_i]:
                blocks_data[b_i].append(interleaved[idx])
                idx += 1
    for _ in range(ec_len):
        for b_i in range(num_blocks):
            blocks_ec[b_i].append(interleaved[idx])
            idx += 1

    all_data = []
    for b_i in range(num_blocks):
        full_blk = blocks_data[b_i] + blocks_ec[b_i]
        assert rs_syndromes_zero(full_blk, ec_len), f"RS syndrome check failed on block {b_i}"
        all_data.extend(blocks_data[b_i])

    data_bits = []
    for byte_val in all_data:
        for b in range(7, -1, -1):
            data_bits.append((byte_val >> b) & 1)

    mode = (data_bits[0] << 3) | (data_bits[1] << 2) | (data_bits[2] << 1) | data_bits[3]
    assert mode == 0b0100, f"Expected Byte mode (0100), got {mode:04b}"

    count_bits = 8 if version <= 9 else 16
    char_len = 0
    for b in data_bits[4 : 4 + count_bits]:
        char_len = (char_len << 1) | b

    pos = 4 + count_bits
    out_bytes = bytearray()
    for _ in range(char_len):
        val = 0
        for b in data_bits[pos : pos + 8]:
            val = (val << 1) | b
        out_bytes.append(val)
        pos += 8

    return out_bytes.decode("utf-8")


def qr_matrix_to_svg(grid: list[list[int]], quiet: int = 4, pixel_size: int = 216) -> str:
    n = len(grid)
    total = n + quiet * 2
    parts = []
    for r, row in enumerate(grid):
        y = r + quiet
        c = 0
        while c < n:
            if row[c] == 1:
                start = c
                while c < n and row[c] == 1:
                    c += 1
                w = c - start
                parts.append(f"M{start + quiet},{y}h{w}v1h-{w}z")
            else:
                c += 1
    d_attr = "".join(parts)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {total} {total}" '
        f'width="{pixel_size}" height="{pixel_size}" shape-rendering="crispEdges" '
        f'role="img" aria-label="QR код для прямого скачивания APK">'
        f'<rect width="{total}" height="{total}" fill="#ffffff"/>'
        f'<path d="{d_attr}" fill="#0b0e13"/></svg>'
    )


def build_landing_html(apk_url: str, size_mb: float, sha256_hex: str, version: str) -> tuple[str, int, int]:
    grid, qr_ver, qr_mask = encode_qr_matrix(apk_url)
    decoded = verify_and_decode_qr_matrix(grid)
    if decoded != apk_url:
        raise RuntimeError("QR round-trip verification mismatch")

    svg_markup = qr_matrix_to_svg(grid, quiet=4, pixel_size=220)
    esc_url = html.escape(apk_url, quote=True)
    esc_ver = html.escape(version)
    esc_sha = html.escape(sha256_hex.lower())
    size_str = f"{size_mb:.2f}"

    page = f"""<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>CRUSHER CORE: Hyper Shredder — Скачать APK для Android</title>
  <style>
    :root {{
      --bg-0: #090b10;
      --bg-1: #121722;
      --bg-2: #19202e;
      --line: #2b364c;
      --amber: #ff9d00;
      --amber-hi: #ffc247;
      --ember: #ff4500;
      --text: #eef2f8;
      --muted: #98a4b8;
      --ok: #2ee59d;
    }}
    * {{ box-sizing: border-box; margin: 0; padding: 0; }}
    body {{
      background:
        radial-gradient(circle at 50% 0%, rgba(255, 157, 0, 0.14), transparent 52%),
        linear-gradient(180deg, var(--bg-0) 0%, #0d1119 100%);
      color: var(--text);
      font-family: "Segoe UI", Roboto, system-ui, -apple-system, sans-serif;
      line-height: 1.5;
      min-height: 100vh;
    }}
    .hazard-bar {{
      height: 8px;
      background: repeating-linear-gradient(
        -45deg,
        var(--amber),
        var(--amber) 14px,
        #11151d 14px,
        #11151d 28px
      );
      border-bottom: 1px solid rgba(255, 157, 0, 0.45);
    }}
    .wrap {{
      max-width: 1080px;
      margin: 0 auto;
      padding: 32px 20px 64px;
    }}
    .badge-row {{
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      margin-bottom: 16px;
    }}
    .badge {{
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      padding: 5px 12px;
      background: var(--bg-2);
      border: 1px solid var(--line);
      color: var(--amber-hi);
      clip-path: polygon(8px 0, 100% 0, calc(100% - 8px) 100%, 0 100%);
    }}
    .hero {{
      display: grid;
      grid-template-columns: 1.35fr 0.85fr;
      gap: 28px;
      background: linear-gradient(145deg, var(--bg-1), #0e131d);
      border: 1px solid var(--line);
      padding: 32px;
      position: relative;
      box-shadow: 0 22px 60px rgba(0, 0, 0, 0.55);
    }}
    .hero::before {{
      content: "";
      position: absolute;
      inset: 0;
      border-top: 3px solid var(--amber);
      pointer-events: none;
    }}
    h1 {{
      font-size: clamp(28px, 4.2vw, 44px);
      line-height: 1.08;
      letter-spacing: 0.02em;
      font-weight: 900;
      text-transform: uppercase;
      margin-bottom: 12px;
    }}
    h1 span {{
      color: var(--amber);
    }}
    .lead {{
      color: var(--muted);
      font-size: 16px;
      margin-bottom: 24px;
      max-width: 58ch;
    }}
    .cta-row {{
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
      margin-bottom: 24px;
    }}
    .btn-dl {{
      display: inline-flex;
      align-items: center;
      gap: 12px;
      background: linear-gradient(90deg, var(--amber) 0%, var(--ember) 100%);
      color: #090b10;
      text-decoration: none;
      font-weight: 900;
      font-size: 18px;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      padding: 16px 28px;
      clip-path: polygon(12px 0, 100% 0, calc(100% - 12px) 100%, 0 100%);
      box-shadow: 0 8px 28px rgba(255, 120, 0, 0.35);
      transition: transform 0.12s ease, filter 0.12s ease;
    }}
    .btn-dl:hover {{
      transform: translateY(-2px);
      filter: brightness(1.08);
    }}
    .meta-grid {{
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 10px;
      margin-bottom: 14px;
    }}
    .meta-cell {{
      background: rgba(9, 11, 16, 0.65);
      border: 1px solid var(--line);
      padding: 10px 14px;
    }}
    .meta-cell .k {{
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--muted);
    }}
    .meta-cell .v {{
      font-size: 14px;
      font-weight: 700;
      color: var(--text);
    }}
    .sha-box {{
      background: #080a0f;
      border: 1px solid var(--line);
      padding: 10px 14px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
    }}
    .sha-code {{
      font-family: Consolas, "Courier New", monospace;
      font-size: 12px;
      color: var(--ok);
      word-break: break-all;
    }}
    .btn-copy {{
      background: var(--bg-2);
      color: var(--text);
      border: 1px solid var(--line);
      padding: 6px 12px;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      flex-shrink: 0;
    }}
    .btn-copy:hover {{ border-color: var(--amber); color: var(--amber-hi); }}
    .qr-panel {{
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      background: #0b0f17;
      border: 1px solid var(--line);
      padding: 22px;
      text-align: center;
    }}
    .qr-frame {{
      padding: 10px;
      background: #ffffff;
      border: 3px solid var(--amber);
      line-height: 0;
      margin-bottom: 12px;
    }}
    .qr-caption {{
      font-size: 13px;
      color: var(--muted);
    }}
    .qr-caption strong {{
      display: block;
      color: var(--text);
      margin-bottom: 3px;
    }}
    h2 {{
      font-size: 20px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      margin: 40px 0 18px;
      padding-left: 12px;
      border-left: 4px solid var(--amber);
    }}
    .shots-grid {{
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 20px;
    }}
    .shot-card {{
      background: var(--bg-1);
      border: 1px solid var(--line);
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }}
    .shot-card img {{
      width: 100%;
      aspect-ratio: 9 / 20;
      object-fit: cover;
      background: #06080c;
      display: block;
      border-bottom: 1px solid var(--line);
    }}
    .shot-cap {{
      padding: 12px 14px;
      font-size: 13px;
      color: var(--muted);
    }}
    .shot-cap strong {{
      color: var(--text);
      display: block;
      margin-bottom: 2px;
    }}
    .steps {{
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 16px;
    }}
    .step {{
      background: var(--bg-1);
      border: 1px solid var(--line);
      padding: 18px;
    }}
    .step-num {{
      display: inline-block;
      font-weight: 900;
      font-size: 13px;
      color: #090b10;
      background: var(--amber);
      padding: 2px 9px;
      margin-bottom: 8px;
    }}
    .step h3 {{
      font-size: 15px;
      margin-bottom: 6px;
    }}
    .step p {{
      font-size: 13px;
      color: var(--muted);
    }}
    @media (max-width: 820px) {{
      .hero {{ grid-template-columns: 1fr; padding: 22px; }}
      .shots-grid, .steps {{ grid-template-columns: 1fr; }}
    }}
  </style>
</head>
<body>
  <div class="hazard-bar"></div>
  <main class="wrap">
    <div class="badge-row">
      <span class="badge">Android 8.0+ (API 26–35)</span>
      <span class="badge">ARM64 IL2CPP • URP Forward</span>
      <span class="badge">100% Procedural 3D &amp; DSP Audio</span>
    </div>

    <section class="hero">
      <div>
        <h1>CRUSHER CORE: <span>HYPER SHREDDER</span></h1>
        <p class="lead">
          Индустриальный 3D tower-shredder для Android: двухбарабанный карбидный ротор, плазменные орбы,
          гравитационная воронка Black Hole, овердрайв Golden Tower, карты и лаборатории.
        </p>

        <div class="cta-row">
          <a class="btn-dl" href="{esc_url}" download>
            <span>⬇ Скачать APK ({size_str} МБ)</span>
          </a>
        </div>

        <div class="meta-grid">
          <div class="meta-cell">
            <div class="k">Версия сборки</div>
            <div class="v">v{esc_ver} (Release Signed)</div>
          </div>
          <div class="meta-cell">
            <div class="k">Архитектура / Рендер</div>
            <div class="v">ARM64-v8a • Vulkan / GLES3</div>
          </div>
          <div class="meta-cell">
            <div class="k">Размер файла</div>
            <div class="v">{size_str} МБ (.apk)</div>
          </div>
          <div class="meta-cell">
            <div class="k">Пакет Android</div>
            <div class="v">com.crushercore.hypershredder</div>
          </div>
        </div>

        <div class="sha-box">
          <div>
            <div style="font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:0.08em;">SHA-256 контрольная сумма</div>
            <div class="sha-code" id="sha-val">{esc_sha}</div>
          </div>
          <button class="btn-copy" type="button" onclick="navigator.clipboard&&navigator.clipboard.writeText(document.getElementById('sha-val').textContent).then(()=>{{this.textContent='Скопировано'}})">Копировать</button>
        </div>
      </div>

      <aside class="qr-panel">
        <div class="qr-frame">
          {svg_markup}
        </div>
        <div class="qr-caption">
          <strong>Сканируйте камерой телефона</strong>
          Прямая ссылка на скачивание <code>.apk</code> с GitHub Releases (QR v{qr_ver}-L)
        </div>
      </aside>
    </section>

    <h2>Живые кадры Android 16 (tt36p, 1080×2400)</h2>
    <section class="shots-grid">
      <article class="shot-card">
        <img src="shots/shot_01.png" alt="Crusher Core — Android Live Старт W1" loading="lazy">
        <div class="shot-cap">
          <strong>01 • Старт забега (Волна 1, АТАКА)</strong>
          Живой кадр с Android: ржавый ротор G0, первые враги, разлёт осколков и стартовая палуба.
        </div>
      </article>
      <article class="shot-card">
        <img src="shots/shot_02.png" alt="Crusher Core — Android Live Прокачка и ЗАЩИТА" loading="lazy">
        <div class="shot-cap">
          <strong>02 • Живая прокачка статов (ЗАЩИТА)</strong>
          Покупка улучшений на ходу и переключение боевых вкладок без паузы арены.
        </div>
      </article>
      <article class="shot-card">
        <img src="shots/shot_03.png" alt="Crusher Core — Android Live Ангар Гипер-Шредера" loading="lazy">
        <div class="shot-cap">
          <strong>03 • Ангар Гипер-Шредера</strong>
          Мастерская, 4 слота карт с гачей, лаборатории с ускорением клетками и ультимейты.
        </div>
      </article>
    </section>

    <h2>Мид и лейт-гейм разгон (Волна 45 / Волна 85)</h2>
    <section class="shots-grid" style="grid-template-columns: repeat(2, minmax(0, 1fr)); max-width: 720px;">
      <article class="shot-card">
        <img src="shots/gate2_03_w45_blender_deathray.png" alt="Волна 45 — Титановый блендер и Death Ray" loading="lazy">
        <div class="shot-cap">
          <strong>Волна 45 • Титановый блендер G2 &amp; Death Ray</strong>
          Плазменные орбы на дорожке, объёмный луч Death Ray и оранжевые кольца Magnetic Garlic против Вампира.
        </div>
      </article>
      <article class="shot-card">
        <img src="shots/gate2_04_w85_gt_bh_moltencore.png" alt="Волна 85 — Комбо Golden Tower, Black Hole и Molten Core" loading="lazy">
        <div class="shot-cap">
          <strong>Волна 85 • Овердрайв GT + Black Hole + Molten Core</strong>
          Синхронный горн Golden Tower, воронка Black Hole, дуги Chain Lightning и бонусное поле Golden Bot.
        </div>
      </article>
    </section>

    <h2>Установка на Android за 30 секунд</h2>
    <section class="steps">
      <div class="step">
        <span class="step-num">ШАГ 1</span>
        <h3>Скачайте APK</h3>
        <p>Нажмите кнопку «Скачать APK» выше на смартфоне или наведите камеру на QR-код.</p>
      </div>
      <div class="step">
        <span class="step-num">ШАГ 2</span>
        <h3>Подтвердите установку</h3>
        <p>Откройте загруженный файл и при первом запуске разрешите установку из вашего браузера.</p>
      </div>
      <div class="step">
        <span class="step-num">ШАГ 3</span>
        <h3>Запустите игру</h3>
        <p>При окне Google Play Protect для инди-сборки нажмите «Подробнее → Всё равно установить».</p>
      </div>
    </section>
  </main>
</body>
</html>
"""
    return page, qr_ver, qr_mask


def run_self_test():
    # 1. ISO/IEC 18004 Annex C exact format info vector checks
    assert qr_format_bits(0b01, 0) == 0x77C4, f"Expected 0x77C4, got {qr_format_bits(0b01, 0):#06x}"
    assert qr_format_bits(0b00, 2) == 0x5E7C, f"Expected 0x5E7C, got {qr_format_bits(0b00, 2):#06x}"
    assert qr_version_bits(7) == 0x07C94, f"Expected 0x07C94, got {qr_version_bits(7):#07x}"

    # 2. Full round-trip matrix + RS syndrome + UTF-8 decode across short, medium, and GitHub Release URLs
    test_urls = [
        "https://mrak0be5.github.io/crusher-core/",
        "https://github.com/Mrak0be5/crusher-core/releases/download/v1.0.0/CrusherCore-v1.0.0-arm64.apk",
        "https://github.com/Mrak0be5/crusher-core/releases/download/v1.0.0-rc1/CrusherCore-HyperShredder-v1.0.0-arm64-release-signed.apk",
    ]
    for u in test_urls:
        mat, ver, mask = encode_qr_matrix(u)
        dec = verify_and_decode_qr_matrix(mat)
        assert dec == u, f"Round-trip failed for {u}: got {dec}"

    # 3. End-to-end HTML generation into temp dir (no draft left in repo)
    sample_url = test_urls[1]
    sample_sha = hashlib.sha256(b"crusher-core-self-test").hexdigest()
    html_text, ver, mask = build_landing_html(sample_url, 48.75, sample_sha, "1.0.0")
    with tempfile.TemporaryDirectory() as tmpdir:
        out_html = os.path.join(tmpdir, "index.html")
        with open(out_html, "w", encoding="utf-8") as f:
            f.write(html_text)
        assert os.path.getsize(out_html) > 6000
        assert "<svg " in html_text and "shots/shot_01.png" in html_text and sample_sha in html_text

    print(f"GEN_LANDING_SELF_TEST_PASS | QR_V={ver}-L (mask={mask}) | AnnexC=OK | HTML_LEN={len(html_text)}B")


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Generate Crusher Core GitHub Pages landing HTML with inline SVG QR.")
    parser.add_argument("--self-test", action="store_true", help="Run QR ISO/RS/HTML self-tests without writing repo drafts.")
    parser.add_argument("--apk-url", default="", help="Direct HTTPS download URL of the release APK.")
    parser.add_argument("--apk-path", default="", help="Local APK path to compute exact MB size and SHA-256.")
    parser.add_argument("--size-mb", type=float, default=0.0, help="Explicit APK size in MB if --apk-path is omitted.")
    parser.add_argument("--sha256", default="", help="Explicit SHA-256 hex digest if --apk-path is omitted.")
    parser.add_argument("--version", default="1.0.0", help="Release version string.")
    parser.add_argument("--shots-dir", default="", help="Optional folder with shot_01.png..shot_03.png to copy into <out_dir>/shots/.")
    parser.add_argument("--gate2-dir", default="", help="Optional folder with gate2_03_w45_blender_deathray.png & gate2_04_w85_gt_bh_moltencore.png.")
    parser.add_argument("--out", default="web/index.html", help="Output HTML file path.")
    args = parser.parse_args()

    if args.self_test:
        run_self_test()
        return

    if not args.apk_url:
        parser.error("--apk-url is required (or pass --self-test)")

    size_mb = args.size_mb
    sha256_hex = args.sha256

    if args.apk_path:
        if not os.path.isfile(args.apk_path):
            raise FileNotFoundError(f"APK file not found: {args.apk_path}")
        size_mb = os.path.getsize(args.apk_path) / (1024.0 * 1024.0)
        h = hashlib.sha256()
        with open(args.apk_path, "rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""):
                h.update(chunk)
        sha256_hex = h.hexdigest()

    if size_mb <= 0 or not sha256_hex:
        parser.error("Provide --apk-path or both --size-mb and --sha256")

    html_text, qr_ver, qr_mask = build_landing_html(args.apk_url, size_mb, sha256_hex, args.version)
    out_abs = os.path.abspath(args.out)
    out_dir = os.path.dirname(out_abs)
    os.makedirs(out_dir, exist_ok=True)
    with open(out_abs, "w", encoding="utf-8") as f:
        f.write(html_text)

    shots_dst = os.path.join(out_dir, "shots")
    if args.shots_dir:
        os.makedirs(shots_dst, exist_ok=True)
        for idx in (1, 2, 3):
            name = f"shot_0{idx}.png"
            src = os.path.abspath(os.path.join(args.shots_dir, name))
            dst = os.path.abspath(os.path.join(shots_dst, name))
            if not os.path.isfile(src):
                raise FileNotFoundError(f"Missing screenshot {src}")
            if src != dst:
                shutil.copy2(src, dst)

    if args.gate2_dir:
        os.makedirs(shots_dst, exist_ok=True)
        for name in ("gate2_03_w45_blender_deathray.png", "gate2_04_w85_gt_bh_moltencore.png"):
            src = os.path.abspath(os.path.join(args.gate2_dir, name))
            dst = os.path.abspath(os.path.join(shots_dst, name))
            if not os.path.isfile(src):
                raise FileNotFoundError(f"Missing Gate2 showcase render {src}")
            if src != dst:
                shutil.copy2(src, dst)

    print(
        f"LANDING_GENERATED: {out_abs} | QR=v{qr_ver}-L(mask={qr_mask}) | "
        f"Size={size_mb:.2f}MB | SHA256={sha256_hex[:16]}..."
    )


if __name__ == "__main__":
    main()
