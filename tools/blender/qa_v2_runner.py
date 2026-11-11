"""Crusher Core V2 Ultra — Cycles Showcase Renderer (build/qa/v2_models_showcase.png, Pitch 62 deg),
Animation Quality Gate Runner (v2_cs_01..09.png), and 15-FBX Contract & Round-Trip Verifier."""
from __future__ import annotations

import json
import math
import os
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qa_gate_runner as q1  # noqa: E402

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING_V2 = os.path.join(ROOT_DIR, "staging", "models_v2")
QA_DIR = os.path.join(ROOT_DIR, "build", "qa")
QA_CACHE_V2 = os.path.join(QA_DIR, "blend_cache_v2")


def render_v2_showcase_sheet() -> str:
    """Renders 4x4 (1600x1600, 400x400 tiles) Cycles showcase at Pitch 62 deg of all V2 Ultra assets."""
    tile_sz = 400
    floor_bg = q1.make_polygon_floor_bg(tile_sz, tile_sz)

    showcase_items = [
        # (mode, blend_a, extra, target_center, cam_dist, azim_deg, label)
        ("arena_wide", "SM_ArenaFoundryEnv", "SM_ShredderCore", (0, 0, 0.8), 31.0, 24.0, "01 FOUNDRY HALL WIDE"),
        ("arena_pit", "SM_ArenaFoundryEnv", "SM_ShredderCore", (0, 0, 0.25), 5.6, 20.0, "02 SHREDDER PIT Z<=15MM"),
        ("core_grade", "SM_ShredderCore", "G0_Rusty", (0, 0, 0.56), 3.65, 18.0, "03 CORE G0 RUSTY V2"),
        ("core_grade", "SM_ShredderCore", "G1_SteelCarbide", (0, 0, 0.56), 3.65, 18.0, "04 CORE G1 CARBIDE V2"),
        ("core_grade", "SM_ShredderCore", "G2_TitaniumTwin", (0, 0, 0.56), 3.70, 18.0, "05 CORE G2 TITAN V2"),
        ("core_grade", "SM_ShredderCore", "G3_DiamondReactor", (0, 0, 0.56), 3.85, 18.0, "06 CORE G3 DIAMOND V2"),
        ("single", "SM_Enemy_Basic", None, (0, 0, 0.33), 1.62, 24.0, "07 ENEMY BASIC 0.70M"),
        ("single", "SM_Enemy_Fast", None, (0, 0, 0.34), 1.62, 28.0, "08 ENEMY FAST 0.74M"),
        ("single", "SM_Enemy_Tank", None, (0, 0, 0.56), 2.50, 26.0, "09 ENEMY TANK 1.15M"),
        ("single", "SM_Enemy_Ranged", None, (0, 0, 0.42), 2.05, 24.0, "10 ENEMY RANGED 0.88M"),
        ("single", "SM_Enemy_Vampire", None, (0, 0, 0.20), 2.05, 24.0, "11 ENEMY VAMPIRE 0.96M"),
        ("single", "SM_Enemy_Boss_ATM", None, (0, 0, 1.02), 4.65, 34.0, "12 BOSS ATM 2.10M"),
        ("single", "SM_PlasmaSawOrb", "lift", (0, 0, 0.12), 2.05, 18.0, "13 PLASMA SAW ORB V2"),
        ("single", "SM_Electromagnet_BH", None, (0, 0, 0.48), 2.75, 22.0, "14 ELECTROMAGNET BH V2"),
        ("single", "SM_RailEmitter_DR", None, (0, -0.12, 0.35), 2.35, 24.0, "15 RAIL EMITTER DR V2"),
        ("pair_bot_ingot", "SM_GoldenBot", "SM_GoldIngot", (0, 0, 0.30), 2.25, 20.0, "16 GOLDEN BOT + INGOT"),
    ]

    tiles: list[np.ndarray] = []
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_png = os.path.join(tmpdir, "v2_tile.png")
        for mode, b_main, extra, center, dist, azim_deg, label in showcase_items:
            bpy.ops.wm.open_mainfile(filepath=os.path.join(QA_CACHE_V2, f"{b_main}.blend"))

            if mode in ("arena_wide", "arena_pit"):
                # Append SM_ShredderCore (G3) into center of SM_ArenaFoundryEnv for authentic in-situ preview!
                core_blend = os.path.join(QA_CACHE_V2, f"{extra}.blend")
                with bpy.data.libraries.load(core_blend, link=False) as (data_from, data_to):
                    data_to.objects = [n for n in data_from.objects if n in ("Core_Chassis", "G3_DiamondReactor")]
                for ob_app in data_to.objects:
                    if ob_app is not None:
                        bpy.context.scene.collection.objects.link(ob_app)
            elif mode == "core_grade":
                q1.solo_core_grade(extra)
            elif mode == "single" and extra == "lift":
                for ob in bpy.data.objects:
                    if ob.type == "MESH":
                        ob.location.z += 0.12
            elif mode == "pair_bot_ingot":
                ing_blend = os.path.join(QA_CACHE_V2, f"{extra}.blend")
                with bpy.data.libraries.load(ing_blend, link=False) as (data_from, data_to):
                    data_to.objects = [n for n in data_from.objects if n == "SM_GoldIngot"]
                for ob_app in data_to.objects:
                    if ob_app is not None:
                        bpy.context.scene.collection.objects.link(ob_app)
                        ob_app.location = (0.44, -0.34, 0.08)
                        ob_app.rotation_euler = (0.0, 0.0, 0.45)

            sc, floor_ob = q1.setup_cycles_qa_rig_and_stage(Vector(center), dist)
            if mode == "arena_wide":
                floor_ob.scale = (3.0, 3.0, 1.0)
            sc.cycles.samples = 28
            sc.render.resolution_x = tile_sz
            sc.render.resolution_y = tile_sz

            # Re-aim camera at exact Pitch 62.0 deg with custom azimuth so side/gold details show clearly
            pitch_rad = math.radians(62.0)
            azim_rad = math.radians(azim_deg)
            t_vec = Vector(center)
            horiz = dist * math.cos(pitch_rad)
            vert = dist * math.sin(pitch_rad)
            cam_pos = t_vec + Vector((horiz * math.sin(azim_rad), -horiz * math.cos(azim_rad), vert))
            sc.camera.location = cam_pos
            sc.camera.rotation_euler = (t_vec - cam_pos).normalized().to_track_quat("-Z", "Y").to_euler()
            sc.camera.data.clip_end = 120.0

            sc.frame_set(1)
            bpy.context.view_layer.update()
            sc.render.filepath = tmp_png
            bpy.ops.render.render(write_still=True)

            img = bpy.data.images.load(tmp_png, check_existing=False)
            w, h = img.size
            px = np.empty(w * h * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            bpy.data.images.remove(img)

            rgba = px.reshape(h, w, 4)[::-1, :, :]
            rgb_lin = np.clip(rgba[..., :3], 0.0, 1.0)
            alpha = np.clip(rgba[..., 3], 0.0, 1.0)
            rgb_srgb = np.where(
                rgb_lin <= 0.0031308,
                rgb_lin * 12.92,
                1.055 * np.power(rgb_lin, 1.0 / 2.4) - 0.055,
            ) * 255.0
            comp = np.clip(np.rint(rgb_srgb * alpha[..., None] + floor_bg * (1.0 - alpha[..., None])), 0, 255).astype(np.uint8)
            q1.draw_label_top_left(comp, label)
            tiles.append(comp)

    # Save 4x4 1600x1600 PNG
    cols = 4
    rows = 4
    sheet_h = rows * tile_sz
    sheet_w = cols * tile_sz
    sheet = np.zeros((sheet_h, sheet_w, 4), dtype=np.float32)
    sheet[..., 3] = 1.0
    for idx, tile in enumerate(tiles):
        r, c = idx // cols, idx % cols
        srgb = tile.astype(np.float32) / 255.0
        lin = np.where(srgb <= 0.04045, srgb / 12.92, np.power((srgb + 0.055) / 1.055, 2.4))
        sheet[r * tile_sz:(r + 1) * tile_sz, c * tile_sz:(c + 1) * tile_sz, :3] = lin

    out_path = os.path.join(QA_DIR, "v2_models_showcase.png")
    img_out = bpy.data.images.new("V2_Showcase_Out", width=sheet_w, height=sheet_h, alpha=True)
    img_out.pixels.foreach_set(sheet[::-1, :, :].ravel())
    img_out.filepath_raw = os.path.abspath(out_path)
    img_out.file_format = "PNG"
    img_out.save()
    bpy.data.images.remove(img_out)
    return out_path


def verify_v2_fbx_roundtrip() -> list[dict]:
    """Re-imports all 15 V2 FBX files in staging/models_v2/ and verifies strict naming, bones, scale,
    COLOR_0, 16-bit vertex budget (<65k), single material, Voronoi hot cuts, and pit z<=0.015m."""
    expected_specs = [
        ("SM_ArenaFoundryEnv.fbx", False, ["ArenaFoundryEnv_Mesh"], None),
        ("SM_ShredderCore.fbx", True, ["Core_Chassis", "G0_Rusty", "G1_SteelCarbide", "G2_TitaniumTwin", "G3_DiamondReactor"],
         ["root", "bn_base", "bn_wall_ring", "bn_drum_lower", "bn_drum_upper", "bn_turret_yaw", "bn_barrel_L", "bn_barrel_R", "bn_core_vent"]),
        ("SM_PlasmaSawOrb.fbx", False, ["SM_PlasmaSawOrb"], None),
        ("SM_Electromagnet_BH.fbx", True, ["Electromagnet_Mesh"], ["root", "bn_yoke", "bn_flux_ring"]),
        ("SM_RailEmitter_DR.fbx", True, ["RailEmitter_Mesh"], ["root", "bn_rail_L", "bn_rail_R"]),
        ("SM_GoldenBot.fbx", True, ["GoldenBot_Mesh"], ["root", "bn_body", "bn_rotor"]),
        ("SM_Projectile.fbx", False, ["SM_Projectile"], None),
        ("SM_Enemy_Basic.fbx", False, ["SM_Enemy_Basic"], None),
        ("SM_Enemy_Fast.fbx", False, ["SM_Enemy_Fast"], None),
        ("SM_Enemy_Tank.fbx", False, ["SM_Enemy_Tank"], None),
        ("SM_Enemy_Ranged.fbx", False, ["SM_Enemy_Ranged"], None),
        ("SM_Enemy_Vampire.fbx", False, ["SM_Enemy_Vampire"], None),
        ("SM_Enemy_Boss_ATM.fbx", True, ["Boss_ATM_Mesh"], ["root", "bn_hull", "bn_rollers", "bn_jaw"]),
        ("SM_Debris_VoronoiPack.fbx", False, [f"Debris_Chunk_{i:02d}" for i in range(12)], None),
        ("SM_GoldIngot.fbx", False, ["SM_GoldIngot"], None),
    ]

    results = []
    for fname, is_skel, exp_meshes, exp_bones in expected_specs:
        fpath = os.path.join(STAGING_V2, fname)
        assert os.path.exists(fpath), f"Missing V2 FBX: {fpath}"
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=fpath)
        bpy.context.view_layer.update()

        meshes = sorted([o for o in bpy.data.objects if o.type == "MESH"], key=lambda x: x.name)
        arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
        mesh_names = [m.name for m in meshes]
        assert mesh_names == sorted(exp_meshes), f"Mesh mismatch in {fname}: {mesh_names} vs {exp_meshes}"

        if is_skel:
            assert len(arms) == 1, f"Expected 1 armature in {fname}, got {len(arms)}"
            arm_scale = [round(s, 4) for s in arms[0].scale]
            assert arm_scale == [1.0, 1.0, 1.0], f"Bad armature scale in {fname}: {arm_scale}"
            got_bones = sorted([b.name for b in arms[0].data.bones])
            assert got_bones == sorted(exp_bones), f"Bone mismatch in {fname}: {got_bones} vs {exp_bones}"
        else:
            arm_scale = None

        has_vcol = all(len(m.data.color_attributes) >= 1 for m in meshes)
        single_mat = all(len(m.data.materials) == 1 for m in meshes)
        tris = sum(len(m.data.polygons) for m in meshes)
        max_loop_corners = max(len(m.data.loops) for m in meshes)
        under_16bit = max_loop_corners < 65000

        extra_ok = True
        if fname == "SM_Debris_VoronoiPack.fbx":
            for m in meshes:
                ca = m.data.color_attributes[0]
                buf = np.empty(len(ca.data) * 4, dtype=np.float32)
                ca.data.foreach_get("color", buf)
                if float(buf.reshape(-1, 4)[:, 0].max()) < 0.85:
                    extra_ok = False
        elif fname == "SM_ArenaFoundryEnv.fbx":
            # Verify central pit world Z after FBX import transform
            ob_env = meshes[0]
            mw = ob_env.matrix_world
            world_pts = [mw @ v.co for v in ob_env.data.vertices]
            pit_zs = [p.z for p in world_pts if math.hypot(p.x, p.y) < 8.80]
            if not pit_zs or max(pit_zs) > 0.0155 or min(pit_zs) < 0.0015:
                extra_ok = False

        ok = bool(has_vcol and single_mat and under_16bit and extra_ok)
        results.append({
            "file": fname,
            "size_kb": round(os.path.getsize(fpath) / 1024.0, 1),
            "meshes": mesh_names,
            "tris": tris,
            "max_mesh_loops": max_loop_corners,
            "skeletal": is_skel,
            "armature_scale": arm_scale,
            "actions_imported": len(bpy.data.actions),
            "has_color_0": has_vcol,
            "single_material_per_mesh": single_mat,
            "under_16bit_index_limit": under_16bit,
            "ok": ok,
        })
    return results


