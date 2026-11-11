"""Crusher Core V2 Ultra — Procedural Generator for:
1) SM_ArenaFoundryEnv.fbx (Central recessed shredder pit r=1.22..2.55m z<=0.015m + Outer Foundry Hall r=9.0..15.8m)
2) SM_ShredderCore.fbx (~22k tris across Core_Chassis + G0..G3, exact 9 bones & 6 clips)
3) SM_PlasmaSawOrb.fbx, SM_Electromagnet_BH.fbx, SM_RailEmitter_DR.fbx, SM_GoldenBot.fbx, SM_Projectile.fbx
Outputs to staging/models_v2/ and build/qa/blend_cache_v2/ without touching Assets/."""
from __future__ import annotations

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mesh_rig_utils as mu  # noqa: E402

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING_V2 = os.path.join(ROOT_DIR, "staging", "models_v2")
QA_CACHE_V2 = os.path.join(ROOT_DIR, "build", "qa", "blend_cache_v2")


def save_blend_cache_v2(name: str) -> str:
    os.makedirs(QA_CACHE_V2, exist_ok=True)
    path = os.path.join(QA_CACHE_V2, f"{name}.blend")
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    return path


# ---------------------------------------------------------------------------
# 1. NEW SM_ArenaFoundryEnv.fbx (Static, 1 mesh ArenaFoundryEnv_Mesh, ~11k-13.5k tris)
# ---------------------------------------------------------------------------
def build_arena_foundry_env() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # === A. CENTRAL RECESSED SHREDDER PIT (r = 1.22m .. 2.55m, strictly 0.002 <= z <= 0.015m!) ===
    # 1) Sub-grate Molten Smelter Ring (z = 0.0022 .. 0.0052m -> above Unity Y=0 floor quad, below grates!)
    mu.add_tube_ring(
        bm, r_outer=2.28, r_inner=1.22, height=0.0030, segs=24,
        center=(0.0, 0.0, 0.0037), chamfer=0.0, zone=1.0, part_hash=0.96,
    )
    # 12 glowing sub-grate inductor heat ribs
    for i in range(12):
        a = i * (2.0 * math.pi / 12.0) + math.pi / 24.0
        rm = 1.74
        mu.add_box(
            bm, size=(0.08, 0.96, 0.0024),
            center=(math.cos(a) * rm, math.sin(a) * rm, 0.0056),
            rot_euler=(0.0, 0.0, a + math.pi * 0.5), bevel=0.0, zone=1.0, part_hash=0.92,
        )

    # 2) Concentric Steel Pit Grate Rings (z_top = 0.0120m)
    for r_g in (1.28, 1.64, 2.02):
        mu.add_tube_ring(
            bm, r_outer=r_g + 0.048, r_inner=r_g - 0.048, height=0.0064, segs=24,
            center=(0.0, 0.0, 0.0088), chamfer=0.0010, zone=0.35, part_hash=0.36,
        )

    # 3) 16 Radial Steel Grate Bars (r = 1.24m .. 2.28m, z_top = 0.0125m)
    for i in range(16):
        a = i * (2.0 * math.pi / 16.0)
        rm = 1.76
        mu.add_box(
            bm, size=(0.055, 1.04, 0.0068),
            center=(math.cos(a) * rm, math.sin(a) * rm, 0.0091),
            rot_euler=(0.0, 0.0, a + math.pi * 0.5), bevel=0.0,
            zone=0.0 if i % 2 == 0 else 0.35, part_hash=0.25 + 0.02 * (i % 8),
        )

    # 4) Octagonal Armored Pit Curb Collar (r = 2.26m .. 2.54m, z_top = 0.0138m)
    mu.add_tube_ring(
        bm, r_outer=2.54, r_inner=2.26, height=0.0110, segs=8,
        center=(0.0, 0.0, 0.0080), chamfer=0.0020, zone=0.0, part_hash=0.22,
    )
    # 16 flush 3D Hazard Warning Plates + 16 Hex Rivets on Octagonal Pit Rim (z_top <= 0.0148m)
    for i in range(16):
        a = i * (2.0 * math.pi / 16.0)
        rc = 2.39
        mu.add_box(
            bm, size=(0.24, 0.14, 0.0032),
            center=(math.cos(a) * rc, math.sin(a) * rc, 0.0128),
            rot_euler=(0.0, 0.0, a + math.pi * 0.5 + 0.25), bevel=0.0,
            zone=0.70 if i % 2 == 0 else 0.35, part_hash=0.68,
        )
    mu.add_bolt_ring(
        bm, count=16, radius=2.47, z_center=0.0132,
        bolt_r=0.028, bolt_h=0.0030, phase_offset=math.pi / 16.0, zone=0.70,
    )

    # 5) 4 Low-Profile Hydraulic Pit Anchors (45, 135, 225, 315 deg, z_top <= 0.0148m)
    for i in range(4):
        a = math.pi * 0.25 + i * (math.pi * 0.5)
        ca, sa = math.cos(a), math.sin(a)
        # Outer hydraulic cylinder housing
        mu.add_box(
            bm, size=(0.38, 0.56, 0.0108),
            center=(ca * 2.18, sa * 2.18, 0.0092),
            rot_euler=(0.0, 0.0, a + math.pi * 0.5), bevel=0.0015, zone=0.35, part_hash=0.44,
        )
        # Twin brass hydraulic piston rams clamping inward to r = 1.24m
        for lat in (-0.11, 0.11):
            px = ca * 1.62 - sa * lat
            py = sa * 1.62 + ca * lat
            mu.add_box(
                bm, size=(0.055, 0.72, 0.0086),
                center=(px, py, 0.0102),
                rot_euler=(0.0, 0.0, a + math.pi * 0.5), bevel=0.0, zone=0.70, part_hash=0.76,
            )
        # Inner clamping jaw at pit throat
        mu.add_box(
            bm, size=(0.34, 0.18, 0.0110),
            center=(ca * 1.30, sa * 1.30, 0.0093),
            rot_euler=(0.0, 0.0, a + math.pi * 0.5), bevel=0.0012, zone=0.70, part_hash=0.80,
        )

    # === B. OUTER FOUNDRY PERIMETER (r = 9.05m .. 15.75m, outside 7.5m range ring!) ===
    # 1) 6 Massive Scrap-Feed Bunker Airlocks at 0, 60, 120, 180, 240, 300 deg
    for b_idx in range(6):
        a = b_idx * (math.pi / 3.0)
        ca, sa = math.cos(a), math.sin(a)
        rot_in = a + math.pi * 0.5  # Local -Y faces center (0,0,0) when using radial frame

        def rad_pt(r_val: float, tang_val: float, z_val: float):
            return (ca * r_val - sa * tang_val, sa * r_val + ca * tang_val, z_val)

        # Concrete-steel bunker foundation pad (r = 10.2 .. 15.4m)
        mu.add_box(
            bm, size=(4.50, 4.80, 0.24), center=rad_pt(12.85, 0.0, 0.12),
            rot_euler=(0.0, 0.0, rot_in), bevel=0.03, zone=0.0, part_hash=0.18 + 0.05 * b_idx,
        )
        # Left & right thick armored bunker walls
        for side in (-1.0, 1.0):
            mu.add_box(
                bm, size=(0.76, 3.80, 2.55), center=rad_pt(12.90, side * 1.68, 1.38),
                rot_euler=(0.0, 0.0, rot_in), bevel=0.035, zone=0.0, part_hash=0.26,
            )
            # Sloped front armor buttress cheek
            mu.add_box(
                bm, size=(0.56, 1.15, 2.20), center=rad_pt(10.95, side * 1.68, 1.12),
                rot_euler=(0.0, 0.0, rot_in), bevel=0.025, zone=0.35, part_hash=0.40,
            )
            # Portal I-beam upright column
            mu.add_ibeam(
                bm, length=2.68, depth=0.36, flange_w=0.30, web_t=0.06, flange_t=0.06,
                center=rad_pt(10.52, side * 1.56, 1.44), rot_euler=(0.0, 0.0, rot_in),
                bevel=0.0, zone=0.35,
            )

        # Heavy overhead bunker roof & blast-door header
        mu.add_box(
            bm, size=(4.36, 3.90, 0.56), center=rad_pt(12.85, 0.0, 2.74),
            rot_euler=(0.0, 0.0, rot_in), bevel=0.035, zone=0.35, part_hash=0.34,
        )
        # Upper blast door apron + 5 3D Hazard Chevron Warning Stripes (R = 0.70)
        mu.add_box(
            bm, size=(2.60, 0.34, 0.68), center=rad_pt(10.72, 0.0, 2.18),
            rot_euler=(0.0, 0.0, rot_in), bevel=0.02, zone=0.35, part_hash=0.48,
        )
        for st in range(-2, 3):
            mu.add_box(
                bm, size=(0.30, 0.10, 0.52), center=rad_pt(10.52, st * 0.48, 2.16),
                rot_euler=(0.0, 0.28, rot_in), bevel=0.0, zone=0.70, part_hash=0.74,
            )

        # Deep Glowing Smelter Chute Throat inside bunker (R = 1.0!) + scrap ramp
        mu.add_box(
            bm, size=(2.48, 0.24, 1.88), center=rad_pt(13.85, 0.0, 1.15),
            rot_euler=(0.0, 0.0, rot_in), bevel=0.0, zone=1.0, part_hash=0.95,
        )
        mu.add_box(
            bm, size=(2.52, 2.60, 0.14), center=rad_pt(11.80, 0.0, 0.22),
            rot_euler=(0.0, 0.0, rot_in), bevel=0.015, zone=0.35, part_hash=0.32,
        )
        # Twin roof exhaust stacks + glowing gate beacons
        for side in (-1.0, 1.0):
            mu.add_cylinder(
                bm, r1=0.26, r2=0.22, depth=0.95, segs=8,
                center=rad_pt(13.65, side * 1.15, 3.45), bevel=0.0, zone=0.35, part_hash=0.52,
            )
            mu.add_tube_ring(
                bm, r_outer=0.29, r_inner=0.19, height=0.12, segs=8,
                center=rad_pt(13.65, side * 1.15, 3.88), chamfer=0.0, zone=0.70, part_hash=0.78,
            )
            mu.add_cylinder(
                bm, r1=0.14, r2=0.11, depth=0.16, segs=8,
                center=rad_pt(10.55, side * 1.56, 2.88), bevel=0.0, zone=1.0, part_hash=0.92,
            )

    # 2) 6 Inter-Bunker Sectors (at 30, 90, 150, 210, 270, 330 deg):
    #    Curved Blast Barriers (r=9.25..9.62m) + Bundled Power Cables + Rear Structural H-Columns
    for s_idx in range(6):
        a_mid = (s_idx + 0.5) * (math.pi / 3.0)
        cm, sm = math.cos(a_mid), math.sin(a_mid)
        rot_m = a_mid + math.pi * 0.5

        # Twin curved blast bumpers flanking the central cable junction box at r = 9.38m
        for sub_off in (-0.21, 0.21):
            a0 = a_mid + sub_off - 0.12
            a1 = a_mid + sub_off + 0.12
            mu.add_arc_plate(
                bm, r_inner=9.22, r_outer=9.58, height=0.54,
                ang_start=a0, ang_end=a1, segs=2, z_center=0.27,
                bevel=0.02, zone=0.35, part_hash=0.38,
            )
            ac = 0.5 * (a0 + a1)
            mu.add_box(
                bm, size=(0.56, 0.10, 0.20),
                center=(math.cos(ac) * 9.19, math.sin(ac) * 9.19, 0.36),
                rot_euler=(0.0, 0.0, ac + math.pi * 0.5), bevel=0.0, zone=0.70, part_hash=0.72,
            )

        # Heavy Floor Power Junction Terminal at r = 9.30m (outside 7.5m range!)
        mu.add_box(
            bm, size=(0.86, 0.72, 0.44),
            center=(cm * 9.35, sm * 9.35, 0.22),
            rot_euler=(0.0, 0.0, rot_m), bevel=0.02, zone=0.0, part_hash=0.28,
        )
        mu.add_box(
            bm, size=(0.48, 0.12, 0.18),
            center=(cm * 9.02, sm * 9.02, 0.26),
            rot_euler=(0.0, 0.0, rot_m), bevel=0.0, zone=1.0, part_hash=0.94,
        )

        # Main corrugated armored power cable + 2 side conduits running r = 9.55m -> 14.85m
        c_pts_main = [
            (cm * 9.50, sm * 9.50, 0.16),
            (cm * 11.10 - sm * 0.18, sm * 11.10 + cm * 0.18, 0.14),
            (cm * 12.90 + sm * 0.16, sm * 12.90 - cm * 0.16, 0.14),
            (cm * 14.70, sm * 14.70, 0.24),
        ]
        mu.add_corrugated_cable(bm, c_pts_main, radius=0.16, segs=6, collar_every=2, zone=0.35, collar_zone=0.70)
        for lat_c in (-0.34, 0.34):
            c_pts_aux = [
                (cm * 9.55 - sm * lat_c, sm * 9.55 + cm * lat_c, 0.10),
                (cm * 12.10 - sm * (lat_c * 1.15), sm * 12.10 + cm * (lat_c * 1.15), 0.10),
                (cm * 14.75 - sm * lat_c, sm * 14.75 + cm * lat_c, 0.18),
            ]
            mu.add_corrugated_cable(bm, c_pts_aux, radius=0.09, segs=6, collar_every=0, zone=0.0, collar_zone=0.70)

        # Outer HV Transformer Cabinet + Tall Foundry Hall I-Beam Column at r = 15.15m
        mu.add_box(
            bm, size=(1.40, 1.05, 1.65),
            center=(cm * 14.95, sm * 14.95, 0.82),
            rot_euler=(0.0, 0.0, rot_m), bevel=0.025, zone=0.35, part_hash=0.42,
        )
        mu.add_ibeam(
            bm, length=4.50, depth=0.44, flange_w=0.36, web_t=0.07, flange_t=0.07,
            center=(cm * 15.45, sm * 15.45, 2.25), rot_euler=(0.0, 0.0, rot_m),
            bevel=0.0, zone=0.0,
        )
        # Overhead crane cantilever bracket at top of I-beam
        mu.add_box(
            bm, size=(0.34, 1.35, 0.32),
            center=(cm * 14.90, sm * 14.90, 4.25),
            rot_euler=(0.0, 0.0, rot_m), bevel=0.0, zone=0.70, part_hash=0.70,
        )

    # 3) 4 Angled Foundry Floodlight Masts (at 45, 135, 225, 315 deg, r = 11.55m, height ~5.2m)
    for m_idx in range(4):
        am = math.pi * 0.25 + m_idx * (math.pi * 0.5)
        cm, sm = math.cos(am), math.sin(am)
        rot_m = am + math.pi * 0.5

        # Heavy octagonal mast base plinth
        mu.add_cylinder(
            bm, r1=0.68, r2=0.54, depth=0.48, segs=8,
            center=(cm * 11.65, sm * 11.65, 0.24), bevel=0.02, zone=0.0, part_hash=0.22,
        )
        # Compute mast center & top in world coords along radial inward lean
        lean_rad = math.radians(16.0)
        mast_len = 4.60
        mid_r = 11.65 - math.sin(lean_rad) * (mast_len * 0.5)
        mid_z = 0.44 + math.cos(lean_rad) * (mast_len * 0.5)
        top_r = 11.65 - math.sin(lean_rad) * mast_len
        top_z = 0.44 + math.cos(lean_rad) * mast_len

        mast_dir = mu.Vector((-cm * math.sin(lean_rad), -sm * math.sin(lean_rad), math.cos(lean_rad))).normalized()
        mast_rot = mast_dir.to_track_quat("Z", "Y").to_euler()
        mu.add_ibeam(
            bm, length=mast_len, depth=0.34, flange_w=0.28, web_t=0.055, flange_t=0.055,
            center=(cm * mid_r, sm * mid_r, mid_z),
            rot_euler=(mast_rot.x, mast_rot.y, mast_rot.z), bevel=0.0, zone=0.35,
        )

        # Wide Twin-Bank Stadium Floodlight Hood aimed down at (0, 0, 0.5)
        aim_vec = (mu.Vector((0.0, 0.0, 0.5)) - mu.Vector((cm * top_r, sm * top_r, top_z))).normalized()
        hood_rot = aim_vec.to_track_quat("-Y", "Z").to_euler()
        mu.add_box(
            bm, size=(1.92, 0.42, 0.78),
            center=(cm * top_r, sm * top_r, top_z + 0.12),
            rot_euler=(hood_rot.x, hood_rot.y, hood_rot.z), bevel=0.02, zone=0.35, part_hash=0.52,
        )
        mu.add_box(
            bm, size=(2.02, 0.18, 0.86),
            center=(cm * top_r, sm * top_r, top_z + 0.12),
            rot_euler=(hood_rot.x, hood_rot.y, hood_rot.z), bevel=0.0, zone=0.70, part_hash=0.76,
        )
        front_c = mu.Vector((cm * top_r, sm * top_r, top_z + 0.12)) + aim_vec * 0.20
        mu.add_box(
            bm, size=(1.74, 0.08, 0.62),
            center=(front_c.x, front_c.y, front_c.z),
            rot_euler=(hood_rot.x, hood_rot.y, hood_rot.z), bevel=0.0, zone=1.0, part_hash=0.98,
        )

    # Enforce strict height clamp on central pit (r < 8.8m -> 0.0020 <= z <= 0.0150)
    for v in bm.verts:
        r_xy = math.hypot(v.co.x, v.co.y)
        if r_xy < 8.80:
            v.co.z = max(0.0020, min(0.0150, v.co.z))

    mat = mu.make_qa_cycles_material(
        "M_ArenaFoundryEnv",
        base_rgb=(0.18, 0.21, 0.25),
        armor_rgb=(0.12, 0.14, 0.18),
        accent_rgb=(0.94, 0.64, 0.10),
        emissive_rgb=(1.0, 0.44, 0.08),
        emissive_strength=5.5,
        metallic=0.86,
        roughness=0.38,
    )
    ob = mu.finalize_bmesh_to_object("ArenaFoundryEnv_Mesh", bm, mat, ao_max_dist=1.65)
    dims = mu.mesh_true_dims(ob)

    # Double-check central pit vertex height guarantee on finalized mesh
    co = np.empty(len(ob.data.vertices) * 3, dtype=np.float32)
    ob.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    rad = np.hypot(co[:, 0], co[:, 1])
    pit_z = co[rad < 8.80, 2]
    assert pit_z.size > 0 and float(pit_z.max()) <= 0.0151 and float(pit_z.min()) >= 0.0019, (
        f"Pit Z out of bounds: min={pit_z.min()}, max={pit_z.max()}"
    )

    fbx_path = os.path.join(STAGING_V2, "SM_ArenaFoundryEnv.fbx")
    mu.export_unity_fbx(fbx_path, [ob], skeletal=False)
    save_blend_cache_v2("SM_ArenaFoundryEnv")
    return {
        "asset": "SM_ArenaFoundryEnv.fbx",
        "mesh": ob.name,
        "tris_total": len(ob.data.polygons),
        "verts_total": len(ob.data.vertices),
        "dims": dims,
        "pit_z_range": [round(float(pit_z.min()), 5), round(float(pit_z.max()), 5)],
    }


