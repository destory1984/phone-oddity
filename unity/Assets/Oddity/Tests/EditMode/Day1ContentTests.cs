using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oddity.Core;

namespace Oddity.Tests
{
    /// <summary>
    /// Checks the day 1 script content. Uses no UnityEngine API so the dotnet harness can run it too.
    /// </summary>
    public class Day1ContentTests
    {
        private const string WarmLine = "거기 따뜻해 보여";

        private static readonly HashSet<string> KnownFaces = new HashSet<string>
        {
            "neutral", "happy", "sad", "angry", "surprised", "relaxed"
        };

        private static string FindDay1Path()
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (string start in starts)
            {
                DirectoryInfo dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    string fromRepo = Path.Combine(dir.FullName, "unity", "Assets", "StreamingAssets", "script", "day1.json");
                    if (File.Exists(fromRepo)) return fromRepo;
                    string fromUnity = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "script", "day1.json");
                    if (File.Exists(fromUnity)) return fromUnity;
                    dir = dir.Parent;
                }
            }
            Assert.Fail("day1.json not found. Looked for unity/Assets/StreamingAssets/script/day1.json above "
                        + Directory.GetCurrentDirectory() + " and " + AppContext.BaseDirectory + ".");
            return null;
        }

        private static DayScript Load()
        {
            return ScriptLoader.Parse(File.ReadAllText(FindDay1Path()));
        }

        [Test]
        public void Day1_Loads()
        {
            string json = File.ReadAllText(FindDay1Path());
            Assert.DoesNotThrow(() => ScriptLoader.Parse(json));
        }

        [Test]
        public void Day1_HasEntryForEachSlot()
        {
            DayScript script = Load();
            Assert.IsNotNull(script.EntryFor(Slot.Morning), "morning entry");
            Assert.IsNotNull(script.EntryFor(Slot.Evening), "evening entry");
            Assert.IsNotNull(script.EntryFor(Slot.Night), "night entry");
        }

        [Test]
        public void Day1_FacesAreKnown()
        {
            DayScript script = Load();
            foreach (Scene scene in script.Scenes)
            {
                foreach (Line line in scene.Lines)
                {
                    Assert.IsTrue(KnownFaces.Contains(line.Face),
                        "Scene \"" + scene.Id + "\" uses unknown face \"" + line.Face + "\".");
                }
            }
        }

        [Test]
        public void Day1_HasFeelingAndTask()
        {
            DayScript script = Load();
            Scene feeling = script.Get("d1_evening_feeling");
            Scene task = script.Get("d1_night_task");
            Assert.IsNotNull(feeling, "d1_evening_feeling");
            Assert.IsNotNull(task, "d1_night_task");
            Assert.AreEqual("feeling", feeling.Type);
            Assert.AreEqual("task", task.Type);
            Assert.AreEqual(3, task.Choices.Count);
        }

        [Test]
        public void Day1_NightEndsWithWarmLine()
        {
            DayScript script = Load();
            Scene entry = script.EntryFor(Slot.Night);
            Assert.IsNotNull(entry, "night entry");

            var visited = new HashSet<string>();
            var stack = new Stack<Scene>();
            stack.Push(entry);
            int terminals = 0;
            while (stack.Count > 0)
            {
                Scene scene = stack.Pop();
                if (!visited.Add(scene.Id)) continue;
                Assert.AreNotEqual("d1_after", scene.Id, "d1_after must not be reachable from the night entry.");

                bool endsHere = scene.Choices.Count == 0;
                foreach (Choice choice in scene.Choices)
                {
                    if (string.IsNullOrEmpty(choice.Next))
                    {
                        endsHere = true;
                        continue;
                    }
                    stack.Push(script.Get(choice.Next));
                }

                if (endsHere)
                {
                    terminals++;
                    Assert.IsNotEmpty(scene.Lines, "Terminal scene \"" + scene.Id + "\" has no lines.");
                    Assert.AreEqual(WarmLine, scene.Lines[scene.Lines.Count - 1].Text,
                        "Night path ends in \"" + scene.Id + "\" without the warm line.");
                }
            }
            Assert.Greater(terminals, 0, "Night never ends.");
        }

        [Test]
        public void Day1_AfterIsNotNightEntry()
        {
            DayScript script = Load();
            Scene after = script.Get("d1_after");
            Assert.IsNotNull(after, "d1_after");
            Assert.AreEqual(Slot.Night, after.Slot);
            Assert.AreNotEqual("d1_after", script.EntryFor(Slot.Night).Id);
        }
    }
}
