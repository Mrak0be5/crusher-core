"""Procedural generator for Hero (SM_ShredderCore with G0..G3 grades), Satellites & UWs:
SM_PlasmaSawOrb, SM_Electromagnet_BH, SM_RailEmitter_DR, SM_GoldenBot, SM_Projectile."""
from __future__ import annotations

import json
import math
import os
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mesh_rig_utils as mu  # noqa: E402

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STAGING_MODELS = os.path.join(ROOT_DIR, "staging", "models")
QA_CACHE = os.path.join(ROOT_DIR, "build", "qa", "blend_cache")


def save_blend_cache(name: str):
    os.makedirs(QA_CACHE, exist_ok=True)
    path = os.path.join(QA_CACHE, f"{name}.blend")
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    return path


# ---------------------------------------------------------------------------
# 1. SM_ShredderCore (RIG_ShredderCore + Core_Chassis + G0..G3 submeshes)
# ---------------------------------------------------------------------------
def build_shredder_core() -> dict:
    sc = mu.reset_scene(fps=30)

    # Bone indices for rigid skinning:
    # 0: root, 1: bn_base, 2: bn_wall_ring, 3: bn_drum_lower (CW), 4: bn_drum_upper (CCW),
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

    # --- Shared Chassis (Base + Reactor Core + Twin Pneumatic Cannon + Vent Cap) ---
    bm_ch = bmesh.new()
    # Heavy octagonal base pedestal (bn_base = 1)
    mu.add_cylinder(bm_ch, r1=1.04, r2=0.94, depth=0.18, segs=8, center=(0, 0, 0.09),
                    bevel=0.02, zone=0.0, part_hash=0.15, bone_id=1)
    for i in range(8):
        a = i * math.pi / 4.0 + math.pi / 8.0
        mu.add_box(bm_ch, size=(0.20, 0.30, 0.14),
                   center=(math.cos(a) * 0.96, math.sin(a) * 0.96, 0.07),
                   rot_euler=(0, 0, a + math.pi / 2.0), bevel=0.015, zone=0.35, part_hash=0.25, bone_id=1)
    # Central glowing reactor column inside drums
    mu.add_cylinder(bm_ch, r1=0.64, r2=0.64, depth=0.72, segs=16, center=(0, 0, 0.52),
                    bevel=0.01, zone=1.0, part_hash=0.9, bone_id=1)
    # Divider collar between lower and upper drums
    mu.add_tube_ring(bm_ch, r_outer=0.84, r_inner=0.62, height=0.05, segs=16,
                     center=(0, 0, 0.50), chamfer=0.01, zone=0.35, part_hash=0.4, bone_id=1)

    # Turret housing (bn_turret_yaw = 5)
    mu.add_cylinder(bm_ch, r1=0.62, r2=0.54, depth=0.20, segs=12, center=(0, 0, 0.92),
                    bevel=0.02, zone=0.0, part_hash=0.5, bone_id=5)
    mu.add_box(bm_ch, size=(0.68, 0.58, 0.22), center=(0, -0.06, 0.95),
               bevel=0.02, zone=0.35, part_hash=0.55, bone_id=5)

    # Twin Pneumatic Barrels (bn_barrel_L = 6, bn_barrel_R = 7) facing -Y (Unity +Z)
    for sign, bid in ((-1.0, 6), (1.0, 7)):
        bx = sign * 0.22
        # Main barrel tube
        mu.add_cylinder(bm_ch, r1=0.085, r2=0.075, depth=0.72, segs=12,
                        center=(bx, -0.58, 0.95), rot_euler=(math.pi / 2.0, 0, 0),
                        bevel=0.008, zone=0.0, part_hash=0.62, bone_id=bid)
        # Muzzle brake
        mu.add_cylinder(bm_ch, r1=0.11, r2=0.10, depth=0.16, segs=8,
                        center=(bx, -0.94, 0.95), rot_euler=(math.pi / 2.0, 0, 0),
                        bevel=0.01, zone=0.70, part_hash=0.7, bone_id=bid)
        # Muzzle glowing bore
        mu.add_cylinder(bm_ch, r1=0.055, r2=0.055, depth=0.04, segs=8,
                        center=(bx, -1.01, 0.95), rot_euler=(math.pi / 2.0, 0, 0),
                        bevel=0.003, zone=1.0, part_hash=0.95, bone_id=bid)

    # Top Reactor Vent Shutter Cap (bn_core_vent = 8)
    mu.add_tube_ring(bm_ch, r_outer=0.42, r_inner=0.18, height=0.10, segs=12,
                     center=(0, 0, 1.11), chamfer=0.015, zone=0.35, part_hash=0.75, bone_id=8)
    mu.add_cylinder(bm_ch, r1=0.20, r2=0.16, depth=0.08, segs=12,
                    center=(0, 0, 1.12), bevel=0.01, zone=1.0, part_hash=0.98, bone_id=8)

    mat_chassis = mu.make_qa_cycles_material(
        "M_Core_Chassis", base_rgb=(0.20, 0.23, 0.27), armor_rgb=(0.30, 0.34, 0.40),
        accent_rgb=(0.88, 0.56, 0.14), emissive_rgb=(1.0, 0.48, 0.08), emissive_strength=5.0
    )
    ob_chassis = mu.finalize_bmesh_to_object("Core_Chassis", bm_ch, mat_chassis,
                                             ao_max_dist=0.65, bone_names=bone_names, rig_obj=rig)

    # --- Grade 0: G0_Rusty (Thin stamped rusty drum plates without carbide teeth) ---
    bm_g0 = bmesh.new()
    for z_c, bid in ((0.34, 3), (0.66, 4)):
        mu.add_tube_ring(bm_g0, r_outer=0.88, r_inner=0.66, height=0.26, segs=12,
                         center=(0, 0, z_c), chamfer=0.015, zone=0.0, part_hash=0.2, bone_id=bid)
        for i in range(6):
            a0 = i * (2.0 * math.pi / 6.0) + 0.05
            a1 = (i + 1) * (2.0 * math.pi / 6.0) - 0.05
            mu.add_arc_plate(bm_g0, r_inner=0.86, r_outer=0.93, height=0.22,
                             ang_start=a0, ang_end=a1, segs=3, z_center=z_c,
                             bevel=0.01, zone=0.35, part_hash=0.35, bone_id=bid)
    mat_g0 = mu.make_qa_cycles_material(
        "M_Core_G0_Rusty", base_rgb=(0.32, 0.24, 0.20), armor_rgb=(0.40, 0.28, 0.21),
        accent_rgb=(0.52, 0.34, 0.22), emissive_rgb=(0.8, 0.3, 0.1), emissive_strength=1.5,
        metallic=0.65, roughness=0.72, rusty_wear=True
    )
    ob_g0 = mu.finalize_bmesh_to_object("G0_Rusty", bm_g0, mat_g0,
                                        ao_max_dist=0.55, bone_names=bone_names, rig_obj=rig)

    # --- Grade 1: G1_SteelCarbide (Gunmetal drums + 12 heavy carbide Thorns, r_tip = 1.11 m) ---
    bm_g1 = bmesh.new()
    for idx_d, (z_c, bid, rake_s, ph) in enumerate(((0.34, 3, 0.10, 0.0), (0.66, 4, -0.10, math.pi / 6.0))):
        mu.add_tube_ring(bm_g1, r_outer=0.91, r_inner=0.66, height=0.27, segs=18,
                         center=(0, 0, z_c), chamfer=0.018, zone=0.0, part_hash=0.3, bone_id=bid)
        mu.add_cutter_teeth_ring(bm_g1, count=6, r_root=0.88, r_tip=1.11,
                                 ang_width_root=0.34, ang_width_tip=0.14, height=0.23,
                                 z_center=z_c, rake_shift_ang=rake_s, phase_offset=ph,
                                 bevel=0.009, zone=0.70, bone_id=bid)
    # Side turret cheek armor
    for sign in (-1.0, 1.0):
        mu.add_box(bm_g1, size=(0.12, 0.48, 0.20), center=(sign * 0.38, -0.05, 0.95),
                   bevel=0.015, zone=0.35, part_hash=0.5, bone_id=5)
    mat_g1 = mu.make_qa_cycles_material(
        "M_Core_G1_Steel", base_rgb=(0.18, 0.21, 0.25), armor_rgb=(0.28, 0.32, 0.38),
        accent_rgb=(0.95, 0.52, 0.10), emissive_rgb=(1.0, 0.42, 0.05), emissive_strength=4.5,
        metallic=0.90, roughness=0.35
    )
    ob_g1 = mu.finalize_bmesh_to_object("G1_SteelCarbide", bm_g1, mat_g1,
                                        ao_max_dist=0.60, bone_names=bone_names, rig_obj=rig)

    # --- Grade 2: G2_TitaniumTwin (24 helical cutter blades r_tip = 1.13 m + twin inductor coils) ---
    bm_g2 = bmesh.new()
    for z_c, bid, rake_s, tw, ph in ((0.34, 3, 0.09, 0.10, 0.0), (0.66, 4, -0.09, -0.10, math.pi / 12.0)):
        mu.add_tube_ring(bm_g2, r_outer=0.92, r_inner=0.66, height=0.28, segs=24,
                         center=(0, 0, z_c), chamfer=0.018, zone=0.35, part_hash=0.4, bone_id=bid)
        mu.add_cutter_teeth_ring(bm_g2, count=12, r_root=0.89, r_tip=1.13,
                                 ang_width_root=0.24, ang_width_tip=0.09, height=0.24,
                                 z_center=z_c, rake_shift_ang=rake_s, twist_ang=tw, phase_offset=ph,
                                 bevel=0.007, zone=0.70, bone_id=bid)
    # Inductor coils on barrels
    for sign, bid in ((-1.0, 6), (1.0, 7)):
        for k in range(3):
            mu.add_tube_ring(bm_g2, r_outer=0.115, r_inner=0.08, height=0.05, segs=12,
                             center=(sign * 0.22, -0.42 - k * 0.14, 0.95),
                             rot_euler=(math.pi / 2.0, 0, 0), chamfer=0.006,
                             zone=1.0, part_hash=0.85, bone_id=bid)
    mat_g2 = mu.make_qa_cycles_material(
        "M_Core_G2_Titanium", base_rgb=(0.28, 0.33, 0.40), armor_rgb=(0.45, 0.52, 0.60),
        accent_rgb=(0.25, 0.80, 1.0), emissive_rgb=(0.20, 0.82, 1.0), emissive_strength=5.5,
        metallic=0.94, roughness=0.24
    )
    ob_g2 = mu.finalize_bmesh_to_object("G2_TitaniumTwin", bm_g2, mat_g2,
                                        ao_max_dist=0.60, bone_names=bone_names, rig_obj=rig)

    # --- Grade 3: G3_DiamondReactor (Diamond-gold cutters r_tip = 1.15 m + Reactor fins + Wall ring) ---
    bm_g3 = bmesh.new()
    for z_c, bid, rake_s, tw, ph in ((0.34, 3, 0.11, 0.12, 0.0), (0.66, 4, -0.11, -0.12, math.pi / 12.0)):
        mu.add_tube_ring(bm_g3, r_outer=0.93, r_inner=0.66, height=0.28, segs=24,
                         center=(0, 0, z_c), chamfer=0.02, zone=0.70, part_hash=0.5, bone_id=bid)
        mu.add_cutter_teeth_ring(bm_g3, count=12, r_root=0.90, r_tip=1.15,
                                 ang_width_root=0.26, ang_width_tip=0.11, height=0.25,
                                 z_center=z_c, rake_shift_ang=rake_s, twist_ang=tw, phase_offset=ph,
                                 bevel=0.008, zone=0.70, bone_id=bid)
        # Glowing plasma core rings between teeth
        mu.add_tube_ring(bm_g3, r_outer=0.96, r_inner=0.91, height=0.05, segs=24,
                         center=(0, 0, z_c), chamfer=0.005, zone=1.0, part_hash=0.92, bone_id=bid)
    # 6 Golden Reactor Crown Fins on bn_core_vent (8)
    for i in range(6):
        a = i * math.pi / 3.0
        mu.add_box(bm_g3, size=(0.08, 0.24, 0.18),
                   center=(math.cos(a) * 0.36, math.sin(a) * 0.36, 1.15),
                   rot_euler=(0.22 * math.cos(a), 0.22 * math.sin(a), a + math.pi / 2.0),
                   bevel=0.01, zone=0.70, part_hash=0.8, bone_id=8)
    # Segmented Magnetic Force-Bumper Ring on bn_wall_ring (2), radius ~1.28 m
    for i in range(8):
        a0 = i * (math.pi / 4.0) + 0.08
        a1 = (i + 1) * (math.pi / 4.0) - 0.08
        mu.add_arc_plate(bm_g3, r_inner=1.21, r_outer=1.29, height=0.16,
                         ang_start=a0, ang_end=a1, segs=3, z_center=0.22,
                         bevel=0.012, zone=0.70, part_hash=0.65, bone_id=2)
    mat_g3 = mu.make_qa_cycles_material(
        "M_Core_G3_DiamondGold", base_rgb=(0.22, 0.24, 0.29), armor_rgb=(0.85, 0.62, 0.16),
        accent_rgb=(1.0, 0.78, 0.22), emissive_rgb=(1.0, 0.68, 0.12), emissive_strength=6.0,
        metallic=0.96, roughness=0.18
    )
    ob_g3 = mu.finalize_bmesh_to_object("G3_DiamondReactor", bm_g3, mat_g3,
                                        ao_max_dist=0.65, bone_names=bone_names, rig_obj=rig)

    # --- Animations (Blender 5.2 Slotted Actions + channelbag API) ---
    pb_base = rig.pose.bones["bn_base"]
    pb_wall = rig.pose.bones["bn_wall_ring"]
    pb_low = rig.pose.bones["bn_drum_lower"]
    pb_upp = rig.pose.bones["bn_drum_upper"]
    pb_bar_l = rig.pose.bones["bn_barrel_L"]
    pb_bar_r = rig.pose.bones["bn_barrel_R"]
    pb_vent = rig.pose.bones["bn_core_vent"]

    clips = []

    # 1) AN_Core_IdleSpin: frames 1..61 (60f seamless loop, LINEAR!)
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

    # 2) AN_Core_OverdriveSpin: frames 1..31 (30f fast seamless loop, LINEAR!)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_OverdriveSpin", cyclic=True)
    for f, frac in ((1, 0.0), (16, 0.5), (31, 1.0)):
        sc.frame_set(f)
        pb_low.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_upp.rotation_euler = (0.0, -2.0 * math.pi * frac, 0.0)
        pb_wall.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_vent.location = (0.0, 0.08, 0.0)  # Raised reactor vent cap
        pb_vent.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        for pb in (pb_low, pb_upp, pb_wall, pb_vent):
            pb.keyframe_insert("rotation_euler", frame=f)
        pb_vent.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    clips.append((act, slot, 1, 31))

    # 3) AN_Core_FireL: frames 1..9 (BEZIER recoil along barrel axis)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_FireL", cyclic=False)
    for f, rec_y in ((1, 0.0), (2, -0.22), (5, -0.07), (9, 0.0)):
        sc.frame_set(f)
        pb_bar_l.location = (0.0, rec_y, 0.0)
        pb_bar_l.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 9))

    # 4) AN_Core_FireR: frames 1..9 (BEZIER recoil along barrel axis)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Core_FireR", cyclic=False)
    for f, rec_y in ((1, 0.0), (2, -0.22), (5, -0.07), (9, 0.0)):
        sc.frame_set(f)
        pb_bar_r.location = (0.0, rec_y, 0.0)
        pb_bar_r.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 9))

    # 5) AN_Core_CrunchImpact: frames 1..11 (BEZIER heavy torque jolt)
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

    # 6) AN_Core_OverloadDeath: frames 1..37 (BEZIER jam shudder + reactor blow-open)
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
    fbx_path = os.path.join(STAGING_MODELS, "SM_ShredderCore.fbx")
    mu.export_unity_fbx(fbx_path, all_objs, skeletal=True)
    save_blend_cache("SM_ShredderCore")
    tris = sum(len(o.data.polygons) for o in all_objs if o.type == "MESH")
    return {"asset": "SM_ShredderCore.fbx", "tris_total": tris, "clips": [c[0].name for c in clips]}


