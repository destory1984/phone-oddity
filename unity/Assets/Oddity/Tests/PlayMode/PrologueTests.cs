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

        static IEnumerator PlayTo(GameBootstrap boot, Func<bool> stop)
        {
            for (int i = 0; i < FrameLimit && !stop(); i++)
            {
                Input(boot);
                yield return null;
            }
        }

        static Vector3 EyesWorld(HarinPuppet harin) =>
            harin.BonePosition(HumanBodyBones.Head) + Vector3.up * 0.075f * harin.transform.lossyScale.y;

        [UnityTest]
        public IEnumerator Notice_FaceBelowBanner()
        {
            yield return LoadScene();
            var boot = Boot();
            yield return PlayTo(boot, () => boot.Prologue.CurrentBeat == PrologueDirector.NoticeBeat);
            Assert.AreEqual(PrologueDirector.NoticeBeat, boot.Prologue.CurrentBeat, "reached the notice line");
            var cam = boot.Prologue.Camera;
            cam.aspect = 9f / 16f;
            boot.Voice.Stop();
            boot.Prologue.SeekBeat(1f);
            for (int i = 0; i < 5; i++) yield return null;

            var harin = HarinWardrobe.ActivePuppet();
            Assert.IsTrue(boot.Prologue.IsNoticeVisible);
            float banner = boot.Prologue.NoticeBottomViewportY;
            var eyes = cam.WorldToViewportPoint(EyesWorld(harin));
            Assert.Less(eyes.y, banner, $"eyes {eyes.y:F3} under the card's lower edge {banner:F3}");
            Assert.That(1f - eyes.y, Is.InRange(0.44f, 0.51f), $"eyes from the top of the frame: {1f - eyes.y:F3}");
            // the bottom edge no lower than 8 cm under the collarbones
            var collar = (harin.BonePosition(HumanBodyBones.LeftShoulder) + harin.BonePosition(HumanBodyBones.RightShoulder)) * 0.5f;
            var low = cam.WorldToViewportPoint(collar - Vector3.up * 0.08f * harin.transform.lossyScale.y);
            var col = cam.WorldToViewportPoint(collar);
            Debug.Log($"Notice framing: eyes {1f - eyes.y:F3} from top, banner bottom {1f - banner:F3}, collar vp {col.y:F3}, collar-8cm vp {low.y:F3}");
            Assert.LessOrEqual(low.y, 0.005f, $"8 cm under the collarbones is at or below the frame edge: {low.y:F3}");

            // the next line keeps the card up; her eyes stay under it
            Input(boot);
            yield return null;
            Assert.IsTrue(boot.Prologue.IsNoticeVisible, "the card stays up on the next line");
            boot.Voice.Stop();
            boot.Prologue.SeekBeat(1f);
            for (int i = 0; i < 5; i++) yield return null;
            eyes = cam.WorldToViewportPoint(EyesWorld(harin));
            Assert.Less(eyes.y, boot.Prologue.NoticeBottomViewportY, $"eyes under the card on the next line: {eyes.y:F3}");
        }

        [UnityTest]
        public IEnumerator Rewind_UiClearsFirst()
        {
            yield return LoadScene();
            var boot = Boot();
            yield return PlayTo(boot, () => boot.Prologue.IsRewinding);
            Assert.IsTrue(boot.Prologue.IsRewinding, "reached the rewind");
            var d = boot.Prologue;
            var harin = HarinWardrobe.ActivePuppet();
            d.RewindSpeed = 0f;
            d.SeekRewind(0f);
            Assert.Greater(d.NoticeAlpha, 0.9f, "the card is up when the rewind starts");
            Assert.Greater(boot.Subtitle.Alpha, 0.9f, "the subtitle is up when the rewind starts");
            Assert.AreEqual(0f, d.NoiseAlpha, 1e-4f, "no noise at the start");
            string face = harin.CurrentFace;

            float noiseAt = -1f;
            for (float t = 0f; t <= PrologueDirector.RewindDuration; t += 0.01f)
            {
                d.SeekRewind(t);
                if (noiseAt < 0f && d.NoiseAlpha > 0f)
                {
                    noiseAt = t;
                    Assert.LessOrEqual(d.NoticeAlpha, 0.05f, $"card alpha when the noise starts at {t:F2}s");
                    Assert.LessOrEqual(boot.Subtitle.Alpha, 0.05f, $"subtitle alpha when the noise starts at {t:F2}s");
                }
                if (noiseAt >= 0f && t < noiseAt + 0.29f)
                    Assert.AreEqual(face, harin.CurrentFace, $"her face holds in the first 0.3 s of noise (t {t:F2})");
            }
            Assert.That(noiseAt, Is.InRange(0.25f, 0.35f), "noise starts after the 0.3 s fade");
            // the noise is short and weaker: 60% of the old 1.7 s, 70% strength
            Assert.AreEqual(1.02f, PrologueDirector.RewindNoiseEnd - PrologueDirector.RewindNoiseStart, 0.01f);
            float peak = 0f;
            for (float t = 0f; t <= PrologueDirector.RewindDuration; t += 0.01f) { d.SeekRewind(t); peak = Mathf.Max(peak, d.NoiseAlpha); }
            Assert.LessOrEqual(peak, 0.85f * 0.55f * 0.7f + 0.01f, "noise at 70% of the old peak");
            d.SeekRewind(1.6f);
            Assert.Less(d.NoiseAlpha, 0.01f, "the noise is over before the screen is fully black");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Wardrobe_SwapOnlyWhenBlack()
        {
            yield return LoadScene();
            var boot = Boot();
            var w = boot.Wardrobe;
            yield return PlayTo(boot, () => boot.Prologue.IsRewinding);
            Assert.IsTrue(boot.Prologue.IsRewinding, "reached the rewind");
            var d = boot.Prologue;
            Assert.AreEqual(HarinWardrobe.Outfit.Indoor, w.Wearing);

            float blackAtSwap = -1f;
            var wearingAtSwap = HarinWardrobe.Outfit.Indoor;
            Action onCovered = () => { blackAtSwap = d.BlackAlpha; wearingAtSwap = w.Wearing; };
            d.RewindCovered += onCovered;
            bool swappedUncovered = false;
            float start = Time.time;
            while (boot.InPrologue && Time.time - start < PrologueDirector.RewindDuration + 2f)
            {
                if (w.Wearing == HarinWardrobe.Outfit.Summer && d.BlackAlpha < 0.99f) swappedUncovered = true;
                yield return null;
            }
            Assert.IsFalse(boot.InPrologue, "the rewind ends the prologue");
            Assert.GreaterOrEqual(blackAtSwap, 0.99f, "black cover alpha on the frame the outfit changes");
            Assert.AreEqual(HarinWardrobe.Outfit.Summer, wearingAtSwap, "the outfit changed at the covered moment");
            Assert.IsFalse(swappedUncovered, "never the summer outfit under a see-through cover");
        }

        [UnityTest]
        public IEnumerator Dinner_EyesOpenWhenLaughing()
        {
            yield return LoadScene();
            var boot = Boot();
            yield return PlayTo(boot, () => boot.PrologueSceneId == "p_dinner" && boot.Prologue.CurrentBeat == "teases");
            Assert.AreEqual("teases", boot.Prologue.CurrentBeat, "reached the teasing line");
            boot.Voice.Stop();
            boot.Prologue.SeekBeat(0.8f);
            for (int i = 0; i < 5; i++) yield return null;

            var harin = HarinWardrobe.ActivePuppet();
            Assert.AreEqual("happy", harin.CurrentFace);
            Assert.LessOrEqual(harin.SmileEyeClosure, 0.25f, $"eye-closing smile morphs: {harin.SmileEyeClosure:F3}");
            Assert.LessOrEqual(harin.ExpressionWeight(UniVRM10.ExpressionKey.Happy), 0.15f, "happy preset (Fcl_ALL_Joy) kept low");
            Assert.Greater(harin.MouthJoyWeight + harin.MouthSmileWeight, 0.3f, "the mouth still smiles");
            Assert.Greater(harin.MouthCloseWeight, 0.3f, "lips half together");
            if (harin.HasMouthInsideMaterial)
                Assert.That(harin.MouthInsideBrightness, Is.InRange(0.75f, 0.85f), "mouth inside 15 -> 25% darker");
            Debug.Log($"Dinner laugh: eye closure {harin.SmileEyeClosure:F3}, happy {harin.ExpressionWeight(UniVRM10.ExpressionKey.Happy):F3}, " +
                      $"mouth joy {harin.MouthJoyWeight:F3}, smile {harin.MouthSmileWeight:F3}, close {harin.MouthCloseWeight:F3}");
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
