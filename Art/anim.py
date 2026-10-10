import bpy, math, sys, os
from math import radians
from mathutils import Vector

OUT = sys.argv[-1]
rig = bpy.data.objects['avo_rig']
if rig.animation_data is None:
    rig.animation_data_create()

# кости, которыми управляем направлением в мире (руки), остальные — обычными углами
AIMED = {"Arm.L", "Arm.R"}
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION' if pb.name in AIMED else 'XYZ'

for t in list(rig.animation_data.nla_tracks):
    rig.animation_data.nla_tracks.remove(t)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)

def clear_pose():
    for pb in rig.pose.bones:
        if pb.name in AIMED:
            pb.rotation_quaternion = (1, 0, 0, 0)
        else:
            pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)

def K(bone, f, rx=0.0, ry=0.0, rz=0.0, loc=None, scale=None):
    pb = rig.pose.bones[bone]
    pb.rotation_euler = (radians(rx), radians(ry), radians(rz))
    pb.keyframe_insert('rotation_euler', frame=f)
    if loc is not None:
        pb.location = loc; pb.keyframe_insert('location', frame=f)
    if scale is not None:
        pb.scale = scale; pb.keyframe_insert('scale', frame=f)

def AIM(bone, f, direction):
    """направление кости задаётся в мировых осях: X вбок, Y вверх, Z вперёд.
       Рест-поза рук — Т-поза (в стороны), поэтому любое «опустить» считаем отсюда."""
    pb = rig.pose.bones[bone]
    rest = pb.bone.matrix_local.to_3x3()
    local = (rest.inverted() @ Vector(direction).normalized()).normalized()
    pb.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(local)
    pb.keyframe_insert('rotation_quaternion', frame=f)

def start(name):
    clear_pose()
    act = bpy.data.actions.new(name); act.use_fake_user = True
    rig.animation_data.action = act
    return act

def finish(act, last, loop):
    act.frame_end = last
    tr = rig.animation_data.nla_tracks.new(); tr.name = act.name
    tr.strips.new(act.name, 0, act).name = act.name
    rig.animation_data.action = None
    print(f"ANIM: {act.name:5s} 0..{last:<3d} {'цикл' if loop else 'однократно'}")

# руки опущены вдоль тела, слегка разведены и чуть вперёд
def arms(f, swingL, swingR=None, down=0.90, out=0.55, fwd=0.14, up=None):
    """руки висят вдоль тела: вниз, заметно в стороны и чуть вперёд,
       иначе уходят внутрь чаши-скорлупы и перекрывают косточку"""
    if swingR is None: swingR = -swingL
    y = up if up is not None else -down
    AIM("Arm.L", f, ( out, y, fwd + swingL))
    AIM("Arm.R", f, (-out, y, fwd + swingR))


def leg_cycle(N, keys, phases, arm_scale, bob):
    """phases: список (бедро, колено) по фазам 0..1 цикла. Правая нога идёт со сдвигом полфазы.
       Колено задаётся отдельным профилем: гасит удар на контакте и подбирается при проносе."""
    n = len(phases)
    for i in range(keys + 1):
        f = round(N * i / keys)
        ph = i / keys
        for side, off in (("L", 0.0), ("R", 0.5)):
            k = ((ph + off) * n) % n
            a, b = phases[int(k) % n], phases[(int(k) + 1) % n]
            frac = k - int(k)
            hip = a[0] + (b[0] - a[0]) * frac
            knee = a[1] + (b[1] - a[1]) * frac
            K(f"LegUp.{side}", f, rx=hip)
            K(f"LegLow.{side}", f, rx=knee)
        # руки в контрмах к ногам
        kL = (ph * n) % n
        aL = phases[int(kL) % n][0]
        arms(f, -aL * arm_scale, aL * arm_scale, down=0.86, out=0.54)
        # корпус проседает дважды за цикл, на каждом контакте
        K("Root", f, loc=(0, bob * math.cos(4 * math.pi * ph), 0))

# ================= IDLE =================
a = start("Idle"); N = 48
for f, s in ((0, 1.00), (12, 1.03), (24, 1.00), (36, 0.98), (48, 1.00)):
    K("Spine", f, rx=(s - 1) * 60, scale=(1, s, 1))
for f, y in ((0, 0.0), (12, 0.02), (24, 0.0), (36, -0.015), (48, 0.0)):
    K("Root", f, loc=(0, y, 0))
for f, sw in ((0, 0.06), (24, -0.04), (48, 0.06)):
    arms(f, sw, sw, down=0.88, out=0.56)          # обе руки чуть покачиваются синхронно
