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
    public class RamenTests
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
            if (anim == null || !anim.isHuman) { Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); }
            return model;
        }

        [Test]
        public void Ramen_TimelineRunsThreeOrderedLoopsIn120Seconds()
        {
            var clock = new RamenClock();
            clock.Start();
            var seen = new List<string> { clock.Phase };
            // Binary-exact dt avoids ambiguous floating-point boundary assertions.
            for (int i = 0; i < 960; i++)
            {
                string previous = clock.Phase;
                clock.Tick(.125f);
                if (clock.Phase != previous) seen.Add(clock.Phase);
                Assert.That(clock.Weight, Is.InRange(0f, 1f));
            }
            Assert.That(clock.Loops, Is.EqualTo(3));
            Assert.That(clock.Time, Is.Zero);
            CollectionAssert.AreEqual(new[] { "Fill", "Carry", "SwitchOn", "Wait", "Phone", "Packet", "Tear", "Drop", "Stir", "SwitchOff", "Return", "Fill" }, seen.Take(12));
            clock.Stop(); clock.Tick(.2f);
            Assert.That(clock.Weight, Is.EqualTo(.5f).Within(.00001f));
            clock.Tick(.2f); Assert.IsFalse(clock.Active);
            Assert.That(clock.Phase, Is.EqualTo("Idle"));
            clock.Start(); clock.Tick(float.NaN); clock.Tick(-1f);
            Assert.That(clock.Time, Is.Zero);
            clock.Tick(120f); Assert.That(clock.Loops, Is.EqualTo(3));
        }

        [Test]
        public void Ramen_DefaultModelReachesHandleAndStirsWithoutChangingLegs()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2.20f, 0, 4.06f), Quaternion.Euler(0, 90, 0)); // where the game ends up: the counter blocker stops her about 2.20 m
                var anim = model.GetComponent<Animator>();
                rig = new RamenRig(anim, 17);
                rig.Sink = OneRoom.RamenSink; rig.Burner = OneRoom.RamenBurner; rig.Knob = OneRoom.RamenKnob; rig.TapControl = OneRoom.RamenTap; // the studio kitchenette contacts
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(t => t.localRotation).ToArray();
                Action reset = () => { for (int i = 0; i < bones.Length; i++) bones[i].localRotation = rest[i]; };
                rig.Start(); rig.Tick(3f); rig.Apply(0);
                Assert.That(rig.Phase, Is.EqualTo("Fill"));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position, rig.LeftTarget), Is.LessThan(.035f));
                Assert.That(rig.Pot.position.y, Is.EqualTo(rig.Sink.y).Within(.001f));
                var shell = rig.Pot.Find("base");
                Assert.That(shell.localScale.x, Is.EqualTo(.18f).Within(.0001f));
                reset(); rig.Tick(26f); rig.Apply(0);
                Assert.That(rig.Phase, Is.EqualTo("Stir"));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightTarget), Is.LessThan(.15f)); // the far side of the stir circle is out of reach by up to 0.14 m from where the counter blocker stops her
                Assert.That(rig.Chopsticks.parent, Is.EqualTo(anim.GetBoneTransform(HumanBodyBones.RightHand)));
                foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var bone = anim.GetBoneTransform(id);
                    Assert.That(Quaternion.Angle(bone.localRotation, rest[Array.IndexOf(bones, bone)]), Is.LessThan(.001f), id.ToString());
                }
                var wrist = anim.GetBoneTransform(HumanBodyBones.RightHand).position;
                for (int i = 0; i < 10; i++) rig.Apply(0);
                Assert.That(Vector3.Distance(wrist, anim.GetBoneTransform(HumanBodyBones.RightHand).position), Is.LessThan(.001f));
                rig.Stop(); reset(); rig.Tick(.4f); rig.Apply(0);
                Assert.IsFalse(rig.Active); Assert.IsFalse(rig.Props.gameObject.activeSelf);
                Assert.That(rig.Smile + rig.EyesClosed + rig.MouthOpen, Is.Zero);
                rig.Dispose(); rig.Dispose();
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void Ramen_TickDoesNotWriteBonesAndSeedRepeatsFor120Seconds()
        {
            var model = LoadModel(); RamenRig a = null, b = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                a = new RamenRig(anim, 22); b = new RamenRig(anim, 22);
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(t => t.localRotation).ToArray();
                a.Start(); b.Start();
                for (int i = 0; i < 960; i++)
                {
                    a.Tick(.125f); b.Tick(.125f);
                    Assert.That(a.Phase, Is.EqualTo(b.Phase));
                    Assert.That(a.EyesClosed, Is.EqualTo(b.EyesClosed));
                    Assert.That(a.Smile, Is.InRange(0f, 1f));
                    Assert.That(a.EyesClosed, Is.InRange(0f, 1f));
                }
                for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(rest[i], bones[i].localRotation), Is.LessThan(.001f));
                a.Tick(24f); a.Apply(0); // Interrupt while the packet is parented to the hand.
                Assert.That(a.Packet.parent, Is.EqualTo(anim.GetBoneTransform(HumanBodyBones.LeftHand)));
                a.Stop(); a.Tick(.4f); a.Apply(0);
                Assert.IsFalse(a.Packet.gameObject.activeSelf);
            }
            finally { a?.Dispose(); b?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
