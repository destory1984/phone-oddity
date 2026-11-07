"""Author lying animations for the Somali cat glb (Blender 5.x, headless).

  blender.exe -b --python tools/blender/somali_lie.py -- --in IN.glb --out OUT.glb [--shots DIR] [--render-only]

Adds LieDown, LyingIdle, GetUp, LoafIdle, LoafDown, LoafUp (and SleepCurl) to the
five original clips. Poses are authored by giving each bone a world direction
(aim solver) and keyed in local bone space, then exported as NLA tracks.
"""
import bpy, sys, math, os, json
from mathutils import Vector, Quaternion, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
IN = arg("--in"); OUT = arg("--out"); SHOTS = arg("--shots")
RENDER_ONLY = "--render-only" in argv
ONLY = arg("--only")  # comma list of clips to build/render
FPS = 24
FLOOR = -1.762  # lowest mesh vertex of the original Idle clip

# ---------------------------------------------------------------- load
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=IN)
scene = bpy.context.scene
scene.render.fps = FPS
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
ORIG_ACTIONS = [a.name for a in bpy.data.actions]
SKIP_MESH = ('Object_54', 'Icosphere')
body_meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in SKIP_MESH]

bones = {b.name: b for b in arm.data.bones}
def short(n):  # 'Hip.L_4' -> 'Hip.L'
    return n.rsplit('_', 1)[0]
BN = {short(n): n for n in bones}
rest = {n: b.matrix_local.copy() for n, b in bones.items()}
rest_head = {n: b.head_local.copy() for n, b in bones.items()}
blen = {n: b.length for n, b in bones.items()}
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
for _t in arm.animation_data.nla_tracks:   # authoring: nothing may overwrite the pose we set
    _t.mute = True
arm.animation_data.action = None

def B(s):
    return BN[s]

# ---------------------------------------------------------------- solver
def solve(spec):
    """spec: dict with 'loc' {bone: head pos}, 'dir' {bone: world dir of its Y axis},
    'roll' {bone: radians about own Y}, 'rotw' {bone: world Euler delta (applied about head)}.
    Returns (basis {bone: (loc, quat)}, posed {bone: 4x4 armature-space matrix})."""
    locs = spec.get('loc', {}); dirs = spec.get('dir', {}); rolls = spec.get('roll', {}); rotw = spec.get('rotw', {})
    posed = {}; basis = {}
    for n in order:
        p = parent[n]
        inh = (posed[p] @ rest[p].inverted() @ rest[n]) if p else rest[n].copy()
        t = inh.copy()
        loc = t.to_translation(); R = t.to_3x3()
        if n in locs:
            loc = Vector(locs[n])
        if n in dirs:
            cur = R @ Vector((0, 1, 0))
            q = cur.rotation_difference(Vector(dirs[n]).normalized())
            R = q.to_matrix() @ R
        if n in rotw:
            R = Euler(rotw[n], 'XYZ').to_matrix() @ R
        if n in rolls:
            R = R @ Matrix.Rotation(rolls[n], 3, 'Y')
        t = Matrix.Translation(loc) @ R.to_4x4()
        posed[n] = t
        bm = inh.inverted() @ t
        basis[n] = (bm.to_translation(), bm.to_quaternion())
    return basis, posed

def apply_basis(basis):
    for n, (l, q) in basis.items():
        pb = arm.pose.bones[n]
        pb.location = l; pb.rotation_quaternion = q
    bpy.context.view_layer.update()

def head_of(posed, n):
    return posed[n].translation.copy()
def tail_of(posed, n):
    return posed[n].translation + posed[n].to_3x3() @ Vector((0, blen[n], 0))

def sample_action_pose(act_name, frame):
    """Return basis dict read from an original action at a frame (the standing pose)."""
    arm.animation_data.action = bpy.data.actions[act_name]
    scene.frame_set(int(frame))
    out = {}
    for n in order:
        pb = arm.pose.bones[n]
        out[n] = (pb.location.copy(), pb.rotation_quaternion.copy())
    arm.animation_data.action = None
    return out

def mesh_min_z():
    dg = bpy.context.evaluated_depsgraph_get()
    mn = 1e9
    for o in body_meshes:
        ev = o.evaluated_get(dg); me = ev.to_mesh()
        mw = ev.matrix_world
        mn = min(mn, min((mw @ v.co).z for v in me.vertices))
        ev.to_mesh_clear()
    return mn

