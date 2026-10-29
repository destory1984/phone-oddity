using System.Collections.Generic;
using NUnit.Framework;

namespace Oddity.Core.Tests
{
    public class DialogueRunnerTests
    {
        private static DayScript MakeScript(string feelingType = "talk")
        {
            return new DayScript
            {
                Day = 1,
                Scenes = new List<Scene>
                {
                    new Scene
                    {
                        Id = "m1", Slot = Slot.Morning, Type = feelingType,
                        Lines = new List<Line>
                        {
                            new Line { Who = "hari", Text = "{player}, 맞지?" },
                            new Line { Who = "hari", Text = "둘째 줄" }
                        },
                        Choices = new List<Choice>
                        {
                            new Choice { Text = "응", Effects = new Effects { Deviation = 1, Stability = 2, Heart = 3 }, Next = "m2" },
                            new Choice { Text = "끝", Next = null }
                        }
                    },
                    new Scene
                    {
                        Id = "m2", Slot = Slot.Morning,
                        Lines = new List<Line> { new Line { Who = "hari", Text = "다음" } }
                    }
                }
            };
        }

        private static List<Line> Capture(DialogueRunner r)
        {
            var lines = new List<Line>();
            r.LineShown += lines.Add;
            return lines;
        }

        [Test]
        public void Runner_SceneNext_AutoAdvances()
        {
            var script = new DayScript
            {
                Day = 12,
                Scenes = new List<Scene>
                {
                    new Scene { Id = "a", Slot = Slot.Night, Next = "b",
                        Lines = new List<Line> { new Line { Who = "harin", Text = "a1", Beat = "teases" } } },
                    new Scene { Id = "b", Slot = Slot.Night,
                        Lines = new List<Line> { new Line { Who = "harin", Text = "b1" } } }
                }
            };
            var r = new DialogueRunner(script, new GameState());
            var lines = Capture(r);
            bool finished = false;
            r.SlotFinished += _ => finished = true;
            r.StartSlot(Slot.Night);
            Assert.AreEqual("teases", lines[0].Beat);
            r.Advance();
            Assert.AreEqual("b", r.Current.Id);
            Assert.AreEqual("b1", lines[1].Text);
            Assert.IsFalse(finished);
            r.Advance();
            Assert.IsTrue(finished);
        }

        [Test]
        public void StartSlot_ShowsFirstLineOfEntry()
        {
            var r = new DialogueRunner(MakeScript(), new GameState { PlayerName = "민수" });
            var lines = Capture(r);
            r.StartSlot(Slot.Morning);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("민수, 맞지?", lines[0].Text);
            Assert.AreEqual("m1", r.Current.Id);
        }

        [Test]
        public void Advance_AfterLastLine_ShowsChoices()
        {
            var r = new DialogueRunner(MakeScript(), new GameState());
            var lines = Capture(r);
            IReadOnlyList<Choice> shown = null;
            r.ChoicesShown += c => shown = c;
            r.StartSlot(Slot.Morning);
            r.Advance();
            Assert.AreEqual(2, lines.Count);
            Assert.IsNull(shown);
            r.Advance();
            Assert.IsNotNull(shown);
            Assert.AreEqual(2, shown.Count);
        }

        [Test]
        public void Choose_AppliesEffectsAndMovesToNext()
        {
            var state = new GameState();
            var r = new DialogueRunner(MakeScript(), state);
            r.StartSlot(Slot.Morning);
            r.Advance();
            r.Advance();
            r.Choose(0);
            Assert.AreEqual(1, state.Deviation);
            Assert.AreEqual(52, state.Stability);
            Assert.AreEqual(3, state.Heart);
            Assert.AreEqual("m2", r.Current.Id);
        }

        [Test]
        public void Choose_FeelingScene_DoesNotApplyEffects()
        {
            var state = new GameState();
            var r = new DialogueRunner(MakeScript("feeling"), state);
            r.StartSlot(Slot.Morning);
            r.Advance();
            r.Advance();
            r.Choose(0);
            Assert.AreEqual(50, state.Stability);
            Assert.AreEqual(0, state.Deviation);
            Assert.AreEqual(0, state.Heart);
        }

        [Test]
        public void Choose_NoNext_FinishesSlot()
        {
            var r = new DialogueRunner(MakeScript(), new GameState());
            var finished = new List<Slot>();
            r.SlotFinished += finished.Add;
            r.StartSlot(Slot.Morning);
            r.Advance();
            r.Advance();
            r.Choose(1);
            Assert.AreEqual(1, finished.Count);
            Assert.AreEqual(Slot.Morning, finished[0]);
        }

        [Test]
        public void PlayerToken_ReplacedWithName()
        {
            var r = new DialogueRunner(MakeScript(), new GameState { PlayerName = "민수" });
            var lines = Capture(r);
            r.StartSlot(Slot.Morning);
            Assert.AreEqual("민수, 맞지?", lines[0].Text);
        }

        [Test]
        public void PlayerToken_BlankName_UsesDefault()
        {
            var script = MakeScript();
            var r = new DialogueRunner(script, new GameState { PlayerName = "  " });
            var lines = Capture(r);
            r.StartSlot(Slot.Morning);
            Assert.AreEqual("너, 맞지?", lines[0].Text);
            Assert.AreEqual("{player}, 맞지?", script.Scenes[0].Lines[0].Text);
        }

        [Test]
        public void StartSlot_AgainAfterAbort_RestartsAtEntry()
        {
            var script = MakeScript();
            var state = new GameState();
            var first = new DialogueRunner(script, state);
            first.StartSlot(Slot.Morning);
            first.Advance();

            var second = new DialogueRunner(script, state);
            var lines = Capture(second);
            second.StartSlot(Slot.Morning);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("너, 맞지?", lines[0].Text);
            Assert.AreEqual("m1", second.Current.Id);
        }

        [Test]
        public void StartScene_ShowsFirstLine()
        {
            var r = new DialogueRunner(MakeScript(), new GameState());
            var lines = Capture(r);
            r.StartScene("m2");
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("다음", lines[0].Text);
            Assert.AreEqual("m2", r.Current.Id);
        }

        [Test]
        public void StartScene_UnknownId_Throws()
        {
            var r = new DialogueRunner(MakeScript(), new GameState());
            var ex = Assert.Throws<System.InvalidOperationException>(() => r.StartScene("nope"));
            StringAssert.Contains("nope", ex.Message);
        }

        [Test]
        public void Misuse_Throws()
        {
            var r = new DialogueRunner(MakeScript(), new GameState());
            Assert.Throws<System.InvalidOperationException>(() => r.Advance());
            Assert.Throws<System.InvalidOperationException>(() => r.Choose(0));
            var ex = Assert.Throws<System.InvalidOperationException>(() => r.StartSlot(Slot.Night));
            StringAssert.Contains("Night", ex.Message);
            r.StartSlot(Slot.Morning);
            r.Advance();
            r.Advance();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => r.Choose(5));
            r.Choose(1);
            Assert.Throws<System.InvalidOperationException>(() => r.Advance());
            Assert.Throws<System.InvalidOperationException>(() => r.Choose(0));
        }
    }
}
