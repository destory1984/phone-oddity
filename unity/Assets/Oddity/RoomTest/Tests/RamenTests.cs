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
    public class RamenTests
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
            if (anim == null || !anim.isHuman) { Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); }
            return model;
        }

        [Test]
        public void Ramen_TimelineRunsThreeOrderedLoopsIn120Seconds()
        {
            var clock = new RamenClock();
            clock.Start();
            var seen = new List<string> { clock.Phase };
            // Binary-exact dt avoids ambiguous floating-point boundary assertions.
            for (int i = 0; i < 960; i++)
            {
                string previous = clock.Phase;
                clock.Tick(.125f);
                if (clock.Phase != previous) seen.Add(clock.Phase);
                Assert.That(clock.Weight, Is.InRange(0f, 1f));
            }
            Assert.That(clock.Loops, Is.EqualTo(3));
            Assert.That(clock.Time, Is.Zero);
            CollectionAssert.AreEqual(new[] { "Fill", "Carry", "SwitchOn", "Wait", "Phone", "Packet", "Tear", "Drop", "Stir", "SwitchOff", "Return", "Fill" }, seen.Take(12));
            clock.Stop(); clock.Tick(.2f);
            Assert.That(clock.Weight, Is.EqualTo(.5f).Within(.00001f));
            clock.Tick(.2f); Assert.IsFalse(clock.Active);
            Assert.That(clock.Phase, Is.EqualTo("Idle"));
            clock.Start(); clock.Tick(float.NaN); clock.Tick(-1f);
            Assert.That(clock.Time, Is.Zero);
            clock.Tick(120f); Assert.That(clock.Loops, Is.EqualTo(3));
        }

        [Test]
        public void Ramen_DefaultModelReachesHandleAndStirsWithoutChangingLegs()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2.20f, 0, 4.06f), Quaternion.Euler(0, 90, 0)); // where the game ends up: the counter blocker stops her about 2.20 m
                var anim = model.GetComponent<Animator>();
                rig = new RamenRig(anim, 17);
                rig.Sink = OneRoom.RamenSink; rig.Burner = OneRoom.RamenBurner; rig.Knob = OneRoom.RamenKnob; rig.TapControl = OneRoom.RamenTap; // the studio kitchenette contacts
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(t => t.localRotation).ToArray();
                Action reset = () => { for (int i = 0; i < bones.Length; i++) bones[i].localRotation = rest[i]; };
                rig.Start(); rig.Tick(3f); rig.Apply(0);
                Assert.That(rig.Phase, Is.EqualTo("Fill"));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position, rig.LeftTarget), Is.LessThan(.035f));
                Assert.That(rig.Pot.position.y, Is.EqualTo(rig.Sink.y).Within(.001f));
                var shell = rig.Pot.Find("base");
                Assert.That(shell.localScale.x, Is.EqualTo(.18f).Within(.0001f));
                reset(); rig.Tick(26f); rig.Apply(0);
                Assert.That(rig.Phase, Is.EqualTo("Stir"));
                Assert.That(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, rig.RightTarget), Is.LessThan(.15f)); // the far side of the stir circle is out of reach by up to 0.14 m from where the counter blocker stops her
                Assert.That(rig.Chopsticks.parent, Is.EqualTo(anim.GetBoneTransform(HumanBodyBones.RightHand)));
                foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var bone = anim.GetBoneTransform(id);
                    Assert.That(Quaternion.Angle(bone.localRotation, rest[Array.IndexOf(bones, bone)]), Is.LessThan(.001f), id.ToString());
                }
                var wrist = anim.GetBoneTransform(HumanBodyBones.RightHand).position;
                for (int i = 0; i < 10; i++) rig.Apply(0);
                Assert.That(Vector3.Distance(wrist, anim.GetBoneTransform(HumanBodyBones.RightHand).position), Is.LessThan(.001f));
                rig.Stop(); reset(); rig.Tick(.4f); rig.Apply(0);
                Assert.IsFalse(rig.Active); Assert.IsFalse(rig.Props.gameObject.activeSelf);
                Assert.That(rig.Smile + rig.EyesClosed + rig.MouthOpen, Is.Zero);
                rig.Dispose(); rig.Dispose();
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void Ramen_TickDoesNotWriteBonesAndSeedRepeatsFor120Seconds()
        {
            var model = LoadModel(); RamenRig a = null, b = null;
            try
            {
                var anim = model.GetComponent<Animator>();
                a = new RamenRig(anim, 22); b = new RamenRig(anim, 22);
                var bones = model.GetComponentsInChildren<Transform>();
                var rest = bones.Select(t => t.localRotation).ToArray();
                a.Start(); b.Start();
                for (int i = 0; i < 960; i++)
                {
                    a.Tick(.125f); b.Tick(.125f);
                    Assert.That(a.Phase, Is.EqualTo(b.Phase));
                    Assert.That(a.EyesClosed, Is.EqualTo(b.EyesClosed));
                    Assert.That(a.Smile, Is.InRange(0f, 1f));
                    Assert.That(a.EyesClosed, Is.InRange(0f, 1f));
                }
                for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(rest[i], bones[i].localRotation), Is.LessThan(.001f));
                a.Tick(24f); a.Apply(0); // Interrupt while the packet is parented to the hand.
                Assert.That(a.Packet.parent, Is.EqualTo(anim.GetBoneTransform(HumanBodyBones.LeftHand)));
                a.Stop(); a.Tick(.4f); a.Apply(0);
                Assert.IsFalse(a.Packet.gameObject.activeSelf);
            }
            finally { a?.Dispose(); b?.Dispose(); Object.DestroyImmediate(model); }
        }

        // item 6: the pot hangs on the real left hand's grip (under 5 mm), the packet turns with the wrist, the stir tilt eases in
        [Test]
        public void Ramen_CarriedPotHangsOnTheRealHandAndThePacketTurnsWithTheWrist()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2.20f, 0, 4.06f), Quaternion.Euler(0, 90, 0));
                var anim = model.GetComponent<Animator>();
                rig = new RamenRig(anim, 17);
                rig.Sink = OneRoom.RamenSink; rig.Burner = OneRoom.RamenBurner; rig.Knob = OneRoom.RamenKnob; rig.TapControl = OneRoom.RamenTap;
                var bones = model.GetComponentsInChildren<Transform>(); var rest = bones.Select(t => t.localRotation).ToArray(); var pos = bones.Select(t => t.localPosition).ToArray();
                Action reset = () => { for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = rest[i]; bones[i].localPosition = pos[i]; } };
                var lhand = anim.GetBoneTransform(HumanBodyBones.LeftHand); var rhand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                rig.Start(); reset(); rig.Tick(RamenClock.FillEnd + 1.0f); rig.Apply(0);
                float worstPot = 0, lowest = 9, highest = -9, bend = 0; int carry = 0;
                var lforearm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lknuckle = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                for (int i = 0; i < 20; i++)
                {
                    reset(); rig.Tick(.05f); rig.Apply(0);
                    if (rig.Phase != "Carry") continue;
                    carry++;
                    worstPot = Mathf.Max(worstPot, Vector3.Distance(lhand.position, rig.PotHandlePoint));
                    float above = lhand.position.y - rig.Burner.y; lowest = Mathf.Min(lowest, above); highest = Mathf.Max(highest, above);
                    if (lknuckle != null) bend = Mathf.Max(bend, Vector3.Angle(lhand.position - lforearm.position, lknuckle.position - lhand.position));
                }
                Debug.Log("RAMEN_POT carry frames=" + carry + " worst hand-to-handle " + worstPot.ToString("0.0000") + " m");
                Assert.That(carry, Is.GreaterThan(5));
                Debug.Log("RAMEN_CARRY wrist " + lowest.ToString("0.000") + " - " + highest.ToString("0.000") + " m above the counter, wrist bend up to " + bend.ToString("0") + " deg");
                Assert.That(worstPot, Is.LessThan(.005f), "pot on its handle under 5 mm");
                Assert.That(lowest, Is.GreaterThanOrEqualTo(.03f), "wrist not dragged onto the counter"); Assert.That(highest, Is.LessThanOrEqualTo(.25f), "carried about counter height");
                Assert.That(bend, Is.LessThanOrEqualTo(40f), "wrist roughly straight while carrying");

                // the packet: its place in the left hand's frame does not move while the wrist turns (Packet / Tear beats, before the pour tilt)
                reset(); rig.Tick(RamenClock.PhoneEnd + 1.8f - (RamenClock.FillEnd + 2.0f)); rig.Apply(0); // forward to the packet beat, after the reach, pickup and turn upright
                Assert.That(rig.Phase, Is.EqualTo("Packet"));
                var local0 = lhand.InverseTransformPoint(rig.Packet.position); var rot0 = Quaternion.Inverse(lhand.rotation) * rig.Packet.rotation;
                float worstShift = 0, worstTurn = 0;
                for (int i = 0; i < 60; i++)
                {
                    reset(); rig.Tick(.05f); rig.Apply(0);
                    if (rig.Phase != "Packet" && rig.Phase != "Tear") break;
                    worstShift = Mathf.Max(worstShift, Vector3.Distance(local0, lhand.InverseTransformPoint(rig.Packet.position)));
                    worstTurn = Mathf.Max(worstTurn, Quaternion.Angle(rot0, Quaternion.Inverse(lhand.rotation) * rig.Packet.rotation));
                }
                Debug.Log("RAMEN_PACKET shift in the hand frame " + worstShift.ToString("0.0000") + " m, turn " + worstTurn.ToString("0.00") + " deg");
                Assert.That(worstShift, Is.LessThan(.003f)); Assert.That(worstTurn, Is.LessThan(.5f));
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        // three-fixes item 1: the packet is a thin bag lying flat on the counter until it is pinched between thumb and index at its top corner
        [Test]
        public void Ramen_PacketIsAThinBagLyingFlatOnTheCounterAndPinchedAtTheCorner()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2.20f, 0, 4.06f), Quaternion.Euler(0, 90, 0));
                var anim = model.GetComponent<Animator>();
                rig = new RamenRig(anim, 17);
                rig.Sink = OneRoom.RamenSink; rig.Burner = OneRoom.RamenBurner; rig.Knob = OneRoom.RamenKnob; rig.TapControl = OneRoom.RamenTap;
                var bones = model.GetComponentsInChildren<Transform>(); var rest = bones.Select(t => t.localRotation).ToArray(); var pos = bones.Select(t => t.localPosition).ToArray();
                Action reset = () => { for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = rest[i]; bones[i].localPosition = pos[i]; } };
                rig.Start(); reset(); rig.Tick(RamenClock.WaitEnd + 1f); rig.Apply(0);
                Assert.That(rig.Phase, Is.EqualTo("Phone"));
                Assert.That(rig.PacketThickness, Is.LessThanOrEqualTo(.006f), "bag thickness");
                var renderer = rig.Packet.GetComponentInChildren<MeshRenderer>();
                Assert.That(Mathf.Min(Vector3.Angle(rig.Packet.forward, Vector3.up), Vector3.Angle(rig.Packet.forward, Vector3.down)), Is.LessThan(5f), "flat face parallel to the counter");
                Assert.That(renderer.bounds.min.y, Is.EqualTo(rig.Burner.y).Within(.002f), "rests on the counter top");
                Assert.That(rig.PacketHeld, Is.False);
                float worstCorner = 0, worstGap = 0, worstTip = 0; int frames = 0;
                for (int i = 0; i < 200; i++)
                {
                    reset(); rig.Tick(.05f); rig.Apply(0);
                    if (rig.Phase != "Tear" && rig.Phase != "Drop") continue;
                    frames++;
                    var local = rig.Packet.InverseTransformPoint(rig.LeftPinch);
                    worstCorner = Mathf.Max(worstCorner, Mathf.Max(0, Mathf.Abs(local.x) - .06f), Mathf.Max(0, .075f - local.y), Mathf.Abs(local.z));
                    var d = anim.GetBoneTransform(HumanBodyBones.LeftIndexDistal); var th = anim.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
                    worstGap = Mathf.Max(worstGap, Vector3.Distance(d.position, th.position)); worstTip = Mathf.Max(worstTip, Vector3.Distance(rig.LeftPinch, d.position) * 2);
                }
                Debug.Log("RAMEN_BAG held frames=" + frames + " pinch off the top corner " + worstCorner.ToString("0.0000") + " m, worst thumb-index distal gap " + worstGap.ToString("0.000") + " m, tip-to-tip gap " + worstTip.ToString("0.000") + " m, thickness " + rig.PacketThickness.ToString("0.0000"));
                Assert.That(frames, Is.GreaterThan(30));
                Assert.That(worstCorner, Is.LessThan(.015f), "pinch at the top corner");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void Ramen_StirWristTiltEasesInInsteadOfSnapping()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2.20f, 0, 4.06f), Quaternion.Euler(0, 90, 0));
                var anim = model.GetComponent<Animator>();
                rig = new RamenRig(anim, 17);
                rig.Sink = OneRoom.RamenSink; rig.Burner = OneRoom.RamenBurner; rig.Knob = OneRoom.RamenKnob; rig.TapControl = OneRoom.RamenTap;
                var bones = model.GetComponentsInChildren<Transform>(); var rest = bones.Select(t => t.localRotation).ToArray(); var pos = bones.Select(t => t.localPosition).ToArray();
                Action reset = () => { for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = rest[i]; bones[i].localPosition = pos[i]; } };
                var rhand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                rig.Start(); rig.Tick(RamenClock.DropEnd - .5f); reset(); rig.Apply(0);
                var last = rhand.rotation; float worst = 0; int stir = 0;
                for (int i = 0; i < 80; i++)
                {
                    reset(); rig.Tick(.02f); rig.Apply(0);
                    worst = Mathf.Max(worst, Quaternion.Angle(last, rhand.rotation)); last = rhand.rotation;
                    if (rig.Phase == "Stir") stir++;
                }
                Debug.Log("RAMEN_STIR frames in stir " + stir + ", worst right-hand turn in one 0.02 s step " + worst.ToString("0.0") + " deg");
                Assert.That(stir, Is.GreaterThan(40));
                Assert.That(worst, Is.LessThan(8f), "no snap at the start of the stir");
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }

        [Test]
        public void Ramen_PacketReadsAsAnInstantNoodleBagRedOrangeWithSealStripsAndALabel()
        {
            var model = LoadModel(); RamenRig rig = null;
            try
            {
                rig = new RamenRig(model.GetComponent<Animator>(), 17);
                Color.RGBToHSV(rig.PacketBodyColour, out float h, out float sat, out float v);
                Assert.That(h < .09f || h > .97f, "body hue " + h + " is red/orange");
                Assert.That(sat, Is.GreaterThanOrEqualTo(.6f)); Assert.That(v, Is.GreaterThanOrEqualTo(.6f));
                var seals = rig.PacketSeals.ToArray();
                Assert.That(seals.Length, Is.EqualTo(2), "one seal strip at each short end");
                foreach (var seal in seals)
                {
                    Assert.That(seal.localScale.y, Is.InRange(.008f, .010f), "seal strip 8-10 mm");
                    Assert.That(Mathf.Abs(seal.localPosition.y), Is.GreaterThan(.06f), "at a short end");
                    Assert.That(seal.localScale.x, Is.GreaterThanOrEqualTo(.11f), "across the whole width");
                }
                Assert.That(Mathf.Sign(seals[0].localPosition.y), Is.Not.EqualTo(Mathf.Sign(seals[1].localPosition.y)));
                Color.RGBToHSV(rig.PacketLabelColour, out _, out float ls, out float lv);
                Assert.That(lv, Is.GreaterThanOrEqualTo(v - .01f)); Assert.That(ls, Is.LessThan(sat - .3f), "label paler (less saturated) than the body");
                var label = rig.PacketLabelPatch; Assert.IsNotNull(label);
                Assert.That(Mathf.Abs(label.localPosition.y), Is.LessThan(.01f), "label in the middle");
                Debug.Log("RAMEN_PACKET hue " + h.ToString("0.000") + " sat " + sat.ToString("0.00") + " value " + v.ToString("0.00") + " seals " + seals.Length + " label value " + lv.ToString("0.00"));
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); }
        }
    }
}
