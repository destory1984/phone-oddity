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
    public sealed class WakeTests
    {
        [Test]
        public void Wake_TickRunsOnceAndStaysDoneFor120Seconds()
        {
            var clock = new WakeClock();
            clock.Start();
            var phases = new HashSet<string>();
            float maxMouth = 0;
            for (int i = 0; i < 7200; i++)
            {
                clock.Tick(1f / 60f);
                phases.Add(clock.Phase);
                maxMouth = Mathf.Max(maxMouth, clock.MouthOpen);
                Assert.That(clock.Weight, Is.InRange(0f, 1f));
                Assert.That(clock.EyesClosed, Is.InRange(0f, 1f));
                if (i > 481) { Assert.IsFalse(clock.Active); Assert.IsTrue(clock.Done); }
            }
            CollectionAssert.IsSubsetOf(new[] { "Raise", "Stretch", "Lower", "RubEye", "ScratchHead", "Shake", "Settle", "Done" }, phases);
            Assert.That(maxMouth, Is.EqualTo(.9f).Within(.001f));
            Assert.That(clock.Weight, Is.Zero);
            Assert.That(clock.MouthOpen, Is.Zero);
            clock.Start();
            Assert.IsFalse(clock.Done);
            Assert.IsTrue(clock.Active);
        }

        [Test]
        public void Wake_BlendStopAndLargeStepsAreConsistent()
        {
            var a = new WakeClock();
            a.Start(); a.Tick(.2f);
            Assert.That(a.Weight, Is.EqualTo(.5f).Within(.0001f));
            a.Stop(); a.Tick(.2f);
            Assert.IsFalse(a.Active);
            Assert.IsFalse(a.Done, "cancelling does not complete the one-shot");
            a.Start(); a.Tick(WakeClock.RaiseEnd);
            Assert.AreEqual("Stretch", a.Phase);
            a.Tick(WakeClock.StretchSeconds);
            Assert.AreEqual("Lower", a.Phase);
            var b = new WakeClock(); b.Start(); b.Tick(120f);
            a.Tick(120f);
            Assert.IsTrue(a.Done); Assert.IsTrue(b.Done);
            Assert.That(a.Weight, Is.EqualTo(b.Weight));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(float.NaN));
        }

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

        [TestCase(0f)]
        [TestCase(90f)]
        public void Wake_OverheadGeometryAndLegsBelongToBaseClip(float yaw)
        {
            var model = LoadModel();
            WakeRig rig = null;
            try
            {
                model.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var anim = model.GetComponent<Animator>();
                rig = new WakeRig(anim, 17);
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(b => b.localRotation).ToArray();
                rig.Start(); rig.Tick(1.5f);
                // Tick never writes any transform.
                for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(rest[i], bones[i].localRotation), Is.LessThan(.001f));
                rig.Apply(0);
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                var left = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                var right = anim.GetBoneTransform(HumanBodyBones.RightHand);
                Assert.That(Vector3.Dot(left.position - head.position, model.transform.up), Is.GreaterThan(.12f));
                Assert.That(Vector3.Dot(right.position - head.position, model.transform.up), Is.GreaterThan(.12f));
                Assert.That(Vector3.Distance(left.position, right.position), Is.InRange(.07f, .16f), "wrists close enough for interlaced fingers");
                foreach (var id in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    bool l = id == HumanBodyBones.LeftHand;
                    var u = anim.GetBoneTransform(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                    var e = anim.GetBoneTransform(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                    var h = anim.GetBoneTransform(id);
                    Assert.That(Vector3.Angle(e.position - u.position, h.position - e.position), Is.LessThan(25f), "straight stretch elbow");
                }
                foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var b = anim.GetBoneTransform(id);
                    Assert.That(Quaternion.Angle(rest[Array.IndexOf(bones, b)], b.localRotation), Is.LessThan(.001f), id.ToString());
                }
                var keep = bones.Select(b => b.localRotation).ToArray();
                rig.Apply(0);
                for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(keep[i], bones[i].localRotation), Is.LessThan(.01f), "no accumulation");
                rig.Tick(120f); rig.Apply(0);
                Assert.IsTrue(rig.Done); Assert.IsFalse(rig.Active);
                for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(rest[i], bones[i].localRotation), Is.LessThan(.01f), "released overlay");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
