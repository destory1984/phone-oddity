using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Lying on the bed: measuring the skinned body surface (not the bones) and keeping the legs from crossing. Pure helpers, used by RoomPlay and by the EditMode tests.
    public sealed class BedProbe
    {
        readonly Mesh bake = new Mesh(); readonly List<Vector3> verts = new List<Vector3>(); readonly SkinnedMeshRenderer[] meshes; readonly int[] bins = new int[80];
        public const float BinSize = .004f, Outlier = .003f, MaxSink = .02f, HeadRadius = .11f;
        SkinnedMeshRenderer skin;
        // the skin is the largest mesh: clothes (a skirt's frayed hem hangs well below the body), shoes and hair do not hold her up, the skin does
        public BedProbe(GameObject model)
        {
            meshes = model.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null && !Loose(r.name)).ToArray();
            skin = meshes.OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            support = skin != null ? new[] { skin } : meshes;
        }
        readonly SkinnedMeshRenderer[] support;
        public int Count => meshes.Length;
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in meshes) { r.BakeMesh(bake); bake.GetVertices(verts); var m = r.transform.localToWorldMatrix; float lo = float.PositiveInfinity, hi = float.NegativeInfinity; foreach (var v in verts) { float y = m.MultiplyPoint3x4(v).y; if (y < lo) lo = y; if (y > hi) hi = y; } sb.Append(r.name + "[n=" + verts.Count + " y=" + lo.ToString("0.000") + ".." + hi.ToString("0.000") + " boundsMinY=" + r.bounds.min.y.ToString("0.000") + " scale=" + r.transform.lossyScale.x.ToString("0.00") + "] "); }
            return sb.ToString();
        }
        // hair and loose accessories hang or float: they do not hold the body up
        public static bool Loose(string n) { n = n.ToLowerInvariant(); return n.Contains("hair") || n.Contains("bang") || n.Contains("tail") || n.Contains("ribbon") || n.Contains("shadow") || n.Contains("outline"); }
        public static Vector3 HeadCentre(Transform head, Transform neck) => neck != null ? head.position + (head.position - neck.position).normalized * .09f : head.position + Vector3.up * .09f;

        // lowestY: the level under all but 0.3% of the body's vertices, never more than 2 cm above the true lowest vertex (so outliers sink at most 2 cm);
        // headLowY: the lowest vertex within 11 cm of the head's centre (hair included: it lies on the pillow too)
        public void Measure(Vector3 headCentre, out float lowestY, out float headLowY, out float trueLowY)
        {
            float lo = float.PositiveInfinity, hl = float.PositiveInfinity; int n = 0;
            foreach (var r in support) { r.BakeMesh(bake); bake.GetVertices(verts); var m = r.transform.localToWorldMatrix; foreach (var v in verts) { var p = m.MultiplyPoint3x4(v); if (p.y < lo) lo = p.y; n++; } }
            System.Array.Clear(bins, 0, bins.Length);
            foreach (var r in support) { r.BakeMesh(bake); bake.GetVertices(verts); var m = r.transform.localToWorldMatrix; foreach (var v in verts) { var p = m.MultiplyPoint3x4(v); int b = (int)((p.y - lo) / BinSize); if (b >= 0 && b < bins.Length) bins[b]++; } }
            foreach (var r in meshes) { r.BakeMesh(bake); bake.GetVertices(verts); var m = r.transform.localToWorldMatrix; foreach (var v in verts) { var p = m.MultiplyPoint3x4(v); if ((p - headCentre).sqrMagnitude < HeadRadius * HeadRadius && p.y < hl) hl = p.y; } }
            float level = lo + MaxSink; int sum = 0, need = Mathf.Max(1, Mathf.CeilToInt(n * Outlier));
            for (int i = 0; i < bins.Length; i++) { sum += bins[i]; if (sum >= need) { level = lo + (i + 1) * BinSize; break; } }
            trueLowY = lo; lowestY = Mathf.Min(level, lo + MaxSink); headLowY = hl;
        }
    }

    public static class BedLie
    {
        static float SegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2; float a = d1.sqrMagnitude, e = d2.sqrMagnitude, f = Vector3.Dot(d2, r), s, t;
            if (a < 1e-9f && e < 1e-9f) { c1 = p1; c2 = p2; return (p1 - p2).magnitude; }
            if (a < 1e-9f) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-9f) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-9f ? Mathf.Clamp01((b * f - c * e) / den) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); } else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t; return (c1 - c2).magnitude;
        }

        public const float KneeRadius = .06f, AnkleRadius = .045f;
        // Side-lying (K curl): the leg underneath lies on the quilt along its length. The thigh is turned about the hip so the knee centre is a knee radius over the quilt (the thigh keeps its horizontal
        // direction), then the shin so the ankle is an ankle radius over it; the clip hovers, so without this the shins hang in the air. The upper leg is stacked on it by Uncross afterwards.
        // Returns the index (0 left, 1 right) of the leg put down, or -1 when she does not lie on her side.
        public static int LegDown(Animator a, float w, float quiltTop)
        {
            var ul = new Transform[2]; var ll = new Transform[2]; var ft = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                ul[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                ll[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                ft[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (ul[i] == null || ll[i] == null || ft[i] == null) return -1;
            }
            var s = ul[1].position - ul[0].position; if (s.magnitude < .02f || Mathf.Abs(s.normalized.y) < .6f) return -1; // the hip axis is vertical when she lies on her side
            int lo = ul[0].position.y <= ul[1].position.y ? 0 : 1;
            var hip = ul[lo].position; var knee = ll[lo].position; float l1 = (knee - hip).magnitude;
            var h = knee - hip; h.y = 0; h = h.sqrMagnitude < 1e-6f ? a.transform.forward : h.normalized;
            float dy = Mathf.Clamp(quiltTop + KneeRadius - hip.y, -l1 * .98f, l1 * .98f);
            var want = hip + h * Mathf.Sqrt(l1 * l1 - dy * dy) + Vector3.up * dy;
            ul[lo].rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(knee - hip, want - hip), w) * ul[lo].rotation;
            knee = ll[lo].position; var ank = ft[lo].position; float l2 = (ank - knee).magnitude;
            var h2 = ank - knee; h2.y = 0; h2 = h2.sqrMagnitude < 1e-6f ? h : h2.normalized;
            float dy2 = Mathf.Clamp(quiltTop + AnkleRadius - knee.y, -l2 * .98f, l2 * .98f);
            var want2 = knee + h2 * Mathf.Sqrt(l2 * l2 - dy2 * dy2) + Vector3.up * dy2;
            ll[lo].rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(ank - knee, want2 - knee), w) * ll[lo].rotation;
            return lo;
        }

        public const float ShinClear = .11f, AnkleClear = .10f, KneeClear = .09f;
        // The clips are posed on a body whose legs may pass through each other. Left on its own side of the right leg: the hip axis (left hip -> right hip) is the lateral axis (it is
        // vertical when she lies on her side), the knees and the ankles must be ordered along it like the hips, the shins at least ShinClear apart and the ankles AnkleClear apart.
        // The leg that is higher moves when the pair is stacked (the lower one rests on the quilt). Returns how far the worst pair was apart before the fix (negative = crossed).
        public static float Uncross(Animator a, float w)
        {
            var ul = new Transform[2]; var ll = new Transform[2]; var ft = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                ul[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                ll[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                ft[i] = a.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (ul[i] == null || ll[i] == null || ft[i] == null) return float.NaN;
            }
            var s = ul[1].position - ul[0].position; float width = s.magnitude; if (width < .02f) return float.NaN; s /= width;
            float worst = float.PositiveInfinity;
            for (int pass = 0; pass < 3; pass++)
            {
                // knees: ordered along the hip axis, KneeClear apart; the knee is swung round the hip-ankle line towards its own side
                float kd = Vector3.Dot(ll[1].position - ll[0].position, s);
                if (pass == 0) worst = Mathf.Min(worst, kd);
                if (kd < KneeClear)
                    for (int i = 0; i < 2; i++)
                    {
                        float sg = i == 0 ? -1 : 1;
                        SofaRig.TwoBone(ul[i], ll[i], ft[i], ft[i].position, ll[i].position + s * sg * (KneeClear - kd) * 6f, w);
                    }
                // shins and ankles
                float d = SegDist(ll[0].position, ft[0].position, ll[1].position, ft[1].position, out var c0, out var c1);
                float ad = Vector3.Dot(ft[1].position - ft[0].position, s);
                if (pass == 0) worst = Mathf.Min(worst, Mathf.Min(d - ShinClear, ad));
                float lack = Mathf.Max(ShinClear - d, AnkleClear - ad);
                if (lack <= .002f) continue;
                var n = c1 - c0; if (n.sqrMagnitude < 1e-6f) n = s; n.Normalize();
                if (Vector3.Dot(n, s) < 0) n = -n; // the right leg goes to the right side of the left one
                for (int i = 0; i < 2; i++)
                {
                    float sg = i == 0 ? -1 : 1; float share = .5f;
                    if (Mathf.Abs(n.y) > .5f) share = (ft[i].position.y + ll[i].position.y > ft[1 - i].position.y + ll[1 - i].position.y) ? 1f : 0f; // stacked: only the upper leg moves
                    if (share <= 0) continue;
                    SofaRig.TwoBone(ul[i], ll[i], ft[i], ft[i].position + n * sg * lack * share, ll[i].position, w);
                }
            }
            return worst;
        }
    }

    // The final lying placement, run after the pose is complete (see RoomPlay.LieFinish): legs uncrossed, neck bent so the head's lowest vertex is 2 cm into the pillow top,
    // head over the pillow, then the model lifted so the body surface's lowest level meets the quilt.
    public sealed class LieRig
    {
        readonly BedProbe probe; readonly Animator anim; readonly Transform root;
        public bool LegsOnQuilt; // the K curl clip: its lower leg is laid on the quilt (other lying clips keep their own legs)
        float lastLift = float.NaN; // how far the last Place lifted the root from where the pose left it
        public float Neck, LowRel = float.NaN, HeadRel, TrueRel, LegsApart = float.NaN; int frame;
        public LieRig(GameObject model, Animator anim) { probe = new BedProbe(model); this.anim = anim; root = model.transform; }
        public int Meshes => probe.Count; public string Describe() => probe.Describe();
        public float LowestY => LowRel + root.position.y; public float TrueLowestY => TrueRel + root.position.y; public float HeadLowY => HeadRel + root.position.y;
        public bool Place(float w, bool pillow, float quiltTop, bool legs, bool forceBake)
        {
            var head = anim.GetBoneTransform(HumanBodyBones.Head); var neck = anim.GetBoneTransform(HumanBodyBones.Neck); var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (head == null || hips == null) return false;
            if (legs && LegsOnQuilt && quiltTop > 0 && pillow && !float.IsNaN(lastLift)) BedLie.LegDown(anim, w, quiltTop - lastLift); // the pose is made with the root on the floor, the root is lifted to the quilt afterwards: the leg is aimed at the quilt as the last frame lifted it
            LegsApart = legs ? BedLie.Uncross(anim, w) : float.NaN;
            pillow &= Apartment.HasPillow;
            if (pillow && neck != null && Mathf.Abs(Neck) > .01f)
            {
                var u = head.position - hips.position; u.y = 0;
                if (u.sqrMagnitude > 1e-4f)
                {
                    u.Normalize(); var keep = neck.rotation; float y0 = head.position.y; neck.rotation = Quaternion.AngleAxis(8, u) * keep; float rise = head.position.y - y0; neck.rotation = keep;
                    neck.rotation = Quaternion.AngleAxis((rise >= 0 ? 1 : -1) * Neck * w, u) * keep;
                }
            }
            var mp = root.position; float rootY0 = mp.y;
            if (pillow) { var hc = BedProbe.HeadCentre(head, neck); var pc = Apartment.PillowAt; mp += new Vector3(pc.x - hc.x, 0, pc.z - hc.z) * w; }
            bool bake = forceBake || float.IsNaN(LowRel) || frame++ % 3 == 0 || w < .999f;
            if (bake)
            {
                root.position = mp;
                probe.Measure(BedProbe.HeadCentre(head, neck), out var low, out var headLow, out var trueLow);
                LowRel = low - mp.y; HeadRel = headLow - mp.y; TrueRel = trueLow - mp.y;
            }
            mp.y = Mathf.Lerp(mp.y, quiltTop - LowRel + .003f, w); root.position = mp; lastLift = mp.y - rootY0;
            if (pillow && bake && w > .99f && !float.IsInfinity(HeadRel))
            {
                var hc = BedProbe.HeadCentre(head, neck); float need = Apartment.PillowTopAt(hc.x, hc.z) - .02f - (HeadRel + mp.y);
                Neck = Mathf.Clamp(Neck + need / .15f * Mathf.Rad2Deg * .6f, -20, 20);
            }
            return bake;
        }
    }
}