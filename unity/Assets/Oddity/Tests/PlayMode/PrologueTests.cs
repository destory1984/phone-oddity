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
    /// <summary>The day 12 prologue on first launch, the rewind into day 1, and the placeholder voice.</summary>
    public class PrologueTests
    {
        const string SceneName = "Prototype";
        const int FrameLimit = 2000;

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = () => _now;
            ChatView.TypingEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = null;
            ChatView.TypingEnabled = true;
            HarinVoice.ResourceDirOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }

        string MarkerPath => _savePath + "." + GameBootstrap.PrologueSeenFile;

        IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        // One prologue input: the first choice if buttons are up, else a tap.
        static void Input(GameBootstrap boot)
        {
            if (boot.Subtitle.VisibleChoiceTexts.Count > 0) boot.Subtitle.PickChoice(0);
            else if (boot.PrologueAwaitingTap) boot.Subtitle.Tap();
        }

        [UnityTest]
        public IEnumerator FirstLaunch_StartsWithPrologue()
        {
            yield return LoadScene();
            var boot = Boot();
            Assert.IsTrue(boot.InPrologue);
            Assert.AreEqual("p_dinner", boot.PrologueSceneId);
            Assert.IsFalse(Chat().IsVisible, "the messenger is hidden during the prologue");
            Assert.IsTrue(boot.Subtitle.IsVisible);
            Assert.AreEqual("책 좀 치워. 네 밥 들고 있잖아.", boot.Subtitle.CurrentText);
            Assert.AreEqual(PrologueDirector.Stage.Seated, boot.Prologue.CurrentStage);
            Assert.AreEqual("sets_table", boot.Prologue.CurrentBeat);
        }

        [UnityTest]
        public IEnumerator Prologue_DoesNotChangeStats()
        {
            var before = new GameState
            {
                PlayerName = "민수", Day = 1, Slot = Slot.Morning, Stability = 40, Deviation = 7, Heart = 12,
                DaysLeft = 30, NextOpenUtc = _now.AddMinutes(-1)
            };
            new SaveStore(_savePath).Save(before);
            string savedJson = File.ReadAllText(_savePath);
            yield return LoadScene();
            var boot = Boot();
            Assert.IsTrue(boot.InPrologue);

            for (int i = 0; i < FrameLimit && boot.InPrologue && !boot.Prologue.IsRewinding; i++)
            {
                Input(boot);
                yield return null;
            }
            Assert.IsTrue(boot.InPrologue && boot.Prologue.IsRewinding, "reached the rewind");
            var s = boot.State;
            Assert.AreEqual(before.Day, s.Day);
            Assert.AreEqual(before.Slot, s.Slot);
            Assert.AreEqual(before.Stability, s.Stability);
            Assert.AreEqual(before.Deviation, s.Deviation);
            Assert.AreEqual(before.Heart, s.Heart);
            Assert.AreEqual(before.DaysLeft, s.DaysLeft);
            Assert.AreEqual(before.NextOpenUtc, s.NextOpenUtc);
            Assert.AreEqual(savedJson, File.ReadAllText(_savePath), "the save file is untouched");
        }

        [UnityTest]
        public IEnumerator Prologue_EndsWithRewindThenDay1Morning()
        {
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            bool sawNotice = false;
            for (int i = 0; i < FrameLimit && !boot.Prologue.IsRewinding; i++)
            {
                if (boot.Prologue.IsNoticeVisible) sawNotice = true;
                Input(boot);
                yield return null;
            }
            Assert.IsTrue(sawNotice, "the deletion notice showed before the rewind");
            StringAssert.Contains("19일 후 삭제됩니다", boot.Prologue.NoticeText);
            Assert.IsTrue(boot.Prologue.IsRewinding);
            Assert.AreEqual("p_rewind", boot.PrologueSceneId);

            // taps do nothing during the rewind
            string text = boot.Subtitle.CurrentText;
            boot.Subtitle.Tap();
            yield return null;
            Assert.AreEqual(text, boot.Subtitle.CurrentText);
            Assert.IsTrue(boot.InPrologue);

            float start = Time.time;
            while (boot.InPrologue && Time.time - start < PrologueDirector.RewindDuration + 2f) yield return null;
            Assert.IsFalse(boot.InPrologue, "the rewind ends the prologue");
            Assert.GreaterOrEqual(Time.time - start, PrologueDirector.RewindDuration - 0.3f, "the rewind takes about 2.5 s");
            Assert.IsTrue(chat.IsVisible);
            Assert.IsFalse(boot.Subtitle.IsVisible);
            Assert.IsTrue(chat.IsLockNoticeVisible);
            StringAssert.Contains("되감기", chat.LockNoticeText);
            Assert.IsTrue(chat.IsAskingName, "a new game asks the name next");
            Assert.AreEqual(1, boot.State.Day);
            Assert.AreEqual(Slot.Morning, boot.State.Slot);
            Assert.IsTrue(File.Exists(MarkerPath), "the prologue is marked as seen");
        }

        [UnityTest]
        public IEnumerator SecondLaunch_SkipsPrologue()
        {
            File.WriteAllText(MarkerPath, "1");
            yield return LoadScene();
            var boot = Boot();
            Assert.IsFalse(boot.InPrologue);
            Assert.IsNull(boot.Prologue);
            Assert.IsTrue(Chat().IsVisible);
            Assert.IsTrue(Chat().IsAskingName);
        }

        [UnityTest]
        public IEnumerator UnknownBeat_Ignored()
        {
            yield return LoadScene();
            var boot = Boot();
            string beat = boot.Prologue.CurrentBeat;
            Assert.IsFalse(boot.Prologue.PlayBeat("no_such_beat_xyz"));
            Assert.IsFalse(PrologueDirector.IsKnown("no_such_beat_xyz"));
            Assert.AreEqual(beat, boot.Prologue.CurrentBeat);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(boot.InPrologue);
            Assert.AreEqual("p_dinner", boot.PrologueSceneId);
        }

        [UnityTest]
        public IEnumerator Prologue_AllScriptBeatsKnown()
        {
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "prologue.json"));
            var script = ScriptLoader.Parse(json);
            foreach (var scene in script.Scenes)
                foreach (var line in scene.Lines)
                    if (!string.IsNullOrEmpty(line.Beat))
                        Assert.IsTrue(PrologueDirector.IsKnown(line.Beat), scene.Id + ": " + line.Beat);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Voice_MissingFile_Silent()
        {
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/";
            var go = new GameObject("VoiceTest");
            var voice = go.AddComponent<HarinVoice>();
            voice.Init(null);
            for (int i = 0; i < 20; i++) voice.PlayLine("p_dinner", 3, "happy", "teases");
            yield return null;
            Assert.IsFalse(voice.IsPlaying);
            Assert.IsNull(voice.CurrentClip);
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Voice_SetSwitch_PicksSuffix()
        {
            string old = HarinVoice.VoiceSet;
            try
            {
                HarinVoice.VoiceSet = "karina";
                Assert.AreEqual("harin_p_dinner_3_karina", HarinVoice.LineClipName("p_dinner", 3));
                Assert.AreEqual("harinA", HarinVoice.NextVoiceSet());
                HarinVoice.VoiceSet = HarinVoice.NextVoiceSet();
                Assert.AreEqual("harin_p_dinner_3_harinA", HarinVoice.LineClipName("p_dinner", 3));
                Assert.AreEqual("harinA", PlayerPrefs.GetString("oddity.voiceSet"), "the choice is remembered");

                var go = new GameObject("VoiceTest");
                var voice = go.AddComponent<HarinVoice>();
                voice.Init(null);
                voice.PlayLine("p_dinner", 3, "happy", "teases");
                // with the placeholder audio present the line's own clip plays; without it, nothing or a short sound
                if (Resources.Load<AudioClip>(HarinVoice.DefaultResourceDir + "harin_p_dinner_3_harinA") != null)
                    Assert.AreEqual("harin_p_dinner_3_harinA", voice.CurrentClip);
                else if (voice.CurrentClip != null)
                    StringAssert.StartsWith("harin_sfx_", voice.CurrentClip);
                HarinVoice.VoiceSet = "karina";
                voice.Replay();
                if (Resources.Load<AudioClip>(HarinVoice.DefaultResourceDir + "harin_p_dinner_3_karina") != null)
                    Assert.AreEqual("harin_p_dinner_3_karina", voice.CurrentClip);
                UnityEngine.Object.Destroy(go);
                yield return null;
            }
            finally
            {
                HarinVoice.VoiceSet = old;
            }
        }

        [UnityTest]
        public IEnumerator Voice_LipSync_FollowsAmplitude()
        {
            GameBootstrap.SkipPrologue = true;
            yield return LoadScene();
            var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
            const int rate = 48000;
            const float seconds = 0.5f;
            var clip = AudioClip.Create("sine", (int)(rate * seconds), 1, rate, false);
            var data = new float[(int)(rate * seconds)];
            for (int i = 0; i < data.Length; i++) data[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 220f * i / rate);
            clip.SetData(data, 0);
            Assert.Greater(HarinVoice.Rms(data, 1, rate, 0.1f, 0.05f), 0.3f);

            var go = new GameObject("VoiceTest");
            var voice = go.AddComponent<HarinVoice>();
            voice.Init(harin);
            voice.PlayClip(clip);
            Assert.IsTrue(voice.IsPlaying);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Greater(harin.MouthOpen, 0f, "the mouth opens while the clip plays");

            float start = Time.time;
            while (voice.IsPlaying && Time.time - start < 2f) yield return null;
            yield return null;
            Assert.AreEqual(0f, harin.MouthOpen, "the mouth closes at the end");
            UnityEngine.Object.Destroy(go);
        }
    }
}
