"""Builds a cartoon bomb (body, cap, curved wick) and exports it as FBX for Unity.

Run headless:  blender -b --python make_bomb.py -- <output.fbx>

Wick UVs run u=0 at the base to u=1 at the tip, so a shader can clip it away as it burns.
Empties WickPt_00..WickPt_N sit along the wick centreline so Unity can find the burning tip
without reading mesh data at runtime.
"""
import sys
import math
import bpy
import bmesh
from mathutils import Vector

out_path = sys.argv[sys.argv.index("--") + 1]

# Unit bomb: body diameter 1, resting on the origin. Z is up in Blender.
BODY_R = 0.5
CAP_R = 0.16
CAP_H = 0.14
CAP_Z = 2 * BODY_R - 0.03            # slightly embedded in the top of the sphere
WICK_BASE = Vector((0.0, 0.0, CAP_Z + CAP_H * 0.5))
WICK_CTRL = Vector((0.0, 0.0, WICK_BASE.z + 0.5))
WICK_TIP  = Vector((0.42, 0.0, WICK_BASE.z + 0.44))
WICK_R = 0.045
WICK_SEGS = 24
WICK_RING = 8

# ---- scene reset -----------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0


def new_object(name, bm, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    if smooth:
        mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


# ---- body ------------------------------------------------------------------
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=20, radius=BODY_R)
bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, BODY_R))
body = new_object("Body", bm)

# ---- cap -------------------------------------------------------------------
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=24,
                      radius1=CAP_R, radius2=CAP_R, depth=CAP_H)
bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, CAP_Z))
cap = new_object("Cap", bm, smooth=False)
# smooth the side, keep the flat ends crisp
for p in cap.data.polygons:
    p.use_smooth = abs(p.normal.z) < 0.5

# ---- wick: tube swept along a quadratic bezier -----------------------------
def bezier(u):
    v = 1.0 - u
    return v * v * WICK_BASE + 2 * v * u * WICK_CTRL + u * u * WICK_TIP


def tangent(u):
    return (2 * (1 - u) * (WICK_CTRL - WICK_BASE) + 2 * u * (WICK_TIP - WICK_CTRL)).normalized()


bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")
rings = []
centres = []
ref = Vector((0, 1, 0))  # the wick curls in the XZ plane, so Y is always a valid side vector
for i in range(WICK_SEGS + 1):
    u = i / WICK_SEGS
    c = bezier(u)
    t = tangent(u)
    n = ref.cross(t).normalized()
    b = t.cross(n).normalized()
    # taper the last bit so the tip looks frayed rather than sawn off
    r = WICK_R * (1.0 if u < 0.9 else max(0.35, 1.0 - (u - 0.9) * 6.5))
    ring = []
    for k in range(WICK_RING):
        a = 2 * math.pi * k / WICK_RING
        ring.append(bm.verts.new(c + n * (math.cos(a) * r) + b * (math.sin(a) * r)))
    rings.append(ring)
    centres.append(c)
bm.verts.ensure_lookup_table()

for i in range(WICK_SEGS):
    u0 = i / WICK_SEGS
    u1 = (i + 1) / WICK_SEGS
    for k in range(WICK_RING):
        k1 = (k + 1) % WICK_RING
        f = bm.faces.new((rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]))
        v0, v1 = k / WICK_RING, (k + 1) / WICK_RING
        f.loops[0][uv_layer].uv = (u0, v0)
        f.loops[1][uv_layer].uv = (u0, v1)
        f.loops[2][uv_layer].uv = (u1, v1)
        f.loops[3][uv_layer].uv = (u1, v0)

# close the tip
tip_face = bm.faces.new(rings[-1])
for loop in tip_face.loops:
    loop[uv_layer].uv = (1.0, 0.5)
wick = new_object("Wick", bm)

# ---- wick path empties -----------------------------------------------------
path = bpy.data.objects.new("WickPath", None)
scene.collection.objects.link(path)
for i, c in enumerate(centres):
    e = bpy.data.objects.new(f"WickPt_{i:02d}", None)
    e.empty_display_size = 0.02
    e.location = c
    e.parent = path
    scene.collection.objects.link(e)

# ---- export ----------------------------------------------------------------
bpy.ops.export_scene.fbx(
    filepath=out_path,
    use_selection=False,
    object_types={'MESH', 'EMPTY'},
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=True,
    axis_forward='-Z',
    axis_up='Y',
    add_leaf_bones=False,
    mesh_smooth_type='OFF',
    use_mesh_modifiers=True,
    path_mode='STRIP',
)
print("Exported", out_path)
