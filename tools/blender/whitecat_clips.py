"""Author animation clips for the white cat glb (Blender 5.x, headless).

  blender.exe -b --python tools/blender/whitecat_clips.py -- --in IN.glb --out OUT.glb [--shots DIR] [--only a,b] [--render-only]

The input glb has a skinned mesh and 35 joints but no clips. Every clip is built by
describing a pose as a flat parameter dict (pelvis offset, spine/head/ear bends, tail
segment directions, paw targets for 2-bone IK legs), interpolating the parameters
frame by frame and solving the pose each frame. Bone world orientations are absolute
(not accumulated), so paws stay flat on the floor unless a pose bends them.
Model units: floor z = 0, the cat faces -Y, its left side is +X, 1 unit = about 0.55 m.
Outputs OUT.glb (clips as NLA tracks, 30 fps) and OUT.json (clip lengths, floor, size).
Regenerate all three cats (about 3 minutes; IN is a copy of whitecat-2-2.vrm renamed .glb), then install the outputs:
  blender -b --python tools/blender/whitecat_clips.py -- --in IN.glb --out whitecat-2-N-anim.glb      -> unity/Assets/Models/test/
  blender -b --python tools/blender/retarget_cats.py -- extract --sleep --src whitecat-2-N-anim.glb --out landmarks.json
  blender -b --python tools/blender/retarget_cats.py -- somali --sleep --src landmarks.json --in unity/Assets/Oddity/DevMotion/Cat/Somali/somali_cat_lie.glb --out somali_cat_shared.glb
  blender -b --python tools/blender/retarget_cats.py -- cat4   --sleep --src landmarks.json --in unity/Assets/Models/test/cat4-3.glb --out cat4_shared.glb
Env SETTLE=0 builds the old, unsettled lying poses; SETTLEKEYS (default pel,oHd) picks the segments settled onto the floor.
"""
import bpy, sys, math, os, json, copy
import numpy as np
from mathutils import Vector, Quaternion, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
IN = arg("--in"); OUT = arg("--out"); SHOTS = arg("--shots")
SHOTS = os.path.abspath(SHOTS) if SHOTS else None
IN = os.path.abspath(IN); OUT = os.path.abspath(OUT) if OUT else None
ONLY = arg("--only")
FRAMES_ONLY = arg("--frames")      # "name:f,f,f;name:f" render only these frames
FPS = 30
V = Vector

# ---------------------------------------------------------------- load
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=IN)
scene = bpy.context.scene
scene.render.fps = FPS
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.name in scene.objects and len(o.data.vertices) > 1000][0]
bones = {b.name: b for b in arm.data.bones}
rest = {n: b.matrix_local.copy() for n, b in bones.items()}
rest3 = {n: rest[n].to_3x3() for n in bones}
rhead = {n: b.head_local.copy() for n, b in bones.items()}
parent = {n: (b.parent.name if b.parent else None) for n, b in bones.items()}
order = []
def _walk(n):
    order.append(n)
    for c in bones[n].children:
        _walk(c.name)
for n, b in bones.items():
    if not b.parent:
        _walk(n)
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
if arm.animation_data:
    arm.animation_data.action = None
    for _t in list(arm.animation_data.nla_tracks):      # an input that already carries clips (a previous output) starts clean
        arm.animation_data.nla_tracks.remove(_t)
    for _a in list(bpy.data.actions):
        bpy.data.actions.remove(_a)
else:
    arm.animation_data_create()

# segment child per bone (the joint a bone's aim points at)
SEGC = {'Pelvis': 'Spine01', 'Spine01': 'Spine02', 'Spine02': 'Chest', 'Chest': 'Neck', 'Neck': 'Head'}
for s in 'LR':
    SEGC.update({'Scapula.' + s: 'FrontUpperLeg.' + s, 'FrontUpperLeg.' + s: 'FrontLowerLeg.' + s,
                 'FrontLowerLeg.' + s: 'FrontPaw.' + s, 'FrontPaw.' + s: 'FrontToes.' + s,
                 'HindUpperLeg.' + s: 'HindLowerLeg.' + s, 'HindLowerLeg.' + s: 'HindHock.' + s,
                 'HindHock.' + s: 'HindPaw.' + s, 'HindPaw.' + s: 'HindToes.' + s})
for i in range(1, 7):
    SEGC['Tail0%d' % i] = 'Tail0%d' % (i + 1)
rseg = {n: (rhead[c] - rhead[n]) for n, c in SEGC.items()}
rlen = {n: v.length for n, v in rseg.items()}

# ---------------------------------------------------------------- mesh queries
def vgroups():
    names = [vg.name for vg in body.vertex_groups]
    dom = []
    for v in body.data.vertices:
        g = max(v.groups, key=lambda g: g.weight) if len(v.groups) else None
        dom.append(names[g.group] if g else '')
    return np.array(dom)
DOM = vgroups()
def mask(prefixes):
    return np.array([d.startswith(prefixes) for d in DOM])
M_TAIL = mask(('Tail',)); M_EAR = mask(('Ear',))
M_PAWS = mask(('FrontPaw', 'FrontToes', 'HindPaw', 'HindToes'))
M_HEAD = mask(('Head',))
M_PAW = {'FL': mask(('FrontPaw.L', 'FrontToes.L')), 'FR': mask(('FrontPaw.R', 'FrontToes.R')),
         'HL': mask(('HindPaw.L', 'HindToes.L')), 'HR': mask(('HindPaw.R', 'HindToes.R'))}
M_BODYFLOOR = ~(mask(('Tail04', 'Tail05', 'Tail06', 'Tail07', 'Ear')))
M_BELLY = mask(('Pelvis', 'Spine0', 'Chest', 'Neck', 'Scapula'))      # trunk without legs, head and tail

def verts():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg); me = ev.to_mesh()
    a = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', a)
    ev.to_mesh_clear()
    a = a.reshape(-1, 3)
    mw = np.array(body.matrix_world)
    return a @ mw[:3, :3].T + mw[:3, 3]

def minz(torso=False):
    """Lowest body vertex; torso=True ignores the paws (tucked poses lie on the belly, paws only rest)."""
    m = M_BELLY if torso == 'belly' else (M_BODYFLOOR & ~M_PAWS) if torso else M_BODYFLOOR
    return float(verts()[m][:, 2].min())

# ---------------------------------------------------------------- solver
def Rz(a): return Matrix.Rotation(math.radians(a), 3, 'Z')
def Rx(a): return Matrix.Rotation(math.radians(a), 3, 'X')
def Ry(a): return Matrix.Rotation(math.radians(a), 3, 'Y')
def euler(pitch, yaw, roll):
    """World rotation: roll about Y, then nose-up pitch about X, then yaw about Z."""
    return Rz(yaw) @ Rx(-pitch) @ Ry(roll)

REACH = {'over': 0, 'max': 0.0}
def ik2(P, l1, l2, T, pole):
    d = T - P; dl = d.length
    if dl > (l1 + l2) * 0.9995:
        REACH['over'] += 1; REACH['max'] = max(REACH['max'], dl - (l1 + l2))
    dl = max(min(dl, (l1 + l2) * 0.9995), abs(l1 - l2) + 1e-4)
    axis = d.normalized()
    a = (l1 * l1 - l2 * l2 + dl * dl) / (2 * dl)
    h = math.sqrt(max(l1 * l1 - a * a, 0))
    pole = Vector(pole)
    pp = pole - axis * pole.dot(axis)
    pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, 1))
    K = P + axis * a + pp * h
    return K, (K - P).normalized(), (P + axis * dl - K).normalized()

def solve_chain(spec):
    """spec: {'pel': Vector, 'inc': {bone: Matrix}, 'abs': {bone: Matrix}, 'aim': {bone: dir}}.
    Returns (basis {bone: (loc, quat)}, posed {bone: 4x4 armature-space})."""
    D = {}; posed = {}; basis = {}
    for n in order:
        p = parent[n]
        if p:
            head = posed[p].translation + D[p] @ (rhead[n] - rhead[p])
            Dn = D[p]
        else:
            head = rhead[n].copy(); Dn = Matrix.Identity(3)
        if n == 'Pelvis':
            head = rhead[n] + spec['pel']
        elif n in spec.get('off', {}):
            head = head + spec['off'][n]
        if n in spec.get('aim', {}):
            cur = rseg[n]
            Dn = cur.rotation_difference(Vector(spec['aim'][n]).normalized()).to_matrix()
        if n in spec.get('abs', {}):
            Dn = spec['abs'][n]
        if n in spec.get('inc', {}):
            Dn = spec['inc'][n] @ Dn
        D[n] = Dn
        posed[n] = Matrix.Translation(head) @ (Dn @ rest3[n]).to_4x4()
        inh = (posed[p] @ rest[p].inverted() @ rest[n]) if p else rest[n].copy()
        bm = inh.inverted() @ posed[n]
        basis[n] = (bm.to_translation(), bm.to_quaternion())
    return basis, posed

def apply_basis(basis):
    for n, (l, q) in basis.items():
        pb = arm.pose.bones[n]
        pb.location = l; pb.rotation_quaternion = q
    bpy.context.view_layer.update()

# ---------------------------------------------------------------- pose parameters
SPINE = ('Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck', 'Head')
# rest tail segment directions, used as defaults for the tail aim parameters
TAILD = [rseg['Tail0%d' % i].normalized() for i in range(1, 7)]

HL_ = {'L': 1.0, 'R': -1.0}
L3 = (rhead['HindPaw.L'] - rhead['HindHock.L']).length
M_REST = (rhead['HindPaw.L'] - rhead['HindHock.L']).normalized()   # hock -> paw, standing

