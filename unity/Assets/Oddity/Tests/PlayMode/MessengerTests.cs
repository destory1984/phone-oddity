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
    /// <summary>Plays the real Prototype scene against a temporary save file and a fake clock.</summary>
    public class MessengerTests
    {
        const string SceneName = "Prototype"; // in Build Settings
        const string Name = "민수";

        string _savePath;
        DateTime _now;
        DayScript _script;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true; // these tests start at day 1
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            _script = ScriptLoader.Parse(json);
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

        void SavePlayer(Slot slot)
        {
            var state = new GameState { PlayerName = Name, Slot = slot, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        string FirstLine(Slot slot) => _script.EntryFor(slot).Lines[0].Text.Replace("{player}", Name);

        [UnityTest]
        public IEnumerator NewGame_AsksName_ThenShowsMorningFirstLine()
        {
            yield return LoadScene();
            var chat = Chat();
            Assert.IsTrue(chat.IsAskingName);
            Assert.AreEqual("", chat.CurrentLineText);

            chat.SubmitName(Name);
            yield return null;

            Assert.IsFalse(chat.IsAskingName);
            Assert.AreEqual(FirstLine(Slot.Morning), chat.CurrentLineText);
        }

        [UnityTest]
        public IEnumerator Choice_ShowsWaiting_About60s()
        {
            SavePlayer(Slot.Morning);
            yield return LoadScene();
            var chat = Chat();

            for (int i = 0; i < 30 && chat.WaitingSeconds < 0; i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }

            Assert.GreaterOrEqual(chat.WaitingSeconds, 59);
            Assert.LessOrEqual(chat.WaitingSeconds, 60);

            _now = _now.AddSeconds(20);
            yield return null;
            Assert.GreaterOrEqual(chat.WaitingSeconds, 39);
            Assert.LessOrEqual(chat.WaitingSeconds, 40);
        }

        [UnityTest]
        public IEnumerator QuitMidMorning_RestartsAtMorningFirstLine()
        {
            yield return LoadScene();
            var chat = Chat();
            chat.SubmitName(Name);
            yield return null;
            chat.Tap();
            chat.Tap();
            Assert.AreNotEqual(FirstLine(Slot.Morning), chat.CurrentLineText);

            yield return LoadScene(); // quit and start again
            chat = Chat();
            Assert.IsFalse(chat.IsAskingName, "the name was saved");
            Assert.AreEqual(FirstLine(Slot.Morning), chat.CurrentLineText);
        }

        [UnityTest]
        public IEnumerator Glitch_FollowsLine()
        {
            SavePlayer(Slot.Morning);
            yield return LoadScene();
            var chat = Chat();
            var harin = HarinWardrobe.ActivePuppet();

            // morning line 2 carries glitch 0.2
            Line expected = _script.EntryFor(Slot.Morning).Lines[1];
            Assert.AreEqual(0.2f, expected.Glitch, 0.0001f);
            chat.Tap();
            yield return null;

            Assert.AreEqual(expected.Text, chat.CurrentLineText);
            // the phone shows the calm share of the line's glitch (CharmTests covers the moments)
            Assert.AreEqual(GameBootstrap.CalmGlitch(0.2f), harin.Glitch, 0.0001f);
            Assert.AreEqual(GameBootstrap.CalmGlitch(0.2f), chat.GlitchAmount, 0.0001f);
        }

        [UnityTest]
        public IEnumerator Feeling_ShowsEmotionButtons()
        {
            SavePlayer(Slot.Evening);
            yield return LoadScene();
            var chat = Chat();

            for (int i = 0; i < 60 && !chat.FeelingMode; i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }

            Oddity.Core.Scene feeling = _script.Get("d1_evening_feeling");
            Assert.IsTrue(chat.FeelingMode);
            Assert.AreEqual(feeling.Choices.Count, chat.VisibleButtonTexts.Count);
            for (int i = 0; i < feeling.Choices.Count; i++)
            {
                Assert.AreEqual(feeling.Choices[i].Text, chat.VisibleButtonTexts[i]);
            }
        }
    }
}
