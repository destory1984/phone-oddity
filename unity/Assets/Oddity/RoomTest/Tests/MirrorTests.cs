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
                    Assert.That(Vector3.Distance(rig.MirrorCentre, head.position), Is.InRange(.15f, .45f));
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

        // three-fixes item 3: she looks into the mirror - 0.18-0.27 m from the eyes, face turned to them, elbow bent near 90 deg, right hand clear of it
        [Test]
        public void Mirror_IsHeldInFrontOfTheFaceFacingTheEyesWithABentElbow()
        {
            var m = Model();
            try
            {
                var a = m.GetComponent<Animator>();
                using (var rig = new MirrorRig(a, 71))
                {
                    m.transform.rotation = Quaternion.Euler(0, 90, 0);
                    var shoulder = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); var elbow = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); var wrist = a.GetBoneTransform(HumanBodyBones.LeftHand);
                    var rhand = a.GetBoneTransform(HumanBodyBones.RightHand);
                    float dMin = 9, dMax = 0, aimWorst = 0, eMin = 999, eMax = 0, tintMirror = 9, handForearm = 9; int frames = 0;
                    rig.Start();
                    for (int i = 0; i < 100; i++)
                    {
                        rig.Tick(.25f); rig.Apply(0);
                        if (rig.Weight < 1 || rig.Phase == "Smile" || rig.Phase == "Squint" || rig.Phase == "Return") continue;
                        frames++;
                        var toEye = rig.EyePoint - rig.MirrorCentre;
                        dMin = Mathf.Min(dMin, toEye.magnitude); dMax = Mathf.Max(dMax, toEye.magnitude);
                        aimWorst = Mathf.Max(aimWorst, Vector3.Angle(rig.MirrorNormal, toEye));
                        float e = Vector3.Angle(shoulder.position - elbow.position, wrist.position - elbow.position); eMin = Mathf.Min(eMin, e); eMax = Mathf.Max(eMax, e);
                        tintMirror = Mathf.Min(tintMirror, Vector3.Distance(rig.TintTip, rig.MirrorCentre), Vector3.Distance(rhand.position, rig.MirrorCentre));
                        var seg = wrist.position - elbow.position; float tt = Mathf.Clamp01(Vector3.Dot(rhand.position - elbow.position, seg) / seg.sqrMagnitude);
                        handForearm = Mathf.Min(handForearm, Vector3.Distance(rhand.position, elbow.position + seg * tt));
                    }
                    Debug.Log("MIRROR_LOOK frames=" + frames + " eye-to-mirror " + dMin.ToString("0.000") + " - " + dMax.ToString("0.000") + " m, normal off the eyes " + aimWorst.ToString("0.0") + " deg, left elbow " + eMin.ToString("0") + " - " + eMax.ToString("0") + " deg, right hand/tint to the disc " + tintMirror.ToString("0.000") + " m, right hand to left forearm " + handForearm.ToString("0.000") + " m");
                    Assert.That(frames, Is.GreaterThan(30));
                    Assert.That(dMin, Is.GreaterThanOrEqualTo(.18f)); Assert.That(dMax, Is.LessThanOrEqualTo(.27f));
                    Assert.That(aimWorst, Is.LessThan(10f), "the disc faces her eyes");
                    Assert.That(eMin, Is.GreaterThanOrEqualTo(70f)); Assert.That(eMax, Is.LessThanOrEqualTo(115f), "elbow bent about 90 degrees");
                    Assert.That(tintMirror, Is.GreaterThan(.07f), "the right hand does not pass through the mirror");
                    Assert.That(handForearm, Is.GreaterThan(.04f), "the right hand does not pass through the left forearm");
                }
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void Mirror_LeftHandIsAClosedGripFingerPadsOnTheHandleNeverInTheDisc()
        {
            var m = Model();
            try
            {
                var a = m.GetComponent<Animator>();
                using (var rig = new MirrorRig(a, 71))
                {
                    m.transform.rotation = Quaternion.Euler(0, 90, 0);
                    float gapMax = 0, gapMin = 9, discMin = 9, thumbMin = 9; int frames = 0; var perFinger = new float[4];
                    rig.Start();
                    for (int i = 0; i < 100; i++)
                    {
                        rig.Tick(.25f); rig.Apply(0);
                        if (rig.Weight < 1) continue;
                        frames++;
                        for (int f = 0; f < 4; f++)
                        {
                            var tip = rig.LeftFingerTip(f);
                            float gap = rig.HandleSurfaceDistance(tip);
                            perFinger[f] = Mathf.Max(perFinger[f], gap); gapMax = Mathf.Max(gapMax, gap); gapMin = Mathf.Min(gapMin, gap);
                            discMin = Mathf.Min(discMin, rig.DiscSurfaceDistance(tip));
                        }
                        thumbMin = Mathf.Min(thumbMin, rig.HandleSurfaceDistance(rig.LeftThumbTip));
                    }
                    Debug.Log("MIRROR_GRIP frames=" + frames + " finger pad to handle surface " + gapMin.ToString("0.000") + " - " + gapMax.ToString("0.000") + " m, nearest to the disc " + discMin.ToString("0.000") + " m, worst per finger " + string.Join("/", perFinger.Select(x => x.ToString("0.000"))) + ", thumb tip to handle " + thumbMin.ToString("0.000") + " m");
                    Assert.That(frames, Is.GreaterThan(60));
                    Assert.That(gapMax, Is.LessThanOrEqualTo(.01f), "every finger pad within 1 cm of the handle");
                    Assert.That(gapMin, Is.GreaterThanOrEqualTo(-.004f), "no finger pad buried in the handle");
                    Assert.That(discMin, Is.GreaterThanOrEqualTo(0f), "no finger pad through the disc");
                }
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
#endif