def default_pose():
    P = {'pel': (0, 0, 0)}
    for k in SPINE: P[k] = (0, 0, 0)
    P['EarL'] = (0, 0, 0); P['EarR'] = (0, 0, 0)
    for i in range(6): P['A%d' % (i + 1)] = tuple(TAILD[i])
    P['A7'] = (0, 0, 0)  # tip bend (pitch, yaw, roll) on Tail07
    for k in ('FL', 'FR', 'HL', 'HR'): P[k] = (0, 0, 0)
    P['FL'] = (0.05, -0.265, 0.06); P['FR'] = (-0.05, -0.265, 0.06)
    P['HL'] = (0.097, 0.277, 0.045); P['HR'] = (-0.097, 0.277, 0.045)
    P['FLp'] = (0.3, 1, 0); P['FRp'] = (-0.3, 1, 0)
    P['HLp'] = (0.1, -1, 0); P['HRp'] = (-0.1, -1, 0)
    P['HLm'] = tuple(M_REST); P['HRm'] = (-M_REST.x, M_REST.y, M_REST.z)
    for k in ('FLpitch', 'FRpitch', 'HLpitch', 'HRpitch'): P[k] = (0,)
    for k in ('FLtoe', 'FRtoe', 'HLtoe', 'HRtoe'): P[k] = (0,)
    for k in ('FLyr', 'FRyr', 'HLyr', 'HRyr'): P[k] = (0, 0)     # paw yaw, roll (lying poses)
    P['oS2'] = (0, 0, 0); P['oCh'] = (0, 0, 0)                    # breathing offsets of Spine02 / Chest heads
    P['oS1'] = (0, 0, 0)                                          # Spine01 head offset (the loin sags with the belly)
    P['oSh'] = (0, 0, 0)                                          # shoulder joints slide on the ribcage (a cat's scapula is not fixed): lying upright, the chest sinks between the shoulders
    P['oNk'] = (0, 0, 0); P['oHd'] = (0, 0, 0)                    # Neck / Head head offsets (lying poses settle each trunk segment onto the floor)
    return P

def lerp_pose(A, B, s):
    out = {}
    for k in A:
        a = A[k]; b = B.get(k, a)
        out[k] = tuple(x + (y - x) * s for x, y in zip(a, b))
    return out

def add(P, **kw):
    """P plus offsets: add(P, A3=(dx,dy,dz)) adds componentwise."""
    Q = {k: tuple(v) for k, v in P.items()}
    for k, d in kw.items():
        Q[k] = tuple(x + y for x, y in zip(Q[k], d))
    return Q

def build_spec(P, ground=None):
    spec = {'pel': V(P['pel']), 'inc': {}, 'abs': {}, 'aim': {}}
    pt, yw, rl = P['Pelvis']
    spec['abs']['Pelvis'] = euler(pt, yw, rl)
    for k in SPINE[1:]:
        spec['inc'][k] = euler(*P[k])
    for s, key in (('L', 'EarL'), ('R', 'EarR')):
        spec['inc']['Ear.' + s] = euler(*P[key])
    for i in range(6):
        spec['aim']['Tail0%d' % (i + 1)] = V(P['A%d' % (i + 1)])
    spec['inc']['Tail07'] = euler(*P['A7'])
    spec['off'] = {'Spine02': V(P['oS2']), 'Chest': V(P['oCh']), 'Neck': V(P['oNk']), 'Head': V(P['oHd'])}
    if 'oS1' in P and V(P['oS1']).length > 0: spec['off']['Spine01'] = V(P['oS1'])
    if 'oSh' in P and V(P['oSh']).length > 0:
        spec['off']['FrontUpperLeg.L'] = V(P['oSh']); spec['off']['FrontUpperLeg.R'] = V(P['oSh'])
    # pass 1: heads of shoulders and hips (they do not depend on the leg aims)
    _, posed0 = solve_chain(spec)
    for s in 'LR':
        # front leg
        sh = posed0['FrontUpperLeg.' + s].translation
        T = V(P['F' + s]); pole = V(P['F' + s + 'p'])
        l1 = rlen['FrontUpperLeg.' + s]; l2 = rlen['FrontLowerLeg.' + s]
        K, d1, d2 = ik2(sh, l1, l2, T, pole)
        spec['aim']['FrontUpperLeg.' + s] = d1; spec['aim']['FrontLowerLeg.' + s] = d2
        spec['abs']['FrontPaw.' + s] = euler(P['F' + s + 'pitch'][0], *P['F' + s + 'yr'])
        spec['inc']['FrontToes.' + s] = Rx(-P['F' + s + 'toe'][0])
        # hind leg: hip -> stifle -> hock by 2-bone IK, hock = paw - m*L3
        hp = posed0['HindUpperLeg.' + s].translation
        T = V(P['H' + s]); m = V(P['H' + s + 'm']).normalized()
        hock = T - m * L3
        l1 = rlen['HindUpperLeg.' + s]; l2 = rlen['HindLowerLeg.' + s]
        K, d1, d2 = ik2(hp, l1, l2, hock, V(P['H' + s + 'p']))
        if os.environ.get('IKDBG') and s == 'L': print('IKDBG hip %s hock %s pole %s l1 %.3f l2 %.3f K %s' % (tuple(round(c, 3) for c in hp), tuple(round(c, 3) for c in hock), tuple(P['H' + s + 'p']), l1, l2, tuple(round(c, 3) for c in K)))
        spec['aim']['HindUpperLeg.' + s] = d1; spec['aim']['HindLowerLeg.' + s] = d2
        spec['aim']['HindHock.' + s] = m
        spec['abs']['HindPaw.' + s] = euler(P['H' + s + 'pitch'][0], *P['H' + s + 'yr'])
        spec['inc']['HindToes.' + s] = Rx(-P['H' + s + 'toe'][0])
    return spec

def pose_basis(P):
    return solve_chain(build_spec(P))

def ground(P, target=0.0, iters=4, torso=False):
    """Shift pelvis height (and keep paw targets) so the lowest body vertex sits on the floor."""
    P = copy.deepcopy(P)
    for it in range(iters):
        apply_basis(pose_basis(P)[0])
        mz = minz(torso)
        if abs(mz - target) < 0.002:
            break
        px, py, pz = P['pel']
        P['pel'] = (px, py, pz + (target - mz))
    return P

# ---------------------------------------------------------------- keyframing
def ease(s):
    s = max(0.0, min(1.0, s))
    return s * s * (3 - 2 * s)

def keyed(keys, frames, loop=False):
    """keys: [(time_s, P)] -> function frame -> P (smoothstep between keys)."""
    def f(fr):
        t = fr / FPS
        for i in range(len(keys) - 1):
            ta, Pa = keys[i]; tb, Pb = keys[i + 1]
            if t <= tb + 1e-9:
                return lerp_pose(Pa, Pb, ease((t - ta) / (tb - ta)))
        return keys[-1][1]
    return f

def make_action(name, fn, frames, key_every=1):
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    prev = None
    fl = list(range(0, frames, key_every)) + [frames]
    for f in fl:
        P = fn(f)
        basis = pose_basis(P)[0]
        for n in order:
            l, q = basis[n]
            q = q.copy()
            if prev is not None:
                q.make_compatible(prev[n])
            pb = arm.pose.bones[n]
            pb.location = l; pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=f)
            pb.keyframe_insert('rotation_quaternion', frame=f)
        if name.startswith('Sleep'):
            bpy.context.view_layer.update()
            v = verts(); h = v[M_HEAD & ~M_EAR]
            m = MEAS.setdefault(name, {'min_z': 9.0, 'x': [9.0, -9.0], 'y': [9.0, -9.0], 'head': []})
            m['min_z'] = min(m['min_z'], float(v[:, 2].min()))
            m['x'] = [min(m['x'][0], float(v[:, 0].min())), max(m['x'][1], float(v[:, 0].max()))]
            m['y'] = [min(m['y'][0], float(v[:, 1].min())), max(m['y'][1], float(v[:, 1].max()))]
            m['head'].append(float(h[:, 2].max()))
        prev = {n: arm.pose.bones[n].rotation_quaternion.copy() for n in order}
    for fc in act.fcurves if hasattr(act, 'fcurves') else []:
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
    arm.animation_data.action = None
    act.use_fake_user = True
    return act

# ---------------------------------------------------------------- render
_cam = None
def setup_render():
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 512; scene.render.resolution_y = 512
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_cavity = False
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("w"); scene.world.color = (0.30, 0.38, 0.50)
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, -0.002))
    fl = bpy.context.object; fl.name = "RenderFloor"
    mat = bpy.data.materials.new("fl"); mat.diffuse_color = (0.55, 0.50, 0.40, 1)
    fl.data.materials.append(mat)
    cam = bpy.data.cameras.new("cam"); cam.type = 'ORTHO'
    co = bpy.data.objects.new("cam", cam); scene.collection.objects.link(co); scene.camera = co
    return co

def set_cam(co, view, center, scale):
    co.data.ortho_scale = scale
    c = Vector(center)
    if view.startswith('az'):       # az<deg>[e<deg>]: a still from that compass direction (0 = from +X), elevation 15 unless given
        az, _, el = view[2:].partition('e'); az = math.radians(float(az)); el = math.radians(float(el or 15))
        co.location = c + Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el))) * 20
        co.rotation_euler = (c - co.location).normalized().to_track_quat('-Z', 'Y').to_euler(); return
    d = {'front34': Vector((2.2, -3.2, 1.7)), 'side': Vector((9, 0, 0.2)), 'top': Vector((0, 0.001, 9)),
         'back34': Vector((-2.2, 3.2, 1.7)), 'front': Vector((0.001, -9, 0.2))}[view].normalized() * 20
    co.location = c + d
    look = (c - co.location).normalized()
    co.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()

def render_pose(tag, views=('front34', 'side', 'top'), center=(0, 0, 0.28), scale=1.3):
    global _cam
    if _cam is None:
        _cam = setup_render()
    os.makedirs(SHOTS, exist_ok=True)
    if os.environ.get('VIEWS'): views = tuple(os.environ['VIEWS'].split(','))     # e.g. VIEWS=back34,front (debug stills)
    scene.display.shading.show_backface_culling = os.environ.get('CULL') == '1'       # CULL=1 shows skin turned inside out as holes, like the game does
    for v in views:
        set_cam(_cam, v, center, scale)
        scene.render.filepath = os.path.join(SHOTS, "%s_%s.png" % (tag, v))
        bpy.ops.render.render(write_still=True)
        if os.environ.get('SKEL'):
            # bone ends in image space (0..1, y down) so a skeleton can be drawn over the still
            import json
            from bpy_extras.object_utils import world_to_camera_view
            bpy.context.view_layer.update()
            def px(p):
                q = world_to_camera_view(scene, _cam, arm.matrix_world @ p)
                return [round(q.x, 4), round(1 - q.y, 4)]
            with open(os.path.join(SHOTS, "%s_%s.json" % (tag, v)), 'w') as f:
                json.dump({pb.name: px(pb.head) + px(pb.tail) + [pb.parent.name if pb.parent else ''] for pb in arm.pose.bones}, f)

# ---------------------------------------------------------------- clips
STAND = default_pose()
STAND['pel'] = (0, 0, -0.012)

def breathe(P, ph, amp=1.0):
    """Breathing: chest rises a little. ph in radians."""
    b = math.sin(ph) * amp
    Q = add(P, Spine02=(1.2 * b, 0, 0), Chest=(1.5 * b, 0, 0), Neck=(-1.0 * b, 0, 0))
    return Q

