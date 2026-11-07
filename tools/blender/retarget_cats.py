"""Retarget the white cat's 14 clips onto the Somali, the cartoon cat and the striped cat4 (Blender 5.x, headless).

  blender.exe -b --python tools/blender/retarget_cats.py -- extract --src SRC.glb --out LANDMARKS.json
  blender.exe -b --python tools/blender/retarget_cats.py -- somali  --src LANDMARKS.json --in somali_cat_lie.glb --out OUT.glb [--shots DIR] [--only a,b] [--frames name:f,f;name:f]
  blender.exe -b --python tools/blender/retarget_cats.py -- cartoon --src LANDMARKS.json --dir CATDIR --out OUT.glb [--shots DIR] [--only a,b]
  blender.exe -b --python tools/blender/retarget_cats.py -- cat4    --src LANDMARKS.json --in cat4-3.glb --out cat4_shared.glb [--shots DIR] [--only a,b] [--frames name:f,f;name:f]

Stage 'extract' samples every clip of the white cat glb (30 fps) into a json: armature-space
matrices of its 35 bones plus the floor clearances of its mesh (per paw, torso, whole body).
The target stages rebuild each pose from those landmarks: spine and tail by chord directions
(rotation over the reference pose), head/ears/feet by world rotation deltas, legs by 2-bone IK
(positions relative to shoulder/hip scaled by leg length), then a per-frame floor solve (pelvis
height from the torso clearance, each paw from the source paw clearance times the scale).
Stage 'cat4' (rigged striped tabby, bone names identical to the white cat, faces +X, left +Y). cat4-3 rests in a neutral stand and
uses its own rest pose as the reference. With --legacy (cat4-2, rest pose a mid-stride walk) it first builds a neutral standing reference from the white cat's Idle frame 0 (tail by chord directions, legs by IK with
the source knee poles, all four paws put on z = 0) and uses that as the reference pose; each leg has its own scale
because the rig's left and right leg lengths differ.
Everything is in-place. Coordinates: floor z = 0 for the source (faces -Y, left +X).
"""
import bpy, sys, math, os, json, copy
import numpy as np
from mathutils import Vector, Quaternion, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = argv[0] if argv else ""
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
def apath(p):
    return os.path.abspath(p) if p else None
V = Vector
DEBUG = '--debug' in argv
FPS = 30
CLIPS = [('Idle', 3.0, True), ('Walk', 1.0, True), ('SitDown', 1.2, False), ('SittingIdle', 2.5, True),
         ('StandUp', 1.0, False), ('LieDown', 1.5, False), ('LyingIdle', 3.0, True), ('GetUp', 1.2, False),
         ('LoafDown', 1.5, False), ('LoafIdle', 3.0, True), ('LoafUp', 1.2, False), ('Jump', 0.8, False),
         ('Groom', 3.0, True), ('Stretch', 2.5, False)]
CLIP_FRAMES = {n: int(round(s * FPS)) for n, s, l in CLIPS}
LOOPS = {n: l for n, s, l in CLIPS}

def Rz(a): return Matrix.Rotation(math.radians(a), 3, 'Z')

# =============================================================== stage extract
def stage_extract():
    SRC = apath(arg('--src')); OUT = apath(arg('--out'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SRC)
    sc = bpy.context.scene
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    body = [o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices) > 1000][0]
    names = [b.name for b in arm.data.bones]
    rest = {n: [list(r) for r in arm.data.bones[n].matrix_local] for n in names}
    names_vg = [vg.name for vg in body.vertex_groups]
    dom = np.array([names_vg[max(v.groups, key=lambda g: g.weight).group] if len(v.groups) else '' for v in body.data.vertices])
    def msk(*pre): return np.array([d.startswith(pre) for d in dom])
    paws = {'FL': msk('FrontLowerLeg.L', 'FrontPaw.L', 'FrontToes.L'), 'FR': msk('FrontLowerLeg.R', 'FrontPaw.R', 'FrontToes.R'),
            'HL': msk('HindHock.L', 'HindPaw.L', 'HindToes.L'), 'HR': msk('HindHock.R', 'HindPaw.R', 'HindToes.R')}
    allpaw = msk('FrontPaw', 'FrontToes', 'HindPaw', 'HindToes')
    torso = msk('Pelvis', 'Spine0', 'Chest', 'Neck', 'Head', 'Scapula')
    allb = ~msk('Tail04', 'Tail05', 'Tail06', 'Tail07', 'Ear')
    for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
    for t in list(arm.animation_data.nla_tracks): t.mute = True
    arm.animation_data.action = None
    ratio = sc.render.fps / FPS
    out = {'bones': names, 'rest': rest, 'clips': {}}
    mw = np.array(body.matrix_world)
    def mesh_pts():
        ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get()); me = ev.to_mesh()
        a = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', a); ev.to_mesh_clear()
        return a.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    for cname, secs, loop in CLIPS:
        act = bpy.data.actions[cname]
        arm.animation_data.action = act
        try:
            arm.animation_data.action_slot = act.slots[0]
        except Exception:
            pass
        nf = CLIP_FRAMES[cname]
        frames = []
        for k in range(nf + 1):
            f = k * ratio
            sc.frame_set(int(f), subframe=f - int(f))
            bpy.context.view_layer.update()
            mats = {n: [list(map(lambda x: round(x, 5), r)) for r in arm.pose.bones[n].matrix] for n in names}
            P = mesh_pts()
            z = P[:, 2]
            fr = {'m': mats, 'torso': float(z[torso].min()), 'body': float(z[allb].min()),
                  'paw': {k2: float(z[m].min()) for k2, m in paws.items()}}
            frames.append(fr)
        out['clips'][cname] = frames
        print("EXTRACT", cname, nf + 1, "frames; frame0 torso %.3f body %.3f" % (frames[0]['torso'], frames[0]['body']))
    json.dump(out, open(OUT, 'w'))
    print("WROTE", OUT, os.path.getsize(OUT) // 1024, "KB")


# =============================================================== common engine
class Src:
    """Landmarks of the white cat per clip frame: H[bone] head, R[bone] world rotation (armature space)."""
    def __init__(self, path):
        d = json.load(open(path))
        self.clips = {}
        for cn, frames in d['clips'].items():
            L = []
            for fr in frames:
                H = {}; R = {}
                for n, m in fr['m'].items():
                    M = Matrix(m); H[n] = M.translation.copy(); R[n] = M.to_quaternion().to_matrix()
                L.append({'H': H, 'R': R, 'torso': fr['torso'], 'body': fr['body'], 'paw': fr['paw']})
            self.clips[cn] = L
        self.ref = self.clips['Idle'][0]

def poly_len(pts):
    c = [0.0]
    for a, b in zip(pts, pts[1:]): c.append(c[-1] + (b - a).length)
    return c
def poly_at(pts, c, u):
    t = u * c[-1]
    for i in range(len(pts) - 1):
        if t <= c[i + 1] + 1e-9:
            s = (t - c[i]) / max(c[i + 1] - c[i], 1e-9)
            return pts[i] + (pts[i + 1] - pts[i]) * s
    return pts[-1].copy()
def chord(pts, u0, u1):
    c = poly_len(pts)
    return poly_at(pts, c, u1) - poly_at(pts, c, u0)
def frac_bounds(pts):
    """Normalised arclength interval [u0,u1] of each segment of a polyline."""
    c = poly_len(pts)
    return [(c[i] / c[-1], c[i + 1] / c[-1]) for i in range(len(pts) - 1)]

def cdelta(C, a, b):
    """Rotation (target frame) that takes direction a to direction b (both source frame)."""
    return C @ a.rotation_difference(b).to_matrix() @ C.inverted()
def part(Dm, f):
    """Fraction f of a rotation matrix."""
    return Quaternion((1, 0, 0, 0)).slerp(Dm.to_quaternion(), f).to_matrix() if f < 0.999 else Dm
def rdelta(C, Rref, Rcur):
    return C @ (Rcur @ Rref.inverted()) @ C.inverted()

def ik2(P, l1, l2, T, pole):
    d = T - P; dl = d.length
    over = dl - (l1 + l2) * 0.9995
    dl = max(min(dl, (l1 + l2) * 0.9995), abs(l1 - l2) + 1e-4)
    axis = d.normalized()
    a = (l1 * l1 - l2 * l2 + dl * dl) / (2 * dl)
    h = math.sqrt(max(l1 * l1 - a * a, 0))
    pp = Vector(pole) - axis * Vector(pole).dot(axis)
    pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, 1))
    K = P + axis * a + pp * h
    return K, (K - P).normalized(), (P + axis * dl - K).normalized(), max(over, 0.0)

