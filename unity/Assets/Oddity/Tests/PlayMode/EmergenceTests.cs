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
    /// <summary>The emergence (Harin comes out of the phone) and the act 2 cut, run 5x faster.</summary>
    public class EmergenceTests
    {
        const string SceneName = "Prototype";
        const float Speed = 5f;

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.ClockOverride = () => _now;
            var state = new GameState { PlayerName = "민수", Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.ClockOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();

        IEnumerator LoadAndPlay()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var seq = Boot().Emergence;
            seq.Speed = Speed;
            seq.Play();
        }

        IEnumerator WaitFinished(EmergenceSequence seq)
        {
            float limit = Time.realtimeSinceStartup + seq.Duration / Speed + 5f;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit) yield return null;
            Assert.IsTrue(seq.HasFinished, "emergence did not finish");
        }

        [UnityTest]
        public IEnumerator Emergence_HarinNeverHidden()
        {
            yield return LoadAndPlay();
            var seq = Boot().Emergence;
            var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
            int id = harin.GetInstanceID();
            var renderers = harin.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0);

            int bad = 0, samples = 0;
            float nextSample = 0f;
            float limit = Time.realtimeSinceStartup + seq.Duration / Speed + 5f;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit)
            {
                // every frame is checked; samples counts the 0.1 s (sequence time) marks passed
                var all = UnityEngine.Object.FindObjectsByType<HarinPuppet>(FindObjectsSortMode.None);
                bool ok = all.Length == 1 && all[0].GetInstanceID() == id && harin.gameObject.activeInHierarchy;
                foreach (var r in renderers) ok &= r != null && r.enabled && r.gameObject.activeInHierarchy;
                if (!ok) bad++;
                if (seq.Time >= nextSample) { samples++; nextSample += 0.1f; }
                yield return null;
            }
            Assert.IsTrue(seq.HasFinished);
            Assert.AreEqual(0, bad, "frames where Harin was hidden or replaced");
            Assert.GreaterOrEqual(samples, 50);
        }

        [UnityTest]
        public IEnumerator Emergence_EndsInRoom()
        {
            yield return LoadAndPlay();
            var seq = Boot().Emergence;
            yield return WaitFinished(seq);
            yield return null;

            var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
            Assert.Less(Vector3.Distance(harin.transform.position, seq.RoomAnchor.position), 0.05f);

            var cam = seq.MainCamera.transform;
            var phone = GameObject.Find("Phone").transform;
            Assert.Greater(Vector3.Distance(cam.position, phone.position), 2f, "camera left the phone");
            Assert.That(cam.position.x, Is.InRange(-2f, 2f));
            Assert.That(cam.position.z, Is.InRange(-2.5f, 2.5f));
            var toHarin = (harin.transform.position + Vector3.up * 0.9f - cam.position).normalized;
            Assert.Greater(Vector3.Dot(cam.forward, toHarin), 0.95f, "camera looks at Harin in the room");
            Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<ChatView>().IsVisible, "phone UI hidden");
        }

        [UnityTest]
        public IEnumerator Emergence_FinishedStartsAfterScene()
        {
            // play the real night to its end: the bootstrap starts the emergence by itself
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var boot = Boot();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            boot.Emergence.Speed = Speed;
            for (int i = 0; i < 80 && !boot.Emergence.IsPlaying; i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }
            Assert.IsTrue(boot.Emergence.IsPlaying, "night end did not play the emergence");
            Assert.AreNotEqual(GameBootstrap.AfterSceneId, boot.CurrentSceneId);

            yield return WaitFinished(boot.Emergence);
            Assert.AreEqual(GameBootstrap.AfterSceneId, boot.CurrentSceneId);
            Assert.IsTrue(boot.InRoom);
            Assert.IsTrue(boot.Subtitle.IsVisible);
            Assert.IsNotEmpty(boot.Subtitle.CurrentText);
        }

        [UnityTest]
        public IEnumerator After_FadeDipsAndRecovers()
        {
            yield return LoadAndPlay();
            var boot = Boot();
            yield return WaitFinished(boot.Emergence);
            var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
            Assert.AreEqual(1f, harin.Fade, 0.001f);

            for (int i = 0; i < 10 && Mathf.Abs(harin.Glitch - 0.6f) > 0.01f; i++)
            {
                boot.Subtitle.Tap();
                yield return null;
            }
            Assert.AreEqual(0.6f, harin.Glitch, 0.01f, "reached the line before the name");
            Assert.AreEqual(0.4f, harin.Fade, 0.05f);

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0.4f, harin.Fade, 0.05f, "still dimmed half way");
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(1f, harin.Fade, 0.001f, "back to opaque after 1 s");
        }
    }
}
