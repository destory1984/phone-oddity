using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class ZeroTwoDanceTests
    {
        [TestCase(0f, 1f, .5f)]
        [TestCase(6f, .5f, .25f)]
        [TestCase(6.9f, .2f, .15f)]
        [TestCase(7f, .2f, .2f)]
        [TestCase(12f, 1f, 1f)]
        [TestCase(0f, 10f, 6.5f)]
        [TestCase(6f, 10f, 9.5f)]
        [TestCase(0f, 0f, 0f)]
        [TestCase(7f, 0f, 0f)]
        [TestCase(12f, 0f, 0f)]
        public void ZeroTwoDance_MotionTimeUsesSlowIntroThenDoublesAtSevenSeconds(float audioStart, float dt, float expected)
        {
            Assert.That(ZeroTwoDance.MotionSecondsBetween(audioStart, dt), Is.EqualTo(expected).Within(.00001f));
        }

        [Test]
        public void ZeroTwoDance_MotionPhaseIsContinuousAcrossSevenSecondsAndPauseResume()
        {
            float before = ZeroTwoDance.MotionSecondsBetween(0, 6.9f);
            float crossing = before + ZeroTwoDance.MotionSecondsBetween(6.9f, .2f);
            float split = before + ZeroTwoDance.MotionSecondsBetween(6.9f, .1f) + ZeroTwoDance.MotionSecondsBetween(7, .1f);
            Assert.That(crossing, Is.EqualTo(split).Within(.00001f));
            Assert.That(crossing, Is.EqualTo(ZeroTwoDance.MotionSecondsBetween(0, 7.1f)).Within(.00001f));
            float phase = Mathf.Repeat(before, ZeroTwoMusic.Duration);
            float audioSeconds = 6.9f, heldPhase = phase;
            for (int i = 0; i < 20; i++)
            {
                phase += ZeroTwoDance.MotionSecondsBetween(audioSeconds, 0);
            }
            Assert.That(phase, Is.EqualTo(heldPhase));
            Assert.That(audioSeconds, Is.EqualTo(6.9f));
            phase = Mathf.Repeat(phase + ZeroTwoDance.MotionSecondsBetween(audioSeconds, .2f), ZeroTwoMusic.Duration);
            audioSeconds += .2f;
            Assert.That(phase, Is.EqualTo(Mathf.Repeat(crossing, ZeroTwoMusic.Duration)).Within(.00001f));
            Assert.That(audioSeconds, Is.EqualTo(7.1f).Within(.00001f));
        }

        [Test]
        public void ZeroTwoDance_I1SmallWeightsPreserveIncomingBentJointPlanes()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/harin-6T-I1-52.vrm");
            if (prefab == null) Assert.Ignore("I1-52 model is not installed.");
            var model = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator = model.GetComponent<Animator>();
                Assert.That(animator != null && animator.isHuman, Is.True);
                using (var dance = new ZeroTwoDance(animator))
                using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                {
                    var incoming = new HumanPose();
                    handler.GetHumanPose(ref incoming);
                    Array.Clear(incoming.muscles, 0, incoming.muscles.Length);
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        // Deliberately fold the arms in front with an inward bend
                        // plane, unlike the dance's outward raised elbows.
                        incoming.muscles[Array.IndexOf(HumanTrait.MuscleName, side + " Arm Down-Up")] = -.4f;
                        incoming.muscles[Array.IndexOf(HumanTrait.MuscleName, side + " Arm Front-Back")] = .6f;
                        incoming.muscles[Array.IndexOf(HumanTrait.MuscleName, side + " Arm Twist In-Out")] = .6f;
                        incoming.muscles[Array.IndexOf(HumanTrait.MuscleName, side + " Forearm Stretch")] = -.6f;
                        incoming.muscles[Array.IndexOf(HumanTrait.MuscleName, side + " Lower Leg Stretch")] = -.4f;
                    }
                    var joints = new[] {
                        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg
                    };
                    handler.SetHumanPose(ref incoming);
                    dance.Apply(ZeroTwoMusic.Duration / 8, 0);
                    var before = Array.ConvertAll(joints, bone => animator.GetBoneTransform(bone).localRotation);
                    foreach (float weight in new[] { .001f, .01f })
                    {
                        // Start each sample from the same incoming animation pose.
                        handler.SetHumanPose(ref incoming);
                        dance.Apply(ZeroTwoMusic.Duration / 8, weight);
                        for (int i = 0; i < joints.Length; i++)
                        {
                            float angle = Quaternion.Angle(before[i], animator.GetBoneTransform(joints[i]).localRotation);
                            Assert.That(angle, Is.LessThan(weight == .001f ? 1f : 5f), joints[i] + " at layer weight " + weight);
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        [Test]
        public void ZeroTwoDance_I1RetargetHasRaisedHandsPlantedFeetAndNoAccumulation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/harin-6T-I1-52.vrm");
            if (prefab == null) Assert.Ignore("I1-52 model is not installed.");
            var model = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator = model.GetComponent<Animator>();
                Assert.That(animator != null && animator.isHuman, Is.True);
                var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Vector3 left = leftFoot.position, right = rightFoot.position;
                var bones = model.GetComponentsInChildren<Transform>();
                using (var dance = new ZeroTwoDance(animator))
                {
                    float minHip = float.PositiveInfinity, maxHip = float.NegativeInfinity;
                    float minHead = float.PositiveInfinity, maxHead = float.NegativeInfinity;
                    for (int frame = 0; frame < 64; frame++)
                    {
                        float seconds = frame * ZeroTwoMusic.Duration / 64;
                        dance.Apply(seconds, 1);
                        Assert.That(Vector3.Distance(left, leftFoot.position), Is.LessThan(.008f), "left ankle at " + frame);
                        Assert.That(Vector3.Distance(right, rightFoot.position), Is.LessThan(.008f), "right ankle at " + frame);
                        Assert.That(animator.GetBoneTransform(HumanBodyBones.LeftHand).position.y, Is.GreaterThan(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.y));
                        Assert.That(animator.GetBoneTransform(HumanBodyBones.RightHand).position.y, Is.GreaterThan(animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position.y));
                        var head = animator.GetBoneTransform(HumanBodyBones.Head);
                        float hipX = model.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position).x;
                        float headX = model.transform.InverseTransformPoint(head.position).x;
                        minHip = Mathf.Min(minHip, hipX); maxHip = Mathf.Max(maxHip, hipX);
                        minHead = Mathf.Min(minHead, headX); maxHead = Mathf.Max(maxHead, headX);
                        for (int side = 0; side < 2; side++)
                        {
                            var hand = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                            Vector3 nape = head.position + model.transform.right * (side == 0 ? -.10f : .10f) - model.transform.forward * .10f + model.transform.up * .01f;
                            Assert.That(Vector3.Distance(hand.position, nape), Is.LessThan(.015f), "hand must reach nape at " + frame);
                        }
                        foreach (var bone in bones)
                        {
                            Quaternion q = bone.rotation;
                            Assert.That(float.IsNaN(q.x + q.y + q.z + q.w) || float.IsInfinity(q.x + q.y + q.z + q.w), Is.False, bone.name);
                        }
                    }
                    Assert.That(maxHip - minHip, Is.GreaterThan(.28f), "actual pelvis must visibly travel side to side");
                    Assert.That(maxHead - minHead, Is.LessThan(maxHip - minHip), "torso must counterbalance pelvis travel");
                    dance.Apply(0, 1);
                    var positions = Array.ConvertAll(bones, b => b.position);
                    var rotations = Array.ConvertAll(bones, b => b.rotation);
                    for (int repeat = 0; repeat < 20; repeat++) dance.Apply(ZeroTwoMusic.Duration, 1);
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Assert.That(Vector3.Distance(positions[i], bones[i].position), Is.LessThan(.0001f), bones[i].name);
                        Assert.That(Quaternion.Angle(rotations[i], bones[i].rotation), Is.LessThan(.05f), bones[i].name);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        [Test]
        public void ZeroTwoDance_LoopIsContinuousAndFinite()
        {
            var a = new float[HumanTrait.MuscleCount];
            var b = new float[a.Length];
            ZeroTwoDance.Sample(0, a);
            ZeroTwoDance.Sample(ZeroTwoMusic.Duration, b);
            Assert.That(Vector3.Distance(ZeroTwoDance.HipOffset(0), ZeroTwoDance.HipOffset(ZeroTwoMusic.Duration)), Is.LessThan(.00001f));
            float epsilon = ZeroTwoMusic.Duration / 100000;
            Assert.That(Vector3.Distance(ZeroTwoDance.HipOffset(ZeroTwoMusic.Duration - epsilon), ZeroTwoDance.HipOffset(epsilon)), Is.LessThan(.0001f));
            for (int i = 0; i < a.Length; i++) Assert.That(b[i], Is.EqualTo(a[i]).Within(0.00001f));
            for (int frame = 0; frame <= 240; frame++)
            {
                ZeroTwoDance.Sample(frame * ZeroTwoMusic.Duration / 240, a);
                foreach (float value in a) Assert.That(!float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1);
            }
        }

        [Test]
        public void ZeroTwoDance_SwayAlternatesWithCounteractingTorso()
        {
            int side = Array.IndexOf(HumanTrait.MuscleName, "Spine Left-Right");
            Assert.That(side, Is.GreaterThanOrEqualTo(0));
            var a = new float[HumanTrait.MuscleCount];
            var b = new float[a.Length];
            float t = ZeroTwoMusic.Duration / 32;
            ZeroTwoDance.Sample(t, a);
            ZeroTwoDance.Sample(t + ZeroTwoMusic.Duration / 8, b);
            Assert.That(Mathf.Abs(a[side]), Is.GreaterThan(.1f));
            Assert.That(b[side], Is.EqualTo(-a[side]).Within(.0001f));
            Assert.That(ZeroTwoDance.HipOffset(t).x * a[side], Is.LessThan(0));
            Assert.That(ZeroTwoDance.HipOffset(t + ZeroTwoMusic.Duration / 8).x, Is.EqualTo(-ZeroTwoDance.HipOffset(t).x).Within(.00001f));
        }

        [Test]
        public void ZeroTwoDance_HipsCompleteFourSwaysPerEightBeatLoop()
        {
            const int samples = 512;
            int positivePeaks = 0;
            for (int i = 0; i < samples; i++)
            {
                float previous = ZeroTwoDance.HipOffset((i - 1) * ZeroTwoMusic.Duration / samples).x;
                float current = ZeroTwoDance.HipOffset(i * ZeroTwoMusic.Duration / samples).x;
                float next = ZeroTwoDance.HipOffset((i + 1) * ZeroTwoMusic.Duration / samples).x;
                if (current > .15f && current > previous && current > next) positivePeaks++;
            }
            Assert.That(positivePeaks, Is.EqualTo(4), "two complete sways per four-beat bar");
        }

        [Test]
        public void ZeroTwoDance_ArmsRemainRaisedAndBentSymmetrically()
        {
            var pose = new float[HumanTrait.MuscleCount];
            ZeroTwoDance.Sample(.7f, pose);
            foreach (string side in new[] { "Left", "Right" })
            {
                Assert.That(pose[Array.IndexOf(HumanTrait.MuscleName, side + " Arm Down-Up")], Is.GreaterThan(.65f));
                Assert.That(pose[Array.IndexOf(HumanTrait.MuscleName, side + " Forearm Stretch")], Is.LessThan(-.7f));
            }
        }
    }
}
