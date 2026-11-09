#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    public sealed class CoffeeTests
    {
        const float Step = .01f, RunSeconds = 120f; // Fine sampling catches short beats over two minutes.
        const float TimeTolerance = .025f, PositionTolerance = .015f; // Timer rounding and IK geometry tolerance.
        const float WristClearance = .10f, RotationTolerance = .002f; // Wrist-to-mug metres and unchanged-bone degrees.
        const int Seed = 204; // Reproducible rest lengths.

        static GameObject LoadModel()
        {
            string dir = Path.Combine(Application.dataPath, "Models/test");
            string file = Directory.Exists(dir) ? Directory.GetFiles(dir, "harin-6T-O2-*.vrm")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out int n) ? n : -1)
                .FirstOrDefault() : null;
            var prefab = file == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/" + Path.GetFileName(file));
            if (prefab == null) Assert.Ignore("no 6T-O2 model installed");
            var model = Object.Instantiate(prefab);
            var anim = model.GetComponent<Animator>();
            if (anim == null || !anim.isHuman)
            {
                Object.DestroyImmediate(model);
                Assert.Ignore("model has no humanoid avatar");
            }
            return model;
        }

        [Test]
        public void Coffee_TwoMinutesVisitsAllBeatsAndHoldsSipAndStreetLook()
        {
            var clock = new CoffeeTimeline(Seed);
            var phases = new HashSet<string>();
            clock.Start();
            string previous = clock.Phase;
            float held = 0;
            int sipCount = 0, lookCount = 0;
            for (int i = 0; i < Mathf.RoundToInt(RunSeconds / Step); i++)
            {
                clock.Tick(Step);
                phases.Add(clock.Phase);
                Assert.That(clock.Weight, Is.InRange(0f, 1f));
                Assert.That(clock.LookingOut, Is.EqualTo(clock.Phase == "LookingOut"));
                Assert.That(clock.Lift, Is.InRange(0f, 1f));
                if (clock.Phase != previous)
                {
                    if (previous == "Sip") { Assert.That(held, Is.EqualTo(1.2f).Within(TimeTolerance)); sipCount++; }
                    if (previous == "LookingOut") { Assert.That(held, Is.EqualTo(2f).Within(TimeTolerance)); lookCount++; }
                    previous = clock.Phase; held = 0;
                }
                held += Step;
                if (clock.LookingOut) Assert.That(clock.Look, Is.EqualTo(1f));
                if (clock.Phase == "Sip") Assert.That(clock.Lift, Is.EqualTo(1f));
            }
            foreach (string beat in new[] { "Blow", "Raise", "Sip", "Lower", "Gaze", "Sigh", "Lean", "TurnOut", "LookingOut", "ReturnOut" })
                Assert.That(phases, Does.Contain(beat));
            Assert.That(sipCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(lookCount, Is.GreaterThanOrEqualTo(3));
            clock.Stop(); clock.Tick(CoffeeRig.BlendSeconds / 2);
            Assert.That(clock.Weight, Is.EqualTo(.5f).Within(.0001f));
            Assert.IsTrue(clock.Active); Assert.IsFalse(clock.LookingOut);
            clock.Tick(CoffeeRig.BlendSeconds / 2);
            Assert.IsFalse(clock.Active); Assert.AreEqual("Off", clock.Phase);
        }

        [Test]
        public void Coffee_SeedAndOvershootAreDeterministicAndStartIsIdempotent()
        {
            var a = new CoffeeTimeline(Seed); var b = new CoffeeTimeline(Seed);
            a.Start(); b.Start();
            for (int i = 0; i < 120; i++)
            {
                a.Start(); // Repeated caller requests do not restart a beat.
                a.Tick(1f);
                for (int j = 0; j < 10; j++) b.Tick(.1f);
                Assert.AreEqual(a.Phase, b.Phase);
                Assert.That(a.Elapsed, Is.EqualTo(b.Elapsed).Within(.001f));
                Assert.That(a.Duration, Is.EqualTo(b.Duration));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(float.PositiveInfinity));
        }

        [Test]
        public void Coffee_DefaultModelBothHandsHoldMugAndRimReachesLipsWithoutMovingLegs()
        {
            var model = LoadModel(); CoffeeRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new CoffeeRig(anim, Seed);
                model.transform.SetPositionAndRotation(new Vector3(1.5f, 0, .62f), Quaternion.Euler(0, 180, 0));
                var bones = model.GetComponentsInChildren<Transform>();
                var original = bones.Select(t => t.localRotation).ToArray();
                var positions = bones.Select(t => t.localPosition).ToArray();
                var lowerBones = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                    HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
                var legRotations = lowerBones.Select(b => anim.GetBoneTransform(b).localRotation).ToArray();
                rig.Start();
                bool checkedSip = false;
                var checkedPhases = new HashSet<string>();
                for (int i = 0; i < 400; i++)
                {
                    // Simulate an Animator resampling the neutral base pose every frame.
                    for (int j = 0; j < bones.Length; j++) { bones[j].localRotation = original[j]; bones[j].localPosition = positions[j]; }
                    rig.Tick(.05f);
                    for (int j = 0; j < bones.Length; j++)
                        Assert.That(Quaternion.Angle(original[j], bones[j].localRotation), Is.LessThan(RotationTolerance), "Tick wrote a bone");
                    rig.Apply(.05f);
                    if (rig.Weight < 1) continue;
                    checkedPhases.Add(rig.Phase);
                    foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                        Assert.That(Vector3.Distance(anim.GetBoneTransform(b).position, rig.Mug.position), Is.LessThan(WristClearance), rig.Phase + " " + b);
                    for (int j = 0; j < lowerBones.Length; j++)
                        Assert.That(Quaternion.Angle(legRotations[j], anim.GetBoneTransform(lowerBones[j]).localRotation), Is.LessThan(RotationTolerance), lowerBones[j].ToString());
                    if (rig.Phase == "Sip")
                    {
                        checkedSip = true;
                        Assert.That(Vector3.Distance(rig.MugRimNear, rig.LipPoint), Is.LessThan(PositionTolerance));
                        Assert.That(rig.EyesClosed, Is.EqualTo(.5f).Within(.001f));
                    }
                    Assert.That(rig.Smile, Is.InRange(0f, 1f));
                    Assert.That(rig.EyesClosed, Is.InRange(0f, 1f));
                    Assert.That(rig.MouthOpen, Is.InRange(0f, 1f));
                    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    Assert.AreSame(hand, rig.Mug.parent);
                }
                Assert.IsTrue(checkedSip); Assert.That(checkedPhases, Does.Contain("Gaze"));
                var wall = rig.Mug.Find("ceramic_wall");
                Assert.That(wall.localScale.y, Is.EqualTo(.085f).Within(.0001f));
                Assert.That(rig.Mug.Find("base").localScale.x, Is.EqualTo(.08f).Within(.0001f));
                Assert.IsNotNull(rig.Mug.Find("handle"));
                Assert.IsEmpty(rig.Mug.GetComponentsInChildren<Collider>(true));
                rig.Stop(); rig.Tick(CoffeeRig.BlendSeconds); rig.Apply(0);
                Assert.IsFalse(rig.Mug.gameObject.activeSelf);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void Coffee_RepeatedApplyDoesNotAccumulateAndDisposeIsIdempotent()
        {
            var model = LoadModel(); CoffeeRig rig = null;
            try
            {
                rig = new CoffeeRig(model.GetComponent<Animator>(), Seed);
                rig.Start(); rig.Tick(CoffeeRig.BlendSeconds); rig.Apply(0);
                var transforms = model.GetComponentsInChildren<Transform>();
                var pose = transforms.Select(t => t.localRotation).ToArray();
                Vector3 mugAt = rig.Mug.position;
                for (int k = 0; k < 30; k++) rig.Apply(0);
                for (int j = 0; j < transforms.Length; j++)
                    Assert.That(Quaternion.Angle(pose[j], transforms[j].localRotation), Is.LessThan(RotationTolerance));
                Assert.That(Vector3.Distance(mugAt, rig.Mug.position), Is.LessThan(.0001f));
                rig.Dispose(); rig.Dispose();
                Assert.IsFalse(rig.Active); Assert.IsFalse(rig.LookingOut); Assert.IsTrue(rig.Mug == null);
                Assert.Throws<ObjectDisposedException>(() => rig.Start());
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
#endif
