#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    public class FloorMealTests
    {
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
        public void FloorMeal_TimelineRunsFor120SecondsAndBlendsWithoutAnAnimator()
        {
            var a = new FloorMealTimeline(104);
            var b = new FloorMealTimeline(104);
            var phases = new HashSet<string>();
            bool chin = false, open = false;
            a.Start(); b.Start();
            for (int i = 0; i < 7200; i++)
            {
                a.Tick(1f / 60); b.Tick(1f / 60);
                phases.Add(a.Phase);
                Assert.AreEqual(a.Phase, b.Phase);
                Assert.AreEqual(a.BowlLift, b.BowlLift);
                Assert.That(a.Weight, Is.InRange(0f, 1f));
                Assert.That(a.MouthOpen, Is.InRange(0f, 1f));
                Assert.That(a.EyesClosed, Is.InRange(0f, 1f));
                Assert.That(a.Smile, Is.InRange(0f, 1f));
                chin |= a.BowlLift > .95f;
                open |= a.Phase == "Slurping" && a.MouthOpen > .4f;
            }
            CollectionAssert.AreEquivalent(new[] { "Gathering", "Lifting", "Slurping", "Chewing", "Lowering", "Phone", "Laughing", "Returning" }, phases);
            Assert.IsTrue(chin); Assert.IsTrue(open);
            a.Stop(); a.Tick(FloorMealTimeline.BlendSeconds / 2);
            Assert.That(a.Weight, Is.EqualTo(.5f).Within(.0001f));
            Assert.IsTrue(a.Active);
            a.Tick(FloorMealTimeline.BlendSeconds / 2);
            Assert.IsFalse(a.Active); Assert.AreEqual("Stopped", a.Phase);
            Assert.AreEqual(0f, a.MouthOpen); Assert.AreEqual(0f, a.Smile);
            a.Start(); a.Tick(float.NaN); a.Tick(-1); a.Tick(float.PositiveInfinity);
            Assert.AreEqual(0f, a.Weight);
            a.Tick(FloorMealTimeline.BlendSeconds);
            Assert.AreEqual(1f, a.Weight);
        }

        [Test]
        public void FloorMeal_DefaultModelHasCrossedShinsAndStableOverlay()
        {
            var model = LoadModel();
            FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                // The caller supplies an upright idle, with root motion disabled.
                model.transform.rotation = rig.Props.rotation;
                rig.Start(); rig.Tick(FloorMealTimeline.BlendSeconds);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                var beforeTick = hips.position;
                rig.Tick(.01f);
                Assert.AreEqual(beforeTick, hips.position, "Tick never writes bones");
                rig.Apply(0);
                Assert.That(rig.Props.InverseTransformPoint(hips.position).y, Is.EqualTo(FloorMealRig.HipHeight).Within(.002f));
                var lk = rig.Props.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position);
                var rk = rig.Props.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.RightLowerLeg).position);
                var lf = rig.Props.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.LeftFoot).position);
                var rf = rig.Props.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.RightFoot).position);
                Assert.That(lk.x, Is.LessThan(-.15f)); Assert.That(rk.x, Is.GreaterThan(.15f));
                Assert.That(lf.x, Is.GreaterThan(.06f)); Assert.That(rf.x, Is.LessThan(-.06f));
                Assert.That(lf.z, Is.GreaterThan(.1f)); Assert.That(rf.z, Is.GreaterThan(.1f));
                Assert.That(Mathf.Abs(lf.y - rf.y), Is.GreaterThan(.035f), "ankles are vertically separated");
                Assert.That(lk.y, Is.GreaterThan(-.01f)); Assert.That(rk.y, Is.GreaterThan(-.01f));
                var saved = hips.position;
                for (int i = 0; i < 20; i++) rig.Apply(0);
                Assert.That(Vector3.Distance(saved, hips.position), Is.LessThan(.001f), "paused Apply does not accumulate translation");
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget), Is.LessThan(.06f));
                Assert.That(rig.Table.localPosition.y + rig.Table.localScale.y / 2,
                    Is.EqualTo(FloorMealRig.TableHeight).Within(.001f));
                Assert.That(rig.Bowl.position.y, Is.GreaterThan(rig.Props.position.y + FloorMealRig.TableHeight));
                Assert.That(rig.Phone.position.y, Is.GreaterThan(rig.Props.position.y + FloorMealRig.TableHeight));
                rig.Stop(); rig.Tick(FloorMealTimeline.BlendSeconds); rig.Apply(0);
                Assert.That(Vector3.Distance(beforeTick, hips.position), Is.LessThan(.001f));
                Assert.IsFalse(rig.Chopsticks.gameObject.activeSelf);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void FloorMeal_BowlAndNoodlesFollowTheSlurpInTheDefaultModel()
        {
            var model = LoadModel();
            FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Phase != "Slurping") continue;
                    rig.Apply(0);
                    Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget), Is.LessThan(.06f));
                    Assert.That(rig.Noodles.gameObject.activeSelf, Is.True);
                    Assert.That(rig.MouthOpen, Is.GreaterThan(0f));
                    rig.Stop(); rig.Tick(FloorMealTimeline.BlendSeconds); rig.Apply(0);
                    Assert.IsFalse(rig.Active);
                    Assert.IsFalse(rig.Noodles.gameObject.activeSelf);
                    Assert.That(rig.Bowl.localPosition.y, Is.EqualTo(FloorMealRig.TableHeight + FloorMealRig.BowlHeight / 2).Within(.001f));
                    Assert.AreEqual(0f, rig.MouthOpen);
                    return;
                }
                Assert.Fail("No slurp in 120 seconds");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
#endif
