using System;

namespace Oddity.Core
{
    public class SlotClock
    {
        public static readonly TimeSpan FastInterval = TimeSpan.Zero;

        private readonly GameState _state;
        private readonly Func<DateTime> _utcNow;

        public SlotClock(GameState state, Func<DateTime> utcNow)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        // A wait longer than the interval means the phone clock went back: count the wait again from now.
        private void ClampWait()
        {
            DateTime now = _utcNow();
            if (_state.NextOpenUtc - now > FastInterval) _state.NextOpenUtc = now + FastInterval;
        }

        public bool IsOpen
        {
            get
            {
                ClampWait();
                return _utcNow() >= _state.NextOpenUtc;
            }
        }

        public TimeSpan Remaining
        {
            get
            {
                ClampWait();
                TimeSpan left = _state.NextOpenUtc - _utcNow();
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }

        public void CloseCurrent()
        {
            switch (_state.Slot)
            {
                case Slot.Morning:
                    _state.Slot = Slot.Evening;
                    break;
                case Slot.Evening:
                    _state.Slot = Slot.Night;
                    break;
                default:
                    _state.Slot = Slot.Morning;
                    _state.Day++;
                    break;
            }
            _state.NextOpenUtc = _utcNow() + FastInterval;
        }

        public void SkipWait()
        {
            _state.NextOpenUtc = _utcNow();
        }
    }
}
