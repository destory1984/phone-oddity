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
    public class LaundryTests
    {
        [Test]
        public void Laundry_FivePiecesThenOnePatAndDoneOver120Seconds()
        {
            var clock = new LaundryTimeline(51);
            clock.Start();
            var entered = new Dictionary<string, int> { { clock.Phase, 1 } };
            string previous = clock.Phase;
            int completed = 0;
            for (int i = 0; i < 12000; i++)
            {
                clock.Tick(.01f);
                Assert.That(clock.Progress, Is.InRange(0f, 1f));
                Assert.That(clock.Completed, Is.InRange(completed, completed + 1));
                completed = clock.Completed;
                if (clock.Phase == previous) continue;
                entered[clock.Phase] = entered.TryGetValue(clock.Phase, out int count) ? count + 1 : 1;
                previous = clock.Phase;
            }
            foreach (string phase in new[] { "Reach", "Lift", "Shake", "Lower", "FoldLength", "FoldWidth", "Place", "Return" })
                Assert.That(entered[phase], Is.EqualTo(5), phase);
            Assert.That(entered["Pat"], Is.EqualTo(1));
            Assert.That(entered["Done"], Is.EqualTo(1));
            Assert.That(clock.Completed, Is.EqualTo(5));
            Assert.IsTrue(clock.Done);
            clock.Tick(120f);
            Assert.That(clock.Completed, Is.EqualTo(5));
            Assert.That(clock.Phase, Is.EqualTo("Done"));
        }

        [Test]
        public void Laundry_ClockCarriesOvershootFreezesOnStopAndRestartsWithSeed()
        {
            var small = new LaundryTimeline(41);
            var large = new LaundryTimeline(41);
            small.Start(); large.Start();
            for (int i = 0; i < 1500; i++) small.Tick(.01f);
            large.Tick(15f);
            Assert.That(large.Completed, Is.EqualTo(small.Completed));
            Assert.That(large.Phase, Is.EqualTo(small.Phase));
            Assert.That(large.Progress, Is.EqualTo(small.Progress).Within(.001f));
            string phase = large.Phase; float progress = large.Progress;
            large.Stop(); large.Tick(120f);
            Assert.That(large.Phase, Is.EqualTo(phase));
            Assert.That(large.Progress, Is.EqualTo(progress));
            large.Start();
            Assert.That(large.Completed, Is.Zero);
            Assert.That(large.Phase, Is.EqualTo("Reach"));
            float firstDuration = large.Duration;
            large.Tick(float.NaN); large.Tick(float.PositiveInfinity); large.Tick(-1);
            Assert.That(large.Progress, Is.Zero);
            large.Start(); Assert.That(large.Duration, Is.EqualTo(firstDuration));
            large.Tick(120f); Assert.IsTrue(large.Done);
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
            { Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); }
            return model;
        }

        [Test]
        public void Laundry_DefaultModelGripsTopCornersAndPreservesLegsAndFoldedStack()
        {
            var model = LoadModel();
            LaundryRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new LaundryRig(anim, 17); // Calibrate before sampling the seated clip.
                var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Oddity/DevMotion/Resources/Motion/KaSitPutHandBetweensLoop.fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) Assert.Ignore("no seated base clip installed");
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                Action sample = () =>
                {
                    clip.SampleAnimation(model, 0);
                    model.transform.rotation = Quaternion.Euler(0, 90f, 0);
                    model.transform.position += new Vector3(.81f, .52f, 1.5f) - hips.position;
                };
                sample();
                rig.Props.SetPositionAndRotation(new Vector3(.81f, .46f, 1.5f), Quaternion.Euler(0, 90, 0));
                var legs = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                    HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                    .Select(anim.GetBoneTransform).ToArray();
                rig.Start();
                bool checkedGrip = false, checkedOnce = false, checkedTwice = false;
                string previous = "";
                var allBones = model.GetComponentsInChildren<Transform>();
                for (int i = 0; i < 2600; i++)
                {
                    sample();
                    var legRotations = legs.Select(b => b.localRotation).ToArray();
                    var legPositions = legs.Select(b => b.localPosition).ToArray();
                    var untouched = allBones.Select(b => b.localRotation).ToArray();
                    rig.Tick(.02f);
                    for (int j = 0; j < allBones.Length; j++)
                        Assert.That(Quaternion.Angle(untouched[j], allBones[j].localRotation), Is.LessThan(.001f), "Tick must not write bones");
                    rig.Apply(.02f);
                    for (int j = 0; j < legs.Length; j++)
                    {
                        Assert.That(Quaternion.Angle(legRotations[j], legs[j].localRotation), Is.LessThan(.001f));
                        Assert.That(Vector3.Distance(legPositions[j], legs[j].localPosition), Is.LessThan(.0001f));
                    }
                    if (rig.Phase == "Shake")
                    {
                        checkedGrip = true;
                        Assert.That(Vector3.Distance(rig.LeftContact, rig.RightContact), Is.EqualTo(LaundryRig.ClothWidth).Within(.001f));
                        var cloth = rig.Piece(rig.Completed);
                        var edge = cloth.position - cloth.forward * LaundryRig.ClothLength * .5f;
                        Assert.That(Vector3.Distance(edge - cloth.right * LaundryRig.ClothWidth * .5f, rig.LeftContact), Is.LessThan(.001f));
                        Assert.That(Vector3.Distance(edge + cloth.right * LaundryRig.ClothWidth * .5f, rig.RightContact), Is.LessThan(.001f));
                        Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position, rig.LeftWristTarget), Is.LessThan(.04f));
                        Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget), Is.LessThan(.04f));
                    }
                    if (rig.Phase != previous && rig.Phase == "FoldWidth")
                    {
                        checkedOnce = true;
                        Assert.That(rig.Piece(rig.Completed).localScale.z, Is.EqualTo(LaundryRig.ClothLength * .5f).Within(.001f));
                    }
                    if (rig.Phase != previous && rig.Phase == "Place")
                    {
                        checkedTwice = true;
                        Assert.That(rig.Piece(rig.Completed).localScale.x, Is.EqualTo(LaundryRig.ClothWidth * .5f).Within(.001f));
                    }
                    previous = rig.Phase;
                }
                Assert.IsTrue(checkedGrip && checkedOnce && checkedTwice);
                Assert.IsTrue(rig.Done); Assert.That(rig.Completed, Is.EqualTo(5));
                for (int i = 0; i < 5; i++)
                {
                    var piece = rig.Piece(i);
                    Assert.That(piece.localScale.x, Is.EqualTo(LaundryRig.ClothWidth * .5f));
                    Assert.That(piece.localScale.z, Is.EqualTo(LaundryRig.ClothLength * .5f));
                    Assert.That(piece.position.y, Is.EqualTo(.46f + LaundryRig.ClothThickness * 4 * (i + .5f)).Within(.001f));
                }
                var rotations = allBones.Select(b => b.localRotation).ToArray();
                for (int i = 0; i < 10; i++) rig.Apply(0);
                for (int i = 0; i < allBones.Length; i++)
                    Assert.That(Quaternion.Angle(rotations[i], allBones[i].localRotation), Is.LessThan(.01f), "paused overlay must not accumulate");
                rig.Stop(); rig.Tick(.2f); Assert.That(rig.Weight, Is.EqualTo(.5f).Within(.001f));
                rig.Tick(.2f); rig.Apply(0);
                Assert.IsFalse(rig.Active); Assert.IsFalse(rig.Props.gameObject.activeSelf);
                Assert.That(rig.Smile + rig.EyesClosed + rig.MouthOpen, Is.Zero);
                rig.Start(); rig.Tick(.4f); rig.Apply(0);
                Assert.That(rig.Completed, Is.Zero); Assert.IsFalse(rig.Done);
                rig.Dispose(); rig.Dispose();
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // item 4: the head bows 10 (was 22) and the hands slide to the new fold corner over 0.3 s instead of snapping when FoldWidth turns into Place
        [Test]
        public void Laundry_FoldCornerSwitchSlidesAndTheUpperBodyBowStaysUnder38Deg()
        {
            var model = LoadModel(); LaundryRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new LaundryRig(anim, 17);
                var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Oddity/DevMotion/Resources/Motion/KaSitPutHandBetweensLoop.fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) Assert.Ignore("no seated base clip installed");
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var head = anim.GetBoneTransform(HumanBodyBones.Head);
                Action sample = () =>
                {
                    clip.SampleAnimation(model, 0);
                    model.transform.rotation = Quaternion.Euler(0, 90f, 0);
                    model.transform.position += new Vector3(.81f, .52f, 1.5f) - hips.position;
                };
                sample();
                rig.Props.SetPositionAndRotation(new Vector3(.81f, .46f, 1.5f), Quaternion.Euler(0, 90, 0));
                rig.Start();
                string previous = ""; Vector3 lastL = default, lastR = default; int sinceSwitch = 99; float boundaryJump = 0, worstAfter = 0, worstBow = 0; int switches = 0;
                for (int i = 0; i < 2600; i++)
                {
                    sample(); var headBefore = head.rotation;
                    rig.Tick(.02f); rig.Apply(.02f);
                    if (rig.Weight >= 1) worstBow = Mathf.Max(worstBow, Quaternion.Angle(headBefore, head.rotation));
                    if (rig.Phase == "Place" && previous == "FoldWidth")
                    {
                        switches++; sinceSwitch = 0;
                        boundaryJump = Mathf.Max(boundaryJump, Vector3.Distance(lastL, rig.LeftContact), Vector3.Distance(lastR, rig.RightContact));
                    }
                    else if (sinceSwitch < 15)
                    {
                        sinceSwitch++;
                        worstAfter = Mathf.Max(worstAfter, Vector3.Distance(lastL, rig.LeftContact), Vector3.Distance(lastR, rig.RightContact));
                    }
                    lastL = rig.LeftContact; lastR = rig.RightContact; previous = rig.Phase;
                }
                Debug.Log("LAUNDRY_SWITCH switches=" + switches + " boundary jump " + boundaryJump.ToString("0.000") + " m, worst step in the next 0.3 s " + worstAfter.ToString("0.000") + " m, worst head turn from the clip " + worstBow.ToString("0.0") + " deg");
                Assert.That(switches, Is.GreaterThan(3));
                Assert.That(boundaryJump, Is.LessThan(.03f), "the hands do not snap to the other corner");
                Assert.That(worstAfter, Is.LessThan(.06f));
                Assert.That(worstBow, Is.LessThan(38f), "spine 12 + chest 5 + neck 4 + head 10 (plus the lean), not 50");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
#endif
