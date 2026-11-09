using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    // P (phone on the sofa), T (TV), B (phone on the bed): the phone between both hands, the TV on the wall facing the sofa, the bed data
    public class SofaMotionTests
    {
        static string NewestModel(string line = "O2")
        {
            var dir = Path.Combine(Application.dataPath, "Models/test");
            if (!Directory.Exists(dir)) return null;
            var f = Directory.GetFiles(dir, "harin-6T-" + line + "-*.vrm").OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out var n) ? n : -1).FirstOrDefault();
            return f == null ? null : "Assets/Models/test/" + Path.GetFileName(f);
        }

        static (GameObject model, SofaRig rig) Make(string line = "O2")
        {
            var path = NewestModel(line);
            var prefab = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Assert.Ignore("no 6T-O2 model installed");
            var model = Object.Instantiate(prefab);
            var a = model.GetComponent<Animator>();
            if (a == null || !a.isHuman) { Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); }
            var fingers = new Vector3[2]; var palm = new Vector3[2];
            for (int s = 0; s < 2; s++) // the T-pose: palms down, fingers along the forearm (what RoomPlay measures before any clip plays)
            {
                var hb = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); var lb = a.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                fingers[s] = Quaternion.Inverse(hb.rotation) * (hb.position - lb.position).normalized; palm[s] = Quaternion.Inverse(hb.rotation) * Vector3.down;
            }
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            return (model, new SofaRig(a, fingers, palm, Quaternion.Inverse(head.rotation) * a.transform.forward));
        }

        [TestCase("O2")]
        [TestCase("N1")]
        [TestCase("I1")]
        public void SofaMotions_PhoneSitsBetweenBothHandsInTheDefaultModel(string line)
        {
            var (model, rig) = Make(line);
            try
            {
                rig.SetImmediate(SofaMode.Phone); rig.Tick(.01f); rig.Apply(0);
                Assert.IsTrue(rig.Phone.gameObject.activeSelf, "the phone is shown in P");
                Assert.That(rig.LastHandLeft, Is.LessThan(.08f), "left palm to the phone");
                Assert.That(rig.LastHandRight, Is.LessThan(.08f), "right palm to the phone");
                var a = model.GetComponent<Animator>();
                foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    Assert.That(Vector3.Distance(a.GetBoneTransform(b).position, rig.Phone.position), Is.LessThan(.11f), b + " wrist to the phone");
                Assert.That(rig.LastHeadPitch, Is.InRange(20f, 32f), "the head is bowed 25 - 30 degrees (less while she lifts it for a breath)");
                rig.Clear(); Assert.IsFalse(rig.Phone.gameObject.activeSelf, "put away");
            }
            finally { rig.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void SofaMotions_TvLookTargetIsTheScreenCentre()
        {
            var (model, rig) = Make();
            try
            {
                var tv = new Vector3(OneRoom.Layout.tvAt.x + OneRoom.Layout.tvSize.z / 2 + .0006f, 1.25f, 1.45f);
                rig.TvCentre = tv; rig.SetImmediate(SofaMode.Tv); rig.Tick(.01f); rig.Apply(0);
                Assert.That(Vector3.Distance(rig.LookPoint, tv), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(rig.LookTarget.position, tv), Is.LessThan(.01f));
                Assert.IsFalse(rig.Phone.gameObject.activeSelf, "no phone in T");
            }
            finally { rig.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void SofaMotions_PhoneGeometryIsOrthonormalAndWristsAreCloseToTheCorners()
        {
            foreach (float tilt in new[] { 0f, 36f, 72f })
            {
                SofaGeometry.PhoneAxes(Vector3.right, Vector3.up, Vector3.forward, tilt, 3f, out var U, out var S, out var R);
                Assert.That(Vector3.Dot(U, S), Is.EqualTo(0).Within(1e-4f)); Assert.That(Vector3.Dot(U, R), Is.EqualTo(0).Within(1e-4f)); Assert.That(Vector3.Dot(S, R), Is.EqualTo(0).Within(1e-4f));
                Assert.That(Vector3.Dot(R, Vector3.right), Is.GreaterThan(.9f), "the phone's right is her right");
                for (int side = 0; side < 2; side++)
                {
                    SofaGeometry.Grip(Vector3.zero, U, S, R, side, out var wrist, out _, out _);
                    Assert.That(wrist.magnitude, Is.LessThan(.08f));
                    Assert.That(Mathf.Sign(Vector3.Dot(wrist, R)), Is.EqualTo(side == 0 ? -1f : 1f), "the left hand holds the left side");
                }
            }
        }

        [Test]
        public void SofaMotions_TvIsOnTheWallFacingTheSofaAndClearOfTheWardrobe()
        {
            var l = OneRoom.Layout;
            Assert.IsTrue(l.hasTv);
            Assert.That(l.tvSize.x, Is.EqualTo(.72f).Within(.001f)); Assert.That(l.tvSize.y, Is.EqualTo(.42f).Within(.001f)); Assert.That(l.tvSize.z, Is.EqualTo(.04f).Within(.001f));
            Assert.That(l.tvAt.x - l.tvSize.z / 2, Is.GreaterThanOrEqualTo(-1e-4f), "on the west wall");
            Assert.That(Vector3.Dot(l.tvFace, l.sofaFace), Is.LessThan(-.99f), "the screen faces the sofa, the sofa faces the screen");
            Assert.That(l.tvAt.z, Is.EqualTo(l.sofaAt.z).Within(.01f), "centred on the sofa");
            Assert.That(l.tvAt.y, Is.EqualTo(1.25f).Within(.01f));
            float z0 = l.tvAt.z - l.tvSize.x / 2, z1 = l.tvAt.z + l.tvSize.x / 2;
            Apartment.Use(l);
            try { foreach (var f in Apartment.Furniture().Where(q => q.name.StartsWith("wardrobe") && !q.name.Contains("handle"))) Assert.IsTrue(z1 < f.z0 || z0 > f.z1 || l.tvAt.y - l.tvSize.y / 2 > f.y1, "the TV is not in front of " + f.name); }
            finally { Apartment.Use(null); }
        }

        [Test]
        public void SofaMotions_BedLieSurfaceIsTheQuiltAndTheBedIsACatPerch()
        {
            var l = OneRoom.Layout;
            Apartment.Use(l);
            try
            {
                var f = Apartment.Furniture().ToDictionary(q => q.name);
                Assert.That(l.bedLieTop, Is.EqualTo(f["bed_duvet"].y1).Within(.005f), "she lies on the quilt");
                Assert.That(l.bedStand.x, Is.GreaterThan(f["bed_frame"].x1), "she walks to the free floor on the bed's east side");
                Assert.AreEqual("bed", f["bed_mattress"].perch);
            }
            finally { Apartment.Use(null); }
        }

        // the lying pose on the studio bed: the clip sampled, the model turned head to the north, then placed by LieRig as RoomPlay does after posing
        static void Lie(string clipName, out GameObject model, out LieRig rig, bool pillow)
        {
            var (m, r) = Make(); r.Dispose(); model = m; rig = null;
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Oddity/DevMotion/Resources/Motion/" + clipName + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) { Object.DestroyImmediate(m); Assert.Ignore("no clip " + clipName); return; }
            var a = m.GetComponent<Animator>();
            m.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clip.SampleAnimation(m, 0);
            var axis = a.GetBoneTransform(HumanBodyBones.Head).position - a.GetBoneTransform(HumanBodyBones.Hips).position; axis.y = 0;
            float ay = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg; var lay = OneRoom.Layout;
            rig = new LieRig(m, a);
            for (int i = 0; i < 12; i++)
            {
                m.transform.SetPositionAndRotation(new Vector3(lay.bedSeat.x, 0, lay.bedSeat.z), Quaternion.Euler(0, -ay, 0)); clip.SampleAnimation(m, 0);
                var hp = a.GetBoneTransform(HumanBodyBones.Hips).position; m.transform.position += new Vector3(lay.bedSeat.x - hp.x, 0, lay.bedSeat.z - hp.z);
                rig.Place(1, pillow, lay.bedLieTop, true, true);
            }
        }

        [TestCase("KaSleepTurnToTheSideLoop")]
        [TestCase("KaSleepCurlUpSidewaysLoop")]
        public void SofaMotions_LyingBodySurfaceRestsOnTheQuiltAndTheHeadOnThePillow(string clipName)
        {
            Apartment.Use(OneRoom.Layout);
            GameObject model = null;
            try
            {
                Lie(clipName, out model, out var rig, true);
                var a = model.GetComponent<Animator>(); float top = OneRoom.Layout.bedLieTop;
                Assert.That(rig.LowestY - top, Is.InRange(-.02f, .01f), "the body's lowest surface is on the quilt, lowest " + rig.LowestY);
                Assert.That(rig.TrueLowestY, Is.GreaterThan(top - .025f), "nothing sinks more than 2 cm");
                var head = a.GetBoneTransform(HumanBodyBones.Head); var pc = Apartment.PillowAt; var ps = Apartment.PillowSize;
                Assert.That(Mathf.Abs(head.position.x - pc.x), Is.LessThan(ps.x / 2 + .05f), "head over the pillow in x");
                Assert.That(Mathf.Abs(head.position.z - pc.z), Is.LessThan(ps.z / 2 + .05f), "head over the pillow in z");
                var hc = BedProbe.HeadCentre(head, a.GetBoneTransform(HumanBodyBones.Neck)); float pt = Apartment.PillowTopAt(hc.x, hc.z);
                Assert.That(rig.HeadLowY, Is.InRange(pt - .06f, pt + .03f), "the head's lowest vertex " + rig.HeadLowY + " rests on the pillow top " + pt + " (neck " + rig.Neck + ")");
                var lay = OneRoom.Layout; Assert.That(a.GetBoneTransform(HumanBodyBones.Hips).position.x, Is.InRange(0f, 1f), "inside the bed");
                // legs: each on its own side of the other along the hip axis (left hip -> right hip), knees and ankles
                var s = (a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position).normalized;
                foreach (var (l, r, name) in new[] { (HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, "knees"), (HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, "ankles") })
                    Assert.That(Vector3.Dot(a.GetBoneTransform(r).position - a.GetBoneTransform(l).position, s), Is.GreaterThan(.05f), name + " are not crossed");
            }
            finally { Apartment.Use(null); if (model != null) Object.DestroyImmediate(model); }
        }
    }
}