for f, z in ((0, 0), (16, 9), (32, -9), (48, 0)):
    K("Tail.1", f, rz=z); K("Tail.2", f, rz=z * 1.3); K("Tail.3", f, rz=z * 1.6)
for f, x in ((0, 0), (24, 4), (48, 0)):
    K("Head", f, rx=x)
for f, z in ((0, 0), (20, 7), (26, -5), (32, 0), (48, 0)):
    K("Ear.L", f, rz=z); K("Ear.R", f, rz=-z)
finish(a, N, True)

# ================= WALK =================
a = start("Walk"); N = 32
# фазы: контакт, амортизация, опора, отталкивание, отрыв, подбор пятки, пронос, вынос
WALK_PHASES = [
    ( 25, -10),
    ( 14, -20),
    (  0,  -9),
    (-14,  -5),
    (-25, -15),
    (-15, -46),
    (  5, -52),
    ( 20, -26),
]
leg_cycle(N, 8, WALK_PHASES, arm_scale=0.030, bob=-0.028)
for f, z in ((0, 0), (8, 5), (16, 0), (24, -5), (32, 0)):
    K("Spine", f, rz=z); K("Tail.1", f, rz=-z * 1.5); K("Tail.2", f, rz=-z * 2)
K("Chest", 0, rx=5); K("Chest", 32, rx=5)
finish(a, N, True)

# ================= RUN =================
a = start("Run"); N = 16
# бег: колено гасит удар на контакте и сильно подбирается при проносе (пятка к корпусу)
RUN_PHASES = [
    ( 42, -22),
    ( 22, -40),
    (  0, -18),
    (-28,  -9),
    (-45, -32),
    (-25, -88),
    (  8, -96),
    ( 38, -56),
]
leg_cycle(N, 8, RUN_PHASES, arm_scale=0.020, bob=-0.055)
for i in range(9):
    f = round(N * i / 8)
    K("Spine", f, rx=15); K("Chest", f, rx=7)
for i in range(9):
    f = round(N * i / 8); z = 9 * math.sin(2 * math.pi * i / 8)
    K("Tail.1", f, rx=-24, rz=z); K("Tail.2", f, rx=-18, rz=z * 1.4); K("Tail.3", f, rx=-12, rz=z * 1.6)
finish(a, N, True)

# ================= JUMP =================
a = start("Jump"); N = 36
for f, (root, up, low, spine, adir) in (
        (0,  ( 0.00,  0,  -5,   0, ( 0.54, -0.82,  0.16))),
        (6,  (-0.10, 38, -55,  14, ( 0.56, -0.66, -0.42))),   # подсед, руки назад на замах
        (12, ( 0.16, -12, -5, -10, ( 0.34,  0.86,  0.30))),   # отрыв, руки вверх
        (20, ( 0.14,  20, -35,  -4, ( 0.46,  0.42,  0.12))),  # полёт
        (28, (-0.12,  42, -60,  12, ( 0.58, -0.62, -0.36))),  # приземление, гашение
        (36, ( 0.00,   0,  -5,   0, ( 0.54, -0.82,  0.16)))):
    K("Root", f, loc=(0, root, 0))
    K("LegUp.L", f, rx=up); K("LegUp.R", f, rx=up)
    K("LegLow.L", f, rx=low); K("LegLow.R", f, rx=low)
    K("Spine", f, rx=spine)
    AIM("Arm.L", f, ( adir[0], adir[1], adir[2]))
    AIM("Arm.R", f, (-adir[0], adir[1], adir[2]))
    K("Tail.1", f, rx=-spine * 1.2); K("Tail.2", f, rx=-spine); K("Tail.3", f, rx=-spine * 0.8)
finish(a, N, False)

# ================= FIRE =================
a = start("Fire"); N = 18
for f, (adir, spine, head) in (
        (0,  (( 0.54, -0.82, 0.16),  0,  0)),
        (4,  (( 0.30, -0.10, 0.95), -5, -3)),   # руки выносятся вперёд
        (7,  (( 0.34, -0.22, 0.90), -9, -5)),   # отдача
        (9,  (( 0.29, -0.06, 0.96), -4, -3)),
        (14, (( 0.30, -0.10, 0.95), -5, -3)),
        (18, (( 0.54, -0.82, 0.16),  0,  0))):
    AIM("Arm.L", f, ( adir[0], adir[1], adir[2]))
    AIM("Arm.R", f, (-adir[0], adir[1], adir[2]))
    K("Spine", f, rx=spine); K("Head", f, rx=head); K("Chest", f, rx=spine * 0.5)
finish(a, N, False)

clear_pose()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "avo_anim.blend"))
print("ANIM: actions:", [x.name for x in bpy.data.actions])
