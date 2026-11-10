using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    public class CatPlayTests
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
        public void CatPlay_TimingAndSeedRemainValidFor120Seconds()
        {
            var a = new CatPlayTiming(91);
            var b = new CatPlayTiming(91);
            a.Start(); b.Start();
            int changes = 0;
            string previous = a.Phase;
            for (int i = 0; i < 12000; i++)
            {
                a.Mode = b.Mode = i < 6000 ? CatPlayMode.Pet : CatPlayMode.Wand;
                a.Tick(.01f); b.Tick(.01f);
                Assert.That(a.Weight, Is.InRange(0f, 1f));
                Assert.That(a.PetProgress, Is.InRange(0f, 1f));
                Assert.That(a.Smile, Is.InRange(0f, 1f));
                Assert.That(a.EyesClosed, Is.InRange(0f, 1f));
                Assert.AreEqual(a.Phase, b.Phase);
                Assert.AreEqual(a.WandOffset, b.WandOffset);
                if (a.Phase != previous) { changes++; previous = a.Phase; }
            }
            Assert.That(changes, Is.GreaterThan(50));
            a.Stop(); a.Tick(CatPlayTiming.BlendSeconds);
            Assert.IsFalse(a.Active);
            Assert.AreEqual("Off", a.Phase);
            Assert.That(a.Smile, Is.Zero);
            a.Start(); a.Tick(.2f); Assert.That(a.Weight, Is.EqualTo(.5f).Within(.0001f));
            a.Tick(120f); Assert.That(a.Weight, Is.EqualTo(1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => a.Tick(float.NaN));
        }

        [Test]
        public void CatPlay_StrokeTakesOnePointTwoSecondsThenReturnsAboveBack()
        {
            var clock = new CatPlayTiming(1);
            clock.Start(); clock.Tick(.6f);
            Assert.That(clock.PetProgress, Is.EqualTo(.5f).Within(.001f));
            Assert.AreEqual("PetStroke", clock.Phase);
            clock.Tick(.6f);
            Assert.That(clock.PetProgress, Is.EqualTo(1f).Within(.001f));
            Assert.AreEqual("PetReturn", clock.Phase);
            clock.Tick(CatPlayTiming.ReturnSeconds / 2);
            Assert.That(clock.PetLift, Is.GreaterThan(.04f));
        }

        [Test]
        public void CatPlay_DefaultModelSquatsPinsFeetPetsAndKeepsStringLength()
        {
            var model = LoadModel();
            CatPlayRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new CatPlayRig(anim, 5);
                rig.CatHead = new Vector3(.12f, .28f, .42f);
                rig.CatTail = new Vector3(-.12f, .28f, .42f);
                var beforeTick = anim.GetBoneTransform(HumanBodyBones.Hips).position;
                rig.Start(); rig.Tick(.6f);
                Assert.AreEqual(beforeTick, anim.GetBoneTransform(HumanBodyBones.Hips).position, "Tick must not write bones");
                rig.Apply(.6f);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                Assert.That(hips.position.y, Is.EqualTo(CatPlayRig.SquatHeight).Within(.005f));
                foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    Assert.That(anim.GetBoneTransform(bone).position.y,
                        Is.EqualTo(CatPlayRig.AnkleHeight).Within(.015f));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget), Is.LessThan(.04f));
                var firstHips = hips.position;
                var firstHand = anim.GetBoneTransform(HumanBodyBones.RightHand).position;
                rig.Apply(0); // No Animator resample: corrections must not accumulate.
                Assert.That(Vector3.Distance(firstHips, hips.position), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(firstHand, anim.GetBoneTransform(HumanBodyBones.RightHand).position), Is.LessThan(.0001f));
                rig.Mode = CatPlayMode.Wand;
                for (int i = 0; i < 600; i++)
                {
                    rig.Tick(.02f); rig.Apply(.02f);
                    Assert.That(Vector3.Distance(rig.WandTipWorld, rig.FeatherTipWorld),
                        Is.EqualTo(CatPlayRig.StringLength).Within(.001f));
                    Assert.IsFalse(float.IsNaN(rig.FeatherTipWorld.x));
                }
                Assert.That(Vector3.Distance(rig.WandBaseWorld, rig.WandTipWorld), Is.EqualTo(.6f).Within(.001f));
                var stick = rig.Wand.Find("stick");
                Assert.That(Vector3.Distance(stick.TransformPoint(Vector3.up), rig.WandTipWorld), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(stick.TransformPoint(Vector3.down), rig.WandBaseWorld), Is.LessThan(.001f));
                rig.Stop(); rig.Tick(.4f); rig.Apply(0);
                Assert.IsFalse(rig.Wand.gameObject.activeSelf);
                Assert.IsFalse(rig.Active);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // item 3: the bend is split over hip joint, spine and chest (total 57, was 86 on the waist), the hips sit behind the root, the palm stays on the cat
        [Test]
        public void CatPlay_BendIsSplitAcrossHipSpineChestHipsSitBackAndPalmStaysOnTheBack()
        {
            var model = LoadModel(); CatPlayRig rig = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                rig = new CatPlayRig(anim, 5);
                rig.CatHead = new Vector3(.12f, .28f, .42f); rig.CatTail = new Vector3(-.12f, .28f, .42f);
                Assert.That(rig.TorsoLeanDegrees, Is.LessThanOrEqualTo(60f), "hip + spine + chest bend sum");
                rig.Start(); rig.Tick(.6f); rig.Apply(.6f);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                Assert.That(Vector3.Dot(hips.position - anim.transform.position, anim.transform.forward), Is.LessThan(-.05f), "the hips are pulled back behind the root");
                float worstGap = 0;
                for (int i = 0; i < 300; i++)
                {
                    rig.Tick(.02f); rig.Apply(.02f);
                    worstGap = Mathf.Max(worstGap, Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightWristTarget));
                    Assert.That(rig.LastHeadDown, Is.LessThanOrEqualTo(45.05f));
                }
                Debug.Log("CATPLAY_PALM worst hand-to-target gap " + worstGap.ToString("0.000") + " m");
                Assert.That(worstGap, Is.LessThan(.04f), "the palm stays on the cat's back");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