def tail_sway(P, ph, yaw=8.0, base=None):
    """Rotate tail segment directions about world Z, each lagging a little further."""
    Q = {k: tuple(v) for k, v in P.items()}
    for i in range(6):
        a = yaw * (0.4 + 0.25 * i) * math.sin(ph - 0.55 * i)
        a0 = yaw * (0.4 + 0.25 * i) * math.sin(-0.55 * i)   # zero offset at ph = 0 so the base pose is the rest of the loop
        d = Rz(a - a0) @ V(P['A%d' % (i + 1)])
        Q['A%d' % (i + 1)] = tuple(d)
    return Q

def idle_fn(frames):
    base = STAND
    def f(fr):
        ph = 2 * math.pi * fr / frames
        P = breathe(base, ph)
        # slow head turns: one look left then right, ear flicks
        P = add(P, Head=(2.0 * math.sin(ph) - 0 , 14 * math.sin(ph) + 6 * math.sin(2 * ph), 3 * math.sin(ph)),
                Neck=(0, 5 * math.sin(ph), 0))
        # ear flick shortly after 1/3 of the loop
        e = max(0.0, math.sin(2 * math.pi * (fr / frames - 0.30) / 0.12)) if 0.30 <= fr / frames <= 0.42 else 0.0
        P = add(P, EarL=(0, 12 * e, 18 * e))
        e2 = max(0.0, math.sin(2 * math.pi * (fr / frames - 0.72) / 0.10)) if 0.72 <= fr / frames <= 0.82 else 0.0
        P = add(P, EarR=(0, -12 * e2, -18 * e2))
        return tail_sway(P, ph, yaw=7.0)
    return f


# ---- walk
WALK_T = 1.0
WALK_STRIDE = 0.70       # model units the body would travel per cycle (speed = stride / WALK_T)
WALK_DUTY = 0.60
WALK_PHASE = {'HL': 0.0, 'FL': 0.25, 'HR': 0.5, 'FR': 0.75}
WALK_YC = {'F': -0.262, 'H': 0.262}
WALK_LIFT = {'F': 0.055, 'H': 0.060}

def leg_path(kind, ph):
    """Returns (dy from centre, dz lift, paw pitch deg, meta-lean) for a phase in [0,1)."""
    half = WALK_STRIDE * WALK_DUTY / 2
    if ph < WALK_DUTY:
        u = ph / WALK_DUTY
        dy = -half + 2 * half * u
        pitch = 0.0
        if u > 0.78:
            pitch = -22 * ease((u - 0.78) / 0.22)
        lift = 0.0
        lean = -0.40 + 0.85 * u
    else:
        v = (ph - WALK_DUTY) / (1 - WALK_DUTY)
        sv = 0.45 * v + 0.55 * ease(v)
        dy = half - 2 * half * sv
        lift = WALK_LIFT[kind] * math.sin(math.pi * min(1.0, v * 1.0)) ** 0.8
        pitch = -22 * (1 - ease(v)) - 8 * math.sin(math.pi * v)
        lean = 0.45 - 0.95 * sv
    return dy, lift, pitch, lean

def walk_fn(frames):
    base = copy.deepcopy(STAND)
    def f(fr):
        t = fr / frames
        P = copy.deepcopy(base)
        bob = 0.004 * math.cos(4 * math.pi * t + 0.5)
        P['pel'] = (0.006 * math.sin(2 * math.pi * t), 0.0, -0.072 + bob)
        P['Pelvis'] = (0.0, -2.5 * math.sin(2 * math.pi * t + 0.4), 1.2 * math.sin(2 * math.pi * t + 1.0))
        yaws = {'Spine01': 3.0 * math.sin(2 * math.pi * t + 0.9), 'Spine02': 2.5 * math.sin(2 * math.pi * t + 1.4),
                'Chest': 1.5 * math.sin(2 * math.pi * t + 1.8)}
        for k, y in yaws.items():
            P[k] = (0.0, y, 0.0)
        tot = P['Pelvis'][1] + sum(yaws.values())
        P['Neck'] = (1.0, -0.4 * tot, 0)
        P['Head'] = (-1.0, -0.6 * tot, -0.5 * P['Pelvis'][2])
        for key in ('FL', 'FR', 'HL', 'HR'):
            kind = key[0]; s = key[1]
            ph = (t - WALK_PHASE[key]) % 1.0
            dy, lift, pitch, lean = leg_path(kind, ph)
            x = P[key][0]
            zbase = P[key][2]
            zextra = lift + 0.05 * math.sin(math.radians(-pitch)) if pitch < 0 else lift
            P[key] = (x, WALK_YC[kind] + dy, zbase + zextra)
            P[key + 'pitch'] = (pitch,)
            if kind == 'H':
                sx = 1.0 if s == 'L' else -1.0
                m = V((sx * 0.14, lean, -1.0 + 0.25 * lift / WALK_LIFT['H'] * 0)).normalized()
                P[key + 'm'] = tuple(m)
        P = tail_hold(P, 2 * math.pi * t)
        return P
    return f

def tail_hold(P, ph):
    Q = copy.deepcopy(P)
    for i in range(6):
        a = 7.0 * (0.4 + 0.25 * i) * math.sin(ph - 0.7 * i)
        d = Rz(a) @ V(P['A%d' % (i + 1)])
        Q['A%d' % (i + 1)] = tuple(d)
    return Q


# ---- resting poses (sit, sphinx, loaf)
def shoulders_hips(P):
    posed0 = solve_chain({**build_spec(P)})[1]
    return {n: posed0[n].translation.copy() for n in posed0}

def tail_dirs(angles):
    """angles: [(yaw deg from straight back toward +x, pitch deg up)] for Tail01..06 -> aim dict entries."""
    out = {}
    for i, (yw, pt) in enumerate(angles):
        y = math.radians(yw); p = math.radians(pt)
        out['A%d' % (i + 1)] = (math.sin(y) * math.cos(p), math.cos(y) * math.cos(p), math.sin(p))
    return out

def fit(P, front=None, hind=None, tailfloor=0.012, iters=5, torso=False):
    """Ground the pelvis, then put front/hind paw targets relative to the solved shoulders/hips.
    front: dict(dx, dy, z, pitch, pole); hind: dict(dx, dy, hz?, ...) see callers."""
    P = copy.deepcopy(P)
    for it in range(iters):
        if front or hind:
            H = shoulders_hips(P)
            for s, sx in (('L', 1), ('R', -1)):
                if front:
                    sh = H['FrontUpperLeg.' + s]
                    P['F' + s] = (sx * front['x'], sh.y + front['dy'], front.get('z', 0.06))
                    P['F' + s + 'p'] = (sx * front['pole'][0], front['pole'][1], front['pole'][2])
                    P['F' + s + 'pitch'] = (front.get('pitch', 0),)
                if hind:
                    hp = H['HindUpperLeg.' + s]
                    P['H' + s] = (sx * hind['x'], hp.y + hind['dy'], hind['z'])
                    P['H' + s + 'm'] = (sx * hind['m'][0], hind['m'][1], hind['m'][2])
                    P['H' + s + 'p'] = (sx * hind['pole'][0], hind['pole'][1], hind['pole'][2])
                    P['H' + s + 'pitch'] = (hind.get('pitch', 0),)
        P = ground(P, iters=1, torso=torso)
    P = ground(P, iters=2, torso=torso)
    return P

def tailfix(P, angles):
    Q = copy.deepcopy(P)
    Q.update(tail_dirs(angles))
    return Q

def droop_tail(P, H, margin=0.03, max_down=0.75):
    """Lying tail: keep each segment's heading, let it sink toward the floor (at most max_down of its length per segment)
    and then lie flat at `margin` height."""
    Q = copy.deepcopy(P)
    head = H['Tail01'].copy()
    for i in range(1, 7):
        L = rlen['Tail0%d' % i]
        d = V(Q['A%d' % i]).normalized()
        hv = V((d.x, d.y, 0)); hv = hv.normalized() if hv.length > 1e-6 else V((0, 1, 0))
        if head.z > margin + 0.003:
            dz = -min(max_down, (head.z - margin) / L)
        else:
            dz = max(-0.2, min(0.5, (margin - head.z) / L))
        d = hv * math.sqrt(max(1 - dz * dz, 0)) + V((0, 0, dz))
        Q['A%d' % i] = tuple(d)
        head = head + d * L
    return Q

def tail_heading(angles, z=0.0):
    """Tail directions lying in the floor plane: angle 0 = straight back (+Y), positive turns toward +X (the belly side)."""
    return [(math.sin(math.radians(a)), math.cos(math.radians(a)), z) for a in angles]

SIT = copy.deepcopy(STAND)
# a sitting cat: rump on the surface, front legs straight and vertical (the shoulders can be no higher than the
# legs reach, so the back is tucked steeply at the pelvis and flattens toward the chest), haunch folded forward
# with the hind paws flat beside/ahead of the hips, tail laid round the hips
SIT.update({'Pelvis': (58, 0, 0), 'Spine01': (-6, 0, 0), 'Spine02': (-10, 0, 0), 'Chest': (-8, 0, 0),
            'Neck': (-17, 0, 0), 'Head': (-15, 0, 0), 'pel': (0, 0.07, -0.30)})
SIT.update(tail_dirs([(35, -18), (80, -10), (125, -4), (155, -2), (175, 0), (190, 0)]))
SIT = fit(SIT, front=dict(x=0.06, dy=0.015, z=0.06, pitch=0, pole=(0.2, 1, 0)),
          hind=dict(x=0.11, dy=-0.05, z=0.045, m=(0.05, -0.97, -0.10), pole=(0.4, -1.0, 0.3), pitch=40))

SPHINX = copy.deepcopy(STAND)
SPHINX.update({'Pelvis': (6, 0, 0), 'Spine01': (0, 0, 0), 'Spine02': (4, 0, 0), 'Chest': (6, 0, 0),
               'Neck': (14, 0, 0), 'Head': (-12, 0, 0), 'pel': (0, 0.02, -0.30)})
SPHINX.update(tail_dirs([(0, -50), (15, -42), (40, -30), (70, -12), (100, -4), (125, 0)]))
SPHINX = fit(SPHINX, front=dict(x=0.06, dy=-0.20, z=0.06, pitch=48, pole=(0.0, 1, 0.6)),
             hind=dict(x=0.16, dy=0.02, z=0.045, m=(0.05, -0.97, -0.10), pole=(0.4, -1.0, 0.3), pitch=40))

