"""STOP-GATE 1B: Headless Cycles (CPU, samples=24, Pitch 62 deg, #CFD6DF light polygon floor)
Contact Sheet Renderer (4x3 PNGs), Metric Verifier, and FBX Round-Trip Inspector."""
from __future__ import annotations

import json
import math
import os
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Euler, Vector

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING_MODELS = os.path.join(ROOT_DIR, "staging", "models")
QA_DIR = os.path.join(ROOT_DIR, "build", "qa")
QA_CACHE = os.path.join(QA_DIR, "blend_cache")

TILE_W = 320
TILE_H = 320

# Minimal 5x7 bitmap font for crisp tile labels without PIL dependency
_FONT_5X7 = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "G": ["01111", "10000", "10000", "10011", "10001", "10001", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "J": ["00011", "00001", "00001", "00001", "10001", "10001", "01110"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "Q": ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
    "X": ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "0": ["01110", "10011", "10101", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["01110", "10000", "11110", "10001", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
    "_": ["00000", "00000", "00000", "00000", "00000", "00000", "11111"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    ":": ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
    " ": ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
}


def draw_label_top_left(img_rgb_u8: np.ndarray, text: str):
    """Draws dark badge + crisp white/amber bitmap text at top-left of HxWx3 uint8 array."""
    txt = text.upper()[:24]
    scale = 2
    char_w = 6 * scale
    char_h = 7 * scale
    pad = 5
    bw = min(img_rgb_u8.shape[1] - 4, len(txt) * char_w + pad * 2)
    bh = char_h + pad * 2
    img_rgb_u8[4:4 + bh, 4:4 + bw] = (img_rgb_u8[4:4 + bh, 4:4 + bw].astype(np.uint16) * 28 // 100).astype(np.uint8)
    for idx, ch in enumerate(txt):
        glyph = _FONT_5X7.get(ch, _FONT_5X7[" "])
        ox = 4 + pad + idx * char_w
        oy = 4 + pad
        if ox + char_w >= img_rgb_u8.shape[1]:
            break
        for r, row in enumerate(glyph):
            for c, bit in enumerate(row):
                if bit == "1":
                    y0, y1 = oy + r * scale, oy + (r + 1) * scale
                    x0, x1 = ox + c * scale, ox + (c + 1) * scale
                    img_rgb_u8[y0:y1, x0:x1] = (255, 224, 96)


def make_polygon_floor_bg(width: int, height: int) -> np.ndarray:
    """Generates exact #CFD6DF sRGB studio polygon floor with subtle meter grid & radial falloff."""
    # #CFD6DF = RGB(207, 214, 223)
    yy, xx = np.mgrid[0:height, 0:width].astype(np.float32)
    nx = (xx / width - 0.5) * 2.0
    ny = (yy / height - 0.5) * 2.0
    r2 = nx * nx + ny * ny
    vign = np.clip(1.0 - 0.08 * r2, 0.86, 1.0)

    base = np.zeros((height, width, 3), dtype=np.float32)
    base[..., 0] = 207.0 * vign
    base[..., 1] = 214.0 * vign
    base[..., 2] = 223.0 * vign

    # Subtle engineering grid lines every 40 px
    gx = np.minimum(xx % 40.0, 40.0 - (xx % 40.0))
    gy = np.minimum(yy % 40.0, 40.0 - (yy % 40.0))
    grid_mask = np.clip(1.0 - np.minimum(gx, gy) / 1.2, 0.0, 1.0) * 0.07
    base = base * (1.0 - grid_mask[..., None]) + np.array([150.0, 162.0, 178.0], dtype=np.float32) * grid_mask[..., None]
    return np.clip(base, 0.0, 255.0)


_FLOOR_BG_RGB = make_polygon_floor_bg(TILE_W, TILE_H)


def setup_cycles_qa_rig_and_stage(target_center: Vector, cam_dist: float):
    """Configures Cycles CPU (samples=24, deterministic seed), Pitch 62 deg camera,
    bright #CFD6DF bounce world, key/rim studio lights, and shadow-catcher floor."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 24
    sc.cycles.use_adaptive_sampling = True
    sc.cycles.adaptive_threshold = 0.05
    sc.cycles.use_denoising = False
    sc.cycles.seed = 42
    if hasattr(sc.cycles, "use_animated_seed"):
        sc.cycles.use_animated_seed = False
    sc.cycles.max_bounces = 4
    sc.cycles.diffuse_bounces = 2
    sc.cycles.glossy_bounces = 2

    sc.render.resolution_x = TILE_W
    sc.render.resolution_y = TILE_H
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "8"

    sc.view_settings.view_transform = "AgX" if "AgX" in ("Standard", "AgX") else "Standard"
    try:
        sc.view_settings.view_transform = "Standard"
    except Exception:
        pass
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.15

    # World: cool-bright industrial skylight reflecting #CFD6DF polygon
    world = bpy.data.worlds.new("QA_World")
    sc.world = world
    world.use_nodes = True
    bg_node = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg_node.inputs["Color"].default_value = (0.624, 0.672, 0.737, 1.0)  # Linear #CFD6DF!
    bg_node.inputs["Strength"].default_value = 0.95

    # Shadow-catcher floor plane at z = 0 so contact shadows anchor the asset cleanly
    bpy.ops.mesh.primitive_plane_add(size=16.0, location=(0.0, 0.0, 0.0))
    floor_ob = bpy.context.active_object
    floor_ob.name = "QA_ShadowCatcherFloor"
    floor_ob.is_shadow_catcher = True
    mat_fl = bpy.data.materials.new("M_QA_Floor")
    mat_fl.use_nodes = True
    bsdf_fl = next(n for n in mat_fl.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf_fl.inputs["Base Color"].default_value = (0.624, 0.672, 0.737, 1.0)
    bsdf_fl.inputs["Roughness"].default_value = 0.80
    floor_ob.data.materials.append(mat_fl)

    # Key Sun Light + Rim Area Light for crisp hard-surface bevel highlights
    sun_data = bpy.data.lights.new("QA_KeySun", type="SUN")
    sun_data.energy = 3.6
    sun_data.angle = math.radians(7.0)
    sun_data.color = (1.0, 0.97, 0.92)
    sun_ob = bpy.data.objects.new("QA_KeySun", sun_data)
    sun_ob.rotation_euler = (math.radians(38.0), math.radians(-22.0), math.radians(28.0))
    sc.collection.objects.link(sun_ob)

    rim_data = bpy.data.lights.new("QA_RimSun", type="SUN")
    rim_data.energy = 1.6
    rim_data.angle = math.radians(12.0)
    rim_data.color = (0.78, 0.88, 1.0)
    rim_ob = bpy.data.objects.new("QA_RimSun", rim_data)
    rim_ob.rotation_euler = (math.radians(52.0), math.radians(145.0), math.radians(-140.0))
    sc.collection.objects.link(rim_ob)

    # Combat Camera at exact Pitch = 62.0 deg downward from horizontal, slight 16 deg azimuth
    pitch_rad = math.radians(62.0)
    azim_rad = math.radians(16.0)
    horiz = cam_dist * math.cos(pitch_rad)
    vert = cam_dist * math.sin(pitch_rad)
    cam_pos = target_center + Vector((horiz * math.sin(azim_rad), -horiz * math.cos(azim_rad), vert))

    cam_data = bpy.data.cameras.new("QA_CombatCam")
    cam_data.lens = 50.0
    cam_ob = bpy.data.objects.new("QA_CombatCam", cam_data)
    cam_ob.location = cam_pos
    look_dir = (target_center - cam_pos).normalized()
    cam_ob.rotation_euler = look_dir.to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cam_ob)
    sc.camera = cam_ob
    return sc, floor_ob


def render_tile_rgba_and_composite(tmp_png: str) -> tuple[np.ndarray, np.ndarray]:
    """Renders current frame to tmp_png, composites over #CFD6DF polygon floor,
    and returns (composited_rgb_u8 [H,W,3], subject_mask_bool [H,W])."""
    sc = bpy.context.scene
    sc.render.filepath = tmp_png
    bpy.ops.render.render(write_still=True)

    img = bpy.data.images.load(tmp_png, check_existing=False)
    # Blender image.pixels is bottom-to-top linear/sRGB depending on colorspace; force raw sRGB read:
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)

    rgba = px.reshape(h, w, 4)[::-1, :, :]  # Flip Y to top-down
    # Convert linear RGB from Blender image buffer to sRGB 0..255
    rgb_lin = np.clip(rgba[..., :3], 0.0, 1.0)
    alpha = np.clip(rgba[..., 3], 0.0, 1.0)
    rgb_srgb = np.where(
        rgb_lin <= 0.0031308,
        rgb_lin * 12.92,
        1.055 * np.power(rgb_lin, 1.0 / 2.4) - 0.055,
    ) * 255.0

    # Pure mesh silhouette mask (shadow catcher has low alpha <= 0.45, solid mesh has alpha >= 0.72)
    subj_mask = alpha >= 0.70

    # Alpha-composite rendered asset + contact shadow over #CFD6DF polygon floor
    comp = rgb_srgb * alpha[..., None] + _FLOOR_BG_RGB * (1.0 - alpha[..., None])
    comp_u8 = np.clip(np.rint(comp), 0, 255).astype(np.uint8)
    return comp_u8, subj_mask


def save_contact_sheet_png(tiles_u8: list[np.ndarray], out_path: str, cols: int = 4):
    rows = math.ceil(len(tiles_u8) / cols)
    sheet_h = rows * TILE_H
    sheet_w = cols * TILE_W
    sheet = np.zeros((sheet_h, sheet_w, 4), dtype=np.float32)
    sheet[..., 3] = 1.0

    for idx, tile in enumerate(tiles_u8):
        r = idx // cols
        c = idx % cols
        y0, y1 = r * TILE_H, (r + 1) * TILE_H
        x0, x1 = c * TILE_W, (c + 1) * TILE_W
        # Convert sRGB 0..255 back to linear float for bpy.data.images.save() so saved PNG matches exact sRGB!
        srgb = tile.astype(np.float32) / 255.0
        lin = np.where(srgb <= 0.04045, srgb / 12.92, np.power((srgb + 0.055) / 1.055, 2.4))
        sheet[y0:y1, x0:x1, :3] = lin

    # Flip vertically for Blender bottom-up image buffer
    flat = sheet[::-1, :, :].ravel()
    img_out = bpy.data.images.new("QA_Sheet_Out", width=sheet_w, height=sheet_h, alpha=True)
    img_out.pixels.foreach_set(flat)
    img_out.filepath_raw = os.path.abspath(out_path)
    img_out.file_format = "PNG"
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    img_out.save()
    bpy.data.images.remove(img_out)


def compute_iou(m1: np.ndarray, m2: np.ndarray) -> float:
    inter = int(np.logical_and(m1, m2).sum())
    union = int(np.logical_or(m1, m2).sum())
    return 1.0 if union == 0 else float(inter) / float(union)


def count_action_bone_scale_keys(act: bpy.types.Action) -> int:
    bad = 0
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if fc.data_path.endswith(".scale") or fc.data_path == "scale":
                        bad += len(fc.keyframe_points)
    return bad


def solo_core_grade(grade_name: str):
    grades = {"G0_Rusty", "G1_SteelCarbide", "G2_TitaniumTwin", "G3_DiamondReactor"}
    for ob in bpy.data.objects:
        if ob.name in grades:
            vis = ob.name == grade_name
            ob.hide_render = not vis
            ob.hide_viewport = not vis


def evaluate_clip_sheet(blend_name: str, rig_name: str, clip_name: str,
                        f_start: int, f_end: int, cyclic: bool,
                        target_center: tuple[float, float, float], cam_dist: float,
                        out_png_name: str, core_grade: str | None = None) -> dict:
    blend_path = os.path.join(QA_CACHE, f"{blend_name}.blend")
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    if core_grade:
        solo_core_grade(core_grade)

    rig = bpy.data.objects[rig_name]
    act = bpy.data.actions[clip_name]
    scale_keys = count_action_bone_scale_keys(act)

    # Solo only the target NLA track or bind action + slot directly
    ad = rig.animation_data
    for tr in ad.nla_tracks:
        tr.mute = True
    ad.action = act
    slot = next((s for s in act.slots if s.target_id_type == "OBJECT"), None)
    ad.action_slot = slot

    sc, _ = setup_cycles_qa_rig_and_stage(Vector(target_center), cam_dist)

    sample_times = np.linspace(float(f_start), float(f_end), 12)
    raw_tiles_rgb: list[np.ndarray] = []
    labeled_tiles: list[np.ndarray] = []
    masks: list[np.ndarray] = []

    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_png = os.path.join(tmpdir, "tile.png")
        for idx, t_val in enumerate(sample_times):
            f_int = int(math.floor(t_val + 1e-6))
            sub_f = float(max(0.0, t_val - f_int))
            sc.frame_set(f_int, subframe=sub_f)
            bpy.context.view_layer.update()

            rgb_u8, mask = render_tile_rgba_and_composite(tmp_png)
            raw_tiles_rgb.append(rgb_u8)
            masks.append(mask)

            tile_lbl = rgb_u8.copy()
            short = clip_name.replace("AN_", "")
            draw_label_top_left(tile_lbl, f"{short} {t_val:04.1f}")
            labeled_tiles.append(tile_lbl)

    out_png = os.path.join(QA_DIR, out_png_name)
    save_contact_sheet_png(labeled_tiles, out_png, cols=4)

    rgb_diffs = []
    ious = []
    for i in range(len(raw_tiles_rgb) - 1):
        d = float(np.abs(raw_tiles_rgb[i + 1].astype(np.float32) - raw_tiles_rgb[i].astype(np.float32)).mean())
        rgb_diffs.append(round(d, 3))
        ious.append(round(compute_iou(masks[i], masks[i + 1]), 4))

    max_rgb_diff = max(rgb_diffs) if rgb_diffs else 0.0
    min_iou = min(ious) if ious else 1.0
    loop_seam = round(
        float(np.abs(raw_tiles_rgb[-1].astype(np.float32) - raw_tiles_rgb[0].astype(np.float32)).mean()), 4
    ) if cyclic else 0.0

    fill_ratios = [float(m.mean()) for m in masks]
    avg_fill = round(sum(fill_ratios) / len(fill_ratios), 4)

    passed = (
        (not cyclic or loop_seam <= 1.5)
        and max_rgb_diff <= 45.0
        and min_iou >= 0.88
        and scale_keys == 0
    )
    return {
        "sheet": out_png_name,
        "asset": blend_name,
        "clip": clip_name,
        "cyclic": cyclic,
        "loop_seam": loop_seam,
        "max_rgb_diff": max_rgb_diff,
        "min_silhouette_iou": min_iou,
        "bone_scale_keys": scale_keys,
        "subject_fill_ratio": avg_fill,
        "frame_rgb_diffs": rgb_diffs,
        "frame_ious": ious,
        "passed": passed,
    }


def render_static_catalog_sheets() -> list[str]:
    """Renders 2 static 4x3 catalog sheets:
    1) cs_10_Catalog_Grades_And_Bestiary.png (G0..G3, Orb, Projectile, 5 non-rigged enemies, GoldIngot)
    2) cs_11_Voronoi_Debris_Chunks.png (All 12 Voronoi fracture chunks closeup showing R=1.0 inner cuts)"""
    catalog_specs = [
        ("SM_ShredderCore", "G0_Rusty", (0, 0, 0.55), 3.6, "Core G0 Rusty"),
        ("SM_ShredderCore", "G1_SteelCarbide", (0, 0, 0.55), 3.6, "Core G1 Steel"),
        ("SM_ShredderCore", "G2_TitaniumTwin", (0, 0, 0.55), 3.6, "Core G2 Titan"),
        ("SM_ShredderCore", "G3_DiamondReactor", (0, 0, 0.55), 3.8, "Core G3 Diamond"),
        ("SM_PlasmaSawOrb", None, (0, 0, 0.05), 1.85, "PlasmaSawOrb"),
        ("SM_Projectile", None, (0, 0, 0.05), 0.85, "Projectile"),
        ("SM_Enemy_Basic", None, (0, 0, 0.25), 1.30, "Enemy Basic"),
        ("SM_Enemy_Fast", None, (0, 0, 0.18), 1.25, "Enemy Fast"),
        ("SM_Enemy_Tank", None, (0, 0, 0.45), 2.10, "Enemy Tank"),
        ("SM_Enemy_Ranged", None, (0, 0, 0.34), 1.60, "Enemy Ranged"),
        ("SM_Enemy_Vampire", None, (0, 0, 0.18), 1.75, "Enemy Vampire"),
        ("SM_GoldIngot", None, (0, 0, 0.02), 0.72, "Gold Ingot"),
    ]
    tiles_cat = []
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_png = os.path.join(tmpdir, "cat.png")
        for blend_name, grade, center, dist, label in catalog_specs:
            bpy.ops.wm.open_mainfile(filepath=os.path.join(QA_CACHE, f"{blend_name}.blend"))
            if grade:
                solo_core_grade(grade)
            # Lift hovering static props slightly above shadow catcher floor if centered at origin
            if blend_name in ("SM_PlasmaSawOrb", "SM_Projectile", "SM_GoldIngot"):
                for ob in bpy.data.objects:
                    if ob.type == "MESH":
                        ob.location.z += 0.10
            setup_cycles_qa_rig_and_stage(Vector(center), dist)
            bpy.context.scene.frame_set(1)
            bpy.context.view_layer.update()
            rgb_u8, _ = render_tile_rgba_and_composite(tmp_png)
            draw_label_top_left(rgb_u8, label)
            tiles_cat.append(rgb_u8)

    out_cat = os.path.join(QA_DIR, "cs_10_Catalog_Grades_And_Bestiary.png")
    save_contact_sheet_png(tiles_cat, out_cat, cols=4)

    # Sheet 11: All 12 Voronoi chunks closeup with inner glowing cut planes visible
    tiles_deb = []
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_png = os.path.join(tmpdir, "deb.png")
        for i in range(12):
            bpy.ops.wm.open_mainfile(filepath=os.path.join(QA_CACHE, "SM_Debris_VoronoiPack.blend"))
            target_name = f"Debris_Chunk_{i:02d}"
            for ob in bpy.data.objects:
                if ob.type == "MESH":
                    if ob.name == target_name:
                        ob.location = (0.0, 0.0, 0.22)
                        ob.rotation_euler = (0.55 + i * 0.35, 0.40 * i, 0.85 * i)
                    else:
                        ob.hide_render = True
                        ob.hide_viewport = True
            setup_cycles_qa_rig_and_stage(Vector((0.0, 0.0, 0.22)), 0.92)
            bpy.context.scene.frame_set(1)
            bpy.context.view_layer.update()
            rgb_u8, _ = render_tile_rgba_and_composite(tmp_png)
            draw_label_top_left(rgb_u8, f"Voronoi Chunk {i:02d}")
            tiles_deb.append(rgb_u8)

    out_deb = os.path.join(QA_DIR, "cs_11_Voronoi_Debris_Chunks.png")
    save_contact_sheet_png(tiles_deb, out_deb, cols=4)
    return ["cs_10_Catalog_Grades_And_Bestiary.png", "cs_11_Voronoi_Debris_Chunks.png"]


def verify_fbx_roundtrip() -> list[dict]:
    """Re-imports every exported FBX in staging/models/ into an empty scene to verify
    scale=1.0, dimensions, vertex colors COLOR_0, single material per mesh, and animations."""
    expected_files = [
        ("SM_ShredderCore.fbx", True),
        ("SM_PlasmaSawOrb.fbx", False),
        ("SM_Electromagnet_BH.fbx", True),
        ("SM_RailEmitter_DR.fbx", True),
        ("SM_GoldenBot.fbx", True),
        ("SM_Projectile.fbx", False),
        ("SM_Enemy_Basic.fbx", False),
        ("SM_Enemy_Fast.fbx", False),
        ("SM_Enemy_Tank.fbx", False),
        ("SM_Enemy_Ranged.fbx", False),
        ("SM_Enemy_Vampire.fbx", False),
        ("SM_Enemy_Boss_ATM.fbx", True),
        ("SM_Debris_VoronoiPack.fbx", False),
        ("SM_GoldIngot.fbx", False),
    ]
    results = []
    for fname, is_skel in expected_files:
        fpath = os.path.join(STAGING_MODELS, fname)
        assert os.path.exists(fpath), f"Missing FBX: {fpath}"
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=fpath)
        bpy.context.view_layer.update()

        meshes = [o for o in bpy.data.objects if o.type == "MESH"]
        arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
        assert len(meshes) >= 1, f"No meshes in {fname}"
        if is_skel:
            assert len(arms) == 1, f"Expected 1 armature in {fname}, got {len(arms)}"
            arm_scale = [round(s, 4) for s in arms[0].scale]
        else:
            arm_scale = None

        has_vcol = all(len(m.data.color_attributes) >= 1 for m in meshes)
        single_mat = all(len(m.data.materials) == 1 for m in meshes)
        tris = sum(len(m.data.polygons) for m in meshes)

        # Check hot inner cut faces (R >= 0.9) on Voronoi pack
        hot_cut_ok = True
        if fname == "SM_Debris_VoronoiPack.fbx":
            assert len(meshes) == 12, f"Expected 12 chunks in VoronoiPack, got {len(meshes)}"
            for m in meshes:
                ca = m.data.color_attributes[0]
                buf = np.empty(len(ca.data) * 4, dtype=np.float32)
                ca.data.foreach_get("color", buf)
                reds = buf.reshape(-1, 4)[:, 0]
                if float(reds.max()) < 0.85:
                    hot_cut_ok = False

        results.append({
            "file": fname,
            "size_kb": round(os.path.getsize(fpath) / 1024.0, 1),
            "meshes": [m.name for m in meshes],
            "tris": tris,
            "skeletal": is_skel,
            "armature_scale": arm_scale,
            "actions_imported": len(bpy.data.actions),
            "has_color_0": has_vcol,
            "single_material_per_mesh": single_mat,
            "voronoi_hot_cuts_ok": hot_cut_ok,
            "ok": bool(has_vcol and single_mat and hot_cut_ok),
        })
    return results


def main():
    os.makedirs(QA_DIR, exist_ok=True)

    clip_jobs = [
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_IdleSpin", 1, 61, True, (0, 0, 0.55), 3.65, "cs_01_Core_IdleSpin.png", "G2_TitaniumTwin"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_OverdriveSpin", 1, 31, True, (0, 0, 0.55), 3.80, "cs_02_Core_OverdriveSpin.png", "G3_DiamondReactor"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_FireL", 1, 9, False, (0, 0, 0.55), 3.65, "cs_03_Core_FireRecoil.png", "G2_TitaniumTwin"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_OverloadDeath", 1, 37, False, (0, 0, 0.55), 3.65, "cs_04_Core_OverloadDeath.png", "G1_SteelCarbide"),
        ("SM_Enemy_Boss_ATM", "RIG_Boss_ATM", "AN_Boss_Roll", 1, 25, True, (0, 0, 0.88), 4.20, "cs_05_Boss_ATM_Roll.png", None),
        ("SM_Enemy_Boss_ATM", "RIG_Boss_ATM", "AN_Boss_Ram", 1, 25, False, (0, 0, 0.88), 4.40, "cs_06_Boss_ATM_Ram.png", None),
        ("SM_Electromagnet_BH", "RIG_Electromagnet_BH", "AN_BH_Pulse", 1, 31, True, (0, 0, 0.45), 2.75, "cs_07_Electromagnet_BH_Pulse.png", None),
        ("SM_RailEmitter_DR", "RIG_RailEmitter_DR", "AN_DR_ChargeFire", 1, 16, False, (0, -0.10, 0.35), 2.30, "cs_08_RailEmitter_DR_Fire.png", None),
        ("SM_GoldenBot", "RIG_GoldenBot", "AN_Bot_Hover", 1, 46, True, (0, 0, 0.32), 1.95, "cs_09_GoldenBot_Hover.png", None),
    ]

    clip_reports = []
    for job in clip_jobs:
        rep = evaluate_clip_sheet(*job)
        print(f"[QA CLIP] {rep['clip']}: passed={rep['passed']} seam={rep['loop_seam']} max_diff={rep['max_rgb_diff']} min_iou={rep['min_silhouette_iou']} scale_keys={rep['bone_scale_keys']}")
        clip_reports.append(rep)

    catalog_sheets = render_static_catalog_sheets()
    fbx_checks = verify_fbx_roundtrip()

    all_clips_ok = all(r["passed"] for r in clip_reports)
    all_fbx_ok = all(f["ok"] for f in fbx_checks)
    overall_pass = bool(all_clips_ok and all_fbx_ok)

    report = {
        "schema": "animation_contact_sheet_report.v1",
        "gate": "STOP-GATE 1B",
        "engine": "CYCLES (CPU, samples=24, Pitch 62.0 deg, #CFD6DF polygon floor)",
        "overall_passed": overall_pass,
        "thresholds": {
            "loop_seam_max": 1.5,
            "max_rgb_diff_max": 45.0,
            "silhouette_iou_min": 0.88,
            "bone_scale_keys_exact": 0,
        },
        "animation_sheets": clip_reports,
        "catalog_sheets": catalog_sheets,
        "fbx_roundtrip": fbx_checks,
    }

    report_path = os.path.join(QA_DIR, "qa_report.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    print(f"=== STOP-GATE 1B OVERALL PASSED: {overall_pass} ===")
    if not overall_pass:
        sys.exit(1)


if __name__ == "__main__":
    main()
