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
