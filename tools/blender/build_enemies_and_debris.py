"""Procedural generator for 60-min Bestiary (Basic, Fast, Tank, Ranged, Vampire, Boss_ATM),
12 Voronoi fracture chunks (bmesh.ops.bisect_plane with R=1.0 hot inner cuts), and Gold Ingot."""
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
STAGING_MODELS = os.path.join(ROOT_DIR, "staging", "models")
QA_CACHE = os.path.join(ROOT_DIR, "build", "qa", "blend_cache")


def save_blend_cache(name: str):
    os.makedirs(QA_CACHE, exist_ok=True)
    path = os.path.join(QA_CACHE, f"{name}.blend")
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    return path


# ---------------------------------------------------------------------------
# 1. SM_Enemy_Basic (~0.55 m pressed gadget/scrap cube, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_basic() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Core pressed block
    mu.add_box(bm, size=(0.48, 0.48, 0.46), center=(0, 0, 0.26), bevel=0.025, zone=0.0, part_hash=0.2)
    # 8 corner bumper clamps -> total footprint ~0.55 m
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            for sz, zc in ((-1.0, 0.07), (1.0, 0.45)):
                mu.add_box(bm, size=(0.14, 0.14, 0.14),
                           center=(sx * 0.205, sy * 0.205, zc),
                           bevel=0.012, zone=0.35, part_hash=0.45)
    # Front cracked gadget screen (-Y) & top copper PCB strips
    mu.add_box(bm, size=(0.28, 0.05, 0.20), center=(0, -0.235, 0.28), bevel=0.008, zone=1.0, part_hash=0.9)
    for k in (-1, 0, 1):
        mu.add_box(bm, size=(0.05, 0.30, 0.04), center=(k * 0.10, 0.0, 0.50), bevel=0.005, zone=0.70, part_hash=0.7)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Basic", base_rgb=(0.22, 0.35, 0.43), armor_rgb=(0.30, 0.44, 0.52),
        accent_rgb=(0.85, 0.50, 0.22), emissive_rgb=(0.30, 0.92, 0.85), emissive_strength=3.5,
        metallic=0.72, roughness=0.40
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Basic", bm, mat, ao_max_dist=0.35)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Basic.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_Enemy_Basic")
    return {"asset": "SM_Enemy_Basic.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 2. SM_Enemy_Fast (~0.54 m turbo lithium cell facing -Y, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_fast() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Cylindrical battery body along Y
    mu.add_cylinder(bm, r1=0.145, r2=0.145, depth=0.36, segs=14,
                    center=(0, 0.03, 0.19), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.012, zone=0.0, part_hash=0.25)
    # Sharp anode nose cone (-Y)
    mu.add_cylinder(bm, r1=0.035, r2=0.135, depth=0.16, segs=14,
                    center=(0, -0.22, 0.19), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.008, zone=0.70, part_hash=0.65)
    # Hazard bands & side speed fins
    for y_c in (-0.06, 0.10):
        mu.add_tube_ring(bm, r_outer=0.16, r_inner=0.135, height=0.05, segs=14,
                         center=(0, y_c, 0.19), rot_euler=(math.pi / 2.0, 0, 0),
                         chamfer=0.006, zone=0.35, part_hash=0.45)
    for sx in (-1.0, 1.0):
        mu.add_box(bm, size=(0.06, 0.34, 0.08), center=(sx * 0.15, 0.02, 0.04),
                   bevel=0.008, zone=0.35, part_hash=0.5)
    # Rear plasma thruster (+Y)
    mu.add_cylinder(bm, r1=0.11, r2=0.08, depth=0.08, segs=12,
                    center=(0, 0.23, 0.19), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.006, zone=1.0, part_hash=0.95)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Fast", base_rgb=(0.92, 0.68, 0.04), armor_rgb=(0.14, 0.15, 0.18),
        accent_rgb=(1.0, 0.82, 0.12), emissive_rgb=(0.45, 1.0, 0.18), emissive_strength=4.5,
        metallic=0.80, roughness=0.28
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Fast", bm, mat, ao_max_dist=0.32)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Fast.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_Enemy_Fast")
    return {"asset": "SM_Enemy_Fast.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 3. SM_Enemy_Tank (~0.95 m cast-iron armored safe, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_tank() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Heavy vault body
    mu.add_box(bm, size=(0.84, 0.84, 0.84), center=(0, 0, 0.46), bevel=0.03, zone=0.0, part_hash=0.2)
    # 8 riveted corner armor bumpers -> ~0.95 m footprint
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            for zc in (0.11, 0.81):
                mu.add_box(bm, size=(0.22, 0.22, 0.22),
                           center=(sx * 0.365, sy * 0.365, zc),
                           bevel=0.015, zone=0.35, part_hash=0.4)
    # Recessed vault door (-Y) + 4-spoke locking wheel
    mu.add_box(bm, size=(0.58, 0.08, 0.58), center=(0, -0.41, 0.46), bevel=0.015, zone=0.35, part_hash=0.55)
    mu.add_tube_ring(bm, r_outer=0.19, r_inner=0.14, height=0.05, segs=16,
                     center=(0, -0.465, 0.46), rot_euler=(math.pi / 2.0, 0, 0),
                     chamfer=0.008, zone=0.70, part_hash=0.75)
    for k in range(2):
        mu.add_box(bm, size=(0.34, 0.04, 0.045), center=(0, -0.465, 0.46),
                   rot_euler=(0, k * math.pi / 2.0 + math.pi / 4.0, 0),
                   bevel=0.005, zone=0.70, part_hash=0.78)
    # Top hazard beacon
    mu.add_cylinder(bm, r1=0.12, r2=0.09, depth=0.08, segs=10, center=(0, 0, 0.91),
                    bevel=0.008, zone=1.0, part_hash=0.92)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Tank", base_rgb=(0.15, 0.17, 0.21), armor_rgb=(0.24, 0.27, 0.32),
        accent_rgb=(1.0, 0.40, 0.08), emissive_rgb=(1.0, 0.36, 0.06), emissive_strength=4.0,
        metallic=0.88, roughness=0.42
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Tank", bm, mat, ao_max_dist=0.50)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Tank.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_Enemy_Tank")
    return {"asset": "SM_Enemy_Tank.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 4. SM_Enemy_Ranged (~0.68 m sparking HV capacitor tripod, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_ranged() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Tripod base legs
    mu.add_cylinder(bm, r1=0.20, r2=0.18, depth=0.12, segs=12, center=(0, 0, 0.08), bevel=0.01, zone=0.35, part_hash=0.3)
    for i in range(3):
        a = i * (2.0 * math.pi / 3.0) + math.pi / 6.0
        mu.add_box(bm, size=(0.11, 0.28, 0.09),
                   center=(math.cos(a) * 0.22, math.sin(a) * 0.22, 0.045),
                   rot_euler=(0, 0, a + math.pi / 2.0), bevel=0.01, zone=0.35, part_hash=0.35)
    # Ribbed ceramic insulator core
    mu.add_cylinder(bm, r1=0.14, r2=0.13, depth=0.42, segs=14, center=(0, 0, 0.32), bevel=0.01, zone=0.0, part_hash=0.4)
    for k in range(3):
        mu.add_tube_ring(bm, r_outer=0.22 - k * 0.015, r_inner=0.12, height=0.055, segs=16,
                         center=(0, 0, 0.20 + k * 0.11), chamfer=0.01, zone=0.0, part_hash=0.5)
    # Twin Tesla arc horns + glowing electrodes on top
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.035, r2=0.025, depth=0.20, segs=8,
                        center=(sx * 0.10, -0.02, 0.58), rot_euler=(0, sx * 0.32, 0),
                        bevel=0.004, zone=0.70, part_hash=0.75)
        mu.add_cylinder(bm, r1=0.06, r2=0.05, depth=0.09, segs=10,
                        center=(sx * 0.14, -0.02, 0.67), bevel=0.012, zone=1.0, part_hash=0.95)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Ranged", base_rgb=(0.14, 0.34, 0.82), armor_rgb=(0.20, 0.24, 0.30),
        accent_rgb=(0.75, 0.82, 0.92), emissive_rgb=(0.35, 0.86, 1.0), emissive_strength=5.5,
        metallic=0.68, roughness=0.26
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Ranged", bm, mat, ao_max_dist=0.40)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Ranged.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_Enemy_Ranged")
    return {"asset": "SM_Enemy_Ranged.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 5. SM_Enemy_Vampire (~0.85 m crimson energy-leech tick facing -Y, base at z=0)
# ---------------------------------------------------------------------------
def build_enemy_vampire() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    # Low-slung triangular carapace
    mu.add_cylinder(bm, r1=0.32, r2=0.22, depth=0.22, segs=6, center=(0, 0.04, 0.18),
                    bevel=0.02, zone=0.0, part_hash=0.25)
    # 3 magnetic anchor claws -> ~0.85 m span
    for a in (-2.15, 2.15, 0.0):
        ang = a + math.pi / 2.0
        cx, cy = math.cos(ang) * 0.31, math.sin(ang) * 0.31
        mu.add_box(bm, size=(0.12, 0.32, 0.11), center=(cx, cy + 0.04, 0.075),
                   rot_euler=(0.15, 0, ang - math.pi / 2.0), bevel=0.012, zone=0.35, part_hash=0.45)
    # Forward siphon proboscis nozzle (-Y)
    mu.add_cylinder(bm, r1=0.11, r2=0.065, depth=0.30, segs=12,
                    center=(0, -0.28, 0.17), rot_euler=(math.pi / 2.0, 0, 0),
                    bevel=0.008, zone=0.70, part_hash=0.7)
    mu.add_tube_ring(bm, r_outer=0.095, r_inner=0.04, height=0.05, segs=12,
                     center=(0, -0.42, 0.17), rot_euler=(math.pi / 2.0, 0, 0),
                     chamfer=0.006, zone=1.0, part_hash=0.95)
    # Glowing crimson dorsal siphon core
    mu.add_cylinder(bm, r1=0.16, r2=0.12, depth=0.12, segs=10, center=(0, 0.05, 0.32),
                    bevel=0.015, zone=1.0, part_hash=0.92)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Vampire", base_rgb=(0.14, 0.12, 0.15), armor_rgb=(0.55, 0.08, 0.18),
        accent_rgb=(0.88, 0.14, 0.30), emissive_rgb=(1.0, 0.08, 0.28), emissive_strength=6.0,
        metallic=0.84, roughness=0.28
    )
    ob = mu.finalize_bmesh_to_object("SM_Enemy_Vampire", bm, mat, ao_max_dist=0.45)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Vampire.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_Enemy_Vampire")
    return {"asset": "SM_Enemy_Vampire.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


# ---------------------------------------------------------------------------
# 6. SM_Enemy_Boss_ATM (~1.85 m Titanium ATM Bunker Boss, Skeletal!)
# ---------------------------------------------------------------------------
def build_enemy_boss_atm() -> dict:
    sc = mu.reset_scene(fps=30)
    # 0: root, 1: bn_hull (forward -Y), 2: bn_rollers (axle +X), 3: bn_jaw (hinge +X)
    bone_specs = [
        ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.35)),
        ("bn_hull", "root", (0.0, 0.0, 0.30), (0.0, -0.65, 0.30)),
        ("bn_rollers", "bn_hull", (-0.50, 0.0, 0.26), (0.50, 0.0, 0.26)),
        ("bn_jaw", "bn_hull", (-0.35, -0.52, 0.72), (0.35, -0.52, 0.72)),
    ]
    bone_names = [b[0] for b in bone_specs]
    rig = mu.create_armature("RIG_Boss_ATM", bone_specs, rotation_mode="XYZ")

    bm = bmesh.new()
    # Heavy Tracked Rollers (bn_rollers = 2), radius 0.26 m -> touches z = 0.0!
    mu.add_cylinder(bm, r1=0.26, r2=0.26, depth=1.36, segs=16,
                    center=(0, 0.0, 0.26), rot_euler=(0, math.pi / 2.0, 0),
                    bevel=0.018, zone=0.35, part_hash=0.3, bone_id=2)
    for k in range(8):
        a = k * (math.pi / 4.0)
        mu.add_box(bm, size=(1.38, 0.07, 0.06),
                   center=(0, math.cos(a) * 0.25, 0.26 + math.sin(a) * 0.25),
                   rot_euler=(a, 0, 0), bevel=0.008, zone=0.70, part_hash=0.5, bone_id=2)

    # Monolithic Titanium ATM Hull (bn_hull = 1), reaches z = 1.85 m!
    mu.add_box(bm, size=(1.32, 1.06, 1.38), center=(0, 0.04, 1.04),
               bevel=0.035, zone=0.0, part_hash=0.2, bone_id=1)
    # Armored top brow hood -> z up to 1.85 m
    mu.add_box(bm, size=(1.42, 1.16, 0.22), center=(0, -0.02, 1.74),
               bevel=0.025, zone=0.35, part_hash=0.4, bone_id=1)
    # Bulldozer lower ram plow (-Y)
    mu.add_box(bm, size=(1.48, 0.34, 0.30), center=(0, -0.56, 0.36),
               rot_euler=(0.24, 0, 0), bevel=0.02, zone=0.35, part_hash=0.45, bone_id=1)
    for i in range(5):
        tx = -0.52 + i * 0.26
        mu.add_box(bm, size=(0.12, 0.18, 0.14), center=(tx, -0.74, 0.24),
                   rot_euler=(0.3, 0, 0), bevel=0.01, zone=0.70, part_hash=0.7, bone_id=1)
    # Glowing ATM terminal screen (-Y) & twin roof alarm beacons
    mu.add_box(bm, size=(0.88, 0.08, 0.44), center=(0, -0.51, 1.32),
               bevel=0.015, zone=1.0, part_hash=0.95, bone_id=1)
    for sx in (-1.0, 1.0):
        mu.add_cylinder(bm, r1=0.11, r2=0.08, depth=0.12, segs=10,
                        center=(sx * 0.46, -0.10, 1.81), bevel=0.01, zone=1.0, part_hash=0.92, bone_id=1)
    # 4 Golden Bullion Cassettes mounted on rear (+Y)
    for row_z in (0.78, 1.30):
        for sx in (-1.0, 1.0):
            mu.add_box(bm, size=(0.42, 0.22, 0.38), center=(sx * 0.32, 0.62, row_z),
                       bevel=0.018, zone=0.70, part_hash=0.82, bone_id=1)

    # Articulated Cash-Slot Crusher Jaw (bn_jaw = 3)
    mu.add_box(bm, size=(0.98, 0.24, 0.22), center=(0, -0.58, 0.72),
               bevel=0.015, zone=0.70, part_hash=0.75, bone_id=3)
    for i in range(4):
        mu.add_box(bm, size=(0.10, 0.12, 0.09), center=(-0.33 + i * 0.22, -0.70, 0.77),
                   bevel=0.008, zone=0.70, part_hash=0.8, bone_id=3)

    mat = mu.make_qa_cycles_material(
        "M_Enemy_Boss_ATM", base_rgb=(0.26, 0.30, 0.36), armor_rgb=(0.18, 0.21, 0.26),
        accent_rgb=(1.0, 0.76, 0.18), emissive_rgb=(1.0, 0.16, 0.16), emissive_strength=5.5,
        metallic=0.92, roughness=0.28
    )
    ob = mu.finalize_bmesh_to_object("Boss_ATM_Mesh", bm, mat, ao_max_dist=0.75,
                                    bone_names=bone_names, rig_obj=rig)
    dims = mu.mesh_true_dims(ob)

    pb_hull = rig.pose.bones["bn_hull"]
    pb_roll = rig.pose.bones["bn_rollers"]
    pb_jaw = rig.pose.bones["bn_jaw"]
    clips = []

    # 1) AN_Boss_Roll (frames 1..25, 24f seamless loop, LINEAR!)
    act, slot, _ = mu.begin_slotted_action(rig, "AN_Boss_Roll", cyclic=True)
    for idx in range(7):
        f = 1 + idx * 4  # 1, 5, 9, 13, 17, 21, 25
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

    # 2) AN_Boss_Ram (frames 1..25, BEZIER weighty siege slam forward along bn_hull local +Y)
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
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_Enemy_Boss_ATM.fbx"), [rig, ob], skeletal=True)
    save_blend_cache("SM_Enemy_Boss_ATM")
    return {"asset": "SM_Enemy_Boss_ATM.fbx", "tris_total": len(ob.data.polygons), "dims": dims, "clips": [c[0].name for c in clips]}


