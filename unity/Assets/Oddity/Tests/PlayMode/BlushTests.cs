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
    /// <summary>Cheek blush: the mask covers the cheeks only, the pulse timing, and the face texture coming back.</summary>
    public class BlushTests
    {
        const string SceneName = "Prototype";
        string _savePath;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => now;
            var state = new GameState { PlayerName = "민수", Day = 1, Slot = Slot.Morning, NextOpenUtc = now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            ChatView.TypingEnabled = true;
            GameBootstrap.ClockOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        static IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static HarinPuppet Harin() => UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();

        static float Sample(Texture2D mask, Vector2 uv) => mask.GetPixelBilinear(uv.x, uv.y).r;

        [UnityTest]
        public IEnumerator Blush_OnlyCheeks()
        {
            yield return LoadScene();
            var harin = Harin();
            var mask = harin.BlushMaskTexture;
            var face = harin.BlushFace;
            Assert.IsNotNull(mask, "mask");
            Assert.IsNotNull(face, "face mesh readable");

            float cheekY = face.EyeY - BlushMask.CheekBelowEye;
            float left = Sample(mask, BlushMask.UvNear(face, BlushMask.CheekSide, cheekY));
            float right = Sample(mask, BlushMask.UvNear(face, -BlushMask.CheekSide, cheekY));
            float forehead = Sample(mask, BlushMask.UvNear(face, 0f, face.EyeY + 0.04f));
            float nose = Sample(mask, BlushMask.UvNear(face, 0f, face.EyeY - 0.025f));
            float chin = Sample(mask, BlushMask.UvNear(face, 0f, face.EyeY - 0.075f));

            // neck: the lowest SKIN vertex near the centre line (under the jaw, where the face meets the neck)
            int low = -1;
            foreach (int i in BlushMask.SkinVertices(face))
                if (Mathf.Abs(face.Positions[i].x) < 0.01f && (low < 0 || face.Positions[i].y < face.Positions[low].y)) low = i;
            Assert.GreaterOrEqual(low, 0, "neck vertex");
            float neck = Sample(mask, face.Uvs[low]);

            Debug.Log($"BLUSH mask cheeks {left:F2}/{right:F2} forehead {forehead:F3} nose {nose:F3} chin {chin:F3} neck {neck:F3}");
            Assert.Greater(left, 0.5f, "left cheek");
            Assert.Greater(right, 0.5f, "right cheek");
            Assert.Less(forehead, 0.05f, "forehead");
            Assert.Less(nose, 0.05f, "nose tip");
            Assert.Less(neck, 0.05f, "neck");
        }

        [UnityTest]
        public IEnumerator Blush_RisesAndFades()
        {
            yield return LoadScene();
            var harin = Harin();
            harin.SetFace("neutral");
            yield return null;
            Assert.AreEqual(0f, harin.Blush, "no blush before");
            harin.SetFace("flustered");
            float t0 = Time.time;
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(harin.Blush, 0.9f, "at 0.3 s");
            // the same face again (the prologue sets it every frame) does not restart it
            harin.SetFace("flustered");
            while (Time.time - t0 < 2.5f) yield return null;
            Assert.LessOrEqual(harin.Blush, 0.05f, "at 2.5 s");

            // the shy beats blush too, other lines do not
            Assert.IsTrue(HarinPuppet.WantsBlush("happy", "looks_down_smiling"));
            Assert.IsTrue(HarinPuppet.WantsBlush("smile", "quiet_confession"));
            Assert.IsTrue(HarinPuppet.WantsBlush("flustered", null));
            Assert.IsFalse(HarinPuppet.WantsBlush("happy", "teases"));
        }

        [UnityTest]
        public IEnumerator Blush_RestoresTexture()
        {
            yield return LoadScene();
            var harin = Harin();
            var mat = harin.BlushMaterial;
            Assert.IsNotNull(mat, "face SKIN material");
            var main = mat.GetTexture("_MainTex");
            var shade = mat.HasProperty("_ShadeTex") ? mat.GetTexture("_ShadeTex") : null;
            Assert.IsNotNull(main);
            Assert.AreSame(main, harin.BlushOriginalTexture);

            harin.PulseBlush();
            yield return new WaitForSeconds(0.5f);
            Assert.AreNotSame(main, mat.GetTexture("_MainTex"), "blushed copy in use");
            Assert.IsInstanceOf<RenderTexture>(mat.GetTexture("_MainTex"));
            // fade and glitch on top of it
            harin.SetFade(0.5f);
            harin.SetGlitch(0.5f);
            yield return null;
            Assert.IsInstanceOf<RenderTexture>(mat.GetTexture("_MainTex"), "still blushed while faded");
            harin.SetFade(1f);
            harin.SetGlitch(0f);

            float wait = HarinPuppet.BlushRise + HarinPuppet.BlushHold + HarinPuppet.BlushFall;
            yield return new WaitForSeconds(wait);
            Assert.AreEqual(0f, harin.Blush);
            Assert.AreSame(main, mat.GetTexture("_MainTex"), "base texture back");
            if (shade != null) Assert.AreSame(shade, mat.GetTexture("_ShadeTex"), "shade texture back");

            // a manual value works the same way
            harin.SetBlush(0.7f);
            Assert.IsInstanceOf<RenderTexture>(mat.GetTexture("_MainTex"));
            harin.SetBlush(0f);
            Assert.AreSame(main, mat.GetTexture("_MainTex"));
        }
    }
}