class Target:
    def __init__(self, arm, meshes):
        self.arm = arm; self.meshes = meshes
        b = arm.data.bones
        self.names = [x.name for x in b]
        self.parent = {x.name: (x.parent.name if x.parent else None) for x in b}
        self.rest = {x.name: x.matrix_local.copy() for x in b}
        self.order = []
        def walk(n):
            self.order.append(n)
            for c in b[n].children: walk(c.name)
        for x in b:
            if not x.parent: walk(x.name)
        for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
        self.rhead = {n: self.rest[n].translation.copy() for n in self.names}
        self.rrot = {n: self.rest[n].to_3x3() for n in self.names}
        self.over = 0.0
    def set_reference(self, posed):
        """Use an observed pose (armature-space matrices) as the reference pose of the retarget."""
        self.rhead = {n: m.translation.copy() for n, m in posed.items()}
        self.rrot = {n: m.to_3x3().normalized() for n, m in posed.items()}
    def solve(self, spec):
        loc = spec.get('loc', {}); ab = spec.get('abs', {})
        D = {}; posed = {}; basis = {}
        for n in self.order:
            p = self.parent[n]
            if p:
                head = posed[p].translation + D[p] @ (self.rhead[n] - self.rhead[p]); Dn = D[p]
            else:
                head = self.rhead[n].copy(); Dn = Matrix.Identity(3)
            if n in loc: head = Vector(loc[n])
            if n in ab: Dn = ab[n]
            D[n] = Dn
            posed[n] = Matrix.Translation(head) @ (Dn @ self.rrot[n]).to_4x4()
            inh = (posed[p] @ self.rest[p].inverted() @ self.rest[n]) if p else self.rest[n]
            bm = inh.inverted() @ posed[n]
            basis[n] = (bm.to_translation(), bm.to_quaternion())
        return basis, posed
    def apply(self, basis):
        for n, (l, q) in basis.items():
            pb = self.arm.pose.bones[n]; pb.location = l; pb.rotation_quaternion = q
        bpy.context.view_layer.update()
    def capture(self):
        """Armature-space matrices of the current pose."""
        return {pb.name: pb.matrix.copy() for pb in self.arm.pose.bones}
    # ---- mesh queries
    def setup_masks(self, trunk, paws, skip):
        dom = []
        for o in self.meshes:
            names = [vg.name for vg in o.vertex_groups]
            for v in o.data.vertices:
                dom.append(names[max(v.groups, key=lambda g: g.weight).group] if len(v.groups) else '')
        dom = np.array(dom)
        mk = lambda pre: np.array([d.startswith(tuple(pre)) for d in dom])
        self.M_trunk = mk(trunk); self.M_paws = {k: mk(v) for k, v in paws.items()}
        self.M_body = ~mk(skip)
        self.dom = dom
    def verts(self):
        dg = bpy.context.evaluated_depsgraph_get(); out = []
        for o in self.meshes:
            ev = o.evaluated_get(dg); me = ev.to_mesh()
            a = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', a); ev.to_mesh_clear()
            mw = np.array(o.matrix_world)
            out.append(a.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3])
        return np.concatenate(out)
    def measure(self):
        z = self.verts()[:, 2]
        return {'trunk': float(z[self.M_trunk].min()), 'body': float(z[self.M_body].min()),
                'paw': {k: float(z[m].min()) for k, m in self.M_paws.items()}}

# =============================================================== retargeter (shared by both targets)
SPINE_SRC = ['Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck', 'Head']
TAIL_SRC = ['Tail0%d' % i for i in range(1, 8)]

