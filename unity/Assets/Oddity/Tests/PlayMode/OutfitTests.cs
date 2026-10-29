using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>
    /// The swapped-in VRoid outfit (summer B: blouse, pleated skirt): skin and hair materials are still found,
    /// the skirt stays outside the thighs when she sits, and the spring bones rest still on the phone stage.
    /// </summary>
    public class OutfitTests
    {
        const string SceneName = "Prototype";

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            ChatView.TypingEnabled = true;
            GameBootstrap.ClockOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        static IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static HarinPuppet Harin() => HarinWardrobe.ActivePuppet();

        static List<Transform> SpringJoints(HarinPuppet harin, string part) => harin
            .GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("J_Sec_") && t.name.Contains(part))
            .ToList();

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * k);
        }

        // The room look finds the face and body skin and both hair materials by their VRoid role names.
        [UnityTest]
        public IEnumerator RoomLook_FindsSkinAndHairMaterials()
        {
            yield return LoadScene();
            var harin = Harin();
            harin.SetRoomLook(true);
            yield return null;
            var mats = harin.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Distinct().ToList();
            var skin = mats.Where(m => m.name.Contains("SKIN")).ToList();
            var hair = mats.Where(m => m.name.Contains("HAIR")).ToList();
            Assert.GreaterOrEqual(skin.Count, 2, "face and body skin: " + string.Join(", ", mats.Select(m => m.name)));
            Assert.GreaterOrEqual(hair.Count, 2, "front and back hair: " + string.Join(", ", mats.Select(m => m.name)));
            foreach (var m in skin)
            {
                var c = m.GetColor("_Color");
                Assert.AreEqual(HarinPuppet.SkinBase.r, c.r, 1e-3f, m.name);
                Assert.AreEqual(HarinPuppet.SkinBase.g, c.g, 1e-3f, m.name);
                Assert.AreEqual(HarinPuppet.SkinBase.b, c.b, 1e-3f, m.name);
                // one soft shading ramp on face and body, so the jaw does not cut sharply over the neck
                Assert.AreEqual(HarinPuppet.RoomSkinToony, m.GetFloat("_ShadingToonyFactor"), 1e-4f, m.name);
                Assert.AreEqual(HarinPuppet.RoomSkinShift, m.GetFloat("_ShadingShiftFactor"), 1e-4f, m.name);
            }
            Assert.LessOrEqual(HarinPuppet.RoomSkinToony, 0.1f + 1e-5f, "ramp width 2 (1 - toony) at least 1.8");

            var glow = HarinPuppet.HairTeal * HarinPuppet.HairGlow;
            foreach (var m in hair)
            {
                var e = m.GetColor("_EmissionColor");
                Assert.AreEqual(glow.g, e.g, 1e-3f, m.name);
                Assert.AreEqual(glow.b, e.b, 1e-3f, m.name);
            }
            harin.SetRoomLook(false);
            yield return null;
            foreach (var m in skin) Assert.Greater(m.GetFloat("_ShadingToonyFactor"), 0.5f, m.name + ": imported toony back");
        }

        // Cut 12, after the spring bones settle: every skirt joint stays outside the thigh colliders
        // (VRoid's 7.7 cm spheres along each thigh), so the skirt does not go through her legs.
        [UnityTest]
        public IEnumerator Seated_SkirtOutsideThighs()
        {
            Save(CoreInfo.EmergenceDay, Slot.Night);
            yield return LoadScene();
            var seq = Boot().Emergence;
            seq.Speed = 0f;
            seq.Play();
            seq.Seek(9f);
            var harin = Harin();
            var skirt = SpringJoints(harin, "Skirt");
            Assert.GreaterOrEqual(skirt.Count, 12, "skirt spring joints");
            float end = Time.time + 1.5f;
            while (Time.time < end) yield return null;

            float s = harin.transform.lossyScale.y;
            float worst = float.MaxValue;
            string worstName = "";
            foreach (bool right in new[] { false, true })
            {
                var hip = harin.BonePosition(right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
                var knee = harin.BonePosition(right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg);
                var last = hip + (knee - hip).normalized * Mathf.Min(ThighColliderReach * s, (knee - hip).magnitude);
                foreach (var j in skirt)
                {
                    float d = DistanceToSegment(j.position, hip, last) / s;
                    if (d < worst) { worst = d; worstName = j.name + (right ? " (right thigh)" : " (left thigh)"); }
                }
            }
            Debug.Log($"OUTFIT skirt nearest thigh {worst:0.000} m at {worstName}");
            Assert.GreaterOrEqual(worst, ThighColliderRadius * 0.9f, "skirt joint inside the thigh: " + worstName);
        }

        // Cut 12, 120 frames after she sits: the tips of the front skirt chains (SkirtFront, SkirtFront2) hang
        // lower than both wrists, so the front of the skirt does not fold up over her forearms.
        [UnityTest]
        public IEnumerator Seated_SkirtBelowHands()
        {
            Save(CoreInfo.EmergenceDay, Slot.Night);
            yield return LoadScene();
            var seq = Boot().Emergence;
            seq.Speed = 0f;
            seq.Play();
            seq.Seek(9f);
            var harin = Harin();
            Assert.AreEqual(Oddity.Game.Pose.Seated, harin.CurrentPose);
            var tips = SpringJoints(harin, "SkirtFront").Where(t => t.childCount == 0).ToList();
            Assert.GreaterOrEqual(tips.Count, 4, "front skirt chain tips");
            // 120 frames and at least 2 s: after other scenes the frames get short and 120 of them were only
            // about 1 s of spring time, so the result depended on the test order (0.05 m alone, -0.03 m after the emergence tests)
            float until = Time.time + 2f;
            for (int i = 0; i < 120 || Time.time < until; i++) yield return null;

            float s = harin.transform.lossyScale.y;
            float wrist = Mathf.Min(harin.BonePosition(HumanBodyBones.LeftHand).y, harin.BonePosition(HumanBodyBones.RightHand).y);
            var top = tips.OrderByDescending(t => t.position.y).First();
            Debug.Log($"OUTFIT seated front skirt tip {top.name} {(wrist - top.position.y) / s:0.000} m under the lower wrist");
            Assert.Less(top.position.y, wrist, $"{top.name} above the wrists by {(top.position.y - wrist) / s:0.000} m");
        }

        const float ThighColliderRadius = 0.077f; // VRoid default thigh sphere radius
        const float ThighColliderReach = 0.21f;   // last thigh sphere, along the thigh from the hip

        // Phone stage (name prompt, idle on): after 1.5 s to settle, no hair or skirt joint moves more than
        // 2 mm (model units) between frames, or 5 mm over a second, relative to its body bone: the springs do not tremble.
        [UnityTest]
        public IEnumerator PhoneStage_SpringsStayStill()
        {
            yield return LoadScene();
            var harin = Harin();
            Assert.IsTrue(UnityEngine.Object.FindFirstObjectByType<ChatView>().IsAskingName);
            var head = harin.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var hips = harin.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var joints = SpringJoints(harin, "Hair").Select(t => (t, head))
                .Concat(SpringJoints(harin, "Skirt").Select(t => (t, hips))).ToList();
            Assert.GreaterOrEqual(joints.Count, 20, "spring joints");
            foreach (var j in harin.GetComponentsInChildren<UniVRM10.VRM10SpringBoneJoint>(true).Where(j => j.name.Contains("Skirt")))
                Assert.GreaterOrEqual(j.m_dragForce, HarinPuppet.SkirtDrag - 1e-5f, j.name + " drag");

            float settle = Time.time + 1.5f;
            while (Time.time < settle) yield return null;

            float s = harin.transform.lossyScale.y;
            var prev = joints.Select(j => j.Item2.InverseTransformPoint(j.Item1.position)).ToArray();
            var min = prev.ToArray();
            var max = prev.ToArray();
            float worst = 0f;
            string worstName = "";
            int frames = 0;
            float end = Time.time + 1f;
            while (Time.time < end || frames < 30)
            {
                yield return null;
                frames++;
                for (int i = 0; i < joints.Count; i++)
                {
                    var p = joints[i].Item2.InverseTransformPoint(joints[i].Item1.position);
                    // InverseTransformPoint divides by the bone's world scale, so this is in model units
                    float d = Vector3.Distance(p, prev[i]) * joints[i].Item2.lossyScale.y / s;
                    if (d > worst) { worst = d; worstName = joints[i].Item1.name; }
                    prev[i] = p;
                    min[i] = Vector3.Min(min[i], p);
                    max[i] = Vector3.Max(max[i], p);
                }
            }
            // and over the whole second (frame-rate independent): each joint stays within a 5 mm box
            float range = 0f;
            string rangeName = "";
            for (int i = 0; i < joints.Count; i++)
            {
                float r = (max[i] - min[i]).magnitude * joints[i].Item2.lossyScale.y / s;
                if (r > range) { range = r; rangeName = joints[i].Item1.name; }
            }
            Debug.Log($"OUTFIT phone springs max step {worst * 1000f:0.00} mm over {frames} frames at {worstName}, "
                + $"max range {range * 1000f:0.00} mm at {rangeName}");
            Assert.LessOrEqual(worst, 0.002f, "spring joint trembles: " + worstName);
            Assert.LessOrEqual(range, 0.005f, "spring joint wanders: " + rangeName);
        }

        void Save(int day, Slot slot)
        {
            var state = new GameState { PlayerName = "민수", Day = day, Slot = slot, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }
    }
}