# ---------------------------------------------------------------------------
# 2. SM_PlasmaSawOrb (Static, diameter ~1.00 m, bake_space_transform=True)
# ---------------------------------------------------------------------------
def build_plasma_saw_orb() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Saw blade disc (r = 0.39 m) + 16 angled plasma teeth reaching r_tip = 0.50 m (diameter = 1.00 m!)
    mu.add_tube_ring(bm, r_outer=0.40, r_inner=0.14, height=0.055, segs=32,
                     center=(0, 0, 0.0), chamfer=0.008, zone=0.0, part_hash=0.3)
    mu.add_cutter_teeth_ring(bm, count=16, r_root=0.38, r_tip=0.50,
                             ang_width_root=0.22, ang_width_tip=0.07, height=0.045,
                             z_center=0.0, rake_shift_ang=0.12, bevel=0.005, zone=0.70)
    # Glowing plasma ring & central gyro bearing
    mu.add_tube_ring(bm, r_outer=0.28, r_inner=0.22, height=0.07, segs=24,
                     center=(0, 0, 0.0), chamfer=0.006, zone=1.0, part_hash=0.9)
    mu.add_cylinder(bm, r1=0.15, r2=0.12, depth=0.12, segs=16,
                    center=(0, 0, 0.0), bevel=0.01, zone=0.35, part_hash=0.5)

    mat = mu.make_qa_cycles_material(
        "M_PlasmaSawOrb", base_rgb=(0.20, 0.24, 0.30), armor_rgb=(0.35, 0.40, 0.48),
        accent_rgb=(0.20, 0.88, 1.0), emissive_rgb=(0.15, 0.90, 1.0), emissive_strength=5.5
    )
    ob = mu.finalize_bmesh_to_object("SM_PlasmaSawOrb", bm, mat, ao_max_dist=0.40)
    dims = mu.mesh_true_dims(ob)
    fbx_path = os.path.join(STAGING_MODELS, "SM_PlasmaSawOrb.fbx")
    mu.export_unity_fbx(fbx_path, [ob], skeletal=False)
    save_blend_cache("SM_PlasmaSawOrb")
    return {"asset": "SM_PlasmaSawOrb.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 3. SM_Electromagnet_BH (Skeletal, bake_space_transform=False)
# ---------------------------------------------------------------------------
def build_electromagnet_bh() -> dict:
    sc = mu.reset_scene(fps=30)
    # 0: root, 1: bn_yoke (vertical axis), 2: bn_flux_ring (vertical spin axis)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.25)),
        ("bn_yoke", "root", (0.0, 0.0, 0.15), (0.0, 0.0, 0.75)),
        ("bn_flux_ring", "bn_yoke", (0.0, 0.0, 0.08), (0.0, 0.0, 0.38)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_Electromagnet_BH", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Heavy U-shaped electromagnet yoke (bn_yoke = 1)
    mu.add_box(bm, size=(1.18, 0.34, 0.20), center=(0, 0, 0.86), bevel=0.02, zone=0.0, part_hash=0.2, bone_id=1)
    for sign in (-1.0, 1.0):
        lx = sign * 0.46
        mu.add_box(bm, size=(0.26, 0.30, 0.68), center=(lx, 0, 0.48), bevel=0.018, zone=0.35, part_hash=0.35, bone_id=1)
        # Copper solenoid coils
        for k in range(3):
            mu.add_tube_ring(bm, r_outer=0.23, r_inner=0.14, height=0.12, segs=16,
                             center=(lx, 0, 0.28 + k * 0.17), chamfer=0.01, zone=0.70, part_hash=0.7, bone_id=1)
        # Glowing pole shoe at bottom
        mu.add_cylinder(bm, r1=0.16, r2=0.19, depth=0.10, segs=12,
                        center=(lx, 0, 0.11), bevel=0.01, zone=1.0, part_hash=0.92, bone_id=1)

    # Central spinning gravity flux ring (bn_flux_ring = 2)
    mu.add_tube_ring(bm, r_outer=0.34, r_inner=0.22, height=0.08, segs=20,
                     center=(0, 0, 0.20), chamfer=0.01, zone=1.0, part_hash=0.95, bone_id=2)
    mu.add_cutter_teeth_ring(bm, count=8, r_root=0.32, r_tip=0.41,
                             ang_width_root=0.24, ang_width_tip=0.12, height=0.06,
                             z_center=0.20, rake_shift_ang=0.05, bevel=0.006, zone=0.70, bone_id=2)

    mat = mu.make_qa_cycles_material(
        "M_Electromagnet_BH", base_rgb=(0.17, 0.19, 0.24), armor_rgb=(0.26, 0.29, 0.36),
        accent_rgb=(0.82, 0.42, 0.18), emissive_rgb=(0.52, 0.22, 1.0), emissive_strength=5.5
    )
    ob = mu.finalize_bmesh_to_object("Electromagnet_Mesh", bm, mat, ao_max_dist=0.55,
                                    bone_names=bone_names, rig_obj=rig)

    pb_yoke = rig.pose.bones["bn_yoke"]
    pb_ring = rig.pose.bones["bn_flux_ring"]
    clips = []

    # AN_BH_Deploy (1..13, BEZIER drop-in)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_BH_Deploy", cyclic=False)
    for f, dy in ((1, 0.45), (7, -0.05), (10, 0.02), (13, 0.0)):
        sc.frame_set(f)
        pb_yoke.location = (0.0, dy, 0.0)
        pb_yoke.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 13))

    # AN_BH_Pulse (1..31, 30f seamless loop, LINEAR!)
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
    fbx_path = os.path.join(STAGING_MODELS, "SM_Electromagnet_BH.fbx")
    mu.export_unity_fbx(fbx_path, [rig, ob], skeletal=True)
    save_blend_cache("SM_Electromagnet_BH")
    return {"asset": "SM_Electromagnet_BH.fbx", "tris_total": len(ob.data.polygons), "clips": [c[0].name for c in clips]}


