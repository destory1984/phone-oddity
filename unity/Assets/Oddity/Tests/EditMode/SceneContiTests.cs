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
                for (int level = 1; level <= 3; level++)
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
        }
    }
}
