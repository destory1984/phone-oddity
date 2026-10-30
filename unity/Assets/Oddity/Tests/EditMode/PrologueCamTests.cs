using System;
using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;

namespace Oddity.Tests
{
    /// <summary>Prologue camera rules (PrologueCamPlanner, PrologueShots) with a fake clock and the real prologue script.</summary>
    public class PrologueCamTests
    {
        static string FindPrologue()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    string a = Path.Combine(dir.FullName, "unity", "Assets", "StreamingAssets", "script", "prologue.json");
                    if (File.Exists(a)) return a;
                    string b = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "script", "prologue.json");
                    if (File.Exists(b)) return b;
                    dir = dir.Parent;
                }
            }
            Assert.Fail("prologue.json not found");
            return null;
        }

        static DayScript Script() => ScriptLoader.Parse(File.ReadAllText(FindPrologue()));

        static Line Harin(string beat = null, string cam = null) => new Line { Who = "harin", Text = "x", Beat = beat, Cam = cam };

        [Test]
        public void PCam_DinnerOpensWide()
        {
            var script = Script();
            var first = script.Scenes[0];
            Assert.AreEqual("p_dinner", first.Id);
            Assert.AreEqual("R", first.Lines[0].Cam, "the first dinner line asks for the master shot");
            var plan = new PrologueCamPlanner();
            plan.OnLine(first.Lines[0], 0f);
            Assert.AreEqual("R", plan.Shot, "the very first line starts on R at once");
            Assert.Greater(PrologueShots.Get("R").Width, PrologueShots.Get("B").Width);
            Assert.Greater(PrologueShots.Get("B").Width, PrologueShots.Get("P").Width);
        }

        [Test]
        public void PCam_InsertOnlyWhenSilent()
        {
            var plan = new PrologueCamPlanner();
            plan.OnLine(Harin("sets_table", "B"), 0f);
            plan.Tick(0f, false, false);
            float t = 1f;
            plan.OnLine(Harin("covers_plate", "I"), t);
            Assert.IsFalse(plan.InsertActive, "the insert does not start with the line");
            // her voice plays until 3.9 s: no insert
            for (; t < 3.95f; t += 0.1f)
            {
                plan.Tick(t, true, false);
                Assert.IsFalse(plan.InsertActive, "no insert while she speaks, t=" + t);
            }
            float ended = t - 0.1f; // the last tick with voice
            plan.Tick(ended + 0.1f, false, false);
            Assert.IsFalse(plan.InsertActive, "0.1 s after the voice");
            Assert.IsTrue(plan.Tick(ended + 0.25f, false, false), "0.2 s after the voice the insert starts");
            Assert.IsTrue(plan.InsertActive);
            float start = ended + 0.25f;
            // it lasts 1.2 to 1.8 s, then the face shot comes back
            plan.Tick(start + PrologueCamPlanner.InsertMin - 0.05f, false, false);
            Assert.IsTrue(plan.InsertActive, "still up at 1.15 s");
            plan.Tick(start + PrologueCamPlanner.InsertMax + 0.05f, false, false);
            Assert.IsFalse(plan.InsertActive, "gone by 1.85 s");
            Assert.That(PrologueCamPlanner.InsertSeconds, Is.InRange(1.2f, 1.8f));
            Assert.AreEqual("B", plan.Shot, "the face shot is the same as before");

            // her voice starting again ends an insert that is up
            plan = new PrologueCamPlanner();
            plan.OnLine(Harin("covers_plate", "I"), 0f);
            plan.Tick(0.1f, true, false); plan.Tick(1f, false, false); plan.Tick(1.3f, false, false);
            Assert.IsTrue(plan.InsertActive);
            plan.Tick(1.4f, true, false);
            Assert.IsFalse(plan.InsertActive, "never under her voice");
            // choices hold the insert back
            plan = new PrologueCamPlanner();
            plan.OnLine(Harin("covers_plate", "I"), 0f);
            plan.Tick(0.1f, true, false); plan.Tick(1f, false, true); plan.Tick(2f, false, true);
            Assert.IsFalse(plan.InsertActive, "no insert while choices are up");
            // a line with no voice file: the insert waits 2.5 s
            plan = new PrologueCamPlanner();
            plan.OnLine(Harin("covers_plate", "I"), 0f);
            plan.Tick(2.0f, false, false);
            Assert.IsFalse(plan.InsertActive, "no voice heard: not in the first 2.5 s");
            plan.Tick(2.6f, false, false);
            Assert.IsTrue(plan.InsertActive);
            // and in the script only the plate and the fingertips ask for one
            foreach (var scene in Script().Scenes)
                foreach (var line in scene.Lines)
                    if (PrologueShots.Canonical(line.Cam) == "I")
                        Assert.IsTrue(line.Beat == "covers_plate" || line.Beat == "fingertips_brush", scene.Id);
        }

        [Test]
        public void PCam_SpeakingFaceVisible()
        {
            var go = new GameObject("PCamTestCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = PrologueDirector.Fov;
                cam.aspect = 9f / 16f;
                float maxDistance = PrologueShots.DistanceFor(PrologueShots.Get("R").Width, PrologueDirector.Fov, cam.aspect);
                var plan = new PrologueCamPlanner();
                float now = 0f;
                int checkedLines = 0;
                foreach (var scene in Script().Scenes)
                {
                    bool standing = PrologueDirector.StageFor(scene.Id) == PrologueDirector.Stage.Standing;
                    var eyes = standing ? new Vector3(-0.45f, 1.45f, -0.6f) : new Vector3(-0.85f, 1.15f, -0.5f);
                    var facing = standing ? Quaternion.Euler(0f, 108f, 0f) * Vector3.forward : Vector3.right;
                    foreach (var line in scene.Lines)
                    {
                        now += 5f;
                        plan.OnLine(line, now);
                        plan.Settle(now);
                        if (line.Who != "harin" || plan.Legacy) continue;
                        Assert.IsFalse(plan.InsertActive, scene.Id + ": a speaking line is never an insert");
                        var pose = PrologueShots.PoseFor(eyes, facing, standing ? -1f : 1f, PrologueShots.Get(plan.Shot),
                            PrologueDirector.Fov, cam.aspect);
                        go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        float dist = Vector3.Distance(pose.position, eyes);
                        Assert.LessOrEqual(dist, maxDistance + 0.001f, scene.Id + " " + line.Beat + ": camera to head " + dist);
                        var vp = cam.WorldToViewportPoint(eyes);
                        var top = cam.WorldToViewportPoint(eyes + Vector3.up * 0.12f);
                        Assert.Greater(vp.z, 0f);
                        Assert.That(vp.x, Is.InRange(0.3f, 0.7f), scene.Id + " " + line.Beat + " eyes x " + vp.x);
                        Assert.That(vp.y, Is.InRange(0.55f, 0.8f), scene.Id + " " + line.Beat + " eyes y " + vp.y);
                        Assert.Less(top.y, 1f, scene.Id + " " + line.Beat + ": the top of her head is on the screen");
                        checkedLines++;
                    }
                }
                Assert.Greater(checkedLines, 15);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PCam_NoticeUnchanged()
        {
            // the notice and the rewind keep the old framing
            Assert.AreEqual(0.475f, PrologueDirector.NoticeEyeLine, 1e-6f);
            Assert.AreEqual(0.62f, PrologueDirector.NoticeDistance, 1e-6f);
            Assert.AreEqual(2.5f, PrologueDirector.RewindDuration, 1e-6f);
            foreach (var scene in Script().Scenes)
                foreach (var line in scene.Lines)
                    if (line.Beat == PrologueDirector.NoticeBeat || line.Beat == PrologueDirector.RewindBeat)
                        Assert.IsTrue(string.IsNullOrEmpty(line.Cam), scene.Id + ": the notice and the rewind have no cam");
            var plan = new PrologueCamPlanner();
            plan.OnLine(Harin("holds_eye_contact", "P"), 0f);
            Assert.IsFalse(plan.Legacy);
            plan.OnLine(new Line { Who = "system", Text = "x", Beat = PrologueDirector.NoticeBeat }, 5f);
            Assert.IsTrue(plan.Legacy, "the notice uses the old framing");
            Assert.AreEqual("P", plan.Shot, "and the shot is left as it was");
            plan.OnLine(Harin("holds_eye_contact"), 6f);
            Assert.IsTrue(plan.Legacy, "it stays old until the rewind is over");
        }

        [Test]
        public void PCam_OnlyBToPMoves()
        {
            Assert.AreEqual(3f, PrologueShots.MoveBetween("B", "P"));
            foreach (var a in new[] { "R", "B", "P", "T" })
                foreach (var b in new[] { "R", "B", "P", "T" })
                    if (!(a == "B" && b == "P")) Assert.AreEqual(0f, PrologueShots.MoveBetween(a, b), a + b);
        }

        [Test]
        public void PCam_CutWaitsForVoiceAndKeepsLatest()
        {
            var plan = new PrologueCamPlanner();
            plan.OnLine(Harin(null, "B"), 0f);
            plan.OnLine(Harin(null, "P"), 5f);
            plan.Tick(5f, true, false);
            Assert.AreEqual("B", plan.Shot, "no cut while her voice plays");
            plan.OnLine(Harin(null, "T"), 6f);          // a newer wish replaces the older one
            plan.Tick(6f, true, false);                  // her voice plays on at the line
            plan.Tick(6.1f, false, false);
            Assert.AreEqual("B", plan.Shot, "0.1 s after the voice");
            plan.Tick(6.3f, false, true);
            Assert.AreEqual("B", plan.Shot, "no cut while choices are up");
            Assert.IsTrue(plan.Tick(6.3f, false, false));
            Assert.AreEqual("T", plan.Shot);
        }
    }
}
