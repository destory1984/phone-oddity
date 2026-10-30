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
    /// <summary>The hand-written conti of day 3 night (task 48): cuts, pushes, the cup sound, and handing the camera back.</summary>
    public class ContiTests
    {
        const string SceneName = "Prototype";

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_contitest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/";
            SceneConti.SetLevelForRun(2);
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
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        // Taps (or picks the first choice) until the given line of the given scene is on screen.
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

        static IEnumerator WaitSeconds(float seconds)
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < seconds) yield return null;
        }

        // Where the camera must stand for a framing, given the anchor the director took at the last cut.
        static void AssertCameraAt(GameBootstrap boot, CamShots.Preset preset, string what)
        {
            var stage = boot.Stage;
            var pose = AppStageDirector.PoseFor(stage.AnchorEyes, stage.AnchorFacing, stage.Camera.fieldOfView, preset);
            Assert.That(Vector3.Distance(pose.position, stage.Camera.transform.position), Is.LessThan(0.001f), what + " position");
            Assert.That(Quaternion.Angle(pose.rotation, stage.Camera.transform.rotation), Is.LessThan(0.1f), what + " rotation");
        }

        [UnityTest]
        public IEnumerator Conti_CupLineCutsToSideAndPlaysSound()
        {
            Assert.NotNull(Resources.Load<AudioClip>("Sfx/" + SceneConti.CupSfx), "the cup sound is missing (python tools/make_cup_sfx.py makes it)");
            yield return Start(3, Slot.Night);
            var boot = Boot();
            var chat = Chat();
            Assert.IsTrue(StepTo(SceneConti.Night, 1), "line 1");
            yield return null;
            Assert.IsTrue(boot.Stage.InConti, "line 1 cuts to L");
            Assert.AreEqual(0.58f, boot.Stage.ContiPreset.Width, 0.0001f);

            ChatView.TypingEnabled = true; // the tap that skips the cup sound is held only in real play
            chat.Tap();
            yield return null;
            Assert.AreEqual(SceneConti.Night, boot.CurrentSceneId);
            Assert.AreEqual(2, boot.LineNumber);
            Assert.AreEqual("system", chat.CurrentLineWho);
            Assert.IsTrue(boot.Stage.InConti);
            Assert.AreEqual(SceneConti.SideYaw[1], boot.Stage.ContiPreset.Yaw, 0.0001f, "mid strength: 32 degrees");
            Assert.AreEqual(0.30f, boot.Stage.ContiPreset.Width, 0.01f, "side face starts at 0.30");
            AssertCameraAt(boot, boot.Stage.ContiPreset, "side face");
            Assert.AreEqual(SceneConti.CupSfx, boot.LastSfx, "the cup sound started");

            chat.Tap();
            yield return null;
            Assert.AreEqual(2, boot.LineNumber, "a tap during the cup sound does not skip it");
            yield return WaitSeconds(1.0f);
            Assert.Less(boot.Stage.ContiPreset.Width, 0.2995f, "slowly pushing in");
            Assert.That(boot.Stage.ContiPreset.Width, Is.GreaterThan(SceneConti.SidePushEnd[1] - 0.0001f));

            yield return WaitSeconds(2.4f); // 2.7 s sound + 0.5 s
            chat.Tap();
            yield return null;
            Assert.AreEqual(3, boot.LineNumber, "after the sound the tap goes on");
        }

        [UnityTest]
        public IEnumerator Conti_ClimaxPushEndsOnC2()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.NightEnd, 3), "end line 3");
            yield return null;
            Assert.AreEqual(0.62f, boot.Stage.ContiPreset.Width, 0.001f, "the push starts from M");
            float min = 1f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 2.6f)
            {
                yield return null;
                min = Mathf.Min(min, boot.Stage.ContiPreset.Width);
            }
            var p = boot.Stage.ContiPreset;
            Assert.AreEqual(0.40f, p.Width, 0.0005f);
            Assert.AreEqual(0.37f, p.EyeLine, 0.0005f);
            Assert.AreEqual(0f, p.Yaw, 0.0005f);
            Assert.AreEqual(0f, p.Pitch, 0.0005f);
            Assert.Less(min, 0.40f * (1f - SceneConti.ClimaxOvershoot[1]) + 0.002f, "it went past C2 a little first");
            AssertCameraAt(boot, p, "C2");
        }

        [UnityTest]
        public IEnumerator Conti_LastLineEndsOnLevelWidth()
        {
            for (int level = 1; level <= 3; level++)
            {
                SceneConti.SetLevelForRun(level);
                yield return Start(3, Slot.Night);
                var boot = Boot();
                Assert.IsTrue(StepTo(SceneConti.NightEnd, 7), "end line 7 at strength " + level);
                yield return WaitSeconds(SceneConti.FinalPushSeconds + 0.4f);
                var p = boot.Stage.ContiPreset;
                Assert.AreEqual(SceneConti.FinalWidth[level - 1], p.Width, 0.0005f, "final width at strength " + level);
                Assert.AreEqual(0.39f, p.EyeLine, 0.0005f);
                AssertCameraAt(boot, p, "final");
            }
            Assert.AreEqual(0.38f, SceneConti.FinalWidth[0], 0.0001f);
            Assert.AreEqual(0.35f, SceneConti.FinalWidth[1], 0.0001f);
            Assert.AreEqual(0.32f, SceneConti.FinalWidth[2], 0.0001f);
        }

        [UnityTest]
        public IEnumerator Conti_OffUsesThePlanner()
        {
            SceneConti.SetLevelForRun(0);
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3");
            yield return null;
            Assert.IsFalse(boot.Stage.InConti, "off: no cue");
            Assert.AreEqual(0, boot.Stage.CueSerial);
            Assert.IsNull(boot.LastSfx, "off: no cup sound");
            var preset = CamShots.Get(boot.Stage.Planner.Shot);
            AssertCameraAt(boot, preset, "planner shot " + preset.Name);
        }

        [UnityTest]
        public IEnumerator Conti_LeavingTheSceneReturnsToThePlanner()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.Night, 5), "line 5");
            yield return null;
            Assert.IsTrue(boot.Stage.InConti);

            // a new slot starts: the planner takes over at its default shot
            boot.JumpTo(3, Slot.Morning);
            yield return null;
            Assert.IsFalse(boot.Stage.InConti, "BeginSlot ends the cue");
            Assert.AreEqual(CamShots.M, boot.Stage.Shot);
            yield return null;
            AssertCameraAt(boot, CamShots.Get(CamShots.M), "morning M");

            // Force (shots, dev tools) works as before
            boot.Stage.Force(CamShots.S);
            yield return null;
            Assert.IsFalse(boot.Stage.InConti);
            AssertCameraAt(boot, CamShots.Get(CamShots.S), "forced S");

            // a day with no script ends the cue too, and the planner's camera works
            yield return Start(3, Slot.Night);
            boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3 before the end of content");
            Assert.IsTrue(boot.Stage.InConti);
            boot.JumpTo(4, Slot.Morning);
            yield return null;
            Assert.IsFalse(boot.Stage.InConti, "end of content ends the cue");
            AssertCameraAt(boot, CamShots.Get(boot.Stage.Planner.Shot), "planner after the end of content");

            // switching the conti off in the middle of the scene hands the camera back at the next line
            yield return Start(3, Slot.Night);
            boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3 again");
            Assert.IsTrue(boot.Stage.InConti);
            SceneConti.SetLevelForRun(0);
            Assert.IsTrue(StepTo(SceneConti.Night, 4), "line 4");
            yield return null;
            Assert.IsFalse(boot.Stage.InConti, "off ends the cue at the next line");
            AssertCameraAt(boot, CamShots.Get(boot.Stage.Planner.Shot), "planner after off");
        }

        [UnityTest]
        public IEnumerator Conti_GlitchOnlyOnTheLastLine()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            int moments = 0;
            for (int i = 0; i < 60 && !(boot.CurrentSceneId == SceneConti.NightEnd && boot.LineNumber == 7); i++)
            {
                if (boot.GlitchMomentActive) moments++;
                if (Chat().VisibleButtonTexts.Count > 0) Chat().PickChoice(0); else Chat().Tap();
                yield return null;
                if (boot.GlitchMomentActive && !(boot.CurrentSceneId == SceneConti.NightEnd && boot.LineNumber == 7)) moments++;
            }
            Assert.AreEqual(0, moments, "no glitch flash before the last line");
            yield return null;
            Assert.IsTrue(boot.GlitchMomentActive, "the last line flashes");
            Assert.IsFalse(boot.Stage.Planner.InGlitch, "but never cuts to G");

            // off keeps the old rule: day 3 night Harin lines flash
            SceneConti.SetLevelForRun(0);
            yield return Start(3, Slot.Night);
            boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3");
            yield return null;
            Assert.IsTrue(boot.GlitchMomentActive, "level 0: the old rule");
        }

        [UnityTest]
        public IEnumerator Conti_NoJumpBetweenTheTwoScenes()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            var chat = Chat();
            Assert.IsTrue(StepTo(SceneConti.Night, 8), "last line of the first scene");
            yield return WaitSeconds(0.3f);
            var cam = boot.Stage.Camera.transform;
            int serial = boot.Stage.Planner.Serial;
            var before = cam.position;
            float widthBefore = boot.Stage.ContiPreset.Width;
            chat.Tap(); // the choices
            yield return null;
            Assert.IsTrue(chat.VisibleButtonTexts.Count > 0, "choices are up");
            chat.PickChoice(0);
            float worst = 0f;
            var last = cam.position;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                worst = Mathf.Max(worst, Vector3.Distance(cam.position, last));
                last = cam.position;
            }
            Assert.AreEqual(SceneConti.NightEnd, boot.CurrentSceneId);
            Assert.AreEqual(1, boot.LineNumber);
            Assert.Less(worst, 0.002f, "the camera does not jump");
            Assert.Less(Vector3.Distance(before, cam.position), 0.002f, "same picture in the next scene");
            Assert.AreEqual(widthBefore, boot.Stage.ContiPreset.Width, 0.0001f);
            Assert.AreEqual(serial, boot.Stage.Planner.Serial, "no BeginSlot or planner shot between the two scenes");
            Assert.IsTrue(boot.Stage.InConti);
        }

        [UnityTest]
        public IEnumerator Conti_StillWhileChoicesAreUp()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            var chat = Chat();
            Assert.IsTrue(StepTo(SceneConti.Night, 7), "line 7");
            yield return WaitSeconds(0.8f); // the chin has gone up
            chat.Tap();                    // line 8 (the head will go back after 0.5 s)
            Assert.AreEqual(8, boot.LineNumber);
            chat.Tap();                    // the choices come at once
            yield return null;
            Assert.IsTrue(chat.VisibleButtonTexts.Count > 0, "choices are up");
            float cueT = boot.Stage.CueSeconds;
            var turn = boot.Harin.HeadTurnOnPose;
            var pos = boot.Stage.Camera.transform.position;
            Assert.That(turn.y, Is.EqualTo(-2f).Within(0.3f), "chin up");
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(cueT, boot.Stage.CueSeconds, 0.05f, "the cue clock stands still");
            Assert.That(boot.Harin.HeadTurnOnPose.y, Is.EqualTo(turn.y).Within(0.05f), "the head does not move");
            Assert.Less(Vector3.Distance(pos, boot.Stage.Camera.transform.position), 0.0005f, "the camera does not move");
        }

        [Test]
        public void CupSfx_LoadsFromResources()
        {
            var clip = Resources.Load<AudioClip>("Sfx/" + SceneConti.CupSfx);
            Assert.NotNull(clip, "Resources/Sfx/" + SceneConti.CupSfx);
            Assert.AreEqual(2.70f, clip.length, 0.05f);
            Assert.AreEqual(1, clip.channels);
            Assert.AreEqual(44100, clip.frequency);
        }
    }
}
