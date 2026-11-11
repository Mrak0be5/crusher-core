"""Procedural hard-surface bmesh builder, COLOR_0 baker (Zone/Curvature/BVH-AO/Hash),
Blender 5.2 Slotted Actions + NLA helper, and Unity FBX exporter."""
from __future__ import annotations

import math
import os

import bmesh
import bpy
import numpy as np
from bpy_extras import anim_utils
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree


def reset_scene(fps: int = 30) -> bpy.types.Scene:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = fps
    sc.render.fps_base = 1.0
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    return sc


def ensure_layers(bm: bmesh.types.BMesh):
    zone_layer = bm.faces.layers.float.get("zone_id") or bm.faces.layers.float.new("zone_id")
    hash_layer = bm.faces.layers.float.get("part_hash") or bm.faces.layers.float.new("part_hash")
    bone_layer = bm.verts.layers.int.get("bone_id") or bm.verts.layers.int.new("bone_id")
    return zone_layer, hash_layer, bone_layer


def make_xform(center=(0.0, 0.0, 0.0), rot_euler=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0)) -> Matrix:
    m_loc = Matrix.Translation(Vector(center))
    m_rot = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
    m_sca = Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    return m_loc @ m_rot @ m_sca


def _merge_sub_bm(dst_bm: bmesh.types.BMesh, sub_bm: bmesh.types.BMesh,
                  mat_xform: Matrix | None, zone: float, part_hash: float, bone_id: int):
    """Transforms isolated sub_bm and appends its vertices/faces cleanly into dst_bm."""
    if mat_xform is not None:
        bmesh.ops.transform(sub_bm, matrix=mat_xform, verts=sub_bm.verts[:])
    zone_layer, hash_layer, bone_layer = ensure_layers(dst_bm)
    v_map = {}
    for sv in sub_bm.verts:
        dv = dst_bm.verts.new(sv.co)
        dv[bone_layer] = int(bone_id)
        v_map[sv] = dv
    for sf in sub_bm.faces:
        try:
            df = dst_bm.faces.new([v_map[sv] for sv in sf.verts])
            df[zone_layer] = float(zone)
            df[hash_layer] = float(part_hash)
        except ValueError:
            pass
    sub_bm.free()


def add_box(bm: bmesh.types.BMesh, size=(1.0, 1.0, 1.0), center=(0.0, 0.0, 0.0),
            rot_euler=(0.0, 0.0, 0.0), bevel: float = 0.015, bevel_segs: int = 1,
            zone: float = 0.0, part_hash: float = 0.25, bone_id: int = 0):
    sub = bmesh.new()
    bmesh.ops.create_cube(sub, size=1.0)
    bmesh.ops.scale(sub, vec=Vector(size), verts=sub.verts[:])
    if bevel > 0.0:
        bmesh.ops.bevel(sub, geom=sub.edges[:], offset=bevel, segments=bevel_segs,
                        affect="EDGES", profile=0.5, clamp_overlap=True)
    _merge_sub_bm(bm, sub, make_xform(center, rot_euler), zone, part_hash, bone_id)


def add_cylinder(bm: bmesh.types.BMesh, r1: float, r2: float, depth: float, segs: int = 16,
                 center=(0.0, 0.0, 0.0), rot_euler=(0.0, 0.0, 0.0), bevel: float = 0.012,
                 zone: float = 0.0, part_hash: float = 0.4, bone_id: int = 0):
    sub = bmesh.new()
    bmesh.ops.create_cone(sub, cap_ends=True, cap_tris=False, segments=segs,
                          radius1=r1, radius2=r2, depth=depth)
    if bevel > 0.0:
        cap_edges = [e for e in sub.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > 0.65]
        if cap_edges:
            bmesh.ops.bevel(sub, geom=cap_edges, offset=bevel, segments=1,
                            affect="EDGES", profile=0.5, clamp_overlap=True)
    _merge_sub_bm(bm, sub, make_xform(center, rot_euler), zone, part_hash, bone_id)


