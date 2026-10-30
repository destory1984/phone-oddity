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
        [SerializeField] HarinWardrobe wardrobe;
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

        // Phone glitch: the script's glitch value is shown at a third or less ("calm") so her hair and straps
        // do not smear on every line; emotional moments flash at full strength for a moment, then settle.
        /// <summary>Steady glitch in the phone = the line's glitch times this (at most a third of the moment).</summary>
        // Task 35: 0.3 -> 0.09 (30% of the old steady strength), so grain and bands do not sit on her face.
        public const float GlitchCalmScale = 0.09f;
        /// <summary>Lines with at least this glitch flash at full strength.</summary>
        public const float GlitchMomentAt = 0.5f;
        /// <summary>Flash strength after a big choice and on the emergence night, when the line itself has less.</summary>
        public const float GlitchMomentFloor = 0.45f;
        public const float GlitchMomentSeconds = 0.5f;
        /// <summary>A choice whose effects add up to at least this (absolute values) is a big one.</summary>
        public const int BigChoiceEffect = 2;

        // Phone stage framing: her eyes this far down Harin's window (0 = top), and the window this wide (m)
        // at her face, so the face fills about 40% of it and the window ends just under the collarbones.
        public const float AppStageEyeLine = 0.38f; // Head + 7.5 cm; the drawn eyes land about 5% lower
        public const float AppStageFrameWidth = 0.36f;

        /// <summary>Seconds the install notice stays alone before the script moves on by itself to Harin's first line.</summary>
        public const float NoticeAutoSeconds = 0.4f;
        int _lineSerial;

        public static float CalmGlitch(float lineGlitch) => Mathf.Clamp01(lineGlitch) * GlitchCalmScale;
        public static float MomentGlitch(float lineGlitch) => Mathf.Max(Mathf.Clamp01(lineGlitch), GlitchMomentFloor);

        /// <summary>Fixed line shown when there is no script for the day.</summary>
        public const string EndOfContentText = "내일 또 보자";

        readonly System.Collections.Generic.Dictionary<int, DayScript> _scripts = new System.Collections.Generic.Dictionary<int, DayScript>();
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
        bool _holdNext; // slot ended: a short pause before the next slot opens by itself
        float _holdUntil;
        const float NextStoryPause = 1.5f;
        bool _act2;
        SubtitleBar _subtitle;
        Coroutine _fadeDip;
        Coroutine _glitchMoment;
        Line _pendingFace;        // a Harin line whose face waits for the typing indicator
        bool _pendingMoment;
        bool _bigChoice;          // the next Harin line follows a big choice
        PokeController _poke;
        string _pokeJson;
        AppStageDirector _stage;

        /// <summary>Fired after the Night slot is closed and saved, just before the emergence plays.</summary>
        public event Action NightFinished;

        public GameState State { get; private set; }
        public SlotClock Clock { get; private set; }
        public EmergenceSequence Emergence => emergence;
        public SubtitleBar Subtitle => _subtitle;
        /// <summary>True once the emergence has finished and act 2 plays in the room.</summary>
        public bool InRoom => _act2;
        /// <summary>Glitch shown in the phone right now (chat window and Harin's jitter).</summary>
        public float PhoneGlitch { get; private set; }
        /// <summary>True while a glitch moment flashes.</summary>
        public bool GlitchMomentActive => _glitchMoment != null;
        /// <summary>True when the current day has no script ("오늘은 여기까지" shown).</summary>
        public bool EndOfContent { get; private set; }
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
        /// <summary>True while the emergence cut plays.</summary>
        public bool EmergenceBusy => emergence != null && emergence.IsPlaying;
        /// <summary>Touch reactions of Harin in the phone (null when poke.json is missing).</summary>
        public PokeController Poke => _poke;
        /// <summary>Harin's model in use now: the indoor outfit during the prologue, the summer one otherwise.</summary>
        public HarinPuppet Harin => wardrobe != null && wardrobe.Current != null ? wardrobe.Current : harin;
        public HarinWardrobe Wardrobe => wardrobe;
        /// <summary>Camera director of Harin's phone window (null until the scene has the camera).</summary>
        public AppStageDirector Stage => _stage;

        string PrologueSeenPath => string.IsNullOrEmpty(SavePathOverride)
            ? Path.Combine(Application.persistentDataPath, PrologueSeenFile)
            : SavePathOverride + "." + PrologueSeenFile;

        static DateTime Now() => ClockOverride != null ? ClockOverride() : DateTime.UtcNow;

        void Start()
        {
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (wardrobe == null) wardrobe = FindFirstObjectByType<HarinWardrobe>();
            if (wardrobe != null) wardrobe.Wear(HarinWardrobe.Outfit.Summer); // the phone starts in the summer outfit
            if (harin == null) harin = wardrobe != null && wardrobe.Summer != null ? wardrobe.Summer : HarinWardrobe.ActivePuppet();
            if (emergence == null) emergence = FindFirstObjectByType<EmergenceSequence>();
            if (emergence == null) emergence = gameObject.AddComponent<EmergenceSequence>();
            emergence.SetArt(cut10DeskPhone, screenNoise, portalRing, wristSwirl);
            emergence.Finished += OnEmergenceFinished;
            FrameAppStage();
            StartCoroutine(LoadAllThenInit());
        }

        /// <summary>
        /// Camera for the phone stage, looking straight at her from the front: the frame is
        /// <see cref="AppStageFrameWidth"/> wide at her eyes and her eyes sit <see cref="AppStageEyeLine"/>
        /// of the way down the part of the square picture the chat window shows.
        /// </summary>
        public static UnityEngine.Pose AppStageCameraPose(Vector3 eyes, Vector3 facing, float fov) =>
            AppStageDirector.PoseFor(eyes, facing, fov, new CamShots.Preset { Name = "front", Width = AppStageFrameWidth, EyeLine = AppStageEyeLine });

        void FrameAppStage()
        {
            var go = GameObject.Find("AppStageCamera");
            if (go == null || Harin == null) return;
            var cam = go.GetComponent<Camera>();
            var head = Harin.BonePosition(HumanBodyBones.Head);
            if (cam == null || head == Vector3.zero) return;
            var eyes = head + Vector3.up * 0.075f * Harin.transform.lossyScale.y;
            var pose = AppStageCameraPose(eyes, Harin.transform.forward, cam.fieldOfView);
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            // from here the director moves it: the first shot is the morning default
            if (_stage == null) _stage = gameObject.AddComponent<AppStageDirector>();
            _stage.Init(cam, () => Harin, () => _voice != null && _voice.IsBusy);
            _stage.BeginSlot(Slot.Morning, 1);
        }

        // Reads one StreamingAssets file: a plain file on desktop, UnityWebRequest on Android (inside the apk).
        // done gets the text, or null with the error in err.
        static System.Collections.Generic.IEnumerable<object> ReadStreaming(string path, System.Action<string, string> done)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                done(File.Exists(path) ? File.ReadAllText(path) : null, "missing file");
                yield break;
            }
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();
                bool ok = req.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
                done(ok ? req.downloadHandler.text : null, req.error);
            }
        }

        // Reads day1..EmergenceDay and the prologue. On desktop nothing yields, so Init runs inside Start.
        System.Collections.IEnumerator LoadAllThenInit()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "script");
            var days = new System.Collections.Generic.Dictionary<int, string>();
            string prologue = null, text = null, err = null;
            for (int day = 1; day <= CoreInfo.EmergenceDay + 1; day++)
            {
                foreach (var o in ReadStreaming(Path.Combine(dir, "day" + day + ".json"), (t, e) => { text = t; err = e; })) yield return o; // inline, so desktop never yields
                if (text == null && day == 1 && Application.platform == RuntimePlatform.Android)
                {
                    Debug.LogError("GameBootstrap: cannot read script: " + err);
                    chat.ShowLockNotice("대본을 읽지 못했다: " + err);
                    yield break;
                }
                if (text != null) days[day] = text;
            }
            foreach (var o in ReadStreaming(Path.Combine(dir, "prologue.json"), (t, e) => { prologue = t; err = e; })) yield return o; // inline, so desktop never yields
            if (prologue == null && Application.platform == RuntimePlatform.Android) Debug.LogWarning("GameBootstrap: no prologue: " + err);
            foreach (var o in ReadStreaming(Path.Combine(dir, "poke.json"), (t, e) => { _pokeJson = t; err = e; })) yield return o; // inline, so desktop never yields
            if (_pokeJson == null && Application.platform == RuntimePlatform.Android) Debug.LogWarning("GameBootstrap: no touch reactions: " + err);
            Init(days, prologue);
        }

        void Init(System.Collections.Generic.Dictionary<int, string> dayJson, string prologueJson = null)
        {
            foreach (var kv in dayJson) _scripts[kv.Key] = ScriptLoader.Parse(kv.Value);
            if (!_scripts.ContainsKey(1)) throw new InvalidOperationException("day1.json is missing");
            if (!string.IsNullOrEmpty(prologueJson)) _prologue = ScriptLoader.Parse(prologueJson);

            string path = string.IsNullOrEmpty(SavePathOverride)
                ? Path.Combine(Application.persistentDataPath, "save.json")
                : SavePathOverride;
            _savePath = path;
            _store = new SaveStore(path);
            State = _store.Load(out bool recovered);
            if (recovered) Debug.LogWarning("GameBootstrap: save was unreadable, starting a new game");

            Clock = new SlotClock(State, Now);
            SelectDay(State.Day);

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
            _voice.Init(Harin);
            _voice.Follow = () => Harin; // lip sync goes to whichever model is worn
            SetPhoneIdle();
            InitPoke();

            if (State.NeedsEmergence() && emergence != null)
            {
                // the app was closed during the emergence or act 2: play them again from the start
                Debug.Log("GameBootstrap: act 2 was not seen, replaying the emergence");
                LeavePhone();
                emergence.Play();
                return;
            }
            if (_prologue != null && _subtitle != null && Harin != null && !SkipPrologue && !PrologueSeen)
            {
                StartPrologue();
                return;
            }
            StartDayOne(false);
        }

        // Touch reactions: a bad or missing poke.json only turns them off.
        void InitPoke()
        {
            if (string.IsNullOrEmpty(_pokeJson) || Harin == null) return;
            try
            {
                var table = PokeTable.Parse(_pokeJson);
                _poke = gameObject.AddComponent<PokeController>();
                _poke.Init(this, chat, Harin, State, table);
            }
            catch (ScriptFormatException e)
            {
                Debug.LogWarning("GameBootstrap: touch reactions off: " + e.Message);
            }
        }

        // Makes the script of the day current and builds its runner. False when the day has no script.
        bool SelectDay(int day)
        {
            DayScript script;
            if (!_scripts.TryGetValue(day, out script))
            {
                _script = null;
                if (_runner == null) WireRunner(new DialogueRunner(_scripts[1], State));
                return false;
            }
            if (_script == script && _runner != null) return true;
            _script = script;
            WireRunner(new DialogueRunner(script, State));
            return true;
        }

        void WireRunner(DialogueRunner runner)
        {
            if (_runner != null)
            {
                _runner.LineShown -= OnLineShown;
                _runner.ChoicesShown -= OnChoicesShown;
                _runner.SlotFinished -= OnSlotFinished;
            }
            _runner = runner;
            _runner.LineShown += OnLineShown;
            _runner.ChoicesShown += OnChoicesShown;
            _runner.SlotFinished += OnSlotFinished;
        }

        void StartDayOne(bool afterPrologue)
        {
            if (afterPrologue && _script != null && State.Day == _script.Day && State.Slot == Slot.Morning)
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

        // Between lines in the phone: the faint smile, breathing and idle glances.
        void SetPhoneIdle()
        {
            if (Harin == null) return;
            Harin.SetFace(HarinPuppet.IdleFace);
            Harin.SetIdle(true);
        }

        void Update()
        {
            if (_pendingFace != null && !chat.IsTyping)
            {
                Line line = _pendingFace;
                _pendingFace = null;
                ApplyPhoneLine(line, _pendingMoment);
            }
            if (_waiting && _holdNext && Time.unscaledTime >= _holdUntil) BeginOrWait(true);
            if (!_waiting || _holdNext) return;
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
            _holdNext = false;
            if (!SelectDay(State.Day) || _script.EntryFor(State.Slot) == null)
            {
                ShowEndOfContent();
                return;
            }
            EndOfContent = false;
            if (!Clock.IsOpen)
            {
                _waiting = true;
                chat.ShowWaiting(Clock.Remaining);
                return;
            }
            if (announce) chat.ShowLockNotice("하린: " + OpeningText(State.Slot));
            if (_stage != null) _stage.BeginSlot(State.Slot, State.Day);
            _runner.StartSlot(State.Slot);
        }

        void ShowEndOfContent()
        {
            Debug.Log("GameBootstrap: no script for day " + State.Day + " " + State.Slot);
            EndOfContent = true;
            var line = new Line { Who = "harin", Text = EndOfContentText, Face = "happy", Typing = 0f };
            _pendingFace = null;
            chat.ShowLine(line);
            SetPhoneGlitch(0f);
            if (Harin != null) Harin.SetFace(HarinPuppet.FaceForBeat(line.Face, line.Beat));
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
            if (_poke != null) _poke.Cancel();
            if (Harin != null) Harin.StopPokeMotion();
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
                if (Harin != null && Mathf.Abs(line.Glitch - FadeDipGlitch) < 0.01f)
                {
                    if (_fadeDip != null) StopCoroutine(_fadeDip);
                    _fadeDip = StartCoroutine(FadeDip());
                }
            }
            else
            {
                bool harinLine = line.Who == "harin";
                if (_stage != null) _stage.OnLine(line);
                bool moment = line.Glitch >= GlitchMomentAt
                    || (harinLine && (_bigChoice || (State.Day >= CoreInfo.EmergenceDay && State.Slot == Slot.Night)));
                if (harinLine) _bigChoice = false;
                chat.ShowLine(line);
                if (harinLine && chat.IsTyping)
                {
                    // she is still typing: keep the idle face until the bubble shows
                    _pendingFace = line;
                    _pendingMoment = moment;
                    SetPhoneIdle();
                    SetPhoneGlitch(CalmGlitch(line.Glitch));
                }
                else
                {
                    _pendingFace = null;
                    ApplyPhoneLine(line, moment);
                }
                // the install notice does not wait for a tap: her first line comes by itself
                int serial = ++_lineSerial;
                if (ChatView.TypingEnabled && line.Who == "system" && isActiveAndEnabled && _runner != null
                    && _runner.Current != null && _runner.Current.Lines.Count > 1 && _lineNumber == 1
                    && _runner.Current.Lines[1].Who == "harin")
                    StartCoroutine(AutoAdvanceNotice(serial));
                return;
            }
            if (Harin != null)
            {
                Harin.SetFace(HarinPuppet.FaceForBeat(line.Face, line.Beat));
                if (line.Who == "harin") Harin.BlushForLine(line.Face, line.Beat);
                Harin.SetGlitch(line.Glitch);
                FaceCameraInRoom(line.Who == "harin" && !LooksDownBeats.Contains(line.Beat ?? ""));
            }
        }

        // Beats where she looks down on purpose; every other line of hers meets the player's eyes.
        static readonly System.Collections.Generic.HashSet<string> LooksDownBeats =
            new System.Collections.Generic.HashSet<string> { "looks_down_smiling" };

        // Act 2: on her own lines (her eyes are on the camera) the eyes and the head turn to it, the head lifts
        // a few degrees, all in 0.4 s. Down-looking beats and the player's lines let the head and eyes go back.
        void FaceCameraInRoom(bool towards)
        {
            var cam = emergence != null ? emergence.MainCamera : null;
            if (!towards || cam == null) { Harin.EaseHeadTurn(0f, 0f, EmergenceSequence.Act2EaseSeconds); return; }
            Harin.SetEyeTarget(cam.transform);
            var local = Harin.transform.InverseTransformPoint(cam.transform.position);
            float side = local.x >= 0f ? 1f : -1f; // model +x is her left; yaw > 0 turns to her left
            Harin.EaseHeadTurn(side * EmergenceSequence.Act2HeadToCamera,
                -(EmergenceSequence.Act2HeadToCamera + EmergenceSequence.Act2HeadLift), EmergenceSequence.Act2EaseSeconds);
        }

        // A phone line is on screen: Harin's lines show their face, other lines leave her at the idle smile.
        void ApplyPhoneLine(Line line, bool moment)
        {
            if (Harin != null)
            {
                if (line.Who == "harin")
                {
                    Harin.SetFace(HarinPuppet.FaceForBeat(line.Face, line.Beat));
                    Harin.BlushForLine(line.Face, line.Beat);
                }
                else Harin.SetFace(HarinPuppet.IdleFace);
            }
            if (_glitchMoment != null) { StopCoroutine(_glitchMoment); _glitchMoment = null; }
            if (moment && isActiveAndEnabled)
            {
                _glitchMoment = StartCoroutine(GlitchMoment(line.Glitch));
                // only the glitch moment cuts to G (a bigChoice or night flash is the same moment)
                if (_stage != null) _stage.GlitchCut();
            }
            else SetPhoneGlitch(CalmGlitch(line.Glitch));
        }

        System.Collections.IEnumerator AutoAdvanceNotice(int serial)
        {
            yield return new WaitForSeconds(NoticeAutoSeconds);
            if (serial != _lineSerial || !_awaitingTap || _inPrologue || _act2) yield break;
            _awaitingTap = false;
            _runner.Advance();
        }

        System.Collections.IEnumerator GlitchMoment(float lineGlitch)
        {
            SetPhoneGlitch(MomentGlitch(lineGlitch));
            yield return new WaitForSeconds(GlitchMomentSeconds);
            SetPhoneGlitch(CalmGlitch(lineGlitch));
            _glitchMoment = null;
        }

        void SetPhoneGlitch(float amount)
        {
            PhoneGlitch = Mathf.Clamp01(amount);
            chat.SetGlitch(PhoneGlitch);
            if (Harin != null) Harin.SetGlitch(PhoneGlitch);
        }

        System.Collections.IEnumerator FadeDip()
        {
            Harin.SetFade(FadeDipAlpha);
            yield return new WaitForSeconds(FadeDipSeconds);
            Harin.SetFade(1f);
            _fadeDip = null;
        }

        // The emergence and act 2 drive Harin themselves.
        void LeavePhone()
        {
            _pendingFace = null;
            if (_stage != null) _stage.Enabled = false;
            if (_glitchMoment != null) { StopCoroutine(_glitchMoment); _glitchMoment = null; }
            if (Harin != null) { Harin.SetIdle(false); Harin.StopPokeMotion(); }
            if (_poke != null) _poke.Cancel();
            if (_voice != null) _voice.Stop();
        }

        void OnEmergenceFinished()
        {
            LeavePhone();
            _act2 = true;
            _awaitingTap = false;
            _script = _scripts[1]; // d1_after lives in the day 1 file
            WireRunner(new DialogueRunner(_script, State));
            _runner.StartScene(AfterSceneId);
        }

        void OnChoicesShown(System.Collections.Generic.IReadOnlyList<Choice> choices)
        {
            _awaitingTap = false;
            if (_stage != null) _stage.SetChoices(true);
            chat.ShowChoices(choices, _runner.Current != null && _runner.Current.Type == "feeling");
        }

        void OnSlotFinished(Slot slot)
        {
            _awaitingTap = false;
            if (_act2)
            {
                Debug.Log("GameBootstrap: act 2 cut finished");
                ActTwoFinished = true;
                State.ActTwoSeen = true;
                SaveState();
                return;
            }
            Clock.CloseCurrent();
            SaveState();
            // CloseCurrent has moved the day on, so the day that just ended is State.Day - 1
            if (slot == Slot.Night && State.Day - 1 >= CoreInfo.EmergenceDay)
            {
                NightFinished?.Invoke();
                LeavePhone();
                emergence.Play();
                return;
            }
            _waiting = true;
            _holdNext = true;
            _holdUntil = Time.unscaledTime + NextStoryPause;
            chat.ShowNextStory();
        }

        public void SaveNow() => SaveState();

        // A failed save is only logged: the game goes on and the next save tries again.
        void SaveState()
        {
            if (!_store.Save(State)) Debug.LogWarning("GameBootstrap: cannot write the save (" + _store.LastError + "), will retry at the next save");
        }

        bool EmergenceOrPrologueBusy => _inPrologue || (emergence != null && emergence.IsPlaying);

        /// <summary>Dev panel: end the wait and start the open slot right away.</summary>
        public void SkipToNextSlot()
        {
            if (EmergenceOrPrologueBusy || _act2) { Debug.Log("GameBootstrap: dev next slot ignored during the prologue or emergence"); return; }
            Clock.SkipWait();
            if (_waiting) BeginOrWait(true);
        }

        /// <summary>Dev panel: jump to the morning of the next day and start it now.</summary>
        public void NextDayNow()
        {
            if (EmergenceOrPrologueBusy || _act2) { Debug.Log("GameBootstrap: dev day +1 ignored during the prologue or emergence"); return; }
            JumpTo(State.Day + 1, Slot.Morning);
        }

        /// <summary>Dev panel and shots: go to the given day and slot and start it now.</summary>
        public void JumpTo(int day, Slot slot)
        {
            _waiting = false;
            _awaitingTap = false;
            State.Day = day;
            State.Slot = slot;
            Clock.SkipWait();
            SaveState();
            BeginOrWait(false);
        }

        /// <summary>Dev panel: delete the save file and load the scene again as a new game.</summary>
        public void ClearSaveAndReload()
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        /// <summary>
        /// Dev panel: delete every progress file (save, its .bad and .tmp copies, the prologue-seen mark) and load the scene again,
        /// so the game starts from the first prologue line. The voice set choice (PlayerPrefs) stays.
        /// </summary>
        public void RestartFromBeginning()
        {
            ClearProgressFiles();
            SkipPrologue = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        /// <summary>Delete the progress files. A missing file or a failed delete is ignored.</summary>
        public void ClearProgressFiles()
        {
            string save = string.IsNullOrEmpty(_savePath)
                ? (string.IsNullOrEmpty(SavePathOverride) ? Path.Combine(Application.persistentDataPath, "save.json") : SavePathOverride)
                : _savePath;
            foreach (var f in new[] { save, save + ".bad", save + ".tmp", PrologueSeenPath })
            {
                try { if (File.Exists(f)) File.Delete(f); }
                catch (Exception) { }
            }
        }

        /// <summary>Dev panel: skip the night and play the emergence now.</summary>
        public void PlayEmergenceNow()
        {
            _waiting = false;
            _awaitingTap = false;
            State.Slot = Slot.Night;
            Clock.CloseCurrent();
            SaveState();
            NightFinished?.Invoke();
            LeavePhone();
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
            LeavePhone();
            chat.SetVisible(false);
            // day 12 at home: the indoor outfit (B2); the phone, the emergence and act 2 keep the summer one (B)
            if (wardrobe != null) wardrobe.Wear(HarinWardrobe.Outfit.Indoor);
            _director = PrologueDirector.Create(gameObject, Harin, emergence, emergence.MainCamera, _subtitle, screenNoise, chat.Font);
            _director.Begin();
            _director.VoiceBusy = () => _voice != null && _voice.IsBusy;
            _director.RewindCovered += WearSummer;
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
            if (_director != null) _director.SetChoices(true);
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
            if (_director != null) { _director.RewindCovered -= WearSummer; _director.End(); }
            _director = null;
            WearSummer(); // also when the script had no rewind line
            _subtitle.SetVisible(false);
            chat.SetVisible(true);
            _lineScene = null;
            SetPhoneIdle();
            StartDayOne(true);
        }

        // Back to the summer outfit, while the rewind screen is black (or at the end of the prologue).
        void WearSummer()
        {
            if (wardrobe == null || wardrobe.Wearing == HarinWardrobe.Outfit.Summer) return;
            wardrobe.Wear(HarinWardrobe.Outfit.Summer);
            Harin.SetBlush(0f);
            SetPhoneIdle();
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
            var current = _runner.Current;
            if (current != null && index >= 0 && index < current.Choices.Count)
            {
                var e = current.Choices[index].Effects;
                _bigChoice = e != null && Mathf.Abs(e.Stability) + Mathf.Abs(e.Deviation) + Mathf.Abs(e.Heart) >= BigChoiceEffect;
            }
            _runner.Choose(index);
        }

        void OnNameEntered(string name)
        {
            State.PlayerName = name;
            SaveState();
            BeginOrWait(false);
        }
    }
}