# ---------------------------------------------------------------------------
# 2. UPGRADED SM_ShredderCore.fbx (~20k-24k tris, exact 9 bones, 5 meshes, 6 clips)
# ---------------------------------------------------------------------------
def build_shredder_core_v2() -> dict:
    sc = mu.reset_scene(fps=30)

    # EXACT V1 / #30 / #51 bone hierarchy & axes:
    # 0: root, 1: bn_base, 2: bn_wall_ring, 3: bn_drum_lower, 4: bn_drum_upper,
    # 5: bn_turret_yaw, 6: bn_barrel_L, 7: bn_barrel_R, 8: bn_core_vent
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.20)),
        ("bn_base", "root", (0.0, 0.0, 0.0), (0.0, 0.0, 0.25)),
        ("bn_wall_ring", "bn_base", (0.0, 0.0, 0.12), (0.0, 0.0, 0.42)),
        ("bn_drum_lower", "bn_base", (0.0, 0.0, 0.20), (0.0, 0.0, 0.50)),
        ("bn_drum_upper", "bn_base", (0.0, 0.0, 0.52), (0.0, 0.0, 0.82)),
        ("bn_turret_yaw", "bn_base", (0.0, 0.0, 0.82), (0.0, 0.0, 1.06)),
        ("bn_barrel_L", "bn_turret_yaw", (-0.22, -0.18, 0.94), (-0.22, -0.92, 0.94)),
        ("bn_barrel_R", "bn_turret_yaw", (0.22, -0.18, 0.94), (0.22, -0.92, 0.94)),
        ("bn_core_vent", "bn_turret_yaw", (0.0, 0.0, 1.04), (0.0, 0.0, 1.24)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_ShredderCore", bone_specs, rotation_mode="XYZ")

    # --- 2A. Core_Chassis (~5,500 tris) ---
    bm_ch = bmesh.new()
    # Stepped 16-seg heavy cast-steel foundry pedestal (bn_base = 1)
    mu.add_cylinder(bm_ch, r1=1.08, r2=0.98, depth=0.14, segs=16, center=(0, 0, 0.07),
                    bevel=0.018, zone=0.0, part_hash=0.14, bone_id=1)
    mu.add_tube_ring(bm_ch, r_outer=0.99, r_inner=0.72, height=0.09, segs=16,
                     center=(0, 0, 0.17), chamfer=0.014, zone=0.35, part_hash=0.22, bone_id=1)
    mu.add_bolt_ring(bm_ch, count=16, radius=0.92, z_center=0.215, bolt_r=0.022, bolt_h=0.022, zone=0.70, bone_id=1)

    # 8 Radial Hydraulic Outrigger Stabilizers with piston rams
    for i in range(8):
        a = i * math.pi / 4.0 + math.pi / 8.0
        ca, sa = math.cos(a), math.sin(a)
        mu.add_box(bm_ch, size=(0.22, 0.34, 0.15),
                   center=(ca * 0.98, sa * 0.98, 0.075),
                   rot_euler=(0, 0, a + math.pi * 0.5), bevel=0.014, zone=0.35, part_hash=0.28, bone_id=1)
        mu.add_cylinder(bm_ch, r1=0.042, r2=0.036, depth=0.22, segs=8,
                        center=(ca * 0.88, sa * 0.88, 0.16),
                        rot_euler=(math.radians(52.0) * sa, -math.radians(52.0) * ca, 0.0),
                        bevel=0.005, zone=0.70, part_hash=0.72, bone_id=1)

    # Internal Incandescent Reactor Core + 8 Copper Busbars + 3 Louver Collars (visible between drums!)
    mu.add_cylinder(bm_ch, r1=0.63, r2=0.63, depth=0.72, segs=24, center=(0, 0, 0.52),
                    bevel=0.01, zone=1.0, part_hash=0.94, bone_id=1)
    for k in range(8):
        ak = k * (math.pi * 0.25)
        mu.add_box(bm_ch, size=(0.055, 0.065, 0.68),
                   center=(math.cos(ak) * 0.65, math.sin(ak) * 0.65, 0.52),
                   rot_euler=(0, 0, ak + math.pi * 0.5), bevel=0.006, zone=0.70, part_hash=0.76, bone_id=1)
    for z_col in (0.20, 0.50, 0.81):
        mu.add_tube_ring(bm_ch, r_outer=0.86, r_inner=0.61, height=0.048, segs=24,
                         center=(0, 0, z_col), chamfer=0.010, zone=0.35, part_hash=0.40, bone_id=1)

    # Multi-plated CNC Twin-Cannon Turret Housing (bn_turret_yaw = 5)
    mu.add_cylinder(bm_ch, r1=0.66, r2=0.58, depth=0.18, segs=16, center=(0, 0, 0.90),
                    bevel=0.018, zone=0.0, part_hash=0.48, bone_id=5)
    mu.add_box(bm_ch, size=(0.72, 0.64, 0.24), center=(0, -0.05, 0.96),
               bevel=0.022, zone=0.35, part_hash=0.52, bone_id=5)
    # Sloped turret cheek shields + side shell-ejection ports + rear dual ammo drums
    for sign in (-1.0, 1.0):
        mu.add_box(bm_ch, size=(0.14, 0.54, 0.20), center=(sign * 0.39, -0.06, 0.95),
                   rot_euler=(0, sign * 0.18, 0), bevel=0.014, zone=0.0, part_hash=0.56, bone_id=5)
        # Recessed glowing ejection port frame
        mu.add_box(bm_ch, size=(0.05, 0.18, 0.09), center=(sign * 0.45, -0.02, 0.96),
                   bevel=0.005, zone=0.70, part_hash=0.78, bone_id=5)
        # Rear rotary ammo drum canister
        mu.add_cylinder(bm_ch, r1=0.14, r2=0.14, depth=0.24, segs=12,
                        center=(sign * 0.20, 0.34, 0.96), rot_euler=(0, math.pi * 0.5, 0),
                        bevel=0.010, zone=0.35, part_hash=0.60, bone_id=5)
        mu.add_tube_ring(bm_ch, r_outer=0.155, r_inner=0.12, height=0.04, segs=12,
                         center=(sign * 0.20, 0.34, 0.96), rot_euler=(0, math.pi * 0.5, 0),
                         chamfer=0.006, zone=0.70, part_hash=0.74, bone_id=5)
    # Top optical rangefinder pod (R = 1.0 lens facing -Y)
    mu.add_box(bm_ch, size=(0.24, 0.22, 0.10), center=(0, -0.22, 1.09),
               bevel=0.010, zone=0.35, part_hash=0.64, bone_id=5)
    mu.add_box(bm_ch, size=(0.16, 0.04, 0.05), center=(0, -0.33, 1.09),
               bevel=0.004, zone=1.0, part_hash=0.96, bone_id=5)

    # Twin Perforated Pneumatic-Plasma Barrels + Hydraulic Recoil Dampers + Muzzle Brakes (6, 7)
    for sign, bid in ((-1.0, 6), (1.0, 7)):
        bx = sign * 0.22
        # Inner hot plasma core barrel (peeks between cooling jacket rings!)
        mu.add_cylinder(bm_ch, r1=0.058, r2=0.054, depth=0.74, segs=12,
                        center=(bx, -0.58, 0.95), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.005, zone=1.0, part_hash=0.92, bone_id=bid)
        # 5 Perforated cooling jacket collar segments + 2 longitudinal stiffening rails
        for k in range(5):
            yk = -0.28 - k * 0.135
            mu.add_tube_ring(bm_ch, r_outer=0.094, r_inner=0.062, height=0.082, segs=12,
                             center=(bx, yk, 0.95), rot_euler=(math.pi * 0.5, 0, 0),
                             chamfer=0.008, zone=0.0, part_hash=0.58 + 0.03 * k, bone_id=bid)
        for sz in (-1.0, 1.0):
            mu.add_box(bm_ch, size=(0.036, 0.62, 0.028),
                       center=(bx, -0.56, 0.95 + sz * 0.086),
                       bevel=0.004, zone=0.35, part_hash=0.62, bone_id=bid)
        # Under-barrel hydraulic recoil piston
        mu.add_cylinder(bm_ch, r1=0.036, r2=0.036, depth=0.42, segs=8,
                        center=(bx, -0.44, 0.855), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.004, zone=0.70, part_hash=0.75, bone_id=bid)
        # Multi-baffle heavy muzzle brake + glowing plasma bore
        mu.add_cylinder(bm_ch, r1=0.118, r2=0.104, depth=0.18, segs=10,
                        center=(bx, -0.95, 0.95), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.010, zone=0.70, part_hash=0.80, bone_id=bid)
        for bf in (-0.91, -0.99):
            mu.add_tube_ring(bm_ch, r_outer=0.130, r_inner=0.085, height=0.032, segs=10,
                             center=(bx, bf, 0.95), rot_euler=(math.pi * 0.5, 0, 0),
                             chamfer=0.005, zone=0.35, part_hash=0.68, bone_id=bid)
        mu.add_cylinder(bm_ch, r1=0.060, r2=0.060, depth=0.04, segs=10,
                        center=(bx, -1.035, 0.95), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.003, zone=1.0, part_hash=0.98, bone_id=bid)

    # Top Reactor Exhaust Louver Cap (bn_core_vent = 8)
    mu.add_tube_ring(bm_ch, r_outer=0.44, r_inner=0.19, height=0.10, segs=16,
                     center=(0, 0, 1.11), chamfer=0.014, zone=0.35, part_hash=0.74, bone_id=8)
    mu.add_bolt_ring(bm_ch, count=8, radius=0.34, z_center=1.165, bolt_r=0.018, bolt_h=0.018, zone=0.70, bone_id=8)
    for v_i in range(8):
        av = v_i * (math.pi * 0.25)
        mu.add_box(bm_ch, size=(0.035, 0.16, 0.045),
                   center=(math.cos(av) * 0.28, math.sin(av) * 0.28, 1.15),
                   rot_euler=(0.30 * math.cos(av), 0.30 * math.sin(av), av + math.pi * 0.5),
                   bevel=0.004, zone=0.70, part_hash=0.77, bone_id=8)
    mu.add_cylinder(bm_ch, r1=0.20, r2=0.15, depth=0.09, segs=16,
                    center=(0, 0, 1.12), bevel=0.012, zone=1.0, part_hash=0.99, bone_id=8)

    mat_chassis = mu.make_qa_cycles_material(
        "M_Core_Chassis", base_rgb=(0.19, 0.22, 0.27), armor_rgb=(0.28, 0.33, 0.39),
        accent_rgb=(0.90, 0.58, 0.14), emissive_rgb=(0.25, 0.88, 1.0), emissive_strength=5.2,
    )
    ob_chassis = mu.finalize_bmesh_to_object("Core_Chassis", bm_ch, mat_chassis,
                                             ao_max_dist=0.65, bone_names=bone_names, rig_obj=rig)

    # --- 2B. G0_Rusty (~2,300 tris — corrugated patchwork scrap drums, NO outer carbide teeth!) ---
    bm_g0 = bmesh.new()
    for d_idx, (z_c, bid) in enumerate(((0.34, 3), (0.66, 4))):
        mu.add_tube_ring(bm_g0, r_outer=0.88, r_inner=0.65, height=0.26, segs=16,
                         center=(0, 0, z_c), chamfer=0.014, zone=0.0, part_hash=0.20, bone_id=bid)
        for i in range(8):
            a0 = i * (math.pi * 0.25) + 0.04 + d_idx * 0.18
            a1 = (i + 1) * (math.pi * 0.25) - 0.05 + d_idx * 0.18
            r_out = 0.935 if i % 2 == 0 else 0.915
            mu.add_arc_plate(bm_g0, r_inner=0.86, r_outer=r_out, height=0.21,
                             ang_start=a0, ang_end=a1, segs=3, z_center=z_c,
                             bevel=0.010, zone=0.35 if i % 2 == 0 else 0.0, part_hash=0.34, bone_id=bid)
            amid = 0.5 * (a0 + a1)
            # Dull welded scraper rib + rusty bolts
            mu.add_box(bm_g0, size=(0.045, 0.06, 0.23),
                       center=(math.cos(amid) * 0.93, math.sin(amid) * 0.93, z_c),
                       rot_euler=(0, 0, amid + math.pi * 0.5), bevel=0.006, zone=0.35, part_hash=0.45, bone_id=bid)
        mu.add_bolt_ring(bm_g0, count=8, radius=0.92, z_center=z_c + 0.07,
                         bolt_r=0.018, bolt_h=0.025, radial=True, zone=0.70, bone_id=bid)

    mat_g0 = mu.make_qa_cycles_material(
        "M_Core_G0_Rusty", base_rgb=(0.33, 0.24, 0.19), armor_rgb=(0.42, 0.28, 0.20),
        accent_rgb=(0.56, 0.38, 0.24), emissive_rgb=(0.85, 0.34, 0.10), emissive_strength=1.6,
        metallic=0.66, roughness=0.70, rusty_wear=True,
    )
    ob_g0 = mu.finalize_bmesh_to_object("G0_Rusty", bm_g0, mat_g0,
                                        ao_max_dist=0.55, bone_names=bone_names, rig_obj=rig)

    # --- 2C. G1_SteelCarbide (~3,900 tris — 16 helical hooked shredder claws r_tip = 1.11m) ---
    bm_g1 = bmesh.new()
    for z_c, bid, r_dir, tw, ph in ((0.34, 3, 1.0, 0.10, 0.0), (0.66, 4, -1.0, -0.10, math.pi / 8.0)):
        mu.add_tube_ring(bm_g1, r_outer=0.91, r_inner=0.65, height=0.27, segs=24,
                         center=(0, 0, z_c), chamfer=0.016, zone=0.0, part_hash=0.30, bone_id=bid)
        mu.add_helical_hook_teeth_ring(
            bm_g1, count=8, r_root=0.88, r_tip=1.11,
            ang_width_root=0.30, ang_width_tip=0.11, height=0.23,
            z_center=z_c, rake_dir=r_dir, hook_ang=0.13, twist_ang=tw,
            phase_offset=ph, z_slices=4, bevel=0.006, zone=0.70, bone_id=bid,
        )
        mu.add_bolt_ring(bm_g1, count=8, radius=0.91, z_center=z_c,
                         bolt_r=0.020, bolt_h=0.026, phase_offset=ph + math.pi / 8.0,
                         radial=True, zone=0.70, bone_id=bid)
    for sign in (-1.0, 1.0):
        mu.add_box(bm_g1, size=(0.12, 0.50, 0.21), center=(sign * 0.44, -0.05, 0.96),
                   bevel=0.014, zone=0.35, part_hash=0.52, bone_id=5)

    mat_g1 = mu.make_qa_cycles_material(
        "M_Core_G1_Steel", base_rgb=(0.18, 0.21, 0.26), armor_rgb=(0.28, 0.33, 0.39),
        accent_rgb=(0.95, 0.54, 0.10), emissive_rgb=(1.0, 0.44, 0.06), emissive_strength=4.8,
        metallic=0.90, roughness=0.33,
    )
    ob_g1 = mu.finalize_bmesh_to_object("G1_SteelCarbide", bm_g1, mat_g1,
                                        ao_max_dist=0.60, bone_names=bone_names, rig_obj=rig)

    # --- 2D. G2_TitaniumTwin (~4,600 tris — 24 helical hooked cutters r_tip=1.13m + plasma coils) ---
    bm_g2 = bmesh.new()
    for z_c, bid, r_dir, tw, ph in ((0.34, 3, 1.0, 0.14, 0.0), (0.66, 4, -1.0, -0.14, math.pi / 12.0)):
        mu.add_tube_ring(bm_g2, r_outer=0.92, r_inner=0.65, height=0.28, segs=24,
                         center=(0, 0, z_c), chamfer=0.018, zone=0.35, part_hash=0.40, bone_id=bid)
        mu.add_helical_hook_teeth_ring(
            bm_g2, count=12, r_root=0.89, r_tip=1.13,
            ang_width_root=0.23, ang_width_tip=0.085, height=0.24,
            z_center=z_c, rake_dir=r_dir, hook_ang=0.14, twist_ang=tw,
            phase_offset=ph, z_slices=3, bevel=0.005, zone=0.70, bone_id=bid,
        )
        mu.add_tube_ring(bm_g2, r_outer=0.945, r_inner=0.90, height=0.042, segs=20,
                         center=(0, 0, z_c), chamfer=0.005, zone=1.0, part_hash=0.90, bone_id=bid)
    for sign, bid in ((-1.0, 6), (1.0, 7)):
        for k in range(4):
            mu.add_tube_ring(bm_g2, r_outer=0.118, r_inner=0.082, height=0.044, segs=10,
                             center=(sign * 0.22, -0.34 - k * 0.135, 0.95),
                             rot_euler=(math.pi * 0.5, 0, 0), chamfer=0.005,
                             zone=1.0, part_hash=0.86, bone_id=bid)
        mu.add_box(bm_g2, size=(0.15, 0.56, 0.22), center=(sign * 0.46, -0.04, 0.97),
                   rot_euler=(0, sign * 0.22, 0), bevel=0.014, zone=0.35, part_hash=0.58, bone_id=5)

    mat_g2 = mu.make_qa_cycles_material(
        "M_Core_G2_Titanium", base_rgb=(0.27, 0.33, 0.40), armor_rgb=(0.45, 0.52, 0.60),
        accent_rgb=(0.25, 0.82, 1.0), emissive_rgb=(0.18, 0.86, 1.0), emissive_strength=5.8,
        metallic=0.94, roughness=0.22,
    )
    ob_g2 = mu.finalize_bmesh_to_object("G2_TitaniumTwin", bm_g2, mat_g2,
                                        ao_max_dist=0.60, bone_names=bone_names, rig_obj=rig)

    # --- 2E. G3_DiamondReactor (~6,200 tris — 28 diamond-gold hooked cutters r_tip=1.15m + Wall Ring + Crown) ---
    bm_g3 = bmesh.new()
    for z_c, bid, r_dir, tw, ph in ((0.34, 3, 1.0, 0.16, 0.0), (0.66, 4, -1.0, -0.16, math.pi / 14.0)):
        mu.add_tube_ring(bm_g3, r_outer=0.93, r_inner=0.65, height=0.28, segs=28,
                         center=(0, 0, z_c), chamfer=0.018, zone=0.70, part_hash=0.50, bone_id=bid)
        mu.add_helical_hook_teeth_ring(
            bm_g3, count=14, r_root=0.90, r_tip=1.15,
            ang_width_root=0.20, ang_width_tip=0.08, height=0.25,
            z_center=z_c, rake_dir=r_dir, hook_ang=0.15, twist_ang=tw,
            phase_offset=ph, z_slices=3, bevel=0.005, zone=0.70, bone_id=bid,
        )
        for dz_ring in (-0.06, 0.06):
            mu.add_tube_ring(bm_g3, r_outer=0.96, r_inner=0.91, height=0.034, segs=24,
                             center=(0, 0, z_c + dz_ring), chamfer=0.004, zone=1.0, part_hash=0.93, bone_id=bid)

    # 8 Golden Reactor Crown Radiator Fins with glowing plasma veins on bn_core_vent (8)
    for i in range(8):
        a = i * (math.pi * 0.25)
        ca, sa = math.cos(a), math.sin(a)
        mu.add_box(bm_g3, size=(0.075, 0.26, 0.20),
                   center=(ca * 0.37, sa * 0.37, 1.16),
                   rot_euler=(0.24 * ca, 0.24 * sa, a + math.pi * 0.5),
                   bevel=0.009, zone=0.70, part_hash=0.82, bone_id=8)
        mu.add_box(bm_g3, size=(0.030, 0.20, 0.15),
                   center=(ca * 0.39, sa * 0.39, 1.17),
                   rot_euler=(0.24 * ca, 0.24 * sa, a + math.pi * 0.5),
                   bevel=0.004, zone=1.0, part_hash=0.96, bone_id=8)

    # Winged Turret Super-Armor + Barrel Overcharger Rails
    for sign, bid in ((-1.0, 6), (1.0, 7)):
        mu.add_box(bm_g3, size=(0.18, 0.62, 0.24), center=(sign * 0.48, -0.04, 0.98),
                   rot_euler=(0, sign * 0.26, 0), bevel=0.015, zone=0.70, part_hash=0.75, bone_id=5)
        for k in range(4):
            mu.add_tube_ring(bm_g3, r_outer=0.122, r_inner=0.084, height=0.046, segs=12,
                             center=(sign * 0.22, -0.34 - k * 0.135, 0.95),
                             rot_euler=(math.pi * 0.5, 0, 0), chamfer=0.006,
                             zone=1.0, part_hash=0.91, bone_id=bid)

    # 8-Segment Magnetic Force-Bumper Wall Ring on bn_wall_ring (2), r = 1.21..1.30m
    for i in range(8):
        a0 = i * (math.pi * 0.25) + 0.06
        a1 = (i + 1) * (math.pi * 0.25) - 0.06
        amid = 0.5 * (a0 + a1)
        mu.add_arc_plate(bm_g3, r_inner=1.21, r_outer=1.295, height=0.17,
                         ang_start=a0, ang_end=a1, segs=4, z_center=0.22,
                         bevel=0.012, zone=0.70, part_hash=0.68, bone_id=2)
        # Glowing force-bumper emitter strip on outer arc
        mu.add_box(bm_g3, size=(0.30, 0.045, 0.055),
                   center=(math.cos(amid) * 1.295, math.sin(amid) * 1.295, 0.22),
                   rot_euler=(0, 0, amid + math.pi * 0.5), bevel=0.005, zone=1.0, part_hash=0.95, bone_id=2)

    mat_g3 = mu.make_qa_cycles_material(
        "M_Core_G3_DiamondGold", base_rgb=(0.22, 0.25, 0.30), armor_rgb=(0.88, 0.64, 0.16),
        accent_rgb=(1.0, 0.80, 0.22), emissive_rgb=(0.25, 0.92, 1.0), emissive_strength=6.2,
        metallic=0.96, roughness=0.17,
    )
    ob_g3 = mu.finalize_bmesh_to_object("G3_DiamondReactor", bm_g3, mat_g3,
                                        ao_max_dist=0.65, bone_names=bone_names, rig_obj=rig)

    # --- Animations (EXACT 6 clips, 30 fps, LINEAR on loops, BEZIER on recoil/impact/death, 0 scale keys) ---
    pb_base = rig.pose.bones["bn_base"]
    pb_wall = rig.pose.bones["bn_wall_ring"]
    pb_low = rig.pose.bones["bn_drum_lower"]
    pb_upp = rig.pose.bones["bn_drum_upper"]
    pb_bar_l = rig.pose.bones["bn_barrel_L"]
    pb_bar_r = rig.pose.bones["bn_barrel_R"]
    pb_vent = rig.pose.bones["bn_core_vent"]

    clips = []

    # 1) AN_Core_IdleSpin (1..61, LINEAR)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_IdleSpin", cyclic=True)
    for f, frac in ((1, 0.0), (31, 0.5), (61, 1.0)):
        sc.frame_set(f)
        pb_low.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_upp.rotation_euler = (0.0, -2.0 * math.pi * frac, 0.0)
        pb_wall.rotation_euler = (0.0, -2.0 * math.pi * frac, 0.0)
        pb_low.keyframe_insert("rotation_euler", frame=f)
        pb_upp.keyframe_insert("rotation_euler", frame=f)
        pb_wall.keyframe_insert("rotation_euler", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    clips.append((act, slot, 1, 61))

    # 2) AN_Core_OverdriveSpin (1..31, LINEAR)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_OverdriveSpin", cyclic=True)
    for f, frac in ((1, 0.0), (16, 0.5), (31, 1.0)):
        sc.frame_set(f)
        pb_low.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_upp.rotation_euler = (0.0, -2.0 * math.pi * frac, 0.0)
        pb_wall.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_vent.location = (0.0, 0.08, 0.0)
        pb_vent.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        for pb in (pb_low, pb_upp, pb_wall, pb_vent):
            pb.keyframe_insert("rotation_euler", frame=f)
        pb_vent.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    clips.append((act, slot, 1, 31))

    # 3) AN_Core_FireL (1..9, BEZIER)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_FireL", cyclic=False)
    for f, rec_y in ((1, 0.0), (2, -0.22), (5, -0.07), (9, 0.0)):
        sc.frame_set(f)
        pb_bar_l.location = (0.0, rec_y, 0.0)
        pb_bar_l.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 9))

    # 4) AN_Core_FireR (1..9, BEZIER)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_FireR", cyclic=False)
    for f, rec_y in ((1, 0.0), (2, -0.22), (5, -0.07), (9, 0.0)):
        sc.frame_set(f)
        pb_bar_r.location = (0.0, rec_y, 0.0)
        pb_bar_r.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 9))

    # 5) AN_Core_CrunchImpact (1..11, BEZIER)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_CrunchImpact", cyclic=False)
    for f, dz, d_rot in ((1, 0.0, 0.0), (3, -0.045, 0.28), (6, 0.015, -0.08), (11, 0.0, 0.0)):
        sc.frame_set(f)
        pb_base.location = (0.0, dz, 0.0)
        pb_low.rotation_euler = (0.0, d_rot, 0.0)
        pb_upp.rotation_euler = (0.0, -d_rot, 0.0)
        pb_base.keyframe_insert("location", frame=f)
        pb_low.keyframe_insert("rotation_euler", frame=f)
        pb_upp.keyframe_insert("rotation_euler", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 11))

    # 6) AN_Core_OverloadDeath (1..37, BEZIER)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_OverloadDeath", cyclic=False)
    keyframes_death = [
        (1, 0.0, 0.0, 0.0, 0.0),
        (8, 0.16, -0.025, 0.038, 0.04),
        (15, 0.10, -0.015, -0.035, 0.09),
        (24, 0.15, -0.025, 0.048, 0.13),
        (37, 0.14, -0.035, 0.055, 0.15),
    ]
    for f, rot_jam, base_y, tilt_x, vent_y in keyframes_death:
        sc.frame_set(f)
        pb_low.rotation_euler = (0.0, rot_jam, 0.0)
        pb_upp.rotation_euler = (0.0, -rot_jam * 0.8, 0.0)
        pb_base.location = (0.0, base_y, 0.0)
        pb_base.rotation_euler = (tilt_x, 0.0, -tilt_x * 0.6)
        pb_vent.location = (0.0, vent_y, 0.0)
        pb_low.keyframe_insert("rotation_euler", frame=f)
        pb_upp.keyframe_insert("rotation_euler", frame=f)
        pb_base.keyframe_insert("location", frame=f)
        pb_base.keyframe_insert("rotation_euler", frame=f)
        pb_vent.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 37))

    mu.push_actions_to_nla(rig, clips)

    all_objs = [rig, ob_chassis, ob_g0, ob_g1, ob_g2, ob_g3]
    fbx_path = os.path.join(STAGING_V2, "SM_ShredderCore.fbx")
    mu.export_unity_fbx(fbx_path, all_objs, skeletal=True)
    save_blend_cache_v2("SM_ShredderCore")

    mesh_tris = {o.name: len(o.data.polygons) for o in all_objs if o.type == "MESH"}
    return {
        "asset": "SM_ShredderCore.fbx",
        "tris_total": sum(mesh_tris.values()),
        "tris_by_mesh": mesh_tris,
        "clips": [c[0].name for c in clips],
    }


