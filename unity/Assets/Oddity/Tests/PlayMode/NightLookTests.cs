using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Oddity.PlayTests
{
    /// <summary>The night look of the conti scenes (task 50): on inside them, every value put back outside, and the dev switch.</summary>
    public class NightLookTests
    {
        const string SceneName = "Prototype";

        string _savePath;
        DateTime _now;
        bool _hadNightPref;
        int _nightPref;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_nighttest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            HarinVoice.ResourceDirOverride = "NoSuchVoiceFolder/";
            SceneConti.SetLevelForRun(2);
            SceneConti.SetNightForRun(true);
            _hadNightPref = PlayerPrefs.HasKey(SceneConti.NightPref);
            _nightPref = PlayerPrefs.GetInt(SceneConti.NightPref, 1);
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
            SceneConti.ClearNightCache();
            if (_hadNightPref) PlayerPrefs.SetInt(SceneConti.NightPref, _nightPref); else PlayerPrefs.DeleteKey(SceneConti.NightPref);
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        IEnumerator Start(int day, Slot slot)
        {
            var state = new GameState { PlayerName = "민수", Day = day, Slot = slot, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static ChatView Chat() => UnityEngine.Object.FindFirstObjectByType<ChatView>();

        static bool StepTo(string scene, int line, int limit = 60)
        {
            var boot = Boot();
            var chat = Chat();
            for (int i = 0; i < limit; i++)
            {
                if (boot.CurrentSceneId == scene && boot.LineNumber == line) return true;
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
            }
            return boot.CurrentSceneId == scene && boot.LineNumber == line;
        }

        static Light KeyLight()
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.name == "AppStageKeyLight") return l;
            return null;
        }

        static Image ChatImage(ChatView chat, string name) => chat.transform.Find(name).GetComponent<Image>();

        // Everything the look touches, as it is now.
        struct Look
        {
            public Color Background, Ambient, KeyColor, Wallpaper, Veil, Area;
            public CameraClearFlags Clear;
            public UnityEngine.Rendering.AmbientMode AmbientMode;
            public bool KeyOn;
            public float KeyIntensity;
            public Quaternion KeyRotation;
        }

        static Look Take(GameBootstrap boot, ChatView chat)
        {
            var cam = boot.Stage.Camera;
            var key = KeyLight();
            var veil = chat.transform.Find("WallpaperVeil");
            return new Look
            {
                Background = cam.backgroundColor,
                Clear = cam.clearFlags,
                AmbientMode = RenderSettings.ambientMode,
                Ambient = RenderSettings.ambientLight,
                KeyOn = key.enabled,
                KeyColor = key.color,
                KeyIntensity = key.intensity,
                KeyRotation = key.transform.rotation,
                Wallpaper = ChatImage(chat, "Wallpaper").color,
                Veil = veil != null ? veil.GetComponent<Image>().color : Color.clear,
                Area = ChatImage(chat, "BottomArea").color,
            };
        }

        static void AssertSame(Look a, Look b, string what)
        {
            Assert.AreEqual(a.Background, b.Background, what + ": window background");
            Assert.AreEqual(a.Clear, b.Clear, what + ": clear flags");
            Assert.AreEqual(a.AmbientMode, b.AmbientMode, what + ": ambient mode");
            Assert.AreEqual(a.Ambient, b.Ambient, what + ": ambient light");
            Assert.AreEqual(a.KeyOn, b.KeyOn, what + ": key light on");
            Assert.AreEqual(a.KeyColor, b.KeyColor, what + ": key colour");
            Assert.AreEqual(a.KeyIntensity, b.KeyIntensity, what + ": key intensity");
            Assert.That(Quaternion.Angle(a.KeyRotation, b.KeyRotation), Is.LessThan(0.001f), what + ": key direction");
            Assert.AreEqual(a.Wallpaper, b.Wallpaper, what + ": wallpaper");
            Assert.AreEqual(a.Veil, b.Veil, what + ": veil");
            Assert.AreEqual(a.Area, b.Area, what + ": bottom area");
        }

        // The day look as the scene starts: the conti off, so nothing is applied yet.
        IEnumerator DayLook(Action<Look> got)
        {
            SceneConti.SetLevelForRun(0);
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsFalse(boot.Night.IsOn, "level 0: the look is off");
            got(Take(boot, Chat()));
            SceneConti.SetLevelForRun(2);
        }

        [UnityTest]
        public IEnumerator NightLook_OnInTheConti()
        {
            SceneConti.SetLevelForRun(3); // 40 degrees: the widest the backdrop has to cover
            yield return Start(3, Slot.Night);
            var boot = Boot();
            var chat = Chat();
            Assert.IsTrue(StepTo(SceneConti.Night, 2), "the cup line");
            yield return null;
            yield return null;
            var night = boot.Night;
            Assert.NotNull(night);
            Assert.IsTrue(night.IsOn, "on inside the conti scene");
            var cam = boot.Stage.Camera;
            Assert.AreEqual(NightLook.BackTop, cam.backgroundColor);
            Assert.AreEqual(NightLook.AmbientNight, RenderSettings.ambientLight);
            Assert.AreEqual(NightLook.FillIntensity, KeyLight().intensity, 0.0001f, "the stage key is only a fill");
            Assert.IsTrue(night.FaceLight.enabled && night.RimLight.enabled);
            Assert.IsTrue(chat.NightStyle, "dark chat");
            Assert.IsNotNull(night.Backdrop);
            Assert.IsTrue(night.Backdrop.activeInHierarchy);
            Assert.AreEqual(boot.Harin.gameObject.layer, night.Backdrop.layer, "drawn by the window camera");

            // yaw 40: the four corners of the picture land on the backdrop (it never shows the empty background)
            var plane = new Plane(night.Backdrop.transform.forward, night.Backdrop.transform.position);
            float half = NightLook.BackdropSizeMeters * 0.5f;
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
            {
                var ray = cam.ViewportPointToRay(corner);
                Assert.IsTrue(plane.Raycast(ray, out float t), "a corner ray reaches the backdrop plane");
                var local = night.Backdrop.transform.InverseTransformPoint(ray.GetPoint(t));
                Assert.That(Mathf.Abs(local.x * night.Backdrop.transform.lossyScale.x), Is.LessThan(half), "corner " + corner + " x");
                Assert.That(Mathf.Abs(local.y * night.Backdrop.transform.lossyScale.y), Is.LessThan(half), "corner " + corner + " y");
            }

            // the chat is dark and reads: bubble text against its fill (system line included) reaches 4.5
            Assert.GreaterOrEqual(NightLook.Contrast(NightLook.HarinText, NightLook.HarinBubble), 4.5f, "Harin bubble");
            Assert.GreaterOrEqual(NightLook.Contrast(NightLook.PlayerText, NightLook.PlayerBubble), 4.5f, "player bubble");
            Assert.GreaterOrEqual(NightLook.Contrast(NightLook.SystemText, NightLook.Over(NightLook.SystemBubble, NightLook.ChatBack)), 4.5f, "system line");
            Assert.GreaterOrEqual(NightLook.Contrast(NightLook.WaitingText, NightLook.Over(NightLook.ChatBottom, NightLook.ChatBack)), 4.5f, "waiting text");
            Assert.GreaterOrEqual(NightLook.Contrast(Color.white, NightLook.ChoiceButton), 4.5f, "choice button");
            Assert.GreaterOrEqual(NightLook.Contrast(Color.white, NightLook.FeelingButton), 4.5f, "feeling button");
            var bottom = ChatImage(chat, "BottomArea").color;
            Assert.AreEqual(NightLook.ChatBottom, bottom);
        }

        [UnityTest]
        public IEnumerator NightLook_RestoredAfterTheScene()
        {
            Look day = default;
            yield return DayLook(l => day = l);

            // 1. a new slot starts
            var boot = Boot();
            boot.JumpTo(3, Slot.Night);
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3");
            yield return null;
            Assert.IsTrue(boot.Night.IsOn);
            boot.JumpTo(3, Slot.Morning);
            yield return null;
            yield return null;
            Assert.IsFalse(boot.Night.IsOn, "a new slot ends it");
            Assert.IsFalse(boot.Night.Backdrop.activeSelf);
            Assert.IsFalse(Chat().NightStyle);
            AssertSame(day, Take(boot, Chat()), "after the slot change");

            // 2. switched off in the middle of the scene
            boot.JumpTo(3, Slot.Night);
            Assert.IsTrue(StepTo(SceneConti.Night, 5), "line 5");
            yield return null;
            Assert.IsTrue(boot.Night.IsOn);
            SceneConti.SetNightForRun(false);
            yield return null;
            yield return null;
            Assert.IsFalse(boot.Night.IsOn, "off puts the day back");
            AssertSame(day, Take(boot, Chat()), "after switching off");
            SceneConti.SetNightForRun(true);

            // 3. the emergence leaves the phone
            boot.JumpTo(3, Slot.Night);
            Assert.IsTrue(StepTo(SceneConti.NightEnd, 2), "end line 2");
            yield return null;
            Assert.IsTrue(boot.Night.IsOn, "on in the second scene too (no flicker between the two)");
            boot.PlayEmergenceNow();
            yield return null;
            Assert.IsFalse(boot.Night.IsOn, "the emergence starts in the day look");
            AssertSame(day, Take(boot, Chat()), "after the emergence starts");
        }

        [UnityTest]
        public IEnumerator NightLook_OffKeepsTheDayLook()
        {
            SceneConti.SetNightForRun(false);
            Look day = default;
            yield return DayLook(l => day = l);
            var boot = Boot();
            boot.JumpTo(3, Slot.Night);
            Assert.IsTrue(StepTo(SceneConti.NightEnd, 3), "end line 3");
            yield return null;
            Assert.IsTrue(boot.Stage.InConti, "the conti itself still plays");
            Assert.IsFalse(boot.Night.IsOn, "night look off");
            Assert.IsFalse(Chat().NightStyle);
            Assert.IsTrue(boot.Night.Backdrop == null || !boot.Night.Backdrop.activeSelf);
            AssertSame(day, Take(boot, Chat()), "in the conti with the look off");
        }

        [UnityTest]
        public IEnumerator NightLook_DevSwitchTakesHoldAtOnce()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsNotNull(boot.Dev, "the dev panel exists in the editor");
            Assert.IsTrue(StepTo(SceneConti.Night, 3), "line 3");
            yield return null;
            Assert.IsTrue(boot.Night.IsOn);
            boot.Dev.SwitchNight();
            Assert.IsFalse(SceneConti.NightOn);
            Assert.IsFalse(boot.Night.IsOn, "the switch works at once");
            boot.Dev.SwitchNight();
            Assert.IsTrue(SceneConti.NightOn);
            Assert.IsTrue(boot.Night.IsOn);
        }
    }
}
