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
    /// <summary>The "typing" bubble that shows before Harin's lines.</summary>
    public class TypingTests
    {
        const string SceneName = "Prototype";
        const string Name = "민수";

        string _savePath;
        DateTime _now;
        DayScript _script;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_typingtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true; // these tests start at day 1
            GameBootstrap.ClockOverride = () => _now;
            ChatView.TypingEnabled = true;
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            _script = ScriptLoader.Parse(json);
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

        IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        static Line Harin(string text, float typing = -1f) => new Line { Who = "harin", Text = text, Typing = typing };

        [UnityTest]
        public IEnumerator HarinLine_ShowsTypingFirst()
        {
            var state = new GameState { PlayerName = Name, Slot = Slot.Morning, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return LoadScene();
            var chat = Chat();

            // the morning opens with two system lines; the taps after them bring Harin's first line
            float limit = Time.realtimeSinceStartup + 3f;
            while (chat.CurrentLineText.Length == 0 && Time.realtimeSinceStartup < limit) yield return null;
            Assert.AreEqual("system", chat.CurrentLineWho);
            var firstHarin = _script.EntryFor(Slot.Morning).Lines.Find(l => l.Who == "harin");
            for (int i = 0; i < 4 && !chat.IsTyping; i++)
            {
                chat.Tap();
                yield return null;
            }

            Assert.IsTrue(chat.IsTyping, "typing should start before Harin's first line");
            Assert.IsTrue(chat.IsTypingBubbleVisible);
            Assert.IsTrue(chat.IsTypingLabelVisible);
            Assert.AreEqual("system", chat.CurrentLineWho, "Harin's line is held back while she types");

            limit = Time.realtimeSinceStartup + 4f;
            while (chat.IsTyping && Time.realtimeSinceStartup < limit) yield return null;

            Assert.IsFalse(chat.IsTyping);
            Assert.IsFalse(chat.IsTypingBubbleVisible);
            Assert.IsFalse(chat.IsTypingLabelVisible);
            Assert.AreEqual(firstHarin.Text.Replace("{player}", Name), chat.CurrentLineText);
        }

        [UnityTest]
        public IEnumerator TypingDuration_FollowsLength()
        {
            yield return LoadScene();
            var chat = Chat();

            var shortLine = Harin("응");
            var longLine = Harin(new string('가', 60));
            var midLine = Harin(new string('나', 20));
            Assert.AreEqual(0.6f, ChatView.TypingSeconds(shortLine), 0.001f);
            Assert.AreEqual(1.8f, ChatView.TypingSeconds(longLine), 0.001f);
            Assert.AreEqual(1.2f, ChatView.TypingSeconds(midLine), 0.001f);
            Assert.AreEqual(2.4f, ChatView.TypingSeconds(Harin("x", 2.4f)), 0.001f);
            Assert.AreEqual(0f, ChatView.TypingSeconds(Harin("x", 0f)), 0.001f);

            foreach (var line in new[] { shortLine, longLine })
            {
                float expected = ChatView.TypingSeconds(line);
                chat.ShowLine(line);
                float start = Time.realtimeSinceStartup;
                while (chat.IsTyping && Time.realtimeSinceStartup - start < 4f) yield return null;
                float elapsed = Time.realtimeSinceStartup - start;
                Assert.AreEqual(expected, elapsed, 0.1f, "wait for \"" + line.Text.Substring(0, 1) + "\" x" + line.Text.Length);
            }
        }

        [UnityTest]
        public IEnumerator TapDuringTyping_SkipsToLine()
        {
            yield return LoadScene();
            var chat = Chat();
            int taps = 0;
            Action onTap = () => taps++;
            chat.Tapped += onTap;

            chat.ShowLine(Harin("안녕", 5f));
            Assert.IsTrue(chat.IsTyping);
            chat.Tap();
            chat.Tapped -= onTap;

            Assert.IsFalse(chat.IsTyping);
            Assert.IsFalse(chat.IsTypingBubbleVisible);
            Assert.AreEqual("안녕", chat.CurrentLineText);
            Assert.AreEqual(0, taps, "the skipping tap must not advance the script");

            chat.Tapped += onTap;
            chat.Tap();
            chat.Tapped -= onTap;
            Assert.AreEqual(1, taps, "the next tap behaves as before");
        }

        [UnityTest]
        public IEnumerator PlayerLine_NoTyping()
        {
            yield return LoadScene();
            var chat = Chat();

            chat.ShowLine(new Line { Who = "player", Text = "응" });
            Assert.IsFalse(chat.IsTyping);
            Assert.IsFalse(chat.IsTypingBubbleVisible);
            Assert.AreEqual("응", chat.CurrentLineText);

            chat.ShowLine(new Line { Who = "system", Text = "알림" });
            Assert.IsFalse(chat.IsTyping);
            Assert.IsFalse(chat.IsTypingBubbleVisible);
            Assert.AreEqual("알림", chat.CurrentLineText);
        }
    }
}
