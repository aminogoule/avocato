import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix
from collections import Counter

OUT = sys.argv[-1]
RESHAPE = os.environ.get("AVO_RESHAPE", "0") == "1"

# ---------- 0. файл сохранён в режиме редактирования: без выхода в OBJECT правка меша молча теряется ----------
me = [o for o in bpy.data.objects if o.type == 'MESH'][0]
if me.mode != 'OBJECT':
    bpy.context.view_layer.objects.active = me
    bpy.ops.object.mode_set(mode='OBJECT')
    print("RIG: объект был в EDIT, переключён в OBJECT")

# ---------- 1. в систему Unity: Y вверх, Z вперёд ----------
me.data.transform(Matrix.Rotation(math.radians(-90), 4, 'X') @ me.matrix_world)
me.data.update()
me.matrix_world = Matrix.Identity(4)
me.name = "avo_hero"
bpy.context.view_layer.update()
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
for m in list(me.modifiers): me.modifiers.remove(m)
_c = [v.co for v in me.data.vertices]
print("RIG: габариты X[%.2f,%.2f] Y[%.2f,%.2f] Z[%.2f,%.2f]  вершин %d" % (
    min(p.x for p in _c), max(p.x for p in _c), min(p.y for p in _c), max(p.y for p in _c),
    min(p.z for p in _c), max(p.z for p in _c), len(_c)))

V = Vector
def smoothstep(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)

# ---------- 2. кости по замерам main_hero (X вбок, Y вверх, Z вперёд) ----------
BONES = [
    ("Root",     V((0, -0.68, -0.08)), V((0, -0.28, -0.08)), None,      False),
    ("Hips",     V((0, -0.28, -0.08)), V((0,  0.05, -0.06)), "Root",    True),
    ("Spine",    V((0,  0.05, -0.06)), V((0,  0.38, -0.06)), "Hips",    True),
    ("Chest",    V((0,  0.38, -0.06)), V((0,  0.56, -0.05)), "Spine",   True),
    ("Head",     V((0,  0.56, -0.05)), V((0,  0.88, -0.04)), "Chest",   True),
    ("Ear.L",    V(( 0.10, 0.84, -0.04)), V(( 0.24, 1.06, -0.04)), "Head", False),
    ("Ear.R",    V((-0.10, 0.84, -0.04)), V((-0.24, 1.06, -0.04)), "Head", False),
    ("Arm.L",    V(( 0.40, 0.46, -0.09)), V(( 0.84, 0.49, -0.09)), "Chest", False),
    ("Arm.R",    V((-0.40, 0.46, -0.09)), V((-0.84, 0.49, -0.09)), "Chest", False),
    ("LegUp.L",  V(( 0.22, -0.28, -0.09)), V(( 0.27, -0.50, -0.10)), "Hips", False),
    ("LegLow.L", V(( 0.27, -0.50, -0.10)), V(( 0.33, -0.68, -0.09)), "LegUp.L", True),
    ("LegUp.R",  V((-0.22, -0.28, -0.09)), V((-0.27, -0.50, -0.10)), "Hips", False),
    ("LegLow.R", V((-0.27, -0.50, -0.10)), V((-0.33, -0.68, -0.09)), "LegUp.R", True),
    ("Tail.1",   V((0, 0.05, -0.46)), V((0, 0.25, -0.62)), "Hips",   False),
    ("Tail.2",   V((0, 0.25, -0.62)), V((0, 0.52, -0.75)), "Tail.1", True),
    ("Tail.3",   V((0, 0.52, -0.75)), V((0, 0.58, -0.94)), "Tail.2", True),
]

# ---------- 3. необязательная правка геометрии ----------
LIMBS = [
    (V(( 0.40, 0.46, -0.09)), V(( 0.84, 0.49, -0.09)), 0.22, 0.10, V(( 0.80, -0.58, 0.06)), 1.9),
    (V((-0.40, 0.46, -0.09)), V((-0.84, 0.49, -0.09)), 0.22, 0.10, V((-0.80, -0.58, 0.06)), 1.9),
    (V(( 0.22, -0.28, -0.09)), V(( 0.33, -0.68, -0.09)), 0.24, 0.08, V(( 0.10, -0.99, 0.02)), 1.35),
    (V((-0.22, -0.28, -0.09)), V((-0.33, -0.68, -0.09)), 0.24, 0.08, V((-0.10, -0.99, 0.02)), 1.35),
]
JOINTS = [(V(( 0.40, 0.46, -0.09)), 0.30, 0.045), (V((-0.40, 0.46, -0.09)), 0.30, 0.045)]

