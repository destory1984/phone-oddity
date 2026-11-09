using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    public class CurtainTests
    {
        static GameObject Model()
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
            model.transform.SetPositionAndRotation(new Vector3(1.5f, 0, .40f), Quaternion.Euler(0, 180, 0));
            return model;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Curtain_HandsFollowBothEdgesDuringPullAndLegsStayInBasePose(bool open)
        {
            var model = Model();
            using (var panel = new CurtainPanel())
            using (var rig = new CurtainRig(model.GetComponent<Animator>(), 10))
            try
            {
                panel.Openness = open ? 0 : 1;
                rig.Curtain = panel; rig.Open = open;
                var bones = model.GetComponentsInChildren<Transform>();
                var rotations = bones.Select(t => t.localRotation).ToArray();
                var positions = bones.Select(t => t.localPosition).ToArray();
                var anim = model.GetComponent<Animator>();
                var legs = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                var legRotations = legs.Select(b => anim.GetBoneTransform(b).localRotation).ToArray();
                int samples = 0;
                rig.Start();
                for (int i = 0; i < 480; i++)
                {
                    for (int j = 0; j < bones.Length; j++) bones[j].localRotation = rotations[j];
                    rig.Tick(1f / 60); rig.Apply(1f / 60);
                    if (rig.Phase == "Pull" || rig.Phase == "Hold")
                    {
                        samples++;
                        for (int side = 0; side < 2; side++)
                            Assert.That(Vector3.Distance(rig.HandContact(side), rig.GripPoint(side)), Is.LessThan(.025f),
                                "actual palm must stay on the moving edge, side " + side);
                    }
                    for (int j = 0; j < legs.Length; j++)
                        Assert.That(Quaternion.Angle(anim.GetBoneTransform(legs[j]).localRotation, legRotations[j]), Is.LessThan(.001f));
                    for (int j = 0; j < bones.Length; j++)
                        Assert.That(Vector3.Distance(bones[j].localPosition, positions[j]), Is.LessThan(.00001f));
                }
                Assert.That(samples, Is.GreaterThan(60));
                Assert.That(panel.Openness, Is.EqualTo(open ? 1 : 0).Within(.00001f));
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void Curtain_TickRuns120SecondsWithoutBoneOrPropWritesAndFinishesBothVariants()
        {
            var model = Model();
            using (var panel = new CurtainPanel())
            using (var rig = new CurtainRig(model.GetComponent<Animator>(), 123))
            try
            {
                rig.Curtain = panel;
                var bones = model.GetComponentsInChildren<Transform>();
                var rotations = bones.Select(t => t.localRotation).ToArray();
                var panelTransforms = panel.Root.GetComponentsInChildren<Transform>();
                var panelPositions = panelTransforms.Select(t => t.localPosition).ToArray();
                for (int pass = 0; pass < 2; pass++)
                {
                    rig.Open = pass == 1; rig.Start();
                    Assert.That(rig.LookSeconds, Is.InRange(1.5f, 2.5f));
                    bool pulled = false, held = false, turned = false;
                    for (int i = 0; i < 3600; i++)
                    {
                        rig.Tick(1f / 60);
                        pulled |= rig.Phase == "Pull"; held |= rig.Phase == "Hold"; turned |= rig.Phase == "TurnAway";
                        Assert.That(rig.Weight, Is.InRange(0f, 1f));
                        Assert.That(rig.Smile, Is.InRange(0f, 1f));
                        Assert.That(rig.EyesClosed, Is.InRange(0f, 1f));
                        Assert.That(rig.MouthOpen, Is.InRange(0f, 1f));
                    }
                    Assert.IsTrue(pulled && held && turned);
                    Assert.IsFalse(rig.Active); Assert.AreEqual("Idle", rig.Phase);
                    Assert.AreEqual(0, rig.Weight);
                    Assert.That(panel.Openness, Is.EqualTo(pass == 1 ? 1 : 0).Within(.00001f));
                }
                for (int j = 0; j < bones.Length; j++) Assert.That(Quaternion.Angle(bones[j].localRotation, rotations[j]), Is.LessThan(.001f));
                for (int j = 0; j < panelTransforms.Length; j++) Assert.AreEqual(panelPositions[j], panelTransforms[j].localPosition);
                rig.Start(); rig.Tick(.2f); Assert.That(rig.Weight, Is.EqualTo(.5f).Within(.001f));
                rig.Stop(); rig.Tick(.4f); Assert.IsFalse(rig.Active);
                rig.Start(); rig.Tick(120); Assert.IsFalse(rig.Active);
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void Curtain_PanelsMeetAndOpenSymmetricallyWithoutLeakingPrimitives()
        {
            Transform root;
            using (var panel = new CurtainPanel())
            {
                root = panel.Root;
                panel.Openness = 0; panel.Apply();
                Assert.That(Vector3.Distance(panel.Edge(0, 1), panel.Edge(1, 1)), Is.LessThan(.001f));
                panel.Openness = 1; panel.Apply();
                Assert.That(panel.Edge(1, 1).x - panel.Edge(0, 1).x, Is.EqualTo(CurtainPanel.OpenGap).Within(.001f));
                Assert.That(root.GetComponentsInChildren<Collider>().Length, Is.EqualTo(0));
            }
            Assert.IsTrue(root == null);
        }
    }
}
