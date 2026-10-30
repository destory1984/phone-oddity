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
    /// <summary>Task 35: the messenger fills its first screen quickly and its noise is low.</summary>
    public class ChatCalmTests
    {
        const string Name = "민수";
        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_calmtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            GameBootstrap.ClockOverride = () => _now;
            ChatView.TypingEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = null;
            ChatView.TypingEnabled = true;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        IEnumerator LoadMorning()
        {
            var state = new GameState { PlayerName = Name, Slot = Slot.Morning, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        // No tap: after the notice shows, her typing bubble starts within 0.6 s.
        [UnityTest]
        public IEnumerator Chat_FirstHarinLineSoon()
        {
            yield return LoadMorning();
            var chat = Chat();
            float limit = Time.realtimeSinceStartup + 3f;
            while (chat.CurrentLineText.Length == 0 && Time.realtimeSinceStartup < limit) yield return null;
            Assert.AreEqual("system", chat.CurrentLineWho);
            float shownAt = Time.realtimeSinceStartup;
            while (!chat.IsTyping && Time.realtimeSinceStartup - shownAt < 2f) yield return null;
            Assert.IsTrue(chat.IsTypingBubbleVisible, "typing bubble shown without a tap");
            Assert.LessOrEqual(Time.realtimeSinceStartup - shownAt, 0.6f, "seconds from the notice to the typing bubble");
        }

        [UnityTest]
        public IEnumerator Chat_InstallNoticeOneLine()
        {
            yield return LoadMorning();
            var chat = Chat();
            float limit = Time.realtimeSinceStartup + 3f;
            while (chat.CurrentLineText.Length == 0 && Time.realtimeSinceStartup < limit) yield return null;
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            var lines = ScriptLoader.Parse(json).EntryFor(Slot.Morning).Lines;
            Assert.AreEqual("system", lines[0].Who);
            Assert.AreEqual("harin", lines[1].Who, "one notice line, then Harin");
            Assert.IsFalse(chat.CurrentLineText.Contains("\n"));
            Assert.AreEqual(1, chat.BubbleCount, "one notice bubble on the first screen");
        }

        [UnityTest]
        public IEnumerator Chat_NoiseCalm()
        {
            yield return LoadMorning();
            var chat = Chat();
            const float oldCalmScale = 0.3f; // the steady window noise before task 35
            Assert.LessOrEqual(GameBootstrap.GlitchCalmScale, 0.45f * oldCalmScale, "window noise");
            Assert.LessOrEqual(chat.WallpaperNoiseLeft, 0.45f, "wallpaper noise");
            Assert.Less(chat.WallpaperNoiseLeft, 1f);
            Assert.AreEqual(GameBootstrap.MomentGlitch(0.7f), 0.7f, 1e-5f, "glitch moments stay strong");
        }
    }
}
