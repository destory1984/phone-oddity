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

        // item 5: the mouth comes from the head bone, the chopsticks sit at one fixed spot of the hand (error under 5 mm), the high lift is 0.15 m over the mouth
        [Test]
        public void FloorMeal_ChopsticksStayInTheHandMouthComesFromTheHeadAndTheHighLiftIs15cm()
        {
            var model = LoadModel(); FloorMealRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                rig.Start();
                float worstGrip = 0, worstAngle = 0, highest = float.MinValue; int frames = 0, lifts = 0;
                for (int i = 0; i < 7200; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(0);
                    frames++;
                    worstGrip = Mathf.Max(worstGrip, rig.StickDistanceFromGrip);
                    worstAngle = Mathf.Max(worstAngle, rig.StickAngleFromFingers);
                    Assert.That(Vector3.Distance(rig.MouthPoint, head.position), Is.LessThan(.13f), "the mouth hangs off the head bone");
                    if (rig.Phase == "Lifting") { lifts++; highest = Mathf.Max(highest, rig.RightWristTarget.y - rig.MouthPoint.y); }
                }
                Debug.Log("FLOORMEAL_GRIP frames=" + frames + " worst stick-to-hand " + worstGrip.ToString("0.0000") + " m, worst stick-vs-fingers angle " + worstAngle.ToString("0.0") + " deg, highest wrist over mouth " + highest.ToString("0.000") + " m");
                Assert.That(lifts, Is.GreaterThan(60));
                Assert.That(worstGrip, Is.LessThan(.005f), "chopstick grip error under 5 mm");
                Assert.That(highest, Is.LessThanOrEqualTo(.16f), "the high lift is 0.15 m over the mouth, not 0.26");
                Assert.That(worstAngle, Is.LessThan(40f), "the fingers point roughly along the sticks");
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
                    if (rig.Phase == "Gathering" || rig.Phase == "Lifting") { right = Mathf.Max(right, rig.FingerSpreadDegrees(1)); nRight++; }
                }
                Debug.Log("FLOORMEAL_SPREAD resting left " + leftRest.ToString("0.0") + " cupping left " + leftCup.ToString("0.0") + " chopstick right " + right.ToString("0.0") + " deg (frames " + nRest + "/" + nCup + "/" + nRight + ")");
                Assert.That(nRest, Is.GreaterThan(60)); Assert.That(nRight, Is.GreaterThan(60));
                Assert.That(leftRest, Is.LessThanOrEqualTo(8f), "resting left hand: fingers together");
                Assert.That(right, Is.LessThanOrEqualTo(10f), "chopstick hand: fingers together");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
#endif
