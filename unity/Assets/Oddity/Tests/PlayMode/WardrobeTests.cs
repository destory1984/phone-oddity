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
    /// <summary>
    /// Harin's two outfits: indoor B2 for the day 12 prologue only, summer M1 for the phone, the emergence and act 2.
    /// The swap happens at the prologue start and while the rewind screen is black.
    /// </summary>
    public class WardrobeTests
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
            if (File.Exists(_savePath)) File.Delete(_savePath);
            string marker = _savePath + "." + GameBootstrap.PrologueSeenFile;
            if (File.Exists(marker)) File.Delete(marker);
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

        static int ActivePuppets() => UnityEngine.Object.FindObjectsByType<HarinPuppet>(FindObjectsSortMode.None).Length;

        [UnityTest]
        public IEnumerator Wardrobe_PrologueUsesIndoor()
        {
            yield return LoadScene();
            var boot = Boot();
            var w = boot.Wardrobe;
            Assert.IsNotNull(w, "the scene has a wardrobe");
            Assert.IsNotNull(w.Summer, "summer model");
            Assert.IsNotNull(w.Indoor, "indoor model");
            Assert.AreNotSame(w.Summer, w.Indoor);
            Assert.IsTrue(boot.InPrologue);

            // through the prologue up to the rewind: always B2, one model on screen
            int frames = 0;
            for (int i = 0; i < FrameLimit && !boot.Prologue.IsRewinding; i++)
            {
                Assert.AreEqual(HarinWardrobe.Outfit.Indoor, w.Wearing, "prologue frame " + i);
                Assert.AreSame(w.Indoor, boot.Harin);
                Assert.IsTrue(w.Indoor.gameObject.activeInHierarchy);
                Assert.IsFalse(w.Summer.gameObject.activeInHierarchy, "summer model hidden in the prologue");
                Assert.AreEqual(1, ActivePuppets());
                Input(boot);
                frames++;
                yield return null;
            }
            Assert.IsTrue(boot.Prologue.IsRewinding, "reached the rewind");
            Assert.Greater(frames, 10);
            Assert.AreSame(w.Indoor, HarinWardrobe.ActivePuppet());
            Assert.IsNotNull(w.Indoor.BlushMaskTexture, "B2 has its own cheek mask");
            Assert.AreNotSame(w.Summer.BlushMaskTexture, w.Indoor.BlushMaskTexture);
        }

        [UnityTest]
        public IEnumerator Wardrobe_PhoneUsesSummerAfterRewind()
        {
            yield return LoadScene();
            var boot = Boot();
            var w = boot.Wardrobe;
            for (int i = 0; i < FrameLimit && !boot.Prologue.IsRewinding; i++)
            {
                Input(boot);
                yield return null;
            }
            Assert.IsTrue(boot.Prologue.IsRewinding, "reached the rewind");
            var director = boot.Prologue;

            // still B2 until the screen is fully black, then B; one model all along
            bool swappedWhileVisible = false;
            float start = Time.time;
            while (boot.InPrologue && Time.time - start < PrologueDirector.RewindDuration + 2f)
            {
                Assert.AreEqual(1, ActivePuppets(), "one model at t " + director.RewindTime);
                if (w.Wearing == HarinWardrobe.Outfit.Summer && director.RewindTime < 2.2f - 0.001f) swappedWhileVisible = true;
                yield return null;
            }
            Assert.IsFalse(boot.InPrologue, "the rewind ends the prologue");
            Assert.IsFalse(swappedWhileVisible, "the outfit changed before the screen was black");
            yield return null;

            Assert.AreEqual(HarinWardrobe.Outfit.Summer, w.Wearing);
            Assert.AreSame(w.Summer, boot.Harin);
            Assert.AreSame(w.Summer, HarinWardrobe.ActivePuppet());
            Assert.IsTrue(w.Summer.gameObject.activeInHierarchy);
            Assert.IsFalse(w.Indoor.gameObject.activeInHierarchy, "B2 is off after the rewind");
            Assert.AreEqual(1, ActivePuppets());
            Assert.AreEqual(HarinPuppet.IdleFace, w.Summer.CurrentFace, "the phone idle face on B");
            Assert.AreEqual(0f, w.Summer.Blush, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Wardrobe_EmergenceKeepsSummer()
        {
            GameBootstrap.SkipPrologue = true;
            var state = new GameState { PlayerName = "민수", Day = CoreInfo.EmergenceDay, Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return LoadScene();
            var boot = Boot();
            var w = boot.Wardrobe;
            Assert.AreEqual(HarinWardrobe.Outfit.Summer, w.Wearing, "the phone starts in summer B");
            var seq = boot.Emergence;
            seq.Speed = 8f;
            seq.Play();
            float limit = Time.realtimeSinceStartup + seq.Duration / seq.Speed + 5f;
            int frames = 0;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit)
            {
                Assert.AreEqual(HarinWardrobe.Outfit.Summer, w.Wearing, "emergence at t " + seq.Time);
                Assert.IsFalse(w.Indoor.gameObject.activeInHierarchy);
                Assert.AreEqual(1, ActivePuppets());
                frames++;
                yield return null;
            }
            Assert.IsTrue(seq.HasFinished, "emergence did not finish");
            Assert.Greater(frames, 20);
            Assert.AreEqual(HarinWardrobe.Outfit.Summer, w.Wearing, "act 2 in the room keeps summer B");
            Assert.AreSame(w.Summer, boot.Harin);
            Assert.Less(Vector3.Distance(w.Summer.transform.position, seq.RoomAnchor.position), 0.05f, "B is the one in the room");
        }
    }
}
