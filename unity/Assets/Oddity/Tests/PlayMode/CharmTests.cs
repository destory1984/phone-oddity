using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UniVRM10;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>Harin charm pass 2: idle face and glitch in the phone, the act 2 framing, the closed smile.</summary>
    public class CharmTests
    {
        const string SceneName = "Prototype";
        const float Speed = 5f;

        static readonly ExpressionKey[] Mouth =
            { ExpressionKey.Aa, ExpressionKey.Ih, ExpressionKey.Ou, ExpressionKey.Ee, ExpressionKey.Oh };

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
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

        void Save(int day, Slot slot)
        {
            var state = new GameState { PlayerName = "민수", Day = day, Slot = slot, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        static IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        static HarinPuppet Harin() => HarinWardrobe.ActivePuppet();

        // No Harin line yet (the name prompt, then the system lines): a faint closed-mouth smile, breathing.
        [UnityTest]
        public IEnumerator AppStage_IdleFaceIsSoftSmile()
        {
            yield return LoadScene();
            var harin = Harin();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            Assert.IsTrue(chat.IsAskingName);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(HarinPuppet.IdleFace, harin.CurrentFace, "before any line");
            Assert.Greater(harin.ExpressionWeight(ExpressionKey.Relaxed), 0f);
            Assert.LessOrEqual(harin.ExpressionWeight(ExpressionKey.Relaxed), 0.45f, "only a faint smile");
            foreach (var m in Mouth) Assert.AreEqual(0f, harin.ExpressionWeight(m), m.ToString());
            Assert.AreEqual(HarinPuppet.SoftMouthClose, harin.MouthCloseWeight, 1e-4f, "lips held shut (the v58 face rests parted)");
            Assert.AreEqual(HarinPuppet.SoftMouthSmile, harin.MouthSmileWeight, 1e-4f, "mouth corners lifted");
            Assert.IsTrue(harin.Idle, "breathing and glances on in the phone");

            chat.SubmitName("민수"); // the first morning line is a system line
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual("system", chat.CurrentLineWho);
            Assert.AreEqual(HarinPuppet.IdleFace, harin.CurrentFace, "system line");
            Assert.Greater(harin.ExpressionWeight(ExpressionKey.Relaxed), 0f);
            foreach (var m in Mouth) Assert.AreEqual(0f, harin.ExpressionWeight(m), m.ToString());
        }

        // The first Harin line is a surprised "...보여?": eyes wide, but the mouth only a little open.
        [UnityTest]
        public IEnumerator Surprised_MouthSmall()
        {
            yield return LoadScene();
            var harin = Harin();
            harin.SetFace("surprised");
            for (int i = 0; i < 3; i++) yield return null;
            float eyes = harin.ExpressionWeight(ExpressionKey.Surprised);
            float open = eyes;
            foreach (var m in Mouth) open += harin.ExpressionWeight(m);
            Assert.Greater(eyes, 0f, "eyes wide");
            Assert.LessOrEqual(open, 0.5f, "mouth-opening morphs summed");
            Assert.Greater(harin.MouthCloseWeight, 0f, "lips drawn together");
        }

        // Day 1 morning: line 2 (glitch 0.2) stays calm; line 3 (glitch 0.7) flashes, then settles to a third or less.
        [UnityTest]
        public IEnumerator AppStage_GlitchCalmByDefault()
        {
            Assert.LessOrEqual(GameBootstrap.GlitchCalmScale, 0.09f + 1e-5f);
            for (float g = 0f; g <= 1f; g += 0.1f)
                Assert.LessOrEqual(GameBootstrap.CalmGlitch(g), GameBootstrap.MomentGlitch(g) / 3f + 1e-5f, "at " + g);

            Save(1, Slot.Morning);
            yield return LoadScene();
            var boot = Boot();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            var harin = Harin();

            yield return null; // line 1, the install notice, glitch 0.2
            Assert.IsFalse(boot.GlitchMomentActive);
            Assert.AreEqual(GameBootstrap.CalmGlitch(0.2f), chat.GlitchAmount, 1e-4f);
            Assert.LessOrEqual(chat.GlitchAmount, 0.2f / 3f + 1e-4f);

            chat.Tap(); // line 2, Harin, glitch 0.7: a moment
            yield return null;
            Assert.IsTrue(boot.GlitchMomentActive, "a 0.7 line flashes");
            float moment = chat.GlitchAmount;
            Assert.AreEqual(0.7f, moment, 1e-4f);
            Assert.AreEqual(moment, harin.Glitch, 1e-4f);

            yield return new WaitForSeconds(GameBootstrap.GlitchMomentSeconds + 0.3f);
            Assert.IsFalse(boot.GlitchMomentActive);
            Assert.LessOrEqual(chat.GlitchAmount, moment / 3f + 1e-4f, "settles after the moment");
            Assert.Greater(chat.GlitchAmount, 0f);
        }

        // Act 2: her eyes (Head + 7.5 cm) sit 30 to 40% down the screen, above the subtitle band, and stay there.
        [UnityTest]
        public IEnumerator Act2_FaceInUpperThird()
        {
            Save(CoreInfo.EmergenceDay, Slot.Night);
            yield return LoadScene();
            var boot = Boot();
            var seq = boot.Emergence;
            var cam = seq.MainCamera;
            cam.aspect = 9f / 16f;
            seq.Speed = Speed;
            seq.Play();
            float limit = Time.realtimeSinceStartup + seq.Duration / Speed + 5f;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit) yield return null;
            Assert.IsTrue(seq.HasFinished, "emergence did not finish");
            Assert.IsTrue(boot.InRoom);

            var harin = Harin();
            for (int frame = 0; frame < 3; frame++)
            {
                if (frame == 1) boot.Subtitle.Tap(); // the next act 2 line: the camera stays put
                yield return null;
                var eyes = harin.BonePosition(HumanBodyBones.Head) + Vector3.up * 0.075f * harin.transform.lossyScale.y;
                var vp = cam.WorldToViewportPoint(eyes);
                Assert.Greater(vp.z, 0f, "eyes in front of the camera");
                Assert.That(vp.y, Is.InRange(0.60f, 0.70f), $"eye line (0 bottom, 1 top) frame {frame}: {vp.y:F3}");
                Assert.That(vp.x, Is.InRange(0.3f, 0.7f), $"face roughly centred: {vp.x:F3}");
                Assert.LessOrEqual(cam.transform.position.y, eyes.y - 0.045f, "camera 5 to 10 cm under her eyes");
                Assert.GreaterOrEqual(cam.transform.position.y, eyes.y - 0.105f, "camera 5 to 10 cm under her eyes");
                // waist up fills about 55 to 65% of the height
                var spine = cam.WorldToViewportPoint(harin.BonePosition(HumanBodyBones.Spine));
                float top = cam.WorldToViewportPoint(eyes + Vector3.up * EmergenceSequence.Act2HeadTopAboveEye * harin.transform.lossyScale.y).y;
                Assert.That(top - spine.y, Is.InRange(0.5f, 0.7f), $"waist to head top share: {top - spine.y:F3}");
            }
            Assert.IsNotNull(seq.FaceLight, "6500 K face light in the room");
            Assert.IsTrue(seq.FaceLight.enabled);
        }

        // The closed-mouth smiles keep the happy preset (which parts the lips) at 0.03 or less.
        [UnityTest]
        public IEnumerator Smile_TeethHidden()
        {
            Assert.LessOrEqual(HarinPuppet.SmileHappy, 0.03f);
            Assert.LessOrEqual(HarinPuppet.FlusteredHappy, 0.03f);
            yield return LoadScene();
            var harin = Harin();
            harin.SetMouth(0f, 0f, 0f);
            foreach (var face in new[] { "smile", "flustered", HarinPuppet.IdleFace })
            {
                harin.SetFace(face);
                for (int i = 0; i < 2; i++) yield return null;
                Assert.AreEqual(face, harin.CurrentFace);
                Assert.Greater(harin.ExpressionWeight(ExpressionKey.Relaxed), 0.1f, face);
                Assert.LessOrEqual(harin.ExpressionWeight(ExpressionKey.Happy), 0.03f, face + ": happy parts the lips");
                foreach (var m in Mouth) Assert.AreEqual(0f, harin.ExpressionWeight(m), face + " " + m);
                Assert.Greater(harin.MouthCloseWeight, 0f, face + ": lips held shut");
            }
            // talking opens the mouth: the close morph fades out, and an open face does not use it
            harin.SetFace(HarinPuppet.IdleFace);
            harin.SetMouth(0.5f, 0f, 0f);
            yield return null;
            Assert.AreEqual(0f, harin.MouthCloseWeight, 1e-4f, "lip sync");
            Assert.AreEqual(0f, harin.MouthSmileWeight, 1e-4f, "lip sync");
            harin.SetMouth(0f, 0f, 0f);
            harin.SetFace("happy");
            yield return null;
            Assert.AreEqual(0f, harin.MouthCloseWeight, 1e-4f, "happy");
        }
    }
}