# ---------------------------------------------------------------- render
def setup_render():
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 512; scene.render.resolution_y = 512
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_cavity = False
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("w"); scene.world.color = (0.55, 0.62, 0.7)
    for o in bpy.data.objects:
        if o.name == 'Object_54':
            o.hide_render = True
    bpy.ops.mesh.primitive_plane_add(size=14, location=(0, -1.2, FLOOR - 0.002))
    fl = bpy.context.object; fl.name = "RenderFloor"
    mat = bpy.data.materials.new("fl"); mat.diffuse_color = (0.30, 0.34, 0.30, 1)
    fl.data.materials.append(mat)
    scene.display.shading.color_type = 'MATERIAL'
    cam = bpy.data.cameras.new("cam"); cam.type = 'ORTHO'
    co = bpy.data.objects.new("cam", cam); scene.collection.objects.link(co); scene.camera = co
    return co, fl

def set_cam(co, view, center, scale):
    co.data.ortho_scale = scale
    c = Vector(center)
    d = {'front34': Vector((3.2, 4.5, 2.6)), 'side': Vector((9, 0, 0.3)), 'top': Vector((0, 0.001, 9)),
         'back34': Vector((-3.2, -4.5, 2.6))}[view].normalized() * 20
    co.location = c + d
    look = (c - co.location).normalized()
    co.rotation_euler = look.to_track_quat('-Z', 'Y' if view != 'top' else 'Y').to_euler()

_cam_fl = None
def render_pose(tag, views=('front34', 'side', 'top'), center=(0, -1.1, -1.0), scale=8.5):
    global _cam_fl
    if _cam_fl is None:
        _cam_fl = setup_render()
    co, fl = _cam_fl
    os.makedirs(SHOTS, exist_ok=True)
    for v in views:
        set_cam(co, v, center, scale)
        scene.render.filepath = os.path.join(SHOTS, "%s_%s.png" % (tag, v))
        bpy.ops.render.render(write_still=True)

# ---------------------------------------------------------------- poses
V = Vector

# ---- helpers on top of the solver
_dom = None
def _dominant():
    global _dom
    if _dom is None:
        _dom = {}
        for o in body_meshes:
            lst = []
            for v in o.data.vertices:
                g = max(v.groups, key=lambda g: g.weight) if len(v.groups) else None
                lst.append(o.vertex_groups[g.group].name if g else '')
            _dom[o.name] = lst
    return _dom

def min_z(exclude_prefix=()):
    """Lowest world z of the deformed body, optionally ignoring vertices dominated by some bones."""
    dom = _dominant(); dg = bpy.context.evaluated_depsgraph_get(); mn = 1e9
    for o in body_meshes:
        ev = o.evaluated_get(dg); me = ev.to_mesh(); mw = ev.matrix_world
        for i, v in enumerate(me.vertices):
            if short(dom[o.name][i]).startswith(exclude_prefix) and exclude_prefix:
                continue
            z = (mw @ v.co).z
            if z < mn: mn = z
        ev.to_mesh_clear()
    return mn

TORSO = ('LowerSpine', 'UpperSpine', 'Butt', 'Collarbone', 'Neck', 'Head')
def group_minz():
    """Lowest world z per dominant bone (short name) of the deformed body."""
    dom = _dominant(); dg = bpy.context.evaluated_depsgraph_get(); out = {}
    for o in body_meshes:
        ev = o.evaluated_get(dg); me = ev.to_mesh(); mw = ev.matrix_world
        for i, v in enumerate(me.vertices):
            g = short(dom[o.name][i]) if dom[o.name][i] else '?'
            z = (mw @ v.co).z
            if g not in out or z < out[g]: out[g] = z
        ev.to_mesh_clear()
    return out

def torso_minz():
    gm = group_minz()
    return min(v for k, v in gm.items() if k in TORSO)

def report_floor():
    gm = group_minz()
    print("   floor contact (z - FLOOR, model units):", ", ".join("%s %+.3f" % (k, v - FLOOR) for k, v in sorted(gm.items()) if not k.startswith(('Eye', 'Ear', 'Whisk'))))

def ik2(P, l1, l2, T, pole):
    d = T - P; dl = d.length
    dl = min(dl, (l1 + l2) * 0.999); axis = d.normalized()
    a = (l1 * l1 - l2 * l2 + dl * dl) / (2 * dl)
    h = math.sqrt(max(l1 * l1 - a * a, 0))
    pp = Vector(pole) - axis * Vector(pole).dot(axis)
    pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, 1))
    K = P + axis * a + pp * h
    return (K - P), (P + axis * dl - K), K