# ---------------------------------------------------------------------------
# 7. SM_Debris_VoronoiPack (12 Voronoi chunks via bmesh.ops.bisect_plane, R=1.0 on cuts)
# ---------------------------------------------------------------------------
def build_voronoi_debris_pack() -> dict:
    mu.reset_scene(fps=30)
    rng = random.Random(20261110)

    # Generate 12 well-spaced 3D Voronoi seed points inside [-0.30, 0.30]^3
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
        "M_Debris_Voronoi", base_rgb=(0.22, 0.26, 0.32), armor_rgb=(0.34, 0.40, 0.46),
        accent_rgb=(0.92, 0.55, 0.14), emissive_rgb=(1.0, 0.24, 0.02), emissive_strength=1.9
    )

    chunk_objs = []
    for i, seed_i in enumerate(seeds):
        bm = bmesh.new()
        zone_layer, hash_layer, _ = mu.ensure_layers(bm)
        # Base hull before Voronoi slicing (mix of beveled armor block & octagonal cylinder)
        if i % 2 == 0:
            mu.add_box(bm, size=(0.76, 0.76, 0.64), center=(0, 0, 0), bevel=0.03,
                       zone=0.0 if i % 4 == 0 else 0.35, part_hash=0.2 + 0.05 * i)
        else:
            mu.add_cylinder(bm, r1=0.42, r2=0.38, depth=0.64, segs=12, center=(0, 0, 0),
                            bevel=0.025, zone=0.35, part_hash=0.25 + 0.05 * i)

        # Slice against every neighbor j != i using Voronoi bisecting plane
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
                        g[zone_layer] = 1.0  # R = 1.0 on inner Voronoi fracture cut faces!
                        g[hash_layer] = 0.90

        # Ensure any face lying on a Voronoi cut plane has zone_id = 1.0
        zone_layer, hash_layer, _ = mu.ensure_layers(bm)
        for f in bm.faces:
            for j, seed_j in enumerate(seeds):
                if i == j:
                    continue
                mid = (seed_i + seed_j) * 0.5
                plane_no = (seed_j - seed_i).normalized()
                if abs((f.calc_center_median() - mid).dot(plane_no)) < 2e-3 and f.normal.dot(plane_no) > 0.95:
                    f[zone_layer] = 1.0

        # Recenter chunk around its centroid so instanced rotation spins around center of mass!
        if len(bm.verts) >= 4:
            centroid = sum((v.co for v in bm.verts), Vector((0.0, 0.0, 0.0))) / len(bm.verts)
            bmesh.ops.translate(bm, verts=bm.verts[:], vec=-centroid)

        ob_chunk = mu.finalize_bmesh_to_object(f"Debris_Chunk_{i:02d}", bm, mat, ao_max_dist=0.25)
        chunk_objs.append(ob_chunk)

    fbx_path = os.path.join(STAGING_MODELS, "SM_Debris_VoronoiPack.fbx")
    mu.export_unity_fbx(fbx_path, chunk_objs, skeletal=False)

    # Arrange chunks in a neat visual burst ring for the QA contact sheet cache!
    for i, ob_c in enumerate(chunk_objs):
        a = 2.0 * math.pi * i / len(chunk_objs)
        r = 0.42 if i % 2 == 0 else 0.68
        ob_c.location = (math.cos(a) * r, math.sin(a) * r, 0.18 + (i % 3) * 0.12)
        ob_c.rotation_euler = (0.4 * i, 0.7 * i, a)
    save_blend_cache("SM_Debris_VoronoiPack")

    total_tris = sum(len(o.data.polygons) for o in chunk_objs)
    return {"asset": "SM_Debris_VoronoiPack.fbx", "chunks": len(chunk_objs), "tris_total": total_tris}


