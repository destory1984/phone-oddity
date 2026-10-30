using System.IO;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;

namespace Oddity.Tests
{
    public class VoiceFileTests
    {
        const string SceneId = "d1_morning";

        [Test]
        public void Voice_D1MorningLinesMatchFiles()
        {
            string dir = Path.Combine(Application.dataPath, "Oddity", "Audio", "Placeholder", "Resources", "Voice");
            if (!Directory.Exists(dir) || Directory.GetFiles(dir, "harin_" + SceneId + "_*_harinA.wav").Length == 0)
                Assert.Ignore("placeholder voice files are not in this checkout");

            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            Scene scene = ScriptLoader.Parse(json).Scenes.Find(s => s.Id == SceneId);
            Assert.NotNull(scene, SceneId);

            // Lines carrying the player's name have no recorded clip (the name is spoken live), so they are skipped.
            for (int i = 0; i < scene.Lines.Count; i++)
            {
                Line line = scene.Lines[i];
                string file = Path.Combine(dir, "harin_" + SceneId + "_" + (i + 1) + "_harinA.wav");
                bool recorded = line.Who == "harin" && !line.Text.Contains("{player}");
                Assert.AreEqual(recorded, File.Exists(file), "line " + (i + 1) + " (" + line.Who + ") vs " + file);
            }
        }
    }
}
