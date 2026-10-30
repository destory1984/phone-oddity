using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;

namespace Oddity.Tests
{
    public class SceneContiTests
    {
        [Test]
        public void SceneConti_KeysMatchDay3Script()
        {
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day3.json"));
            DayScript script = ScriptLoader.Parse(json);
            Assert.AreEqual(15, SceneConti.Speakers.Count, "15 lines of the two scenes");
            foreach (var kv in SceneConti.Speakers)
            {
                string[] parts = kv.Key.Split(':');
                string sceneId = parts[0];
                int number = int.Parse(parts[1]);
                Scene scene = script.Scenes.Find(s => s.Id == sceneId);
                Assert.NotNull(scene, "scene " + sceneId + " is in day3.json");
                Assert.That(number, Is.InRange(1, scene.Lines.Count), kv.Key + " is a line of the scene");
                string who = scene.Lines[number - 1].Who;
                string expected = kv.Value == 'p' ? "player" : kv.Value == 's' ? "system" : "harin";
                Assert.AreEqual(expected, who, kv.Key + " speaker");
                for (int level = 1; level <= SceneConti.MaxLevel; level++)
                    Assert.IsTrue(SceneConti.TryGet(sceneId, number, level, out _), kv.Key + " has a cue at strength " + level);
            }
            // every line of both scenes is in the table
            foreach (string id in new[] { SceneConti.Night, SceneConti.NightEnd })
            {
                Scene scene = script.Scenes.Find(s => s.Id == id);
                for (int n = 1; n <= scene.Lines.Count; n++)
                    Assert.IsTrue(SceneConti.Speakers.ContainsKey(SceneConti.Key(id, n)), id + " line " + n);
            }
            Assert.IsFalse(SceneConti.TryGet(SceneConti.Night, 9, 2, out _));
            Assert.IsFalse(SceneConti.TryGet("d3_morning", 1, 2, out _));
            Assert.IsFalse(SceneConti.TryGet(SceneConti.Night, 1, 0, out _), "off has no cues");
            Assert.IsFalse(SceneConti.TryGet(SceneConti.Night, 1, SceneConti.MaxLevel + 1, out _), "no strength above the last");
        }

        // The close strength 4 (task 50): the same table with its own numbers.
        [Test]
        public void SceneConti_CloseLevelHasItsOwnNumbers()
        {
            Assert.AreEqual(4, SceneConti.MaxLevel);
            Assert.AreEqual("근", SceneConti.LevelName(4));
            ContiCue Cue(string scene, int line) { Assert.IsTrue(SceneConti.TryGet(scene, line, 4, out var c), scene + ":" + line); return c; }

            var l = Cue(SceneConti.Night, 1);
            Assert.AreEqual(0.42f, l.Start.Width, 0.0001f, "L");
            Assert.AreEqual(8f, l.Start.Yaw, 0.0001f);
            var side = Cue(SceneConti.Night, 2);
            Assert.AreEqual(0.26f, side.Start.Width, 0.0001f, "side face");
            Assert.AreEqual(32f, side.Start.Yaw, 0.0001f);
            Assert.AreEqual(side.Start.Width, side.End.Width, 0.0001f, "no push on the side face");
            var h2 = Cue(SceneConti.Night, 5);
            Assert.AreEqual(0.34f, h2.Start.Width, 0.0001f, "H2");
            Assert.AreEqual(h2.Start.Width, h2.End.Width, 0.0001f, "no push on H2");
            Assert.AreEqual(0.40f, Cue(SceneConti.Night, 7).Start.Width, 0.0001f, "M");
            var climax = Cue(SceneConti.NightEnd, 3);
            Assert.AreEqual(0.30f, climax.End.Width, 0.0001f, "the climax push ends at 0.30");
            Assert.AreEqual(3.0f, climax.MoveSeconds, 0.0001f);
            Assert.AreEqual(0f, climax.Overshoot, 0.0001f, "no overshoot");
            var last = Cue(SceneConti.NightEnd, 7);
            Assert.AreEqual(0.26f, last.End.Width, 0.0001f, "the last push ends at 0.26");
            Assert.AreEqual(4.0f, last.MoveSeconds, 0.0001f);
            // every eye line keeps the face inside the window (the head may be cut at the top)
            foreach (var c in new[] { l.Start, side.Start, h2.Start, Cue(SceneConti.Night, 7).Start, climax.End, last.End })
                Assert.That(c.EyeLine, Is.InRange(0.30f, 0.46f), "eye line " + c.EyeLine);
            // the other strengths keep their numbers
            Assert.AreEqual(0.30f, SceneConti.SideShot(2).Width, 0.0001f);
            Assert.AreEqual(0.40f, SceneConti.ClimaxEnd(3).Width, 0.0001f);
            Assert.AreEqual(2.7f, SceneConti.FinalPush(3), 0.0001f);
        }

        [Test]
        public void SceneConti_StrengthCyclesThroughFour()
        {
            try
            {
                SceneConti.SetLevelForRun(3);
                Assert.AreEqual(4, SceneConti.NextLevel());
                SceneConti.SetLevelForRun(4);
                Assert.AreEqual(0, SceneConti.NextLevel(), "close goes back to off");
                SceneConti.SetLevelForRun(9);
                Assert.AreEqual(4, SceneConti.Level, "clamped to the last strength");
            }
            finally { SceneConti.ClearLevelCache(); }
        }
    }
}