def rotv(v, axis, ang):
    return Matrix.Rotation(ang, 3, axis) @ Vector(v)

def N(*a):
    return Vector(a).normalized()

def show(posed, names):
    for n in names:
        print("   %-12s head (%.2f %.2f %.2f) tail (%.2f %.2f %.2f)" % ((n,) + tuple(head_of(posed, B(n))) + tuple(tail_of(posed, B(n)))))

# ---------------------------------------------------------------- pose builder
PAW_Z = rest_head[B('Paw.L')].z      # paw/foot bone heads sit at sole + 0.15
FOOT_Z = rest_head[B('Foot.L')].z
SIDE = {'L': -1.0, 'R': 1.0}

def build(P, ground=True, verbose=False):
    """Pose spec from a parameter dict (see SPHINX). Returns (spec, basis, posed)."""
    D = P.get('D', 0.93)
    bl = lambda s: blen[B(s)]
    for it in range(4 if ground else 1):
        spec = {'loc': {B('Root'): rest_head[B('Root')] + V((P.get('rx', 0), P.get('ry', 0), -D))}, 'dir': {}, 'rotw': {}, 'roll': {}}
        dirs = spec['dir']
        for k in ('LowerSpine', 'UpperSpine', 'Butt', 'Collarbone', 'Neck', 'Head'):
            if k in P['spine']:
                dirs[B(k)] = N(*P['spine'][k])
        for i, k in enumerate(('TailBase', 'TailBaseMiddle', 'TailTipMiddle', 'TailTip')):
            if i < len(P.get('tail', [])):
                dirs[B(k)] = N(*P['tail'][i])
        for k, e in P.get('extra_dir', {}).items():
            dirs[B(k)] = N(*e)
        if 'tailh' in P:   # (yaw deg from straight back toward +x, height of the bone end above floor)
            for i, k in enumerate(('TailBase', 'TailBaseMiddle', 'TailTipMiddle', 'TailTip')):
                if i >= len(P['tailh']): break
                yaw, hh = P['tailh'][i]
                hd = head_of(solve(spec)[1], B(k)); L = bl(k)
                dz = max(-L * 0.98, min(L * 0.98, (FLOOR + hh) - hd.z))
                hl = math.sqrt(L * L - dz * dz); a = math.radians(yaw)
                dirs[B(k)] = V((math.sin(a) * hl, -math.cos(a) * hl, dz)).normalized()
        posed0 = solve(spec)[1]
        for sd, sx in SIDE.items():
            # front leg: elbow and wrist from direction hints
            f = P['front']
            sh = head_of(posed0, B('UpperArm.' + sd)) + V((sx * f.get('sdx', 0), f.get('sdy', 0), -f.get('sd', 0)))
            if any(k in f for k in ('sd', 'sdx', 'sdy')):
                spec['loc'][B('UpperArm.' + sd)] = sh
            E = V((sh.x + sx * f['ex'], sh.y - f['ey'], FLOOR + f['ez']))
            d1 = (E - sh).normalized(); dirs[B('UpperArm.' + sd)] = d1
            elbow = sh + d1 * bl('UpperArm.L')
            d2 = N(sx * f['fx'], f['fy'], f['fz']); dirs[B('Arm.' + sd)] = d2
            wrist = elbow + d2 * f['flen']
            spec['loc'][B('Paw.' + sd)] = V((wrist.x, wrist.y, FLOOR + f['pz']))
            if 'pdir' in f:
                dirs[B('Paw.' + sd)] = N(sx * f['pdir'][0], f['pdir'][1], f['pdir'][2])
            # hind leg: 2-bone ik hip -> hock
            h = P['hind']
            hp = head_of(posed0, B('Hip.' + sd)) + V((sx * h.get('hdx', 0), h.get('hdy', 0), -h.get('hd', 0)))
            if any(k in h for k in ('hd', 'hdx', 'hdy')):
                spec['loc'][B('Hip.' + sd)] = hp
            T = V((hp.x + sx * h['hx'], hp.y - h['hy'], FLOOR + h['hz']))
            d1, d2, K = ik2(hp, bl('Hip.L'), bl('Thigh.L'), T, V((sx * h['pole'][0], h['pole'][1], h['pole'][2])))
            dirs[B('Hip.' + sd)] = d1; dirs[B('Thigh.' + sd)] = d2
            hd = N(sx * h['heel'][0], h['heel'][1], h['heel'][2]); dirs[B('Heel.' + sd)] = hd
            hock = hp + d1 * bl('Hip.L'); hock = hock + d2 * bl('Thigh.L')
            fz = (FLOOR + h['fz']) if 'fz' in h else hock.z + hd.z * h['ffrac']
            spec['loc'][B('Foot.' + sd)] = V((hock.x + hd.x * h['ffrac'], hock.y + hd.y * h['ffrac'], fz))
            if 'fdir' in h:
                dirs[B('Foot.' + sd)] = N(sx * h['fdir'][0], h['fdir'][1], h['fdir'][2])
        basis, posed = solve(spec)
        if not ground:
            break
        apply_basis(basis)
        mz = torso_minz()
        if verbose: print("   ground iter", it, "minz(torso)", round(mz, 3), "D", round(D, 3))
        if abs(mz - FLOOR) < 0.004:
            break
        D += mz - FLOOR
    P['D'] = D
    return spec, basis, posed

