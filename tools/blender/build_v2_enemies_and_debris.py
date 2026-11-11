"""Crusher Core V2 Ultra — Procedural Generator for Bestiary, Voronoi Debris & Gold Ingot:
- SM_Enemy_Basic.fbx (~0.70m pressed scrap car-cube with ribs & glowing battery seams)
- SM_Enemy_Fast.fbx (~0.74m turbo monowheel drone with twin nozzles & neon visor)
- SM_Enemy_Tank.fbx (~1.15m armored safe-bulldozer with vault wheel, caterpillar tracks & firebox)
- SM_Enemy_Ranged.fbx (~0.88m tri-legged plasma welder walker with lens & induction coils)
- SM_Enemy_Vampire.fbx (~0.96m leech-drone with 3 copper induction rings & siphon pincer claws)
- SM_Enemy_Boss_ATM.fbx (~2.10m, ~4.8k tris Mega-ATM Vault Leviathan with gold bars behind grates)
- SM_Debris_VoronoiPack.fbx (Debris_Chunk_00..11 with R=1.0 hot inner Voronoi cuts)
- SM_GoldIngot.fbx (detailed stepped mint bullion bar)
Outputs to staging/models_v2/ and build/qa/blend_cache_v2/ without touching Assets/."""
from __future__ import annotations

