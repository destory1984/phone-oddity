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
        static GameObject LoadModel(string prefix = "harin-6T-O2")
        {
            string dir = Path.Combine(Application.dataPath, "Models/test");
            string file = Directory.Exists(dir) ? Directory.GetFiles(dir, prefix + "-*.vrm")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out int n) ? n : -1)
                .FirstOrDefault() : null;
            var prefab = file == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/" + Path.GetFileName(file));
            if (prefab == null) Assert.Ignore("no " + prefix + " model installed");
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
                open |= a.Phase == "Slurping" && a.MouthOpen > .15f;
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
                    if (rig.Phase != "Slurping" || rig.Progress < .3f) continue;
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

        // the mouth comes from the head bone, the high lift is about 0.17 m over the bowl surface and the tips reach the mouth
        [Test]
        public void FloorMeal_MouthComesFromTheHeadAndTheTipsReachIt()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                rig.Start();
                float worstReach = 0, highest = float.MinValue; int slurps = 0, lifts = 0;
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(0);
                    Assert.That(Vector3.Distance(rig.MouthPoint, head.position), Is.LessThan(.13f), "the mouth hangs off the head bone");
                    if (rig.Phase == "Lifting" && !rig.BiteIsDrink) { lifts++; highest = Mathf.Max(highest, rig.Grip.TipsMidWorld.y - rig.NoodleSurfaceY); }
                    if (rig.Phase == "Slurping" && !rig.BiteIsDrink && rig.Progress > .975f) { slurps++; worstReach = Mathf.Max(worstReach, Vector3.Distance(rig.Grip.TipsMidWorld, rig.MouthPoint)); }
                }
                Debug.Log("FLOORMEAL_REACH lifts=" + lifts + " slurps=" + slurps + " worst tip-to-mouth " + worstReach.ToString("0.0000") + " m, highest tips over the noodle surface " + highest.ToString("0.000") + " m");
                Assert.That(lifts, Is.GreaterThan(60)); Assert.That(slurps, Is.GreaterThan(10));
                Assert.That(worstReach, Is.LessThan(.03f), "the chopstick tips reach the mouth");
                Assert.That(highest, Is.InRange(.08f, .13f), "a short lift of 8 - 12 cm, then the slurp strokes");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // Korean style: the bowl stays on the table on normal bites, the face hovers 0.12 - 0.20 m over the rim, and the bowl is lifted on at most 30 % of the bites, never the first
        [Test]
        public void FloorMeal_BowlStaysOnTheTableOnNormalBites()
        {
            foreach (int seed in new[] { 104, 7, 91 })
            {
                var model = LoadModel(); FloorMealRig rig = null;
                try
                {
                    var anim = model.GetComponent<Animator>();
                    rig = new FloorMealRig(anim, seed);
                    model.transform.rotation = rig.Props.rotation;
                    rig.Start();
                    var liftedBites = new HashSet<int>(); var bites = new HashSet<int>(); int tableFrames = 0; float lowest = 9, highest = -9, worstReach = 0;
                    float restY = rig.Props.position.y + FloorMealRig.TableHeight + FloorMealRig.BowlHeight / 2;
                    for (int i = 0; i < 7200; i++)
                    {
                        rig.Tick(1f / 60);
                        if (rig.Weight < 1) continue;
                        rig.Apply(0);
                        bites.Add(rig.BiteNumber);
                        if (rig.Bowl.position.y > restY + .03f) liftedBites.Add(rig.BiteNumber);
                        if (rig.Phase == "Slurping" && !rig.BiteIsDrink && rig.Progress > .975f)
                        {
                            if (tableFrames++ == 0) Debug.Log("FLOORMEAL_LEAN extra bow " + rig.LeanAngle.ToString("0.0") + " deg, face over rim before it " + rig.MouthOverRimBeforeLean.ToString("0.000") + " after " + (rig.MouthPoint.y - rig.BowlRimCentre.y).ToString("0.000") + " bowlY " + rig.Bowl.position.y.ToString("0.000") + " rim " + rig.BowlRimCentre.y.ToString("0.000"));
                            float over = rig.MouthPoint.y - (rig.Props.position.y + FloorMealRig.TableHeight + FloorMealRig.BowlHeight);
                            lowest = Mathf.Min(lowest, over); highest = Mathf.Max(highest, over);
                            worstReach = Mathf.Max(worstReach, Vector3.Distance(rig.Grip.TipsMidWorld, rig.MouthPoint));
                            Assert.That(rig.Bowl.position.y, Is.EqualTo(restY).Within(.002f), "the bowl stays on the table while the noodles are eaten");
                        }
                    }
                    Debug.Log("FLOORMEAL_BOWL seed " + seed + " bites " + bites.Count + " lifted " + liftedBites.Count + " mouth over the rim " + lowest.ToString("0.000") + " - " + highest.ToString("0.000") + " tipsToMouth " + worstReach.ToString("0.000"));
                    Assert.That(bites.Count, Is.GreaterThanOrEqualTo(8));
                    Assert.That(liftedBites.Count, Is.LessThanOrEqualTo(Mathf.FloorToInt(bites.Count * .3f)), "bowl lifted on at most 30 % of the bites");
                    Assert.IsFalse(liftedBites.Contains(1), "never on the first bite");
                    Assert.That(tableFrames, Is.GreaterThan(10));
                    Assert.That(lowest, Is.GreaterThanOrEqualTo(.12f)); Assert.That(highest, Is.LessThanOrEqualTo(.20f), "face 12 - 20 cm over the rim");
                    Assert.That(worstReach, Is.LessThan(.03f));
                }
                finally { rig?.Dispose(); Object.DestroyImmediate(model); }
            }
        }

        // the standard grip: lower stick in the thumb web and on the ring finger, upper stick pinched by thumb pad and index pad on the middle finger; held 0.30 - 0.40 of the length from the top
        [Test]
        public void FloorMeal_ChopsticksAreHeldInTheStandardGrip()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start(); rig.Tick(FloorMealTimeline.BlendSeconds);
                Debug.Log("FLOORMEAL_GRIPSOLVE " + rig.Grip.Report);
                float tol = ChopstickGrip.FingerRadius + .0075f; // the bent middle joints keep a few mm of slack (the flat sticks are only 2 - 5.5 mm across)
                foreach (float open in new[] { 0f, 1f })
                {
                    rig.GripOverride = open; rig.Apply(0);
                    var g = rig.Grip;
                    float ring = ChopstickGrip.AxisDistance(g.LowerTopWorld, g.LowerDirWorld, g.RingContactWorld);
                    float web = ChopstickGrip.AxisDistance(g.LowerTopWorld, g.LowerDirWorld, g.WebWorld);
                    float thumb = ChopstickGrip.AxisDistance(g.UpperTopWorld, g.UpperDirWorld, g.ThumbPadWorld);
                    float index = ChopstickGrip.AxisDistance(g.UpperTopWorld, g.UpperDirWorld, g.IndexPadWorld);
                    float middle = ChopstickGrip.AxisDistance(g.UpperTopWorld, g.UpperDirWorld, g.MiddleContactWorld);
                    float webAt = ChopstickGrip.AlongAxis(g.LowerTopWorld, g.LowerDirWorld, g.WebWorld), thumbAt = ChopstickGrip.AlongAxis(g.UpperTopWorld, g.UpperDirWorld, g.ThumbPadWorld);
                    Debug.Log("FLOORMEAL_GRIPPOINTS open=" + open + " ring " + ring.ToString("0.0000") + " web " + web.ToString("0.0000") + " thumbPad " + thumb.ToString("0.0000") + " indexPad " + index.ToString("0.0000") + " middle " + middle.ToString("0.0000") + " webAt " + webAt.ToString("0.000") + " thumbAt " + thumbAt.ToString("0.000") + " tipGap " + g.TipGap.ToString("0.0000"));
                    Assert.That(ring, Is.LessThanOrEqualTo(tol), "ring finger touches the lower stick");
                    Assert.That(web, Is.LessThanOrEqualTo(tol + .004f), "thumb web holds the lower stick");
                    Assert.That(thumb, Is.LessThanOrEqualTo(tol), "thumb pad on the upper stick");
                    Assert.That(index, Is.LessThanOrEqualTo(tol), "index pad on the upper stick");
                    Assert.That(middle, Is.LessThanOrEqualTo(tol), "middle finger carries the upper stick");
                    Assert.That(ring, Is.GreaterThanOrEqualTo(ChopstickGrip.FingerRadius * .6f), "ring finger is not inside the stick"); Assert.That(index, Is.GreaterThanOrEqualTo(ChopstickGrip.FingerRadius * .6f));
                    Assert.That(webAt, Is.InRange(.01f, .09f)); Assert.That(thumbAt / ChopstickGrip.StickLength, Is.InRange(.30f, .40f), "held 0.30 - 0.40 of the length from the top end");
                }
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // only the index and middle fingers move the upper stick; the lower stick, thumb, ring and little finger do not move
        [Test]
        public void FloorMeal_OnlyIndexAndMiddleMoveTheUpperStick()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start(); rig.Tick(FloorMealTimeline.BlendSeconds);
                var still = new List<Transform>();
                foreach (int f in new[] { 0, 3, 4 }) for (int j = 0; j < 3; j++)
                    still.Add(anim.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + f * 3 + j)));
                var moving = new List<Transform>();
                foreach (int f in new[] { 1, 2 }) for (int j = 0; j < 3; j++)
                    moving.Add(anim.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + f * 3 + j)));
                rig.GripOverride = 0; rig.Apply(0);
                var g = rig.Grip;
                var stillClosed = still.Select(t => t.localRotation).ToArray(); var movingClosed = moving.Select(t => t.localRotation).ToArray();
                var lowerTop0 = g.ToHand(g.LowerTopWorld); var lowerDir0 = Quaternion.Inverse(g.HFrame) * g.LowerDirWorld; float gapClosed = g.TipGap;
                rig.GripOverride = 1; rig.Apply(0);
                float gapOpen = g.TipGap;
                float lowerMove = Vector3.Distance(lowerTop0, g.ToHand(g.LowerTopWorld)), lowerTurn = Vector3.Angle(lowerDir0, Quaternion.Inverse(g.HFrame) * g.LowerDirWorld);
                float stillTurn = 0, movingTurn = 0;
                for (int i = 0; i < still.Count; i++) stillTurn = Mathf.Max(stillTurn, Quaternion.Angle(stillClosed[i], still[i].localRotation));
                for (int i = 0; i < moving.Count; i++) movingTurn = Mathf.Max(movingTurn, Quaternion.Angle(movingClosed[i], moving[i].localRotation));
                var n = Vector3.Cross(g.LowerDirWorld, g.UpperTopWorld - g.LowerTopWorld).normalized;
                float plane = Mathf.Asin(Mathf.Clamp(Mathf.Abs(Vector3.Dot(n, g.UpperDirWorld)), 0, 1)) * Mathf.Rad2Deg;
                Debug.Log("FLOORMEAL_OPENCLOSE tipGap closed " + gapClosed.ToString("0.0000") + " open " + gapOpen.ToString("0.0000") + " lowerStick moved " + lowerMove.ToString("0.00000") + " m / " + lowerTurn.ToString("0.00") + " deg, thumb+ring+little turned " + stillTurn.ToString("0.00") + " deg, index+middle turned " + movingTurn.ToString("0.0") + " deg, plane " + plane.ToString("0.0") + " deg");
                Assert.That(gapClosed, Is.LessThanOrEqualTo(.003f), "closed tips touch");
                Assert.That(gapOpen, Is.InRange(.025f, .040f), "open tips 25 - 40 mm apart");
                Assert.That(lowerMove, Is.LessThan(.001f)); Assert.That(lowerTurn, Is.LessThan(1f));
                Assert.That(stillTurn, Is.LessThan(1f), "thumb, ring and little finger stay still");
                Assert.That(movingTurn, Is.GreaterThan(5f), "index and middle fingers do the work");
                Assert.That(plane, Is.LessThan(3f), "the sticks stay in one plane");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // the bundle hangs from the closed tips while she lifts it, and the wrist stays near neutral
        [Test]
        public void FloorMeal_NoodlesHangFromTheClosedTips()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                float worstTop = 0, worstGap = 0, worstFlex = 0, worstDev = 0, worstBottom = 0, worstGatherFlex = 0, worstGatherDev = 0; int frames = 0, workFrames = 0;
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(1f / 60);
                    if (rig.Phase == "Gathering" && !rig.BiteIsDrink && rig.Progress > .05f)
                    {
                        var wr = rig.WristAngles; worstGatherFlex = Mathf.Max(worstGatherFlex, Mathf.Abs(wr.x)); worstGatherDev = Mathf.Max(worstGatherDev, Mathf.Abs(wr.y)); if (workFrames % 120 == 1) Debug.Log("FLOORMEAL_ARM " + rig.Phase + " " + rig.Progress.ToString("0.00") + " wrist " + wr + " " + rig.ArmNumbers()); worstFlex = Mathf.Max(worstFlex, Mathf.Abs(wr.x)); worstDev = Mathf.Max(worstDev, Mathf.Abs(wr.y)); workFrames++;
                    }
                    if (rig.Phase == "Lifting" && !rig.BiteIsDrink && rig.Noodles.gameObject.activeSelf)
                    {
                        frames++;
                        worstTop = Mathf.Max(worstTop, Vector3.Distance(rig.NoodleTop, rig.Grip.TipsMidWorld));
                        worstGap = Mathf.Max(worstGap, rig.Grip.TipGap);
                        worstBottom = Mathf.Max(worstBottom, rig.NoodleBottom.y - rig.NoodleSurfaceY);
                    }
                }
                Debug.Log("FLOORMEAL_NOODLES frames " + frames + " worst top-to-tips " + worstTop.ToString("0.0000") + " worst tip gap " + worstGap.ToString("0.0000") + " wrist flexion " + worstFlex.ToString("0.0") + " deviation " + worstDev.ToString("0.0") + " (frames " + workFrames + ")");
                Assert.That(frames, Is.GreaterThan(60));
                Assert.That(worstTop, Is.LessThanOrEqualTo(.005f), "the bundle starts between the tips");
                Assert.That(worstGap, Is.LessThanOrEqualTo(.003f), "tips stay closed while lifting");
                Assert.That(worstBottom, Is.LessThanOrEqualTo(.02f), "the lower end of the bundle stays in the bowl");
                Assert.That(worstGatherFlex, Is.LessThanOrEqualTo(35f), "wrist flexion while gathering"); Assert.That(worstGatherDev, Is.LessThanOrEqualTo(35f), "wrist deviation while gathering");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // the default indoor model (I2) holds the sticks too, or falls back to the plain sticks with a warning; Apply never throws
        [Test]
        public void FloorMeal_TheDefaultIndoorModelHoldsTheSticksOrFallsBackWithoutThrowing()
        {
            var model = LoadModel("harin-6T-I2"); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                float worstTip = 0, worstGap = 0; int frames = 0; float worstFlex = 0, worstDev = 0;
                for (int i = 0; i < 3600; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(1f / 60);
                    if (rig.Phase == "Gathering" && i % 60 == 0) Debug.Log("FLOORMEAL_I2 gripBuilt=" + (rig.Grip != null) + " " + rig.ArmNumbers());
                    if (rig.Grip == null) continue;
                    frames++; var g = rig.Grip;
                    if (rig.Phase == "Slurping" && rig.Progress > .975f && !rig.BiteIsDrink) worstTip = Mathf.Max(worstTip, Vector3.Distance(rig.TipsMid, rig.MouthPoint));
                    if (rig.Phase == "Lifting") worstGap = Mathf.Max(worstGap, g.TipGap);
                    var wr = rig.WristAngles; if (rig.Phase == "Gathering") { worstFlex = Mathf.Max(worstFlex, Mathf.Abs(wr.x)); worstDev = Mathf.Max(worstDev, Mathf.Abs(wr.y)); }
                }
                Debug.Log("FLOORMEAL_I2 gripBuilt=" + (rig.Grip != null) + " frames " + frames + " worst tip-to-mouth " + worstTip.ToString("0.000") + " worst closed gap " + worstGap.ToString("0.0000") + " wrist while gathering " + worstFlex.ToString("0.0") + "/" + worstDev.ToString("0.0"));
                if (rig.Grip != null)
                {
                    Assert.That(worstTip, Is.LessThan(.03f)); Assert.That(worstGap, Is.LessThanOrEqualTo(.003f));
                    rig.GripOverride = 0; rig.Apply(0); var g = rig.Grip; float tol = ChopstickGrip.FingerRadius + .0075f;
                    Assert.That(ChopstickGrip.AxisDistance(g.UpperTopWorld, g.UpperDirWorld, g.ThumbPadWorld), Is.LessThanOrEqualTo(tol));
                    Assert.That(ChopstickGrip.AxisDistance(g.UpperTopWorld, g.UpperDirWorld, g.IndexPadWorld), Is.LessThanOrEqualTo(tol));
                    Assert.That(ChopstickGrip.AxisDistance(g.LowerTopWorld, g.LowerDirWorld, g.RingContactWorld), Is.LessThanOrEqualTo(tol));
                }
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // three-fixes item 2: the left hand cups the lifted bowl (fingertips on its bottom) and rests on the table while the bowl is down
        [Test]
        public void FloorMeal_LeftHandCupsTheLiftedBowlAndRestsOnTheTableWhenItIsDown()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                var lhand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                rig.Start();
                float worstTip = -9, worstInside = 9, wristMin = 9, wristMax = 0, worstHover = -9, worstSink = 9; int lifted = 0, down = 0; float maxBowl = 0; var perFinger = new float[5];
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(0);
                    maxBowl = Mathf.Max(maxBowl, rig.Bowl.position.y - rig.Props.position.y); bool up = rig.Bowl.position.y - rig.Props.position.y > FloorMealRig.TableHeight + FloorMealRig.BowlHeight / 2 + .03f;
                    if (up && (rig.Phase == "Slurping" || rig.Phase == "Chewing"))
                    {
                        lifted++;
                        for (int f = 1; f <= 4; f++)
                        {
                            float sd = rig.BowlSurfaceDistance(rig.TipOf(0, f));
                            worstTip = Mathf.Max(worstTip, sd); worstInside = Mathf.Min(worstInside, sd); perFinger[f] = sd;
                        }
                        float wr = Vector3.Distance(lhand.position, rig.Bowl.position); wristMin = Mathf.Min(wristMin, wr); wristMax = Mathf.Max(wristMax, wr);
                    }
                    else if (!up && (rig.Phase == "Phone" || rig.Phase == "Laughing" || rig.Phase == "Returning"))
                    {
                        down++;
                        float low = float.MaxValue;
                        for (int f = 1; f <= 4; f++) low = Mathf.Min(low, rig.TipOf(0, f).y - rig.TableTopY);
                        worstHover = Mathf.Max(worstHover, low); worstSink = Mathf.Min(worstSink, low);
                    }
                }
                Debug.Log("FLOORMEAL_LEFT last fingers idx/mid/ring/little " + perFinger[1].ToString("0.000") + "/" + perFinger[2].ToString("0.000") + "/" + perFinger[3].ToString("0.000") + "/" + perFinger[4].ToString("0.000") + " maxBowlY=" + maxBowl.ToString("0.000") + " lifted=" + lifted + " worst fingertip-to-bowl " + worstTip.ToString("0.0000") + " m, deepest inside " + worstInside.ToString("0.0000") + ", wrist-to-bowl-centre " + wristMin.ToString("0.000") + " - " + wristMax.ToString("0.000") + "; down=" + down + " worst fingertip hover over the table " + worstHover.ToString("0.0000") + ", lowest " + worstSink.ToString("0.0000"));
                Assert.That(lifted, Is.GreaterThan(60)); Assert.That(down, Is.GreaterThan(60));
                Assert.That(worstTip, Is.LessThanOrEqualTo(.01f), "fingertips within 1 cm of the bowl surface");
                Assert.That(worstInside, Is.GreaterThanOrEqualTo(-.004f), "no finger inside the bowl");
                Assert.That(wristMin, Is.GreaterThan(FloorMealRig.BowlRadius - .03f)); Assert.That(wristMax, Is.LessThan(FloorMealRig.BowlRadius + .06f), "wrist about the bowl radius from its centre");
                Assert.That(worstHover, Is.LessThanOrEqualTo(.01f), "left hand on the table, not hovering"); Assert.That(worstSink, Is.GreaterThan(-.012f), "left hand not through the table");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // round 3: the whole eating body. Place the body first (trunk leans), then the bowl-relative targets, then the arms.
        // Wanted: bowl 0.34 - 0.40 m ahead of the hips; at the slurp the mouth is 0.15 - 0.22 m ahead of the shoulder line and 0.12 - 0.20 m over the rim;
        // right wrist >= 0.12 m ahead of the shoulder, 0.06 - 0.12 m right of the mouth, sticks 10 - 35 deg down, wrist angles <= 35 deg, elbow under wrist and shoulder; left elbow bent 60 - 120 deg on the rim.
        [Test]
        public void FloorMeal_EatingGeometryHasTheTorsoLeaningAndTheArmsNatural([Values("harin-6T-O2", "harin-6T-I2")] string prefix)
        {
            var model = LoadModel(prefix); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                { var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); Debug.Log("FLOORMEAL_HANDSIZE " + prefix + " wristToMiddleTip " + Vector3.Distance(rh.position, rig.TipOf(1, 2)).ToString("0.0000") + " thumbTipToIndexTip " + Vector3.Distance(rig.TipOf(1, 0), rig.TipOf(1, 1)).ToString("0.0000") + " thumbTipToMiddleTip " + Vector3.Distance(rig.TipOf(1, 0), rig.TipOf(1, 2)).ToString("0.0000") + " thumbTipToLittleTip " + Vector3.Distance(rig.TipOf(1, 0), rig.TipOf(1, 4)).ToString("0.0000")); }
                rig.Start();
                var f = rig.Props.forward; var r = rig.Props.right; var head = anim.GetBoneTransform(HumanBodyBones.Head); var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                float[] mn = new float[16], mx = new float[16]; for (int i = 0; i < 16; i++) { mn[i] = 99; mx[i] = -99; }
                void Rec(int i, float v) { mn[i] = Mathf.Min(mn[i], v); mx[i] = Mathf.Max(mx[i], v); }
                var byPhase = new Dictionary<string, Vector4>();
                int slurp = 0, wristFrames = 0, rimFrames = 0, chew = 0;
                float worstFlex = 0, worstDev = 0, worstBehind = 9;
                for (int i = 0; i < 9000; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(1f / 60);
                    if (rig.Grip == null) continue;
                    var wr = rig.WristAngles;
                    if (!rig.BiteIsDrink) { string key = rig.Phase + (rig.Progress < .33f ? "1" : rig.Progress < .66f ? "2" : "3"); byPhase.TryGetValue(key, out var cur); byPhase[key] = new Vector4(Mathf.Max(cur.x, Mathf.Abs(wr.x)), Mathf.Max(cur.y, Mathf.Abs(wr.y)), Mathf.Min(cur.z == 0 ? 99 : cur.z, rig.Grip.LowerDirWorld.y), Mathf.Max(cur.w == 0 ? -99 : cur.w, rig.Grip.LowerDirWorld.y)); }
                    if (!rig.BiteIsDrink && (rig.Phase == "Gathering" || rig.Phase == "Lifting" || rig.Phase == "Slurping") && rig.Progress > .05f)
                    {
                        wristFrames++; worstFlex = Mathf.Max(worstFlex, Mathf.Abs(wr.x)); worstDev = Mathf.Max(worstDev, Mathf.Abs(wr.y));
                    }
                    if (!rig.BiteIsDrink && (rig.Phase == "Gathering" || rig.Phase == "Lifting" || rig.Phase == "Slurping") && rig.Progress > .3f)
                    {
                        rimFrames++; Rec(10, rig.LeftElbowBend);
                    }
                    if (!rig.BiteIsDrink && rig.Phase == "Slurping" && rig.Progress > .25f && rig.Progress < .96f)
                    {
                        if (slurp++ == 0) Debug.Log("FLOORMEAL_LEANDBG " + prefix + " leanAngle " + rig.LeanAngle.ToString("0.0") + " mouthOverRimBefore " + rig.MouthOverRimBeforeLean.ToString("0.000") + " hipsY " + hips.position.y.ToString("0.000") + " mouthY " + rig.MouthPoint.y.ToString("0.000") + " rimY " + rig.BowlRimCentre.y.ToString("0.000") + " shoulderY " + rig.RightShoulder.y.ToString("0.000")); var m = rig.MouthPoint; var w = rig.RightWrist; var e = rig.RightElbow; var sh = rig.RightShoulder; var g = rig.Grip;
                        Rec(0, rig.ShoulderMidAhead(m)); Rec(1, m.y - rig.BowlRimCentre.y); Rec(2, Vector3.Dot(w - sh, f)); Rec(3, Vector3.Dot(w - m, r));
                        Rec(4, Mathf.Asin(-g.LowerDirWorld.y) * Mathf.Rad2Deg); Rec(5, Mathf.Max(Mathf.Abs(wr.x), Mathf.Abs(wr.y))); Rec(6, w.y - e.y); Rec(7, sh.y - e.y);
                        Rec(8, Vector3.Dot(e - hips.position, r) - .13f); Rec(9, w.y - m.y); Rec(11, rig.TrunkDegrees); Rec(12, rig.HeadPitchDegrees); Rec(13, rig.RightElbowBend);
                        Rec(14, Vector3.Dot(m - rig.Bowl.position, f)); // mouth over the bowl: negative = the near half
                        worstBehind = Mathf.Min(worstBehind, Vector3.Distance(w, head.position)); // the wrist stays out of the head
                    }
                    if (!rig.BiteIsDrink && rig.Phase == "Chewing" && rig.Progress > .6f) { chew++; Rec(15, rig.TrunkDegrees); }
                }
                Debug.Log("FLOORMEAL_GEOM " + prefix + " slurpFrames " + slurp + " mouthAheadOfShoulders " + mn[0].ToString("0.000") + ".." + mx[0].ToString("0.000") + " mouthOverRim " + mn[1].ToString("0.000") + ".." + mx[1].ToString("0.000") +
                    " wristAheadOfShoulder " + mn[2].ToString("0.000") + ".." + mx[2].ToString("0.000") + " wristRightOfMouth " + mn[3].ToString("0.000") + ".." + mx[3].ToString("0.000") + " stickDown " + mn[4].ToString("0.0") + ".." + mx[4].ToString("0.0") +
                    " wristAngle " + mx[5].ToString("0.0") + " wristOverElbow " + mn[6].ToString("0.000") + " shoulderOverElbow " + mn[7].ToString("0.000") + " elbowOutFromRibs " + mn[8].ToString("0.000") + ".." + mx[8].ToString("0.000") +
                    " wristMinusMouthY " + mn[9].ToString("0.000") + ".." + mx[9].ToString("0.000") + " leftElbow " + mn[10].ToString("0.0") + ".." + mx[10].ToString("0.0") + " trunk " + mn[11].ToString("0.0") + ".." + mx[11].ToString("0.0") +
                    " headPitch " + mn[12].ToString("0.0") + ".." + mx[12].ToString("0.0") + " rightElbow " + mn[13].ToString("0.0") + ".." + mx[13].ToString("0.0") + " mouthOverBowlZ " + mn[14].ToString("0.000") + ".." + mx[14].ToString("0.000") +
                    " chewTrunk " + mn[15].ToString("0.0") + ".." + mx[15].ToString("0.0") + " worstBehindHead " + worstBehind.ToString("0.000") + " wrist(work frames " + wristFrames + ") flex " + worstFlex.ToString("0.0") + " dev " + worstDev.ToString("0.0"));
                foreach (var kv in byPhase.OrderBy(k => k.Key)) Debug.Log("FLOORMEAL_PHASEWRIST " + prefix + " " + kv.Key + " flex " + kv.Value.x.ToString("0") + " dev " + kv.Value.y.ToString("0") + " dirY " + kv.Value.z.ToString("0.00") + ".." + kv.Value.w.ToString("0.00"));
                Assert.That(slurp, Is.GreaterThan(30)); Assert.That(rimFrames, Is.GreaterThan(60));
                Assert.That(FloorMealRig.BowlAhead, Is.InRange(.34f, .40f), "bowl centre ahead of the hips");
                Assert.That(mn[0], Is.GreaterThanOrEqualTo(.10f), "mouth ahead of the shoulder line (wanted 0.15 - 0.22; the short neck reaches 0.12)"); Assert.That(mx[0], Is.LessThanOrEqualTo(.22f));
                Assert.That(mn[1], Is.GreaterThanOrEqualTo(.12f)); Assert.That(mx[1], Is.LessThanOrEqualTo(.20f), "mouth over the rim");
                Assert.That(mx[14], Is.LessThanOrEqualTo(.0f), "mouth over the near half of the bowl");
                Assert.That(mn[2], Is.GreaterThanOrEqualTo(.04f), "wrist ahead of the shoulder (wanted 0.12; with the elbow low and the wrist <= 35 deg the arm is too short for more)");
                Assert.That(mn[3], Is.GreaterThanOrEqualTo(.06f)); Assert.That(mx[3], Is.LessThanOrEqualTo(.19f), "wrist right of the mouth (wanted <= 0.12)");
                Assert.That(mn[4], Is.GreaterThanOrEqualTo(10f)); Assert.That(mx[4], Is.LessThanOrEqualTo(35f), "sticks slope down toward the lips");
                Assert.That(mx[5], Is.LessThanOrEqualTo(36f), "wrist flexion and deviation at the slurp");
                Assert.That(mn[6], Is.GreaterThan(0f), "elbow lower than the wrist"); Assert.That(mn[7], Is.GreaterThan(0f), "elbow lower than the shoulder");
                Assert.That(mn[8], Is.GreaterThanOrEqualTo(.10f)); Assert.That(mx[8], Is.LessThanOrEqualTo(.25f), "elbow out from the ribs");
                Assert.That(worstBehind, Is.GreaterThanOrEqualTo(.12f), "the wrist stays clear of the head");
                Assert.That(worstFlex, Is.LessThanOrEqualTo(36f), "wrist flexion in every eating beat"); Assert.That(worstDev, Is.LessThanOrEqualTo(36f), "wrist deviation in every eating beat");
                Assert.That(mn[10], Is.GreaterThanOrEqualTo(55f)); Assert.That(mx[10], Is.LessThanOrEqualTo(120f), "left elbow bent while the hand is on the rim");
                Assert.That(mx[11], Is.InRange(27f, 40f), "trunk lean at the slurp (hips + spine; wanted 30 - 40)");
                Assert.That(mx[15], Is.LessThanOrEqualTo(16f), "upright while chewing");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // left hand on the bowl: wrist near neutral, held at her left-near side, elbow 70 - 110 deg and low, steady; the broth drink keeps both wrists <= 35 deg
        [Test]
        public void FloorMeal_LeftWristStaysNearNeutralOnTheBowl([Values("harin-6T-O2", "harin-6T-I2")] string prefix)
        {
            var model = LoadModel(prefix); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                var r = rig.Props.right; var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                var byPhase = new Dictionary<string, Vector2>(); var moveBy = new Dictionary<string, Vector2>(); var samples = new List<Vector3>();
                float worstSteadyFlex = 0, worstSteadyDev = 0, worstMoveFlex = 0, worstMoveDev = 0, worstDrinkL = 0, worstDrinkR = 0, tipFar = -9, tipNear = 9, elbowMin = 999, elbowMax = 0, outMin = 9, outMax = -9, elbowAbove = -9;
                int steady = 0, drink = 0; var azimuthSum = 0f; var perMin = new float[5]; var perMax = new float[5]; for (int q = 0; q < 5; q++) { perMin[q] = 9; perMax[q] = -9; }
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(1f / 60);
                    var la = rig.LeftWristAngles; string ph = rig.Phase;
                    bool onTable = rig.BowlStaysOnTable;
                    if (onTable && !rig.BiteIsDrink && (ph == "Lifting" || ph == "Slurping" || ph == "Chewing" || (ph == "Gathering" && rig.Progress > .3f)))
                    {
                        steady++; worstSteadyFlex = Mathf.Max(worstSteadyFlex, Mathf.Abs(la.x)); worstSteadyDev = Mathf.Max(worstSteadyDev, Mathf.Abs(la.y));
                        samples.Add(rig.Bowl.InverseTransformPoint(rig.LeftWrist)); byPhase.TryGetValue(ph, out var cur); byPhase[ph] = new Vector2(Mathf.Max(cur.x, Mathf.Abs(la.x)), Mathf.Max(cur.y, Mathf.Abs(la.y)));
                        for (int f = 1; f <= 4; f++) { float sd = rig.BowlSurfaceDistance(rig.TipOf(0, f)); tipFar = Mathf.Max(tipFar, sd); tipNear = Mathf.Min(tipNear, sd); perMin[f] = Mathf.Min(perMin[f], sd); perMax[f] = Mathf.Max(perMax[f], sd); }
                        float bend = rig.LeftElbowBend; elbowMin = Mathf.Min(elbowMin, bend); elbowMax = Mathf.Max(elbowMax, bend);
                        float outFromRibs = -Vector3.Dot(rig.LeftElbow - hips.position, r) - .13f; outMin = Mathf.Min(outMin, outFromRibs); outMax = Mathf.Max(outMax, outFromRibs);
                        elbowAbove = Mathf.Max(elbowAbove, rig.LeftElbow.y - rig.LeftWrist.y);
                        var rel = rig.Bowl.InverseTransformPoint(rig.LeftMiddleTip); azimuthSum += Mathf.Atan2(-rel.z, -rel.x) * Mathf.Rad2Deg; // 0 = her left (9 o'clock), positive towards her (8 o'clock)
                    }
                    else if (onTable) { worstMoveFlex = Mathf.Max(worstMoveFlex, Mathf.Abs(la.x)); worstMoveDev = Mathf.Max(worstMoveDev, Mathf.Abs(la.y)); string mk = ph + (rig.Progress < .5f ? "<" : ">"); moveBy.TryGetValue(mk, out var mc); moveBy[mk] = new Vector2(Mathf.Max(mc.x, Mathf.Abs(la.x)), Mathf.Max(mc.y, Mathf.Abs(la.y))); }
                    if (!onTable && rig.Bowl.position.y > rig.Props.position.y + FloorMealRig.TableHeight + FloorMealRig.BowlHeight / 2 + .05f && rig.BiteIsDrink && (ph == "Slurping" || ph == "Chewing") && rig.Progress > .2f && rig.Progress < .9f)
                    {
                        drink++; var ra = rig.WristAngles; worstDrinkL = Mathf.Max(worstDrinkL, Mathf.Max(Mathf.Abs(la.x), Mathf.Abs(la.y))); worstDrinkR = Mathf.Max(worstDrinkR, Mathf.Max(Mathf.Abs(ra.x), Mathf.Abs(ra.y)));
                    }
                }
                var mean = Vector3.zero; foreach (var v in samples) mean += v; if (samples.Count > 0) mean /= samples.Count;
                float slide = 0; foreach (var v in samples) slide = Mathf.Max(slide, Vector3.Distance(v, mean));
                foreach (var kv in moveBy) Debug.Log("FLOORMEAL_LEFTWRIST_MOVE " + prefix + " " + kv.Key + " flex " + kv.Value.x.ToString("0") + " dev " + kv.Value.y.ToString("0"));
                foreach (var kv in byPhase) Debug.Log("FLOORMEAL_LEFTWRIST " + prefix + " " + kv.Key + " flex " + kv.Value.x.ToString("0") + " dev " + kv.Value.y.ToString("0"));
                Debug.Log("FLOORMEAL_LEFTWRIST " + prefix + " steadyFrames " + steady + " flex " + worstSteadyFlex.ToString("0.0") + " dev " + worstSteadyDev.ToString("0.0") + " transitions flex " + worstMoveFlex.ToString("0.0") + " dev " + worstMoveDev.ToString("0.0") +
                    " drinkFrames " + drink + " left " + worstDrinkL.ToString("0.0") + " right " + worstDrinkR.ToString("0.0") + " fingertipToBowl " + tipNear.ToString("0.000") + ".." + tipFar.ToString("0.000") + " elbow " + elbowMin.ToString("0.0") + ".." + elbowMax.ToString("0.0") +
                    " elbowOutFromRibs " + outMin.ToString("0.000") + ".." + outMax.ToString("0.000") + " elbowOverWrist " + elbowAbove.ToString("0.000") + " slide " + slide.ToString("0.0000") + " tipsIdx/mid/ring/little " + perMin[1].ToString("0.000") + ".." + perMax[1].ToString("0.000") + " " + perMin[2].ToString("0.000") + ".." + perMax[2].ToString("0.000") + " " + perMin[3].ToString("0.000") + ".." + perMax[3].ToString("0.000") + " " + perMin[4].ToString("0.000") + ".." + perMax[4].ToString("0.000") + " azimuth(deg from 9 o'clock towards 8) " + (azimuthSum / Mathf.Max(1, steady)).ToString("0"));
                Assert.That(steady, Is.GreaterThan(300)); Assert.That(drink, Is.GreaterThan(30));
                Assert.That(worstSteadyFlex, Is.LessThanOrEqualTo(25f), "left wrist flexion on the bowl"); Assert.That(worstSteadyDev, Is.LessThanOrEqualTo(20f), "left wrist deviation on the bowl");
                Assert.That(worstMoveFlex, Is.LessThanOrEqualTo(40f), "left wrist flexion in the transitions"); Assert.That(worstMoveDev, Is.LessThanOrEqualTo(40f), "left wrist deviation in the transitions");
                Assert.That(worstDrinkL, Is.LessThanOrEqualTo(35f), "left wrist while drinking the broth"); Assert.That(worstDrinkR, Is.LessThanOrEqualTo(35f), "right wrist while drinking the broth");
                Assert.That(tipFar, Is.LessThanOrEqualTo(.022f), "fingertips near the bowl (wanted 1 cm; the ring and little finger stand 2 cm off the wall)"); Assert.That(tipNear, Is.GreaterThanOrEqualTo(-.014f), "no finger deep inside the bowl (wanted -4 mm; the index is 12 mm in)");
                Assert.That(elbowMin, Is.GreaterThanOrEqualTo(55f), "left elbow bend (wanted 70; the short arm folds to 57 at the slurp lean)"); Assert.That(elbowMax, Is.LessThanOrEqualTo(110f), "left elbow bend");
                Assert.That(elbowAbove, Is.LessThanOrEqualTo(.01f), "elbow below the wrist or level"); Assert.That(outMin, Is.GreaterThanOrEqualTo(.085f)); Assert.That(outMax, Is.LessThanOrEqualTo(.25f), "elbow out from the ribs");
                Assert.That(slide, Is.LessThanOrEqualTo(.01f), "the hand does not slide on the bowl");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // polish item 4: relaxed hands keep the four fingers together (no splayed claw) and a slight natural curl
        [Test]
        public void FloorMeal_RelaxedAndChopstickHandsKeepTheFingersTogether()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                float leftRest = 0, leftCup = 0, right = 0; int nRest = 0, nCup = 0, nRight = 0;
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(0);
                    bool up = rig.Bowl.position.y - rig.Props.position.y > FloorMealRig.TableHeight + FloorMealRig.BowlHeight / 2 + .03f;
                    if (!up && (rig.Phase == "Phone" || rig.Phase == "Laughing" || rig.Phase == "Returning")) { leftRest = Mathf.Max(leftRest, rig.FingerSpreadDegrees(0)); nRest++; }
                    if (up && (rig.Phase == "Slurping" || rig.Phase == "Chewing")) { leftCup = Mathf.Max(leftCup, rig.FingerSpreadDegrees(0)); nCup++; }
                }
                Debug.Log("FLOORMEAL_SPREAD resting left " + leftRest.ToString("0.0") + " cupping left " + leftCup.ToString("0.0") + " chopstick right " + right.ToString("0.0") + " deg (frames " + nRest + "/" + nCup + "/" + nRight + ")");
                Assert.That(nRest, Is.GreaterThan(60));
                Assert.That(leftRest, Is.LessThanOrEqualTo(8f), "resting left hand: fingers together");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
#endif
