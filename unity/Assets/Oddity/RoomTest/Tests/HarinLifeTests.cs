#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Oddity.RoomTest.Tests
{
    public sealed class HarinLifeTests
    {
        const int Picks = 2000;

        static List<(LifeAct act, float sec, LifeContext ctx)> Run(int seed, Func<int, LifeContext> make)
        {
            var rng = new Random(seed);
            var list = new List<(LifeAct, float, LifeContext)>();
            var last = LifeAct.None;
            for (int i = 0; i < Picks; i++)
            {
                var c = make(i); c.Last = last;
                var p = HarinLife.Pick(rng, c);
                list.Add((p.Act, p.Seconds, c));
                last = p.Act;
            }
            return list;
        }

        static LifeContext Mixed(int i) => new LifeContext { Night = i % 3 == 0, CurtainOpen = i % 5 != 0, CatOnFloor = i % 4 != 0 };

        [Test]
        public void HarinLife_EveryActivityIsPicked()
        {
            var seen = new HashSet<LifeAct>(Run(1, Mixed).Select(t => t.act));
            foreach (LifeAct a in Enum.GetValues(typeof(LifeAct)))
                if (a != LifeAct.None) Assert.IsTrue(seen.Contains(a), "never picked " + a);
        }

        [Test]
        public void HarinLife_NeverTheSameTwiceInARow()
        {
            var r = Run(2, Mixed);
            for (int i = 1; i < r.Count; i++) Assert.AreNotEqual(r[i - 1].act, r[i].act, "repeat at " + i);
        }

        [Test]
        public void HarinLife_DurationsStayInRange()
        {
            foreach (var t in Run(3, Mixed))
            {
                var (min, max) = HarinLife.Range(t.act);
                Assert.GreaterOrEqual(t.sec, min - 1e-3f, t.act.ToString());
                Assert.LessOrEqual(t.sec, max + 1e-3f, t.act.ToString());
            }
        }

        [Test]
        public void HarinLife_MealMostlyFollowsRamen()
        {
            var r = Run(4, Mixed);
            int meals = 0, after = 0;
            for (int i = 1; i < r.Count; i++) if (r[i].act == LifeAct.Meal) { meals++; if (r[i - 1].act == LifeAct.Ramen) after++; }
            Assert.Greater(meals, 20);
            Assert.Greater(after / (float)meals, .8f);
        }

        [Test]
        public void HarinLife_WakeStretchMostlyFollowsNap()
        {
            var r = Run(5, Mixed);
            int wakes = 0, after = 0;
            for (int i = 1; i < r.Count; i++) if (r[i].act == LifeAct.Wake) { wakes++; if (r[i - 1].act == LifeAct.Nap) after++; }
            Assert.Greater(wakes, 20);
            Assert.Greater(after / (float)wakes, .8f);
        }

        [Test]
        public void HarinLife_CurtainNeverClosedByDay()
        {
            foreach (var t in Run(6, i => new LifeContext { Night = false, CurtainOpen = i % 2 == 0, CatOnFloor = true }))
                Assert.AreNotEqual(LifeAct.CurtainClose, t.act);
            // and it is not opened when it is already open
            foreach (var t in Run(7, i => new LifeContext { Night = i % 2 == 0, CurtainOpen = true, CatOnFloor = true }))
                Assert.AreNotEqual(LifeAct.CurtainOpen, t.act);
        }

        [Test]
        public void HarinLife_NoCatPlayWithoutACatOnTheFloor()
        {
            foreach (var t in Run(8, i => new LifeContext { CurtainOpen = true, CatOnFloor = false }))
                Assert.IsFalse(t.act == LifeAct.Pet || t.act == LifeAct.Wand, t.act.ToString());
        }

        [Test]
        public void HarinLife_SameSeedSameSequence()
        {
            CollectionAssert.AreEqual(Run(9, Mixed).Select(t => t.act).ToList(), Run(9, Mixed).Select(t => t.act).ToList());
        }

        [Test]
        public void HarinLife_TakeoverSwitchesOffAndResumesAfterIdle()
        {
            var c = new LifeControl(20f, true);
            Assert.IsTrue(c.Auto);
            Assert.IsTrue(c.Input(), "the first input switches her off");
            Assert.IsFalse(c.Auto);
            Assert.IsFalse(c.Input(), "a second input is not a new switch-off");
            for (int i = 0; i < 199; i++) Assert.IsFalse(c.Tick(.1f)); // 19.9 s
            Assert.IsFalse(c.Auto);
            c.Input(); // input resets the clock
            for (int i = 0; i < 199; i++) Assert.IsFalse(c.Tick(.1f));
            Assert.IsFalse(c.Auto);
            Assert.IsTrue(c.Tick(.2f), "20 s without input gives her back");
            Assert.IsTrue(c.Auto);
            Assert.IsFalse(c.Tick(5f), "no second resume while she is already living");
        }

        [Test]
        public void HarinLife_CameraKeysAreNotHarinInput()
        {
            foreach (var k in new[] { "tab", "o", "i", "cam1", "camcat", "viewliving", "orbit", "orbita30", "wheelup", "liftup", "shift", "shiftleft", "wait", "gi30", "shade95", "insideswing90" })
                if (k != "shift") Assert.IsFalse(LifeControl.IsHarinKey(k), k);
            foreach (var k in new[] { "q", "p", "space", "1", "comma", "rbracket", "clickfloor", "w", "l", "y", "shift", "shiftx" })
                Assert.IsTrue(LifeControl.IsHarinKey(k), k);
        }
    }
}
#endif
