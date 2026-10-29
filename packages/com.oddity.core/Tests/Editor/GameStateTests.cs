using System;
using System.IO;
using NUnit.Framework;

namespace Oddity.Core.Tests
{
    public class GameStateTests
    {
        [Test]
        public void Apply_AddsAndClamps()
        {
            var s = new GameState();
            s.Apply(new Effects { Stability = 60, Deviation = -5, Heart = 3 });
            Assert.AreEqual(100, s.Stability);
            Assert.AreEqual(0, s.Deviation);
            Assert.AreEqual(3, s.Heart);
        }

        [Test]
        public void Json_RoundTrips()
        {
            var s = new GameState
            {
                Day = 7, Slot = Slot.Night, Stability = 12, Deviation = 34, Heart = 56,
                DaysLeft = 9, PlayerName = "하린",
                NextOpenUtc = new DateTime(2026, 10, 29, 3, 4, 5, DateTimeKind.Utc)
            };
            string json = s.ToJson();
            StringAssert.Contains("\"night\"", json);
            StringAssert.Contains("\"playerName\"", json);
            GameState r = GameState.FromJson(json, out bool recovered);
            Assert.IsFalse(recovered);
            Assert.AreEqual(GameState.CurrentVersion, r.Version);
            Assert.AreEqual(7, r.Day);
            Assert.AreEqual(Slot.Night, r.Slot);
            Assert.AreEqual(12, r.Stability);
            Assert.AreEqual(34, r.Deviation);
            Assert.AreEqual(56, r.Heart);
            Assert.AreEqual(9, r.DaysLeft);
            Assert.AreEqual("하린", r.PlayerName);
            Assert.AreEqual(s.NextOpenUtc, r.NextOpenUtc);
            Assert.AreEqual(DateTimeKind.Utc, r.NextOpenUtc.Kind);
        }

        [Test]
        public void FromJson_CorruptReturnsFresh()
        {
            GameState r = GameState.FromJson("{not json", out bool recovered);
            Assert.IsTrue(recovered);
            Assert.AreEqual(1, r.Day);
            Assert.AreEqual(50, r.Stability);
        }

        [Test]
        public void FromJson_OldVersionReturnsFresh()
        {
            GameState r = GameState.FromJson("{\"version\":0,\"day\":5,\"stability\":80}", out bool recovered);
            Assert.IsTrue(recovered);
            Assert.AreEqual(1, r.Day);
            Assert.AreEqual(50, r.Stability);
            Assert.AreEqual(GameState.CurrentVersion, r.Version);
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "oddity-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        public void SaveStore_MissingFileReturnsFresh()
        {
            string dir = TempDir();
            try
            {
                var store = new SaveStore(Path.Combine(dir, "save.json"));
                GameState s = store.Load(out bool recovered);
                Assert.IsFalse(recovered);
                Assert.AreEqual(1, s.Day);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void SaveStore_SaveThenLoad()
        {
            string dir = TempDir();
            try
            {
                var store = new SaveStore(Path.Combine(dir, "sub", "save.json"));
                var s = new GameState { Day = 3, Heart = 20 };
                store.Save(s);
                s.Day = 4;
                store.Save(s); // replace path
                GameState r = store.Load(out bool recovered);
                Assert.IsFalse(recovered);
                Assert.AreEqual(4, r.Day);
                Assert.AreEqual(20, r.Heart);
                Assert.AreEqual(1, Directory.GetFiles(Path.Combine(dir, "sub")).Length);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void ActTwo_ReplaysIfInterrupted()
        {
            var s = new GameState { Day = CoreInfo.EmergenceDay + 1 };
            Assert.IsTrue(s.NeedsEmergence());
            Assert.IsFalse(new GameState { Day = CoreInfo.EmergenceDay }.NeedsEmergence());
            // an old save has no actTwoSeen: false
            GameState old = GameState.FromJson("{\"version\":1,\"day\":4}", out bool rec);
            Assert.IsFalse(rec);
            Assert.IsFalse(old.ActTwoSeen);
            Assert.IsTrue(old.NeedsEmergence());
            s.ActTwoSeen = true;
            GameState back = GameState.FromJson(s.ToJson(), out rec);
            Assert.IsTrue(back.ActTwoSeen);
            Assert.IsFalse(back.NeedsEmergence());
        }

        [Test]
        public void Save_FailureDoesNotStall()
        {
            string dir = TempDir();
            try
            {
                // the save path is a directory: writing the file fails, but Save must not throw
                string path = Path.Combine(dir, "save.json");
                Directory.CreateDirectory(path);
                var store = new SaveStore(path);
                bool ok = true;
                Assert.DoesNotThrow(() => ok = store.Save(new GameState()));
                Assert.IsFalse(ok);
                Assert.IsNotNull(store.LastError);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void Load_CorruptFileStartsFresh()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "save.json");
                File.WriteAllText(path, "{ not json !!");
                var store = new SaveStore(path);
                GameState s = store.Load(out bool recovered);
                Assert.IsTrue(recovered);
                Assert.AreEqual(1, s.Day);
                Assert.IsTrue(File.Exists(path + ".bad"));
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(store.Save(s)); // and the next save works
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