class Retarget:
    """cfg keys: C, root, rear[], fwd[] (bones) + fwd_end (bone whose head ends the forward polyline), head[], rots{tbone: srcbone},
    tail[] (+ implied tip), legs{'F'|'H': {side: dict(up, mid, eff, src=(S,E,W names), eff_loc, chord, foot_loc)}}, floor, paws groups."""
    def __init__(self, tgt, src, cfg):
        self.t = tgt; self.S = src; self.cfg = cfg; self.C = cfg['C']; self.floor = cfg['floor']
        t = tgt; ref = src.ref; C = self.C
        self.ref_m = None
        self.pk2leg = {k: k for k in ('FL', 'FR', 'HL', 'HR')}
        self.kf = self.kh = None
        self.clip = None
        # leg scales from the reference poses
        def dist(a, b): return (t.rhead[a] - t.rhead[b]).length
        L = cfg['legs']
        lf = L['F']['L']; lh = L['H']['L']
        self.kf = dist(lf['eff'], lf['up']) / (ref['H']['FrontPaw.L'] - ref['H']['FrontUpperLeg.L']).length
        self.kh = dist(lh['eff'], lh['up']) / (ref['H']['HindHock.L'] - ref['H']['HindUpperLeg.L']).length
        self.kb = 0.5 * (self.kf + self.kh)
        self.kl = {}      # per-leg scale (identical to kf/kh when the rig is left/right symmetric)
        for kind, sn in (('F', ('FrontPaw', 'FrontUpperLeg')), ('H', ('HindHock', 'HindUpperLeg'))):
            for sd, lg in L[kind].items():
                self.kl[kind + sd] = dist(lg['eff'], lg['up']) / (ref['H'][sn[0] + '.' + sd] - ref['H'][sn[1] + '.' + sd]).length
        self.kf = 0.5 * (self.kl['FL'] + self.kl['FR']) if 'FR' in self.kl else self.kf
        self.kh = 0.5 * (self.kl['HL'] + self.kl['HR']) if 'HR' in self.kl else self.kh
        self.kb = 0.5 * (self.kf + self.kh)
        print("SCALE kf %.3f kh %.3f kb %.3f" % (self.kf, self.kh, self.kb))

    def build_spec(self, fd, par):
        t = self.t; S = self.S; cfg = self.cfg; C = self.C; ref = S.ref
        H, R, RH, RR = fd['H'], fd['R'], ref['H'], ref['R']
        spec = {'loc': {}, 'abs': {}}; self.ovk = {'F': 0.0, 'H': 0.0}; self.clamped = {}
        off = self.kb * (C @ (H['Pelvis'] - RH['Pelvis']))
        root = cfg['root']
        spec['loc'][root] = t.rhead[root] + V((off.x, off.y, par['pz']))
        dP = rdelta(C, RR['Pelvis'], R['Pelvis'])
        for b in cfg['rear']: spec['abs'][b] = part(dP, par['sr'])
        # forward spine by chords over the source polyline
        ps_ref = [RH[n] for n in SPINE_SRC]; ps = [H[n] for n in SPINE_SRC]
        tp = [t.rhead[b] for b in cfg['fwd']] + [t.rhead[cfg['fwd_end']]]
        for b, (u0, u1) in zip(cfg['fwd'], frac_bounds(tp)):
            spec['abs'][b] = part(cdelta(C, chord(ps_ref, u0, u1), chord(ps, u0, u1)), par['sf'])
        for b in cfg['head']: spec['abs'][b] = rdelta(C, RR['Head'], R['Head'])
        for b, sb in cfg['rots'].items(): spec['abs'][b] = rdelta(C, RR[sb], R[sb])
        # tail
        tl = cfg['tail']
        if tl:
            tpt = [t.rhead[b] for b in tl]; tpt.append(tpt[-1] + (tpt[-1] - tpt[-2]))
            def ext(P):
                pts = [P[n] for n in TAIL_SRC]; pts.append(pts[-1] + (pts[-1] - pts[-2])); return pts
            sr, sc_ = ext(RH), ext(H)
            for b, (u0, u1) in zip(tl, frac_bounds(tpt)):
                spec['abs'][b] = cdelta(C, chord(sr, u0, u1), chord(sc_, u0, u1))
        # legs: pass 0 gives shoulder and hip heads
        _, posed0 = t.solve(spec)
        if tl:      # the tail must not sink through the floor: lift any segment whose end would go below it
            head = posed0[tl[0]].translation.copy()
            for i, b in enumerate(tl[:-1]):
                seg0 = t.rhead[tl[i + 1]] - t.rhead[b]; cur = spec['abs'][b] @ seg0
                if head.z + cur.z < self.floor + cfg.get('tail_margin', 0.02):
                    L = seg0.length; dz = max(-L * 0.999, min(L * 0.999, self.floor + cfg.get('tail_margin', 0.02) - head.z))
                    hv = V((cur.x, cur.y, 0)); hv = hv.normalized() if hv.length > 1e-6 else V((0, 0, 0))
                    cur = V((hv.x, hv.y, 0)) * math.sqrt(max(L * L - dz * dz, 0.0)) + V((0, 0, dz))
                    spec['abs'][b] = seg0.rotation_difference(cur).to_matrix()
                head = head + cur
            _, posed0 = t.solve(spec)
        for kind in ('F', 'H'):
            for side, lg in cfg['legs'][kind].items():
                k = self.kl[kind + side]
                sS, sE, sW = lg['src']
                Pt = posed0[lg['up']].translation     # the reference pose already carries the up_dz of stand_reference
                Sref, Eref, Wref = t.rhead[lg['up']], t.rhead[lg['mid']], t.rhead[lg['eff']]
                rel = (Wref - Sref) + k * (C @ ((H[sW] - H[sS]) - (RH[sW] - RH[sS])))
                T = Pt + rel
                T.z = Wref.z + k * (H[sW].z - RH[sW].z) + par['pawdz'][kind + side]
                pole = C @ (H[sE] - (H[sS] + H[sW]) * 0.5)
                if pole.length < 1e-4: pole = Eref - (Sref + Wref) * 0.5
                l1 = (Eref - Sref).length; l2 = (Wref - Eref).length
                # reach: pull the target in horizontally first (keeps the paw on the floor); only a shoulder that is
                # too high above the paw counts as overreach (the caller then flattens the spine pitch)
                maxr = (l1 + l2) * 0.997; dz = Pt.z - T.z; vo = 0.0
                if dz > maxr:
                    vo = dz - maxr; T.z = Pt.z - maxr; dz = maxr
                hx = V((T.x - Pt.x, T.y - Pt.y, 0)); av = math.sqrt(max(maxr * maxr - dz * dz, 0.0))
                if hx.length > av and hx.length > 1e-9:
                    hx = hx * (av / hx.length); T = V((Pt.x + hx.x, Pt.y + hx.y, T.z))
                Kp, d1, d2, ov = ik2(Pt, l1, l2, T, pole); ov = vo
                t.over += ov; self.ovk[kind] = max(self.ovk[kind], ov); self.clamped[kind + side] = vo > 0.0
                if DEBUG and vo > 0.01: print('   OVER %s%s S=%s T=%s l1=%.2f l2=%.2f vo=%.2f pz=%.2f sf=%.2f sr=%.2f' % (kind, side, tuple(round(x,2) for x in Pt), tuple(round(x,2) for x in T), l1, l2, vo, par['pz'], par['sf'], par['sr']))
                spec['abs'][lg['up']] = (Eref - Sref).rotation_difference(d1).to_matrix()
                spec['abs'][lg['mid']] = (Wref - Eref).rotation_difference(d2).to_matrix()
                W = Pt + d1 * l1 + d2 * l2
                if lg.get('eff_loc'): spec['loc'][lg['eff_loc']] = W
                for tb, sb in lg.get('rots', {}).items(): spec['abs'][tb] = rdelta(C, RR[sb], R[sb])
                if lg.get('chord'):
                    cb, a, b2 = lg['chord']
                    Dc = cdelta(C, RH[b2] - RH[a], H[b2] - H[a])
                    spec['abs'][cb] = Dc
                    if lg.get('foot_loc'):
                        fb, fr = lg['foot_loc']
                        spec['loc'][fb] = W + Dc @ (t.rhead[fb] - t.rhead[fr])
        return spec

    def measure_ref(self):
        self.ref_m = self.t.measure()
        return self.ref_m

    def solve_frame(self, fd):
        t = self.t; ref = self.S.ref; kb = self.kb; fl = self.floor; rm = self.ref_m
        par = {'sf': 1.0, 'sr': 1.0, 'pz': kb * (fd['H']['Pelvis'].z - ref['H']['Pelvis'].z), 'pawdz': {k: 0.0 for k in self.cfg['pawkeys']}}
        wt = max(fl, rm['trunk'] + kb * (fd['torso'] - ref['torso']))
        wp = {k: (fl if fd['paw'][k] < 0.03 else max(fl, rm['paw'][k] + kb * (fd['paw'][k] - ref['paw'][k]))) for k in rm['paw']}   # a source paw within 0.03 of the floor is planted
        pd = self.clip in self.cfg.get('pelvis_driven', ())
        def inner(par, iters=10):
            for it in range(iters):
                basis, posed = t.solve(self.build_spec(fd, par)); t.apply(basis)
                m = t.measure()
                et = wt - m['trunk']; ep = {k: wp[k] - m['paw'][k] for k in wp}
                if pd: et = max(0.0, fl + 0.03 - m['trunk'])      # pelvis follows the source pelvis; the trunk only has to stay off the floor
                if DEBUG: print('   ITER %d pz %.3f trunk %.3f want %.3f paws %s' % (it, par['pz'], m['trunk'], wt, {k: round(v, 3) for k, v in m['paw'].items()}))
                if abs(et) < 0.002 and max(abs(v) for v in ep.values()) < 0.002: break
                par['pz'] += et
                for k, v in ep.items():
                    if not self.clamped.get(k): par['pawdz'][k] += v
            basis, posed = t.solve(self.build_spec(fd, par)); t.apply(basis)
            m = t.measure()
            self.fl = {'F': 0.0, 'H': 0.0}
            for k in wp:     # a paw that hangs above its floor because the leg is at full stretch
                if self.clamped.get(k) or self.ovk[k[0]] > 0.0: self.fl[k[0]] = max(self.fl[k[0]], m['paw'][k] - wp[k])
            return basis
        basis = inner(par)
        # a shoulder or hip too high above its paw: flatten that half of the spine rotation (bisection on the fraction)
        for key, kind in (('sf', 'F'), ('sr', 'H')):
            if self.fl[kind] < 0.01: continue
            lo, hi = 0.0, 1.0
            for _ in range(6):
                mid = 0.5 * (lo + hi); par[key] = mid
                basis = inner(par)
                if self.fl[kind] < 0.01: lo = mid
                else: hi = mid
            par[key] = lo; basis = inner(par)
        if DEBUG:
            zz = t.verts()[:, 2]; ii = np.where(t.M_trunk)[0][np.argmin(zz[t.M_trunk])]
            print('   TRUNKMIN group %s z %.3f pelvis z %.3f' % (t.dom[ii], zz[ii], t.arm.pose.bones['Pelvis'].head.z))
        if DEBUG: print('   WANT trunk %.3f paws %s srcpaw %s refpaw %s' % (wt-fl, {k: round(v-fl,3) for k,v in wp.items()}, {k: round(v,3) for k,v in fd['paw'].items()}, {k: round(v,3) for k,v in ref['paw'].items()})); print('   FRAME sf %.2f sr %.2f fl %s pz %.3f pawdz %s clamped %s' % (par['sf'], par['sr'], {k: round(v, 3) for k, v in self.fl.items()}, par['pz'], {k: round(v, 3) for k, v in par['pawdz'].items()}, self.clamped))
        # never sink: the lowest body vertex must not go below the floor
        m = t.measure()
        if m['body'] < fl - 0.002:
            d = fl - m['body']
            par['pz'] += d
            for k in par['pawdz']: par['pawdz'][k] += d
            basis, posed = t.solve(self.build_spec(fd, par)); t.apply(basis)
            if DEBUG:
                m2 = t.measure(); zz = t.verts()[:, 2]; ii = np.where(t.M_body)[0][np.argmin(zz[t.M_body])]
                print('   LIFT %.3f -> body %.3f lowest vertex group %s' % (d, m2['body'] - fl, t.dom[ii]))
        return basis, par