def add_tube_ring(bm: bmesh.types.BMesh, r_outer: float, r_inner: float, height: float,
                  segs: int = 24, center=(0.0, 0.0, 0.0), rot_euler=(0.0, 0.0, 0.0),
                  chamfer: float = 0.012, zone: float = 0.0, part_hash: float = 0.5, bone_id: int = 0):
    sub = bmesh.new()
    hz = height * 0.5
    c = min(chamfer, (r_outer - r_inner) * 0.35, hz * 0.4)
    if c > 1e-4:
        prof = [
            (r_inner, -hz + c),
            (r_inner + c, -hz),
            (r_outer - c, -hz),
            (r_outer, -hz + c),
            (r_outer, hz - c),
            (r_outer - c, hz),
            (r_inner + c, hz),
            (r_inner, hz - c),
        ]
    else:
        prof = [(r_inner, -hz), (r_outer, -hz), (r_outer, hz), (r_inner, hz)]

    n_p = len(prof)
    rings = []
    for i in range(segs):
        ang = 2.0 * math.pi * i / segs
        ca, sa = math.cos(ang), math.sin(ang)
        rings.append([sub.verts.new((r * ca, r * sa, z)) for (r, z) in prof])

    for i in range(segs):
        r_curr = rings[i]
        r_next = rings[(i + 1) % segs]
        for j in range(n_p):
            jn = (j + 1) % n_p
            sub.faces.new((r_curr[j], r_next[j], r_next[jn], r_curr[jn]))

    _merge_sub_bm(bm, sub, make_xform(center, rot_euler), zone, part_hash, bone_id)


def add_arc_plate(bm: bmesh.types.BMesh, r_inner: float, r_outer: float, height: float,
                  ang_start: float, ang_end: float, segs: int = 6, z_center: float = 0.0,
                  bevel: float = 0.01, zone: float = 0.35, part_hash: float = 0.6, bone_id: int = 0):
    sub = bmesh.new()
    hz = height * 0.5
    cols = []
    for i in range(segs + 1):
        t = i / segs
        a = ang_start + (ang_end - ang_start) * t
        ca, sa = math.cos(a), math.sin(a)
        cols.append([
            sub.verts.new((r_inner * ca, r_inner * sa, z_center - hz)),
            sub.verts.new((r_outer * ca, r_outer * sa, z_center - hz)),
            sub.verts.new((r_outer * ca, r_outer * sa, z_center + hz)),
            sub.verts.new((r_inner * ca, r_inner * sa, z_center + hz)),
        ])
    for i in range(segs):
        c0, c1 = cols[i], cols[i + 1]
        for j in range(4):
            jn = (j + 1) % 4
            sub.faces.new((c0[j], c1[j], c1[jn], c0[jn]))
    sub.faces.new((cols[0][0], cols[0][3], cols[0][2], cols[0][1]))
    sub.faces.new((cols[-1][0], cols[-1][1], cols[-1][2], cols[-1][3]))

    if bevel > 0.0:
        sharp_edges = [e for e in sub.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > 0.75]
        if sharp_edges:
            bmesh.ops.bevel(sub, geom=sharp_edges, offset=bevel, segments=1,
                            affect="EDGES", profile=0.5, clamp_overlap=True)
    _merge_sub_bm(bm, sub, None, zone, part_hash, bone_id)


def add_cutter_teeth_ring(bm: bmesh.types.BMesh, count: int, r_root: float, r_tip: float,
                          ang_width_root: float, ang_width_tip: float, height: float,
                          z_center: float = 0.0, rake_shift_ang: float = 0.08,
                          twist_ang: float = 0.0, phase_offset: float = 0.0,
                          bevel: float = 0.006, zone: float = 0.70, bone_id: int = 0):
    hz = height * 0.5
    for i in range(count):
        sub = bmesh.new()
        base_a = phase_offset + (2.0 * math.pi * i) / count
        verts_tooth = []
        for z_idx, z_val in enumerate((z_center - hz, z_center + hz)):
            tw = (-0.5 + z_idx) * twist_ang
            a_r0 = base_a - ang_width_root * 0.5 + tw
            a_r1 = base_a + ang_width_root * 0.5 + tw
            a_t0 = base_a - ang_width_tip * 0.5 + rake_shift_ang + tw
            a_t1 = base_a + ang_width_tip * 0.5 + rake_shift_ang + tw
            verts_tooth.append([
                sub.verts.new((r_root * math.cos(a_r0), r_root * math.sin(a_r0), z_val)),
                sub.verts.new((r_tip * math.cos(a_t0), r_tip * math.sin(a_t0), z_val)),
                sub.verts.new((r_tip * math.cos(a_t1), r_tip * math.sin(a_t1), z_val)),
                sub.verts.new((r_root * math.cos(a_r1), r_root * math.sin(a_r1), z_val)),
            ])
        b_ring, t_ring = verts_tooth[0], verts_tooth[1]
        sub.faces.new((b_ring[0], b_ring[3], b_ring[2], b_ring[1]))
        sub.faces.new((t_ring[0], t_ring[1], t_ring[2], t_ring[3]))
        for j in range(4):
            jn = (j + 1) % 4
            sub.faces.new((b_ring[j], t_ring[j], t_ring[jn], b_ring[jn]))
        if bevel > 0.0:
            bmesh.ops.bevel(sub, geom=sub.edges[:], offset=bevel, segments=1,
                            affect="EDGES", profile=0.5, clamp_overlap=True)
        p_hash = 0.15 + 0.75 * ((i * 7) % count) / max(1, count)
        _merge_sub_bm(bm, sub, None, zone, p_hash, bone_id)


