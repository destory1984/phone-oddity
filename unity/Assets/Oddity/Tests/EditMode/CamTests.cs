using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;

namespace Oddity.Tests
{
    /// <summary>Messenger camera rules (CamPlanner) with a fake clock.</summary>
    public class CamTests
    {
        static Line Harin(string beat = null, string cam = null) => new Line { Who = "harin", Text = "x", Beat = beat, Cam = cam };

        static CamPlanner Start(Slot slot = Slot.Morning, int day = 2)
        {
            var p = new CamPlanner();
            p.BeginSlot(slot, day, 0f);
            return p;
        }

        [Test]
        public void Cam_SceneStartDefault()
        {
            Assert.AreEqual("M", Start(Slot.Morning).Shot);
            Assert.AreEqual("S", Start(Slot.Evening).Shot);
            Assert.AreEqual("C", Start(Slot.Night).Shot);
        }

        [Test]
        public void Cam_ShotHoldsMinimum()
        {
            var p = Start();
            float t = 1f;
            // three Harin lines at 2 s apart: a "teases" wish arrives but only 6 s have passed
            p.OnLine(Harin(), t); p.Tick(t, false, false); t += 2f;
            p.OnLine(Harin(), t); p.Tick(t, false, false); t += 2f;
            p.OnLine(Harin("teases"), t);
            Assert.IsFalse(p.Tick(t, false, false), "8 s have not passed");
            Assert.AreEqual("M", p.Shot);
            // 8 s passed but only two Harin lines
            p = Start();
            p.OnLine(Harin(), 5f); p.Tick(5f, false, false);
            p.OnLine(Harin("teases"), 9f);
            Assert.IsFalse(p.Tick(9f, false, false), "only 2 Harin lines");
            Assert.AreEqual("M", p.Shot);
            // both met: it changes at the line boundary
            p = Start();
            p.OnLine(Harin(), 3f); p.Tick(3f, false, false);
            p.OnLine(Harin(), 6f); p.Tick(6f, false, false);
            p.OnLine(Harin("teases"), 9f);
            Assert.IsTrue(p.Tick(9f, false, false));
            Assert.AreEqual("S", p.Shot);
        }

        [Test]
        public void Cam_NoCutDuringVoice()
        {
            var p = Start();
            p.OnLine(Harin(), 3f); p.Tick(3f, false, false);
            p.OnLine(Harin(), 6f); p.Tick(6f, false, false);
            p.OnLine(Harin("teases"), 9f);
            Assert.IsFalse(p.Tick(9f, true, false), "voice playing");
            Assert.IsFalse(p.Tick(11f, true, false), "voice still playing");
            Assert.AreEqual("M", p.Shot);
            Assert.IsFalse(p.Tick(11.1f, false, false), "ended 0.1 s ago");
            Assert.IsTrue(p.Tick(11.3f, false, false), "0.2 s after the voice ended");
            Assert.AreEqual("S", p.Shot);
        }

        [Test]
        public void Cam_NoCutWhileChoicesUp()
        {
            var p = Start();
            p.OnLine(Harin(), 3f); p.Tick(3f, false, false);
            p.OnLine(Harin(), 6f); p.Tick(6f, false, false);
            p.OnLine(Harin("teases"), 9f);
            Assert.IsFalse(p.Tick(10f, false, true));
            Assert.AreEqual("M", p.Shot);
        }

        [Test]
        public void Cam_ScriptCamOverrides()
        {
            var p = Start();
            // the cam field wins over the beat's candidate (S), and skips the minimum hold
            p.OnLine(Harin("teases", "E"), 1f);
            Assert.IsTrue(p.Tick(1f, false, false));
            Assert.AreEqual("E", p.Shot);
            // but the voice lock still wins
            p = Start();
            p.OnLine(Harin(null, "C"), 1f);
            Assert.IsFalse(p.Tick(1f, true, false));
            Assert.IsTrue(p.Tick(1.3f, false, false));
            Assert.AreEqual("C", p.Shot);
            // an unknown name is ignored
            p = Start();
            p.OnLine(Harin(null, "Z"), 1f);
            Assert.IsFalse(p.Tick(1f, false, false));
        }

        [Test]
        public void Cam_LatestWishOnly()
        {
            var p = Start();
            p.OnLine(Harin(), 3f); p.Tick(3f, false, false);
            p.OnLine(Harin(), 6f); p.Tick(6f, false, false);
            p.OnLine(Harin("teases"), 9f);
            p.Tick(9f, true, false);
            p.OnLine(Harin("waits_for_answer"), 9.5f);
            p.Tick(9.5f, true, false);
            Assert.IsTrue(p.Tick(11f, false, false));
            Assert.AreEqual("C", p.Shot);
            Assert.AreEqual(1, p.Changes);
        }

        [Test]
        public void Cam_FaceAloneDoesNotCut()
        {
            var p = Start();
            for (int i = 1; i <= 6; i++)
            {
                p.OnLine(new Line { Who = "harin", Face = "surprised" }, i * 3f);
                Assert.IsFalse(p.Tick(i * 3f, false, false));
            }
            Assert.AreEqual("M", p.Shot);
        }

        [Test]
        public void Cam_GlitchCutReturns()
        {
            var p = Start();
            p.GlitchCut(5f);
            Assert.AreEqual("G", p.Shot);
            Assert.IsFalse(p.Tick(5.5f, true, false));
            Assert.AreEqual("G", p.Shot);
            Assert.IsTrue(p.Tick(5.7f, true, false), "0.6 s later");
            Assert.AreEqual("M", p.Shot);
            p.GlitchCut(8f);
            p.OnLine(Harin(), 8.2f); // the next line comes first
            Assert.AreEqual("M", p.Shot);
            Assert.AreEqual(0, p.Changes, "the glitch cut is not a rule change");
        }

        [Test]
        public void Cam_MovesSmoothOnSameAxisOnly()
        {
            Assert.AreEqual(1.4f, CamShots.MoveBetween("M", "C"), 0.001f);
            Assert.AreEqual(1.4f, CamShots.MoveBetween("C", "M"), 0.001f);
            Assert.AreEqual(2.4f, CamShots.MoveBetween("C", "E"), 0.001f);
            Assert.AreEqual(0f, CamShots.MoveBetween("M", "S"), 0.001f);
            Assert.AreEqual(0f, CamShots.MoveBetween("C", "H"), 0.001f);
        }

        [Test]
        public void Cam_DriftStopsAtEnd()
        {
            var p = Start();
            float t = 0f;
            for (int i = 0; i < 400; i++) { t += 0.05f; p.Tick(t, false, false); }
            Assert.AreEqual(1f, p.Drift01, 0.001f);
            Assert.AreEqual(0.97f, p.DriftWidthScale, 0.001f);
            Assert.AreEqual(0f, p.DriftSlideMeters, 0.0001f, "one kind of drift at a time");
        }

        [Test]
        public void Cam_PresetsKeepPlanValues()
        {
            var m = CamShots.Get("M");
            Assert.AreEqual(0.62f, m.Width, 0.0001f);
            Assert.AreEqual(0.32f, m.EyeLine, 0.0001f);
            Assert.AreEqual(14f, CamShots.Get("S").Yaw, 0.0001f);
            Assert.AreEqual(-3f, CamShots.Get("Clow").Pitch, 0.0001f);
            Assert.AreEqual(2f, CamShots.Get("G").Roll, 0.0001f);
        }
    }
}
