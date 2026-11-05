using System;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Original eight-beat pose loop. HumanPoseHandler supplies avatar-independent axes;
    // a two-bone solve restores the stationary ankles after moving the pelvis.
    public sealed class ZeroTwoDance : IDisposable
    {
        const int SwayCycles = 4;
        const int KneePulses = 8;
        const float LateralAmplitude = .16f, DepthAmplitude = .05f;
        const float PelvisYaw = 36, PelvisRoll = 16;
        static float LoopPhase(float seconds) => Mathf.Repeat(seconds, ZeroTwoMusic.Duration) / ZeroTwoMusic.Duration * Mathf.PI * 2;
        static float HipPhase(float seconds) => LoopPhase(seconds) * SwayCycles;
        const float IntroSeconds = 7;

        // Integrate motion time over the unwrapped music clock. Splitting the
        // boundary keeps the phase continuous even when a frame straddles 7 s.
        public static float MotionSecondsBetween(float audioStart, float dt)
        {
            if (dt <= 0) return 0;
            float introPart = Mathf.Min(dt, Mathf.Max(0, IntroSeconds - audioStart));
            return .5f * introPart + (dt - introPart);
        }
        readonly Animator animator;
        readonly HumanPoseHandler handler;
        HumanPose pose;
        readonly float[] sampled = new float[HumanTrait.MuscleCount];
        readonly Vector3 restBody;
        readonly Quaternion restRotation;
        readonly Transform[] thighs = new Transform[2], knees = new Transform[2], feet = new Transform[2];
        readonly Vector3[] ankle = new Vector3[2];
        readonly Quaternion[] footRotation = new Quaternion[2];
        readonly Vector3[] target = new Vector3[2];
        readonly Quaternion[] rotations = new Quaternion[2];
        readonly Transform head;
        readonly Transform hips, spine;
        readonly Vector3 restHips, restHead;
        readonly Quaternion restHeadRotation;
        readonly Transform[] upperArms = new Transform[2], elbows = new Transform[2], hands = new Transform[2];
        readonly Quaternion[] restWrist = new Quaternion[2];

        public ZeroTwoDance(Animator animator)
        {
            this.animator = animator;
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            handler.GetHumanPose(ref pose);
            restBody = pose.bodyPosition;
            restRotation = pose.bodyRotation;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            restHips = animator.transform.InverseTransformPoint(hips.position);
            restHead = animator.transform.InverseTransformPoint(head.position);
            restHeadRotation = Quaternion.Inverse(animator.transform.rotation) * head.rotation;
            for (int i = 0; i < 2; i++)
            {
                thighs[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                knees[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                feet[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                ankle[i] = animator.transform.InverseTransformPoint(feet[i].position);
                footRotation[i] = Quaternion.Inverse(animator.transform.rotation) * feet[i].rotation;
                upperArms[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                elbows[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hands[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                restWrist[i] = hands[i].localRotation;
            }
        }

        public static Vector3 HipOffset(float seconds)
        {
            float hipPhase = HipPhase(seconds);
            return new Vector3(LateralAmplitude * Mathf.Sin(hipPhase), -.09f - .02f * (1 - Mathf.Cos(LoopPhase(seconds) * KneePulses)), DepthAmplitude * Mathf.Sin(hipPhase * 2));
        }

        public static void Sample(float seconds, float[] muscles)
        {
            if (muscles == null || muscles.Length != HumanTrait.MuscleCount) throw new ArgumentException("Expected a complete Humanoid muscle array.");
            Array.Clear(muscles, 0, muscles.Length);
            float hipPhase = HipPhase(seconds);
            float sway = Mathf.Sin(hipPhase), twist = Mathf.Sin(hipPhase + .4f);
            Set(muscles, "Spine Left-Right", -.34f * sway);
            Set(muscles, "Chest Left-Right", -.20f * sway);
            Set(muscles, "Spine Twist Left-Right", -.38f * twist);
            Set(muscles, "Chest Twist Left-Right", -.20f * twist);
            Set(muscles, "Chest Front-Back", -.08f);
            Set(muscles, "Neck Tilt Left-Right", .08f * sway);
            foreach (string side in Sides)
            {
                Set(muscles, side + " Shoulder Down-Up", .3f);
                Set(muscles, side + " Arm Down-Up", .78f);
                Set(muscles, side + " Arm Front-Back", -.28f);
                Set(muscles, side + " Arm Twist In-Out", -.35f);
                Set(muscles, side + " Forearm Stretch", -.92f);
                Set(muscles, side + " Forearm Twist In-Out", .2f);
                Set(muscles, side + " Hand Down-Up", -.12f);
                Set(muscles, side + " Upper Leg Front-Back", .05f);
                Set(muscles, side + " Lower Leg Stretch", -.08f);
            }
        }

        static readonly string[] Sides = { "Left", "Right" };
        static void Set(float[] values, string name, float value)
        {
            int i = Array.IndexOf(HumanTrait.MuscleName, name);
            if (i < 0) throw new InvalidOperationException("Unknown Humanoid muscle: " + name);
            values[i] = value;
        }

        // The playable owns time and blending; the pose layer owns retargeting and
        // planted feet. No imported clip or previous frame is used as a dance pose.
        public static AnimationClip CreateClip()
        {
            var clip = new AnimationClip { name = "ZeroTwoDance", frameRate = 60, wrapMode = WrapMode.Loop };
            clip.SetCurve("", typeof(Animator), "Spine Front-Back", AnimationCurve.Constant(0, ZeroTwoMusic.Duration, 0));
            return clip;
        }

        public void Apply(float seconds, float weight)
        {
            if (weight <= 0) return;
            weight = Mathf.Clamp01(weight);
            var root = animator.transform;
            Vector3 offset = HipOffset(seconds);
            Vector3 hipTarget = Vector3.Lerp(hips.position, root.TransformPoint(restHips + offset), weight);
            for (int i = 0; i < 2; i++)
            {
                target[i] = Vector3.Lerp(feet[i].position, root.TransformPoint(ankle[i]), weight);
                rotations[i] = Quaternion.Slerp(feet[i].rotation, root.rotation * footRotation[i], weight);
            }
            handler.GetHumanPose(ref pose);
            Sample(seconds, sampled);
            for (int i = 0; i < sampled.Length; i++) pose.muscles[i] = Mathf.Lerp(pose.muscles[i], sampled[i], weight);
            pose.bodyPosition = Vector3.Lerp(pose.bodyPosition, restBody + offset / animator.humanScale, weight);
            float hipPhase = HipPhase(seconds);
            pose.bodyRotation = Quaternion.Slerp(pose.bodyRotation, restRotation * Quaternion.Euler(0, PelvisYaw * Mathf.Sin(hipPhase + .4f), PelvisRoll * Mathf.Sin(hipPhase)), weight);
            handler.SetHumanPose(ref pose);
            // HumanPose.bodyPosition is a centre of mass, not a pelvis position.
            // Anchor the actual pelvis so retargeting preserves the intended travel.
            hips.position = hipTarget;
            // Counterbalance through the spine rather than translating the head or
            // shortening bones. Aim for 5 cm of head travel against 32 cm at the hips.
            Vector3 headTarget = root.TransformPoint(restHead + new Vector3(.025f * Mathf.Sin(hipPhase), offset.y * .25f, .01f * Mathf.Sin(hipPhase * 2)));
            Vector3 headAxis = head.position - spine.position;
            Vector3 aimAxis = headTarget - spine.position;
            if (headAxis.sqrMagnitude > .000001f && aimAxis.sqrMagnitude > .000001f)
                spine.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(headAxis, aimAxis), weight) * spine.rotation;
            head.rotation = Quaternion.Slerp(head.rotation, root.rotation * restHeadRotation, weight);
            for (int i = 0; i < 2; i++)
            {
                SolveTwoBone(thighs[i], knees[i], feet[i], target[i], rotations[i], root.forward, weight);
                float side = i == 0 ? -1 : 1;
                Vector3 nape = head.position + root.right * (side * .10f) - root.forward * .10f + root.up * .01f;
                Vector3 handTarget = Vector3.Lerp(hands[i].position, nape, weight);
                Quaternion wrist = hands[i].localRotation;
                SolveTwoBone(upperArms[i], elbows[i], hands[i], handTarget, hands[i].rotation, root.right * side + root.up * .35f, weight);
                // Preserve the avatar's neutral wrist relative to its forearm, so the
                // fingers follow the inward forearm direction without a broken cuff.
                hands[i].localRotation = Quaternion.Slerp(wrist, restWrist[i], weight);
            }
        }

        static void SolveTwoBone(Transform thigh, Transform knee, Transform foot, Vector3 target, Quaternion rotation, Vector3 bendHint, float weight)
        {
            Quaternion originalUpper = thigh.localRotation, originalLower = knee.localRotation;
            Vector3 origin = thigh.position;
            float upper = Vector3.Distance(origin, knee.position), lower = Vector3.Distance(knee.position, foot.position);
            Vector3 delta = target - origin;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
            Vector3 axis = delta.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(bendHint, axis).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(Vector3.right, axis).normalized;
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 desiredKnee = origin + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            thigh.rotation = Quaternion.FromToRotation(knee.position - origin, desiredKnee - origin) * thigh.rotation;
            knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, target - knee.position) * knee.rotation;
            // A tiny layer weight must not replace the incoming bend plane with
            // the dance's outward elbow / forward knee plane in a single frame.
            // Blend local joint rotations after solving the complete chain; weight
            // one still reaches the exact hand and ankle targets.
            Quaternion solvedUpper = thigh.localRotation, solvedLower = knee.localRotation;
            thigh.localRotation = Quaternion.Slerp(originalUpper, solvedUpper, weight);
            knee.localRotation = Quaternion.Slerp(originalLower, solvedLower, weight);
            foot.rotation = rotation;
        }

        public void Dispose() => handler.Dispose();
    }
}