# ---------------------------------------------------------------------------
# 3. UPGRADED SM_PlasmaSawOrb.fbx (~2,900 tris, diameter ~1.02 m)
# ---------------------------------------------------------------------------
def build_plasma_saw_orb_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Dual armored saw disc + expansion ribs
    mu.add_tube_ring(bm, r_outer=0.41, r_inner=0.14, height=0.056, segs=36,
                     center=(0, 0, 0.0), chamfer=0.009, zone=0.0, part_hash=0.28)
    for i in range(8):
        a = i * (math.pi * 0.25)
        mu.add_box(bm, size=(0.045, 0.22, 0.068),
                   center=(math.cos(a) * 0.27, math.sin(a) * 0.27, 0.0),
                   rot_euler=(0, 0, a + math.pi * 0.5 + 0.22), bevel=0.006, zone=0.35, part_hash=0.45)
    # 20 Helical hooked plasma-carbide teeth reaching r_tip = 0.51 m (diameter 1.02 m)
    mu.add_helical_hook_teeth_ring(
        bm, count=20, r_root=0.39, r_tip=0.51,
        ang_width_root=0.17, ang_width_tip=0.06, height=0.046,
        z_center=0.0, rake_dir=1.0, hook_ang=0.11, twist_ang=0.06,
        z_slices=3, bevel=0.004, zone=0.70,
    )
    # Twin glowing concentric plasma containment rings + central bolted gyro bearing
    for rz, hz in ((0.35, 0.066), (0.20, 0.082)):
        mu.add_tube_ring(bm, r_outer=rz + 0.024, r_inner=rz - 0.024, height=hz, segs=28,
                         center=(0, 0, 0.0), chamfer=0.005, zone=1.0, part_hash=0.94)
    mu.add_cylinder(bm, r1=0.15, r2=0.12, depth=0.13, segs=20,
                    center=(0, 0, 0.0), bevel=0.012, zone=0.35, part_hash=0.52)
    mu.add_bolt_ring(bm, count=8, radius=0.105, z_center=0.068, bolt_r=0.014, bolt_h=0.014, zone=0.70)

    mat = mu.make_qa_cycles_material(
        "M_PlasmaSawOrb", base_rgb=(0.19, 0.23, 0.29), armor_rgb=(0.34, 0.40, 0.48),
        accent_rgb=(0.22, 0.88, 1.0), emissive_rgb=(0.14, 0.92, 1.0), emissive_strength=5.8,
    )
    ob = mu.finalize_bmesh_to_object("SM_PlasmaSawOrb", bm, mat, ao_max_dist=0.42)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_PlasmaSawOrb.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_PlasmaSawOrb")
    return {"asset": "SM_PlasmaSawOrb.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 4. UPGRADED SM_Electromagnet_BH.fbx (~3,900 tris, Skeletal)
# ---------------------------------------------------------------------------
def build_electromagnet_bh_v2() -> dict:
    sc = mu.reset_scene(fps=30)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.25)),
        ("bn_yoke", "root", (0.0, 0.0, 0.15), (0.0, 0.0, 0.75)),
        ("bn_flux_ring", "bn_yoke", (0.0, 0.0, 0.08), (0.0, 0.0, 0.38)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_Electromagnet_BH", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Heavy H-beam gantry yoke crossbar + top cooling radiator fins (bn_yoke = 1)
    mu.add_box(bm, size=(1.22, 0.38, 0.22), center=(0, 0, 0.86), bevel=0.022, zone=0.0, part_hash=0.20, bone_id=1)
    for fx in range(-4, 5):
        mu.add_box(bm, size=(0.035, 0.32, 0.12), center=(fx * 0.11, 0, 1.01),
                   bevel=0.004, zone=0.35, part_hash=0.40, bone_id=1)
    mu.add_cylinder(bm, r1=0.16, r2=0.12, depth=0.12, segs=12, center=(0, 0, 1.02),
                    bevel=0.01, zone=1.0, part_hash=0.95, bone_id=1)

    for sign in (-1.0, 1.0):
        lx = sign * 0.47
        mu.add_box(bm, size=(0.27, 0.32, 0.70), center=(lx, 0, 0.48), bevel=0.018, zone=0.35, part_hash=0.35, bone_id=1)
        # 4 High-density copper solenoid coils + busbar clamps per leg
        for k in range(4):
            mu.add_tube_ring(bm, r_outer=0.24, r_inner=0.145, height=0.10, segs=16,
                             center=(lx, 0, 0.24 + k * 0.145), chamfer=0.008, zone=0.70, part_hash=0.72, bone_id=1)
        mu.add_box(bm, size=(0.05, 0.38, 0.56), center=(lx + sign * 0.15, 0, 0.46),
                   bevel=0.006, zone=0.70, part_hash=0.78, bone_id=1)
        # Stepped glowing gravity pole shoe at bottom
        mu.add_cylinder(bm, r1=0.17, r2=0.21, depth=0.11, segs=14,
                        center=(lx, 0, 0.105), bevel=0.010, zone=1.0, part_hash=0.93, bone_id=1)

    # Central spinning gravity-flux rotor (bn_flux_ring = 2)
    mu.add_tube_ring(bm, r_outer=0.35, r_inner=0.21, height=0.085, segs=20,
                     center=(0, 0, 0.20), chamfer=0.010, zone=1.0, part_hash=0.96, bone_id=2)
    mu.add_cylinder(bm, r1=0.13, r2=0.13, depth=0.10, segs=14,
                    center=(0, 0, 0.20), bevel=0.010, zone=1.0, part_hash=0.99, bone_id=2)
    mu.add_helical_hook_teeth_ring(
        bm, count=10, r_root=0.33, r_tip=0.42,
        ang_width_root=0.22, ang_width_tip=0.09, height=0.065,
        z_center=0.20, rake_dir=1.0, hook_ang=0.08, twist_ang=0.08,
        z_slices=2, bevel=0.005, zone=0.70, bone_id=2,
    )

    mat = mu.make_qa_cycles_material(
        "M_Electromagnet_BH", base_rgb=(0.17, 0.19, 0.25), armor_rgb=(0.26, 0.30, 0.37),
        accent_rgb=(0.86, 0.44, 0.18), emissive_rgb=(0.56, 0.24, 1.0), emissive_strength=5.8,
    )
    ob = mu.finalize_bmesh_to_object("Electromagnet_Mesh", bm, mat, ao_max_dist=0.55,
                                    bone_names=bone_names, rig_obj=rig)

    pb_yoke = rig.pose.bones["bn_yoke"]
    pb_ring = rig.pose.bones["bn_flux_ring"]
    clips = []

    act, slot, _ = mu.begin_slotted_action(rig, "AN_BH_Deploy", cyclic=False)
    for f, dy in ((1, 0.45), (7, -0.05), (10, 0.02), (13, 0.0)):
        sc.frame_set(f)
        pb_yoke.location = (0.0, dy, 0.0)
        pb_yoke.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 13))

    act, slot, _ = mu.begin_slotted_action(rig, "AN_BH_Pulse", cyclic=True)
    for idx in range(7):
        f = 1 + idx * 5
        frac = idx / 6.0
        sc.frame_set(f)
        pb_ring.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_yoke.location = (0.0, 0.035 * math.sin(2.0 * math.pi * frac), 0.0)
        pb_ring.keyframe_insert("rotation_euler", frame=f)
        pb_yoke.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    clips.append((act, slot, 1, 31))

    mu.push_actions_to_nla(rig, clips)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Electromagnet_BH.fbx"), [rig, ob], skeletal=True)
    save_blend_cache_v2("SM_Electromagnet_BH")
    return {"asset": "SM_Electromagnet_BH.fbx", "tris_total": len(ob.data.polygons), "clips": [c[0].name for c in clips]}


