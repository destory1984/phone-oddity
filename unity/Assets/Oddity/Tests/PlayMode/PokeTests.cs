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
    /// <summary>Touching Harin's window in the phone: zones, the quiet states, and the script staying put.</summary>
    public class PokeTests
    {
        const string SceneName = "Prototype";

        string _savePath;
        DateTime _now;
        double _pokeClock;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            _pokeClock = 100;
            PokeController.ClockOverride = () => _pokeClock;
            var state = new GameState { PlayerName = "민수", Day = 1, Slot = Slot.Morning, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            ChatView.TypingEnabled = true;
            GameBootstrap.ClockOverride = null;
            PokeController.ClockOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        static IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();
        static HarinPuppet Harin() => HarinWardrobe.ActivePuppet();

        static Vector2 Viewport(float dx, float dy) =>
            PokeZones.ViewportOf(GameObject.Find("AppStageCamera").GetComponent<Camera>(), Harin(), dx, dy);

        // A touch 20 s after the last one (new stage 1 each time), with her voice stopped.
        static void Touch(Vector2 viewport, ref double clock)
        {
            clock += 20;
            Boot().Voice.Stop();
            Chat().PokeWindow(viewport);
        }

        [UnityTest]
        public IEnumerator Poke_HitZones()
        {
            yield return LoadScene();
            var poke = Boot().Poke;
            Assert.IsNotNull(poke, "poke.json loaded");
            var chat = Chat();

            // the window shows the top 760 of 1080 px of the square picture
            var rect = new Rect(-540f, -760f, 1080f, 760f);
            Assert.AreEqual(new Vector2(0.5f, 1f), ChatView.WindowToViewport(new Vector2(0f, 0f), rect));
            Assert.AreEqual(1f - 760f / 1080f, ChatView.WindowToViewport(new Vector2(-540f, -760f), rect).y, 1e-4f);

            Touch(Viewport(0f, 0.06f), ref _pokeClock);
            Assert.AreEqual(PokeHit.Head, poke.LastHit);
            Assert.AreEqual(1, poke.Reactions);
            Assert.AreEqual("head", poke.LastLine.Zone);
            Assert.IsTrue(Harin().PokeActive, "motion " + poke.LastLine.Motion);

            Touch(Viewport(0f, -0.04f), ref _pokeClock);
            Assert.AreEqual(PokeHit.Cheek, poke.LastHit);
            Assert.AreEqual(2, poke.Reactions);
            Assert.AreEqual("cheek", poke.LastLine.Zone);

            int bubbles = chat.BubbleCount;
            Touch(Viewport(0f, -0.18f), ref _pokeClock);
            Assert.AreEqual(PokeHit.None, poke.LastHit, "neck, shoulders, chest");
            Assert.AreEqual(2, poke.Reactions);
            Assert.AreEqual(bubbles, chat.BubbleCount, "the body does not react");

            Touch(Viewport(0.17f, 0f), ref _pokeClock);
            Assert.AreEqual(PokeHit.Window, poke.LastHit, "empty window beside her");
            Assert.AreEqual(3, poke.Reactions);
            Assert.AreEqual("window", poke.LastLine.Zone);
            Assert.AreEqual(bubbles + 1, chat.BubbleCount);
        }

        [UnityTest]
        public IEnumerator Poke_IgnoredWhileTypingOrChoices()
        {
            ChatView.TypingEnabled = true;
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            var poke = boot.Poke;
            for (int i = 0; i < 8 && !chat.IsTyping; i++) chat.Tap(); // past the system lines to her typing
            Assert.IsTrue(chat.IsTyping, "she is typing");
            int bubbles = chat.BubbleCount;
            Touch(Viewport(0f, 0.06f), ref _pokeClock);
            Assert.AreEqual(0, poke.Reactions);
            Assert.AreEqual(0, poke.EyeContacts);
            Assert.AreEqual(bubbles, chat.BubbleCount);
            Assert.IsTrue(chat.IsTyping, "the touch did not end the typing");

            ChatView.TypingEnabled = false;
            chat.Tap(); // show the held line
            for (int i = 0; i < 30 && chat.VisibleButtonTexts.Count == 0; i++) chat.Tap();
            Assert.Greater(chat.VisibleButtonTexts.Count, 0, "choices on screen");
            var state = boot.State;
            int stability = state.Stability, deviation = state.Deviation, heart = state.Heart;
            bubbles = chat.BubbleCount;
            Touch(Viewport(0f, 0.06f), ref _pokeClock);
            Assert.AreEqual(0, poke.Reactions, "no line while choices are up");
            Assert.AreEqual(1, poke.EyeContacts, "only a short eye contact");
            Assert.AreEqual(0, poke.PokesToday, "not counted");
            Assert.AreEqual(0, poke.Stage, "no stage");
            Assert.AreEqual(bubbles, chat.BubbleCount);
            Assert.Greater(chat.VisibleButtonTexts.Count, 0, "the choices stay");
            Assert.AreEqual(stability, state.Stability);
            Assert.AreEqual(deviation, state.Deviation);
            Assert.AreEqual(heart, state.Heart);
        }

        [UnityTest]
        public IEnumerator Poke_DoesNotAdvanceScript()
        {
            yield return LoadScene();
            var boot = Boot();
            var chat = Chat();
            var state = boot.State;
            chat.Tap();
            yield return null;
            string text = chat.CurrentLineText;
            string who = chat.CurrentLineWho;
            string scene = boot.CurrentSceneId;
            int stability = state.Stability, deviation = state.Deviation, heart = state.Heart;
            int bubbles = chat.BubbleCount;

            Touch(Viewport(0f, 0.06f), ref _pokeClock);
            Assert.AreEqual(1, boot.Poke.Reactions);
            Assert.AreEqual(bubbles + 1, chat.BubbleCount, "one reaction bubble");
            Assert.AreEqual(text, chat.CurrentLineText);
            Assert.AreEqual(who, chat.CurrentLineWho);
            Assert.AreEqual(scene, boot.CurrentSceneId);
            Assert.AreEqual(stability, state.Stability);
            Assert.AreEqual(deviation, state.Deviation);
            Assert.AreEqual(heart, state.Heart);
            Assert.AreEqual(1, state.PokesToday);

            chat.Tap(); // the script still waits for its tap and moves on from where it was
            yield return null;
            Assert.AreNotEqual(text, chat.CurrentLineText);

            // her face goes back to the idle smile 2.5 s after a reaction
            Touch(Viewport(0f, 0.06f), ref _pokeClock);
            string face = boot.Poke.LastLine.Face;
            Assert.AreNotEqual(HarinPuppet.IdleFace, Harin().CurrentFace, face);
            yield return new WaitForSeconds(PokeController.FaceSeconds + 0.3f);
            Assert.AreEqual(HarinPuppet.IdleFace, Harin().CurrentFace);
        }
    }
}