# ---------------------------------------------------------------- pose parameters
import copy
SPHINX = {
 'D': 1.04,
 'spine': {'LowerSpine': (0, -1, -0.06), 'UpperSpine': (0, 1, 0.04), 'Butt': (0, -1, -0.3), 'Collarbone': (0, 1, 0.1), 'Neck': (0, 0.5, 0.87), 'Head': (0, 1, -0.02)},
 'tailh': [(0, 0.80), (30, 0.76), (75, 0.58), (125, 0.22)],
 'front': {'sd': 0.2, 'ex': 0.08, 'ey': 0.28, 'ez': 0.28, 'fx': 0.03, 'fy': 1, 'fz': 0.0, 'flen': 0.95, 'pz': 0.156},
 'hind': {'hx': 0.2, 'hy': 0.35, 'hz': 0.10, 'pole': (0.25, 1, 0.5), 'heel': (0.05, 1, 0.0), 'ffrac': 0.42, 'fz': 0.149},
}
LOAF = {
 'D': 1.04,
 'spine': {'LowerSpine': (0, -1, 0.12), 'UpperSpine': (0, 1, 0.12), 'Butt': (0, -1, -0.5), 'Collarbone': (0, 1, 0.2), 'Neck': (0, 0.3, 0.95), 'Head': (0, 1, -0.05)},
 'tailh': [(50, 0.56), (175, 0.62), (185, 0.46), (230, 0.20)],
 'front': {'sd': 0.2, 'ex': -0.15, 'ey': 0.55, 'ez': 0.28, 'fx': -0.25, 'fy': 1, 'fz': 0.0, 'flen': 0.15, 'pz': 0.156},
 'hind': {'hx': -0.32, 'hy': 0.30, 'hz': 0.10, 'pole': (0.0, 1, 0.5), 'heel': (-0.3, 1, 0.0), 'ffrac': 0.42, 'fz': 0.149},
}

# ---------------------------------------------------------------- keyframing
def make_action(name, keys):
    """keys: [(frame, basis)] -> new action with loc+quat keys on every bone (smooth Bezier)."""
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    prev = None
    for f, basis in keys:
        for n in order:
            l, q = basis[n]
            q = q.copy()
            if prev is not None:
                q.make_compatible(prev[n])
            pb = arm.pose.bones[n]
            pb.location = l; pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=f)
            pb.keyframe_insert('rotation_quaternion', frame=f)
        prev = {n: basis[n][1] for n in order}
        prev = {n: arm.pose.bones[n].rotation_quaternion.copy() for n in order}
    arm.animation_data.action = None
    act.use_fake_user = True
    return act

def fix_ground_shift(P):
    return P

def lying_variant(P0, ph, tail_idx=(2, 3), head_deg=10.0, breath=0.04):
    """Loop variation of a base pose at phase ph (radians). Zero at ph=0 so the base pose is frame 0."""
    P = copy.deepcopy(P0)
    sp = P['spine']
    b = math.sin(ph)
    up = Vector(sp['UpperSpine']); up.z += breath * b; sp['UpperSpine'] = tuple(up)
    co = Vector(sp['Collarbone']); co.z += breath * 0.6 * b; sp['Collarbone'] = tuple(co)
    lo = Vector(sp['LowerSpine']); lo.z -= breath * 0.25 * b; sp['LowerSpine'] = tuple(lo)
    # keep the head level against the chest rise
    sp['Head'] = tuple(rotv(sp['Head'], 'X', -breath * 0.8 * b))
    sp['Neck'] = tuple(rotv(sp['Neck'], 'Z', math.radians(head_deg * 0.3) * math.sin(ph)))
    sp['Head'] = tuple(rotv(sp['Head'], 'Z', math.radians(head_deg) * math.sin(ph)))
    th = [list(x) for x in P['tailh']]
    amp = {2: 7.0, 3: 16.0}
    for i in tail_idx:
        th[i][0] += amp[i] * (math.sin(ph - 0.7 * (i - 1)) + math.sin(0.7 * (i - 1)))   # zero at ph=0
    P['tailh'] = [tuple(x) for x in th]
    return P