LOAF = copy.deepcopy(STAND)
# loaf: a compact bread loaf. Belly on the surface (grounded by the trunk alone: legs, head and tail only rest), back a hump,
# haunch folded under the body, forepaws and hind paws tucked under it (elbows and knees turned outward so the legs stay
# inside the silhouette), chest and head up, tail laid on the floor round the body
LOAF.update({'Pelvis': (22, 0, 0), 'Spine01': (-8, 0, 0), 'Spine02': (-10, 0, 0), 'Chest': (-8, 0, 0),
             'Neck': (22, 0, 0), 'Head': (-14, 0, 0), 'pel': (0, 0.08, -0.31)})
LOAF.update(tail_dirs([(20, -25), (70, -20), (115, -12), (150, -5), (175, 0), (190, 0)]))
for _it in range(3):
    LOAF = fit(LOAF, front=dict(x=0.03, dy=0.09, z=0.045, pitch=60, pole=(0.8, 0.5, 0.0)),
               hind=dict(x=0.07, dy=-0.10, z=0.045, m=(0.05, -0.97, -0.10), pole=(0.5, -0.8, 0.3), pitch=40), torso='belly')
    LOAF = droop_tail(LOAF, shoulders_hips(LOAF), 0.03)
POSES = {'Stand': STAND, 'Sit': SIT, 'Sphinx': SPHINX, 'Loaf': LOAF}

# ---- shared modulations for the rest loops
def rest_loop(base, frames, head_yaw=10.0, head_pitch=2.0, breath=1.0, tail_segs=(3, 4, 5), tail_yaw=10.0,
              ear=True, chest_extra=0.0):
    def f(fr):
        ph = 2 * math.pi * fr / frames
        P = breathe(base, ph, breath)
        P = add(P, Chest=(chest_extra * math.sin(ph), 0, 0),
                Neck=(0, 0.4 * head_yaw * math.sin(ph), 0),
                Head=(head_pitch * math.sin(ph), head_yaw * math.sin(ph) + 0.35 * head_yaw * math.sin(2 * ph), 0.2 * head_yaw * math.sin(ph)))
        if ear:
            t = fr / frames
            e = math.sin(2 * math.pi * (t - 0.30) / 0.12) if 0.30 <= t <= 0.36 else 0.0
            P = add(P, EarL=(0, 12 * e, 18 * e))
            e2 = math.sin(2 * math.pi * (t - 0.74) / 0.10) if 0.74 <= t <= 0.79 else 0.0
            P = add(P, EarR=(0, -12 * e2, -18 * e2))
        Q = {k: tuple(v) for k, v in P.items()}
        for j, i in enumerate(tail_segs):
            a = tail_yaw * (0.5 + 0.35 * j) * math.sin(ph - 0.8 * j)
            a0 = tail_yaw * (0.5 + 0.35 * j) * math.sin(-0.8 * j)
            Q['A%d' % (i + 1)] = tuple(Rz(a - a0) @ V(P['A%d' % (i + 1)]))
        return Q
    return f

def grounded(fn, iters=2):
    """Wrap a pose function so the lowest body vertex stays within 2 mm of the floor."""
    def f(fr):
        return ground(fn(fr), iters=iters)
    return f
def grounded_blend(fn, frames, down, iters=2):
    """Grounding for the loaf transitions: paws+body near the standing end, the belly (paws ignored) near the
    loaf end, blended by smoothstep over the second half. down=True: the loaf is at the end of the clip."""
    def f(fr):
        P = fn(fr)
        t = fr / frames
        w = ease((t - 0.4) / 0.4) if down else ease((0.6 - t) / 0.4)
        Pa = ground(P, iters=iters); Pt = ground(P, iters=iters, torso=True)
        Q = copy.deepcopy(Pa)
        Q['pel'] = tuple(a + (b - a) * w for a, b in zip(Pa['pel'], Pt['pel']))
        return Q
    return f

# ---- jump
CROUCH = copy.deepcopy(STAND)
CROUCH.update({'pel': (0, 0.02, -0.13), 'Pelvis': (-6, 0, 0), 'Spine01': (-3, 0, 0), 'Spine02': (-3, 0, 0), 'Chest': (-3, 0, 0),
               'Neck': (4, 0, 0), 'Head': (10, 0, 0),
               'FL': (0.06, -0.20, 0.06), 'FR': (-0.06, -0.20, 0.06), 'HL': (0.10, 0.235, 0.045), 'HR': (-0.10, 0.235, 0.045)})
CROUCH.update(tail_dirs([(0, 35), (0, 20), (0, 8), (0, 0), (0, -5), (0, -8)]))
LAUNCH = copy.deepcopy(STAND)
LAUNCH.update({'pel': (0, 0.0, -0.01), 'Pelvis': (24, 0, 0), 'Spine01': (3, 0, 0), 'Spine02': (3, 0, 0), 'Chest': (3, 0, 0),
               'Neck': (-12, 0, 0), 'Head': (-8, 0, 0),
               'FL': (0.07, -0.56, 0.50), 'FR': (-0.07, -0.56, 0.50), 'FLp': (0.2, 1, 0.2), 'FRp': (-0.2, 1, 0.2),
               'FLpitch': (-35,), 'FRpitch': (-35,),
               'HL': (0.09, 0.52, 0.17), 'HR': (-0.09, 0.52, 0.17), 'HLm': (0.1, 0.65, -0.75), 'HRm': (-0.1, 0.65, -0.75),
               'HLpitch': (-40,), 'HRpitch': (-40,), 'HLp': (0.1, -1, 0.3), 'HRp': (-0.1, -1, 0.3)})
LAUNCH.update(tail_dirs([(0, 5), (0, 0), (0, -5), (0, -10), (0, -15), (0, -20)]))
TUCK = copy.deepcopy(STAND)
TUCK.update({'pel': (0, 0.0, 0.0), 'Pelvis': (4, 0, 0), 'Spine01': (-2, 0, 0), 'Spine02': (-2, 0, 0), 'Chest': (-3, 0, 0),
             'Neck': (-4, 0, 0), 'Head': (2, 0, 0),
             'FL': (0.075, -0.18, 0.33), 'FR': (-0.075, -0.18, 0.33), 'FLp': (0.4, 1, 0.1), 'FRp': (-0.4, 1, 0.1),
             'FLpitch': (-45,), 'FRpitch': (-45,),
             'HL': (0.10, 0.14, 0.17), 'HR': (-0.10, 0.14, 0.17), 'HLm': (0.1, -0.5, -0.85), 'HRm': (-0.1, -0.5, -0.85),
             'HLpitch': (-20,), 'HRpitch': (-20,)})
TUCK.update(tail_dirs([(0, 10), (0, 10), (0, 5), (0, 0), (0, -5), (0, -10)]))
LAND = copy.deepcopy(STAND)
LAND.update({'pel': (0, 0.02, -0.10), 'Pelvis': (-4, 0, 0), 'Spine01': (-2, 0, 0), 'Spine02': (-2, 0, 0), 'Chest': (-2, 0, 0),
             'Neck': (2, 0, 0), 'Head': (4, 0, 0),
             'FL': (0.06, -0.31, 0.06), 'FR': (-0.06, -0.31, 0.06), 'HL': (0.10, 0.22, 0.045), 'HR': (-0.10, 0.22, 0.045)})
LAND.update(tail_dirs([(0, 25), (0, 15), (0, 8), (0, 0), (0, -5), (0, -8)]))

# ---- groom (sitting): lick the raised left paw, then wipe the face with it
def groom_fn(frames):
    base = copy.deepcopy(SIT)
    H = shoulders_hips(base)
    head = H['Head']
    mouth = V((0.03, head.y - 0.075, head.z - 0.075))
    UP = copy.deepcopy(base)
    UP.update({'Neck': (-40, 6, 0), 'Head': (-46, 18, 4),
               'FL': tuple(mouth + V((0.0, 0.02, -0.03))), 'FLp': (1.0, 0.6, 0.2), 'FLpitch': (-35,), 'FLtoe': (-20,)})
    LICK1 = add(UP, Head=(6, 3, 0), FL=(0.0, 0.0, 0.012))
    LICK2 = add(UP, Head=(-4, -2, 0), FL=(0.0, 0.0, -0.008))
    WIPE_UP = copy.deepcopy(UP)
    WIPE_UP.update({'FL': (0.075, head.y + 0.015, head.z + 0.045), 'Head': (-40, 10, 8), 'Neck': (-37, 5, 0), 'FLpitch': (-10,), 'EarL': (-8, 0, 0)})
    WIPE_DN = add(WIPE_UP, FL=(0.0, -0.01, -0.07), Head=(-3, 0, -3))
    keys = [(0.0, base), (0.45, UP), (0.62, LICK1), (0.78, LICK2), (0.94, LICK1), (1.10, LICK2), (1.25, UP),
            (1.55, WIPE_UP), (1.75, WIPE_DN), (1.95, WIPE_UP), (2.15, WIPE_DN), (2.35, UP), (2.75, base), (3.0, base)]
    return keyed(keys, frames)

# ---- stretch: front legs far forward, chest low, rump high
STRETCH = copy.deepcopy(STAND)
STRETCH.update({'pel': (0, 0.02, -0.03), 'Pelvis': (-17, 0, 0), 'Spine01': (-1, 0, 0), 'Spine02': (-1, 0, 0), 'Chest': (3, 0, 0),
                'Neck': (10, 0, 0), 'Head': (12, 0, 0),
                'FL': (0.075, -0.52, 0.06), 'FR': (-0.075, -0.52, 0.06), 'FLp': (0.3, 1, 0.9), 'FRp': (-0.3, 1, 0.9),
                'FLpitch': (35,), 'FRpitch': (35,), 'HL': (0.097, 0.30, 0.045), 'HR': (-0.097, 0.30, 0.045)})
STRETCH.update(tail_dirs([(0, 55), (0, 45), (0, 30), (0, 20), (0, 10), (0, 5)]))

def stretch_fn(frames):
    MID = lerp_pose(STAND, STRETCH, 0.5)
    MID['FL'] = (0.075, -0.40, 0.06); MID['FR'] = (-0.075, -0.40, 0.06)
    S2 = add(STRETCH, Pelvis=(-2, 0, 0), Chest=(2, 0, 0), Head=(3, 0, 0))
    S3 = add(STRETCH, Pelvis=(1, 0, 0), Chest=(-1, 0, 0))
    keys = [(0.0, STAND), (0.55, MID), (1.0, STRETCH), (1.2, S2), (1.45, STRETCH), (1.65, S3), (1.85, STRETCH),
            (2.2, lerp_pose(STAND, STRETCH, 0.35)), (2.5, STAND)]
    return keyed(keys, frames)

