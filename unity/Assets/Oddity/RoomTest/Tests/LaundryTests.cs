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
                        var cloth = rig.Piece(rig.Completed);
                        Assert.That(Vector3.Distance(rig.LeftContact, rig.RightContact), Is.EqualTo(cloth.localScale.x).Within(.001f));
                        var edge = cloth.position + cloth.forward * cloth.localScale.z * .5f; // the held edge is local +z
                        Assert.That(Vector3.Distance(edge - cloth.right * cloth.localScale.x * .5f, rig.LeftContact), Is.LessThan(.001f));
                        Assert.That(Vector3.Distance(edge + cloth.right * cloth.localScale.x * .5f, rig.RightContact), Is.LessThan(.001f));
                        Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position, rig.LeftWristTarget), Is.LessThan(.04f));
                        Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget), Is.LessThan(.04f));
                    }
                    if (rig.Phase != previous && rig.Phase == "FoldWidth")
                    {
                        checkedOnce = true;
                        Assert.That(rig.Piece(rig.Completed).localScale.x, Is.EqualTo(LaundryRig.ClothLength * rig.PieceScale(rig.Completed) * .5f).Within(.002f)); // turned 90 degrees: local x is the folded length
                    }
                    if (rig.Phase != previous && rig.Phase == "Place")
                    {
                        checkedTwice = true;
                        Assert.That(rig.Piece(rig.Completed).localScale.x, Is.EqualTo(LaundryRig.ClothWidth * rig.PieceScale(rig.Completed) * .5f).Within(.002f));
                    }
                    previous = rig.Phase;
                }
                Assert.IsTrue(checkedGrip && checkedOnce && checkedTwice);
                Assert.IsTrue(rig.Done); Assert.That(rig.Completed, Is.EqualTo(5));
                for (int i = 0; i < 5; i++)
                {
                    var piece = rig.Piece(i);
                    Assert.That(piece.localScale.x, Is.EqualTo(LaundryRig.ClothWidth * rig.PieceScale(i) * .5f).Within(.0001f));
                    Assert.That(piece.localScale.z, Is.EqualTo(LaundryRig.ClothLength * rig.PieceScale(i) * .5f).Within(.0001f));
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

        // item 4: the head bows 10 (was 22) and the hands slide to the folded cloth's far corners (EdgeSwitchSeconds) instead of snapping when FoldWidth turns into Place
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

        // ---- measured geometry tests: they drive one whole run on the seated clip, hips at (0.81, 0.52, 1.50) facing east
        static void Drive(Action<LaundryRig, Animator, GameObject, int> onFrame)
        {
            var model = LoadModel(); LaundryRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new LaundryRig(anim, 17);
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
                rig.Start();
                for (int i = 0; i < 2600; i++) { sample(); rig.Tick(.02f); rig.Apply(.02f); onFrame(rig, anim, model, i); }
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        static Vector3[] ClothVertices(LaundryRig rig, int piece)
        {
            var t = rig.Piece(piece);
            return t.GetComponent<MeshFilter>().sharedMesh.vertices.Select(t.TransformPoint).ToArray();
        }

        // thigh top above the hips -> knee line at forward distance d (the same rule the rig uses)
        static float ThighTop(Animator anim, Vector3 forward, float d, out float dk, out float latMax)
        {
            var hp = anim.GetBoneTransform(HumanBodyBones.Hips).position;
            var kl = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position; var kr = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg).position;
            var mid = (kl + kr) * .5f; var right = Vector3.Cross(Vector3.up, forward);
            dk = Mathf.Max(.25f, Vector3.Dot(mid - hp, forward));
            latMax = Mathf.Max(Mathf.Abs(Vector3.Dot(kl - hp, right)), Mathf.Abs(Vector3.Dot(kr - hp, right))) + LaundryRig.ThighRadius;
            return hp.y + Vector3.Dot(mid - hp, Vector3.up) * Mathf.Clamp01(d / dk) + LaundryRig.ThighRadius + LaundryRig.ThighGap;
        }

        // old code: the lap cloth ran from 0.05 behind the hips forward, inside the belly, so this failed
        [Test]
        public void Laundry_ClothNeverEntersTheTorsoOrThighs()
        {
            float worstFront = 9, worstThigh = 9; int checkedFrames = 0;
            Drive((rig, anim, model, i) =>
            {
                if (rig.Weight < 1 || rig.Completed >= 5) return;
                string ph = rig.Phase;
                bool onLap = ph == "FoldLength" || ph == "FoldWidth" || (ph == "Lower" && rig.PhaseProgress > .9f);
                bool moving = onLap || ph == "Lift" || ph == "Shake" || ph == "Lower";
                if (!moving) return;
                var hp = anim.GetBoneTransform(HumanBodyBones.Hips).position; var fwd = model.transform.forward; fwd.y = 0; fwd.Normalize();
                var right = Vector3.Cross(Vector3.up, fwd);
                foreach (var v in ClothVertices(rig, rig.Completed))
                {
                    float d = Vector3.Dot(v - hp, fwd), lat = Mathf.Abs(Vector3.Dot(v - hp, right));
                    if (onLap) { worstFront = Mathf.Min(worstFront, d); checkedFrames++; }
                    float top = ThighTop(anim, fwd, d, out float dk, out float latMax);
                    if (d >= .08f && d <= dk + .08f && lat <= latMax - .01f) worstThigh = Mathf.Min(worstThigh, v.y - top);
                }
            });
            Debug.Log("LAUNDRY_CLEAR in front of the hips >= " + worstFront.ToString("0.000") + " m while folding, lowest clearance over the thighs " + worstThigh.ToString("0.000") + " m");
            Assert.That(checkedFrames, Is.GreaterThan(100));
            Assert.That(worstFront, Is.GreaterThanOrEqualTo(.10f - .005f), "flat cloth stays in front of the belly");
            Assert.That(worstThigh, Is.GreaterThanOrEqualTo(-.006f), "hanging or moving cloth never sinks into the thighs (mid-thickness gap 0.01 minus half a thickness)");
        }

        // old code: FoldLength had no flap at all (a shrinking box), so the contacts could not match any flap corner
        [Test]
        public void Laundry_HandsRideTheMovingFlap()
        {
            float worstLen = 0, worstWidth = 0, worstLeft = 0; int lenFrames = 0, widthFrames = 0;
            Drive((rig, anim, model, i) =>
            {
                if (rig.Weight < 1 || rig.Completed >= 5) return;
                if (rig.Phase != "FoldLength" && rig.Phase != "FoldWidth") return;
                var v = ClothVertices(rig, rig.Completed);
                if (rig.Phase == "FoldLength")
                {
                    lenFrames++;
                    worstLen = Mathf.Max(worstLen, Vector3.Distance(v[6 * 4], rig.LeftContact), Vector3.Distance(v[6 * 4 + 1], rig.RightContact)); // far-end ring, top corners
                }
                else
                {
                    widthFrames++;
                    worstWidth = Mathf.Max(worstWidth, Vector3.Distance(v[6 * 4 + 1], rig.RightContact)); // near-right corner of the flap
                    // the left hand holds the left edge (ring 0): distance from the contact to the segment of that edge
                    var a = v[1]; var b = v[0]; var ab = b - a;
                    float k = Mathf.Clamp01(Vector3.Dot(rig.LeftContact - a, ab) / ab.sqrMagnitude);
                    worstLeft = Mathf.Max(worstLeft, Vector3.Distance(a + ab * k, rig.LeftContact));
                }
            });
            Debug.Log("LAUNDRY_FLAP worst hand-to-corner FoldLength " + worstLen.ToString("0.0000") + " m, FoldWidth right " + worstWidth.ToString("0.0000") + " m, left to its edge " + worstLeft.ToString("0.0000") + " m");
            Assert.That(lenFrames, Is.GreaterThan(100)); Assert.That(widthFrames, Is.GreaterThan(100));
            Assert.That(worstLen, Is.LessThan(.01f)); Assert.That(worstWidth, Is.LessThan(.01f)); Assert.That(worstLeft, Is.LessThan(.01f));
        }

        // old code: FoldLength started with the hands at the near edge and FoldWidth at the far edge, a jump of about 0.2 m
        [Test]
        public void Laundry_NoContactJumpAtPhaseBoundaries()
        {
            string previous = ""; Vector3 lastL = default, lastR = default; float worst = 0; int boundaries = 0; string worstAt = "";
            Drive((rig, anim, model, i) =>
            {
                if (rig.Weight >= 1 && previous != "" && rig.Phase != previous)
                {
                    boundaries++;
                    float jump = Mathf.Max(Vector3.Distance(lastL, rig.LeftContact), Vector3.Distance(lastR, rig.RightContact));
                    if (jump > worst) { worst = jump; worstAt = previous + "->" + rig.Phase; }
                }
                lastL = rig.LeftContact; lastR = rig.RightContact; previous = rig.Weight >= 1 ? rig.Phase : "";
            });
            Debug.Log("LAUNDRY_BOUNDARY " + boundaries + " boundaries, worst contact step " + worst.ToString("0.000") + " m at " + worstAt);
            Assert.That(boundaries, Is.GreaterThanOrEqualTo(5 * 8));
            Assert.That(worst, Is.LessThan(.03f), worstAt);
        }

        [Test]
        public void Laundry_ElbowsStayInFrontAndWristsReach()
        {
            float worstElbow = 9, worstReach = 0; string reachAt = ""; var perPhase = new Dictionary<string, float>(); string elbowAt = "";
            Drive((rig, anim, model, i) =>
            {
                if (rig.Weight < 1) return;
                var fwd = model.transform.forward; fwd.y = 0; fwd.Normalize();
                for (int s = 0; s < 2; s++)
                {
                    var up = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
                    var lo = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
                    if (Vector3.Dot(lo - up, fwd) < worstElbow) { worstElbow = Vector3.Dot(lo - up, fwd); elbowAt = rig.Phase; }
                    float miss = Vector3.Distance(anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position, s == 0 ? rig.LeftWristTarget : rig.RightWristTarget);
                    perPhase[rig.Phase] = Mathf.Max(perPhase.TryGetValue(rig.Phase, out float pm) ? pm : 0, miss);
                    if (miss > worstReach) { worstReach = miss; reachAt = rig.Phase + " piece " + rig.Completed; }
                }
            });
            Debug.Log("LAUNDRY_PHASES wrist miss per phase " + string.Join(", ", perPhase.Select(kv => kv.Key + " " + kv.Value.ToString("0.000"))) + "; worst elbow in " + elbowAt);
            Debug.Log("LAUNDRY_ARMS elbow never more than " + (-worstElbow).ToString("0.000") + " m behind the shoulder plane, worst wrist miss " + worstReach.ToString("0.000") + " m at " + reachAt);
            Assert.That(worstElbow, Is.GreaterThanOrEqualTo(-.05f));
            Assert.That(worstReach, Is.LessThan(.04f), reachAt);
        }
    }
}
#endif
