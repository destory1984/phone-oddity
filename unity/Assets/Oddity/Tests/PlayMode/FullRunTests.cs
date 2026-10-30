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
    /// <summary>Plays the whole prototype through the real scene, using the dev panel to skip waits.</summary>
    public class FullRunTests
    {
        const string SceneName = "Prototype";
        const string Name = "민수";
        const int FrameLimitPerStage = 1000;

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true; // these tests start at day 1
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

        IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();
        static DevPanel Dev() => UnityEngine.Object.FindFirstObjectByType<DevPanel>();

        void SaveState(Slot slot, int heart = 0)
        {
            var state = new GameState { PlayerName = Name, Slot = slot, Heart = heart, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        // One input: skip the wait screen with the dev panel, else the first button (the first
        // emotion button too), else a tap.
        static void Input(ChatView chat)
        {
            if (chat.WaitingSeconds >= 0) Dev().NextSlot();
            else if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
            else chat.Tap();
        }

        [UnityTest]
        public IEnumerator MorningChoice_RaisesDeviation()
        {
            SaveState(Slot.Morning);
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            Assert.AreEqual(0, boot.State.Deviation);

            for (int i = 0; i < 60 && chat.VisibleButtonTexts.Count == 0; i++) { chat.Tap(); yield return null; }
            Assert.AreEqual("누구세요?", chat.VisibleButtonTexts[0]);
            chat.PickChoice(0);
            yield return null;

            Assert.AreEqual(1, boot.State.Deviation);
        }

        [UnityTest]
        public IEnumerator NextSlot_OpensEveningImmediately()
        {
            SaveState(Slot.Morning);
            yield return LoadScene();
            var chat = Chat();
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            string eveningFirst = ScriptLoader.Parse(json).EntryFor(Slot.Evening).Lines[0].Text.Replace("{player}", Name);

            for (int i = 0; i < 80 && chat.WaitingSeconds < 0; i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }
            Assert.GreaterOrEqual(chat.WaitingSeconds, 0, "morning should end on the wait screen");

            Dev().NextSlot();
            yield return null;

            Assert.AreEqual(Slot.Evening, Boot().State.Slot);
            Assert.AreEqual(eveningFirst, chat.CurrentLineText);
        }

        [UnityTest]
        public IEnumerator FullRun_ThreeDays_ReachesActTwo()
        {
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            boot.Emergence.Speed = 20f; // 10 s of film in 0.5 s, so the frame count stays small
            Assert.IsTrue(chat.IsAskingName);
            chat.SubmitName(Name);

            var frames = new Dictionary<string, int>();
            var days = new HashSet<int>();
            string lastStage = null;
            int total = 0;
            while (!boot.ActTwoFinished && total < 15 * FrameLimitPerStage)
            {
                if (!boot.InRoom && !boot.Emergence.IsPlaying && !boot.Emergence.HasFinished) days.Add(boot.State.Day);
                string stage = boot.InRoom ? "Act2"
                    : (boot.Emergence.IsPlaying || boot.Emergence.HasFinished) ? "Emergence"
                    : "Day" + boot.State.Day + boot.State.Slot;
                if (stage != lastStage) { lastStage = stage; frames[stage] = 0; }
                frames[stage]++;
                total++;
                Assert.LessOrEqual(frames[stage], FrameLimitPerStage, "stage " + stage + " took more than " + FrameLimitPerStage + " frames");
                if (boot.InRoom) chat.Tap(); // act 2 lines advance on the same tap event
                else if (!boot.Emergence.IsPlaying) Input(chat);
                yield return null;
            }

            Assert.IsTrue(boot.ActTwoFinished, "act 2 cut did not finish");
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, days, "days played before the emergence");
            Assert.IsFalse(boot.EndOfContent);
            GameState s = boot.State;
            string message = "FRAMES " + string.Join(" ", frames.Select(kv => kv.Key + "=" + kv.Value))
                + " | Stability=" + s.Stability + " Deviation=" + s.Deviation + " Heart=" + s.Heart;
            Debug.Log(message);
            Assert.Pass(message);
        }

        [UnityTest]
        public IEnumerator Day4_ShowsSeeYouTomorrowAndStops()
        {
            var state = new GameState { PlayerName = Name, Day = CoreInfo.EmergenceDay + 1, Slot = Slot.Morning, NextOpenUtc = _now.AddMinutes(-1), ActTwoSeen = true };
            new SaveStore(_savePath).Save(state);
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(boot.EndOfContent);
            Assert.AreEqual(GameBootstrap.EndOfContentText, chat.CurrentLineText);
        }

        [UnityTest]
        public IEnumerator Day4_WithoutActTwoSeen_ReplaysEmergence()
        {
            var state = new GameState { PlayerName = Name, Day = CoreInfo.EmergenceDay + 1, Slot = Slot.Morning, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return LoadScene();
            var boot = Boot();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(boot.Emergence.IsPlaying, "the app was closed before act 2 ended: the emergence plays again");
            Assert.IsFalse(boot.EndOfContent);
        }

        [UnityTest]
        public IEnumerator DevPanel_NextDay_StartsNextDayMorning()
        {
            SaveState(Slot.Morning);
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            Dev().NextDay();
            yield return null;
            Assert.AreEqual(2, boot.State.Day);
            Assert.AreEqual(Slot.Morning, boot.State.Slot);
            Assert.AreEqual("d2_morning", boot.CurrentSceneId);
            Assert.IsFalse(boot.EndOfContent);
        }

        [UnityTest]
        public IEnumerator Dev_RestartFromBeginning()
        {
            SaveState(Slot.Morning);
            yield return LoadScene();
            var boot = Boot();
            string seen = _savePath + "." + GameBootstrap.PrologueSeenFile;
            File.WriteAllText(seen, "1");
            File.WriteAllText(_savePath + ".bad", "x");
            Assert.IsTrue(File.Exists(_savePath));
            string before = PlayerPrefs.GetString(HarinVoice.VoiceSetPref, "");
            PlayerPrefs.SetString(HarinVoice.VoiceSetPref, "harinB");
            try
            {
                boot.ClearProgressFiles();
                Assert.IsFalse(File.Exists(_savePath), "save deleted");
                Assert.IsFalse(File.Exists(_savePath + ".bad"), ".bad deleted");
                Assert.IsFalse(File.Exists(seen), "prologue-seen mark deleted");
                Assert.AreEqual("harinB", PlayerPrefs.GetString(HarinVoice.VoiceSetPref, ""), "voice set kept");
                boot.ClearProgressFiles(); // nothing left: no exception
            }
            finally
            {
                PlayerPrefs.SetString(HarinVoice.VoiceSetPref, before);
            }
        }

        IEnumerator RunNightTask(string taskText, int startHeart, int expectedHeart)
        {
            SaveState(Slot.Night, startHeart);
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            Assert.AreEqual(startHeart, boot.State.Heart);

            for (int i = 0; i < 80 && !chat.VisibleButtonTexts.Contains(taskText); i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }
            Assert.IsTrue(chat.VisibleButtonTexts.Contains(taskText), "task choice not shown");
            chat.PickChoice(chat.VisibleButtonTexts.ToList().IndexOf(taskText));
            yield return null;

            Assert.AreEqual(expectedHeart, boot.State.Heart);
        }

        // Heart starts at 10 so that the -2 is not hidden by the 0 floor.
        [UnityTest]
        public IEnumerator FullDay_TaskChoices_ChangeHeart()
        {
            yield return RunNightTask("친구 단톡에 답하기", 10, 12);
            yield return RunNightTask("서윤 대화방 지우기", 10, 8);
        }
    }
}
