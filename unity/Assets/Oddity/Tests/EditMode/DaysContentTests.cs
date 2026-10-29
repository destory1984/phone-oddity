using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oddity.Core;

namespace Oddity.Tests
{
    /// <summary>
    /// Checks the day 1 to 3 scripts together. Uses no UnityEngine API so the dotnet harness can run it too.
    /// </summary>
    public class DaysContentTests
    {
        private static readonly HashSet<string> KnownFaces = new HashSet<string>
        {
            "neutral", "happy", "sad", "angry", "surprised", "relaxed"
        };

        private static readonly Slot[] Slots = { Slot.Morning, Slot.Evening, Slot.Night };

        private static string FindScript(int day)
        {
            string name = "day" + day + ".json";
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (string start in starts)
            {
                DirectoryInfo dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    string fromRepo = Path.Combine(dir.FullName, "unity", "Assets", "StreamingAssets", "script", name);
                    if (File.Exists(fromRepo)) return fromRepo;
                    string fromUnity = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "script", name);
                    if (File.Exists(fromUnity)) return fromUnity;
                    dir = dir.Parent;
                }
            }
            Assert.Fail(name + " not found above " + Directory.GetCurrentDirectory() + " and " + AppContext.BaseDirectory + ".");
            return null;
        }

        private static DayScript Load(int day)
        {
            DayScript script = ScriptLoader.Parse(File.ReadAllText(FindScript(day)));
            Assert.AreEqual(day, script.Day, "day field of day" + day + ".json");
            return script;
        }

        private static IEnumerable<DayScript> AllDays()
        {
            for (int day = 1; day <= CoreInfo.EmergenceDay; day++) yield return Load(day);
        }

        [Test]
        public void Day2_Loads()
        {
            Assert.DoesNotThrow(() => ScriptLoader.Parse(File.ReadAllText(FindScript(2))));
            Assert.AreEqual(2, Load(2).Day);
        }

        [Test]
        public void Day3_Loads()
        {
            Assert.DoesNotThrow(() => ScriptLoader.Parse(File.ReadAllText(FindScript(3))));
            Assert.AreEqual(3, Load(3).Day);
        }

        [Test]
        public void Days_AllPathsReachSlotEnd()
        {
            foreach (DayScript script in AllDays())
            {
                foreach (Slot slot in Slots)
                {
                    Scene entry = script.EntryFor(slot);
                    Assert.IsNotNull(entry, "day " + script.Day + " has no " + slot + " entry");
                    int ends = Walk(script, slot, entry, new HashSet<string>());
                    Assert.Greater(ends, 0, "day " + script.Day + " " + slot + " never ends");
                }
            }
        }

        // Follows every choice and scene.Next; returns how many ends were reached. Fails on a cycle
        // or when a path leaves the slot (the slot clock, not the script, moves between slots).
        private static int Walk(DayScript script, Slot slot, Scene scene, HashSet<string> onPath)
        {
            string where = "day " + script.Day + " " + slot + " scene \"" + scene.Id + "\"";
            Assert.AreEqual(slot, scene.Slot, where + " belongs to another slot");
            Assert.IsTrue(onPath.Add(scene.Id), where + " is part of a cycle");
            int ends = 0;
            if (scene.Choices.Count > 0)
            {
                foreach (Choice choice in scene.Choices)
                {
                    if (string.IsNullOrEmpty(choice.Next)) ends++;
                    else ends += Walk(script, slot, script.Get(choice.Next), onPath);
                }
            }
            else if (!string.IsNullOrEmpty(scene.Next))
            {
                ends += Walk(script, slot, script.Get(scene.Next), onPath);
            }
            else
            {
                ends++;
            }
            onPath.Remove(scene.Id);
            return ends;
        }

        [Test]
        public void Days_FacesKnown()
        {
            foreach (DayScript script in AllDays())
            {
                foreach (Scene scene in script.Scenes)
                {
                    foreach (Line line in scene.Lines)
                    {
                        Assert.IsTrue(KnownFaces.Contains(line.Face),
                            "day " + script.Day + " scene \"" + scene.Id + "\" uses unknown face \"" + line.Face + "\".");
                    }
                }
            }
        }

        [Test]
        public void Days_EffectsWithinTwoAndNoTilde()
        {
            foreach (DayScript script in AllDays())
            {
                foreach (Scene scene in script.Scenes)
                {
                    foreach (Line line in scene.Lines) Assert.IsFalse((line.Text ?? "").Contains("~"), scene.Id + " has a tilde");
                    foreach (Choice c in scene.Choices)
                    {
                        Assert.IsFalse((c.Text ?? "").Contains("~"), scene.Id + " has a tilde");
                        Assert.LessOrEqual(Math.Abs(c.Effects.Stability), 2, scene.Id + " stability");
                        Assert.LessOrEqual(Math.Abs(c.Effects.Deviation), 2, scene.Id + " deviation");
                        Assert.LessOrEqual(Math.Abs(c.Effects.Heart), 2, scene.Id + " heart");
                    }
                }
            }
        }

        // System lines are app notices and alerts. A narrated stage direction ends in "-ㄴ다."
        // ("나타난다.", "본다."); an app notice ends in "-니다." or has no verb ending.
        [Test]
        public void Days_NoStageDirectionSystemLines()
        {
            foreach (DayScript script in AllDays())
            {
                foreach (Scene scene in script.Scenes)
                {
                    foreach (Line line in scene.Lines)
                    {
                        if (line.Who != "system") continue;
                        Assert.IsFalse(LooksLikeStageDirection(line.Text),
                            "day " + script.Day + " scene \"" + scene.Id + "\": system line reads as a stage direction: " + line.Text);
                    }
                }
            }
        }

        private static bool LooksLikeStageDirection(string text)
        {
            string t = (text ?? "").Trim().TrimEnd('.', ' ');
            if (t.Length < 2 || t[t.Length - 1] != '다') return false;
            char before = t[t.Length - 2];
            if (before == '니') return false; // "...습니다" / "...입니다" is an app notice
            if (before < 0xAC00 || before > 0xD7A3) return false;
            int finalConsonant = (before - 0xAC00) % 28;
            return finalConsonant == 4 // ㄴ: 나타난다, 본다, 뜬다, 든다
                || finalConsonant == 16 // ㅁ: 다문다
                || finalConsonant == 21; // ㅆ: 보였다, 열었다
        }
    }
}
