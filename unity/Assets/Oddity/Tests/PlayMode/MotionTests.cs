using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>
    /// A Mixamo motion on the upper body (task 51). The motion files are not in the repository
    /// (Assets/Oddity/DevMotion is git-ignored), so these tests are ignored where the file is missing.
    /// </summary>
    public class MotionTests
    {
        const string SceneName = "Prototype";
        const string Motion = SceneConti.MotionName;

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_motiontest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/";
            SceneConti.SetLevelForRun(2);
            SceneConti.SetMotionForRun(false);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            ChatView.TypingEnabled = true;
            GameBootstrap.ClockOverride = null;
            HarinVoice.ResourceDirOverride = null;
            SceneConti.ClearLevelCache();
            SceneConti.SetMotionForRun(false);
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        IEnumerator Start()
        {
            var state = new GameState { PlayerName = "민수", Day = 3, Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        static bool StepTo(string scene, int line, int limit = 60)
        {
            var boot = Boot();
            var chat = Chat();
            for (int i = 0; i < limit; i++)
            {
                if (boot.CurrentSceneId == scene && boot.LineNumber == line) return true;
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
            }
            return boot.CurrentSceneId == scene && boot.LineNumber == line;
        }

        static void NeedFile()
        {
            if (HarinPuppet.LoadMotionClip(Motion) == null) Assert.Ignore("the motion file is not in this checkout (Assets/Oddity/DevMotion is git-ignored)");
        }

        static Transform Bone(HarinPuppet h, HumanBodyBones b) => h.GetComponent<Animator>().GetBoneTransform(b);

        static readonly HumanBodyBones[] Lower =
        {
            HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        [UnityTest]
        public IEnumerator Motion_UpperBodyOnly()
        {
            NeedFile();
            yield return Start();
            var h = HarinWardrobe.ActivePuppet();
            var rot = new Quaternion[Lower.Length];
            var pos = new Vector3[Lower.Length];
            for (int i = 0; i < Lower.Length; i++) { rot[i] = Bone(h, Lower[i]).localRotation; pos[i] = Bone(h, Lower[i]).localPosition; }
            var arm = Bone(h, HumanBodyBones.RightLowerArm);
            var armRest = arm.localRotation;
            var rootPos = h.transform.position;

            Assert.IsTrue(h.PlayMotion(Motion), "the motion starts");
            float armMax = 0f, lowerMax = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < Mathf.Min(2.5f, h.MotionLength - 0.5f))
            {
                yield return null;
                Assert.IsTrue(h.MotionPlaying);
                armMax = Mathf.Max(armMax, Quaternion.Angle(armRest, arm.localRotation));
                for (int i = 0; i < Lower.Length; i++)
                {
                    lowerMax = Mathf.Max(lowerMax, Quaternion.Angle(rot[i], Bone(h, Lower[i]).localRotation));
                    lowerMax = Mathf.Max(lowerMax, Vector3.Distance(pos[i], Bone(h, Lower[i]).localPosition) * 1000f);
                }
            }
            Assert.Less(lowerMax, 0.01f, "hips and legs do not move");
            Assert.Greater(armMax, 5f, "the arm moves");
            Assert.AreEqual(0f, Vector3.Distance(rootPos, h.transform.position), 0.0005f, "the model root stays");
        }

        [UnityTest]
        public IEnumerator Motion_ReturnsToThePose()
        {
            NeedFile();
            yield return Start();
            var h = HarinWardrobe.ActivePuppet();
            var ids = new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.RightIndexProximal };
            yield return null;
            var rest = new Quaternion[ids.Length];
            for (int i = 0; i < ids.Length; i++) { var t = Bone(h, ids[i]); if (t != null) rest[i] = t.localRotation; }

            Assert.IsTrue(h.PlayMotion(Motion));
            float moved = 0f;
            float t0 = Time.time;
            float len = h.MotionLength;
            while (h.MotionPlaying && Time.time - t0 < len + 2f)
            {
                yield return null;
                moved = Mathf.Max(moved, Quaternion.Angle(rest[1], Bone(h, ids[1]).localRotation));
            }
            Assert.IsFalse(h.MotionPlaying, "the motion ends by itself");
            Assert.Greater(moved, 5f, "it did move");
            yield return null;
            for (int i = 0; i < ids.Length; i++)
            {
                var t = Bone(h, ids[i]);
                if (t == null) continue;
                Assert.Less(Quaternion.Angle(rest[i], t.localRotation), 0.5f, ids[i] + " is back");
            }
        }

        [UnityTest]
        public IEnumerator Motion_OffChangesNothing()
        {
            yield return Start();
            var boot = Boot();
            Assert.IsFalse(SceneConti.MotionOn);
            Assert.IsTrue(StepTo(SceneConti.Night, 5), "line 5");
            var h = HarinWardrobe.ActivePuppet();
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 1.5f) { yield return null; Assert.IsFalse(h.MotionPlaying, "no motion with the switch off"); }
            Assert.IsTrue(boot.Stage.InConti);
            Assert.AreEqual(0.42f, boot.Stage.ContiPreset.Width, 0.05f, "the conti camera is the same as before");
            Assert.IsTrue(SceneConti.TryGet(SceneConti.Night, 5, 2, out var cue));
            Assert.AreEqual(SceneConti.MotionName, cue.Motion, "the table holds the name even while off");
        }

        [UnityTest]
        public IEnumerator Motion_PlaysOnTheCueLineWhenOn()
        {
            NeedFile();
            SceneConti.SetMotionForRun(true);
            yield return Start();
            Assert.IsTrue(StepTo(SceneConti.Night, 5), "line 5");
            var h = HarinWardrobe.ActivePuppet();
            yield return null;
            Assert.IsTrue(h.MotionPlaying);
            Assert.AreEqual(Motion, h.MotionName);
        }
    }
}
