using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class CatSleepTests
    {
        static readonly string[] Base14 = { "Idle", "Walk", "SitDown", "SittingIdle", "StandUp", "LieDown", "LyingIdle", "GetUp", "LoafDown", "LoafIdle", "LoafUp", "Jump", "Groom", "Stretch" };

        static string WhiteGlb()
        {
            string dir = Path.Combine(Application.dataPath, "Models/test");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "whitecat-2-*-anim.glb")
                .OrderByDescending(f => { var parts = Path.GetFileNameWithoutExtension(f).Split('-'); return parts.Length >= 3 && int.TryParse(parts[2], out var n) ? n : -1; })
                .FirstOrDefault() : null;
        }
        static (string tag, string asset)[] Cats()
        {
            var w = WhiteGlb();
            var list = new System.Collections.Generic.List<(string, string)>();
            if (w != null) list.Add(("WHITE", "Assets/Models/test/" + Path.GetFileName(w)));
            list.Add(("SOMALI", "Assets/Oddity/DevMotion/Cat/Somali/somali_cat_shared.glb"));
            list.Add(("TABBY", "Assets/Oddity/DevMotion/Cat/Cat4/cat4_shared.glb"));
            return list.ToArray();
        }
        static string Disk(string asset) => Path.Combine(Application.dataPath, "../" + asset);

        [Test]
        public void CatSleep_AllTwentyOneClipsFoundOnEachCat()
        {
            int checkedCats = 0;
            foreach (var (tag, asset) in Cats())
            {
                if (!File.Exists(Disk(asset))) continue;
                var names = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Select(c => c.name).ToList();
                if (names.Count == 0) { AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport); names = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Select(c => c.name).ToList(); }
                bool Has(string n) => names.Contains(n) || names.Any(c => c.EndsWith("|" + n) && !c.StartsWith("__preview__"));
                foreach (var n in Base14.Concat(CatSleep.AllClipNames())) Assert.IsTrue(Has(n), tag + " lacks clip " + n + " (have " + string.Join(",", names) + ")");
                Assert.AreEqual(21, CatSleep.AllClipNames().Count(), "three clips for each of seven poses");
                checkedCats++;
            }
            if (checkedCats == 0) Assert.Ignore("no cat files installed");
        }

        [Test]
        public void CatSleep_JsonFootprintsPresentForEverySevenPoses()
        {
            int n = 0;
            foreach (var (tag, asset) in Cats())
            {
                var json = Path.ChangeExtension(Disk(asset), ".json");
                if (!File.Exists(json)) continue;
                var fp = CatSleep.ParseFootprints(File.ReadAllText(json));
                for (int p = 0; p < CatSleep.Count; p++)
                {
                    Assert.Greater(fp[p].lng, 0f, tag + " " + CatSleep.Id[p] + " long side");
                    Assert.Greater(fp[p].sht, 0f, tag + " " + CatSleep.Id[p] + " short side");
                    Assert.GreaterOrEqual(fp[p].lng, fp[p].sht);
                }
                n++;
            }
            if (n == 0) Assert.Ignore("no cat json installed");
        }

        [Test]
        public void CatSleep_PoseChoiceRespectsThePerch()
        {
            var rng = new System.Random(7);
            // the cat tower top is 0.40 x 0.40 m; the poses' footprints (long x short, metres): the big ones do not fit
            var fp = new[] { (.51f, .26f), (.29f, .28f), (.68f, .21f), (.61f, .26f), (.29f, .28f), (.72f, .25f), (.48f, .33f) };
            bool Tower(int k) => CatSleep.Fits(fp[k].Item1, fp[k].Item2, .40f, .40f);
            Assert.IsFalse(Tower(CatSleep.Superman), "superman is longer than the tower top");
            Assert.IsFalse(Tower(CatSleep.SideSprawl));
            for (int i = 0; i < 2000; i++)
            {
                int k = CatSleep.Choose(CatSleep.Place.Tower, false, Tower, rng);
                Assert.GreaterOrEqual(k, 0);
                Assert.IsTrue(Tower(k) && CatSleep.Allowed(k, CatSleep.Place.Tower), "tower got " + CatSleep.Id[k]);
                Assert.IsTrue(k == CatSleep.LoafDoze || k == CatSleep.Donut || k == CatSleep.CurlC || k == CatSleep.PawCover);
            }
            // the floor and the bed take every pose
            var seen = new bool[CatSleep.Count];
            for (int i = 0; i < 4000; i++) seen[CatSleep.Choose(CatSleep.Place.Floor, false, k => true, rng)] = true;
            Assert.IsTrue(seen.All(b => b), "every pose turns up on the floor");
            // the sill is a 2.4 x 0.28 m board: only the small poses, and none whose short side is wider than the board
            for (int i = 0; i < 1000; i++)
            {
                int k = CatSleep.Choose(CatSleep.Place.Sill, false, j => CatSleep.Fits(fp[j].Item1, fp[j].Item2, 2.4f, .28f), rng);
                Assert.IsTrue(k == CatSleep.LoafDoze || k == CatSleep.Donut || k == CatSleep.CurlC || k == CatSleep.PawCover, "sill got " + (k < 0 ? "none" : CatSleep.Id[k]));
            }
            // nothing fits: no choice
            Assert.AreEqual(-1, CatSleep.Choose(CatSleep.Place.Tower, false, k => false, rng));
            // lamp on: PawCover is chosen more often
            int off = 0, on = 0;
            for (int i = 0; i < 6000; i++) { if (CatSleep.Choose(CatSleep.Place.Floor, false, k => true, rng) == CatSleep.PawCover) off++; if (CatSleep.Choose(CatSleep.Place.Floor, true, k => true, rng) == CatSleep.PawCover) on++; }
            Assert.Greater(on, off * 1.5, "PawCover more likely with the lamp on");
        }
    }
}