# ================================================================= sleeping poses (phase A)
# Lying poses are authored on the cat's right side (roll -90 about Y: belly toward +X, back toward -X, left flank up).
# Spine bends are world-axis increments, so a curl of the lying spine is a yaw about Z (+ yaw curls the head toward the belly).
SLEEP_NAMES = ['LoafDoze', 'Donut', 'SideSprawl', 'BellyUp', 'PawCover', 'Superman', 'CurlC']

def clamp_tail(P, H, margin=0.03):
    """Keep the tail segments above the floor by turning a segment up when its end would go lower than margin."""
    Q = copy.deepcopy(P)
    head = H['Tail01'].copy()
    for i in range(1, 7):
        L = rlen['Tail0%d' % i]
        d = V(Q['A%d' % i]).normalized()
        if head.z + d.z * L < margin:
            dz = max(-0.999, min(0.999, (margin - head.z) / L))
            hv = V((d.x, d.y, 0)); hv = hv.normalized() if hv.length > 1e-6 else V((0, 1, 0))
            d = hv * math.sqrt(max(1 - dz * dz, 0)) + V((0, 0, dz))
            Q['A%d' % i] = tuple(d)
        head = head + d * L
    return Q

def world_tail(P, dirs):
    """dirs: six world direction vectors for Tail01..06 (normalised here)."""
    Q = copy.deepcopy(P)
    for i, d in enumerate(dirs):
        Q['A%d' % (i + 1)] = tuple(V(d).normalized())
    return Q

M_SEG = {'pel': mask(('Pelvis', 'Spine01')), 'oS2': mask(('Spine02',)), 'oCh': mask(('Chest', 'Scapula')), 'oNk': mask(('Neck',)),
         'oHd': mask(('Head',)) & ~M_EAR}
SETTLE_ALL = ('pel', 'oS2', 'oCh', 'oNk', 'oHd')
OFFCAP = {'oS2': 0.03, 'oCh': 0.03, 'oNk': 0.05, 'oHd': 0.09}      # the most a segment may be moved: more would crease the skin
def settle(P, keys=SETTLE_ALL, target=0.003, pct=2.0, passes=2, cap=0.12, head_pct=0.0):
    """A lying body spreads under its weight: lower each trunk segment (pelvis, mid, chest, neck, head) until the lowest 2 % of its
    vertices rest on the floor. Segment heights are free offsets of the bone heads; everything below a segment in the chain follows."""
    P = copy.deepcopy(P)
    for _ in range(passes):
        for key in SETTLE_ALL:
            if key not in keys: continue
            apply_basis(pose_basis(P)[0])
            z = float(np.percentile(verts()[M_SEG[key]][:, 2], head_pct if key == 'oHd' else pct))   # the head by its lowest vertex (ground_all lifts the whole body by it)
            d = max(-cap, min(cap, target - z))
            x, y, zz = P[key]; P[key] = (x, y, max(-OFFCAP[key], min(OFFCAP[key], zz + d)) if key != 'pel' else zz + d)
    return P

M_EARR = mask(('Ear.R',)); M_EARL = mask(('Ear.L',))
def tuck_ears(P, margin=0.008):
    """An ear that would be the lowest part of the lying head (the ear on the floor side) is folded flat against the head instead of lifting the body."""
    P = copy.deepcopy(P)
    for key, m in (('EarL', M_EARL), ('EarR', M_EARR)):
        apply_basis(pose_basis(P)[0]); v = verts(); hmin = float(v[M_SEG['oHd']][:, 2].min())
        if float(v[m][:, 2].min()) > hmin + margin: continue
        best = None
        for pit in (-50, 0, 50):
            for yaw in (-50, 0, 50):
                for rol in (-50, 0, 50):
                    Q = copy.deepcopy(P); Q[key] = (pit, yaw, rol); apply_basis(pose_basis(Q)[0]); v = verts()
                    h = float(v[m][:, 2].min()) - float(v[M_SEG['oHd']][:, 2].min())
                    if best is None or h > best[0]: best = (h, Q)
        P = best[1]
    return P

def settle_pitch(P, keys=('Spine01', 'Spine02', 'Chest', 'Neck'), target=0.005, sweeps=3):
    """Level the trunk: coordinate descent on the spine pitches (bends about the world X axis, which is the sag of a lying body whatever side
    it lies on) so that the pelvis, mid, chest and neck segments all rest at the same height just above the floor."""
    P = copy.deepcopy(P)
    segs = ('pel', 'oS2', 'oCh', 'oNk')
    def cost(Q):
        apply_basis(pose_basis(Q)[0]); v = verts()
        z = [float(np.percentile(v[M_SEG[k]][:, 2], 10)) for k in segs]
        return float(np.var(z))                  # all four at one height; the pelvis height itself is set afterwards
    best = cost(P)
    for _ in range(sweeps):
        for k in keys:
            for dlt in (6, -6, 3, -3, 1.5, -1.5):
                Q = copy.deepcopy(P); a, b, c = Q[k]; Q[k] = (max(-14, min(14, a + dlt)), b, c)
                c2 = cost(Q)
                if c2 < best - 1e-9: best, P = c2, Q
    return P

def lie_fit(P, legs, iters=6, tail_margin=0.03, droop=True, settle_keys=None, level=False, head_pct=0.0, refloor=False, body_pct=0.0):
    if settle_keys is None: settle_keys = tuple(os.environ.get('SETTLEKEYS', 'pel,oHd').split(',')) if os.environ.get('SETTLE', '1') == '1' else ()
    """Grounded lying pose. legs: {'FL': dict(ref=bone?, off=(dx,dy,dz), floor=True?, pole=, pitch=, yr=(yaw,roll), toe=, m=(hind))}.
    A paw target is the head of `ref` (default: that leg's shoulder/hip) plus off in world axes. A leg with floor=True instead
    keeps adjusting the target height until its paw mesh rests on the floor (the body is grounded by the torso only)."""
    P = copy.deepcopy(P)
    zc = {k: None for k in legs}
    clear = 0.002
    for it in range(iters):
        H = shoulders_hips(P)
        for k, d in legs.items():
            kind, sd = k[0], k[1]
            ref = H[d.get('ref', ('FrontUpperLeg.' if kind == 'F' else 'HindUpperLeg.') + sd)]
            T = ref + V(d['off'])
            ring = (H['Pelvis'] + H['Spine02'] + H['Chest'] + H['Head']) / 4          # the middle of a curled spine
            if 'mix' in d:      # curled poses: the paw lies in the gap between the head and the hips (t along head -> pelvis), pulled u toward the ring's middle
                t, u = d['mix']; T = H['Head'].lerp(H['Pelvis'], t); T = T.lerp(ring, u) + V(d['off'])
            if d.get('floor'):
                if zc[k] is None: zc[k] = T.z
                T.z = zc[k]
            P[k] = tuple(T); P[k + 'p'] = tuple(d.get('pole', (0, 0, 1)))
            if d.get('pole_in'):    # elbow / knee toward the middle of the ring, as in a curled cat's skeleton (they lie against the belly, never outside the back)
                ax = T - ref; ax.z = 0; c = H[d.get('pole_to', 'Spine02')] - (ref + T) / 2; c.z = 0
                if ax.length > 1e-5: c -= ax.normalized() * c.dot(ax.normalized())       # only the part across the limb: a pole along the limb leaves the joint anywhere (it went out through the rump)
                if c.length < 1e-4: c = ring - ref
                c.z = 0; c.normalize(); P[k + 'p'] = (c.x, c.y, d['pole_in'] if d['pole_in'] is not True else 0.0)
            P[k + 'pitch'] = (d.get('pitch', 0),); P[k + 'yr'] = tuple(d.get('yr', (0, 0))); P[k + 'toe'] = (d.get('toe', 0),)
            if kind == 'H': P[k + 'm'] = tuple(d.get('m', (0, 1, -0.3)))
            if kind == 'H' and d.get('m_to'):    # hock -> paw runs along the rim toward that bone (X-ray of a curled cat: the hind feet lie along the belly toward the nose)
                o = H[d['m_to']] - H['Pelvis']; o.z = 0; P[k + 'm'] = tuple(o.normalized())
            if kind == 'H' and d.get('m_out'):   # hock -> paw runs on outward from the ring's middle
                o = T - ring; o.z = 0; P[k + 'm'] = tuple(o.normalized())
        P = ground(P, iters=2, torso=True)
        if level and it == 0: P = settle_pitch(P, sweeps=4)
        if settle_keys: P = settle(P, settle_keys, head_pct=head_pct)
        apply_basis(pose_basis(P)[0])
        v = verts()
        for k, d in legs.items():
            if d.get('floor'):
                zc[k] += clear + d.get('lift', 0.0) - float(v[M_PAW[k]][:, 2].min())   # lift: this paw rests that high (on another paw)
        P = droop_tail(P, shoulders_hips(P), tail_margin) if droop else clamp_tail(P, shoulders_hips(P), tail_margin)
    for it in range(3):
        P = ground(P, iters=2, torso=True)
        if settle_keys: P = settle(P, settle_keys, head_pct=head_pct)
    P = ground_all(P, head_pct=head_pct, body_pct=body_pct)   # body_pct: the floor-side thigh and shoulder press into the floor like flesh does
    if refloor:
        # ground_all lifted the paw targets with the body (the floor-side thigh is what touches): lay the floor paws down again
        for it in range(4):
            apply_basis(pose_basis(P)[0]); v = verts()
            for k, d in legs.items():
                if d.get('floor'):
                    x, y, z = P[k]; P[k] = (x, y, z + clear + d.get('lift', 0.0) - float(v[M_PAW[k]][:, 2].min()))
    return P