def main():
    os.makedirs(QA_DIR, exist_ok=True)
    # Point qa_gate_runner cache to V2 cache so evaluate_clip_sheet tests V2 rigs!
    q1.QA_CACHE = QA_CACHE_V2

    print("[V2 QA] Rendering build/qa/v2_models_showcase.png (1600x1600 Cycles Pitch 62 deg)...")
    showcase_path = render_v2_showcase_sheet()
    print(f"[V2 QA] Showcase saved -> {showcase_path}")

    clip_jobs = [
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_IdleSpin", 1, 61, True, (0, 0, 0.56), 3.70, "v2_cs_01_Core_IdleSpin.png", "G2_TitaniumTwin"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_OverdriveSpin", 1, 31, True, (0, 0, 0.56), 3.85, "v2_cs_02_Core_OverdriveSpin.png", "G3_DiamondReactor"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_FireL", 1, 9, False, (0, 0, 0.56), 3.70, "v2_cs_03_Core_FireRecoil.png", "G2_TitaniumTwin"),
        ("SM_ShredderCore", "RIG_ShredderCore", "AN_Core_OverloadDeath", 1, 37, False, (0, 0, 0.56), 3.70, "v2_cs_04_Core_OverloadDeath.png", "G1_SteelCarbide"),
        ("SM_Enemy_Boss_ATM", "RIG_Boss_ATM", "AN_Boss_Roll", 1, 25, True, (0, 0, 1.02), 4.65, "v2_cs_05_Boss_ATM_Roll.png", None),
        ("SM_Enemy_Boss_ATM", "RIG_Boss_ATM", "AN_Boss_Ram", 1, 25, False, (0, 0, 1.02), 4.80, "v2_cs_06_Boss_ATM_Ram.png", None),
        ("SM_Electromagnet_BH", "RIG_Electromagnet_BH", "AN_BH_Pulse", 1, 31, True, (0, 0, 0.48), 2.80, "v2_cs_07_Electromagnet_BH_Pulse.png", None),
        ("SM_RailEmitter_DR", "RIG_RailEmitter_DR", "AN_DR_ChargeFire", 1, 16, False, (0, -0.10, 0.35), 2.35, "v2_cs_08_RailEmitter_DR_Fire.png", None),
        ("SM_GoldenBot", "RIG_GoldenBot", "AN_Bot_Hover", 1, 46, True, (0, 0, 0.32), 2.00, "v2_cs_09_GoldenBot_Hover.png", None),
    ]

    clip_reports = []
    for job in clip_jobs:
        rep = q1.evaluate_clip_sheet(*job)
        print(
            f"[V2 QA CLIP] {rep['clip']}: passed={rep['passed']} seam={rep['loop_seam']} "
            f"max_diff={rep['max_rgb_diff']} min_iou={rep['min_silhouette_iou']} scale_keys={rep['bone_scale_keys']}"
        )
        clip_reports.append(rep)

    fbx_checks = verify_v2_fbx_roundtrip()
    all_clips_ok = all(r["passed"] for r in clip_reports)
    all_fbx_ok = all(f["ok"] for f in fbx_checks)
    overall_pass = bool(all_clips_ok and all_fbx_ok)

    report = {
        "schema": "animation_contact_sheet_report.v2_ultra",
        "gate": "WAVE A2 V2 ULTRA",
        "engine": "CYCLES (CPU, Pitch 62.0 deg, #CFD6DF polygon floor)",
        "showcase_png": "build/qa/v2_models_showcase.png",
        "overall_passed": overall_pass,
        "thresholds": {
            "loop_seam_max": 1.5,
            "max_rgb_diff_max": 45.0,
            "silhouette_iou_min": 0.88,
            "bone_scale_keys_exact": 0,
        },
        "animation_sheets": clip_reports,
        "fbx_roundtrip": fbx_checks,
    }

    report_path = os.path.join(QA_DIR, "qa_report_v2.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    print(f"=== V2 ULTRA QA OVERALL PASSED: {overall_pass} ===")
    if not overall_pass:
        sys.exit(1)


if __name__ == "__main__":
    main()
