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
        /// <summary>Tests and most shots set this to start at day 1 as before, without the prologue.</summary>
        public static bool SkipPrologue;

        [SerializeField] ChatView chat;
        [SerializeField] HarinPuppet harin;
        [SerializeField] EmergenceSequence emergence;
        [Header("Cut 10 art")]
        [SerializeField] Texture2D cut10DeskPhone;
        [SerializeField] Texture2D screenNoise;
        [SerializeField] Texture2D portalRing;
        [SerializeField] Texture2D wristSwirl;

        /// <summary>Act 2 scene played in the room after the emergence.</summary>
        public const string AfterSceneId = "d1_after";
        /// <summary>Marker file name (next to the save) written once the prologue has been seen.</summary>
        public const string PrologueSeenFile = "prologue_seen";
        /// <summary>Pause before each Harin line of the prologue (instead of the messenger's typing indicator).</summary>
        public const float PrologueBreathSeconds = 0.3f;
        /// <summary>The act 2 line with this glitch (she loses the player's name) dips Harin's fade.</summary>
        const float FadeDipGlitch = 0.6f;
        const float FadeDipAlpha = 0.4f;
        const float FadeDipSeconds = 1f;

        DayScript _script;
        DayScript _prologue;
        DialogueRunner _prologueRunner;
        PrologueDirector _director;
        HarinVoice _voice;
        bool _inPrologue;
        Coroutine _breath;
        string _lineScene;
        int _lineNumber;
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
        /// <summary>True from the first prologue line until the rewind has handed over to day 1.</summary>
        public bool InPrologue => _inPrologue;
        /// <summary>Id of the prologue scene being played, or null.</summary>
        public string PrologueSceneId => _inPrologue && _prologueRunner != null && _prologueRunner.Current != null ? _prologueRunner.Current.Id : null;
        public PrologueDirector Prologue => _director;
        public HarinVoice Voice => _voice;
        /// <summary>True while a prologue line waits for a tap.</summary>
        public bool PrologueAwaitingTap => _inPrologue && _awaitingTap;
        public bool PrologueSeen => File.Exists(PrologueSeenPath);

        string PrologueSeenPath => string.IsNullOrEmpty(SavePathOverride)
            ? Path.Combine(Application.persistentDataPath, PrologueSeenFile)
            : SavePathOverride + "." + PrologueSeenFile;

        static DateTime Now() => ClockOverride != null ? ClockOverride() : DateTime.UtcNow;

        void Start()
        {
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (harin == null) harin = FindFirstObjectByType<HarinPuppet>();
            if (emergence == null) emergence = FindFirstObjectByType<EmergenceSequence>();
            if (emergence == null) emergence = gameObject.AddComponent<EmergenceSequence>();
            emergence.SetArt(cut10DeskPhone, screenNoise, portalRing, wristSwirl);
            emergence.Finished += OnEmergenceFinished;

            string scriptPath = Path.Combine(Application.streamingAssetsPath, "script", "day1.json");
            string prologuePath = Path.Combine(Application.streamingAssetsPath, "script", "prologue.json");
            if (Application.platform == RuntimePlatform.Android)
            {
                // StreamingAssets sits inside the apk on Android, so File cannot read it
                StartCoroutine(LoadScriptThenInit(scriptPath, prologuePath));
                return;
            }
            Init(File.ReadAllText(scriptPath), File.Exists(prologuePath) ? File.ReadAllText(prologuePath) : null);
        }

        System.Collections.IEnumerator LoadScriptThenInit(string url, string prologueUrl)
        {
            string day1;
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError("GameBootstrap: cannot read script: " + req.error);
                    chat.ShowLockNotice("대본을 읽지 못했다: " + req.error);
                    yield break;
                }
                day1 = req.downloadHandler.text;
            }
            string prologue = null;
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(prologueUrl))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success) prologue = req.downloadHandler.text;
                else Debug.LogWarning("GameBootstrap: no prologue: " + req.error);
            }
            Init(day1, prologue);
        }

        void Init(string json, string prologueJson = null)
        {
            _script = ScriptLoader.Parse(json);
            if (!string.IsNullOrEmpty(prologueJson)) _prologue = ScriptLoader.Parse(prologueJson);

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
                _subtitle.ChoicePicked += OnSubtitleChoice;
                Dev = DevPanel.Create(this, emergence.MainCamera, chat.Font);
            }
            _voice = gameObject.AddComponent<HarinVoice>();
            _voice.Init(harin);

            if (_prologue != null && _subtitle != null && harin != null && !SkipPrologue && !PrologueSeen)
            {
                StartPrologue();
                return;
            }
            StartDayOne(false);
        }

        void StartDayOne(bool afterPrologue)
        {
            if (afterPrologue && State.Day == _script.Day && State.Slot == Slot.Morning)
            {
                // the first thing after the rewind: the day 1 lock-screen notice
                Scene entry = _script.EntryFor(Slot.Morning);
                if (entry != null && entry.Lines.Count > 0) chat.ShowLockNotice(entry.Lines[0].Text);
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
            if (_subtitle != null)
            {
                _subtitle.Tapped -= OnTapped;
                _subtitle.ChoicePicked -= OnSubtitleChoice;
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

        // Counts lines per scene (first line = 1), the numbering the voice files use.
        int NextLineNumber(string sceneId)
        {
            if (sceneId != _lineScene)
            {
                _lineScene = sceneId;
                _lineNumber = 0;
            }
            return ++_lineNumber;
        }

        void OnLineShown(Line line)
        {
            _awaitingTap = true;
            int number = NextLineNumber(CurrentSceneId);
            if (_voice != null)
            {
                if (line.Who == "harin")
                {
                    if (_act2) _voice.PlayLine(CurrentSceneId, number, line.Face, line.Beat);
                    else _voice.PlayLine(CurrentSceneId, number, line.Face, line.Beat, () => !chat.IsTyping);
                }
                else _voice.Stop();
            }
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
            if (_inPrologue)
            {
                if (!_awaitingTap || (_director != null && _director.IsRewinding)) return;
                _awaitingTap = false;
                _prologueRunner.Advance();
                return;
            }
            if (!_awaitingTap) return;
            _awaitingTap = false;
            _runner.Advance();
        }

        void OnSubtitleChoice(int index)
        {
            if (_inPrologue) _prologueRunner.Choose(index);
            else _runner.Choose(index);
        }

        // ---------------------------------------------------------------- prologue (day 12 flash-forward)

        void StartPrologue()
        {
            _inPrologue = true;
            _awaitingTap = false;
            chat.SetVisible(false);
            _director = PrologueDirector.Create(gameObject, harin, emergence, emergence.MainCamera, _subtitle, screenNoise, chat.Font);
            _director.Begin();
            // a throwaway state: the prologue never touches the hidden values, the day or the slot clock
            _prologueRunner = new DialogueRunner(_prologue, new GameState());
            _prologueRunner.LineShown += OnPrologueLine;
            _prologueRunner.ChoicesShown += OnPrologueChoices;
            _prologueRunner.SlotFinished += OnPrologueFinished;
            _lineScene = null;
            _prologueRunner.StartScene(_prologue.Scenes[0].Id);
        }

        void OnPrologueLine(Line line)
        {
            string sceneId = _prologueRunner.Current.Id;
            int number = NextLineNumber(sceneId);
            _awaitingTap = false;
            if (_breath != null) { StopCoroutine(_breath); _breath = null; }
            _voice.Stop();
            _director.ShowLine(line, sceneId);
            if (line.Beat == PrologueDirector.RewindBeat)
            {
                _subtitle.ShowLine(line);
                _director.PlayRewind(EndPrologue);
                return;
            }
            if (line.Who == "harin" && ChatView.TypingEnabled && PrologueBreathSeconds > 0f)
                _breath = StartCoroutine(Breath(line, sceneId, number));
            else RevealPrologueLine(line, sceneId, number);
        }

        System.Collections.IEnumerator Breath(Line line, string sceneId, int number)
        {
            _subtitle.HideBand();
            yield return new WaitForSeconds(PrologueBreathSeconds);
            _breath = null;
            RevealPrologueLine(line, sceneId, number);
        }

        void RevealPrologueLine(Line line, string sceneId, int number)
        {
            _subtitle.ShowLine(line);
            // the notice text sits on the lock-screen card, not on the subtitle band
            if (line.Beat == PrologueDirector.NoticeBeat) _subtitle.HideBand();
            if (line.Who == "harin") _voice.PlayLine(sceneId, number, line.Face, line.Beat);
            _awaitingTap = true;
        }

        void OnPrologueChoices(System.Collections.Generic.IReadOnlyList<Choice> choices)
        {
            _awaitingTap = false;
            _subtitle.ShowChoices(choices);
        }

        void OnPrologueFinished(Slot slot)
        {
            // only reached if the script has no rewind line
            if (_inPrologue && (_director == null || !_director.IsRewinding)) EndPrologue();
        }

        void EndPrologue()
        {
            if (!_inPrologue) return;
            try { File.WriteAllText(PrologueSeenPath, "1"); }
            catch (Exception e) { Debug.LogWarning("GameBootstrap: cannot write the prologue marker: " + e.Message); }
            _inPrologue = false;
            _awaitingTap = false;
            if (_breath != null) { StopCoroutine(_breath); _breath = null; }
            _voice.Stop();
            _prologueRunner.LineShown -= OnPrologueLine;
            _prologueRunner.ChoicesShown -= OnPrologueChoices;
            _prologueRunner.SlotFinished -= OnPrologueFinished;
            if (_director != null) _director.End();
            _director = null;
            _subtitle.SetVisible(false);
            chat.SetVisible(true);
            _lineScene = null;
            StartDayOne(true);
        }

        /// <summary>Dev panel: forget that the prologue was seen and load the scene again.</summary>
        public void ReplayPrologue()
        {
            if (File.Exists(PrologueSeenPath)) File.Delete(PrologueSeenPath);
            SkipPrologue = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
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
