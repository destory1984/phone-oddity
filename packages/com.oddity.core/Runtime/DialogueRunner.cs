using System;
using System.Collections.Generic;

namespace Oddity.Core
{
    /// <summary>
    /// Walks one slot of a <see cref="DayScript"/>. Progress is not saved:
    /// <see cref="StartSlot"/> always begins at the slot's entry scene.
    /// </summary>
    public class DialogueRunner
    {
        private enum Phase
        {
            NotStarted,
            Lines,
            Choices,
            Finished
        }

        private const string DefaultPlayerName = "너";
        private const string PlayerToken = "{player}";

        private readonly DayScript _script;
        private readonly GameState _state;
        private Phase _phase = Phase.NotStarted;
        private Slot _slot;
        private int _lineIndex;

        public DialogueRunner(DayScript script, GameState state)
        {
            _script = script ?? throw new ArgumentNullException(nameof(script));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public event Action<Line> LineShown;
        public event Action<IReadOnlyList<Choice>> ChoicesShown;
        public event Action<Slot> SlotFinished;

        /// <summary>Scene being played, or null before <see cref="StartSlot"/>.</summary>
        public Scene Current { get; private set; }

        public void StartSlot(Slot slot)
        {
            Scene entry = _script.EntryFor(slot);
            if (entry == null)
            {
                throw new InvalidOperationException("No entry scene for slot " + slot + ".");
            }
            _slot = slot;
            EnterScene(entry);
        }

        /// <summary>Starts a scene by id. The slot reported on finish is the scene's own slot.</summary>
        public void StartScene(string id)
        {
            Scene scene = _script.Get(id);
            if (scene == null)
            {
                throw new InvalidOperationException("Unknown scene '" + id + "'.");
            }
            _slot = scene.Slot;
            EnterScene(scene);
        }

        public void Advance()
        {
            if (_phase != Phase.Lines)
            {
                throw new InvalidOperationException("Advance is only valid while lines are being shown (phase: " + _phase + ").");
            }
            _lineIndex++;
            ShowCurrentLineOrEnd();
        }

        public void Choose(int index)
        {
            if (_phase != Phase.Choices)
            {
                throw new InvalidOperationException("Choose is only valid while choices are shown (phase: " + _phase + ").");
            }
            if (index < 0 || index >= Current.Choices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Choice choice = Current.Choices[index];
            if (Current.Type != "feeling")
            {
                _state.Apply(choice.Effects);
            }

            if (string.IsNullOrEmpty(choice.Next))
            {
                Finish();
                return;
            }
            Scene next = _script.Get(choice.Next);
            if (next == null)
            {
                throw new InvalidOperationException("Choice leads to unknown scene '" + choice.Next + "'.");
            }
            EnterScene(next);
        }

        private void EnterScene(Scene scene)
        {
            Current = scene;
            _lineIndex = 0;
            _phase = Phase.Lines;
            ShowCurrentLineOrEnd();
        }

        private void ShowCurrentLineOrEnd()
        {
            if (_lineIndex < Current.Lines.Count)
            {
                LineShown?.Invoke(Localize(Current.Lines[_lineIndex]));
            }
            else if (Current.Choices.Count > 0)
            {
                _phase = Phase.Choices;
                ChoicesShown?.Invoke(Current.Choices);
            }
            else
            {
                Finish();
            }
        }

        private void Finish()
        {
            _phase = Phase.Finished;
            SlotFinished?.Invoke(_slot);
        }

        private Line Localize(Line line)
        {
            string name = string.IsNullOrWhiteSpace(_state.PlayerName) ? DefaultPlayerName : _state.PlayerName;
            return new Line
            {
                Who = line.Who,
                Text = line.Text == null ? null : line.Text.Replace(PlayerToken, name),
                Face = line.Face,
                Glitch = line.Glitch
            };
        }
    }
}
