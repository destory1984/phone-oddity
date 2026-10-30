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
    /// <summary>The previs video's hands-free player (task 49): it reads day 3 night like a person and stops on the last line.</summary>
    public class PrevisTests
    {
        const string SceneName = "Prototype";

        static readonly string[] Expected =
        {
            "d3_night:1", "d3_night:2", "d3_night:3", "d3_night:4", "d3_night:5", "d3_night:6", "d3_night:7", "d3_night:8",
            "d3_night_end:1", "d3_night_end:2", "d3_night_end:3", "d3_night_end:4", "d3_night_end:5", "d3_night_end:6", "d3_night_end:7",
        };

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_previstest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = true; // the typing indicator is part of the pace being tested
            GameBootstrap.ClockOverride = () => _now;
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/"; // no voice files: the pace does not depend on the clips on this machine
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Previs_AutoPlayReachesTheLastLine()
        {
            var state = new GameState { PlayerName = "민수", Day = 3, Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            var auto = new PrevisAutoPlay(boot, chat, SceneConti.NightEnd, 7);

            float t0 = Time.time;
            while (!auto.Done && Time.time - t0 < 200f)
            {
                auto.Step(Time.time);
                yield return null;
            }

            Assert.IsTrue(auto.Done, "the player reached the end within 200 s");
            CollectionAssert.AreEqual(Expected, auto.Visited, "every line once, in the order of the script");
            Assert.AreEqual(1, auto.ChoicesPicked, "the one choice was picked");
            Assert.AreEqual(SceneConti.NightEnd, boot.CurrentSceneId, "it stays on the last scene");
            Assert.AreEqual(7, boot.LineNumber, "it does not leave the last line");
            Assert.IsFalse(boot.EmergenceBusy, "the emergence does not start");

            // pace: each tap comes no earlier than the rule of its kind (only the last line has none; the choice after d3_night:8 is picked, not tapped)
            var cup = Resources.Load<AudioClip>("Sfx/" + SceneConti.CupSfx);
            Assert.NotNull(cup, "the cup sound is missing (python tools/make_cup_sfx.py makes it)");
            int taps = 0;
            for (int i = 0; i < auto.Visited.Count; i++)
            {
                if (i == auto.Visited.Count - 1) continue;      // the last line stays
                float shown = auto.VisitedAt[i];
                float tapped = auto.AdvancedAt[taps++];
                float gap = tapped - shown;
                string who = auto.VisitedWho[i];
                if (who == "system") Assert.GreaterOrEqual(gap, cup.length + PrevisAutoPlay.SystemAfterSfxSeconds - 0.1f, auto.Visited[i] + " waits for the cup sound and 0.5 s");
                else if (who == "player") Assert.GreaterOrEqual(gap, PrevisAutoPlay.PlayerSeconds(1) - 0.1f, auto.Visited[i] + " player line pace");
                else Assert.GreaterOrEqual(gap, PrevisAutoPlay.HarinHoldSeconds + 0.5f, auto.Visited[i] + " typing and 0.8 s");
            }
            Assert.AreEqual(taps, auto.AdvancedAt.Count, "every tap was matched to a line");
        }
    }
}
