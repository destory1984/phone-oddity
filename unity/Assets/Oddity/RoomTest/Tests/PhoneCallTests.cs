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
    public class PhoneCallTests
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
        public void PhoneCall_PaceTargetsRespectFloorClearanceAndDistance()
        {
            var floors = new List<Rect> { new Rect(0, 0, 3f, 2.5f) };
            var blockers = new List<Rect> { new Rect(.25f, .35f, .55f, .6f), new Rect(1.9f, 1.35f, .6f, .7f) };
            var rng = new System.Random(410);
            Vector2 previous = new Vector2(1.5f, .25f);
            for (int i = 0; i < 200; i++)
            {
                var next = PacePlan.Next(floors, blockers, previous, rng);
                Assert.IsTrue(floors[0].Contains(next.Target));
                Assert.That(Vector2.Distance(previous, next.Target), Is.GreaterThanOrEqualTo(.8f));
                foreach (var b in blockers)
                {
                    var closest = new Vector2(Mathf.Clamp(next.Target.x, b.xMin, b.xMax), Mathf.Clamp(next.Target.y, b.yMin, b.yMax));
                    Assert.That(Vector2.Distance(closest, next.Target), Is.GreaterThanOrEqualTo(.35f));
                }
                Assert.That(next.StandSeconds, Is.InRange(1.5f, 4f));
                previous = next.Target;
            }
        }

        [Test]
        public void PhoneCall_PaceFailureAndSeedAreExplicit()
        {
            var floors = new List<Rect> { new Rect(0, 0, 3, 2.5f) };
            var blocked = new List<Rect> { floors[0] };
            Assert.IsFalse(PacePlan.TryNext(floors, blocked, Vector2.zero, new System.Random(1), out _));
            var a = new System.Random(9);
            var b = new System.Random(9);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(PacePlan.Next(floors, null, Vector2.zero, a).Target,
                    PacePlan.Next(floors, null, Vector2.zero, b).Target);
        }

        [Test]
        public void PhoneCall_PhoneFollowsEarAndStopLowersAndHidesIt()
        {
            var model = LoadModel();
            PhoneCallRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new PhoneCallRig(anim, 17);
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(t => t.localRotation).ToArray();
                Action reset = () => { for (int j = 0; j < bones.Length; j++) bones[j].localRotation = rest[j]; };
                rig.Start();
                for (int i = 0; i < 30; i++) { reset(); rig.Tick(1f / 60); rig.Apply(1f / 60); }
                Assert.That(rig.Weight, Is.EqualTo(1f));
                Assert.IsTrue(rig.Phone.gameObject.activeSelf);
                Assert.That(Vector3.Distance(rig.Phone.position, rig.EarTarget), Is.LessThanOrEqualTo(.06f));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.Phone.position), Is.LessThanOrEqualTo(.08f));
                foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                    HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var bone = anim.GetBoneTransform(id);
                    Assert.That(Quaternion.Angle(bone.localRotation, rest[Array.IndexOf(bones, bone)]), Is.LessThan(.001f), id + " stays with the clip");
                }
                var firstPosition = rig.Phone.position;
                for (int i = 0; i < 10; i++) rig.Apply(0);
                Assert.That(Vector3.Distance(firstPosition, rig.Phone.position), Is.LessThan(.001f), "paused Animator must not accumulate the overlay");
                // An external head turn is followed in the same LateUpdate, without a frame of lag.
                reset();
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                head.rotation = Quaternion.AngleAxis(12f, model.transform.up) * head.rotation;
                rig.Apply(0);
                Assert.That(Vector3.Distance(rig.Phone.position, rig.EarTarget), Is.LessThanOrEqualTo(.06f));
                float heldY = rig.Phone.position.y;
                rig.Stop(); reset(); rig.Tick(.2f); rig.Apply(.2f);
                Assert.That(rig.Phone.position.y, Is.LessThan(heldY));
                reset(); rig.Tick(.21f); rig.Apply(.21f);
                Assert.IsFalse(rig.Active);
                Assert.IsFalse(rig.Phone.gameObject.activeSelf);
                Assert.That(rig.MouthOpen, Is.Zero);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void PhoneCall_TalkListenDurationsStayInRangeFor120Seconds()
        {
            var model = LoadModel();
            PhoneCallRig rig = null;
            try
            {
                rig = new PhoneCallRig(model.GetComponent<Animator>(), 33);
                rig.Start();
                bool previous = rig.Talking;
                float elapsed = 0;
                int transitions = 0;
                const float dt = .01f;
                for (int i = 0; i < 12000; i++)
                {
                    rig.Tick(dt); elapsed += dt;
                    Assert.That(rig.StateDuration, Is.InRange(2f, rig.Talking ? 5f : 6f));
                    Assert.That(rig.StateElapsed, Is.InRange(0f, rig.StateDuration));
                    Assert.That(rig.MouthOpen, Is.InRange(0f, 1f));
                    Assert.That(rig.Smile, Is.InRange(0f, 1f));
                    if (!rig.Talking) Assert.That(rig.MouthOpen, Is.Zero);
                    if (previous == rig.Talking) continue;
                    Assert.That(elapsed, Is.InRange(2f - dt * 2, (previous ? 5f : 6f) + dt * 2));
                    elapsed = 0; previous = rig.Talking; transitions++;
                }
                Assert.That(transitions, Is.GreaterThan(15));
                Assert.That(rig.ReactionCount, Is.InRange(6, 12));
                Assert.IsTrue(rig.LastReaction == "Laugh" || rig.LastReaction == "Sigh");
                rig.Tick(30f); // Overshoot must carry through several states.
                Assert.That(rig.StateElapsed, Is.InRange(0f, rig.StateDuration));
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
