using System;
using NUnit.Framework;

namespace Oddity.Core.Tests
{
    public class SlotClockTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Close_AdvancesSlotAndWaits60s()
        {
            DateTime now = T0;
            var state = new GameState();
            var clock = new SlotClock(state, () => now);
            Assert.IsTrue(clock.IsOpen);
            clock.CloseCurrent();
            Assert.AreEqual(Slot.Evening, state.Slot);
            Assert.IsFalse(clock.IsOpen);
            Assert.AreEqual(TimeSpan.FromSeconds(60), clock.Remaining);
        }

        [Test]
        public void OpensAfterInterval()
        {
            DateTime now = T0;
            var clock = new SlotClock(new GameState(), () => now);
            clock.CloseCurrent();
            now = T0.AddSeconds(61);
            Assert.IsTrue(clock.IsOpen);
            Assert.AreEqual(TimeSpan.Zero, clock.Remaining);
        }

        [Test]
        public void Close_Night_GoesToNextDayMorning()
        {
            var state = new GameState { Day = 1, Slot = Slot.Night };
            var clock = new SlotClock(state, () => T0);
            clock.CloseCurrent();
            Assert.AreEqual(2, state.Day);
            Assert.AreEqual(Slot.Morning, state.Slot);
        }

        [Test]
        public void ClockMovedBack_RemainingCappedAt60s()
        {
            DateTime now = T0;
            var clock = new SlotClock(new GameState(), () => now);
            clock.CloseCurrent();
            now = T0.AddHours(-1);
            Assert.AreEqual(TimeSpan.FromSeconds(60), clock.Remaining);
            Assert.IsFalse(clock.IsOpen);
        }

        [Test]
        public void SkipWait_OpensNow()
        {
            var clock = new SlotClock(new GameState(), () => T0);
            clock.CloseCurrent();
            clock.SkipWait();
            Assert.IsTrue(clock.IsOpen);
            Assert.AreEqual(TimeSpan.Zero, clock.Remaining);
        }
    }
}