# ---------------------------------------------------------------------------
# 8. SM_GoldIngot (Faceted trapezoidal gold bullion bar, bake_space_transform=True)
# ---------------------------------------------------------------------------
def build_gold_ingot() -> dict:
    mu.reset_scene(fps=30)
    bm = bmesh.new()
    zone_layer, hash_layer, _ = mu.ensure_layers(bm)

    # Trapezoidal bullion bar (~0.36 x 0.20 x 0.11 m) centered around origin
    bx0, by0, bz0 = 0.18, 0.10, -0.055
    bx1, by1, bz1 = 0.135, 0.068, 0.055
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

    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.012, segments=1, affect="EDGES", profile=0.5, clamp_overlap=True)
    zone_layer, hash_layer, _ = mu.ensure_layers(bm)
    for f in bm.faces:
        f[zone_layer] = 0.70
        f[hash_layer] = 0.82

    # Stamped glowing crest bar on top
    mu.add_box(bm, size=(0.18, 0.075, 0.016), center=(0, 0, 0.058), bevel=0.004, zone=1.0, part_hash=0.95)

    mat = mu.make_qa_cycles_material(
        "M_GoldIngot", base_rgb=(0.95, 0.72, 0.14), armor_rgb=(1.0, 0.78, 0.20),
        accent_rgb=(1.0, 0.84, 0.26), emissive_rgb=(1.0, 0.88, 0.35), emissive_strength=3.5,
        metallic=0.98, roughness=0.16
    )
    ob = mu.finalize_bmesh_to_object("SM_GoldIngot", bm, mat, ao_max_dist=0.25)
    dims = mu.mesh_true_dims(ob)
    mu.export_unity_fbx(os.path.join(STAGING_MODELS, "SM_GoldIngot.fbx"), [ob], skeletal=False)
    save_blend_cache("SM_GoldIngot")
    return {"asset": "SM_GoldIngot.fbx", "tris_total": len(ob.data.polygons), "dims": dims}


def main():
    os.makedirs(STAGING_MODELS, exist_ok=True)
    summary = [
        build_enemy_basic(),
        build_enemy_fast(),
        build_enemy_tank(),
        build_enemy_ranged(),
        build_enemy_vampire(),
        build_enemy_boss_atm(),
        build_voronoi_debris_pack(),
        build_gold_ingot(),
    ]
    print("=== ENEMIES & DEBRIS SUMMARY ===")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