# =============================================================== render, keyframing, export
_cam = None
def setup_render(floor):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x = 512; sc.render.resolution_y = 512
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_cavity = False
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.30, 0.38, 0.50)
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, floor - 0.002))
    fl = bpy.context.object; fl.name = "RenderFloor"
    mat = bpy.data.materials.new("fl"); mat.diffuse_color = (0.55, 0.50, 0.40, 1)
    fl.data.materials.append(mat)
    cam = bpy.data.cameras.new("cam"); cam.type = 'ORTHO'
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    return co

def render_views(tag, shots, C, floor, center, scale, views=('front34', 'side')):
    global _cam
    if _cam is None: _cam = setup_render(floor)
    os.makedirs(shots, exist_ok=True)
    for v in views:
        d0 = {'front34': V((2.2, -3.2, 1.7)), 'side': V((9, 0, 0.2))}[v]
        d = (C @ d0).normalized() * 40
        _cam.data.ortho_scale = scale
        _cam.location = V(center) + d
        _cam.rotation_euler = (V(center) - _cam.location).normalized().to_track_quat('-Z', 'Y').to_euler()
        bpy.context.scene.render.filepath = os.path.join(shots, "%s_%s.png" % (tag, v))
        bpy.ops.render.render(write_still=True)

def default_frames(cn, nf):
    if cn == 'Walk': return [0, 8, 15, 23, nf]
    return [0, nf // 2, nf]

def build_clips(rt, shots, only, frames_arg, cam):
    """Pose every frame of the chosen clips, key them, render the chosen frames. Returns {name: action}."""
    t = rt.t; arm = t.arm; acts = {}
    want = {}
    if frames_arg:
        for part in frames_arg.split(';'):
            n, fs = part.split(':'); want[n] = [int(x) for x in fs.split(',')]
    for cn, secs, loop in CLIPS:
        if only and cn not in only: continue
        nf = CLIP_FRAMES[cn]
        act = bpy.data.actions.new(cn); arm.animation_data.action = act
        prev = None; rf = want.get(cn, default_frames(cn, nf)) if shots else []
        t.over = 0.0; worst = 0.0
        for k in range(nf + 1):
            fd = rt.S.clips[cn][k]; rt.clip = cn
            basis, par = rt.solve_frame(fd)
            for n in t.order:
                l, q = basis[n]; q = q.copy()
                if prev is not None: q.make_compatible(prev[n])
                pb = arm.pose.bones[n]; pb.location = l; pb.rotation_quaternion = q
                pb.keyframe_insert('location', frame=k); pb.keyframe_insert('rotation_quaternion', frame=k)
            prev = {n: arm.pose.bones[n].rotation_quaternion.copy() for n in t.order}
            if k in rf:
                bpy.context.scene.frame_set(k)   # the action drives the pose at render time
                m = t.measure()
                print("  %-11s f%02d trunk %+.3f paws %s body %+.3f" % (cn, k, m['trunk'] - rt.floor,
                      " ".join("%s%+.3f" % (a, b - rt.floor) for a, b in m['paw'].items()), m['body'] - rt.floor))
                render_views("%s_f%02d" % (cn, k), shots, rt.C, rt.floor, cam[0], cam[1])
        print("CLIP %-11s %d frames, IK overreach total %.3f" % (cn, nf + 1, t.over))
        arm.animation_data.action = None
        act.use_fake_user = True
        acts[cn] = (act, nf, loop)
    return acts

def export_glb(arm, clips, out, scale24=False):
    arm.animation_data.action = None
    for nm in ('RenderFloor', 'cam'):
        if nm in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[nm], do_unlink=True)
    for name, (act, nfr, loop) in clips.items():
        tr = arm.animation_data.nla_tracks.new(); tr.name = name
        st = tr.strips.new(name, 0, act); st.name = name
        if scale24 and name.startswith('Orig_'): st.scale = 1.25
    for tr in arm.animation_data.nla_tracks:
        tr.mute = False
        for st in tr.strips:
            if st.name.startswith('Orig_'): st.frame_start_ui = 0
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=True,
                              export_animation_mode='NLA_TRACKS', export_skins=True, export_yup=True,
                              export_apply=False, export_cameras=False, export_lights=False,
                              export_force_sampling=True, export_optimize_animation_size=False,
                              export_frame_range=False)