def add_helical_hook_teeth_ring(
    bm: bmesh.types.BMesh,
    count: int,
    r_root: float,
    r_tip: float,
    ang_width_root: float,
    ang_width_tip: float,
    height: float,
    z_center: float = 0.0,
    rake_dir: float = 1.0,
    hook_ang: float = 0.14,
    twist_ang: float = 0.12,
    phase_offset: float = 0.0,
    z_slices: int = 4,
    bevel: float = 0.005,
    zone: float = 0.70,
    bone_id: int = 0,
):
    """Builds multi-slice helical hooked car-shredder claw blades with raked gullets."""
    hz = height * 0.5
    r_mid = r_root + (r_tip - r_root) * 0.58
    s_dir = 1.0 if rake_dir >= 0.0 else -1.0
    nz = max(2, z_slices)

    for i in range(count):
        sub = bmesh.new()
        base_a = phase_offset + (2.0 * math.pi * i) / count
        rings = []
        for sz in range(nz):
            tz = sz / (nz - 1)
            zv = z_center - hz + height * tz
            tw = (tz - 0.5) * twist_ang
            taper = 1.0 - 0.10 * abs(tz - 0.5) * 2.0
            rt = r_root + (r_tip - r_root) * taper

            # 6-point hooked claw cross-section (CCW in XY)
            a0 = base_a - ang_width_root * 0.52 + tw
            a1 = base_a - ang_width_tip * 0.45 + s_dir * hook_ang * 0.55 + tw
            a2 = base_a + s_dir * hook_ang * 1.15 - ang_width_tip * 0.15 + tw
            a3 = base_a + s_dir * hook_ang * 1.15 + ang_width_tip * 0.55 + tw
            a4 = base_a + ang_width_root * 0.28 + s_dir * hook_ang * 0.35 + tw
            a5 = base_a + ang_width_root * 0.52 + tw

            prof = [
                (r_root, a0),
                (r_mid, a1),
                (rt, a2),
                (rt * 0.985, a3),
                (r_mid * 0.96, a4),
                (r_root, a5),
            ]
            if s_dir < 0.0:
                # Keep consistent outward face winding regardless of rake sign
                prof = [
                    (r_root, base_a - ang_width_root * 0.52 + tw),
                    (r_mid * 0.96, base_a - ang_width_root * 0.28 + s_dir * hook_ang * 0.35 + tw),
                    (rt * 0.985, base_a + s_dir * hook_ang * 1.15 - ang_width_tip * 0.55 + tw),
                    (rt, base_a + s_dir * hook_ang * 1.15 + ang_width_tip * 0.15 + tw),
                    (r_mid, base_a + ang_width_tip * 0.45 + s_dir * hook_ang * 0.55 + tw),
                    (r_root, base_a + ang_width_root * 0.52 + tw),
                ]
            rings.append([sub.verts.new((rr * math.cos(aa), rr * math.sin(aa), zv)) for (rr, aa) in prof])

        n_p = len(rings[0])
        for sz in range(nz - 1):
            r0, r1 = rings[sz], rings[sz + 1]
            for j in range(n_p):
                jn = (j + 1) % n_p
                sub.faces.new((r0[j], r0[jn], r1[jn], r1[j]))
        sub.faces.new(tuple(reversed(rings[0])))
        sub.faces.new(tuple(rings[-1]))
        bmesh.ops.recalc_face_normals(sub, faces=sub.faces[:])

        if bevel > 0.0:
            sharp = [e for e in sub.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > 0.55]
            if sharp:
                bmesh.ops.bevel(sub, geom=sharp, offset=bevel, segments=1,
                                affect="EDGES", profile=0.5, clamp_overlap=True)
        p_hash = 0.12 + 0.80 * ((i * 11) % count) / max(1, count)
        _merge_sub_bm(bm, sub, None, zone, p_hash, bone_id)


