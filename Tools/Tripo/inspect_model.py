"""Report triangle count, size, materials, textures and animation for a model file.

Run with Blender (no system Python needed):
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup \
      --python Tools/Tripo/inspect_model.py -- Assets/LastLight/Art/Tripo/jeep/LL_Jeep.fbx [target_largest_dim_m]

If target_largest_dim_m is given, it also prints the uniform scale that makes the
largest bounding-box dimension equal that size (use it on the prefab's child mesh).
Read-only: nothing is saved.
"""
import sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not argv:
    print("usage: blender --background --python inspect_model.py -- <model> [target_largest_dim_m]")
    sys.exit(2)
path = argv[0]
target = float(argv[1]) if len(argv) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = path.lower().rsplit(".", 1)[-1]
if ext == "fbx":
    bpy.ops.import_scene.fbx(filepath=path)
elif ext in ("glb", "gltf"):
    bpy.ops.import_scene.gltf(filepath=path)
elif ext == "obj":
    bpy.ops.wm.obj_import(filepath=path)
else:
    print(f"unsupported extension: {ext}")
    sys.exit(2)

deps = bpy.context.evaluated_depsgraph_get()
tris = verts = 0
lo = Vector((1e18, 1e18, 1e18))
hi = Vector((-1e18, -1e18, -1e18))
meshes = []
for ob in bpy.context.scene.objects:
    if ob.type != "MESH":
        continue
    ev = ob.evaluated_get(deps)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    t = len(me.loop_triangles)
    tris += t
    verts += len(me.vertices)
    for v in me.vertices:
        w = ev.matrix_world @ v.co
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
    meshes.append((ob.name, t, [s.material.name for s in ob.material_slots if s.material]))
    ev.to_mesh_clear()

size = hi - lo if meshes else Vector((0, 0, 0))
print("=== inspect_model ===")
print(f"file: {path}")
print(f"meshes: {len(meshes)}  triangles: {tris}  vertices: {verts}")
for name, t, mats in meshes:
    print(f"  mesh {name}: {t} tris, materials {mats}")
print(f"bbox size (Blender X,Y,Z; Z up): {size.x:.4f} x {size.y:.4f} x {size.z:.4f}")
print(f"bbox min z: {lo.z:.4f} (0 means pivot at base)")
for img in bpy.data.images:
    if img.size[0]:
        print(f"  texture {img.name}: {img.size[0]}x{img.size[1]}")
arm = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
print(f"armatures: {len(arm)}" + (f" ({len(arm[0].data.bones)} bones)" if arm else ""))
for act in bpy.data.actions:
    print(f"  action {act.name}: frames {act.frame_range[0]:.0f}-{act.frame_range[1]:.0f}")
if target and max(size) > 0:
    print(f"uniform scale for largest dim = {target} m: {target / max(size):.6f}")
