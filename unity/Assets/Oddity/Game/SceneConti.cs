using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>Whether the eyes (and so the head) are on the window's camera while a conti line shows.</summary>
    public enum ContiGaze { Keep, Lens, Free }

    /// <summary>A framing: width across her eyes (m), eye line (0 = top of the window), yaw and pitch (deg). Width 0 = not set.</summary>
    public struct ContiShot
    {
        public float Width, EyeLine, Yaw, Pitch;
        public bool Has => Width > 0f;
        public ContiShot(float width, float eyeLine, float yaw, float pitch) { Width = width; EyeLine = eyeLine; Yaw = yaw; Pitch = pitch; }
    }

    /// <summary>The head turns to yaw (deg, towards the camera's side) and Down (deg, negative = chin up) after Delay s, taking Seconds.</summary>
    public struct ContiHead
    {
        public bool Has;
        public float Delay, Yaw, Down, Seconds;
        public ContiHead(float delay, float yaw, float down, float seconds = SceneConti.HeadSeconds)
        { Has = true; Delay = delay; Yaw = yaw; Down = down; Seconds = seconds; }
    }

    /// <summary>The face changes to Name after Delay s.</summary>
    public struct ContiFace
    {
        public string Name;
        public float Delay;
        public bool Has => !string.IsNullOrEmpty(Name);
        public ContiFace(float delay, string name) { Delay = delay; Name = name; }
    }

    /// <summary>
    /// One hand-written direction for one script line (task 48). Times are seconds from the moment the line shows.
    /// Start: the cut at the cue's start (not set = keep the picture as it is). End: where the slow move goes (not set = hold).
    /// </summary>
    public struct ContiCue
    {
        public ContiShot Start, End;
        public float MoveDelay, MoveSeconds;
        /// <summary>0 = none. The move goes this share of the width past the end width, then comes back in <see cref="SceneConti.OvershootBackSeconds"/>.</summary>
        public float Overshoot;
        public ContiHead Head1, Head2;
        public ContiGaze Gaze;
        public ContiFace Face1, Face2;
        /// <summary>Face set while the typing indicator shows (before the line), after its Delay.</summary>
        public ContiFace TypingFace;
        public string Sfx;
        /// <summary>A Mixamo motion (file name in Resources/Motion) on the upper body while the line shows; played only when <see cref="SceneConti.MotionOn"/>.</summary>
        public string Motion;
        /// <summary>True: the line shows the glitch flash (the day 3 night rule would flash every Harin line and bury the conti).</summary>
        public bool Glitch;
    }

    /// <summary>
    /// The hand-written conti of day 3 night ("d3_night", "d3_night_end"), a pilot of per-line direction
    /// (shots/plans/conti_d3_night.md). Keys are "sceneId:lineNumber", the number counting from 1 like the voice files.
    /// </summary>
    public static class SceneConti
    {
        // ---- the numbers of the plan ----
        public static readonly ContiShot ShotL = new ContiShot(0.58f, 0.33f, 8f, 0f);
        public static readonly ContiShot ShotH2 = new ContiShot(0.42f, 0.37f, 8f, 6f);
        public static readonly ContiShot ShotM = new ContiShot(0.62f, 0.32f, 0f, 0f);
        public static readonly ContiShot ShotC2 = new ContiShot(0.40f, 0.37f, 0f, 0f);
        public const float SideWidth = 0.30f, SideEyeLine = 0.42f;
        public const float EndEyeLine = 0.39f;
        /// <summary>
        /// Strength 1 (weak), 2 (mid), 3 (strong), 4 (close): side-face yaw, width at the start and at the end of the side push,
        /// overshoot of the last-line-but-three push, final width. Strength 4 shoots close and moves little (no side push, no overshoot).
        /// </summary>
        public static readonly float[] SideYaw = { 25f, 32f, 40f, 32f };
        public static readonly float[] SideStartWidth = { SideWidth, SideWidth, SideWidth, 0.26f };
        public static readonly float[] SidePushEnd = { 0.29f, 0.28f, 0.27f, 0.26f };
        public static readonly float[] ClimaxOvershoot = { 0f, 0.03f, 0.05f, 0f };
        public static readonly float[] FinalWidth = { 0.38f, 0.35f, 0.32f, 0.26f };
        // Strength 4 (close): the same cuts framed closer; the eye line is set per shot so the face stays in the window.
        public const int CloseLevel = 4;
        public static readonly ContiShot ShotLClose = new ContiShot(0.42f, 0.36f, 8f, 0f);
        public const float SideEyeLineClose = 0.42f;
        public static readonly ContiShot ShotH2Close = new ContiShot(0.34f, 0.40f, 8f, 6f);
        public static readonly ContiShot ShotMClose = new ContiShot(0.40f, 0.36f, 0f, 0f);
        /// <summary>Where the push of end line 3 ends at strength 4 (the plan's C2 is 0.40 wide), and the eye line of the last pushes.</summary>
        public static readonly ContiShot ShotClimaxClose = new ContiShot(0.30f, 0.38f, 0f, 0f);
        public const float EndEyeLineClose = 0.38f;
        public const float CupSeconds = 2.7f;            // length of the cup sound, the side push lasts as long
        public const string CupSfx = "cup_voice_message";
        public const float H2PushSeconds = 2.0f, H2EndWidth = 0.40f;
        public const float ClimaxDelay = 0.35f, ClimaxSeconds = 1.5f, ClimaxVoiceSeconds = 2.0f;
        public const float ClimaxSecondsClose = 3.0f;    // strength 4: a very slow push with no overshoot
        public const float FinalPushSeconds = 2.7f;
        public const float FinalPushSecondsClose = 4.0f;
        public const float OvershootBackSeconds = 0.3f;
        public const float HeadSeconds = 0.6f;           // default time of a head move
        public const float TapBlockExtraSeconds = 0.5f;  // the tap that skips the cup sound waits this long after it
        public const float SfxVolume = 0.7f;             // Harin's voice is 0.9

        public const string Night = "d3_night", NightEnd = "d3_night_end";

        public const string LevelPref = "oddity.conti.v1";
        public const int DefaultLevel = 2;
        public const int MaxLevel = 4;
        public const string NightPref = "oddity.contiNight.v1";
        static int _level = -1;

        /// <summary>0 off, 1 weak, 2 mid (default), 3 strong, 4 close; remembered in PlayerPrefs (development).</summary>
        public static int Level
        {
            get
            {
                if (_level < 0)
                {
                    try { _level = PlayerPrefs.GetInt(LevelPref, DefaultLevel); }
                    catch (Exception) { _level = DefaultLevel; }
                    if (_level < 0 || _level > MaxLevel) _level = DefaultLevel;
                }
                return _level;
            }
            set
            {
                _level = Mathf.Clamp(value, 0, MaxLevel);
                try { PlayerPrefs.SetInt(LevelPref, _level); PlayerPrefs.Save(); }
                catch (Exception) { }
            }
        }

        /// <summary>Forget the cached level so the next read goes to PlayerPrefs again (tests).</summary>
        public static void ClearLevelCache() { _level = -1; }

        /// <summary>Sets the strength for this run only, without writing PlayerPrefs (shots).</summary>
        public static void SetLevelForRun(int level) { _level = Mathf.Clamp(level, 0, MaxLevel); }

        public static int NextLevel() => (Level + 1) % (MaxLevel + 1);

        public static string LevelName(int level) => level == 0 ? "끔" : level == 1 ? "약" : level == 3 ? "강" : level == CloseLevel ? "근" : "중";

        static int _night = -1;

        /// <summary>The night look (<see cref="NightLook"/>) on the conti scenes; default on, remembered in PlayerPrefs (development).</summary>
        public static bool NightOn
        {
            get
            {
                if (_night < 0)
                {
                    try { _night = PlayerPrefs.GetInt(NightPref, 1) != 0 ? 1 : 0; }
                    catch (Exception) { _night = 1; }
                }
                return _night == 1;
            }
            set
            {
                _night = value ? 1 : 0;
                try { PlayerPrefs.SetInt(NightPref, _night); PlayerPrefs.Save(); }
                catch (Exception) { }
            }
        }

        /// <summary>Sets the night look for this run only, without writing PlayerPrefs (shots, previs, tests).</summary>
        public static void SetNightForRun(bool on) { _night = on ? 1 : 0; }

        /// <summary>Forget the cached night setting so the next read goes to PlayerPrefs again (tests).</summary>
        public static void ClearNightCache() { _night = -1; }

        static bool _motionOn;

        /// <summary>The Mixamo motions of the cues (task 51); default off, this run only (never saved).</summary>
        public static bool MotionOn { get => _motionOn; set => _motionOn = value; }

        /// <summary>Sets the motion switch for this run only (shots, previs, tests).</summary>
        public static void SetMotionForRun(bool on) { _motionOn = on; }

        /// <summary>The line that carries the motion in the pilot.</summary>
        public const string MotionName = "Bashful";

        /// <summary>True for the two scenes the conti table is written for.</summary>
        public static bool IsContiScene(string sceneId) => sceneId == Night || sceneId == NightEnd;

        public static string Key(string sceneId, int line) => sceneId + ":" + line;

        /// <summary>The cue of a line at a strength (1 to 3), or false when the line has none.</summary>
        public static bool TryGet(string sceneId, int line, int level, out ContiCue cue)
        {
            cue = default;
            if (level < 1 || level > MaxLevel || sceneId == null) return false;
            if (sceneId != Night && sceneId != NightEnd) return false;
            return Table(level).TryGetValue(Key(sceneId, line), out cue);
        }

        /// <summary>Every key of the table with the speaker the plan expects: p = player, s = system, h = Harin.</summary>
        public static IReadOnlyDictionary<string, char> Speakers => SpeakerTable;

        static readonly Dictionary<string, char> SpeakerTable = new Dictionary<string, char>
        {
            { "d3_night:1", 'p' }, { "d3_night:2", 's' }, { "d3_night:3", 'h' }, { "d3_night:4", 'p' },
            { "d3_night:5", 'h' }, { "d3_night:6", 'p' }, { "d3_night:7", 'h' }, { "d3_night:8", 'h' },
            { "d3_night_end:1", 'h' }, { "d3_night_end:2", 'p' }, { "d3_night_end:3", 'h' }, { "d3_night_end:4", 'h' },
            { "d3_night_end:5", 'p' }, { "d3_night_end:6", 'h' }, { "d3_night_end:7", 'h' },
        };

        static readonly Dictionary<string, ContiCue>[] Tables = new Dictionary<string, ContiCue>[MaxLevel];

        static Dictionary<string, ContiCue> Table(int level) => Tables[level - 1] ?? (Tables[level - 1] = Build(level));

        // The side face: yaw by strength; it pushes from SideWidth to SidePushEnd while the cup sound plays.
        public static ContiShot SideShot(int level) => new ContiShot(SideStartWidth[level - 1], level == CloseLevel ? SideEyeLineClose : SideEyeLine, SideYaw[level - 1], 0f);

        public static ContiShot FinalShot(int level) => new ContiShot(FinalWidth[level - 1], level == CloseLevel ? EndEyeLineClose : EndEyeLine, 0f, 0f);

        public static ContiShot LShot(int level) => level == CloseLevel ? ShotLClose : ShotL;
        public static ContiShot MShot(int level) => level == CloseLevel ? ShotMClose : ShotM;
        public static ContiShot H2Shot(int level) => level == CloseLevel ? ShotH2Close : ShotH2;
        /// <summary>Where the push of end line 3 ends: the plan's C2, or the closer framing of strength 4.</summary>
        public static ContiShot ClimaxEnd(int level) => level == CloseLevel ? ShotClimaxClose : ShotC2;
        public static float ClimaxPushSeconds(int level) => level == CloseLevel ? ClimaxSecondsClose : ClimaxSeconds;
        public static float FinalPush(int level) => level == CloseLevel ? FinalPushSecondsClose : FinalPushSeconds;

        static Dictionary<string, ContiCue> Build(int level)
        {
            int i = level - 1;
            var side = SideShot(level);
            var sideEnd = side;
            sideEnd.Width = SidePushEnd[i];
            var h2 = H2Shot(level);
            var h2End = h2;
            if (level != CloseLevel) h2End.Width = H2EndWidth; // strength 4 does not push
            var shotL = LShot(level);
            var shotM = MShot(level);
            var t = new Dictionary<string, ContiCue>();

            // ---- d3_night ----
            t[Key(Night, 1)] = new ContiCue { Start = shotL, Head1 = new ContiHead(0.4f, 0f, 4f), Gaze = ContiGaze.Free, Face1 = new ContiFace(0f, "relaxed") };
            t[Key(Night, 2)] = new ContiCue
            {
                Start = side, End = sideEnd, MoveSeconds = CupSeconds, Sfx = CupSfx, Gaze = ContiGaze.Free,
                Head1 = new ContiHead(1.10f, 3f, 0f), Face1 = new ContiFace(2.10f, "smile"),
            };
            t[Key(Night, 3)] = new ContiCue { Start = shotL, Head1 = new ContiHead(0f, 6f, 0f), Gaze = ContiGaze.Lens };
            t[Key(Night, 4)] = new ContiCue { Head1 = new ContiHead(0f, 0f, 3f), Gaze = ContiGaze.Free, Face1 = new ContiFace(0f, "smile") };
            t[Key(Night, 5)] = new ContiCue { Start = h2, End = h2End, MoveSeconds = H2PushSeconds, Head1 = new ContiHead(0f, 0f, 8f), Gaze = ContiGaze.Free, Motion = MotionName };
            t[Key(Night, 6)] = new ContiCue { Head1 = new ContiHead(1.0f, 0f, 0f) };
            t[Key(Night, 7)] = new ContiCue { Start = shotM, Head1 = new ContiHead(0f, 0f, -2f), Gaze = ContiGaze.Lens };
            t[Key(Night, 8)] = new ContiCue { Head1 = new ContiHead(0.5f, 0f, 0f) };

            // ---- d3_night_end ----
            t[Key(NightEnd, 1)] = new ContiCue { Head1 = new ContiHead(0f, 0f, 6f), Gaze = ContiGaze.Free };
            t[Key(NightEnd, 2)] = new ContiCue { Head1 = new ContiHead(0f, 0f, 3f), Face1 = new ContiFace(0f, "soft") };
            t[Key(NightEnd, 3)] = new ContiCue
            {
                End = ClimaxEnd(level), MoveDelay = ClimaxDelay, MoveSeconds = ClimaxPushSeconds(level), Overshoot = ClimaxOvershoot[i],
                Head1 = new ContiHead(0f, 0f, 0f, 0.35f), Gaze = ContiGaze.Lens,
                Face1 = new ContiFace(0f, "relaxed"), Face2 = new ContiFace(ClimaxVoiceSeconds, "smile"),
            };
            t[Key(NightEnd, 4)] = new ContiCue { Gaze = ContiGaze.Lens, Face1 = new ContiFace(0f, "relaxed") };
            t[Key(NightEnd, 5)] = new ContiCue { Head1 = new ContiHead(0f, 0f, 3f), Face1 = new ContiFace(2.5f, "smile") };
            t[Key(NightEnd, 6)] = new ContiCue { Gaze = ContiGaze.Lens, Head1 = new ContiHead(0.3f, 0f, -2f, 0.5f), Head2 = new ContiHead(1.5f, 0f, 0f, 0.8f) };
            // the head has no tilt, so the 4 degree tilt is a 3 degree yaw
            t[Key(NightEnd, 7)] = new ContiCue
            {
                End = FinalShot(level), MoveSeconds = FinalPush(level), Head1 = new ContiHead(0f, 3f, 0f, 0.8f), Gaze = ContiGaze.Lens,
                TypingFace = new ContiFace(1.4f, "soft"), Glitch = true,
            };
            return t;
        }
    }
}
