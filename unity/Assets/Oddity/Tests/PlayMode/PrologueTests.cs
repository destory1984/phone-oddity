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

        [Test]
        public void Voice_DefaultIsNotKarina()
        {
            Assert.AreEqual("harinA", HarinVoice.DefaultVoiceSet);
            Assert.AreEqual("karina", HarinVoice.VoiceSets[HarinVoice.VoiceSets.Length - 1], "karina is last in the list");
            Assert.AreEqual(HarinVoice.DefaultVoiceSet, HarinVoice.VoiceSets[0]);
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
        public IEnumerator Closeup_FaceInUpperHalf()
        {
            yield return LoadScene();
            var boot = Boot();
            for (int i = 0; i < FrameLimit && !(boot.PrologueSceneId == "p_side_by_side" && boot.Prologue.CurrentBeat == "quiet_confession"); i++)
            {
                Input(boot);
                yield return null;
            }
            Assert.AreEqual("quiet_confession", boot.Prologue.CurrentBeat, "reached the close-up line");
            boot.Voice.Stop();
            boot.Prologue.SeekBeat(1f);
            for (int i = 0; i < 5; i++) yield return null;

            var cam = boot.Prologue.Camera;
            cam.aspect = 9f / 16f;
            var harin = HarinWardrobe.ActivePuppet();
            // the VRoid model has no eye bones mapped; the director uses Head + 7.5 cm for her eyes, and so does this test.
            // The Head bone itself sits at the top of the neck, about 0.19 of the frame under the eyes.
            var headWorld = harin.BonePosition(HumanBodyBones.Head);
            var eyesWorld = headWorld + Vector3.up * 0.075f * harin.transform.lossyScale.y;
            var head = cam.WorldToViewportPoint(headWorld);
            var eyes = cam.WorldToViewportPoint(eyesWorld);
            Assert.Greater(head.z, 0f, "her head is in front of the camera");
            // Task 25: EyeLine 0.35 -> 0.28 so the v58 bangs and crown cover less of the frame; her eyes now sit
            // 0.25 -> 0.35 from the top (was 0.30 -> 0.45) and more neck and collar show at the bottom.
            Assert.That(eyes.y, Is.InRange(0.65f, 0.75f), $"eye line on screen (0 bottom, 1 top): {eyes.y:F3}");
            Assert.That(head.y, Is.InRange(0.45f, 0.62f), $"Head bone (top of the neck) on screen: {head.y:F3}");
            Assert.That(head.x, Is.InRange(0.3f, 0.7f), "head roughly centred");
            Assert.LessOrEqual(cam.transform.position.y, eyesWorld.y + 0.005f, "the camera is not above her eyes");
        }

        [UnityTest]
        public IEnumerator Smile_MouthClosed()
        {
            GameBootstrap.SkipPrologue = true;
            yield return LoadScene();
            var harin = HarinWardrobe.ActivePuppet();
            harin.SetMouth(0f, 0f, 0f);
            harin.SetFace("smile");
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual("smile", harin.CurrentFace);
            Assert.Greater(harin.ExpressionWeight(UniVRM10.ExpressionKey.Relaxed), 0.4f);
            Assert.LessOrEqual(harin.ExpressionWeight(UniVRM10.ExpressionKey.Happy), 0.3f, "happy kept low so the mouth stays shut");
            foreach (var mouth in new[] { UniVRM10.ExpressionKey.Aa, UniVRM10.ExpressionKey.Ih, UniVRM10.ExpressionKey.Ou,
                         UniVRM10.ExpressionKey.Ee, UniVRM10.ExpressionKey.Oh })
                Assert.AreEqual(0f, harin.ExpressionWeight(mouth), mouth.ToString());

            // "happy" lines show the smile unless the beat is a laugh
            Assert.AreEqual("smile", HarinPuppet.FaceForBeat("happy", "quiet_confession"));
            Assert.AreEqual("smile", HarinPuppet.FaceForBeat("happy", null));
            Assert.AreEqual("happy", HarinPuppet.FaceForBeat("happy", "laughs"));
            Assert.AreEqual("happy", HarinPuppet.FaceForBeat("happy", "teases"));
            Assert.AreEqual("relaxed", HarinPuppet.FaceForBeat("relaxed", "laughs"));
        }

        [UnityTest]
        public IEnumerator Voice_SetSwitch_CyclesPresentSets()
        {
            string old = HarinVoice.VoiceSet;
            try
            {
                HarinVoice.VoiceSet = "karina";
                var seen = new System.Collections.Generic.List<string>();
                for (int i = 0; i < HarinVoice.VoiceSets.Length; i++)
                {
                    HarinVoice.VoiceSet = HarinVoice.NextVoiceSet();
                    seen.Add(HarinVoice.VoiceSet);
                }
                // with every set's files present the button visits them all in order and comes back to karina
                bool all = true;
                foreach (var set in HarinVoice.VoiceSets) all &= HarinVoice.HasVoiceSet(set);
                if (all)
                    CollectionAssert.AreEqual(new[] { "harinA", "harinB", "harinC", "harinD", "karina" }, seen);
                foreach (var set in seen)
                    CollectionAssert.Contains(HarinVoice.VoiceSets, set);
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
            var harin = HarinWardrobe.ActivePuppet();
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
