using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Construct in the imported neutral pose; call Apply in LateUpdate after the seated Animator.
    public sealed class MirrorRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Seconds for activation and every hand transition.
        public const float LoopSeconds = 25f; // Complete grooming loop in seconds.
        public const float CheckEnd = 6f, TintEnd = 12f, PressEnd = 14f, FringeEnd = 19f; // Beat boundaries, seconds.
        public const float SmileEnd = 22f, SquintEnd = 23f; // Return occupies the final two seconds.
        public const float MirrorDiameter = .12f; // Outside diameter, metres.
        const float DiscThickness = .006f, FaceInset = .008f, FaceGap = .0005f; // Rim thickness, inset and surface separation, metres.
        const float HandleLength = .085f, HandleWidth = .016f, GripToCentre = .115f, GripDepth = .015f; // Mirror grip geometry, metres.
        const float TintLength = .060f, TintDiameter = .012f, TintExposed = .015f; // Uncapped solid tint geometry, metres.
        const float TintGripHeight = .025f, TintGripDepth = .012f; // Tint bottom relative to right wrist, metres.
        const float MirrorLeft = .13f, MirrorAhead = .32f, MirrorUp = .035f; // Disc centre relative to head, metres.
        const float LipDown = .035f, LipAhead = .095f, LipSweep = .014f, LipGap = .003f; // Lip approximation and lateral tint stroke, metres.
        const float FringeUp = .13f, FringeAhead = .125f, FringeStart = -.025f, FringeFinish = .065f; // Forehead contact path, metres.
        const float FingerReachFallback = .15f, FingerTipLength = .018f; // Missing-finger wrist reach and terminal phalanx extension, metres.
        const float LapRight = .16f, LapUp = .08f, LapAhead = .24f; // Resting wrist relative to hips, metres.
        const float PoleOut = .22f, PoleDown = .28f, PoleAhead = .18f; // Elbow pole offsets, metres.
        const float CheckYaw = 13f, CheckPitch = 2f, SquintRoll = 5f; // Head turns and playful tilt, degrees.
        const float BreathDegrees = .6f, BreathPeriod = 4.5f, NeckShare = .35f; // Torso breathing and neck share of face turn.
        const float TintCycles = 3f, PressCycles = 2f; // Lip sweeps / lip compressions per beat.
        const float TintMouth = .28f, PressMouth = .045f, SmileAmount = .72f, SquintAmount = .8f; // Face blend strengths, 0..1.
        const float CurlProximal = 26f, CurlIntermediate = 34f, CurlDistal = 18f, ThumbCurl = 20f; // Grip flexion, degrees.
        const float OpenFingerCurl = .12f, RelaxedCurl = .35f; // Fringe index/middle and resting finger curl multipliers.
        const float VariationMin = .92f, VariationMax = 1.08f; // Seeded per-loop head-turn strength.
        const float RimR = .65f, RimG = .36f, RimB = .40f, GlassShade = .82f; // Prop colours.
        const float TintR = .64f, TintG = .08f, TintB = .19f, CaseShade = .15f; // Tint and casing colours.
        const float Smoothness = .9f, Metallic = .8f; // Reflective-looking silver surface; no live reflection camera.
        const float Epsilon = .000001f, RotationTolerance = .001f; // Axis and paused-pose tolerances.
        const float Tau = 2 * Mathf.PI; // Radians per cycle.

        readonly Animator anim;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 4, 3];
        readonly Transform[,] thumb = new Transform[2, 3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        readonly Quaternion headFrame;
        readonly System.Random rng;
        readonly float fringeReach;
        readonly Quaternion[] before, written;
        bool wanted, disposed, hasWritten;
        float time, variation, tintBlend, fringeBlend;

        public Transform Mirror { get; }
        public Transform Tint { get; }
        public bool Active => !disposed && (wanted || Weight > 0);
        public float Weight { get; private set; }
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public float LipPress { get; private set; } // Optional lip-compression blendshape; MouthOpen alone cannot compress lips.
        public string Phase { get; private set; } = "Idle";
        public int Loops { get; private set; }
        public Vector3 MirrorCentre => Mirror.position;
        public Vector3 TintTip => Tint.TransformPoint(Vector3.up * (TintLength + TintExposed));
        public Vector3 LipPoint => head.position + HeadAxes() * new Vector3(0, -LipDown, LipAhead);
        Quaternion HeadAxes() => head.rotation * headFrame;

        public MirrorRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("MirrorRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; rng = new System.Random(seed);
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("MirrorRig needs hips, spine and head.");
            headFrame = Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[s] == null || lower[s] == null || hand[s] == null) throw new InvalidOperationException("MirrorRig needs both arms.");
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                    finger[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j));
                for (int j = 0; j < 3; j++) thumb[s, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + j));
                var direction = finger[s, 1, 0] != null ? finger[s, 1, 0].position - hand[s].position : hand[s].position - lower[s].position;
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < Epsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                handFrame[s] = Quaternion.LookRotation(Quaternion.Inverse(hand[s].rotation) * direction.normalized, Quaternion.Inverse(hand[s].rotation) * palm);
            }
            var tip = finger[1, 0, 2];
            fringeReach = tip != null ? Vector3.Distance(hand[1].position, tip.position) + FingerTipLength : FingerReachFallback;
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("MirrorRig needs a Lit shader.");
            var rim = Material(shader, new Color(RimR, RimG, RimB));
            var glass = Material(shader, new Color(GlassShade, GlassShade, GlassShade));
            glass.SetFloat("_Smoothness", Smoothness); glass.SetFloat("_Metallic", Metallic);
            var casing = Material(shader, new Color(CaseShade, CaseShade, CaseShade));
            var tint = Material(shader, new Color(TintR, TintG, TintB));
            Mirror = Prop("MirrorHandMirror", 0, new Vector3(0, -GripDepth, GripToCentre), Quaternion.Euler(90, 0, 0));
            Part(Mirror, "rim", Vector3.zero, new Vector3(MirrorDiameter, DiscThickness / 2, MirrorDiameter), Quaternion.Euler(90, 0, 0), rim);
            Part(Mirror, "silver", new Vector3(0, 0, -DiscThickness / 2 - FaceGap), new Vector3(MirrorDiameter - FaceInset, FaceGap / 2, MirrorDiameter - FaceInset), Quaternion.Euler(90, 0, 0), glass);
            Part(Mirror, "handle", Vector3.down * (MirrorDiameter / 2 + HandleLength / 2), new Vector3(HandleWidth, HandleLength / 2, HandleWidth), Quaternion.identity, rim);
            Tint = Prop("MirrorLipTint", 1, new Vector3(0, TintGripDepth, TintGripHeight), Quaternion.Euler(90, 0, 0));
            Part(Tint, "case", Vector3.up * TintLength / 2, new Vector3(TintDiameter, TintLength / 2, TintDiameter), Quaternion.identity, casing);
            Part(Tint, "colour", Vector3.up * (TintLength + TintExposed / 2), new Vector3(TintDiameter, TintExposed / 2, TintDiameter), Quaternion.identity, tint);
        }

        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) controlled.Add(b);
            return b;
        }
        Material Material(Shader shader, Color colour)
        {
            var m = new Material(shader); m.color = colour; materials.Add(m); return m;
        }
        Transform Prop(string name, int side, Vector3 gripOffset, Quaternion gripRotation)
        {
            var p = new GameObject(name).transform; p.SetParent(hand[side], false);
            p.localPosition = handFrame[side] * gripOffset;
            p.localRotation = handFrame[side] * gripRotation;
            p.gameObject.SetActive(false); return p;
        }
        static void Part(Transform parent, string name, Vector3 p, Vector3 size, Quaternion q, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
            g.transform.SetParent(parent, false); g.transform.localPosition = p; g.transform.localRotation = q; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }
        static float Smooth(float x) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(x));
        static float Envelope(float t, float start, float end) => Smooth((t - start) / BlendSeconds) * Smooth((end - t) / BlendSeconds);
        static void Rotate(Transform b, Vector3 axis, float angle) { if (b != null) b.rotation = Quaternion.AngleAxis(angle, axis) * b.rotation; }
        void NewLoop() => variation = VariationMin + (VariationMax - VariationMin) * (float)rng.NextDouble();
        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MirrorRig));
            if (wanted) return;
            wanted = true; time = 0; Loops = 0; NewLoop(); Evaluate();
        }
        public void Stop() { wanted = false; }
        public void Tick(float dt)
        {
            if (disposed || float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) return;
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (wanted)
            {
                time += dt;
                while (time >= LoopSeconds) { time -= LoopSeconds; Loops++; NewLoop(); }
            }
            Evaluate();
        }
        void Evaluate()
        {
            Phase = !Active ? "Idle" : time < CheckEnd ? "Check" : time < TintEnd ? "Tint" : time < PressEnd ? "Press" :
                time < FringeEnd ? "Fringe" : time < SmileEnd ? "Smile" : time < SquintEnd ? "Squint" : "Return";
            tintBlend = Envelope(time, CheckEnd, TintEnd);
            fringeBlend = Envelope(time, PressEnd, FringeEnd);
            float w = Smooth(Weight);
            MouthOpen = (TintMouth * tintBlend + PressMouth * Envelope(time, TintEnd, PressEnd)) * w;
            LipPress = Mathf.Pow(Mathf.Sin(Mathf.PI * PressCycles * Mathf.InverseLerp(TintEnd, PressEnd, time)), 2) * Envelope(time, TintEnd, PressEnd) * w;
            Smile = SmileAmount * Envelope(time, FringeEnd, SquintEnd) * w;
            EyesClosed = SquintAmount * Envelope(time, SmileEnd, SquintEnd) * w;
        }
        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var b = controlled[i];
                if (hasWritten && Quaternion.Angle(b.localRotation, written[i]) < RotationTolerance) b.localRotation = before[i];
                before[i] = b.localRotation;
            }
        }
        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose(); float w = Smooth(Weight);
            Mirror.gameObject.SetActive(Active); Tint.gameObject.SetActive(Active);
            if (w > 0)
            {
                var r = anim.transform.right; var u = anim.transform.up; var f = anim.transform.forward;
                Rotate(chest, r, BreathDegrees * Mathf.Sin(time * Tau / BreathPeriod) * w);
                float check = Envelope(time, 0, CheckEnd);
                float yaw = CheckYaw * variation * Mathf.Sin(time * Tau / CheckEnd) * check;
                Rotate(neck ?? head, u, yaw * NeckShare * w);
                Rotate(head, u, yaw * (1 - NeckShare) * w);
                Rotate(head, r, CheckPitch * check * w);
                Rotate(head, f, SquintRoll * Envelope(time, SmileEnd, SquintEnd) * w);
                var axes = HeadAxes();
                var centre = head.position - r * MirrorLeft + f * MirrorAhead + u * MirrorUp;
                var leftFrame = Quaternion.LookRotation(u, -f);
                PoseArm(0, centre - u * GripToCentre - f * GripDepth, leftFrame, r, u, f, w);

                var rest = hips.position + r * LapRight + u * LapUp + f * LapAhead;
                var restFrame = Quaternion.LookRotation(f, u);
                float stroke = Mathf.Sin(Tau * TintCycles * Mathf.InverseLerp(CheckEnd, TintEnd, time));
                var lip = LipPoint + axes * (Vector3.right * (LipSweep * stroke) + Vector3.forward * LipGap);
                // Tint points up; its tip, not the wrist, follows the lip line.
                var tintFrame = Quaternion.LookRotation(u, -f);
                var tintOffset = tintFrame * new Vector3(0, TintGripDepth, TintGripHeight + TintLength + TintExposed);
                var tintWrist = lip - tintOffset;
                float progress = Smooth(Mathf.InverseLerp(PressEnd + BlendSeconds, FringeEnd - BlendSeconds, time));
                var contact = head.position + axes * new Vector3(Mathf.Lerp(FringeStart, FringeFinish, progress), FringeUp, FringeAhead);
                var fringeFrame = Quaternion.LookRotation(axes * Vector3.up, axes * Vector3.back);
                var fringeWrist = contact - fringeFrame * Vector3.forward * fringeReach;
                var target = Vector3.Lerp(Vector3.Lerp(rest, tintWrist, tintBlend), fringeWrist, fringeBlend);
                var frame = Quaternion.Slerp(Quaternion.Slerp(restFrame, tintFrame, tintBlend), fringeFrame, fringeBlend);
                PoseArm(1, target, frame, r, u, f, w);
                Curl(0, w, 0); Curl(1, w, fringeBlend);
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }
        void PoseArm(int side, Vector3 target, Quaternion frame, Vector3 r, Vector3 u, Vector3 f, float w)
        {
            var pole = upper[side].position + r * (side == 0 ? -PoleOut : PoleOut) - u * PoleDown + f * PoleAhead;
            SofaRig.TwoBone(upper[side], lower[side], hand[side], target, pole, w);
            hand[side].rotation = Quaternion.Slerp(hand[side].rotation, frame * Quaternion.Inverse(handFrame[side]), w);
        }
        void Curl(int side, float w, float fringe)
        {
            var frame = hand[side].rotation * handFrame[side];
            var axis = Vector3.Cross(frame * Vector3.forward, frame * Vector3.up).normalized;
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
            {
                float amount = side == 0 ? 1 : f < 2 ? Mathf.Lerp(1, OpenFingerCurl, fringe) : 1;
                if (side == 1) amount *= Mathf.Lerp(RelaxedCurl, 1, Mathf.Max(tintBlend, fringeBlend));
                Rotate(finger[side, f, j], axis, (j == 0 ? CurlProximal : j == 1 ? CurlIntermediate : CurlDistal) * amount * w);
            }
            for (int j = 0; j < 3; j++) Rotate(thumb[side, j], axis, ThumbCurl * w);
        }
        public void Dispose()
        {
            if (disposed) return;
            if (hasWritten) ReadPose();
            disposed = true; wanted = false; Weight = Smile = EyesClosed = MouthOpen = LipPress = 0; Phase = "Idle";
            SofaRig.Kill(Mirror != null ? Mirror.gameObject : null); SofaRig.Kill(Tint != null ? Tint.gameObject : null);
            foreach (var m in materials)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m); else UnityEngine.Object.DestroyImmediate(m);
            }
        }
    }
}