def add_bolt_ring(
    bm: bmesh.types.BMesh,
    count: int,
    radius: float,
    z_center: float = 0.0,
    bolt_r: float = 0.020,
    bolt_h: float = 0.020,
    phase_offset: float = 0.0,
    radial: bool = False,
    zone: float = 0.70,
    bone_id: int = 0,
):
    """Adds a ring of 6-sided hex flange bolts (either vertical +Z or radially outward)."""
    for i in range(count):
        a = phase_offset + (2.0 * math.pi * i) / count
        cx, cy = math.cos(a) * radius, math.sin(a) * radius
        rot = (math.pi * 0.5, 0.0, a - math.pi * 0.5) if radial else (0.0, 0.0, a)
        add_cylinder(
            bm, r1=bolt_r, r2=bolt_r * 0.86, depth=bolt_h, segs=6,
            center=(cx, cy, z_center), rot_euler=rot, bevel=0.0,
            zone=zone, part_hash=0.25 + 0.5 * ((i * 5) % max(1, count)) / max(1, count),
            bone_id=bone_id,
        )


def add_ibeam(
    bm: bmesh.types.BMesh,
    length: float,
    depth: float = 0.28,
    flange_w: float = 0.22,
    web_t: float = 0.045,
    flange_t: float = 0.045,
    center=(0.0, 0.0, 0.0),
    rot_euler=(0.0, 0.0, 0.0),
    bevel: float = 0.006,
    zone: float = 0.35,
    part_hash: float = 0.42,
    bone_id: int = 0,
):
    """Builds a structural H/I-beam aligned along local Z, transformed to world space."""
    sub = bmesh.new()
    # Central web
    add_box(sub, size=(web_t, depth - flange_t * 1.2, length), center=(0.0, 0.0, 0.0),
            bevel=bevel, zone=zone, part_hash=part_hash, bone_id=bone_id)
    # Front & back flanges
    fy = (depth - flange_t) * 0.5
    for sy in (-1.0, 1.0):
        add_box(sub, size=(flange_w, flange_t, length), center=(0.0, sy * fy, 0.0),
                bevel=bevel, zone=zone, part_hash=part_hash + 0.07, bone_id=bone_id)
    _merge_sub_bm(bm, sub, make_xform(center, rot_euler), zone, part_hash, bone_id)


