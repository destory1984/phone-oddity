using System.Collections.Generic;
using NUnit.Framework;

namespace Oddity.Core.Tests
{
    public class PokeRulesTests
    {
        private static PokeTable Table()
        {
            var t = new PokeTable();
            foreach (string zone in new[] { "head", "cheek", "window" })
            {
                for (int stage = 1; stage <= 3; stage++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        t.Lines.Add(new PokeLine { Zone = zone, Stage = stage, Text = zone + stage + "-" + k, Face = "neutral", Motion = "눈맞춤" });
                    }
                }
            }
            return t;
        }

        private static PokeRules Make(out GameState state)
        {
            state = new GameState();
            return new PokeRules(state, Table(), 1);
        }

        [Test]
        public void Poke_StageAdvancesWithin8s()
        {
            PokeRules rules = Make(out _);
            Assert.AreEqual(1, rules.Poke(PokeZone.Head, 0).Stage);
            Assert.AreEqual(2, rules.Poke(PokeZone.Cheek, 3).Stage, "zones share the stage");
            PokeResult third = rules.Poke(PokeZone.Window, 10); // 7 s after the last one
            Assert.AreEqual(PokeOutcome.Reaction, third.Outcome);
            Assert.AreEqual(3, third.Stage);
        }

        [Test]
        public void Poke_ResetsAfter8s()
        {
            PokeRules rules = Make(out _);
            rules.Poke(PokeZone.Head, 0);
            Assert.AreEqual(2, rules.Poke(PokeZone.Head, 5).Stage);
            Assert.AreEqual(1, rules.Poke(PokeZone.Head, 13.5).Stage, "8.5 s since the last one");
        }

        [Test]
        public void Poke_IgnoresFor2s()
        {
            PokeRules rules = Make(out GameState state);
            Assert.AreEqual(PokeOutcome.Reaction, rules.Poke(PokeZone.Head, 10).Outcome);
            Assert.AreEqual(PokeOutcome.Ignored, rules.Poke(PokeZone.Head, 10.5).Outcome);
            Assert.AreEqual(PokeOutcome.Ignored, rules.Poke(PokeZone.Head, 11.9).Outcome);
            Assert.AreEqual(1, rules.Stage, "ignored touches do not move the stage");
            Assert.AreEqual(1, state.PokesToday);
            Assert.AreEqual(2, rules.Poke(PokeZone.Head, 12.1).Stage);
        }

        [Test]
        public void Poke_RestsAfterStage3()
        {
            PokeRules rules = Make(out _);
            rules.Poke(PokeZone.Head, 0);
            rules.Poke(PokeZone.Head, 3);
            Assert.AreEqual(3, rules.Poke(PokeZone.Head, 6).Stage);
            Assert.AreEqual(PokeOutcome.Ignored, rules.Poke(PokeZone.Head, 10).Outcome);
            Assert.AreEqual(PokeOutcome.Ignored, rules.Poke(PokeZone.Head, 35.9).Outcome, "30 s from the third reaction");
            PokeResult again = rules.Poke(PokeZone.Head, 36.1);
            Assert.AreEqual(PokeOutcome.Reaction, again.Outcome);
            Assert.AreEqual(1, again.Stage);
        }

        [Test]
        public void Poke_DailyCapNine()
        {
            PokeRules rules = Make(out GameState state);
            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual(PokeOutcome.Reaction, rules.Poke(PokeZone.Head, i * 40).Outcome, "poke " + (i + 1));
            }
            Assert.AreEqual(9, state.PokesToday);
            PokeResult tenth = rules.Poke(PokeZone.Head, 9 * 40);
            Assert.AreEqual(PokeOutcome.EyeContact, tenth.Outcome);
            Assert.IsNull(tenth.Line);
            Assert.AreEqual(9, state.PokesToday, "eye contact is not counted");

            state.Day = 2; // a new game day starts again at 0
            Assert.AreEqual(PokeOutcome.Reaction, rules.Poke(PokeZone.Head, 10 * 40).Outcome);
            Assert.AreEqual(1, state.PokesToday);
            Assert.AreEqual(2, state.PokeDay);
        }

        [Test]
        public void Poke_AvoidsSameLineSameDay()
        {
            PokeRules rules = Make(out GameState state);
            var seen = new HashSet<string>();
            seen.Add(rules.Poke(PokeZone.Head, 0).Line.Text);
            seen.Add(rules.Poke(PokeZone.Head, 20).Line.Text); // stage 1 again: the other line
            Assert.AreEqual(2, seen.Count, "both stage 1 head lines used, no repeat");
            Assert.AreEqual(2, state.PokesToday);
        }

        [Test]
        public void Poke_CounterSurvivesSave()
        {
            PokeRules rules = Make(out GameState state);
            rules.Poke(PokeZone.Head, 0);
            GameState loaded = GameState.FromJson(state.ToJson(), out bool recovered);
            Assert.IsFalse(recovered);
            Assert.AreEqual(1, loaded.PokesToday);
            Assert.AreEqual(state.PokeDay, loaded.PokeDay);
        }
    }
}
