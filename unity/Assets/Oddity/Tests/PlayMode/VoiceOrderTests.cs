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
    /// <summary>Harin's voice and the typing indicator (task 50): the voice starts with the bubble, not with the dots.</summary>
    public class VoiceOrderTests
    {
        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_voicetest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            GameBootstrap.ClockOverride = () => _now;
            ChatView.TypingEnabled = true;
            SceneConti.SetLevelForRun(0); // no cue sound to wait for
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = null;
            ChatView.TypingEnabled = true;
            SceneConti.ClearLevelCache();
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        [UnityTest]
        public IEnumerator Voice_WaitsForTheBubble()
        {
            var clipName = "Voice/" + HarinVoice.LineClipName(SceneConti.Night, 3);
            if (Resources.Load<AudioClip>(clipName) == null) Assert.Ignore("no voice file " + clipName + " in this clone");

            var state = new GameState { PlayerName = "민수", Day = 3, Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            var voice = boot.Voice;

            // tap on until her line 3 starts to type
            float limit = Time.realtimeSinceStartup + 20f;
            while (!(boot.CurrentSceneId == SceneConti.Night && boot.LineNumber == 3) && Time.realtimeSinceStartup < limit)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0); else chat.Tap();
                yield return null;
            }
            Assert.AreEqual(3, boot.LineNumber, "line 3 reached");
            Assert.IsTrue(chat.IsTyping, "she is typing");

            // while the dots show, the voice waits
            int framesTyping = 0;
            while (chat.IsTyping && Time.realtimeSinceStartup < limit)
            {
                Assert.IsFalse(voice.IsPlaying, "the voice does not play during the typing indicator");
                Assert.IsFalse(voice.SourcePlaying);
                Assert.IsTrue(voice.IsBusy, "the clip waits for the bubble (the camera and the babble know she is about to speak)");
                framesTyping++;
                yield return null;
            }
            Assert.IsFalse(chat.IsTyping, "the typing indicator ended");
            Assert.Greater(framesTyping, 5, "the indicator was seen for a while");
            Assert.AreEqual("harin", chat.CurrentLineWho, "the bubble is up");

            // the bubble is up: the voice starts (within two frames)
            for (int i = 0; i < 3 && !voice.IsPlaying; i++) yield return null;
            Assert.IsTrue(voice.IsPlaying, "the voice plays once the bubble shows");
            Assert.AreEqual(HarinVoice.LineClipName(SceneConti.Night, 3), voice.CurrentClip);
        }
    }
}
