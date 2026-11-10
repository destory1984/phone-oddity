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
    public class MirrorTests
    {
        static GameObject Model()
        {
            var dir = Path.Combine(Application.dataPath, "Models/test");
            var file = Directory.Exists(dir) ? Directory.GetFiles(dir, "harin-6T-O2-*.vrm")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out var n) ? n : -1)
                .FirstOrDefault() : null;
            var prefab = file == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/" + Path.GetFileName(file));
            if (prefab == null) Assert.Ignore("no 6T-O2 model installed");
            var model = Object.Instantiate(prefab);
            var a = model.GetComponent<Animator>();
            if (a != null && a.isHuman) return model;
            Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); return null;
        }

        [Test]
        public void Mirror_TickRunsAllBeatsFor120SecondsWithoutWritingBones()
        {
            var m = Model();
            try
            {
                var a = m.GetComponent<Animator>();
                using (var rig = new MirrorRig(a, 71))
                {
                    var transforms = m.GetComponentsInChildren<Transform>();
                    var rotations = transforms.Select(t => t.localRotation).ToArray();
                    var phases = new HashSet<string>();
                    rig.Start();
                    for (int i = 0; i < 7200; i++)
                    {
                        rig.Tick(1f / 60); phases.Add(rig.Phase);
                        Assert.That(rig.Weight, Is.InRange(0f, 1f));
                        Assert.That(rig.Smile, Is.InRange(0f, 1f));
                        Assert.That(rig.MouthOpen, Is.InRange(0f, 1f));
                        Assert.That(rig.EyesClosed, Is.InRange(0f, 1f));
                    }
                    CollectionAssert.AreEquivalent(new[] { "Check", "Tint", "Press", "Fringe", "Smile", "Squint", "Return" }, phases);
                    Assert.That(rig.Loops, Is.EqualTo(4));
                    for (int i = 0; i < transforms.Length; i++)
                        Assert.That(Quaternion.Angle(rotations[i], transforms[i].localRotation), Is.LessThan(.001f), transforms[i].name);
                    rig.Stop(); rig.Tick(MirrorRig.BlendSeconds / 2);
                    Assert.That(rig.Weight, Is.EqualTo(.5f).Within(.001f));
                    rig.Tick(MirrorRig.BlendSeconds / 2);
                    Assert.IsFalse(rig.Active); Assert.AreEqual("Idle", rig.Phase);
                    Assert.AreEqual(0, rig.MouthOpen + rig.Smile + rig.EyesClosed);
                }
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void Mirror_DefaultModelHoldsFaceHeightDiscAndTintReachesLips()
        {
            var m = Model();
            try
            {
                var a = m.GetComponent<Animator>();
                using (var rig = new MirrorRig(a, 71))
                {
                    m.transform.rotation = Quaternion.Euler(0, 90, 0);
                    var legs = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg };
                    var saved = legs.Select(b => a.GetBoneTransform(b).localRotation).ToArray();
                    rig.Start(); rig.Tick(9); rig.Apply(0);
                    var head = a.GetBoneTransform(HumanBodyBones.Head);
                    Assert.That(Mathf.Abs(rig.MirrorCentre.y - head.position.y), Is.LessThan(.10f));
                    Assert.That(Vector3.Distance(rig.MirrorCentre, head.position), Is.InRange(.24f, .45f));
                    Assert.That(Vector3.Dot(-rig.Mirror.forward, (head.position - rig.MirrorCentre).normalized), Is.GreaterThan(.8f));
                    Assert.That(Vector3.Distance(rig.TintTip, rig.LipPoint), Is.LessThan(.055f));
                    Assert.That(rig.Mirror.Find("rim").lossyScale.x, Is.EqualTo(.12f).Within(.001f));
                    Assert.AreEqual(a.GetBoneTransform(HumanBodyBones.LeftHand), rig.Mirror.parent);
                    for (int i = 0; i < legs.Length; i++) Assert.That(Quaternion.Angle(saved[i], a.GetBoneTransform(legs[i]).localRotation), Is.LessThan(.001f));
                    var wrist = a.GetBoneTransform(HumanBodyBones.RightHand);
                    var position = wrist.position;
                    rig.Apply(0);
                    Assert.That(Vector3.Distance(position, wrist.position), Is.LessThan(.001f), "paused Animator does not accumulate the overlay");
                    rig.Stop(); rig.Tick(MirrorRig.BlendSeconds); rig.Apply(0);
                    Assert.IsFalse(rig.Mirror.gameObject.activeSelf);
                    Assert.IsFalse(rig.Tint.gameObject.activeSelf);
                }
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void Mirror_SameSeedAndLargeTickAgree()
        {
            var a = Model(); var b = Model();
            try
            {
                using (var x = new MirrorRig(a.GetComponent<Animator>(), 9))
                using (var y = new MirrorRig(b.GetComponent<Animator>(), 9))
                {
                    x.Start(); y.Start(); x.Tick(120);
                    for (int i = 0; i < 480; i++) y.Tick(.25f);
                    Assert.AreEqual(x.Loops, y.Loops); Assert.AreEqual(x.Phase, y.Phase);
                    Assert.That(x.Smile, Is.EqualTo(y.Smile).Within(.001f));
                    Assert.That(x.MouthOpen, Is.EqualTo(y.MouthOpen).Within(.001f));
                    x.Tick(float.NaN); x.Tick(-1); x.Tick(float.PositiveInfinity);
                    Assert.That(x.Weight, Is.EqualTo(1));
                }
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
#endif
