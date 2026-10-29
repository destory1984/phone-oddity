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
        [SerializeField] EmergenceSequence emergence;

        /// <summary>Act 2 scene played in the room after the emergence.</summary>
        public const string AfterSceneId = "d1_after";
        /// <summary>The act 2 line with this glitch (she loses the player's name) dips Harin's fade.</summary>
        const float FadeDipGlitch = 0.6f;
        const float FadeDipAlpha = 0.4f;
        const float FadeDipSeconds = 1f;

        DayScript _script;
        SaveStore _store;
        string _savePath;
        DialogueRunner _runner;
        bool _awaitingTap;
        bool _waiting;
        bool _act2;
        SubtitleBar _subtitle;
        Coroutine _fadeDip;

        /// <summary>Fired after the Night slot is closed and saved, just before the emergence plays.</summary>
        public event Action NightFinished;

        public GameState State { get; private set; }
        public SlotClock Clock { get; private set; }
        public EmergenceSequence Emergence => emergence;
        public SubtitleBar Subtitle => _subtitle;
        /// <summary>True once the emergence has finished and act 2 plays in the room.</summary>
        public bool InRoom => _act2;
        /// <summary>True once the last line of the act 2 cut has been passed.</summary>
        public bool ActTwoFinished { get; private set; }
        public DevPanel Dev { get; private set; }
        /// <summary>Id of the scene the runner is playing, or null.</summary>
        public string CurrentSceneId => _runner != null && _runner.Current != null ? _runner.Current.Id : null;

        static DateTime Now() => ClockOverride != null ? ClockOverride() : DateTime.UtcNow;

        void Start()
        {
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (harin == null) harin = FindFirstObjectByType<HarinPuppet>();
            if (emergence == null) emergence = FindFirstObjectByType<EmergenceSequence>();
            if (emergence == null) emergence = gameObject.AddComponent<EmergenceSequence>();
            emergence.Finished += OnEmergenceFinished;

            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "script", "day1.json"));
            _script = ScriptLoader.Parse(json);

            string path = string.IsNullOrEmpty(SavePathOverride)
                ? Path.Combine(Application.persistentDataPath, "save.json")
                : SavePathOverride;
            _savePath = path;
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

            if (emergence.MainCamera != null)
            {
                _subtitle = SubtitleBar.Create(emergence.MainCamera, chat.Font);
                _subtitle.Tapped += OnTapped;
                Dev = DevPanel.Create(this, emergence.MainCamera, chat.Font);
            }

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
            if (emergence != null) emergence.Finished -= OnEmergenceFinished;
            if (_subtitle != null) _subtitle.Tapped -= OnTapped;
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
            if (_act2)
            {
                _subtitle.ShowLine(line);
                if (harin != null && Mathf.Abs(line.Glitch - FadeDipGlitch) < 0.01f)
                {
                    if (_fadeDip != null) StopCoroutine(_fadeDip);
                    _fadeDip = StartCoroutine(FadeDip());
                }
            }
            else
            {
                chat.ShowLine(line);
                chat.SetGlitch(line.Glitch);
            }
            if (harin != null)
            {
                harin.SetFace(line.Face);
                harin.SetGlitch(line.Glitch);
            }
        }

        System.Collections.IEnumerator FadeDip()
        {
            harin.SetFade(FadeDipAlpha);
            yield return new WaitForSeconds(FadeDipSeconds);
            harin.SetFade(1f);
            _fadeDip = null;
        }

        void OnEmergenceFinished()
        {
            _act2 = true;
            _awaitingTap = false;
            _runner.StartScene(AfterSceneId);
        }

        void OnChoicesShown(System.Collections.Generic.IReadOnlyList<Choice> choices)
        {
            _awaitingTap = false;
            chat.ShowChoices(choices, _runner.Current != null && _runner.Current.Type == "feeling");
        }

        void OnSlotFinished(Slot slot)
        {
            _awaitingTap = false;
            if (_act2)
            {
                Debug.Log("GameBootstrap: act 2 cut finished");
                ActTwoFinished = true;
                return;
            }
            Clock.CloseCurrent();
            _store.Save(State);
            if (slot == Slot.Night)
            {
                NightFinished?.Invoke();
                emergence.Play();
                return;
            }
            BeginOrWait(false);
        }

        public void SaveNow() => _store.Save(State);

        /// <summary>Dev panel: end the wait and start the open slot right away.</summary>
        public void SkipToNextSlot()
        {
            Clock.SkipWait();
            if (_waiting) BeginOrWait(true);
        }

        /// <summary>Dev panel: delete the save file and load the scene again as a new game.</summary>
        public void ClearSaveAndReload()
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        /// <summary>Dev panel: skip the night and play the emergence now.</summary>
        public void PlayEmergenceNow()
        {
            _waiting = false;
            _awaitingTap = false;
            State.Slot = Slot.Night;
            Clock.CloseCurrent();
            _store.Save(State);
            NightFinished?.Invoke();
            emergence.Play();
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