def write_json(path, rt, vmeas, speed_note):
    d = {'fps': FPS, 'units': speed_note, 'floor_height': round(rt.floor, 4),
         'body_length_nose_to_tail_base': round(vmeas['len'], 4), 'head_top_height_above_floor': round(vmeas['head'], 4),
         'standing_trunk_clearance': round(rt.ref_m['trunk'] - rt.floor, 4),
         'scale_vs_whitecat': {'front': round(rt.kf, 4), 'hind': round(rt.kh, 4), 'body': round(rt.kb, 4)},
         'walk_speed_units_per_s': round(0.7 * rt.kb, 4), 'walk_stride_units': round(0.7 * rt.kb, 4),
         'clips': {n: {'seconds': s, 'frames': CLIP_FRAMES[n], 'loop': l} for n, s, l in CLIPS}}
    json.dump(d, open(path, 'w'), indent=1)

def ref_framing(t, C):
    P = t.verts(); lo = P.min(0); hi = P.max(0)
    c = (lo + hi) / 2; ext = float(max(hi - lo))
    return (tuple(c), ext * 1.25)

# =============================================================== stage somali
def stage_somali():
    SRC = Src(apath(arg('--src'))); IN = apath(arg('--in')); OUT = apath(arg('--out')); SHOTS = apath(arg('--shots'))
    ONLY = arg('--only').split(',') if arg('--only') else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=IN)
    sc = bpy.context.scene; sc.render.fps = FPS
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in ('Object_54', 'Icosphere')]
    BN = {b.name.rsplit('_', 1)[0]: b.name for b in arm.data.bones}
    B = lambda s: BN[s]
    orig = {a.name: a for a in bpy.data.actions}
    for tr in arm.animation_data.nla_tracks: tr.mute = True
    # Orig_ prefix on the original clips (24 fps frames -> strip scale 1.25 at 30 fps)
    for tr in arm.animation_data.nla_tracks:
        tr.name = 'Orig_' + tr.name
        for st in tr.strips: st.name = 'Orig_' + st.name; st.scale = 1.25
    for a in orig.values(): a.name = 'Orig_' + a.name
    tgt = Target(arm, meshes)
    # reference pose = original Idle frame 0
    a0 = bpy.data.actions['Orig_Idle']; arm.animation_data.action = a0
    try: arm.animation_data.action_slot = a0.slots[0]
    except Exception: pass
    sc.frame_set(0); bpy.context.view_layer.update()
    tgt.set_reference(tgt.capture())
    FLOOR = -1.762
    tgt.setup_masks(('LowerSpine', 'UpperSpine', 'Butt', 'Collarbone', 'Neck', 'Head'),
                    {'FL': ('Arm.L', 'Paw.L'), 'FR': ('Arm.R', 'Paw.R'), 'HL': ('Heel.L', 'Foot.L'), 'HR': ('Heel.R', 'Foot.R')},
                    ('Tail', 'Ear', 'Whisk', 'Eye'))
    legs = {'F': {}, 'H': {}}
    for sd in 'LR':
        legs['F'][sd] = dict(up=B('UpperArm.' + sd), mid=B('Arm.' + sd), eff=B('Paw.' + sd), eff_loc=B('Paw.' + sd),
                             src=('FrontUpperLeg.' + sd, 'FrontLowerLeg.' + sd, 'FrontPaw.' + sd),
                             rots={B('Paw.' + sd): 'FrontPaw.' + sd})
        legs['H'][sd] = dict(up=B('Hip.' + sd), mid=B('Thigh.' + sd), eff=B('Heel.' + sd),
                             src=('HindUpperLeg.' + sd, 'HindLowerLeg.' + sd, 'HindHock.' + sd),
                             chord=(B('Heel.' + sd), 'HindHock.' + sd, 'HindPaw.' + sd),
                             foot_loc=(B('Foot.' + sd), B('Heel.' + sd)), rots={B('Foot.' + sd): 'HindPaw.' + sd})
    cfg = dict(C=Rz(180), floor=FLOOR, root=B('Root'), rear=[B('LowerSpine'), B('Butt')],
               fwd=[B('UpperSpine'), B('Collarbone'), B('Neck')], fwd_end=B('Head'), head=[B('Head')],
               rots={B('Ear.L'): 'Ear.L', B('Ear.R'): 'Ear.R'},
               tail=[B('TailBase'), B('TailBaseMiddle'), B('TailTipMiddle'), B('TailTip')],
               legs=legs, pawkeys=['FL', 'FR', 'HL', 'HR'])
    rt = Retarget(tgt, SRC, cfg)
    arm.animation_data.action = None
    # reference pose measurement: apply the reference as an exact pose (D = identity)
    spec = {'loc': {}, 'abs': {}}
    basis, _ = tgt.solve(spec); tgt.apply(basis)
    m = rt.measure_ref()
    print("REF trunk %+.3f paws %s body %+.3f" % (m['trunk'] - FLOOR, {k: round(v - FLOOR, 3) for k, v in m['paw'].items()}, m['body'] - FLOOR))
    cam = ref_framing(tgt, cfg['C'])
    acts = build_clips(rt, SHOTS, ONLY, arg('--frames'), cam)
    if OUT and not ONLY:
        export_glb(arm, acts, OUT)
        P = tgt.verts(); f = cfg['C'] @ V((0, -1, 0))
        body = P[tgt.M_body]; proj = body @ np.array(f)
        hd = np.array([d.startswith('Head') for d in tgt.dom]); hz = P[hd][:, 2].max() - FLOOR
        write_json(os.path.splitext(OUT)[0] + '.json', rt, {'len': float(proj.max() - proj.min()), 'head': float(hz)},
                   'model units of the somali glb (about 0.12 m each), faces +Y, floor z = %s' % FLOOR)
        print("EXPORTED", OUT)



# =============================================================== stage cartoon
CARTOON_CLIPS = ['Idle', 'Walk', 'Jump', 'Sound', 'Eat']

