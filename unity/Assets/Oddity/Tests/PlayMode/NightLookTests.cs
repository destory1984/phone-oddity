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

        // ---------------------------------------------------------------- hair and face (task 52)

        // Renders the window camera once and reads it back.
        static Texture2D Render(Camera cam)
        {
            var target = cam.targetTexture;
            var rt = target != null ? target : RenderTexture.GetTemporary(512, 512, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            cam.targetTexture = target;
            if (target == null) RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // The viewport rectangle (0..1, y up) of the renderers' bounds.
        static Rect ViewRect(Camera cam, System.Collections.Generic.IEnumerable<Renderer> renderers)
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var v = cam.WorldToViewportPoint(c);
                    x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
                    y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
                }
            }
            return Rect.MinMaxRect(Mathf.Clamp01(x0), Mathf.Clamp01(y0), Mathf.Clamp01(x1), Mathf.Clamp01(y1));
        }

        // Mean brightness (0..1) of the pixels in a viewport rectangle that pass the filter; their count in `n`.
        static float Mean(Texture2D tex, Rect r, Func<Color, bool> keep, out int n)
        {
            int x0 = Mathf.FloorToInt(r.xMin * tex.width), x1 = Mathf.CeilToInt(r.xMax * tex.width);
            int y0 = Mathf.FloorToInt(r.yMin * tex.height), y1 = Mathf.CeilToInt(r.yMax * tex.height);
            double sum = 0;
            n = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = tex.GetPixel(x, y);
                if (!keep(c)) continue;
                sum += c.grayscale;
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        [UnityTest]
        public IEnumerator NightLook_FaceIsTheBrightest()
        {
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.NightEnd, 4), "end line 4 (the face in the middle of the window)");
            for (int i = 0; i < 20; i++) yield return null;
            Assert.IsTrue(boot.Night.IsOn);
            var harin = boot.Harin;
            Assert.Greater(harin.NightHairMaterials, 0, "the hair materials were found");
            var cam = boot.Stage.Camera;
            var tex = Render(cam);

            Assert.IsTrue(BlushMask.FindSkin(harin.gameObject, out var faceRenderer, out _), "face renderer");
            var hairRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (var h in harin.NightHairRenderers) hairRenderers.Add(h.renderer);
            Assert.IsNotEmpty(hairRenderers, "hair renderers");

            // the face: the middle of the face mesh, from the eyes down to the chin
            var fr = ViewRect(cam, new[] { faceRenderer });
            var face = new Rect(fr.x + fr.width * 0.25f, fr.y + fr.height * 0.10f, fr.width * 0.5f, fr.height * 0.55f);
            // the crown: the top quarter of the hair, its teal pixels only (the dark backdrop is not hair)
            var hr = ViewRect(cam, hairRenderers);
            var crown = new Rect(hr.x, hr.yMax - hr.height * 0.25f, hr.width, hr.height * 0.25f);
            float faceMean = Mean(tex, face, c => true, out int faceN);
            float crownMean = Mean(tex, crown, c => c.g > c.r * 1.15f && c.b > c.r * 1.15f && c.grayscale > 0.12f, out int crownN);
            Debug.Log("NIGHTLOOK face " + faceMean.ToString("F3") + " (" + faceN + " px), crown " + crownMean.ToString("F3") + " (" + crownN + " px)");
            Assert.Greater(crownN, 50, "the crown has hair pixels to measure");
            Assert.Greater(faceMean, crownMean, "the face is brighter than the top of the head");
            UnityEngine.Object.Destroy(tex);
        }

        [UnityTest]
        public IEnumerator NightLook_RestoresHairMaterial()
        {
            SceneConti.SetNightForRun(false);
            yield return Start(3, Slot.Night);
            var boot = Boot();
            Assert.IsTrue(StepTo(SceneConti.NightEnd, 3), "end line 3");
            yield return null;
            Assert.IsFalse(boot.Night.IsOn);
            var harin = boot.Harin;
            string[] props = { "_MatcapColor", "_RimColor", "_EmissionColor", "_Color", "_ShadeColor" };
            var maskBefore = new System.Collections.Generic.List<uint>();
            var mats = new System.Collections.Generic.List<Material>();
            // sharedMaterials of an instantiated renderer are its own instances, the ones the look changes
            foreach (var r in harin.GetComponentsInChildren<Renderer>(true))
            {
                maskBefore.Add(r.renderingLayerMask);
                foreach (var m in r.sharedMaterials) if (m != null && (m.name.Contains("HAIR") || m.name.Contains("SKIN"))) mats.Add(m);
            }
            Assert.IsNotEmpty(mats, "hair and skin materials");
            Func<Color[][]> read = () =>
            {
                var all = new Color[mats.Count][];
                for (int k = 0; k < mats.Count; k++)
                {
                    all[k] = new Color[props.Length];
                    for (int i = 0; i < props.Length; i++) all[k][i] = mats[k].HasProperty(props[i]) ? mats[k].GetColor(props[i]) : Color.clear;
                }
                return all;
            };
            var first = read();

            SceneConti.SetNightForRun(true);
            yield return null;
            yield return null;
            Assert.IsTrue(boot.Night.IsOn, "night is on");
            Assert.IsTrue(harin.NightMaterialsOn);
            var during = read();
            bool changed = false;
            for (int k = 0; k < mats.Count && !changed; k++)
                for (int i = 0; i < props.Length; i++) if (during[k][i] != first[k][i]) { changed = true; break; }
            Assert.IsTrue(changed, "the night look changes the hair and skin colours");
            Assert.AreEqual(NightLook.HairLayerBit, boot.Night.RimLightLayers, "the rim lights only the hair");

            SceneConti.SetNightForRun(false);
            yield return null;
            yield return null;
            Assert.IsFalse(boot.Night.IsOn);
            Assert.IsFalse(harin.NightMaterialsOn);
            var after = read();
            for (int k = 0; k < mats.Count; k++)
                for (int i = 0; i < props.Length; i++)
                {
                    // colour properties go through a gamma/linear conversion on every Set and Get, so a round trip can differ in the last bit
                    var a = first[k][i]; var b = after[k][i];
                    Assert.That(Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a), Is.LessThan(0.001f),
                        mats[k].name + " " + props[i] + " is back: " + a + " vs " + b);
                }
            int idx = 0;
            foreach (var r in harin.GetComponentsInChildren<Renderer>(true)) Assert.AreEqual(maskBefore[idx++], r.renderingLayerMask, "lighting layer of " + r.name);
            Assert.AreEqual(boot.Night.RimDayLayers, boot.Night.RimLightLayers, "the rim light layers are back");
            Assert.AreEqual(1f, harin.BlushScale, "blush strength back");
        }
    }
}
