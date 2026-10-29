using System;
using System.IO;
using Oddity.Core;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Entry point of the prototype: loads day1.json and the save, wires the dialogue runner to
    /// the chat view and Harin, closes slots and saves. Tests can redirect the save file and the clock.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        /// <summary>When set, the save file goes here instead of persistentDataPath/save.json.</summary>
        public static string SavePathOverride;
        /// <summary>When set, replaces DateTime.UtcNow as the game clock.</summary>
        public static Func<DateTime> ClockOverride;

        [SerializeField] ChatView chat;
        [SerializeField] HarinPuppet harin;

        DayScript _script;
        SaveStore _store;
        DialogueRunner _runner;
        bool _awaitingTap;
        bool _waiting;

        /// <summary>Fired after the Night slot is closed and saved. Task 9 plays the emergence from here.</summary>
        public event Action NightFinished;

        public GameState State { get; private set; }
        public SlotClock Clock { get; private set; }

        static DateTime Now() => ClockOverride != null ? ClockOverride() : DateTime.UtcNow;

        void Start()
        {
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (harin == null) harin = FindFirstObjectByType<HarinPuppet>();

            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            _script = ScriptLoader.Parse(json);

            string path = string.IsNullOrEmpty(SavePathOverride)
                ? Path.Combine(Application.persistentDataPath, "save.json")
                : SavePathOverride;
            _store = new SaveStore(path);
            State = _store.Load(out bool recovered);
            if (recovered) Debug.LogWarning("GameBootstrap: save was unreadable, starting a new game");

            Clock = new SlotClock(State, Now);
            _runner = new DialogueRunner(_script, State);
            _runner.LineShown += OnLineShown;
            _runner.ChoicesShown += OnChoicesShown;
            _runner.SlotFinished += OnSlotFinished;

            chat.Tapped += OnTapped;
            chat.ChoicePicked += OnChoicePicked;
            chat.NameEntered += OnNameEntered;

            if (string.IsNullOrWhiteSpace(State.PlayerName))
            {
                chat.AskName();
            }
            else
            {
                BeginOrWait(false);
            }
        }

        void OnDestroy()
        {
            if (chat != null)
            {
                chat.Tapped -= OnTapped;
                chat.ChoicePicked -= OnChoicePicked;
                chat.NameEntered -= OnNameEntered;
            }
        }

        void Update()
        {
            if (!_waiting) return;
            if (Clock.IsOpen)
            {
                BeginOrWait(true);
            }
            else
            {
                chat.ShowWaiting(Clock.Remaining);
            }
        }

        void BeginOrWait(bool announce)
        {
            _waiting = false;
            if (State.Day != _script.Day || _script.EntryFor(State.Slot) == null)
            {
                // nothing written past day 1 yet
                _waiting = false;
                Debug.Log("GameBootstrap: no script for day " + State.Day + " " + State.Slot);
                return;
            }
            if (!Clock.IsOpen)
            {
                _waiting = true;
                chat.ShowWaiting(Clock.Remaining);
                return;
            }
            if (announce) chat.ShowLockNotice("하린: " + OpeningText(State.Slot));
            _runner.StartSlot(State.Slot);
        }

        string OpeningText(Slot slot)
        {
            Scene entry = _script.EntryFor(slot);
            string name = string.IsNullOrWhiteSpace(State.PlayerName) ? "너" : State.PlayerName;
            if (entry != null)
            {
                foreach (Line line in entry.Lines)
                {
                    if (line.Who == "harin" && !string.IsNullOrEmpty(line.Text))
                    {
                        return line.Text.Replace("{player}", name);
                    }
                }
            }
            return name + ", 지금 얘기할 수 있어?";
        }

        void OnLineShown(Line line)
        {
            _awaitingTap = true;
            chat.ShowLine(line);
            chat.SetGlitch(line.Glitch);
            if (harin != null)
            {
                harin.SetFace(line.Face);
                harin.SetGlitch(line.Glitch);
            }
        }

        void OnChoicesShown(System.Collections.Generic.IReadOnlyList<Choice> choices)
        {
            _awaitingTap = false;
            chat.ShowChoices(choices, _runner.Current != null && _runner.Current.Type == "feeling");
        }

        void OnSlotFinished(Slot slot)
        {
            _awaitingTap = false;
            Clock.CloseCurrent();
            _store.Save(State);
            if (slot == Slot.Night)
            {
                NightFinished?.Invoke();
                return;
            }
            BeginOrWait(false);
        }

        void OnTapped()
        {
            if (!_awaitingTap) return;
            _awaitingTap = false;
            _runner.Advance();
        }

        void OnChoicePicked(int index)
        {
            _runner.Choose(index);
        }

        void OnNameEntered(string name)
        {
            State.PlayerName = name;
            _store.Save(State);
            BeginOrWait(false);
        }
    }
}