def build_clean_cat(dirp):
    """Import cat_Idle.fbx (and the other four), rebuild a clean 43-bone armature with identity rest rotations and
    the mesh baked to world space with the Texture.png material. Returns (armature object, mesh object, {name: fbx armature})."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    fbx = {}
    for nm in CARTOON_CLIPS:
        before = set(bpy.data.objects.keys())
        bpy.ops.import_scene.fbx(filepath=os.path.join(dirp, 'cat_%s.fbx' % nm))
        new = [o for o in bpy.data.objects if o.name not in before]
        fbx[nm] = (next(o for o in new if o.type == 'ARMATURE' and o.name.startswith('TSMG_Cruft')),
                   next(o for o in new if o.type == 'MESH' and o.name.startswith('cat')))
    arm0, mesh0 = fbx['Idle']
    used = {vg.name for vg in mesh0.vertex_groups}
    keep = [b.name for b in arm0.data.bones if b.name in used or b.name == 'TSMGWorldJoint']
    def keep_parent(b):
        p = b.parent
        while p and p.name not in keep: p = p.parent
        return p.name if p else None
    mw0 = arm0.matrix_world
    info = {}
    for b in arm0.data.bones:
        if b.name in keep: info[b.name] = ((mw0 @ b.head_local).copy(), keep_parent(b))
    # rest-pose mesh in world space
    arm0.data.pose_position = 'REST'; bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(mesh0.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.transform(mesh0.evaluated_get(dg).matrix_world)
    arm0.data.pose_position = 'POSE'
    # clean armature
    ad = bpy.data.armatures.new('CatRig'); ao = bpy.data.objects.new('CatRig', ad)
    bpy.context.scene.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao; ao.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    def mk(n):
        if n in eb: return
        h, p = info[n]
        if p: mk(p)
        e = ad.edit_bones.new(n); e.head = h; e.tail = h + V((0, 0.03, 0)); e.roll = 0.0
        if p: e.parent = eb[p]
        eb[n] = e
    for n in keep: mk(n)
    bpy.ops.object.mode_set(mode='OBJECT')
    # mesh object
    mo = bpy.data.objects.new('cat', me); bpy.context.scene.collection.objects.link(mo)
    for vg in mesh0.vertex_groups: mo.vertex_groups.new(name=vg.name)
    md = mo.modifiers.new('Armature', 'ARMATURE'); md.object = ao
    mo.parent = ao
    mat = bpy.data.materials.new('CatMat'); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(os.path.join(dirp, 'Texture.png'))
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.85
    me.materials.clear(); me.materials.append(mat)
    return ao, mo, fbx, keep

def clean_pose(a, keep):
    """World pose of the kept FBX bones expressed for the clean armature (rest orientation identity):
    head position plus the world rotation change from the FBX rest pose (the Maya joint orients drop out)."""
    mw = a.matrix_world; out = {}
    for n in keep:
        M = mw @ a.pose.bones[n].matrix
        Rr = (mw @ a.data.bones[n].matrix_local).to_3x3().normalized()
        R = M.to_3x3().normalized() @ Rr.inverted()
        out[n] = Matrix.Translation(M.translation) @ R.to_4x4()
    return out

def bake_orig(ao, tgt, fbxarm, name, keep):
    """Key the FBX clip onto the clean armature (world pose of each kept bone -> basis relative to the clean rest)."""
    sc = bpy.context.scene; a, _m = fbxarm
    act0 = a.animation_data.action; f0, f1 = int(act0.frame_range[0]), int(act0.frame_range[1])
    act = bpy.data.actions.new('Orig_' + name); ao.animation_data_create(); ao.animation_data.action = act
    prev = None
    for f in range(f0, f1 + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        posed = clean_pose(a, keep)
        for n in tgt.order:
            p = tgt.parent[n]
            inh = (posed[p] @ tgt.rest[p].inverted() @ tgt.rest[n]) if p else tgt.rest[n]
            bm = inh.inverted() @ posed[n]
            q = bm.to_quaternion()
            if prev is not None: q.make_compatible(prev[n])
            pb = ao.pose.bones[n]; pb.location = bm.to_translation(); pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=f - f0); pb.keyframe_insert('rotation_quaternion', frame=f - f0)
        prev = {n: ao.pose.bones[n].rotation_quaternion.copy() for n in tgt.order}
    ao.animation_data.action = None; act.use_fake_user = True
    return act, f1 - f0, True

def stage_cartoon():
    SRC = Src(apath(arg('--src'))); DIR = apath(arg('--dir')); OUT = apath(arg('--out')); SHOTS = apath(arg('--shots'))
    ONLY = arg('--only').split(',') if arg('--only') else None
    ao, mo, fbx, keep = build_clean_cat(DIR)
    sc = bpy.context.scene; sc.render.fps = FPS
    tgt = Target(ao, [mo])
    # reference pose = original Idle frame 2 (the FBX rest pose has the tail up)
    a0 = fbx['Idle'][0]; sc.frame_set(2); bpy.context.view_layer.update()
    tgt.set_reference(clean_pose(a0, keep))
    ao.animation_data_create()
    orig = {}
    if not ONLY:    # the FBX imports make the scene slow (about 1000 animated empties): bake the originals, then drop them
        for nm in CARTOON_CLIPS:
            orig['Orig_' + nm] = bake_orig(ao, tgt, fbx[nm], nm, keep)
            print("ORIG", nm, orig['Orig_' + nm][1] + 1, "frames")
    for o in [o for o in bpy.data.objects if o not in (ao, mo)]:
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if not a.name.startswith('Orig_'): bpy.data.actions.remove(a)
    bpy.context.scene.frame_set(1)
    tgt.setup_masks(('spine1_', 'head1_', 'Jaw'),
                    {'FL': ('leftLeg1_HiResLowerLeg', 'leftLeg1_HiResFoot', 'leftLeg1_HiResToe'),
                     'FR': ('rightLeg1_HiResLowerLeg', 'rightLeg1_HiResFoot', 'rightLeg1_HiResToe'),
                     'HL': ('leftLeg2_HiResLowerLeg', 'leftLeg2_HiResFoot', 'leftLeg2_HiResToe'),
                     'HR': ('rightLeg2_HiResLowerLeg', 'rightLeg2_HiResFoot', 'rightLeg2_HiResToe')}, ('tail1_',))
    legs = {'F': {}, 'H': {}}
    for sd, nm in (('L', 'left'), ('R', 'right')):
        for kind, ln, sp, hp in (('F', 1, 'Front', 'Front'), ('H', 2, 'Hind', 'Hind')):
            pre = '%sLeg%d_HiRes' % (nm, ln)
            if kind == 'F':
                src = ('FrontUpperLeg.' + sd, 'FrontLowerLeg.' + sd, 'FrontPaw.' + sd)
                rots = {pre + 'Foot': 'FrontPaw.' + sd, pre + 'Toe': 'FrontToes.' + sd}
            else:
                src = ('HindUpperLeg.' + sd, 'HindLowerLeg.' + sd, 'HindHock.' + sd)
                rots = {pre + 'Foot': 'HindPaw.' + sd, pre + 'Toe': 'HindToes.' + sd}
            legs[kind][sd] = dict(up=pre + 'UpperLeg', mid=pre + 'LowerLeg', eff=pre + 'Foot', src=src, rots=rots)
    sp = ['spine1_hiResSpine%d' % i for i in range(1, 6)] + ['head1_neck']
    cfg = dict(C=Matrix.Identity(3), floor=0.0, root='spine1_hiResSpine1', rear=['spine1_hiResHips'],
               fwd=sp, fwd_end='head1_head', head=['head1_head'], rots={},
               tail=['tail1_Joint%d' % i for i in range(1, 10)], tail_margin=0.035, legs=legs, pawkeys=['FL', 'FR', 'HL', 'HR'])
    rt = Retarget(tgt, SRC, cfg)
    basis, _ = tgt.solve({'loc': {}, 'abs': {}}); tgt.apply(basis)
    m = rt.measure_ref()
    print("REF trunk %+.3f paws %s body %+.3f" % (m['trunk'], {k: round(v, 3) for k, v in m['paw'].items()}, m['body']))
    cam = ref_framing(tgt, cfg['C'])
    ao.animation_data_create()
    acts = build_clips(rt, SHOTS, ONLY, arg('--frames'), cam)
    if OUT and not ONLY:
        acts.update(orig)
        export_glb(ao, acts, OUT, scale24=True)
        P = tgt.verts(); f = V((0, -1, 0))
        body = P[tgt.M_body]; proj = body @ np.array(f)
        hd = np.array([d.startswith('head1_head') for d in tgt.dom]); hz = P[hd][:, 2].max()
        write_json(os.path.splitext(OUT)[0] + '.json', rt, {'len': float(proj.max() - proj.min()), 'head': float(hz)},
                   'metres of the FBX import (a 0.33 m shoulder-height cat), faces -Y, floor z = 0')
        print("EXPORTED", OUT)


# =============================================================== stage cat4
def stand_reference(tgt, SRC, C, legs, tail, front_back=0.0, shoulder_z=None, spine_level=None, head_pitch=0.0, hip_z=None, pelvis_lift=0.0, foot_under=None):
    """Neutral standing reference for a rig whose rest pose is a mid-stride walk. Tail directions follow the white cat's Idle
    frame 0, each leg is solved by 2-bone IK (source knee pole, source leg shape scaled to this rig's leg length) and its
    effector height is corrected (front paws are moved back by front_back to compensate the mesh) until the lowest vertex of each paw group sits on z = 0. Returns the posed matrices."""
    ref = SRC.ref; RH = ref['H']; bones = tgt.arm.data.bones
    def rdir(b): return (bones[b].tail_local - bones[b].head_local).normalized()
    spec = {'loc': {}, 'abs': {}}
    pts = [RH[n] for n in TAIL_SRC]; pts.append(pts[-1] + (pts[-1] - pts[-2]))
    tp = [tgt.rhead[b] for b in tail]; tp.append(tp[-1] + (tp[-1] - tp[-2]))
    for b, (u0, u1) in zip(tail, frac_bounds(tp)):
        spec['abs'][b] = rdir(b).rotation_difference(C @ chord(pts, u0, u1)).to_matrix()
    # spine: the rest pose is a mid-stride walk with a dipped chest, so level it with the white cat's Idle chord directions
    # (the head bone keeps its rest orientation so the face stays level); then put the shoulders at the wanted height
    sp_b = ['Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck']
    sp_src = [RH[n] for n in SPINE_SRC]
    sp_t = [tgt.rhead[b] for b in sp_b] + [tgt.rhead['Head']]
    for i, b in enumerate(sp_b):
        a = sp_t[i + 1] - sp_t[i]; g = C @ (sp_src[i + 1] - sp_src[i])
        if i == 0: g = V((g.x, g.y, g.z * pelvis_lift))
        elif spine_level is not None: g = V((g.x, g.y, g.z * spine_level))
        spec['abs'][b] = a.rotation_difference(g).to_matrix()
    spec['abs']['Head'] = Matrix.Rotation(math.radians(head_pitch), 3, 'Y')
    _, posed0 = tgt.solve(spec)
    if shoulder_z is not None:
        zs = posed0['Pelvis'].translation.z if hip_z is not None else 0.5 * (posed0['FrontUpperLeg.L'].translation.z + posed0['FrontUpperLeg.R'].translation.z)
        spec['loc']['Root'] = tgt.rhead['Root'] + V((0, 0, (hip_z if hip_z is not None else shoulder_z) - zs))
        _, posed0 = tgt.solve(spec)
    dz = {}; dxb = {}; shx = {}
    def legspec():
        sp = {'loc': dict(spec['loc']), 'abs': dict(spec['abs'])}
        for kind in ('F', 'H'):
            for sd, lg in legs[kind].items():
                sS, sE, sW = lg['src']
                Pt = posed0[lg['up']].translation + V((0, 0, lg.get('up_dz', 0.0))); sp['loc'][lg['up']] = Pt; shx[kind + sd] = Pt.x
                Sref, Eref, Wref = tgt.rhead[lg['up']], tgt.rhead[lg['mid']], tgt.rhead[lg['eff']]
                l1 = (Eref - Sref).length; l2 = (Wref - Eref).length
                k = (l1 + l2) / ((RH[sE] - RH[sS]).length + (RH[sW] - RH[sE]).length)
                T = Pt + k * (C @ (RH[sW] - RH[sS])); T.z += dz.get(kind + sd, 0.0)
                if kind == 'F': T += (C @ V((0, 1, 0))) * dxb.get(kind + sd, front_back)
                pole = C @ (RH[sE] - (RH[sS] + RH[sW]) * 0.5)
                Kp, d1, d2, ov = ik2(Pt, l1, l2, T, pole)
                if DEBUG: print('   STANDLEG %s%s P=%s T=%s K=%s l1=%.3f l2=%.3f k=%.3f over=%.3f' % (kind, sd, tuple(round(x, 3) for x in Pt), tuple(round(x, 3) for x in T), tuple(round(x, 3) for x in Kp), l1, l2, k, ov))
                sp['abs'][lg['up']] = (Eref - Sref).rotation_difference(d1).to_matrix()
                sp['abs'][lg['mid']] = (Wref - Eref).rotation_difference(d2).to_matrix()
                if kind == 'F':      # paw bone points like the source paw (head -> toes)
                    a = tgt.rhead[lg['toe']] - tgt.rhead[lg['eff']]
                    sp['abs'][lg['eff']] = a.rotation_difference(C @ (RH['FrontToes.' + sd] - RH['FrontPaw.' + sd])).to_matrix()
                else:                # hock -> paw like the source, then paw -> toes
                    a = tgt.rhead[lg['paw']] - tgt.rhead[lg['eff']]
                    sp['abs'][lg['eff']] = a.rotation_difference(C @ (RH['HindPaw.' + sd] - RH['HindHock.' + sd])).to_matrix()
                    a = tgt.rhead[lg['toe']] - tgt.rhead[lg['paw']]
                    sp['abs'][lg['paw']] = a.rotation_difference(C @ (RH['HindToes.' + sd] - RH['HindPaw.' + sd])).to_matrix()
        return sp
    for it in range(10):
        basis, posed = tgt.solve(legspec()); tgt.apply(basis)
        m = tgt.measure(); worst = 0.0
        for k, v in m['paw'].items():
            dz[k] = dz.get(k, 0.0) - v; worst = max(worst, abs(v))
        P = tgt.verts(); worstx = 0.0
        for k in ('FL', 'FR'):      # a foreleg hangs vertically: the foot's mesh sits under the shoulder joint
            if foot_under is None: break
            # the mesh forearm does not follow its bones (the left one leans forward about 45 degrees against 30 for the bones),
            # so measure the mesh: bottom half of the forearm vertices against its top half
            md = np.array([d == 'FrontLowerLeg.' + k[1] for d in tgt.dom]); zz = P[md][:, 2]; xx = P[md][:, 0]; zm = np.median(zz)
            ex = 0.8 * float(xx[zz <= zm].mean() - xx[zz > zm].mean() - foot_under)
            dxb[k] = dxb.get(k, front_back) + ex; worstx = max(worstx, abs(ex))
        print("STAND it%d paw heights %s foot dx %s" % (it, {k: round(v, 4) for k, v in m['paw'].items()}, {k: round(v, 3) for k, v in dxb.items()}))
        if worst < 0.0005 and worstx < 0.003: break
    basis, posed = tgt.solve(legspec()); tgt.apply(basis)
    return tgt.capture()

def stage_cat4():
    SRC = Src(apath(arg('--src'))); IN = apath(arg('--in', 'C:/_c/test1/blender_demo/output/cat4/cat4-3.glb')); LEGACY = '--legacy' in argv; OUT = apath(arg('--out')); SHOTS = apath(arg('--shots'))
    ONLY = arg('--only').split(',') if arg('--only') else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=IN)
    sc = bpy.context.scene; sc.render.fps = FPS
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for o in list(bpy.data.objects):        # a stray 42-vertex icosphere sits in the Sketchfab file
        if o.type == 'MESH' and o.name == 'Icosphere': bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and len(o.vertex_groups)]
    # cat4-3 comes with parent empties that rotate the armature and mesh by 90 degrees about X (Y-up glTF root), which makes armature
    # space Y-up while this script works Z-up (up = +Z, floor z): bake that rotation into bone rests and mesh data so world == armature space
    if max(abs(x) for r in (arm.matrix_world - Matrix.Identity(4)) for x in r) > 1e-4:
        bpy.ops.object.select_all(action='DESELECT')
        for o in [arm] + meshes: o.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bpy.context.view_layer.update()
        print('BAKED object transforms; arm world is now identity:', max(abs(x) for r in (arm.matrix_world - Matrix.Identity(4)) for x in r) < 1e-4)
    arm.animation_data_create()
    C = Rz(90)          # source faces -Y and its left is +X; cat4 faces +X and its left is +Y
    tgt = Target(arm, meshes)
    tgt.setup_masks(('Pelvis', 'Spine0', 'Chest', 'Neck', 'Head', 'Scapula'),
                    {'FL': ('FrontLowerLeg.L', 'FrontPaw.L', 'FrontToes.L'), 'FR': ('FrontLowerLeg.R', 'FrontPaw.R', 'FrontToes.R'),
                     'HL': ('HindHock.L', 'HindPaw.L', 'HindToes.L'), 'HR': ('HindHock.R', 'HindPaw.R', 'HindToes.R')},
                    ('Tail0', 'Ear'))      # the whole tail is ignored by the floor test: a sitting cat's tail base lies 2 cm in the floor rather than lifting every paw
    legs = {'F': {}, 'H': {}}
    for sd in 'LR':
        legs['F'][sd] = dict(up='FrontUpperLeg.' + sd, mid='FrontLowerLeg.' + sd, eff='FrontPaw.' + sd, eff_loc='FrontPaw.' + sd,
                             toe='FrontToes.' + sd, src=('FrontUpperLeg.' + sd, 'FrontLowerLeg.' + sd, 'FrontPaw.' + sd),
                             rots={'FrontPaw.' + sd: 'FrontPaw.' + sd, 'FrontToes.' + sd: 'FrontToes.' + sd})
        legs['H'][sd] = dict(up='HindUpperLeg.' + sd, mid='HindLowerLeg.' + sd, eff='HindHock.' + sd, paw='HindPaw.' + sd, toe='HindToes.' + sd,
                             src=('HindUpperLeg.' + sd, 'HindLowerLeg.' + sd, 'HindHock.' + sd),
                             chord=('HindHock.' + sd, 'HindHock.' + sd, 'HindPaw.' + sd),
                             foot_loc=('HindPaw.' + sd, 'HindHock.' + sd), rots={'HindPaw.' + sd: 'HindPaw.' + sd, 'HindToes.' + sd: 'HindToes.' + sd})
    tail = ['Tail0%d' % i for i in range(1, 8)]
    if LEGACY:      # cat4-2 only: mid-stride rest pose and uneven legs, so build a stand from the white cat's Idle frame 0
        for sd, sg in (('L', 1.0), ('R', -1.0)):
            legs['F'][sd]['up_dz'] = sg * float(os.environ.get('C4_FDZ', 0.03)); legs['H'][sd]['up_dz'] = sg * float(os.environ.get('C4_HDZ', 0.02))
        ref_m = stand_reference(tgt, SRC, C, legs, tail, front_back=float(os.environ.get('C4_FB', 0.08)), shoulder_z=float(os.environ.get('C4_HS', 0.385)), spine_level=float(os.environ.get('C4_SL', 1.0)), head_pitch=float(os.environ.get('C4_HP', 0.0)), hip_z=float(os.environ.get('C4_HZ', 0.382)), pelvis_lift=float(os.environ.get('C4_PL', 0.0)), foot_under=float(os.environ.get('C4_FU', 0.01)))
    else:           # cat4-3: the rig's own rest pose is a neutral stand (even legs, paws on z = 0), like the somali's Idle frame 0
        arm.animation_data.action = None
        for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
        bpy.context.view_layer.update()
        ref_m = tgt.capture()
    tgt.set_reference(ref_m)
    FLOOR = 0.0
    if DEBUG:
        for n in ('Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck', 'Head', 'FrontUpperLeg.L', 'FrontLowerLeg.L', 'FrontPaw.L', 'FrontToes.L', 'HindUpperLeg.L', 'HindLowerLeg.L', 'HindHock.L', 'HindPaw.L', 'HindToes.L'):
            h = ref_m[n].translation; print('   REFPT %-16s %.3f %.3f %.3f' % (n, h.x, h.y, h.z))
        for n in ('Pelvis', 'Spine01', 'Spine02', 'Chest', 'Neck', 'Head', 'FrontUpperLeg.L', 'FrontPaw.L', 'HindUpperLeg.L', 'HindHock.L', 'HindPaw.L'):
            h = C @ SRC.ref['H'][n]; print('   SRCPT %-16s %.3f %.3f %.3f' % (n, h.x, h.y, h.z))
    cfg = dict(C=C, floor=FLOOR, root=('Root' if 'Root' in tgt.rhead else 'Pelvis'), rear=['Pelvis'], fwd=['Spine01', 'Spine02', 'Chest', 'Neck'], fwd_end='Head', head=['Head'],
               rots={'Ear.L': 'Ear.L', 'Ear.R': 'Ear.R'},
               tail=tail, tail_margin=0.07, legs=legs, pawkeys=['FL', 'FR', 'HL', 'HR'],
               pelvis_driven=('Walk',) if '--no-pelvis-fix' not in argv else ())
    rt = Retarget(tgt, SRC, cfg)
    print("LEGSCALE", {k: round(v, 3) for k, v in rt.kl.items()})
    basis, _ = tgt.solve({'loc': {}, 'abs': {}}); tgt.apply(basis)
    m = rt.measure_ref()
    print("REF trunk %+.3f paws %s body %+.3f" % (m['trunk'] - FLOOR, {k: round(v - FLOOR, 3) for k, v in m['paw'].items()}, m['body'] - FLOOR))
    cam = ref_framing(tgt, C)
    if SHOTS and not ONLY: render_views('ref', SHOTS, C, FLOOR, cam[0], cam[1])
    acts = build_clips(rt, SHOTS, ONLY, arg('--frames'), cam)
    if OUT and not ONLY:
        export_glb(arm, acts, OUT)
        P = tgt.verts(); f = C @ V((0, -1, 0))
        body = P[tgt.M_body]; proj = body @ np.array(f)
        hd = np.array([d.startswith('Head') for d in tgt.dom]); hz = P[hd][:, 2].max() - FLOOR
        write_json(os.path.splitext(OUT)[0] + '.json', rt, {'len': float(proj.max() - proj.min()), 'head': float(hz)},
                   'model units of the cat4 glb (0.91 units from nose to tail base, shoulder height about 0.4), faces +X, floor z = %s' % FLOOR)
        print("EXPORTED", OUT)

if STAGE == 'extract':
    stage_extract()
elif STAGE == 'somali':
    stage_somali()
elif STAGE == 'cartoon':
    stage_cartoon()
elif STAGE == 'cat4':
    stage_cat4()