M_HEADPART = mask(('Head',)) & ~M_EAR
def ground_all(P, iters=4, head_pct=0.0, body_pct=0.0):
    """Lift the pose so that no vertex at all (ears and tail tip included) is below the floor."""
    P = copy.deepcopy(P)
    for it in range(iters):
        apply_basis(pose_basis(P)[0])
        vv = verts(); mz = float(vv[:, 2].min())
        if head_pct > 0:      # whiskers on the floor side reach 5 cm below the head: they may pass through the floor
            mz = min(float(np.percentile(vv[~M_HEADPART][:, 2], body_pct)), float(np.percentile(vv[M_HEADPART][:, 2], head_pct)))
            if os.environ.get('GDEBUG'): print('GDEBUG nonhead pct 0/1/2/3/5/8', ' '.join('%+.3f' % np.percentile(vv[~M_HEADPART][:, 2], q) for q in (0, 1, 2, 3, 5, 8)))
        if os.environ.get('GDEBUG'):
            hz = vv[M_SEG['oHd']][:, 2]
            print("GDEBUG it%d min %+.3f at %s; head pct0/0.5/2/5 %s; nonhead min %+.3f" % (it, mz, DOM[int(vv[:, 2].argmin())],
                  " ".join("%+.3f" % np.percentile(hz, q) for q in (0, 0.5, 2, 5)), vv[~mask(('Head', 'Ear'))][:, 2].min()))
        if mz >= -0.0015: break
        px, py, pz = P['pel']; P['pel'] = (px, py, pz - mz)
        for k in ('FL', 'FR', 'HL', 'HR'):       # the paw targets are absolute: lift them with the body
            x, y, z = P[k]; P[k] = (x, y, z - mz)
    return P

def _e(name, default):
    return float(os.environ.get(name, default))

def sleep_base(pel=(0, 0.0, 0.10)):
    P = copy.deepcopy(STAND)
    P.update({'pel': pel, 'Pelvis': (0, 0, -90), 'Spine01': (-1, 0, 0), 'Spine02': (-2, 0, 0), 'Chest': (-4, 0, 0),
              'Neck': (0, 45, 0), 'Head': (0, -45, 0)})
    return P

def make_sprawl():
    """Chart panel 'side loose': on its side, spine nearly straight, head on the ground, forelegs lying forward a little bent (one over the other), hind legs lying back, tail trailing."""
    P = sleep_base()
    P.update({'Spine01': (0, 3, 0), 'Spine02': (0, 3, 0), 'Neck': (_e('SNP', -6), _e('SNY', 15), 0), 'Head': (_e('SHP', -4), _e('SHY', -10), _e('SHR', 0))})
    P = world_tail(P, [(0.15, 1, 0.1), (0.35, 1, 0), (0.6, 1, 0), (0.8, 1, 0), (0.9, 1, 0), (1, 1, 0)])
    fp = (_e('FPX', 0.35), _e('FPY', 1), _e('FPZ', 0.3)); hp = (_e('HPX', 0.7), _e('HPY', -1), _e('HPZ', 0.3))   # elbows and knees lie in the ground plane, not up
    hm = (_e('HMX', 0.0), 0.97, _e('HMZ', -0.2))
    return lie_fit(P, {
        'FR': dict(off=(_e('FRX', 0.10), _e('FRY', -0.24), 0), floor=True, pole=fp, pitch=55, yr=(0, -90)),
        'FL': dict(off=(_e('FLX', 0.16), _e('FLY', -0.20), 0), floor=True, lift=_e('LIFT', 0.045), pole=fp, pitch=55, yr=(0, -90)),
        'HR': dict(off=(_e('HRX2', 0.16), _e('HRY2', 0.18), 0), floor=True, pole=hp, m=hm, pitch=50, yr=(0, -90)),
        'HL': dict(off=(_e('HLX2', 0.20), _e('HLY2', 0.20), 0), floor=True, lift=_e('LIFT', 0.045), pole=hp, m=hm, pitch=50, yr=(0, -90))}, level=True, head_pct=_e('HPCT', 0.5), refloor=True, body_pct=_e('BPCT', 1))

def make_stretch():
    """Chart panel 'full stretch': on its side fully elongated in one long line, forelegs reaching past the head, hind legs straight back, tail hooked up."""
    P = sleep_base()
    P.update({'Spine01': (0, 0, 0), 'Spine02': (0, 0, 0), 'Chest': (-4, 0, 0), 'Neck': (-6, 42, 0), 'Head': (-4, -44, 0)})
    P = world_tail(P, [(0.15, 1, 0.1), (0.3, 1, 0.1), (0.4, 1, 0.3), (0.3, 1, 0.7), (0.15, 1, 1), (0.0, 0.6, 1)])
    return lie_fit(P, {
        'FR': dict(off=(0.10, -0.50, 0), floor=True, pole=(0, 0.3, 1), pitch=25, yr=(0, -90)),
        'FL': dict(off=(0.16, -0.46, 0), floor=True, lift=0.045, pole=(0, 0.3, 1), pitch=25, yr=(0, -90)),
        'HR': dict(off=(0.18, 0.40, 0), floor=True, pole=(0, -0.3, 1), m=(0.0, 0.97, -0.2), pitch=15, yr=(0, -90)),
        'HL': dict(off=(0.22, 0.42, 0), floor=True, lift=0.045, pole=(0, -0.3, 1), m=(0.0, 0.97, -0.2), pitch=15, yr=(0, -90))}, level=True)

def make_donut(cover=False):
    """Chart panel 'curl ball': belly down with the back up as one dome, the spine bent SIDEWAYS into a ring (not the fetal curl of a cat
    on its side), head laid round at the hind paws, legs folded under the body, tail wrapped along the ground round the front."""
    if os.environ.get('DONUT', 'side') == 'up':      # the belly-down ring of 2026-11-11 (kept for comparison)
        y1, y2, y3, yn, yh = _e('DY1', 55), _e('DY2', 65), _e('DY3', 60), _e('DYN', 50), _e('DYH', 40)
        P = copy.deepcopy(LOAF)
        P.update({'Pelvis': (_e('DPP', 28), 0, _e('DROLL', 0)), 'Spine01': (-8, y1, 0), 'Spine02': (-8, y2, 0), 'Chest': (-4, y3, 0),
                  'Neck': (_e('DNP', 30), yn, 0), 'Head': (_e('DHP', -20), yh, _e('DHR', 0))})
        P = world_tail(P, tail_heading([_e('DT', 1) * a for a in (20, 55, 95, 130, 160, 185)]))
        h = math.radians(y1 + y2 + y3)                      # the chest's heading: the forelegs fold back under it
        def rot(x, y): return (x * math.cos(h) - y * math.sin(h) * _e('DS', 1), (x * math.sin(h) * _e('DS', 1) + y * math.cos(h)), 0)
        return lie_fit(P, {
            'FL': dict(off=rot(-0.05, 0.07), floor=True, pole=(0.2, 1, 0.3), pitch=60),
            'FR': dict(off=rot(0.05, 0.07), floor=True, pole=(-0.2, 1, 0.3), pitch=60),
            'HL': dict(off=(_e('HLX', 0.08), _e('HLY', -0.12), 0), floor=True, pole=(0.35, -0.6, 0.3), m=(0.0, -1, -0.1), pitch=40),
            'HR': dict(off=(_e('HRX', 0.03), _e('HRY', -0.12), 0), floor=True, pole=(-0.35, -0.6, 0.3), m=(0.0, -1, -0.1), pitch=40)},
            settle_keys=SETTLE_ALL)
    # on its side, after an X-ray of a curled cat: the spine is one C from the pelvis to the skull (the skull points back at the hips), the thighs lie forward
    # along the belly with the knees in the middle, the elbows too, and all four paws gather in the gap in front of the face; the tail wraps round outside
    P = sleep_base()
    P.update({'Spine01': (0, _e('XY1', 45), 0), 'Spine02': (0, _e('XY2', 55), 0), 'Chest': (0, _e('XY3', 55), 0), 'Neck': (-8, _e('XYN', 85), 0), 'Head': (-6, _e('XYH', -10), 0)})
    P = world_tail(P, tail_heading([_e('XT', 1) * a for a in (_e('XT1', 30), _e('XT2', 80), _e('XT3', 115), _e('XT4', 125), _e('XT5', 125), _e('XT6', 125))]))
    # the ring is left a little open (155 deg over the trunk) and the hind paws lie in its middle: curled tighter, the hips touch the face, the thigh has nowhere
    # to go and the knee comes out through the rump. The knees bend toward the face, the elbows toward the hips
    lo, up = _e('XPLO', 0.2), _e('XPUP', -0.6)      # the floor-side elbow and knee a little up off the floor, the upper ones pressed down onto the body
    # second X-ray (2026-11-12): knees AND elbows in the middle of the ring; the shanks run back out to the hocks on the rim between the hips and the head, the
    # hind feet lie along the rim with the toes at the nose; the forearms come back from the middle to the chin; the tail follows the rim round to under the chin
    ft, fu, ht, hu = _e('XFT', 0.0), _e('XFU', 0.05), _e('XHT', 0.05), _e('XHU', -0.2)
    hm = os.environ.get('XHM', 'Head')
    legs = {
        'FR': dict(mix=(ft, fu), off=(0, 0, 0), floor=True, pole_to=os.environ.get('XFP', 'Pelvis'), pole_in=lo, pitch=48, yr=(0, -90)),
        'FL': dict(mix=(ft + 0.06, fu), off=(0, 0, 0), floor=True, lift=_e('XLIFT', 0.045), pole_to=os.environ.get('XFP', 'Pelvis'), pole_in=up, pitch=48, yr=(0, -90)),
        'HR': dict(mix=(ht, hu), off=(0, 0, 0), floor=True, pole_to=os.environ.get('XHP', 'Chest'), pole_in=_e('XKLO', 0.8), m_to=hm, pitch=40, yr=(0, -90)),
        'HL': dict(mix=(ht + 0.06, hu), off=(0, 0, 0), floor=True, lift=_e('XLIFT', 0.045), pole_to=os.environ.get('XHP', 'Chest'), pole_in=_e('XKUP', -0.2), m_to=hm, pitch=40, yr=(0, -90))}
    if cover:
        legs['FL'] = dict(ref='Head', off=(0.0, 0.05, 0.05), pole=(0.3, 1, 0.1), pitch=70, yr=(0, -90))
    return lie_fit(P, legs, head_pct=0.5, refloor=True, body_pct=_e('BPCT', 1))

def make_belly_up():
    P = sleep_base(pel=(0, 0, 0.1))
    P.update({'Pelvis': (0, -12, 168), 'Spine01': (0, 5, 4), 'Spine02': (0, 6, 5), 'Chest': (0, 0, 4), 'Neck': (45, 10, 0), 'Head': (-37, 0, -75)})
    P = world_tail(P, [(0.1, 1, 0.1), (0.4, 1, 0), (0.8, 1, 0), (1, 0.6, 0), (1, 0.2, 0), (1, 0.0, 0)])
    return lie_fit(P, {
        'FL': dict(off=(0.05, -0.12, 0.12), pole=(0.3, 1, 0.5), pitch=30, yr=(0, 160)),
        'FR': dict(off=(-0.05, -0.14, 0.12), pole=(-0.3, 1, 0.5), pitch=30, yr=(0, -160)),
        'HL': dict(off=(-0.11, -0.05, 0.10), pole=(-0.5, -0.6, 0.2), m=(-0.2, 0.3, 1), pitch=0, yr=(0, 180)),
        'HR': dict(off=(0.11, -0.05, 0.10), pole=(0.5, -0.6, 0.2), m=(0.2, 0.3, 1), pitch=0, yr=(0, 180))}, level=True)