def loop_keys(P0, frames, step=4, **kw):
    P0 = copy.deepcopy(P0)
    keys = []
    for f in list(range(0, frames, step)) + [frames]:
        ph = 2 * math.pi * (f % frames) / frames
        P = lying_variant(P0, ph, **kw)
        P['D'] = P0['D']
        _, basis, _ = build(P, ground=False)
        keys.append((f, basis))
    return keys

# ---------------------------------------------------------------- main
NEW = []   # (name, frames, looping)

def build_all(only=None):
    stand = sample_action_pose('Idle', 0)
    out = {}
    def want(n): return only is None or n in only
    Ps = copy.deepcopy(SPHINX); _, sph, _ = build(Ps, verbose=True)
    Pl = copy.deepcopy(LOAF); _, loaf, _ = build(Pl, verbose=True)
    if want('LieDown'):
        out['LieDown'] = (make_action('LieDown', [(0, stand), (36, sph)]), 36, False)
    if want('LyingIdle'):
        out['LyingIdle'] = (make_action('LyingIdle', loop_keys(Ps, 60)), 60, True)
    if want('GetUp'):
        out['GetUp'] = (make_action('GetUp', [(0, sph), (29, stand)]), 29, False)
    if want('LoafDown'):
        out['LoafDown'] = (make_action('LoafDown', [(0, stand), (36, loaf)]), 36, False)
    if want('LoafIdle'):
        out['LoafIdle'] = (make_action('LoafIdle', loop_keys(Pl, 60)), 60, True)
    if want('LoafUp'):
        out['LoafUp'] = (make_action('LoafUp', [(0, loaf), (29, stand)]), 29, False)
    return out

def render_clips(clips, frames_per_clip=None):
    for name, (act, nfr, loop) in clips.items():
        arm.animation_data.action = act
        fl = frames_per_clip.get(name) if frames_per_clip else None
        if fl is None:
            fl = [0, nfr // 2, nfr] if not loop else [0, nfr // 4, nfr // 2, nfr - 1]
        for f in fl:
            scene.frame_set(f)
            gm = group_minz()
            print("CONTACT %-10s f%-3d torso %+.3f  all %+.3f  (model units, 1 unit = ~0.12 m)" % (name, f, min(v for k, v in gm.items() if k in TORSO) - FLOOR, min(gm.values()) - FLOOR))
            render_pose("%s_f%02d" % (name, f), views=('front34', 'side', 'top'), center=(0, -0.2, -1.0), scale=5.8)
            if f == fl[-1]:
                render_pose("%s_f%02d_wide" % (name, f), views=('top',), center=(0, -1.8, -1.0), scale=9.5)
        arm.animation_data.action = None

def export(clips, out):
    arm.animation_data.action = None
    for nm in ('RenderFloor', 'cam'):       # render helpers must not reach the glb
        if nm in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[nm], do_unlink=True)
    for t in arm.animation_data.nla_tracks:
        t.mute = False
        for st in t.strips:                   # importer puts strips at frame 1; clips must start at time 0
            st.frame_start_ui = 0
    for name, (act, nfr, loop) in clips.items():
        tr = arm.animation_data.nla_tracks.new(); tr.name = name
        st = tr.strips.new(name, 0, act); st.name = name
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=True,
                              export_animation_mode='NLA_TRACKS', export_skins=True, export_yup=True,
                              export_apply=False, export_cameras=False, export_lights=False,
                              export_force_sampling=True, export_optimize_animation_size=False,
                              export_frame_range=False)

if __name__ == "__main__" or True:
    if arg("--stage") != "lib":
        only = ONLY.split(",") if ONLY else None
        clips = build_all(only)
        if SHOTS:
            _cam_fl = None
            render_clips(clips)
        if OUT and not ONLY:
            export(clips, OUT)