import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

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
# 1. SM_Enemy_Basic (~0.70m pressed scrap car-cube, ~1,700 tris, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_basic_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # Layered compacted car-scrap block (z = 0.02 .. 0.66m, ~0.70m span)
    mu.add_box(bm, size=(0.60, 0.60, 0.56), center=(0, 0, 0.32), bevel=0.028, zone=0.0, part_hash=0.20)
    # Glowing internal lithium battery seam bands peeking out horizontally & vertically (R = 1.0)
    mu.add_box(bm, size=(0.625, 0.56, 0.045), center=(0, 0, 0.32), bevel=0.006, zone=1.0, part_hash=0.94)
    mu.add_box(bm, size=(0.56, 0.625, 0.045), center=(0, 0, 0.20), bevel=0.006, zone=1.0, part_hash=0.92)

    # 8 Heavy corner hydraulic press-clamps + hex bolts -> ~0.70m footprint, base at z = 0.0!
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            for zc in (0.085, 0.555):
                mu.add_box(bm, size=(0.17, 0.17, 0.17),
                           center=(sx * 0.265, sy * 0.265, zc),
                           bevel=0.014, zone=0.35, part_hash=0.42)
            mu.add_cylinder(bm, r1=0.024, r2=0.020, depth=0.03, segs=6,
                            center=(sx * 0.265, sy * 0.265, 0.65), bevel=0.0, zone=0.70, part_hash=0.74)

    # Structural stiffening ribs on sides & top
    for k in (-1, 0, 1):
        mu.add_box(bm, size=(0.055, 0.46, 0.05), center=(k * 0.14, 0.0, 0.62),
                   bevel=0.006, zone=0.70 if k == 0 else 0.35, part_hash=0.66)
        for sx in (-1.0, 1.0):
            mu.add_box(bm, size=(0.045, 0.055, 0.40), center=(sx * 0.31, k * 0.14, 0.33),
                       bevel=0.006, zone=0.35, part_hash=0.48)

    # Crushed car radiator grille + twin cracked headlights facing -Y (Unity +Z)
    mu.add_box(bm, size=(0.38, 0.06, 0.22), center=(0, -0.305, 0.36), bevel=0.010, zone=0.35, part_hash=0.52)
    for g in range(-2, 3):
        mu.add_box(bm, size=(0.03, 0.075, 0.16), center=(g * 0.065, -0.315, 0.36),
                   bevel=0.003, zone=0.70, part_hash=0.72)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.055, r2=0.048, depth=0.05, segs=10,
                        center=(sx * 0.20, -0.31, 0.46), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.005, zone=1.0, part_hash=0.96)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Basic", base_rgb=(0.22, 0.36, 0.45), armor_rgb=(0.28, 0.42, 0.52),
        accent_rgb=(0.90, 0.54, 0.20), emissive_rgb=(0.25, 0.95, 0.88), emissive_strength=4.2,
        metallic=0.76, roughness=0.38,
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Basic", bm, mat, ao_max_dist=0.40)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Basic.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Enemy_Basic")
    return {"asset": "SM_Enemy_Basic.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 2. SM_Enemy_Fast (~0.74m turbo monowheel drone with twin nozzles & neon visor)
# ---------------------------------------------------------------------------
def build_enemy_fast_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # Central Oversized Serrated Circular Monowheel (r = 0.33m, center z = 0.34m -> touches z = 0.0!)
    mu.add_tube_ring(bm, r_outer=0.32, r_inner=0.19, height=0.14, segs=24,
                     center=(0, 0.0, 0.34), rot_euler=(0, math.pi * 0.5, 0),
                     chamfer=0.012, zone=0.35, part_hash=0.32)
    # Glowing inner gyro rim
    mu.add_tube_ring(bm, r_outer=0.20, r_inner=0.13, height=0.15, segs=20,
                     center=(0, 0.0, 0.34), rot_euler=(0, math.pi * 0.5, 0),
                     chamfer=0.008, zone=1.0, part_hash=0.92)
    # 14 High-grip monowheel tread claws around Y-Z wheel circumference
    for i in range(14):
        a = i * (2.0 * math.pi / 14.0)
        wy = math.cos(a) * 0.325
        wz = 0.34 + math.sin(a) * 0.325
        mu.add_box(bm, size=(0.15, 0.055, 0.038), center=(0, wy, wz),
                   rot_euler=(a, 0, 0), bevel=0.005, zone=0.70, part_hash=0.68)

    # Twin Flanking Forward-Swept Aero Cowlings + Side Winglets
    for sx in (-1.0, 1.0):
        mu.add_box(bm, size=(0.11, 0.56, 0.24), center=(sx * 0.14, -0.02, 0.36),
                   rot_euler=(0.16, 0, 0), bevel=0.016, zone=0.0, part_hash=0.24)
        mu.add_cylinder(bm, r1=0.09, r2=0.09, depth=0.06, segs=12,
                        center=(sx * 0.20, 0.0, 0.34), rot_euler=(0, math.pi * 0.5, 0),
                        bevel=0.008, zone=0.70, part_hash=0.74)
        # Swept speed winglet
        mu.add_box(bm, size=(0.16, 0.30, 0.045), center=(sx * 0.24, 0.06, 0.36),
                   rot_euler=(0.12, sx * -0.22, sx * 0.28), bevel=0.006, zone=0.70, part_hash=0.70)

    # Aggressive Forward Aero Nose Hood + Neon Visor (-Y)
    mu.add_box(bm, size=(0.32, 0.26, 0.16), center=(0, -0.24, 0.44),
               rot_euler=(0.28, 0, 0), bevel=0.015, zone=0.0, part_hash=0.30)
    mu.add_box(bm, size=(0.28, 0.07, 0.075), center=(0, -0.35, 0.42),
               rot_euler=(0.22, 0, 0), bevel=0.008, zone=1.0, part_hash=0.96)

    # Twin Rear Afterburner Thruster Nozzles (+Y)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.095, r2=0.072, depth=0.18, segs=12,
                        center=(sx * 0.12, 0.28, 0.40), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.008, zone=0.35, part_hash=0.52)
        mu.add_cylinder(bm, r1=0.065, r2=0.055, depth=0.05, segs=10,
                        center=(sx * 0.12, 0.365, 0.40), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.004, zone=1.0, part_hash=0.98)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Fast", base_rgb=(0.94, 0.70, 0.05), armor_rgb=(0.15, 0.17, 0.21),
        accent_rgb=(1.0, 0.84, 0.14), emissive_rgb=(0.40, 1.0, 0.20), emissive_strength=5.0,
        metallic=0.82, roughness=0.25,
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Fast", bm, mat, ao_max_dist=0.36)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Fast.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Enemy_Fast")
    return {"asset": "SM_Enemy_Fast.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 3. SM_Enemy_Tank (~1.15m armored safe-bulldozer with vault wheel, tracks & firebox)
# ---------------------------------------------------------------------------
def build_enemy_tank_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # Twin Heavy Caterpillar Track Pods (base cleanly at z = 0.0, width span ~1.14m)
    for sx in (-1.0, 1.0):
        mu.add_tread_track_pod(
            bm, center=(sx * 0.44, 0.0, 0.16),
            length=0.96, height=0.32, width=0.24, wheel_count=3, cleat_count=6,
        )
        # Armored track fender skirt with rivets
        mu.add_box(bm, size=(0.27, 0.98, 0.07), center=(sx * 0.44, 0.0, 0.34),
                   bevel=0.010, zone=0.35, part_hash=0.38)

    # Monolithic Cast-Iron Vault Superstructure (z = 0.20 .. 1.06m)
    mu.add_box(bm, size=(0.82, 0.86, 0.84), center=(0, 0.02, 0.62), bevel=0.032, zone=0.0, part_hash=0.20)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            mu.add_box(bm, size=(0.18, 0.18, 0.86), center=(sx * 0.36, 0.02 + sy * 0.37, 0.62),
                       bevel=0.015, zone=0.35, part_hash=0.40)

    # Sloped Front Siege Dozer Blade (-Y) + 6 Carbide Ripper Teeth
    mu.add_box(bm, size=(1.12, 0.20, 0.28), center=(0, -0.50, 0.18),
               rot_euler=(0.30, 0, 0), bevel=0.016, zone=0.35, part_hash=0.46)
    for i in range(6):
        tx = -0.45 + i * 0.18
        mu.add_box(bm, size=(0.075, 0.14, 0.11), center=(tx, -0.59, 0.08),
                   rot_euler=(0.35, 0, 0), bevel=0.008, zone=0.70, part_hash=0.72)

    # Recessed Circular Bank Vault Door (-Y) + Hinges + 6-Spoke Brass Locking Wheel
    mu.add_cylinder(bm, r1=0.29, r2=0.27, depth=0.08, segs=20,
                    center=(0, -0.42, 0.66), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.012, zone=0.35, part_hash=0.54)
    mu.add_tube_ring(bm, r_outer=0.19, r_inner=0.145, height=0.045, segs=18,
                     center=(0, -0.475, 0.66), rot_euler=(math.pi * 0.5, 0, 0),
                     chamfer=0.007, zone=0.70, part_hash=0.78)
    for k in range(3):
        mu.add_box(bm, size=(0.35, 0.035, 0.038), center=(0, -0.475, 0.66),
                   rot_euler=(0, k * (math.pi / 3.0), 0), bevel=0.004, zone=0.70, part_hash=0.80)

    # Rear Glowing Furnace Firebox Grate (+Y) + Twin Exhaust Smokestacks + Top Beacon (z_top ~1.15m)
    mu.add_box(bm, size=(0.52, 0.07, 0.34), center=(0, 0.45, 0.56), bevel=0.008, zone=1.0, part_hash=0.95)
    for g in range(-2, 3):
        mu.add_box(bm, size=(0.035, 0.09, 0.36), center=(g * 0.10, 0.465, 0.56),
                   bevel=0.004, zone=0.35, part_hash=0.48)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.075, r2=0.065, depth=0.28, segs=10,
                        center=(sx * 0.24, 0.34, 1.02), bevel=0.008, zone=0.35, part_hash=0.50)
    mu.add_cylinder(bm, r1=0.13, r2=0.095, depth=0.11, segs=12,
                    center=(0, -0.04, 1.09), bevel=0.010, zone=1.0, part_hash=0.94)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Tank", base_rgb=(0.15, 0.17, 0.22), armor_rgb=(0.24, 0.28, 0.34),
        accent_rgb=(1.0, 0.44, 0.08), emissive_rgb=(1.0, 0.36, 0.06), emissive_strength=4.8,
        metallic=0.90, roughness=0.38,
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Tank", bm, mat, ao_max_dist=0.52)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Tank.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Enemy_Tank")
    return {"asset": "SM_Enemy_Tank.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 4. SM_Enemy_Ranged (~0.88m tri-legged plasma welder walker with lens & coils)
# ---------------------------------------------------------------------------
def build_enemy_ranged_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # Pelvis turntable hub
    mu.add_cylinder(bm, r1=0.22, r2=0.19, depth=0.14, segs=14, center=(0, 0, 0.28),
                    bevel=0.012, zone=0.35, part_hash=0.30)

    # 3 Articulated Industrial Tripod Walker Legs (hip -> thigh -> knee joint -> shin -> footpad at z=0)
    for i in range(3):
        a = i * (2.0 * math.pi / 3.0) + math.pi / 6.0
        ca, sa = math.cos(a), math.sin(a)
        rot_z = a + math.pi * 0.5
        # Upper thigh strut + hydraulic cylinder
        mu.add_box(bm, size=(0.095, 0.24, 0.085),
                   center=(ca * 0.25, sa * 0.25, 0.26),
                   rot_euler=(-0.25 * ca * 0.0 + 0.25, 0, rot_z), bevel=0.008, zone=0.0, part_hash=0.36)
        # Knee drum joint
        mu.add_cylinder(bm, r1=0.065, r2=0.065, depth=0.11, segs=10,
                        center=(ca * 0.35, sa * 0.35, 0.21),
                        rot_euler=(0, math.pi * 0.5, a), bevel=0.006, zone=0.70, part_hash=0.72)
        # Lower shin leg + planted footpad (z = 0.0 .. 0.06m)
        mu.add_box(bm, size=(0.08, 0.10, 0.21),
                   center=(ca * 0.39, sa * 0.39, 0.115),
                   rot_euler=(0, 0, rot_z), bevel=0.008, zone=0.35, part_hash=0.44)
        mu.add_cylinder(bm, r1=0.095, r2=0.075, depth=0.05, segs=8,
                        center=(ca * 0.40, sa * 0.40, 0.025), bevel=0.006, zone=0.70, part_hash=0.68)

    # Upper Plasma Welder Turret Body (z = 0.35 .. 0.72m)
    mu.add_box(bm, size=(0.36, 0.42, 0.30), center=(0, 0.02, 0.50), bevel=0.020, zone=0.0, part_hash=0.25)
    # Forward Focusing Plasma Lens Barrel (-Y) with brass collet rings & glowing plasma aperture
    mu.add_cylinder(bm, r1=0.12, r2=0.10, depth=0.28, segs=16,
                    center=(0, -0.26, 0.50), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.010, zone=0.35, part_hash=0.52)
    for yc in (-0.20, -0.32):
        mu.add_tube_ring(bm, r_outer=0.138, r_inner=0.095, height=0.036, segs=16,
                         center=(0, yc, 0.50), rot_euler=(math.pi * 0.5, 0, 0),
                         chamfer=0.006, zone=0.70, part_hash=0.76)
    mu.add_cylinder(bm, r1=0.082, r2=0.075, depth=0.04, segs=14,
                    center=(0, -0.395, 0.50), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.005, zone=1.0, part_hash=0.98)

    # Side Twin Ribbed Copper Induction Coils + Top Arc-Welder Electrodes (reaches z ~0.86m)
    for sx in (-1.0, 1.0):
        for kz in range(3):
            mu.add_tube_ring(bm, r_outer=0.095, r_inner=0.05, height=0.045, segs=12,
                             center=(sx * 0.23, 0.04, 0.42 + kz * 0.08),
                             rot_euler=(0, math.pi * 0.5, 0), chamfer=0.005, zone=0.70, part_hash=0.74)
        # Top Tesla arc horn + glowing plasma sphere
        mu.add_cylinder(bm, r1=0.035, r2=0.025, depth=0.18, segs=8,
                        center=(sx * 0.11, 0.0, 0.72), rot_euler=(0, sx * 0.28, 0),
                        bevel=0.004, zone=0.70, part_hash=0.78)
        mu.add_cylinder(bm, r1=0.062, r2=0.050, depth=0.09, segs=12,
                        center=(sx * 0.145, 0.0, 0.82), bevel=0.014, zone=1.0, part_hash=0.95)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Ranged", base_rgb=(0.14, 0.35, 0.84), armor_rgb=(0.20, 0.25, 0.32),
        accent_rgb=(0.88, 0.62, 0.24), emissive_rgb=(0.30, 0.90, 1.0), emissive_strength=5.8,
        metallic=0.74, roughness=0.25,
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Ranged", bm, mat, ao_max_dist=0.44)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Ranged.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Enemy_Ranged")
    return {"asset": "SM_Enemy_Ranged.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 5. SM_Enemy_Vampire (~0.96m leech-drone with 3 copper induction rings & siphon claws)
# ---------------------------------------------------------------------------
def build_enemy_vampire_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()

    # Segmented Predatory Leech Carapace aligned along Y (facing -Y)
    mu.add_cylinder(bm, r1=0.30, r2=0.22, depth=0.24, segs=8, center=(0, -0.04, 0.20),
                    bevel=0.020, zone=0.0, part_hash=0.24)
    mu.add_box(bm, size=(0.38, 0.56, 0.18), center=(0, 0.12, 0.20),
               bevel=0.018, zone=0.35, part_hash=0.36)

    # 3 Concentric Copper Induction Rings + Glowing Crimson Energy Cores along spine!
    for idx_r, (yr, rr) in enumerate(((-0.06, 0.25), (0.12, 0.22), (0.29, 0.185))):
        mu.add_tube_ring(bm, r_outer=rr, r_inner=rr - 0.055, height=0.055, segs=18,
                         center=(0, yr, 0.22), rot_euler=(math.pi * 0.5, 0, 0),
                         chamfer=0.008, zone=0.70, part_hash=0.72 + 0.04 * idx_r)
        mu.add_cylinder(bm, r1=rr - 0.05, r2=rr - 0.05, depth=0.035, segs=14,
                        center=(0, yr, 0.22), rot_euler=(math.pi * 0.5, 0, 0),
                        bevel=0.004, zone=1.0, part_hash=0.94)

    # Articulated Twin Front Siphon Pincer Mandibles (-Y) + Central Glowing Proboscis
    mu.add_cylinder(bm, r1=0.095, r2=0.055, depth=0.34, segs=12,
                    center=(0, -0.32, 0.18), rot_euler=(math.pi * 0.5, 0, 0),
                    bevel=0.008, zone=0.70, part_hash=0.76)
    mu.add_tube_ring(bm, r_outer=0.09, r_inner=0.032, height=0.05, segs=14,
                     center=(0, -0.48, 0.18), rot_euler=(math.pi * 0.5, 0, 0),
                     chamfer=0.006, zone=1.0, part_hash=0.98)
    for sx in (-1.0, 1.0):
        # Outer pincer arm
        mu.add_box(bm, size=(0.085, 0.28, 0.085), center=(sx * 0.24, -0.26, 0.17),
                   rot_euler=(0, 0, sx * -0.32), bevel=0.010, zone=0.35, part_hash=0.48)
        # Inward-hooked copper claw tip
        mu.add_box(bm, size=(0.065, 0.20, 0.07), center=(sx * 0.17, -0.43, 0.17),
                   rot_euler=(0, 0, sx * 0.48), bevel=0.008, zone=0.70, part_hash=0.80)
        # Swept lateral hover stabilizer fins (~0.95m wingspan)
        mu.add_box(bm, size=(0.24, 0.28, 0.055), center=(sx * 0.36, 0.04, 0.11),
                   rot_euler=(0.10, sx * 0.22, sx * -0.35), bevel=0.008, zone=0.35, part_hash=0.42)
        mu.add_box(bm, size=(0.16, 0.20, 0.05), center=(sx * 0.28, 0.28, 0.09),
                   rot_euler=(-0.08, sx * 0.20, sx * -0.45), bevel=0.006, zone=0.35, part_hash=0.45)

    # Dorsal crimson siphon heart crystal
    mu.add_cylinder(bm, r1=0.14, r2=0.09, depth=0.11, segs=10, center=(0, -0.04, 0.36),
                    bevel=0.012, zone=1.0, part_hash=0.96)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Vampire", base_rgb=(0.14, 0.12, 0.16), armor_rgb=(0.56, 0.08, 0.18),
        accent_rgb=(0.90, 0.48, 0.20), emissive_rgb=(1.0, 0.08, 0.28), emissive_strength=6.2,
        metallic=0.86, roughness=0.25,
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Vampire", bm, mat, ao_max_dist=0.46)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Vampire.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_Enemy_Vampire")
    return {"asset": "SM_Enemy_Vampire.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 6. SM_Enemy_Boss_ATM (~2.10m, ~4,800 tris Mega-ATM Vault Leviathan, Skeletal!)
# ---------------------------------------------------------------------------
def build_enemy_boss_atm_v2() -> dict:
    sc = mu.reset_scene(fps=30)
    # EXACT V1 / #30 bone hierarchy & axes:
    # 0: root, 1: bn_hull (forward -Y), 2: bn_rollers (axle +X), 3: bn_jaw (hinge +X)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.35)),
        ("bn_hull", "root", (0.0, 0.0, 0.30), (0.0, -0.65, 0.30)),
        ("bn_rollers", "bn_hull", (-0.50, 0.0, 0.28), (0.50, 0.0, 0.28)),
        ("bn_jaw", "bn_hull", (-0.35, -0.56, 0.78), (0.35, -0.56, 0.78)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_Boss_ATM", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()

    # --- A. Heavy Dual-Drum Crusher Rollers (bn_rollers = 2, r = 0.28m -> touches z = 0.0!) ---
    mu.add_cylinder(bm, r1=0.275, r2=0.275, depth=1.52, segs=20,
                    center=(0, 0.0, 0.28), rot_euler=(0, math.pi * 0.5, 0),
                    bevel=0.018, zone=0.35, part_hash=0.30, bone_id=2)
    for k in range(12):
        a = k * (2.0 * math.pi / 12.0)
        mu.add_box(bm, size=(1.54, 0.065, 0.055),
                   center=(0, math.cos(a) * 0.27, 0.28 + math.sin(a) * 0.27),
                   rot_euler=(a, 0, 0), bevel=0.006, zone=0.70, part_hash=0.55, bone_id=2)
    for sx in (-1.0, 1.0):
        mu.add_tube_ring(bm, r_outer=0.22, r_inner=0.11, height=0.07, segs=12,
                         center=(sx * 0.77, 0.0, 0.28), rot_euler=(0, math.pi * 0.5, 0),
                         chamfer=0.008, zone=0.70, part_hash=0.72, bone_id=2)

    # --- B. Towering Titanium Mega-ATM / Vault Fortress Hull (bn_hull = 1, height ~2.10m!) ---
    mu.add_box(bm, size=(1.46, 1.18, 1.52), center=(0, 0.05, 1.12),
               bevel=0.035, zone=0.0, part_hash=0.20, bone_id=1)
    # Armored overhanging crown penthouse -> top reaches ~2.10m!
    mu.add_box(bm, size=(1.58, 1.28, 0.24), center=(0, -0.02, 1.88),
               bevel=0.026, zone=0.35, part_hash=0.38, bone_id=1)
    # Vertical corner H-beam armor pillars + shoulder pauldrons
    for sx in (-1.0, 1.0):
        mu.add_ibeam(bm, length=1.46, depth=0.20, flange_w=0.18, web_t=0.04, flange_t=0.04,
                     center=(sx * 0.74, -0.46, 1.12), bevel=0.006, zone=0.35, bone_id=1)
        mu.add_box(bm, size=(0.18, 0.88, 0.34), center=(sx * 0.78, 0.04, 1.62),
                   bevel=0.018, zone=0.35, part_hash=0.44, bone_id=1)

    # Bulldozer Siege Ram Plow (-Y) + 7 Carbide Ripper Spikes
    mu.add_box(bm, size=(1.60, 0.36, 0.32), center=(0, -0.60, 0.38),
               rot_euler=(0.24, 0, 0), bevel=0.020, zone=0.35, part_hash=0.46, bone_id=1)
    for i in range(7):
        tx = -0.63 + i * 0.21
        mu.add_box(bm, size=(0.10, 0.20, 0.14), center=(tx, -0.79, 0.25),
                   rot_euler=(0.30, 0, 0), bevel=0.008, zone=0.70, part_hash=0.70, bone_id=1)

    # Angry Glowing Cyber-ATM Screen + Armored Brow + Front Gold-Bullion Treasury Window (-Y)
    mu.add_box(bm, size=(1.02, 0.09, 0.36), center=(0, -0.56, 1.54),
               bevel=0.015, zone=1.0, part_hash=0.96, bone_id=1)
    mu.add_box(bm, size=(1.12, 0.16, 0.10), center=(0, -0.61, 1.75),
               rot_euler=(0.20, 0, 0), bevel=0.010, zone=0.70, part_hash=0.76, bone_id=1)
    # Front Chest Armored Gold-Bullion Window (-Y, z = 1.04..1.30m) behind 5 vertical security bars!
    mu.add_box(bm, size=(0.94, 0.06, 0.26), center=(0, -0.55, 1.16),
               bevel=0.006, zone=1.0, part_hash=0.94, bone_id=1)
    for sx in (-1.0, 1.0):
        for rz in (1.10, 1.22):
            mu.add_box(bm, size=(0.36, 0.11, 0.09), center=(sx * 0.22, -0.58, rz),
                       bevel=0.008, zone=0.70, part_hash=0.86, bone_id=1)
    for fb in range(-2, 3):
        mu.add_cylinder(bm, r1=0.022, r2=0.022, depth=0.30, segs=8,
                        center=(fb * 0.20, -0.64, 1.16), bevel=0.003, zone=0.35, part_hash=0.50, bone_id=1)

    # VISIBLE GOLD BULLION BARS BEHIND ARMORED SECURITY GRATES (Left, Right & Rear Vault Bay!)
    # 1) Rear vault treasury bay (+Y) with glowing furnace backdrop + 6 gold bullion bars + 5 vertical prison bars
    mu.add_box(bm, size=(1.04, 0.08, 0.86), center=(0, 0.64, 1.18),
               bevel=0.010, zone=1.0, part_hash=0.92, bone_id=1)
    for r_row in (0.92, 1.18, 1.44):
        for sx in (-1.0, 1.0):
            mu.add_box(bm, size=(0.38, 0.16, 0.16), center=(sx * 0.24, 0.69, r_row),
                       bevel=0.012, zone=0.70, part_hash=0.84, bone_id=1)
    for gb in range(-2, 3):
        mu.add_cylinder(bm, r1=0.026, r2=0.026, depth=0.92, segs=8,
                        center=(gb * 0.22, 0.78, 1.18), bevel=0.004, zone=0.35, part_hash=0.50, bone_id=1)
    # 2) Side bullion windows (Left & Right X) with gold bars + horizontal cage bars
    for sx in (-1.0, 1.0):
        for r_row in (0.98, 1.24):
            mu.add_box(bm, size=(0.12, 0.46, 0.15), center=(sx * 0.74, 0.10, r_row),
                       bevel=0.010, zone=0.70, part_hash=0.85, bone_id=1)
        for zb in (0.90, 1.11, 1.32):
            mu.add_box(bm, size=(0.05, 0.56, 0.04), center=(sx * 0.81, 0.10, zb),
                       bevel=0.004, zone=0.35, part_hash=0.52, bone_id=1)

    # Roof Emergency Beacon Tower + Twin Rear Exhaust Stacks (reaches exact z = 2.10m!)
    mu.add_cylinder(bm, r1=0.22, r2=0.18, depth=0.10, segs=12,
                    center=(0, -0.06, 2.02), bevel=0.010, zone=0.70, part_hash=0.78, bone_id=1)
    mu.add_cylinder(bm, r1=0.14, r2=0.11, depth=0.12, segs=12,
                    center=(0, -0.06, 2.06), bevel=0.012, zone=1.0, part_hash=0.98, bone_id=1)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.10, r2=0.085, depth=0.22, segs=10,
                        center=(sx * 0.52, 0.36, 2.00), bevel=0.008, zone=0.35, part_hash=0.58, bone_id=1)

    # --- C. Articulated Hydraulic Cash-Slot Crusher Jaw (bn_jaw = 3) ---
    mu.add_box(bm, size=(1.10, 0.26, 0.24), center=(0, -0.62, 0.78),
               bevel=0.015, zone=0.70, part_hash=0.76, bone_id=3)
    mu.add_box(bm, size=(0.88, 0.08, 0.14), center=(0, -0.54, 0.92),
               bevel=0.006, zone=1.0, part_hash=0.94, bone_id=3)
    for i in range(6):
        mu.add_box(bm, size=(0.095, 0.14, 0.11), center=(-0.425 + i * 0.17, -0.75, 0.84),
                   bevel=0.008, zone=0.70, part_hash=0.82, bone_id=3)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Boss_ATM", base_rgb=(0.25, 0.29, 0.36), armor_rgb=(0.17, 0.20, 0.26),
        accent_rgb=(1.0, 0.78, 0.18), emissive_rgb=(1.0, 0.18, 0.16), emissive_strength=5.8,
        metallic=0.93, roughness=0.26,
    )
    ob = mu.finalize_bmesh_to_object("Boss_ATM_Mesh", bm, mat, ao_max_dist=0.80,
                                    bone_names=bone_names, rig_obj=rig)
    dims = mu.mesh_true_dims(ob)

    pb_hull = rig.pose.bones["bn_hull"]
    pb_roll = rig.pose.bones["bn_rollers"]
    pb_jaw = rig.pose.bones["bn_jaw"]
    clips = []

    # 1) AN_Boss_Roll (frames 1..25, 24f seamless loop, LINEAR!)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Boss_Roll", cyclic=True)
    for idx in range(7):
        f = 1 + idx * 4
        frac = idx / 6.0
        sc.frame_set(f)
        pb_roll.rotation_euler = (0.0, -2.0 * math.pi * frac, 0.0)
        pb_jaw.rotation_euler = (0.0, 0.22 * (0.5 - 0.5 * math.cos(2.0 * math.pi * frac)), 0.0)
        pb_hull.location = (0.0, 0.0, 0.025 * math.sin(2.0 * math.pi * frac))
        pb_roll.keyframe_insert("rotation_euler", frame=f)
        pb_jaw.keyframe_insert("rotation_euler", frame=f)
        pb_hull.keyframe_insert("location", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="LINEAR")
    clips.append((act, slot, 1, 25))

    # 2) AN_Boss_Ram (frames 1..25, BEZIER weighty siege slam)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Boss_Ram", cyclic=False)
    for f, fwd_y, jaw_a in ((1, 0.0, 0.0), (7, -0.055, 0.36), (12, 0.115, -0.06), (18, 0.035, 0.08), (25, 0.0, 0.0)):
        sc.frame_set(f)
        pb_hull.location = (0.0, fwd_y, 0.0)
        pb_jaw.rotation_euler = (0.0, jaw_a, 0.0)
        pb_hull.keyframe_insert("location", frame=f)
        pb_jaw.keyframe_insert("rotation_euler", frame=f)
    mu.finalize_slotted_action(act, slot, interpolation="BEZIER")
    clips.append((act, slot, 1, 25))

    mu.push_actions_to_nla(rig, clips)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Enemy_Boss_ATM.fbx"), [rig, ob], skeletal=True)
    save_blend_cache_v2("SM_Enemy_Boss_ATM")
    return {"asset": "SM_Enemy_Boss_ATM.fbx", "tris_total": len(ob.data.polygons), "dims": dims, "clips": [c[0].name for c in clips]}