def make_superman():
    P = copy.deepcopy(STAND)
    P.update({'pel': (0, 0.03, -0.30), 'Pelvis': (0, 0, 0), 'Spine01': (-1, 0, 0), 'Spine02': (-2, 0, 0), 'Chest': (-4, 0, 0),
              'Neck': (-20, 0, 0), 'Head': (6, 0, 0)})
    P = world_tail(P, [(0, 1, -0.2), (0, 1, -0.1), (0.1, 1, 0), (0.2, 1, 0), (0.3, 1, 0), (0.3, 1, 0)])
    return lie_fit(P, {
        'FL': dict(off=(0.04, -0.34, 0), floor=True, pole=(0.3, 0.3, 1), pitch=48),
        'FR': dict(off=(-0.04, -0.34, 0), floor=True, pole=(-0.3, 0.3, 1), pitch=48),
        'HL': dict(off=(0.12, 0.30, 0), floor=True, pole=(0.3, -0.3, 1), m=(0.25, 0.97, 0), pitch=-135),
        'HR': dict(off=(-0.12, 0.30, 0), floor=True, pole=(-0.3, -0.3, 1), m=(-0.25, 0.97, 0), pitch=-135)}, level=True)

def make_curlc(cover=False):
    """Lying on its side, after X-rays of a cat lying down (2026-11-12): the spine a gentle C, the head down on the floor-side forepaw, that foreleg's elbow back by
    the ribs and its forearm forward under the chin, the upper foreleg folded with the paw by the chest (cover: laid over the eyes), both thighs FORWARD toward the
    belly with the shanks running back and the feet forward again (a Z), the upper hind leg lying on the lower, the tail trailing with a hooked tip."""
    P = sleep_base()
    P.update({'Spine01': (0, _e('CY1', 10), 0), 'Spine02': (0, _e('CY2', 12), 0), 'Chest': (0, _e('CY3', 12), 0),
              'Neck': (_e('CNP', -6), _e('CNY', 48), 0), 'Head': (_e('CHP', -4), _e('CHY', 12), 0)})
    P = world_tail(P, [(0.15, 1, 0.1), (0.3, 1, 0), (0.4, 1, 0), (0.3, 1, 0), (0.0, 1, 0), (-0.6, 0.6, 0)])
    fp = (_e('CFPX', 0.5), 1, _e('CFPZ', 0.2)); hp = (_e('CHPX', 0.6), -1, _e('CHPZ', 0.2)); hm = (0.0, -1, 0.0)
    legs = {
        'FR': dict(ref='Head', off=(_e('CFRX', 0.04), _e('CFRY', 0.0), 0), floor=True, pole=fp, pitch=55, yr=(0, -90)),
        'FL': dict(off=(_e('CFLX', 0.13), _e('CFLY', -0.03), 0), floor=True, lift=0.045, pole=fp, pitch=55, yr=(0, -90)),
        'HR': dict(off=(_e('CHRX', 0.17), _e('CHRY', -0.03), 0), floor=True, pole=hp, m=hm, pitch=50, yr=(0, -90)),
        'HL': dict(off=(_e('CHLX', 0.19), _e('CHLY', 0.01), 0), floor=True, lift=0.045, pole=hp, m=hm, pitch=50, yr=(0, -90))}
    if cover:
        legs['FL'] = dict(ref='Head', off=(float(os.environ.get('CX', 0.07)), float(os.environ.get('CY', -0.02)), float(os.environ.get('CZ', 0.06))), pole=(0.3, 1, 0.3), pitch=70, yr=(0, -90))
    return lie_fit(P, legs, level=True, head_pct=0.5, refloor=True, body_pct=1)


def sphinx_pose(spine, neck, head):
    """Lying upright on the chest, after an X-ray of a cat in the sphinx pose (2026-11-12): the upper arms stand under the shoulders and the forearms lie
    forward on the floor with the paws out as far as the head; the thighs lie FORWARD along the flanks (knees ahead of the hips), the shanks run back
    down to the hocks and the feet lie forward under the knees; the back rises from the low hips to the shoulders; the tail lies forward along the body."""
    P = copy.deepcopy(LOAF)
    pp, s1, s2, ch = spine
    P.update({'Pelvis': (pp, 0, 0), 'Spine01': (s1, 0, 0), 'Spine02': (s2, 0, 0), 'Chest': (ch, 0, 0), 'Neck': (neck, 0, 0), 'Head': (head, 0, 0)})
    P['oSh'] = (0, _e('OSHY', 0.0), _e('OSH', 0.085))
    P.update(tail_dirs([(35, -25), (80, -20), (125, -12), (160, -5), (175, 0), (180, 0)]))
    fx, fy = _e('LFX', 0.0), _e('LFY', -0.15); hy = _e('LHY', -0.05)
    fp = (_e('LFPX', 0.0), 1, _e('LFPZ', -0.3))
    def build(hx):
        hp = (_e('LHPX', 3.0), -1, _e('LHPZ', -2.0))       # knees low: the thighs lie forward along the floor and close the hollow in front of them
        Q = lie_fit(P, {
            'FL': dict(off=(fx, fy, 0), floor=True, pole=fp, pitch=_e('LFP', 48)),
            'FR': dict(off=(-fx, fy, 0), floor=True, pole=(-fp[0], fp[1], fp[2]), pitch=_e('LFP', 48)),
            'HL': dict(off=(hx, hy, 0), floor=True, pole=hp, m=(0.0, -1, -0.1), pitch=40),
            'HR': dict(off=(-hx, hy, 0), floor=True, pole=(-hp[0], hp[1], hp[2]), m=(0.0, -1, -0.1), pitch=40)},
            settle_keys=('pel',), refloor=True)
        return Q, pose_basis(Q)[1]['HindLowerLeg.L'].translation.x
    # the knees lie out at the width of the hips (seen from behind they frame the rump), not drawn in under the spine. This cat's shank is longer than its thigh,
    # so the knee lies on the far side of the hip from the hock: a hock out at the flank throws the knee in under the spine. Search how far the feet go in
    # under the haunches for the knee to come out to that width
    kx = _e('LKX', 0.085); lo, hi = -0.06, 0.06
    for _ in range(7):
        mid = (lo + hi) / 2; Q, x = build(mid)
        if x > kx: lo = mid
        else: hi = mid
    print('KNEE sphinx paw dx %+.3f knee x %+.3f (want %+.3f)' % (mid, x, kx))
    # the belly sags to the floor (this model has no belly fur to hide the gap under the flank): the mid-trunk joint drops, the chest joint comes back up by as much
    d = _e('LBELLY', 0.04); d1 = _e('LLOIN', 0.045)      # the loin joint (in front of the thighs) drops d1, the mid-trunk joint d in all
    Q['oS1'] = (0, 0, -d1); x, y, z = Q['oS2']; Q['oS2'] = (x, y, z - (d - d1)); x, y, z = Q['oCh']; Q['oCh'] = (x, y, z + d)
    return Q

def make_doze():
    """Sphinx loaf (the user's chart): the sphinx pose with the head sunk between the shoulders; the Idle adds the dozing nod."""
    return sphinx_pose((_e('LPP', 30), _e('LS1', -12), _e('LS2', -12), _e('LCH', -6)), _e('LNK', 22), _e('LHD', -30))

# key 1 (LieDown / LyingIdle / GetUp): the alert sphinx, neck upright
SPHINX = sphinx_pose((_e('SPP', 30), _e('SS1', -12), _e('SS2', -12), _e('SCH', -6)), _e('SNK', 48), _e('SHD', -50))
POSES['Sphinx'] = SPHINX

SLEEP_POSES = {'LoafDoze': make_doze, 'SideSprawl': make_sprawl, 'Donut': make_donut, 'PawCover': lambda: make_curlc(True),
               'BellyUp': make_belly_up, 'Superman': make_stretch, 'CurlC': make_curlc, }
SLEEP_BREATH = {'LoafDoze': 0.006, 'Donut': 0.008, 'SideSprawl': 0.016, 'BellyUp': 0.014, 'PawCover': 0.008, 'Superman': 0.008, 'CurlC': 0.008}
SLEEP_DOWN_T = 1.6; SLEEP_UP_T = 1.1; SLEEP_IDLE_T = 8.0
MEAS = {}      # clip name -> measurements recorded by make_action for the Sleep clips

def pulse(t, t0, dur):
    return math.sin(math.pi * (t - t0) / dur) ** 2 if t0 <= t <= t0 + dur else 0.0

def sleep_idle_fn(name, base, frames):
    """Breathing (3 cycles per loop, only upward from the base), ear flicks every 4 s, slow tail tip twitches; the doze adds head nods."""
    amp = SLEEP_BREATH[name]
    def f(fr):
        t = fr / FPS; T = frames / FPS
        ph = 2 * math.pi * 3 * fr / frames
        b = 0.5 - 0.5 * math.cos(ph)
        P = copy.deepcopy(base)
        P['oS2'] = tuple(x + y for x, y in zip(P['oS2'], (0, 0, amp * b))); P['oCh'] = tuple(x + y for x, y in zip(P['oCh'], (0, 0, amp * 1.3 * b)))
        eL = pulse(t, 1.5, 0.45); eR = pulse(t, 5.5, 0.45)
        P = add(P, EarL=(0, 12 * eL, 18 * eL), EarR=(0, -12 * eR, -18 * eR))
        tw = pulse(t, 2.4, 1.3) - 0.8 * pulse(t, 6.2, 1.1)
        for i in (3, 4, 5):
            P['A%d' % (i + 1)] = tuple(Rz(22 * tw * (0.4 + 0.3 * (i - 3))) @ V(P['A%d' % (i + 1)]))
        P['A7'] = (0, 15 * tw, 0)
        if name == 'LoafDoze':
            # slow nod down 0.6 -> 3.4 s, quick jerk back up (3.8 s), a shallower nod 5.0 -> 7.4 s
            d = 0.0
            if 0.6 <= t < 3.4: d = ease((t - 0.6) / 2.8)
            elif 3.4 <= t < 3.8: d = 1.0 - ease((t - 3.4) / 0.4) * 1.05
            elif 3.8 <= t < 4.4: d = -0.05 * (1 - ease((t - 3.8) / 0.6))
            elif 5.0 <= t < 7.0: d = 0.55 * ease((t - 5.0) / 2.0)
            elif 7.0 <= t < 7.6: d = 0.55 * (1 - ease((t - 7.0) / 0.6))
            P = add(P, Neck=(-14 * d, 0, 0), Head=(-9 * d, 0, 0))
        return ground_all(P, iters=1)
    return f