def add_corrugated_cable(
    bm: bmesh.types.BMesh,
    points: list[tuple[float, float, float]],
    radius: float = 0.11,
    segs: int = 8,
    collar_every: int = 2,
    zone: float = 0.35,
    collar_zone: float = 0.70,
    bone_id: int = 0,
):
    """Sweeps a multi-segment armored power cable along 3D polyline points with clamping collars."""
    if len(points) < 2:
        return
    pts = [Vector(p) for p in points]
    sub = bmesh.new()
    rings = []
    for idx, p in enumerate(pts):
        if idx == 0:
            t_dir = (pts[1] - pts[0]).normalized()
        elif idx == len(pts) - 1:
            t_dir = (pts[-1] - pts[-2]).normalized()
        else:
            t_dir = ((pts[idx] - pts[idx - 1]).normalized() + (pts[idx + 1] - pts[idx]).normalized()).normalized()
        if t_dir.length_squared < 1e-6:
            t_dir = Vector((0.0, 0.0, 1.0))
        up = Vector((0.0, 0.0, 1.0)) if abs(t_dir.z) < 0.92 else Vector((1.0, 0.0, 0.0))
        n_vec = t_dir.cross(up).normalized()
        b_vec = t_dir.cross(n_vec).normalized()
        ring = []
        for s in range(segs):
            ang = 2.0 * math.pi * s / segs
            off = n_vec * (math.cos(ang) * radius) + b_vec * (math.sin(ang) * radius)
            ring.append(sub.verts.new(p + off))
        rings.append((ring, p, t_dir))

    for i in range(len(rings) - 1):
        r0 = rings[i][0]
        r1 = rings[i + 1][0]
        for s in range(segs):
            sn = (s + 1) % segs
            sub.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
    sub.faces.new(tuple(reversed(rings[0][0])))
    sub.faces.new(tuple(rings[-1][0]))
    bmesh.ops.recalc_face_normals(sub, faces=sub.faces[:])
    _merge_sub_bm(bm, sub, None, zone, 0.38, bone_id)

    if collar_every > 0:
        for idx in range(1, len(rings) - 1):
            if idx % collar_every == 0:
                _, p_c, t_dir = rings[idx]
                q = t_dir.to_track_quat("Z", "Y").to_euler()
                add_cylinder(
                    bm, r1=radius * 1.24, r2=radius * 1.24, depth=radius * 0.75, segs=segs,
                    center=(p_c.x, p_c.y, p_c.z), rot_euler=(q.x, q.y, q.z),
                    bevel=radius * 0.08, zone=collar_zone, part_hash=0.72, bone_id=bone_id,
                )