def profile(t): return (0.5 + 0.5 * smoothstep(t / 0.22)) * (1.0 - 0.30 * smoothstep((t - 0.55) / 0.45))
def falloff(u): return 1.0 - smoothstep((u - 0.5) / 0.5)

def reshape(p):
    q = p.copy()
    for S, T, grab, extra, grow, fat in LIMBS:
        D = T - S; L = D.length; d = D.normalized()
        along = (q - S).dot(d)
        if along <= 0: continue
        radial = q - (S + d * along)
        if radial.length > grab: continue
        t = along / L
        q = q + grow.normalized() * (extra * smoothstep(t)) + radial * ((fat - 1.0) * profile(t) * falloff(radial.length / grab))
    return q

if RESHAPE:
    for v in me.data.vertices: v.co = reshape(v.co)
    me.data.update()
    _n = [v.normal.copy() for v in me.data.vertices]
    for i, v in enumerate(me.data.vertices):
        w = 0.0
        for J, R_, amt in JOINTS:
            dd = (v.co - J).length
            if dd < R_: w = max(w, (1.0 - smoothstep(dd / R_)) * amt)
        if w: v.co = v.co + _n[i] * w
    me.data.update()
    _c = [v.co for v in me.data.vertices]
    print("RIG: геометрия поправлена, X[%.2f,%.2f] Y[%.2f,%.2f]" % (
        min(p.x for p in _c), max(p.x for p in _c), min(p.y for p in _c), max(p.y for p in _c)))

# ---------- 4. арматура ----------
arm_data = bpy.data.armatures.new("avo_rig")
rig = bpy.data.objects.new("avo_rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
for name, head, tail, parent, conn in BONES:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = (reshape(head), reshape(tail)) if RESHAPE else (head, tail)
    if parent:
        b.parent = arm_data.edit_bones[parent]; b.use_connect = conn
    b.align_roll(V((0, 0, 1)))          # локальный Z вперёд -> поворот по локальному X = мах вперёд/назад
bpy.ops.object.mode_set(mode='OBJECT')

me.parent = rig
me.modifiers.new("Armature", 'ARMATURE').object = rig
DEFORM = [n for n, *_ in BONES if n != "Root"]
for n in DEFORM: me.vertex_groups.new(name=n)
segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones if b.name in DEFORM}

def d2seg(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared == 0 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length

# ---------- 5. сегментация по анатомии + сглаживание весов по рёбрам ----------
BODY = ["Hips", "Spine", "Chest", "Head"]
def region_of(p):
    x, y, z = p.x, p.y, p.z
    if y > 0.76 and abs(x) > 0.05:
        return "Ear.L" if x > 0 else "Ear.R"
    if z < -0.46 and abs(x) < 0.17:
        if z > -0.62: return "Tail.1"
        return "Tail.2" if z > -0.76 else "Tail.3"
    if abs(x) > 0.46 and 0.34 < y < 0.64:
        return "Arm.L" if x > 0 else "Arm.R"
    if y < -0.28 and abs(x) > 0.03:
        side = "L" if x > 0 else "R"
        return ("LegUp." if y > -0.50 else "LegLow.") + side
    return min(BODY, key=lambda n: d2seg(p, *segs[n]))

verts = me.data.vertices
assign = [region_of(v.co) for v in verts]
print("RIG: области:", dict(Counter(assign)))

bm = bmesh.new(); bm.from_mesh(me.data); bm.verts.ensure_lookup_table()
neigh = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
bm.free()
W = [{assign[i]: 1.0} for i in range(len(verts))]
LAM, ITERS, KEEP = 0.55, 10, 4
for _ in range(ITERS):
    nxt = []
    for i in range(len(verts)):
        acc = {k: (1 - LAM) * val for k, val in W[i].items()}
        ns = neigh[i]
        if ns:
            share = LAM / len(ns)
            for j in ns:
                for k, val in W[j].items(): acc[k] = acc.get(k, 0.0) + share * val
        nxt.append(acc)
    W = nxt
for i in range(len(verts)):
    items = sorted(W[i].items(), key=lambda kv: -kv[1])[:KEEP]
    s = sum(v for _, v in items)
    for name, val in items:
        if val / s > 0.005: me.vertex_groups[name].add([i], val / s, 'REPLACE')
print("RIG: веса готовы (сегментация + %d итераций сглаживания)" % ITERS)

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("RIG: сохранено", OUT)