def sleep_trans(A, B, T, mid=None, blend=None, rest=None, down=True):
    """rest: the settled Idle pose the clip must end on (down) or start from (up): the last / first 8 frames blend to it, so Idle never pops."""
    def g(fr):
        keys = [(0.0, A)] + ([(T * 0.42, mid)] if mid is not None else []) + [(T, B)]
        if blend is not None:
            fn = grounded_blend(keyed(keys, fr), fr, blend)
        else:
            fn = grounded(keyed(keys, fr))
        def h(f):
            P = ground_all(fn(f), iters=1)
            if rest is not None:
                w = ease((f - (fr - 8)) / 8.0) if down else ease(1.0 - f / 8.0)
                if f >= fr and down: return copy.deepcopy(rest)
                if f <= 0 and not down: return copy.deepcopy(rest)
                if w > 0: P = lerp_pose(P, rest, w)
            return P
        return h
    return g

def lift(fn):
    """Wrap a pose function: nothing may end up below the floor (the compact loaf's tail and ears)."""
    return lambda f: ground_all(fn(f), iters=2)

def build_all(only=None):
    out = {}
    def want(n): return only is None or n in only
    def mk(name, fn, secs, loop):
        if want(name):
            fr = int(round(secs * FPS))
            out[name] = (make_action(name, fn(fr), fr), fr, loop)
    def trans(A, B, mid=None, blend=None):
        def g(fr):
            T = fr / FPS
            keys = [(0.0, A)] + ([(T * 0.5, mid)] if mid is not None else []) + [(T, B)]
            if blend is not None:
                return grounded_blend(keyed(keys, fr), fr, blend)
            return grounded(keyed(keys, fr))
        return g
    def jump_fn(fr):
        T = fr / FPS
        keys = [(0.0, STAND), (0.20 * T, CROUCH), (0.36 * T, LAUNCH), (0.55 * T, TUCK), (0.76 * T, LAND), (T, STAND)]
        return keyed(keys, fr)
    mk('Idle', idle_fn, 3.0, True)
    mk('Walk', walk_fn, WALK_T, True)
    mk('SitDown', trans(STAND, SIT), 1.2, False)
    mk('SittingIdle', lambda fr: rest_loop(SIT, fr, head_yaw=12, tail_segs=(4, 5), tail_yaw=12), 2.5, True)
    mk('StandUp', trans(SIT, STAND), 1.0, False)
    mk('LieDown', trans(STAND, SPHINX), 1.5, False)
    mk('LyingIdle', lambda fr: rest_loop(SPHINX, fr, head_yaw=9, tail_segs=(3, 4, 5), tail_yaw=14), 3.0, True)
    mk('GetUp', trans(SPHINX, STAND), 1.2, False)
    mk('LoafDown', lambda fr: lift(trans(STAND, LOAF, blend=True)(fr)), 1.5, False)
    mk('LoafIdle', lambda fr: lift(rest_loop(LOAF, fr, head_yaw=5, breath=1.3, tail_segs=(4, 5), tail_yaw=8)), 3.0, True)
    mk('LoafUp', lambda fr: lift(trans(LOAF, STAND, blend=False)(fr)), 1.2, False)
    mk('Jump', jump_fn, 0.8, False)
    mk('Groom', groom_fn, 3.0, True)
    mk('Stretch', stretch_fn, 2.5, False)
    for nm in SLEEP_NAMES:
        names = ['Sleep%sDown' % nm, 'Sleep%sIdle' % nm, 'Sleep%sUp' % nm]
        if only is not None and not any(n in only for n in names): continue
        B = SLEEP_POSES[nm]()
        Bg = sleep_idle_fn(nm, B, 240)(0)        # the Idle's first frame (grounded base, no breathing): the Down ends on it, the Up starts from it
        if nm == 'LoafDoze':
            mk(names[0], lambda fr, B=B, Bg=Bg: sleep_trans(STAND, B, fr / FPS, blend=True, rest=Bg)(fr), SLEEP_DOWN_T - 0.1, False)
            mk(names[2], lambda fr, B=B, Bg=Bg: sleep_trans(B, STAND, fr / FPS, blend=False, rest=Bg, down=False)(fr), SLEEP_UP_T, False)
        else:
            mk(names[0], lambda fr, B=B, Bg=Bg: sleep_trans(STAND, B, fr / FPS, mid=LOAF, rest=Bg)(fr), SLEEP_DOWN_T, False)
            mk(names[2], lambda fr, B=B, Bg=Bg: sleep_trans(B, STAND, fr / FPS, mid=LOAF, rest=Bg, down=False)(fr), SLEEP_UP_T, False)
        mk(names[1], lambda fr, nm=nm, B=B: sleep_idle_fn(nm, B, fr), SLEEP_IDLE_T, True)
    return out

def render_clips(clips):
    over = {}
    if FRAMES_ONLY:
        for part in FRAMES_ONLY.split(';'):
            nm, fs = part.split(':'); over[nm] = [int(x) for x in fs.split(',')]
    for name, (act, nfr, loop) in clips.items():
        arm.animation_data.action = act
        fl = over.get(name) or ([0, nfr // 2, nfr] if not loop else [0, nfr // 3, 2 * nfr // 3, nfr])
        for f in fl:
            scene.frame_set(f)
            v = verts()
            pz = v[M_PAWS][:, 2].min(); mz = v[M_BODYFLOOR][:, 2].min()
            print("CONTACT %-10s f%-3d body minz %+.4f  paws minz %+.4f" % (name, f, mz, pz))
            render_pose("%s_f%03d" % (name, f))
        arm.animation_data.action = None

def export(clips, out):
    arm.animation_data.action = None
    for nm in ('RenderFloor', 'cam'):
        if nm in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[nm], do_unlink=True)
    for name, (act, nfr, loop) in clips.items():
        tr = arm.animation_data.nla_tracks.new(); tr.name = name
        st = tr.strips.new(name, 0, act); st.name = name
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=True,
                              export_animation_mode='NLA_TRACKS', export_skins=True, export_yup=True,
                              export_apply=False, export_cameras=False, export_lights=False,
                              export_force_sampling=True, export_optimize_animation_size=False,
                              export_frame_range=False)

def render_static(names):
    for nm in names:
        if nm not in POSES and nm in SLEEP_POSES:
            POSES[nm] = SLEEP_POSES[nm]()
        apply_basis(pose_basis(POSES[nm])[0])
        v = verts()
        print("CONTACT pose %-8s body minz %+.4f paws minz %+.4f tail minz %+.4f ALL minz %+.4f reachover %d max %.3f" % (
            nm, v[M_BODYFLOOR][:, 2].min(), v[M_PAWS][:, 2].min(), v[M_TAIL][:, 2].min(), v[:, 2].min(), REACH['over'], REACH['max']))
        print("CONTACT paws %s " % nm + " ".join("%s=%+.3f" % (k, v[M_PAW[k]][:, 2].min()) for k in ('FL', 'FR', 'HL', 'HR')) + " head=%+.3f" % v[M_SEG['oHd']][:, 2].min() + " off " + " ".join("%s=%+.3f" % (k, POSES[nm][k][2]) for k in SETTLE_ALL if k in POSES[nm]))
        print("HEADS %s " % nm + " ".join("%s=(%.2f,%.2f,%.2f)" % (b, *arm.pose.bones[b].head) for b in ('Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck', 'Head', 'Tail07', 'FrontUpperLeg.L', 'FrontUpperLeg.R', 'HindUpperLeg.L', 'HindUpperLeg.R', 'FrontPaw.L', 'FrontPaw.R')))
        lo, hi = v.min(axis=0), v.max(axis=0); ctr = (lo + hi) / 2
        render_pose("pose_%s_f000" % nm, center=(float(ctr[0]), float(ctr[1]), float(ctr[2])), scale=max(0.45, float((hi - lo).max()) * 1.2))

if __name__ == "__main__":
    if arg("--pose"):
        render_static(arg("--pose").split(","))
        sys.exit(0)
    only = ONLY.split(",") if ONLY else None
    clips = build_all(only)
    if SHOTS:
        render_clips(clips)
    if OUT and not ONLY:
        # measurements on the rest pose
        for pb in arm.pose.bones:
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
        bpy.context.view_layer.update()
        v = verts()
        head = v[M_HEAD & ~M_EAR]
        info = {
            'fps': FPS,
            'units': 'model units, cat faces -Y, floor z = 0; 1 unit is about 0.55 m for a 0.3 m tall cat',
            'floor_height': round(float(v[:, 2].min()), 4),
            'body_length_nose_to_tail_base': round(float(rhead['Tail01'].y - v[:, 1].min()), 4),
            'head_top_height': round(float(head[:, 2].max()), 4),
            'ear_top_height': round(float(v[M_EAR][:, 2].max()), 4),
            'shoulder_height': round(float(rhead['FrontUpperLeg.L'].z), 4),
            'walk_speed_units_per_s': round(WALK_STRIDE / WALK_T, 3),
            'walk_stride_units': WALK_STRIDE,
            'clips': {n: dict({'seconds': round(nfr / FPS, 4), 'frames': nfr, 'loop': lp}, **(
                {'lowest_vertex_z': round(MEAS[n]['min_z'], 4), 'footprint_x': round(MEAS[n]['x'][1] - MEAS[n]['x'][0], 4),
                 'footprint_y': round(MEAS[n]['y'][1] - MEAS[n]['y'][0], 4), 'head_height_start': round(MEAS[n]['head'][0], 4),
                 'head_height_end': round(MEAS[n]['head'][-1], 4), 'head_height_max': round(max(MEAS[n]['head']), 4)} if n in MEAS else {}))
                      for n, (a, nfr, lp) in clips.items()},
        }
        export(clips, OUT)
        json.dump(info, open(os.path.splitext(OUT)[0] + '.json', 'w'), indent=1)
        print('INFO', json.dumps(info))