def add_tread_track_pod(
    bm: bmesh.types.BMesh,
    center: tuple[float, float, float],
    length: float,
    height: float,
    width: float,
    wheel_count: int = 4,
    cleat_count: int = 10,
    bone_id: int = 0,
):
    """Builds a detailed armored caterpillar track pod (wheels, frame, top/bottom cleats)."""
    cx, cy, cz = center
    r_end = height * 0.5
    straight = max(0.05, length - height)
    # Inner armored bogie beam
    add_box(bm, size=(width * 0.72, straight + r_end * 0.9, height * 0.56),
            center=(cx, cy, cz), bevel=0.012, zone=0.0, part_hash=0.28, bone_id=bone_id)
    # Road wheels & drive sprockets along Y
    for w in range(wheel_count):
        ty = -0.5 + w / max(1, wheel_count - 1) if wheel_count > 1 else 0.0
        wy = cy + ty * straight
        add_cylinder(bm, r1=r_end * 0.84, r2=r_end * 0.84, depth=width * 0.92, segs=12,
                     center=(cx, wy, cz), rot_euler=(0.0, math.pi * 0.5, 0.0),
                     bevel=0.008, zone=0.35, part_hash=0.45 + 0.06 * w, bone_id=bone_id)
        add_cylinder(bm, r1=r_end * 0.36, r2=r_end * 0.36, depth=width * 1.04, segs=8,
                     center=(cx, wy, cz), rot_euler=(0.0, math.pi * 0.5, 0.0),
                     bevel=0.005, zone=0.70, part_hash=0.74, bone_id=bone_id)
    # Top & bottom track belts
    for sz in (-1.0, 1.0):
        add_box(bm, size=(width, straight, height * 0.11),
                center=(cx, cy, cz + sz * (r_end - height * 0.055)),
                bevel=0.006, zone=0.35, part_hash=0.36, bone_id=bone_id)
    # Individual tread cleats along top, bottom, and front/rear arcs
    n_lin = max(2, cleat_count // 2)
    for k in range(n_lin):
        yk = cy - straight * 0.45 + straight * 0.90 * (k / max(1, n_lin - 1))
        for sz in (-1.0, 1.0):
            add_box(bm, size=(width * 1.05, straight * 0.55 / n_lin, height * 0.08),
                    center=(cx, yk, cz + sz * (r_end - height * 0.02)),
                    bevel=0.004, zone=0.70, part_hash=0.66, bone_id=bone_id)
    for sy in (-1.0, 1.0):
        ey = cy + sy * straight * 0.5
        for a_deg in (-55.0, 0.0, 55.0):
            ar = math.radians(a_deg)
            py = ey + sy * math.cos(ar) * (r_end * 0.94)
            pz = cz + math.sin(ar) * (r_end * 0.94)
            add_box(bm, size=(width * 1.05, height * 0.12, height * 0.08),
                    center=(cx, py, pz), rot_euler=(-sy * (math.pi * 0.5 - ar), 0.0, 0.0),
                    bevel=0.004, zone=0.70, part_hash=0.68, bone_id=bone_id)


def mesh_true_dims(ob: bpy.types.Object) -> list[float]:
    me = ob.data
    if len(me.vertices) == 0:
        return [0.0, 0.0, 0.0]
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    span = co.max(axis=0) - co.min(axis=0)
    return [round(float(x), 4) for x in span]


def _fibonacci_hemisphere(n: int = 16) -> list[Vector]:
    dirs = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(n):
        z = (i + 0.5) / n
        r = math.sqrt(max(0.0, 1.0 - z * z))
        phi = i * golden
        dirs.append(Vector((r * math.cos(phi), r * math.sin(phi), z)).normalized())
    return dirs


_HEMI_DIRS = _fibonacci_hemisphere(16)


def make_qa_cycles_material(mat_name: str, base_rgb=(0.24, 0.28, 0.33),
                            armor_rgb=(0.16, 0.19, 0.23), accent_rgb=(0.95, 0.58, 0.12),
                            emissive_rgb=(1.0, 0.38, 0.05), emissive_strength: float = 4.5,
                            metallic: float = 0.88, roughness: float = 0.34,
                            rusty_wear: bool = False) -> bpy.types.Material:
    old = bpy.data.materials.get(mat_name)
    if old:
        bpy.data.materials.remove(old)
    mat = bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")

    vcol = nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "COLOR_0"

    sep = nodes.new("ShaderNodeSeparateColor")
    sep.mode = "RGB"
    links.new(vcol.outputs["Color"], sep.inputs["Color"])

    ramp = nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = "CONSTANT"
    cr.elements[0].position = 0.0
    cr.elements[0].color = (*base_rgb, 1.0)
    cr.elements[1].position = 0.22
    cr.elements[1].color = (*armor_rgb, 1.0)
    e2 = cr.elements.new(0.55)
    e2.color = (*accent_rgb, 1.0)
    e3 = cr.elements.new(0.88)
    e3.color = (*emissive_rgb, 1.0)
    links.new(sep.outputs["Red"], ramp.inputs["Fac"])

    mix_wear = nodes.new("ShaderNodeMix")
    mix_wear.data_type = "RGBA"
    mix_wear.blend_type = "MIX"
    wear_col = (0.58, 0.24, 0.09, 1.0) if rusty_wear else (0.78, 0.82, 0.88, 1.0)
    mix_wear.inputs[7].default_value = wear_col

    math_wear = nodes.new("ShaderNodeMath")
    math_wear.operation = "MULTIPLY"
    math_wear.inputs[1].default_value = 0.55 if rusty_wear else 0.32
    links.new(sep.outputs["Green"], math_wear.inputs[0])
    links.new(math_wear.outputs[0], mix_wear.inputs[0])
    links.new(ramp.outputs["Color"], mix_wear.inputs[6])

    mix_ao = nodes.new("ShaderNodeMix")
    mix_ao.data_type = "RGBA"
    mix_ao.blend_type = "MULTIPLY"
    mix_ao.inputs[0].default_value = 0.85
    links.new(mix_wear.outputs[2], mix_ao.inputs[6])
    comb_ao = nodes.new("ShaderNodeCombineColor")
    links.new(sep.outputs["Blue"], comb_ao.inputs["Red"])
    links.new(sep.outputs["Blue"], comb_ao.inputs["Green"])
    links.new(sep.outputs["Blue"], comb_ao.inputs["Blue"])
    links.new(comb_ao.outputs["Color"], mix_ao.inputs[7])

    links.new(mix_ao.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness

    gt_em = nodes.new("ShaderNodeMath")
    gt_em.operation = "GREATER_THAN"
    gt_em.inputs[1].default_value = 0.88
    links.new(sep.outputs["Red"], gt_em.inputs[0])

    mul_em = nodes.new("ShaderNodeMath")
    mul_em.operation = "MULTIPLY"
    mul_em.inputs[1].default_value = emissive_strength
    links.new(gt_em.outputs[0], mul_em.inputs[0])

    bsdf.inputs["Emission Color"].default_value = (*emissive_rgb, 1.0)
    links.new(mul_em.outputs[0], bsdf.inputs["Emission Strength"])
    return mat


def finalize_bmesh_to_object(name: str, bm: bmesh.types.BMesh, mat: bpy.types.Material,
                             ao_max_dist: float = 0.55, sharp_deg: float = 42.0,
                             bone_names: list[str] | None = None,
                             rig_obj: bpy.types.Object | None = None) -> bpy.types.Object:
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")

    zone_layer, hash_layer, bone_layer = ensure_layers(bm)
    uv_layer = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")

    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    sharp_rad = math.radians(sharp_deg)
    edge_wear_map: dict[int, float] = {}
    for e in bm.edges:
        if len(e.link_faces) == 2:
            f1, f2 = e.link_faces[0], e.link_faces[1]
            ang = e.calc_face_angle(0.0)
            e.smooth = ang < sharp_rad
            delta = f2.calc_center_median() - f1.calc_center_median()
            is_convex = delta.dot(f1.normal) < -1e-5
            edge_wear_map[e.index] = min(1.0, max(0.12, ang / 1.35)) if is_convex else 0.04
        else:
            e.smooth = False
            edge_wear_map[e.index] = 0.85

    for f in bm.faces:
        f.smooth = True
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for lp in f.loops:
            co = lp.vert.co
            if az >= ax and az >= ay:
                lp[uv_layer].uv = (co.x * 0.5 + 0.5, co.y * 0.5 + 0.5)
            elif ax >= ay:
                lp[uv_layer].uv = (co.y * 0.5 + 0.5, co.z * 0.5 + 0.5)
            else:
                lp[uv_layer].uv = (co.x * 0.5 + 0.5, co.z * 0.5 + 0.5)

    bvh = BVHTree.FromBMesh(bm, epsilon=1e-5)
    vert_ao = [1.0] * len(bm.verts)
    for v in bm.verts:
        vn = v.normal.normalized() if v.normal.length_squared > 1e-8 else Vector((0.0, 0.0, 1.0))
        up = Vector((0.0, 0.0, 1.0)) if abs(vn.z) < 0.9 else Vector((1.0, 0.0, 0.0))
        tan = vn.cross(up).normalized()
        bitan = vn.cross(tan).normalized()
        origin = v.co + vn * 0.006
        occ = 0.0
        for hd in _HEMI_DIRS:
            ray_dir = (tan * hd.x + bitan * hd.y + vn * hd.z).normalized()
            hit_loc, _, _, hit_dist = bvh.ray_cast(origin, ray_dir, ao_max_dist)
            if hit_loc is not None and hit_dist is not None and hit_dist < ao_max_dist:
                occ += (1.0 - hit_dist / ao_max_dist) * 0.88
        vert_ao[v.index] = max(0.14, min(1.0, 1.0 - occ / len(_HEMI_DIRS)))

    loop_colors: list[float] = []
    vert_bone_ids = [v[bone_layer] for v in bm.verts]
    for f in bm.faces:
        z_id = max(0.0, min(1.0, f[zone_layer]))
        p_hash = max(0.0, min(1.0, f[hash_layer]))
        for lp in f.loops:
            w1 = edge_wear_map.get(lp.edge.index, 0.15)
            w2 = edge_wear_map.get(lp.link_loop_prev.edge.index, 0.15)
            curv = max(0.12, max(w1, w2))
            loop_colors.extend((z_id, curv, vert_ao[lp.vert.index], p_hash))

    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()

    col_attr = me.color_attributes.new(name="COLOR_0", type="BYTE_COLOR", domain="CORNER")
    col_attr.data.foreach_set("color", loop_colors)
    me.color_attributes.active_color = col_attr
    me.color_attributes.render_color_index = 0

    me.materials.clear()
    me.materials.append(mat)
    me.validate()
    me.update()

    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.update()

    if bone_names is not None and rig_obj is not None:
        ob.parent = rig_obj
        vgroups = [ob.vertex_groups.new(name=bn) for bn in bone_names]
        for v_idx, b_idx in enumerate(vert_bone_ids):
            clamped = max(0, min(len(vgroups) - 1, b_idx))
            vgroups[clamped].add([v_idx], 1.0, "REPLACE")
        arm_mod = ob.modifiers.new(name="Armature", type="ARMATURE")
        arm_mod.object = rig_obj

    return ob


def create_armature(rig_name: str, bone_specs: list[tuple[str, str | None, tuple[float, float, float], tuple[float, float, float]]],
                    rotation_mode: str = "XYZ") -> bpy.types.Object:
    arm_data = bpy.data.armatures.new(rig_name)
    rig_obj = bpy.data.objects.new(rig_name, arm_data)
    bpy.context.scene.collection.objects.link(rig_obj)

    bpy.context.view_layer.objects.active = rig_obj
    rig_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    ebones = arm_data.edit_bones
    created = {}
    for b_name, p_name, head, tail in bone_specs:
        eb = ebones.new(b_name)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        eb.use_connect = False
        eb.use_deform = True
        if p_name and p_name in created:
            eb.parent = created[p_name]
        created[b_name] = eb
    bpy.ops.object.mode_set(mode="OBJECT")

    for pb in rig_obj.pose.bones:
        pb.rotation_mode = rotation_mode
    return rig_obj


def reset_pose(rig_obj: bpy.types.Object):
    for pb in rig_obj.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def begin_slotted_action(rig_obj: bpy.types.Object, action_name: str, cyclic: bool = False):
    old = bpy.data.actions.get(action_name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(action_name)
    act.use_fake_user = True
    act.use_cyclic = cyclic

    ad = rig_obj.animation_data_create()
    ad.action = act
    slot = next((s for s in act.slots if s.target_id_type == "OBJECT"), None)
    if slot is None:
        slot = act.slots.new(id_type="OBJECT", name=rig_obj.name)
    ad.action_slot = slot
    cb = anim_utils.action_ensure_channelbag_for_slot(act, slot)
    reset_pose(rig_obj)
    return act, slot, cb


def finalize_slotted_action(act: bpy.types.Action, slot, interpolation: str = "LINEAR"):
    cb = anim_utils.action_ensure_channelbag_for_slot(act, slot)
    for fc in cb.fcurves:
        if fc.data_path.endswith(".scale") or fc.data_path == "scale":
            raise AssertionError(f"Forbidden bone scale keyframe in {act.name}: {fc.data_path}")
        for kp in fc.keyframe_points:
            kp.interpolation = interpolation
            if interpolation == "BEZIER":
                kp.handle_left_type = "AUTO_CLAMPED"
                kp.handle_right_type = "AUTO_CLAMPED"
        fc.update()


def count_bone_scale_keys(act: bpy.types.Action) -> int:
    bad = 0
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if fc.data_path.endswith(".scale") or fc.data_path == "scale":
                        bad += len(fc.keyframe_points)
    return bad


def push_actions_to_nla(rig_obj: bpy.types.Object, clip_specs: list[tuple[bpy.types.Action, object, int, int]]):
    ad = rig_obj.animation_data_create()
    reset_pose(rig_obj)
    ad.action = None
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)

    for act, slot, f_start, f_end in clip_specs:
        assert count_bone_scale_keys(act) == 0, f"Scale keys detected in {act.name}"
        tr = ad.nla_tracks.new()
        tr.name = act.name
        tr.mute = False
        st = tr.strips.new(act.name, f_start, act)
        st.name = act.name
        st.action_slot = slot
        st.scale = 1.0
        st.action_frame_start = float(f_start)
        st.action_frame_end = float(f_end)
        st.frame_start = float(f_start)
        st.frame_end = float(f_end)
        st.extrapolation = "NOTHING"


def export_unity_fbx(filepath: str, objects: list[bpy.types.Object], skeletal: bool):
    os.makedirs(os.path.dirname(os.path.abspath(filepath)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    kw = dict(
        filepath=os.path.abspath(filepath),
        use_selection=True,
        object_types={"ARMATURE", "MESH"} if skeletal else {"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=(not skeletal),
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_triangles=True,
        use_tspace=True,
        colors_type="LINEAR",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        armature_nodetype="NULL",
        bake_anim=skeletal,
        bake_anim_use_nla_strips=skeletal,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="AUTO",
    )
    res = bpy.ops.export_scene.fbx(**kw)
    if res != {"FINISHED"}:
        raise RuntimeError(f"FBX export failed for {filepath}: {res}")