# ---------------------------------------------------------------------------
# 5. UPGRADED SM_RailEmitter_DR.fbx (~2,200 tris, Skeletal)
# ---------------------------------------------------------------------------
def build_rail_emitter_dr_v2() -> dict:
    sc = mu.reset_scene(fps=30)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.30)),
        ("bn_rail_L", "root", (-0.14, 0.0, 0.42), (-0.44, 0.0, 0.42)),
        ("bn_rail_R", "root", (0.14, 0.0, 0.42), (0.44, 0.0, 0.42)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_RailEmitter_DR", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Solid lower CNC bedplate (guarantees rock-solid silhouette IoU during rail spread!)
    mu.add_box(bm, size=(0.68, 1.08, 0.16), center=(0, -0.18, 0.14), bevel=0.02, zone=0.0, part_hash=0.18, bone_id=0)
    mu.add_bolt_ring(bm, count=8, radius=0.30, z_center=0.225, bolt_r=0.018, bolt_h=0.016, zone=0.70, bone_id=0)
    # Rear breech block + twin HV capacitor cylinders
    mu.add_box(bm, size=(0.60, 0.46, 0.32), center=(0, 0.18, 0.32), bevel=0.02, zone=0.35, part_hash=0.28, bone_id=0)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.105, r2=0.095, depth=0.38, segs=12,
                        center=(sx * 0.19, 0.24, 0.52), rot_euler=(math.radians(-28.0), 0, 0),
                        bevel=0.008, zone=0.70, part_hash=0.72, bone_id=0)
    # Central glowing plasma accelerator core + focusing rings
    mu.add_cylinder(bm, r1=0.105, r2=0.095, depth=0.78, segs=16,
                    center=(0, -0.22, 0.38), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.008, zone=1.0, part_hash=0.96, bone_id=0)
    for ky in range(4):
        mu.add_tube_ring(bm, r_outer=0.135, r_inner=0.098, height=0.036, segs=14,
                         center=(0, -0.05 - ky * 0.17, 0.38), rot_euler=(math.pi * 0.5, 0, 0),
                         chamfer=0.005, zone=0.70, part_hash=0.76, bone_id=0)

    # Left & Right Multi-Stage Electromagnetic Rails (bn_rail_L = 1, bn_rail_R = 2)
    for sign, bid in ((-1.0, 1), (1.0, 2)):
        rx = sign * 0.215
        mu.add_box(bm, size=(0.15, 0.98, 0.23), center=(rx, -0.22, 0.38),
                   bevel=0.015, zone=0.35, part_hash=0.45, bone_id=bid)
        # Inner glowing plasma rail liner
        mu.add_box(bm, size=(0.035, 0.90, 0.065), center=(sign * 0.135, -0.24, 0.38),
                   bevel=0.004, zone=1.0, part_hash=0.92, bone_id=bid)
        # 6 Copper induction fins along outer rail flank
        for kf in range(6):
            mu.add_box(bm, size=(0.065, 0.075, 0.26),
                       center=(sign * 0.275, 0.12 - kf * 0.145, 0.38),
                       bevel=0.006, zone=0.70, part_hash=0.74 + 0.02 * kf, bone_id=bid)

    mat = mu.make_qa_cycles_material(
        "M_RailEmitter_DR", base_rgb=(0.18, 0.22, 0.27), armor_rgb=(0.30, 0.36, 0.43),
        accent_rgb=(1.0, 0.48, 0.12), emissive_rgb=(1.0, 0.26, 0.08), emissive_strength=5.2,
    )
    ob = mu.finalize_bmesh_to_object("RailEmitter_Mesh", bm, mat, ao_max_dist=0.50,
                                    bone_names=bone_names, rig_obj=rig)

    pb_l = rig.pose.bones["bn_rail_L"]
    pb_r = rig.pose.bones["bn_rail_R"]
    act, slot, _ = mu.begin_slotted_action(rig, "AN_DR_ChargeFire", cyclic=False)
    for f, spread in ((1, 0.0), (6, 0.036), (10, -0.008), (16, 0.0)):
        sc.frame_set(f)
        pb_l.location = (0.0, spread, 0.0)
        pb_r.location = (0.0, spread, 0.0)
        pb_l.keyframe_insert("location", frame=f)
        pb_r.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    mu.push_actions_to_nla(rig, [(act, slot, 1, 16)])

    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_RailEmitter_DR.fbx"), [rig, ob], skeletal=True)
    save_blend_cache_v2("SM_RailEmitter_DR")
    return {"asset": "SM_RailEmitter_DR.fbx", "tris_total": len(ob.data.polygons), "clips": ["AN_DR_ChargeFire"]}


