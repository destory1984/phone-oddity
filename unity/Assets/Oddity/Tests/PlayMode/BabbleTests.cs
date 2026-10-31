using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>The player's chatter plays on his lines in the prologue, never on Harin's, and stops on a tap.</summary>
    public class BabbleTests
    {
        const string SceneName = "Prototype";
        const int FrameLimit = 2000;

        string _savePath;
        DateTime _now;
        string _setting;
        HashSet<string> _playerTexts;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = () => _now;
            ChatView.TypingEnabled = false;
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/"; // Harin is silent, so the chatter never waits for her
            _setting = PlayerBabble.Setting;
            PlayerBabble.Setting = "mid";
            var script = ScriptLoader.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "prologue.json")));
            _playerTexts = new HashSet<string>();
            foreach (var scene in script.Scenes)
                foreach (var line in scene.Lines)
                    if (line.Who == "player") _playerTexts.Add(line.Text);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            GameBootstrap.ClockOverride = null;
            ChatView.TypingEnabled = true;
            HarinVoice.ResourceDirOverride = null;
            PlayerBabble.Setting = _setting;
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_savePath + "." + GameBootstrap.PrologueSeenFile)) File.Delete(_savePath + "." + GameBootstrap.PrologueSeenFile);
        }

        IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();

        static void Input(GameBootstrap boot)
        {
            if (boot.Subtitle.VisibleChoiceTexts.Count > 0) boot.Subtitle.PickChoice(0);
            else if (boot.PrologueAwaitingTap) boot.Subtitle.Tap();
        }

        [Test]
        public void Babble_OffByDefault()
        {
            PlayerPrefs.DeleteKey(PlayerBabble.SettingPref);
            PlayerBabble.ClearSettingCache();
            Assert.IsFalse(PlayerBabble.Enabled);
        }

        [UnityTest]
        public IEnumerator Babble_PlayerLinePlays()
        {
            yield return LoadScene();
            var boot = Boot();
            Assert.IsNotNull(boot.Babble);
            bool found = false;
            for (int i = 0; i < FrameLimit && boot.InPrologue && !boot.Prologue.IsRewinding; i++)
            {
                if (boot.Babble.IsPlaying) { found = true; break; }
                Input(boot);
                yield return null;
            }
            Assert.IsTrue(found, "a player line started the chatter");
            CollectionAssert.Contains(_playerTexts, boot.Subtitle.CurrentText, "it is the player's line on the subtitle");
            Assert.Greater(boot.Babble.ClipSeconds, 0.2f);
        }

        [UnityTest]
        public IEnumerator Babble_HarinLineSilent()
        {
            yield return LoadScene();
            var boot = Boot();
            int harinFrames = 0, playerFrames = 0;
            for (int i = 0; i < FrameLimit && boot.InPrologue && !boot.Prologue.IsRewinding; i++)
            {
                if (boot.Subtitle.IsVisible && boot.Subtitle.Alpha > 0f && !_playerTexts.Contains(boot.Subtitle.CurrentText))
                {
                    harinFrames++;
                    Assert.IsFalse(boot.Babble.IsBusy, "no chatter on: " + boot.Subtitle.CurrentText);
                }
                else if (_playerTexts.Contains(boot.Subtitle.CurrentText)) playerFrames++;
                Input(boot);
                yield return null;
            }
            Assert.Greater(harinFrames, 3);
            Assert.Greater(playerFrames, 0);
        }

        [UnityTest]
        public IEnumerator Babble_StopsOnAdvance()
        {
            yield return LoadScene();
            var boot = Boot();
            bool stopped = false;
            for (int i = 0; i < FrameLimit && boot.InPrologue && !boot.Prologue.IsRewinding; i++)
            {
                if (boot.Babble.IsPlaying && boot.PrologueAwaitingTap)
                {
                    boot.Subtitle.Tap();
                    // the next line is Harin's or a system line unless the script has two player lines in a row
                    if (!_playerTexts.Contains(boot.Subtitle.CurrentText))
                    {
                        Assert.IsFalse(boot.Babble.IsBusy, "the tap cut the chatter");
                        stopped = true;
                        break;
                    }
                }
                else Input(boot);
                yield return null;
            }
            Assert.IsTrue(stopped);

            // the component itself: Stop frees the clip and goes silent at once
            var go = new GameObject("babble");
            var babble = go.AddComponent<PlayerBabble>();
            babble.Play("안녕 반가워");
            Assert.IsTrue(babble.IsPlaying);
            babble.Stop();
            Assert.IsFalse(babble.IsBusy);
            PlayerBabble.Setting = "off";
            babble.Play("안녕 반가워");
            Assert.IsFalse(babble.IsBusy, "off is silent");
            UnityEngine.Object.Destroy(go);
        }
    }
}
