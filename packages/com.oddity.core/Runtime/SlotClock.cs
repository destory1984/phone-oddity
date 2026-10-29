using System;

namespace Oddity.Core
{
    public class SlotClock
    {
        public static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(60);

        private readonly GameState _state;
        private readonly Func<DateTime> _utcNow;

        public SlotClock(GameState state, Func<DateTime> utcNow)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public bool IsOpen
        {
            get { return _utcNow() >= _state.NextOpenUtc; }
        }

        public TimeSpan Remaining
        {
            get
            {
                TimeSpan left = _state.NextOpenUtc - _utcNow();
                if (left < TimeSpan.Zero) return TimeSpan.Zero;
                return left > FastInterval ? FastInterval : left;
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
