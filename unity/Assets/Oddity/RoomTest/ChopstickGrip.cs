using System;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    // The standard right-hand chopstick grip, built from the real finger bones.
    // Everything is authored in the "grip frame" H of the hand bone: x towards the thumb side, y along the fingers, z towards the back of the hand
    // (the palm is -z). The lower stick is fixed in H. The upper stick swings about the thumb pad (the pivot); only the index and middle fingers
    // follow it. Thumb, ring and little finger are solved once.
    // Call Pose after the hand bone has its final world rotation; it writes the finger bones and refreshes the stick lines.
    public sealed class ChopstickGrip
    {
        // Korean stainless chopsticks sized to her hand (wrist to middle fingertip 0.134 m, so a real 0.19 m hand scales to 0.70 and 0.27 m sticks to 0.19 m): flat, 5.5 x 2.5 mm at the handle, 3.5 x 2 mm at the tip.
        // The flat faces look at each other (thin along the opening direction o, wide along the plane normal n).
        public const float StickLength = .19f, StickRadius = .00125f, StickHalfWide = .00275f, TipHalfThick = .001f, TipHalfWide = .00175f, FingerRadius = .0075f; // metres; StickRadius is the half thickness at the handle
        // finger centre line to stick axis when a finger touches the flat stick on the (o, n) side direction, at 'at' metres from the top end
        public static float Reach(Vector2 side, float at)
        {
            float t = Mathf.Clamp01(at / StickLength), hz = Mathf.Lerp(StickRadius, TipHalfThick, t), hw = Mathf.Lerp(StickHalfWide, TipHalfWide, t);
            var u = side.normalized;
            return 1f / Mathf.Max(Mathf.Abs(u.x) / hz, Mathf.Abs(u.y) / hw) + FingerRadius;
        }
        // stick placement in H: the lower stick axis passes StickPivot from its top end at GripPoint, pointing (azimuth, elevation) with a roll about its axis
        static readonly Vector3 GripPoint = new Vector3(.0079f, .0419f, -.0569f); // lower stick axis point at the pivot, metres in H
        const float Azimuth = -.339f, Elevation = -.448f, Roll = 1.635f; // radians: azimuth from +y towards +x, elevation towards +z
        const float PivotAt = .058f, ThumbAt = .058f, RingAt = .048f, MiddleAt = .078f, IndexAt = .093f; // metres from the top end of a stick (the grip sits 0.305 of the length from the top; the contacts keep their distances from the pivot)
        const float PivotGap = .020f; // centre distance between the sticks at the pivot, metres (the middle finger lies between them)
        const float OpenTipGap = .031f; // gap between the tips when open, metres
        const float PadIndex = .011f, PadMiddle = .005f, PadRing = .007f, PadThumb = .011f; // contact point along the last phalanx from its joint, metres
        const float DistalLength = .0185f, ThumbDistalLength = .02f; // last phalanx lengths, metres
        // cross-section directions (along the opening direction o, along the plane normal n) from a stick towards the finger touching it
        static readonly Vector2 ThumbSide = new Vector2(-.30f, .95f), IndexSide = new Vector2(.55f, -.83f), MiddleSide = new Vector2(-.95f, -.30f), RingSide = new Vector2(-.85f, -.50f);
        const float FingerAbductionLimit = .45f; // radians
        const float WebRadius = .010f, WebWeight = .6f; // thumb base radius and how strongly the thumb base is pulled onto the lower stick
        const int Iterations = 24;

        sealed class Chain
        {
            public Transform[] bone = new Transform[3];
            public Vector3[] rest = new Vector3[3]; // H-frame positions
            public Quaternion[] restRot = new Quaternion[3]; // bone rotation relative to H
            public double[] x; // abduction, 3 flexions (fingers) or 3 rotation vectors (thumb)
            public bool thumb;
            public float lastLength;
            public Quaternion[] c = new Quaternion[3];
            public Vector3[] p = new Vector3[4]; // joint positions and the finger tip
        }

        readonly Transform hand;
        readonly Quaternion rh; // hand bone -> H (identity for this rig)
        readonly Chain[] chains = new Chain[5]; // thumb, index, middle, ring, little
        readonly Vector3 lowerTop, lowerDir, openNormal, planeNormal, pivot;
        readonly float closedAngle, openAngle;
        readonly double[] scratchLo = { -FingerAbductionLimit, -.17, 0, -.1 }, scratchHi = { FingerAbductionLimit, 1.5, 1.9, 1.3 };
        readonly double[] bentLo = { -FingerAbductionLimit, -.17, .85, -.1 }, bentHi = { FingerAbductionLimit, 1.5, 1.25, .9 }; // index and middle: the middle joint stays bent 50 - 70 deg, the last one fairly straight
        float open = -1;
        public string Report;

        // Stick lines, world metres. Valid after Pose.
        public Vector3 LowerTopWorld { get; private set; }
        public Vector3 LowerDirWorld { get; private set; }
        public Vector3 UpperTopWorld { get; private set; }
        public Vector3 UpperDirWorld { get; private set; }
        public Vector3 LowerTipWorld => LowerTopWorld + LowerDirWorld * StickLength;
        public Vector3 UpperTipWorld => UpperTopWorld + UpperDirWorld * StickLength;
        public float TipGap => Mathf.Max(0, Vector3.Distance(LowerTipWorld, UpperTipWorld) - 2 * StickRadius);
        public float Opening { get; private set; }
        public Vector3 ThumbPadWorld => ToWorld(PadOf(chains[0], PadThumb));
        public Vector3 IndexPadWorld => ToWorld(PadOf(chains[1], PadIndex));
        public Vector3 MiddleContactWorld => ToWorld(PadOf(chains[2], PadMiddle));
        public Vector3 RingContactWorld => ToWorld(PadOf(chains[3], PadRing));
        public Vector3 WebWorld => ToWorld((chains[0].p[0] + chains[0].p[1]) * .5f); // middle of the thumb's first bone, the base of the thumb in the web
        public Vector3 TipsMidInHand { get; private set; } // closed tip midpoint in H (metres), for aiming the hand
        public Quaternion HFrame => hand.rotation * rh;
        public Quaternion HandToFrame => rh;
        public Vector3 LowerDirInHand => lowerDir;
        public Vector3 TipsMidWorld => (LowerTipWorld + UpperTipWorld) * .5f;
        public Vector3 ToWorld(Vector3 h) => hand.position + hand.rotation * rh * h;
        public Vector3 ToHand(Vector3 world) => Quaternion.Inverse(hand.rotation * rh) * (world - hand.position);
        public float PivotFromTop => PivotAt;
        public float ThumbFromTop => ThumbAt;
        public Vector3 ThumbPadH => PadOf(chains[0], PadThumb);

        public ChopstickGrip(Transform hand, Transform[] thumb, Transform[] index, Transform[] middle, Transform[] ring, Transform[] little)
        {
            this.hand = hand ?? throw new ArgumentNullException(nameof(hand));
            var set = new[] { thumb, index, middle, ring, little };
            for (int f = 0; f < 5; f++) if (set[f] == null || set[f].Length < 3 || set[f][0] == null || set[f][1] == null || set[f][2] == null)
                throw new InvalidOperationException("ChopstickGrip needs all five fingers.");
            // H is the hand bone's own local frame (this VRM rig: y along the fingers, x towards the thumb side, z towards the back of the hand); checked against the rest hand
            rh = Quaternion.identity;
            var inv = Quaternion.Inverse(hand.rotation);
            Vector3 L(Transform t) => inv * (t.position - hand.position);
            if (L(middle[0]).normalized.y < .9f || L(thumb[1]).x < L(little[0]).x) throw new InvalidOperationException("ChopstickGrip: unexpected hand bone axes.");
            for (int f = 0; f < 5; f++)
            {
                var c = new Chain { thumb = f == 0, x = new double[f == 0 ? 9 : 4] };
                for (int j = 0; j < 3; j++)
                {
                    c.bone[j] = set[f][j];
                    c.rest[j] = Quaternion.Inverse(rh) * L(set[f][j]);
                    c.restRot[j] = Quaternion.Inverse(hand.rotation * rh) * set[f][j].rotation;
                }
                c.lastLength = f == 0 ? ThumbDistalLength : DistalLength;
                chains[f] = c;
            }
            // stick frame
            var a = new Vector3(Mathf.Cos(Elevation) * Mathf.Sin(Azimuth), Mathf.Cos(Elevation) * Mathf.Cos(Azimuth), Mathf.Sin(Elevation));
            var n0 = Vector3.Cross(a, Vector3.forward).normalized; var o0 = Vector3.Cross(n0, a);
            var o = Mathf.Cos(Roll) * o0 + Mathf.Sin(Roll) * n0; var n = Vector3.Cross(a, o);
            lowerDir = a; openNormal = o; planeNormal = n;
            lowerTop = GripPoint - a * PivotAt;
            pivot = GripPoint + o * PivotGap;
            closedAngle = Mathf.Asin((2 * TipHalfThick - PivotGap) / (StickLength - PivotAt));
            openAngle = Mathf.Asin((2 * TipHalfThick + OpenTipGap - PivotGap) / (StickLength - PivotAt));
            TipsMidInHand = ((lowerTop + a * StickLength) + (Upper(closedAngle, out var ut, out var ud) ? ut + ud * StickLength : Vector3.zero)) * .5f;
            // the parts that never move with the grip: ring, little and the thumb
            SolveRing(); SolveLittle(); SolveThumb();
            Report = "ring x=" + string.Join(",", chains[3].x.Select(v => v.ToString("0.00"))) + " thumb x=" + string.Join(",", chains[0].x.Select(v => v.ToString("0.00"))) + " restH idx0=" + chains[1].rest[0].ToString("0.0000") + " thumb0=" + chains[0].rest[0].ToString("0.0000") + " ring0=" + chains[3].rest[0].ToString("0.0000") + " ringTargetErr=" + (PadOf(chains[3], PadRing) - (lowerTop + lowerDir * RingAt + (openNormal * RingSide.x + planeNormal * RingSide.y) * Reach(RingSide, RingAt))).magnitude.ToString("0.0000");
        }

        bool Upper(float angle, out Vector3 top, out Vector3 dir)
        {
            var r = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, planeNormal);
            dir = r * lowerDir; top = pivot - dir * PivotAt; return true;
        }

        // ---- chain maths -------------------------------------------------------------------------------------------------------------------
        void Eval(Chain c, double[] x)
        {
            Quaternion c0, c1, c2;
            if (c.thumb)
            {
                var q0 = Rv(x, 0); var q1 = Rv(x, 3); var q2 = Rv(x, 6);
                c0 = q0; c1 = q1 * c0; c2 = q2 * c1;
            }
            else
            {
                var abd = Quaternion.AngleAxis((float)x[0] * Mathf.Rad2Deg, Vector3.forward);
                var k = abd * -Vector3.right;
                c0 = Quaternion.AngleAxis((float)x[1] * Mathf.Rad2Deg, k) * abd;
                c1 = Quaternion.AngleAxis((float)x[2] * Mathf.Rad2Deg, k) * c0;
                c2 = Quaternion.AngleAxis((float)x[3] * Mathf.Rad2Deg, k) * c1;
            }
            c.c[0] = c0; c.c[1] = c1; c.c[2] = c2;
            c.p[0] = c.rest[0];
            c.p[1] = c.p[0] + c0 * (c.rest[1] - c.rest[0]);
            c.p[2] = c.p[1] + c1 * (c.rest[2] - c.rest[1]);
            c.p[3] = c.p[2] + c2 * ((c.rest[2] - c.rest[1]).normalized * c.lastLength);
        }
        static Quaternion Rv(double[] x, int i)
        {
            var v = new Vector3((float)x[i], (float)x[i + 1], (float)x[i + 2]); float m = v.magnitude;
            return m < 1e-7f ? Quaternion.identity : Quaternion.AngleAxis(m * Mathf.Rad2Deg, v / m);
        }
        Vector3 PadOf(Chain c, float along) => c.p[2] + c.c[2] * ((c.rest[2] - c.rest[1]).normalized * along);

        // Levenberg-Marquardt on residual(x) with box limits.
        delegate void Residual(double[] x, double[] r);
        static void Lm(double[] x, double[] lo, double[] hi, int m, Residual f, int iterations)
        {
            int n = x.Length; var r = new double[m]; var r2 = new double[m]; var jac = new double[m, n]; var a = new double[n, n]; var g = new double[n]; var dx = new double[n]; var xn = new double[n];
            f(x, r); double cost = Dot(r), lambda = 1e-3;
            for (int it = 0; it < iterations; it++)
            {
                for (int j = 0; j < n; j++)
                {
                    double h = 1e-4, keep = x[j]; x[j] = keep + h; f(x, r2); x[j] = keep;
                    for (int i = 0; i < m; i++) jac[i, j] = (r2[i] - r[i]) / h;
                }
                for (int i = 0; i < n; i++)
                {
                    g[i] = 0; for (int k = 0; k < m; k++) g[i] += jac[k, i] * r[k];
                    for (int j = 0; j < n; j++) { double s = 0; for (int k = 0; k < m; k++) s += jac[k, i] * jac[k, j]; a[i, j] = s; }
                }
                bool accepted = false;
                for (int tries = 0; tries < 6 && !accepted; tries++)
                {
                    var aa = (double[,])a.Clone();
                    for (int i = 0; i < n; i++) aa[i, i] += lambda * (1 + a[i, i]);
                    var rhs = new double[n]; for (int i = 0; i < n; i++) rhs[i] = -g[i];
                    if (!Gauss(aa, rhs, dx)) { lambda *= 10; continue; }
                    for (int i = 0; i < n; i++) xn[i] = Math.Max(lo[i], Math.Min(hi[i], x[i] + dx[i]));
                    f(xn, r2); double c2 = Dot(r2);
                    if (c2 < cost) { Array.Copy(xn, x, n); Array.Copy(r2, r, m); cost = c2; lambda = Math.Max(1e-7, lambda * .3); accepted = true; }
                    else lambda *= 8;
                }
                if (!accepted) break;
            }
        }
        static double Dot(double[] v) { double s = 0; foreach (var d in v) s += d * d; return s; }
        static bool Gauss(double[,] a, double[] b, double[] x)
        {
            int n = b.Length;
            for (int i = 0; i < n; i++)
            {
                int piv = i; for (int r = i + 1; r < n; r++) if (Math.Abs(a[r, i]) > Math.Abs(a[piv, i])) piv = r;
                if (Math.Abs(a[piv, i]) < 1e-14) return false;
                if (piv != i) { for (int c = 0; c < n; c++) { var t = a[i, c]; a[i, c] = a[piv, c]; a[piv, c] = t; } var tb = b[i]; b[i] = b[piv]; b[piv] = tb; }
                for (int r = i + 1; r < n; r++)
                {
                    double f = a[r, i] / a[i, i]; if (f == 0) continue;
                    for (int c = i; c < n; c++) a[r, c] -= f * a[i, c]; b[r] -= f * b[i];
                }
            }
            for (int i = n - 1; i >= 0; i--) { double s = b[i]; for (int c = i + 1; c < n; c++) s -= a[i, c] * x[c]; x[i] = s / a[i, i]; }
            return true;
        }

        void SolveFinger(Chain c, Vector3 target, float pad, int seeds, bool bent = false)
        {
            double[] best = null; double bestCost = double.MaxValue;
            for (int s = 0; s < seeds; s++)
            {
                var x = bent ? new double[] { 0, .8, 1.0, .5 } : new double[] { 0, .4 + .4 * s, .7 + .4 * s, .4 + .2 * s };
                if (best != null && s == 0) Array.Copy(best, x, 4);
                Lm(x, bent ? bentLo : scratchLo, bent ? bentHi : scratchHi, 7, (v, r) =>
                {
                    Eval(c, v); var d = c.p[2] + c.c[2] * ((c.rest[2] - c.rest[1]).normalized * pad) - target;
                    r[0] = d.x; r[1] = d.y; r[2] = d.z; for (int i = 0; i < 4; i++) r[3 + i] = .0015 * v[i];
                }, Iterations);
                Eval(c, x); var e = (PadOf(c, pad) - target).sqrMagnitude;
                if (e < bestCost) { bestCost = e; best = x; }
            }
            Array.Copy(best, c.x, 4); Eval(c, c.x);
        }

        void SolveRing()
        {
            var lowerAt = lowerTop + lowerDir * RingAt;
            SolveFinger(chains[3], lowerAt + (openNormal * RingSide.x + planeNormal * RingSide.y) * Reach(RingSide, RingAt), PadRing, 3);
        }
        void SolveLittle()
        {
            var r = chains[3].x; var l = chains[4].x;
            l[0] = r[0] * 1.15; l[1] = r[1] * .9; l[2] = r[2] * .9; l[3] = r[3] * .9;
            Eval(chains[4], l);
        }
        void SolveThumb()
        {
            var c = chains[0]; var target = pivot + (openNormal * ThumbSide.x + planeNormal * ThumbSide.y) * Reach(ThumbSide, PivotAt);
            double[] best = null; double bestCost = double.MaxValue;
            var lo = new double[9]; var hi = new double[9];
            for (int i = 0; i < 9; i++) { lo[i] = -1.4; hi[i] = 1.4; }
            for (int s = 0; s < 3; s++)
            {
                var x = new double[9]; x[0] = .25 * s; x[1] = .3; x[2] = -.2;
                Lm(x, lo, hi, 13, (v, r) =>
                {
                    Eval(c, v); var d = PadOf(c, PadThumb) - target;
                    var web = (c.p[0] + c.p[1]) * .5f - lowerTop; var off = web - lowerDir * Vector3.Dot(web, lowerDir);
                    r[0] = d.x; r[1] = d.y; r[2] = d.z; r[3] = WebWeight * (off.magnitude - (StickHalfWide + WebRadius));
                    for (int i = 0; i < 9; i++) r[4 + i] = .0012 * v[i];
                }, 40);
                Eval(c, x); var e = (PadOf(c, PadThumb) - target).sqrMagnitude;
                if (e < bestCost) { bestCost = e; best = x; }
            }
            Array.Copy(best, c.x, 9); Eval(c, c.x);
        }

        // open01: 0 = tips touching, 1 = about 31 mm apart. w blends from the current pose.
        public void Pose(float open01, float w)
        {
            open01 = Mathf.Clamp01(open01); Opening = open01;
            float angle = Mathf.Lerp(closedAngle, openAngle, open01);
            Upper(angle, out var ut, out var ud);
            var r = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, planeNormal);
            var mid = ut + ud * IndexAt; // placeholder, real contact points below
            SolveFinger(chains[1], ut + ud * IndexAt + r * (openNormal * IndexSide.x + planeNormal * IndexSide.y) * Reach(IndexSide, IndexAt), PadIndex, 1, true);
            SolveFinger(chains[2], ut + ud * MiddleAt + r * (openNormal * MiddleSide.x + planeNormal * MiddleSide.y) * Reach(MiddleSide, MiddleAt), PadMiddle, 1, true);
            _ = mid;
            var frame = hand.rotation * rh;
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                chains[f].bone[j].rotation = Quaternion.Slerp(chains[f].bone[j].rotation, frame * chains[f].c[j] * chains[f].restRot[j], w);
            LowerTopWorld = ToWorld(lowerTop); LowerDirWorld = frame * lowerDir;
            UpperTopWorld = ToWorld(ut); UpperDirWorld = frame * ud;
        }

        // Distance from a point to the stick axis segment (world), metres.
        public static float AxisDistance(Vector3 top, Vector3 dir, Vector3 point)
        {
            float t = Mathf.Clamp(Vector3.Dot(point - top, dir), 0, StickLength);
            return Vector3.Distance(point, top + dir * t);
        }
        public static float AlongAxis(Vector3 top, Vector3 dir, Vector3 point) => Vector3.Dot(point - top, dir);
    }
}
