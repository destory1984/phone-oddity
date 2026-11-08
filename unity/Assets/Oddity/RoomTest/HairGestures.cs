using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Two one-shot hair gestures made by hand, in the same spirit as ZeroTwoDance: Humanoid muscle curves written by name
    // on top of the standing idle (the pose Space plays), so the first and last frame equal the idle and nothing pops.
    //   head toss (1.4 s): head and neck tilt, two quick flicks that decay, a small chin lift, back to idle
    //   hair sweep (2.2 s): the right hand reaches the forehead, sweeps back over the temple and above the ear, comes down
    // The sweep's arm muscles only get the arm near; HairSweepRig then puts the right hand on the head surface by IK.
    public static class HairGestures
    {
        public const float TossLength = 1.4f, SweepLength = 2.2f;
        // how far one unit of "tilt" turns the head: the muscle units for 15 degrees, measured against the real Humanoid limits in the play log
        public const float HeadTiltUnit = .50f, NeckTiltUnit = .25f;
        // a positive Head / Neck Tilt Left-Right leans the head toward the character's right (checked by ROOMPLAY / HAIRGESTURE log and the strips)
        public const float TiltTowardRight = 1f;

        static Dictionary<string, int> muscleIndex;
        static int Index(string name)
        {
            if (muscleIndex == null)
            {
                muscleIndex = new Dictionary<string, int>();
                for (int i = 0; i < HumanTrait.MuscleName.Length; i++) muscleIndex[HumanTrait.MuscleName[i]] = i;
            }
            if (!muscleIndex.TryGetValue(name, out int index)) throw new InvalidOperationException("Unknown Humanoid muscle: " + name);
            return index;
        }
        public static bool IsMuscle(string name)
        {
            Index("Spine Front-Back");
            return muscleIndex.ContainsKey(name);
        }
        public static float Get(float[] m, string name) => m[Index(name)];
        static void Add(float[] m, string name, float v) => m[Index(name)] += v;
        static float S(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));

        // ---- head toss ----
        // 1 = a 30 degree lean to the side. A 15 degree lean in (0 - 0.2 s), a fast first flick through the other side to 30 degrees
        // (peak 0.34 s), a second flick back to half of that (peak 0.48 s), then it eases out. Each half swing is 0.14 s; smoothstep
        // makes it fast in the middle with short holds at the ends.
        static readonly (float t, float v)[] TossKeys = { (0, 0), (.20f, .5f), (.34f, -1f), (.48f, .5f), (.80f, 0) };
        public static float TossTilt(float t)
        {
            if (t <= 0 || t >= TossLength) return 0;
            for (int i = 1; i < TossKeys.Length; i++)
                if (t <= TossKeys[i].t) return Mathf.Lerp(TossKeys[i - 1].v, TossKeys[i].v, S(TossKeys[i - 1].t, TossKeys[i].t, t));
            return 0;
        }
        // head nod, positive = chin up: slightly down while tossing, then the chin lift (0.95 - 1.15 s) and the ease back
        public static float TossNod(float t)
        {
            if (t <= 0 || t >= TossLength) return 0;
            float down = -.10f * S(0, .25f, t) * (1 - S(.7f, .95f, t));
            float lift = .2f * S(.95f, 1.15f, t) * (1 - S(1.15f, 1.4f, t));
            return down + lift;
        }
        public static float TossLift(float t) => t <= 0 || t >= TossLength ? 0 : S(.95f, 1.15f, t) * (1 - S(1.15f, 1.4f, t));

        public static void SampleToss(float t, float[] m)
        {
            Array.Clear(m, 0, m.Length);
            float tilt = TossTilt(t) * TiltTowardRight, nod = TossNod(t), lift = TossLift(t);
            Add(m, "Head Tilt Left-Right", tilt * HeadTiltUnit);
            Add(m, "Neck Tilt Left-Right", tilt * NeckTiltUnit);
            Add(m, "Head Turn Left-Right", tilt * .18f); // the twist rides along with the tilt
            Add(m, "Neck Turn Left-Right", tilt * .12f);
            Add(m, "Head Nod Down-Up", nod * .55f);
            Add(m, "Neck Nod Down-Up", nod * .45f);
            Add(m, "Chest Front-Back", .05f * lift); // a tiny chest rise with the chin lift
            Add(m, "Spine Front-Back", .02f * lift);
            Add(m, "Chest Left-Right", .14f * tilt); // neck and upper chest follow, about 30% of the head
            Add(m, "Spine Left-Right", .06f * tilt);
            Add(m, "Chest Twist Left-Right", .06f * tilt);
            Add(m, "Left Shoulder Down-Up", .04f * Mathf.Max(0, -tilt));
            Add(m, "Right Shoulder Down-Up", .04f * Mathf.Max(0, tilt));
        }
        // the blink pulse of the toss (about 0.12 s, during the first flick); 0..1
        public static float TossBlink(float t) => t > .32f && t < .44f ? Mathf.Sin(Mathf.PI * (t - .32f) / .12f) : 0;

        // ---- hair sweep ----
        public static float SweepArm(float t) => t <= 0 || t >= SweepLength ? 0 : S(0, .5f, t) * (1 - S(1.1f, 1.7f, t));   // right arm up / down
        public static float SweepHead(float t) => t <= 0 || t >= SweepLength ? 0 : S(.3f, .6f, t) * (1 - S(1.1f, 1.7f, t)); // the head leans into the hand
        public static float SweepProgress(float t) => S(.5f, 1.1f, t); // 0 forehead, .5 temple, 1 above the ear

        public static void SampleSweep(float t, float[] m)
        {
            Array.Clear(m, 0, m.Length);
            float arm = SweepArm(t), head = SweepHead(t);
            Add(m, "Right Shoulder Down-Up", .25f * arm);
            Add(m, "Right Arm Down-Up", .85f * arm);
            Add(m, "Right Arm Front-Back", .35f * arm);
            Add(m, "Right Arm Twist In-Out", .15f * arm);
            Add(m, "Right Forearm Stretch", -.80f * arm);
            Add(m, "Right Forearm Twist In-Out", .25f * arm);
            Add(m, "Right Hand Down-Up", .05f * arm);
            // head leans into the hand (~8 degrees) and up (~6 degrees)
            Add(m, "Head Tilt Left-Right", head * .10f * TiltTowardRight);
            Add(m, "Neck Tilt Left-Right", head * .06f * TiltTowardRight);
            Add(m, "Head Nod Down-Up", head * .09f + .06f * S(.6f, .8f, t) * (1 - S(.8f, 1.0f, t))); // a slight lift as the hand passes the crown
            Add(m, "Neck Nod Down-Up", head * .06f);
        }

        // ---- clips ----
        // the idle's value of every Animator curve at its first frame: muscles and the root, so the gesture starts and ends on exactly that pose
        public static Dictionary<string, float> ReadBase(AnimationClip idle)
        {
            var values = new Dictionary<string, float>();
            if (idle == null) return values;
            foreach (var b in AnimationUtility.GetCurveBindings(idle))
            {
                if (b.type != typeof(Animator)) continue;
                var curve = AnimationUtility.GetEditorCurve(idle, b);
                if (curve != null && curve.length > 0) values[b.propertyName] = curve.Evaluate(0);
            }
            return values;
        }

        public static AnimationClip CreateClip(string name, float length, Action<float, float[]> sample, IDictionary<string, float> baseValues)
        {
            var clip = new AnimationClip { name = name, frameRate = 60, wrapMode = WrapMode.Once };
            int frames = Mathf.RoundToInt(length * 60);
            var m = new float[HumanTrait.MuscleCount];
            var perMuscle = new Keyframe[HumanTrait.MuscleCount][];
            for (int j = 0; j <= frames; j++)
            {
                float t = j * length / frames;
                sample(t, m);
                for (int i = 0; i < m.Length; i++)
                {
                    float baseValue = baseValues != null && baseValues.TryGetValue(HumanTrait.MuscleName[i], out var bv) ? bv : 0;
                    (perMuscle[i] ??= new Keyframe[frames + 1])[j] = new Keyframe(t, Mathf.Clamp(baseValue + m[i], -1, 1));
                }
            }
            var animated = new HashSet<string>();
            for (int i = 0; i < m.Length; i++)
            {
                string muscle = HumanTrait.MuscleName[i];
                bool moves = perMuscle[i].Any(k => Mathf.Abs(k.value - perMuscle[i][0].value) > 1e-6f);
                bool inBase = baseValues != null && baseValues.ContainsKey(muscle);
                if (!moves && !inBase) continue; // nothing to say about it
                var curve = moves ? new AnimationCurve(perMuscle[i]) : AnimationCurve.Constant(0, length, perMuscle[i][0].value);
                if (moves) for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0);
                clip.SetCurve("", typeof(Animator), muscle, curve);
                animated.Add(muscle);
            }
            if (baseValues != null)
                foreach (var kv in baseValues)
                    if (!animated.Contains(kv.Key) && !IsMuscle(kv.Key)) clip.SetCurve("", typeof(Animator), kv.Key, AnimationCurve.Constant(0, length, kv.Value)); // root and goal curves
            if (animated.Count == 0) clip.SetCurve("", typeof(Animator), "Spine Front-Back", AnimationCurve.Constant(0, length, 0));
            return clip;
        }

        public static AnimationClip CreateTossClip(AnimationClip idle) => CreateClip("HairToss", TossLength, SampleToss, ReadBase(idle));
        public static AnimationClip CreateSweepClip(AnimationClip idle) => CreateClip("HairSweep", SweepLength, SampleSweep, ReadBase(idle));

        public static bool IsGesture(int index) => index == RoomPlayRunner.SweepIndex || index == RoomPlayRunner.TossIndex;
    }

    // Puts the right hand of the hair sweep on the head by a two-bone IK, from a model of the head measured on the loaded mesh.
    // Everything is blended by weight * arm envelope, with the target and the elbow pole starting from where the clip left them,
    // so at weight 0 nothing moves and the correction fades in and out without a pop.
    public sealed class HairSweepRig
    {
        readonly Animator anim;
        readonly Transform head, upper, lower, hand, midProximal, midDistal, midIntermediate, indexProximal, littleProximal;
        readonly Vector3 centerLocal;        // ellipsoid centre in Head bone space
        readonly Vector3 radii;              // half extents in the character frame (right, up, forward), hair included
        readonly Vector3[] verts;            // head mesh vertices in Head bone space
        public readonly float HeadRadius;    // mean half extent
        readonly float palmLength, palmThick;
        readonly Vector3 fingersLocal, palmLocal; // finger direction and palm normal in the hand bone's space
        const float Clearance = .003f, Touch = .003f; // the palm sits this far off the hair vertices

        // measured by Sample(): the smallest distances over a clip
        public float MinBone = float.MaxValue, MinCenter = float.MaxValue, MinMesh = float.MaxValue, MaxPalmForward = -1;

        public HairSweepRig(Animator anim)
        {
            this.anim = anim;
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            upper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            midProximal = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            midIntermediate = anim.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
            midDistal = anim.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
            indexProximal = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            littleProximal = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if (head == null || upper == null || lower == null || hand == null)
                throw new InvalidOperationException("HairSweepRig needs the head, right arm and hand bones");
            // the loaded rig has no finger bones, so the hand's axes come from its T-pose (arms along the sides, palms down, built before any clip plays)
            var rootRot = anim.transform.rotation;
            fingersLocal = Quaternion.Inverse(hand.rotation) * (hand.position - lower.position).normalized;
            palmLocal = Quaternion.Inverse(hand.rotation) * (rootRot * Vector3.down);
            palmLength = midProximal != null ? Vector3.Distance(hand.position, midProximal.position) : .09f;
            palmThick = .012f;

            // head vertices: everything above the Head bone's height (a little below it for the nape), near the head
            var root = anim.transform;
            float cut = root.InverseTransformPoint(head.position).y - .02f;
            Vector3 headAt = root.InverseTransformPoint(head.position);
            var mesh = new Mesh();
            var local = new List<Vector3>();
            foreach (var smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh);
                var vs = mesh.vertices;
                var toWorld = smr.transform.localToWorldMatrix;
                for (int i = 0; i < vs.Length; i++)
                {
                    var p = root.InverseTransformPoint(toWorld.MultiplyPoint3x4(vs[i]));
                    if (p.y < cut || Mathf.Abs(p.x - headAt.x) > .22f || Mathf.Abs(p.z - headAt.z) > .25f || p.y > headAt.y + .5f) continue;
                    local.Add(p);
                }
            }
            UnityEngine.Object.Destroy(mesh);
            if (local.Count < 50) throw new InvalidOperationException("HairSweepRig found no head mesh");
            Vector3 lo = Percentile(local, .02f), hi = Percentile(local, .98f);
            var centerChar = (lo + hi) * .5f;
            radii = (hi - lo) * .5f;
            HeadRadius = (radii.x + radii.y + radii.z) / 3f;
            centerLocal = head.InverseTransformPoint(root.TransformPoint(centerChar));
            int stride = Mathf.Max(1, local.Count / 5000);
            verts = local.Where((_, i) => i % stride == 0).Select(p => head.InverseTransformPoint(root.TransformPoint(p))).ToArray();
        }

        static Vector3 Percentile(List<Vector3> pts, float q)
        {
            float Pick(Func<Vector3, float> f) { var a = pts.Select(f).OrderBy(v => v).ToArray(); return a[Mathf.Clamp(Mathf.RoundToInt(q * (a.Length - 1)), 0, a.Length - 1)]; }
            return new Vector3(Pick(p => p.x), Pick(p => p.y), Pick(p => p.z));
        }

        public Vector3 Center => head.TransformPoint(centerLocal);
        public Vector3 Radii => radii;

        // directions on the head in the character frame: forehead above the right eyebrow, right temple, above the right ear
        static readonly Vector3 Forehead = new Vector3(.40f, .45f, .80f), Temple = new Vector3(.70f, .70f, .15f), AboveEar = new Vector3(.92f, .30f, -.38f); // Temple is the top-side of the head
        public static Vector3 PathDirection(float progress)
        {
            Vector3 d = progress < .5f ? Vector3.Lerp(Forehead.normalized, Temple.normalized, Mathf.SmoothStep(0, 1, progress / .5f))
                                       : Vector3.Lerp(Temple.normalized, AboveEar.normalized, Mathf.SmoothStep(0, 1, (progress - .5f) / .5f));
            return d.normalized;
        }

        Vector3 Surface(Vector3 center, Vector3 d, out Vector3 normal)
        {
            var r = anim.transform.right; var u = anim.transform.up; var f = anim.transform.forward;
            var p = center + r * (radii.x * d.x) + u * (radii.y * d.y) + f * (radii.z * d.z);
            normal = (r * (d.x / radii.x) + u * (d.y / radii.y) + f * (d.z / radii.z)).normalized;
            return p;
        }

        // 1 inside the ellipsoid grown by the clearance's share, so a point under 1 sits too close to the hair
        float Inside(Vector3 center, Vector3 p, float grow)
        {
            var d = p - center; var root = anim.transform;
            return new Vector3(Vector3.Dot(d, root.right) / (radii.x + grow), Vector3.Dot(d, root.up) / (radii.y + grow), Vector3.Dot(d, root.forward) / (radii.z + grow)).magnitude;
        }

        // palm normal (out of the palm) and finger direction of the right hand, from its own bones
        void HandAxes(out Vector3 fingers, out Vector3 palm)
        {
            fingers = (hand.rotation * fingersLocal).normalized;
            palm = (hand.rotation * palmLocal).normalized;
        }

        static Vector3 Through(Vector3 from, Vector3 via, Vector3 to, float k) => k < .5f ? Vector3.Slerp(from, via, k * 2) : Vector3.Slerp(via, to, k * 2 - 1);
        // smallest distance from a world point to the head mesh vertices (hair and skin; the outer surface is the hair)
        float MeshDistance(Vector3 world)
        {
            var local = head.InverseTransformPoint(world);
            float best = float.MaxValue;
            foreach (var v in verts) best = Mathf.Min(best, (v - local).sqrMagnitude);
            return Mathf.Sqrt(best) * head.lossyScale.x;
        }

        public void Apply(float t, float weight)
        {
            float env = HairGestures.SweepArm(t) * Mathf.Clamp01(weight);
            if (env <= 1e-4f) return;
            var root = anim.transform;
            var c = Center;
            float progress = HairGestures.SweepProgress(t);
            var d = PathDirection(progress);
            var surface = Surface(c, d, out var n);
            var palmWant = -n;                                                        // the palm faces the head
            var up = Vector3.ProjectOnPlane(root.up - root.forward * (.55f * progress), n);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(root.right, n);
            var fingersWant = up.normalized;                                          // fingers up, leaning back as it sweeps
            var palmPoint = surface + n * Clearance;                                  // the palm's middle
            var handWant = palmPoint - fingersWant * (palmLength * .55f) + n * palmThick;
            // settle the palm onto the hair: move along the surface normal until the palm side is Touch from the nearest head vertex
            for (int k = 0; k < 3; k++)
            {
                float near = float.MaxValue;
                foreach (float along in new[] { .55f, 1f, 1.6f }) near = Mathf.Min(near, MeshDistance(handWant + fingersWant * (palmLength * along) - n * palmThick));
                handWant -= n * Mathf.Clamp(near - Touch, -.03f, .03f);
            }

            HandAxes(out var fingersNow, out var palmNow);
            var handNow = hand.position;
            float e = env;
            var aim = Vector3.Lerp(handNow, handWant, e);
            aim += root.right * (.05f * Mathf.Sin(Mathf.PI * e)); // swings out a little while it comes up or goes down
            var pole = Vector3.Lerp(lower.position, upper.position + root.right * .30f - root.up * .35f + root.forward * .05f, e);
            TwoBone(upper, lower, hand, aim, pole);

            // turn the hand so fingers and palm go from where the clip had them to the wanted directions; coming down it drops
            // along the side of the head with the palm toward the body and the wrist relaxed, never an open palm toward the front
            HandAxes(out fingersNow, out palmNow);
            Vector3 fingers, palm;
            if (t <= 1.1f) { fingers = Through(fingersNow, root.right, fingersWant, e); palm = Through(palmNow, -root.up, palmWant, e); } // rises by way of fingers out to the side, palm down
            else
            {
                var relaxedFingers = (-root.up + root.forward * .25f).normalized;
                var relaxedPalm = Vector3.ProjectOnPlane(-root.right, relaxedFingers).normalized;
                float contact = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1f, e)), free = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .35f, e));
                // fingers up and fingers down are nearly opposite, so go by way of fingers out to the side with the palm down (a straight slerp sweeps across the front)
                float g = 1 - contact;
                var wf = Through(fingersWant, root.right, relaxedFingers, g); var wp = Through(palmWant, -root.up, relaxedPalm, g);
                fingers = Vector3.Slerp(fingersNow, wf, free); palm = Vector3.Slerp(palmNow, wp, free);
            }
            fingers.Normalize(); palm.Normalize();
            var now = Quaternion.LookRotation(fingersNow, palmNow);
            var want = Quaternion.LookRotation(fingers, palm);
            hand.rotation = want * Quaternion.Inverse(now) * hand.rotation;
        }

        // upper then lower turn so the wrist reaches the target with the elbow bent toward the pole
        static void TwoBone(Transform a, Transform b, Transform h, Vector3 target, Vector3 pole)
        {
            Vector3 s = a.position, e = b.position, w = h.position;
            float la = (e - s).magnitude, lb = (w - e).magnitude;
            var delta = target - s;
            float len = Mathf.Clamp(delta.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            var dir = delta.normalized;
            var bend = Vector3.ProjectOnPlane(pole - s, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(e - s, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((la * la + len * len - lb * lb) / (2 * la * len), -1, 1);
            var elbowAt = s + dir * la * cosA + bend * la * Mathf.Sqrt(1 - cosA * cosA);
            a.rotation = Quaternion.FromToRotation(e - s, elbowAt - s) * a.rotation;
            e = b.position; w = h.position;
            b.rotation = Quaternion.FromToRotation(w - e, s + dir * len - e) * b.rotation;
        }

        // distances of the right hand to the head at this instant; MinBone / MinCenter / MinMesh keep the smallest seen
        public float PalmForwardAt;
        public void Sample(float t = 0)
        {
            var root = anim.transform;
            HandAxes(out var fingers, out var palmOut);
            { float pf = Vector3.Dot(palmOut, root.forward); if (pf > MaxPalmForward) { MaxPalmForward = pf; PalmForwardAt = t; } }
            Vector3 tip = midDistal != null && midIntermediate != null ? midDistal.position + (midDistal.position - midIntermediate.position).normalized * .02f
                        : hand.position + fingers * (palmLength * 1.9f); // no finger bones: the middle fingertip is about 17 cm past the wrist
            var points = new[] { hand.position, tip, hand.position + fingers * (palmLength * .55f) + palmOut * palmThick, hand.position + fingers * palmLength + palmOut * palmThick, hand.position + fingers * (palmLength * 1.6f) + palmOut * palmThick };
            var c = Center;
            MinBone = Mathf.Min(MinBone, Mathf.Min(Vector3.Distance(hand.position, head.position), Vector3.Distance(tip, head.position)));
            foreach (var p in points)
            {
                MinCenter = Mathf.Min(MinCenter, Vector3.Distance(p, c));
                var local = head.InverseTransformPoint(p);
                float best = float.MaxValue;
                foreach (var v in verts) best = Mathf.Min(best, (v - local).sqrMagnitude);
                MinMesh = Mathf.Min(MinMesh, Mathf.Sqrt(best) * head.lossyScale.x);
            }
        }
    }
}