# ---------------------------------------------------------------------------
# 6. UPGRADED SM_GoldenBot.fbx (~2,500 tris, Skeletal)
# ---------------------------------------------------------------------------
def build_golden_bot_v2() -> dict:
    sc = mu.reset_scene(fps=30)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.20)),
        ("bn_body", "root", (0.0, 0.0, 0.18), (0.0, 0.0, 0.56)),
        ("bn_rotor", "bn_body", (0.0, 0.0, 0.16), (0.0, 0.0, 0.42)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_GoldenBot", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Stepped ornate golden collector dome + 8 crown ribs + bolts (bn_body = 1)
    mu.add_cylinder(bm, r1=0.34, r2=0.23, depth=0.22, segs=20, center=(0, 0, 0.42),
                    bevel=0.020, zone=0.70, part_hash=0.62, bone_id=1)
    mu.add_cylinder(bm, r1=0.18, r2=0.12, depth=0.08, segs=16, center=(0, 0, 0.55),
                    bevel=0.012, zone=1.0, part_hash=0.96, bone_id=1)
    for i in range(8):
        a = i * (math.pi * 0.25)
        mu.add_box(bm, size=(0.036, 0.12, 0.16),
                   center=(math.cos(a) * 0.27, math.sin(a) * 0.27, 0.44),
                   rot_euler=(0.28 * math.cos(a), 0.28 * math.sin(a), a + math.pi * 0.5),
                   bevel=0.005, zone=0.35, part_hash=0.44, bone_id=1)
    # Wrap-around glowing sensor visor (-Y)
    mu.add_arc_plate(bm, r_inner=0.26, r_outer=0.335, height=0.075,
                     ang_start=-math.pi * 0.72, ang_end=-math.pi * 0.28, segs=6,
                     z_center=0.41, bevel=0.008, zone=1.0, part_hash=0.95, bone_id=1)
    # Ventral magnetic tractor-beam ring
    mu.add_tube_ring(bm, r_outer=0.29, r_inner=0.14, height=0.085, segs=20,
                     center=(0, 0, 0.18), chamfer=0.010, zone=1.0, part_hash=0.92, bone_id=1)
    # Shrouded outer hover duct (bn_body = 1 -> preserves circular silhouette during spin!)
    mu.add_tube_ring(bm, r_outer=0.44, r_inner=0.375, height=0.09, segs=28,
                     center=(0, 0, 0.30), chamfer=0.012, zone=0.35, part_hash=0.40, bone_id=1)
    mu.add_bolt_ring(bm, count=12, radius=0.408, z_center=0.35, bolt_r=0.014, bolt_h=0.014, zone=0.70, bone_id=1)

    # Internal 6-blade turbine + 3 spinning thruster pods (bn_rotor = 2)
    for i in range(6):
        a = i * (math.pi / 3.0)
        mu.add_box(bm, size=(0.045, 0.14, 0.025),
                   center=(math.cos(a) * 0.31, math.sin(a) * 0.31, 0.29),
                   rot_euler=(0.35, 0, a + math.pi * 0.5), bevel=0.004, zone=0.70, part_hash=0.72, bone_id=2)
    for i in range(3):
        a = i * (2.0 * math.pi / 3.0)
        mu.add_cylinder(bm, r1=0.062, r2=0.052, depth=0.115, segs=12,
                        center=(math.cos(a) * 0.325, math.sin(a) * 0.325, 0.33),
                        bevel=0.008, zone=0.70, part_hash=0.78, bone_id=2)

    mat = mu.make_qa_cycles_material(
        "M_GoldenBot", base_rgb=(0.22, 0.24, 0.29), armor_rgb=(0.34, 0.38, 0.45),
        accent_rgb=(1.0, 0.78, 0.18), emissive_rgb=(1.0, 0.84, 0.22), emissive_strength=4.6,
        metallic=0.96, roughness=0.18,
    )
    ob = mu.finalize_bmesh_to_object("GoldenBot_Mesh", bm, mat, ao_max_dist=0.45,
                                    bone_names=bone_names, rig_obj=rig)

    pb_body = rig.pose.bones["bn_body"]
    pb_rot = rig.pose.bones["bn_rotor"]
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Bot_Hover", cyclic=True)
    for idx in range(10):
        f = 1 + idx * 5
        frac = idx / 9.0
        sc.frame_set(f)
        pb_rot.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_body.location = (0.0, 0.020 * math.sin(2.0 * math.pi * frac), 0.0)
        pb_rot.keyframe_insert("rotation_euler", frame=f)
        pb_body.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    mu.push_actions_to_nla(rig, [(act, slot, 1, 46)])

    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_GoldenBot.fbx"), [rig, ob], skeletal=True)
    save_blend_cache_v2("SM_GoldenBot")
    return {"asset": "SM_GoldenBot.fbx", "tris_total": len(ob.data.polygons), "clips": ["AN_Bot_Hover"]}


# ---------------------------------------------------------------------------
# 7. UPGRADED SM_Projectile.fbx (~680 tris, ~0.46 m fin-stabilized sabot plasma dart)
# ---------------------------------------------------------------------------
def build_projectile_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Penetrator glowing plasma nose cone facing -Y (Unity +Z)
    mu.add_cylinder(bm, r1=0.072, r2=0.012, depth=0.22, segs=12,
                    center=(0, -0.12, 0.0), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.004, zone=1.0, part_hash=0.95)
    # Fluted kinetic body + 2 brass sabot driving bands
    mu.add_cylinder(bm, r1=0.062, r2=0.072, depth=0.20, segs=12,
                    center=(0, 0.09, 0.0), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.005, zone=0.35, part_hash=0.42)
    for yb in (-0.01, 0.08):
        mu.add_tube_ring(bm, r_outer=0.082, r_inner=0.058, height=0.028, segs=12,
                         center=(0, yb, 0.0), rot_euler=(math.pi * 0.5, 0, 0),
                         chamfer=0.004, zone=0.70, part_hash=0.76)
    # 4 Cruciform stabilizer tail fins + glowing tracer base
    for k in range(4):
        af = k * (math.pi * 0.5) + math.pi * 0.25
        mu.add_box(bm, size=(0.018, 0.12, 0.095),
                   center=(math.cos(af) * 0.062, 0.15, math.sin(af) * 0.062),
                   rot_euler=(0.0, af, 0.0), bevel=0.003, zone=0.70, part_hash=0.70)
    mu.add_cylinder(bm, r1=0.048, r2=0.042, depth=0.04, segs=10,
                    center=(0, 0.20, 0.0), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.003, zone=1.0, part_hash=0.98)

    mat = mu.make_qa_cycles_material(
        "M_Projectile", base_rgb=(0.30, 0.35, 0.42), armor_rgb=(0.55, 0.62, 0.70),
        accent_rgb=(0.95, 0.82, 0.36), emissive_rgb=(0.40, 0.92, 1.0), emissive_strength=6.2,
    )
    ob = mu.finalize_bmesh_to_object("SM_Projectile", bm, mat, ao_max_dist=0.26)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Projectile.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Projectile")
    return {"asset": "SM_Projectile.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


def main():
    os.makedirs(STAGING_V2, exist_ok=True)
    summary = [
        build_arena_foundry_env(),
        build_shredder_core_v2(),
        build_plasma_saw_orb_v2(),
        build_electromagnet_bh_v2(),
        build_rail_emitter_dr_v2(),
        build_golden_bot_v2(),
        build_projectile_v2(),
    ]
    print("=== V2 FOUNDRY, HERO & UWS SUMMARY ===")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
