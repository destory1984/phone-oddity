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
    /// <summary>In the messenger Harin looks at the window's camera while she talks (task 44).</summary>
    public class GazeTests
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
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/";
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
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        IEnumerator Start(int day, Slot slot)
        {
            var state = new GameState { PlayerName = "민수", Day = day, Slot = slot, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();

        // Taps (or picks the first choice) until `stop` is true; false when the limit ran out.
        static bool StepUntil(Func<bool> stop, int limit = 60)
        {
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            for (int i = 0; i < limit; i++)
            {
                if (stop()) return true;
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
            }
            return stop();
        }

        static bool HerLine(GameBootstrap b) => b.PhoneGazeLine != null && b.PhoneGazeLine.Who == "harin";

        static IEnumerator WaitSeconds(float seconds)
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < seconds) yield return null;
        }

        [UnityTest]
        public IEnumerator Gaze_PhoneTargetsStageCamera()
        {
            yield return Start(1, Slot.Morning);
            var boot = Boot();
            var stageCam = GameObject.Find("AppStageCamera").transform;
            Assert.IsNull(boot.PhoneGazeTarget, "free eyes before her first line");

            Assert.IsTrue(StepUntil(() => HerLine(boot) && boot.PhoneGazeTarget != null), "her line sets the target");
            Assert.AreSame(stageCam, boot.PhoneGazeTarget, "the target is the AppStageCamera, not the room camera");
            Assert.Less(boot.PhoneGazeWeight, 1f, "the eyes are still on their way");

            float t0 = Time.unscaledTime;
            while (boot.PhoneGazeWeight < 1f && Time.unscaledTime - t0 < 2f) yield return null;
            float took = Time.unscaledTime - t0;
            Assert.AreEqual(1f, boot.PhoneGazeWeight, 0.0001f);
            Assert.AreSame(stageCam, boot.Harin.EyeTarget, "the eyes follow the stage camera");
            Assert.That(took, Is.InRange(0.25f, 0.7f), "about 0.4 s");

            // the player's lines keep her eyes on the camera: step on until choices or an answer show
            int playerFrames = 0;
            for (int i = 0; i < 80; i++)
            {
                var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0); else chat.Tap();
                yield return null;
                if (boot.PhoneGazeLine != null && boot.PhoneGazeLine.Who == "player")
                {
                    playerFrames++;
                    Assert.AreSame(stageCam, boot.PhoneGazeTarget, "eyes stay on the camera on the player's line");
                }
            }
            Debug.Log("GAZE player-line frames: " + playerFrames);
        }

        [UnityTest]
        public IEnumerator Gaze_HeadWithinLimits()
        {
            // the numbers: 60% of the shot, never past 10 / 6 degrees
            foreach (var name in CamShots.Names)
            {
                var shot = CamShots.Get(name);
                var head = GameBootstrap.PhoneHeadFor(shot);
                Assert.LessOrEqual(head.x, GameBootstrap.PhoneHeadYawMax + 0.0001f, name);
                Assert.LessOrEqual(Mathf.Abs(head.y), GameBootstrap.PhoneHeadPitchMax + 0.0001f, name);
                Assert.AreEqual(Mathf.Min(10f, Mathf.Abs(shot.Yaw) * GameBootstrap.PhoneHeadShare), head.x, 0.0001f, name);
                Assert.AreEqual(Mathf.Clamp(shot.Pitch * GameBootstrap.PhoneHeadShare, -6f, 6f), head.y, 0.0001f, name);
            }
            Assert.AreEqual(10f, GameBootstrap.PhoneHeadFor(CamShots.Get("S")).x, 0.0001f);

            yield return Start(1, Slot.Morning);
            var boot = Boot();
            Assert.IsTrue(StepUntil(() => HerLine(boot) && boot.PhoneGazeTarget != null));
            var harin = boot.Harin;
            foreach (var name in new[] { "S", "H" })
            {
                boot.Stage.Force(name);
                yield return WaitSeconds(1.2f);
                var turn = harin.HeadTurnOnPose;
                var eyes = harin.EyeRotation;
                var limit = harin.EyeRotationLimit;
                Debug.Log("GAZE shot " + name + " head yaw/pitch " + turn + " eye input " + harin.EyeInput + " eye rotation " + eyes + " limit " + limit);
                var expect = GameBootstrap.PhoneHeadFor(CamShots.Get(name));
                Assert.AreEqual(expect.x, Mathf.Abs(turn.x), 0.05f, name + " head yaw");
                Assert.AreEqual(-expect.y, turn.y, 0.05f, name + " head pitch");
                Assert.Greater(turn.x * CamShots.Get(name).Yaw, 0f, name + " turns towards the camera's side");
                Assert.LessOrEqual(Mathf.Abs(turn.x), 10.001f);
                Assert.LessOrEqual(Mathf.Abs(turn.y), 6.001f);
                Assert.LessOrEqual(eyes.x, limit.x + 0.001f, name + " eyes sideways inside the model's range");
                Assert.LessOrEqual(eyes.y, limit.y + 0.001f, name + " eyes up/down inside the model's range");
            }
        }

        [UnityTest]
        public IEnumerator Gaze_LooksDownBeatExempt()
        {
            // this test is about the planner's glitch cut, which the day 3 night conti (task 48) replaces
            SceneConti.SetLevelForRun(0); // not saved; TearDown reads the real setting again
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepUntil(() => HerLine(boot) && boot.PhoneGazeLine.Beat == "teases"), "her ordinary line is reached");
            // the night line flashes the glitch shot G for 0.6 s first; G keeps the old free eyes
            Assert.IsNull(boot.PhoneGazeTarget, "the glitch cut leaves the eyes free");
            float t0 = Time.unscaledTime;
            while (boot.PhoneGazeWeight < 1f && Time.unscaledTime - t0 < 3f) yield return null;
            Assert.AreEqual(1f, boot.PhoneGazeWeight, 0.0001f, "after the glitch she looks at the camera");
            Assert.IsNotNull(boot.PhoneGazeTarget);

            Assert.IsTrue(StepUntil(() => HerLine(boot) && boot.PhoneGazeLine.Beat == "looks_down_smiling"), "the looks_down_smiling line is reached");
            Assert.IsNull(boot.PhoneGazeTarget, "that beat looks down: no camera target");
            yield return WaitSeconds(0.8f);
            Assert.AreEqual(0f, boot.PhoneGazeWeight, 0.0001f);
            Assert.IsNull(boot.Harin.EyeTarget);
            Assert.AreEqual(0f, boot.Harin.HeadTurnOnPose.x, 0.05f);
        }

        [UnityTest]
        public IEnumerator Gaze_ClearedLeavingPhone()
        {
            yield return Start(1, Slot.Morning);
            var boot = Boot();
            Assert.IsTrue(StepUntil(() => HerLine(boot) && boot.PhoneGazeTarget != null));
            yield return WaitSeconds(0.6f);
            Assert.IsNotNull(boot.Harin.EyeTarget);

            boot.PlayEmergenceNow(); // the phone is left for the room
            Assert.IsNull(boot.PhoneGazeTarget);
            Assert.AreEqual(0f, boot.PhoneGazeWeight);
            Assert.IsNull(boot.Harin.EyeTarget, "no stage camera left as the eye target");
            yield return null;
            Assert.IsNull(boot.PhoneGazeTarget);
        }
    }
}