# ---------------------------------------------------------------------------
# 7. SM_Debris_VoronoiPack (12 Detailed Voronoi Chunks with ribs/bolts & R=1.0 inner cuts)
# ---------------------------------------------------------------------------
def build_voronoi_debris_pack_v2() -> dict:
    mu.reset_scene(fps=30)
    rng = random.Random(20261111)

    seeds: list[Vector] = []
    attempts = 0
    while len(seeds) < 12 and attempts < 5000:
        attempts += 1
        cand = Vector((rng.uniform(-0.28, 0.28), rng.uniform(-0.28, 0.28), rng.uniform(-0.24, 0.24)))
        if all((cand - s).length > 0.21 for s in seeds):
            seeds.append(cand)
    while len(seeds) < 12:
        i = len(seeds)
        seeds.append(Vector((((i % 3) - 1) * 0.22, (((i // 3) % 2) - 0.5) * 0.26, ((i // 6) - 0.5) * 0.22)))

    mat = mu.make_qa_cycles_material(
        "M_Debris_Voronoi", base_rgb=(0.22, 0.26, 0.32), armor_rgb=(0.34, 0.40, 0.48),
        accent_rgb=(0.94, 0.58, 0.14), emissive_rgb=(1.0, 0.30, 0.03), emissive_strength=2.6,
    )

    chunk_objs = []
    for i, seed_i in enumerate(seeds):
        bm = bmesh.new()
        zone_layer, hash_layer, _ = mu.ensure_layers(bm)
        # Detailed ribbed industrial armor blank before Voronoi slicing
        if i % 2 == 0:
            mu.add_box(bm, size=(0.78, 0.78, 0.66), center=(0, 0, 0), bevel=0.035,
                       zone=0.0 if i % 4 == 0 else 0.35, part_hash=0.20 + 0.05 * i)
            for rb in (-0.22, 0.0, 0.22):
                mu.add_box(bm, size=(0.82, 0.08, 0.69), center=(0, rb, 0), bevel=0.012,
                           zone=0.70 if rb == 0.0 else 0.35, part_hash=0.68)
        else:
            mu.add_cylinder(bm, r1=0.44, r2=0.40, depth=0.66, segs=16, center=(0, 0, 0),
                            bevel=0.028, zone=0.35, part_hash=0.25 + 0.05 * i)
            mu.add_tube_ring(bm, r_outer=0.47, r_inner=0.36, height=0.10, segs=16,
                             center=(0, 0, 0.08), chamfer=0.012, zone=0.70, part_hash=0.74)

        for j, seed_j in enumerate(seeds):
            if i == j:
                continue
            diff = seed_j - seed_i
            if diff.length < 1e-5:
                continue
            mid = (seed_i + seed_j) * 0.5
            plane_no = diff.normalized()

            geom_all = bm.verts[:] + bm.edges[:] + bm.faces[:]
            if not geom_all:
                break
            res = bmesh.ops.bisect_plane(
                bm,
                geom=geom_all,
                dist=1e-5,
                plane_co=mid,
                plane_no=plane_no,
                clear_outer=True,
                clear_inner=False,
            )
            cut_edges = [
                g for g in res.get("geom_cut", [])
                if isinstance(g, bmesh.types.BMEdge) and g.is_valid and g.is_boundary
            ]
            if cut_edges:
                fill_res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=cut_edges)
                zone_layer, hash_layer, _ = mu.ensure_layers(bm)
                for g in fill_res.get("geom", []):
                    if isinstance(g, bmesh.types.BMFace) and g.is_valid:
                        g[zone_layer] = 1.0  # R = 1.0 hot inner fracture glow!
                        g[hash_layer] = 0.92

        zone_layer, hash_layer, _ = mu.ensure_layers(bm)
        for f in bm.faces:
            for j, seed_j in enumerate(seeds):
                if i == j:
                    continue
                mid = (seed_i + seed_j) * 0.5
                plane_no = (seed_j - seed_i).normalized()
                if abs((f.calc_center_median() - mid).dot(plane_no)) < 2e-3 and f.normal.dot(plane_no) > 0.95:
                    f[zone_layer] = 1.0

        if len(bm.verts) >= 4:
            centroid = sum((v.co for v in bm.verts), Vector((0.0, 0.0, 0.0))) / len(bm.verts)
            bmesh.ops.translate(bm, verts=bm.verts[:], vec=-centroid)

        ob_chunk = mu.finalize_bmesh_to_object(f"Debris_Chunk_{i:02d}", bm, mat, ao_max_dist=0.28)
        chunk_objs.append(ob_chunk)

    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_Debris_VoronoiPack.fbx"), chunk_objs, skeletal=False)

    for i, ob_c in enumerate(chunk_objs):
        a = 2.0 * math.pi * i / len(chunk_objs)
        r = 0.44 if i % 2 == 0 else 0.70
        ob_c.location = (math.cos(a) * r, math.sin(a) * r, 0.20 + (i % 3) * 0.12)
        ob_c.rotation_euler = (0.4 * i, 0.7 * i, a)
    save_blend_cache_v2("SM_Debris_VoronoiPack")

    total_tris = sum(len(o.data.polygons) for o in chunk_objs)
    return {"asset": "SM_Debris_VoronoiPack.fbx", "chunks": len(chunk_objs), "tris_total": total_tris}


# ---------------------------------------------------------------------------
# 8. SM_GoldIngot (~550 tris, stepped treasury bullion bar)
# ---------------------------------------------------------------------------
def build_gold_ingot_v2() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    zone_layer, hash_layer, _ = mu.ensure_layers(bm)

    # Lower beveled trapezoidal bullion base
    bx0, by0, bz0 = 0.19, 0.108, -0.058
    bx1, by1, bz1 = 0.145, 0.074, 0.048
    v_bot = [
        bm.verts.new((-bx0, -by0, bz0)),
        bm.verts.new((bx0, -by0, bz0)),
        bm.verts.new((bx0, by0, bz0)),
        bm.verts.new((-bx0, by0, bz0)),
    ]
    v_top = [
        bm.verts.new((-bx1, -by1, bz1)),
        bm.verts.new((bx1, -by1, bz1)),
        bm.verts.new((bx1, by1, bz1)),
        bm.verts.new((-bx1, by1, bz1)),
    ]
    faces = [
        bm.faces.new((v_bot[0], v_bot[3], v_bot[2], v_bot[1])),
        bm.faces.new((v_top[0], v_top[1], v_top[2], v_top[3])),
    ]
    for k in range(4):
        kn = (k + 1) % 4
        faces.append(bm.faces.new((v_bot[k], v_bot[kn], v_top[kn], v_top[k])))
    for f in faces:
        f[zone_layer] = 0.70
        f[hash_layer] = 0.80

    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.014, segments=2, affect="EDGES", profile=0.5, clamp_overlap=True)
    zone_layer, hash_layer, _ = mu.ensure_layers(bm)
    for f in bm.faces:
        f[zone_layer] = 0.70
        f[hash_layer] = 0.82

    # Stepped treasury seal rim + 3 glowing mint assay bars + 4 corner rivets
    mu.add_box(bm, size=(0.24, 0.11, 0.016), center=(0, 0, 0.052), bevel=0.005, zone=0.70, part_hash=0.86)
    for kb in (-1, 0, 1):
        mu.add_box(bm, size=(0.048, 0.072, 0.016), center=(kb * 0.065, 0, 0.062),
                   bevel=0.004, zone=1.0, part_hash=0.96)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            mu.add_cylinder(bm, r1=0.012, r2=0.010, depth=0.012, segs=6,
                            center=(sx * 0.105, sy * 0.042, 0.060), bevel=0.0, zone=0.70, part_hash=0.88)

    mat = mu.make_qa_cycles_material(
        "M_GoldIngot", base_rgb=(0.95, 0.72, 0.14), armor_rgb=(1.0, 0.78, 0.20),
        accent_rgb=(1.0, 0.85, 0.28), emissive_rgb=(1.0, 0.90, 0.38), emissive_strength=4.0,
        metallic=0.98, roughness=0.14,
    )
    ob = mu.finalize_bmesh_to_object("SM_GoldIngot", bm, mat, ao_max_dist=0.26)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_V2, "SM_GoldIngot.fbx"), [ob], skeletal=False)
    save_blend_cache_v2("SM_GoldIngot")
    return {"asset": "SM_GoldIngot.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


def main():
    os.makedirs(STAGING_V2, exist_ok=True)
    summary = [
        build_enemy_basic_v2(),
        build_enemy_fast_v2(),
        build_enemy_tank_v2(),
        build_enemy_ranged_v2(),
        build_enemy_vampire_v2(),
        build_enemy_boss_atm_v2(),
        build_voronoi_debris_pack_v2(),
        build_gold_ingot_v2(),
    ]
    print("=== V2 ENEMIES & DEBRIS SUMMARY ===")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
