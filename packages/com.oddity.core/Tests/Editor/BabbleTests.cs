using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Oddity.Core;

namespace Oddity.Core.Tests
{
    /// <summary>The player's babble synthesizer: deterministic, vowel-dependent, silent on punctuation, no clicks.</summary>
    public class BabbleTests
    {
        const int Rate = Babble.DefaultSampleRate;

        static float Diff(float[] a, float[] b)
        {
            double sum = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) sum += Math.Abs(a[i] - b[i]);
            return (float)sum;
        }

        static float[] Slice(float[] a, int from, int to)
        {
            var r = new float[to - from];
            Array.Copy(a, from, r, 0, r.Length);
            return r;
        }

        [Test]
        public void Babble_SameTextSameSamples()
        {
            foreach (BabbleVoice v in Enum.GetValues(typeof(BabbleVoice)))
            {
                var a = Babble.Render("안녕? 나는 민수야, abc 123!", v, Rate);
                var b = Babble.Render("안녕? 나는 민수야, abc 123!", v, Rate);
                Assert.Greater(a.Length, 0);
                CollectionAssert.AreEqual(a, b);
            }
        }

        [Test]
        public void Babble_VowelsDiffer()
        {
            var a = Babble.Render("아", BabbleVoice.Mid, Rate);
            var i = Babble.Render("이", BabbleVoice.Mid, Rate);
            var u = Babble.Render("우", BabbleVoice.Mid, Rate);
            Assert.AreEqual(a.Length, i.Length);
            Assert.Greater(Diff(a, i), 5f);
            Assert.Greater(Diff(a, u), 5f);
            Assert.Greater(Diff(i, u), 5f);
            Assert.Greater(Diff(Babble.Render("가", BabbleVoice.Low, Rate), Babble.Render("가", BabbleVoice.High, Rate)), 5f);
        }

        [Test]
        public void Babble_PunctuationIsSilent()
        {
            foreach (var p in new[] { ".", ",", "?", "!", " ", "?!.,  " })
            {
                var s = Babble.Render(p, BabbleVoice.Mid, Rate);
                Assert.Greater(s.Length, 0, p);
                foreach (float x in s) Assert.AreEqual(0f, x, "punctuation must be silence: " + p);
            }
            Assert.AreEqual((int)Math.Round(Babble.StopSeconds * Rate), Babble.Render(".", BabbleVoice.Mid, Rate).Length);
            Assert.AreEqual((int)Math.Round(Babble.CommaSeconds * Rate), Babble.Render(",", BabbleVoice.Mid, Rate).Length);
            Assert.AreEqual((int)Math.Round(Babble.SpaceSeconds * Rate), Babble.Render(" ", BabbleVoice.Mid, Rate).Length);
            // the pause after a syllable is silent too
            var tail = Babble.Render("가.", BabbleVoice.Mid, Rate);
            int voiced = (int)Math.Round(Babble.SyllableSeconds * Rate);
            for (int k = voiced; k < tail.Length; k++) Assert.AreEqual(0f, tail[k]);
        }

        [Test]
        public void Babble_LengthFollowsText()
        {
            int one = Babble.Render("가", BabbleVoice.Mid, Rate).Length;
            int three = Babble.Render("가나다", BabbleVoice.Mid, Rate).Length;
            Assert.AreEqual((Babble.SyllableSeconds + Babble.GapSeconds) * Rate, one, 2f);
            Assert.AreEqual(3 * one, three, 3);
            Assert.Less(Babble.Render("각", BabbleVoice.Mid, Rate).Length, one, "a final consonant cuts the syllable short");
            Assert.Less(Babble.Render("a", BabbleVoice.Mid, Rate).Length, one, "other letters are short syllables");
            Assert.Greater(Babble.Render("가나다라마바사", BabbleVoice.Mid, Rate).Length, three);
            Assert.Greater(Babble.Render("가 나", BabbleVoice.Mid, Rate).Length, Babble.Render("가나", BabbleVoice.Mid, Rate).Length);
            Assert.AreEqual(0, Babble.Render("", BabbleVoice.Mid, Rate).Length);
            Assert.AreEqual(0, Babble.Render(null, BabbleVoice.Mid, Rate).Length);
        }

        [Test]
        public void Babble_NoClicksAtEdges()
        {
            foreach (BabbleVoice v in Enum.GetValues(typeof(BabbleVoice)))
            {
                foreach (var text in new[] { "코끼리", "싸움", "하하", "문", "안녕? 응!", "a1" })
                {
                    var s = Babble.Render(text, v, Rate);
                    Assert.Less(Math.Abs(s[0]), 0.01f, text + " first sample");
                    Assert.Less(Math.Abs(s[s.Length - 1]), 0.01f, text + " last sample");
                    float peak = 0f;
                    foreach (float x in s) peak = Math.Max(peak, Math.Abs(x));
                    Assert.Greater(peak, 0.05f, text + " is audible");
                    Assert.LessOrEqual(peak, 1f, text + " does not clip");
                    for (int i = 1; i < s.Length; i++) Assert.Less(Math.Abs(s[i] - s[i - 1]), 0.5f, text + " step at " + i);
                }
            }
        }

        [Test]
        public void Babble_QuestionRaisesPitch()
        {
            var plain = Babble.Render("가나다라", BabbleVoice.Mid, Rate);
            var ask = Babble.Render("가나다라?", BabbleVoice.Mid, Rate);
            int syl = (int)Math.Round((Babble.SyllableSeconds + Babble.GapSeconds) * Rate);
            for (int i = 0; i < 2 * syl; i++) Assert.AreEqual(plain[i], ask[i]);
            Assert.Greater(Diff(Slice(plain, 2 * syl, 4 * syl), Slice(ask, 2 * syl, 4 * syl)), 5f);
        }

        static string FindPrologue()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    string p = Path.Combine(dir.FullName, "unity", "Assets", "StreamingAssets", "script", "prologue.json");
                    if (File.Exists(p)) return p;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        static void WriteWav(string path, float[] samples, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float x in samples) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, x)) * 32767f));
            }
        }

        /// <summary>Only runs when ODDITY_BABBLE_WAV names a folder: writes the first 6 player lines of the prologue in three voices.</summary>
        [Test]
        public void Babble_WriteSamples()
        {
            string dir = Environment.GetEnvironmentVariable("ODDITY_BABBLE_WAV");
            if (string.IsNullOrEmpty(dir)) Assert.Pass("skipped: ODDITY_BABBLE_WAV is not set");
            string path = FindPrologue();
            Assert.IsNotNull(path, "prologue.json not found");
            var script = ScriptLoader.Parse(File.ReadAllText(path, new UTF8Encoding(false)));
            var lines = new List<string>();
            foreach (var scene in script.Scenes)
                foreach (var line in scene.Lines)
                    if (line.Who == "player" && lines.Count < 6) lines.Add((line.Text ?? "").Replace("{player}", "민수"));
            Assert.AreEqual(6, lines.Count);
            Directory.CreateDirectory(dir);
            foreach (BabbleVoice v in Enum.GetValues(typeof(BabbleVoice)))
                for (int n = 0; n < lines.Count; n++)
                    WriteWav(Path.Combine(dir, "babble_" + v.ToString().ToLowerInvariant() + "_" + (n + 1) + ".wav"),
                        Babble.Render(lines[n], v, Rate), Rate);
        }
    }
}