# ---------------------------------------------------------------------------
# 4. SM_RailEmitter_DR (Skeletal, bake_space_transform=False)
# ---------------------------------------------------------------------------
def build_rail_emitter_dr() -> dict:
    sc = mu.reset_scene(fps=30)
    # 0: root, 1: bn_rail_L (-X outward), 2: bn_rail_R (+X outward)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.30)),
        ("bn_rail_L", "root", (-0.14, 0.0, 0.42), (-0.44, 0.0, 0.42)),
        ("bn_rail_R", "root", (0.14, 0.0, 0.42), (0.44, 0.0, 0.42)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_RailEmitter_DR", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Solid lower bedplate & carriage so rail spread never opens empty floor holes (root = 0)
    mu.add_box(bm, size=(0.64, 1.04, 0.16), center=(0, -0.18, 0.14), bevel=0.02, zone=0.0, part_hash=0.18, bone_id=0)
    mu.add_box(bm, size=(0.56, 0.46, 0.30), center=(0, 0.16, 0.30), bevel=0.02, zone=0.0, part_hash=0.2, bone_id=0)
    mu.add_cylinder(bm, r1=0.11, r2=0.11, depth=0.72, segs=12,
                    center=(0, -0.22, 0.38), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.008, zone=1.0, part_hash=0.95, bone_id=0)
    # Left & Right CNC plasma rails (bn_rail_L = 1, bn_rail_R = 2)
    for sign, bid in ((-1.0, 1), (1.0, 2)):
        rx = sign * 0.21
        mu.add_box(bm, size=(0.15, 0.96, 0.22), center=(rx, -0.22, 0.38),
                   bevel=0.015, zone=0.35, part_hash=0.45, bone_id=bid)
        mu.add_box(bm, size=(0.06, 0.88, 0.08), center=(sign * 0.135, -0.24, 0.40),
                   bevel=0.006, zone=0.70, part_hash=0.75, bone_id=bid)

    mat = mu.make_qa_cycles_material(
        "M_RailEmitter_DR", base_rgb=(0.18, 0.22, 0.26), armor_rgb=(0.30, 0.36, 0.42),
        accent_rgb=(1.0, 0.45, 0.12), emissive_rgb=(1.0, 0.25, 0.08), emissive_strength=4.5
    )
    ob = mu.finalize_bmesh_to_object("RailEmitter_Mesh", bm, mat, ao_max_dist=0.50,
                                    bone_names=bone_names, rig_obj=rig)

    pb_l = rig.pose.bones["bn_rail_L"]
    pb_r = rig.pose.bones["bn_rail_R"]
    act, slot, _ = mu.begin_slotted_action(rig, "AN_DR_ChargeFire", cyclic=False)
    # Bone local +Y points outward for both L and R!
    for f, spread in ((1, 0.0), (6, 0.036), (10, -0.008), (16, 0.0)):
        sc.frame_set(f)
        pb_l.location = (0.0, spread, 0.0)
        pb_r.location = (0.0, spread, 0.0)
        pb_l.keyframe_insert("location", frame=f)
        pb_r.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    mu.push_actions_to_nla(rig, [(act, slot, 1, 16)])

    fbx_path = os.path.join(STAGING_MODELS, "SM_RailEmitter_DR.fbx")
    mu.export_unity_fbx(fbx_path, [rig, ob], skeletal=True)
    save_blend_cache("SM_RailEmitter_DR")
    return {"asset": "SM_RailEmitter_DR.fbx", "tris_total": len(ob.data.polygons), "clips": ["AN_DR_ChargeFire"]}


# ---------------------------------------------------------------------------
# 5. SM_GoldenBot (Skeletal, bake_space_transform=False, ~0.82 m)
# ---------------------------------------------------------------------------
def build_golden_bot() -> dict:
    sc = mu.reset_scene(fps=30)
    # 0: root, 1: bn_body (vertical Z), 2: bn_rotor (vertical Z spin)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.20)),
        ("bn_body", "root", (0.0, 0.0, 0.18), (0.0, 0.0, 0.56)),
        ("bn_rotor", "bn_body", (0.0, 0.0, 0.16), (0.0, 0.0, 0.42)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_GoldenBot", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Golden collector dome (bn_body = 1)
    mu.add_cylinder(bm, r1=0.34, r2=0.22, depth=0.22, segs=16, center=(0, 0, 0.42),
                    bevel=0.02, zone=0.70, part_hash=0.6, bone_id=1)
    # Glowing eye visor facing -Y
    mu.add_box(bm, size=(0.24, 0.12, 0.08), center=(0, -0.28, 0.42),
               bevel=0.01, zone=1.0, part_hash=0.95, bone_id=1)
    # Bottom magnetic collector nozzle
    mu.add_tube_ring(bm, r_outer=0.28, r_inner=0.14, height=0.08, segs=16,
                     center=(0, 0, 0.18), chamfer=0.01, zone=1.0, part_hash=0.9, bone_id=1)
    # Shrouded outer ring (bn_body = 1) + inner spinning 3-pod turbine rotor (bn_rotor = 2)
    mu.add_tube_ring(bm, r_outer=0.43, r_inner=0.37, height=0.08, segs=24,
                     center=(0, 0, 0.30), chamfer=0.012, zone=0.35, part_hash=0.4, bone_id=1)
    for i in range(3):
        a = i * (2.0 * math.pi / 3.0)
        mu.add_cylinder(bm, r1=0.065, r2=0.055, depth=0.11, segs=10,
                        center=(math.cos(a) * 0.32, math.sin(a) * 0.32, 0.33),
                        bevel=0.008, zone=0.70, part_hash=0.75, bone_id=2)

    mat = mu.make_qa_cycles_material(
        "M_GoldenBot", base_rgb=(0.22, 0.24, 0.28), armor_rgb=(0.35, 0.38, 0.44),
        accent_rgb=(1.0, 0.76, 0.18), emissive_rgb=(1.0, 0.80, 0.20), emissive_strength=4.0,
        metallic=0.95, roughness=0.20
    )
    ob = mu.finalize_bmesh_to_object("GoldenBot_Mesh", bm, mat, ao_max_dist=0.45,
                                    bone_names=bone_names, rig_obj=rig)

    pb_body = rig.pose.bones["bn_body"]
    pb_rot = rig.pose.bones["bn_rotor"]
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Bot_Hover", cyclic=True)
    for idx in range(10):
        f = 1 + idx * 5  # 1, 6, ..., 46
        frac = idx / 9.0
        sc.frame_set(f)
        pb_rot.rotation_euler = (0.0, 2.0 * math.pi * frac, 0.0)
        pb_body.location = (0.0, 0.020 * math.sin(2.0 * math.pi * frac), 0.0)
        pb_rot.keyframe_insert("rotation_euler", frame=f)
        pb_body.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    mu.push_actions_to_nla(rig, [(act, slot, 1, 46)])

    fbx_path = os.path.join(STAGING_MODELS, "SM_GoldenBot.fbx")
    mu.export_unity_fbx(fbx_path, [rig, ob], skeletal=True)
    save_blend_cache("SM_GoldenBot")
    return {"asset": "SM_GoldenBot.fbx", "tris_total": len(ob.data.polygons), "clips": ["AN_Bot_Hover"]}


# ---------------------------------------------------------------------------
# 6. SM_Projectile (Static, facing -Y = Unity +Z, bake_space_transform=True)
# ---------------------------------------------------------------------------
def build_projectile() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Sleek armored kinetic-plasma slug (~0.42 m long)
    mu.add_cylinder(bm, r1=0.075, r2=0.015, depth=0.24, segs=8,
                    center=(0, -0.10, 0.0), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.004, zone=1.0, part_hash=0.9)
    mu.add_cylinder(bm, r1=0.065, r2=0.075, depth=0.18, segs=8,
                    center=(0, 0.11, 0.0), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.005, zone=0.35, part_hash=0.4)

    mat = mu.make_qa_cycles_material(
        "M_Projectile", base_rgb=(0.30, 0.35, 0.42), armor_rgb=(0.55, 0.62, 0.70),
        accent_rgb=(0.95, 0.85, 0.40), emissive_rgb=(0.45, 0.90, 1.0), emissive_strength=6.0
    )
    ob = mu.finalize_bmesh_to_object("SM_Projectile", bm, mat, ao_max_dist=0.25)
    dims = mu.mesh_true_dims(ob)
    fbx_path = os.path.join(STAGING_MODELS, "SM_Projectile.fbx")
    mu.export_unity_fbx(fbx_path, [ob], skeletal=False)
    save_blend_cache("SM_Projectile")
    return {"asset": "SM_Projectile.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


def main():
    os.makedirs(STAGING_MODELS, exist_ok=True)
    summary = [
        build_shredder_core(),
        build_plasma_saw_orb(),
        build_electromagnet_bh(),
        build_rail_emitter_dr(),
        build_golden_bot(),
        build_projectile(),
    ]
    print("=== HERO & UWS SUMMARY ===")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
