using System;
using System.Collections.Generic;
using Oddity.Core;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>Shot presets of the messenger's Harin window (camera_plan.md, section 2).</summary>
    public static class CamShots
    {
        public struct Preset
        {
            public string Name;
            public float Width;    // m, across her eyes
            public float EyeLine;  // 0 = top of the window, 1 = bottom
            public float Yaw;      // deg, camera to the side of her front (+ = toward model +x, her left)
            public float Pitch;    // deg, + = camera above, looking down
            public float Roll;     // deg
        }

        public const string M = "M", C = "C", CLow = "Clow", E = "E", S = "S", H = "H", G = "G";

        static readonly Dictionary<string, Preset> Table = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase)
        {
            { M,    new Preset { Name = M,    Width = 0.62f, EyeLine = 0.32f } },
            { C,    new Preset { Name = C,    Width = 0.44f, EyeLine = 0.36f } },
            { CLow, new Preset { Name = CLow, Width = 0.44f, EyeLine = 0.36f, Pitch = -3f } },
            { E,    new Preset { Name = E,    Width = 0.34f, EyeLine = 0.39f } },
            { S,    new Preset { Name = S,    Width = 0.58f, EyeLine = 0.33f, Yaw = 14f } },
            { H,    new Preset { Name = H,    Width = 0.54f, EyeLine = 0.35f, Yaw = 8f, Pitch = 4f } },
            { G,    new Preset { Name = G,    Width = 0.44f, EyeLine = 0.36f, Roll = 2f } },
        };

        public static IEnumerable<string> Names => Table.Keys;

        /// <summary>Canonical name for a script's cam value, or null when it is not a shot.</summary>
        public static string Canonical(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Table.TryGetValue(name.Trim(), out var p) ? p.Name : null;
        }

        public static Preset Get(string name) => Table[name];

        public static string DefaultFor(Slot slot) => slot == Slot.Morning ? M : slot == Slot.Evening ? S : C;

        public const float MoveSeconds = 1.4f;
        public const float MoveToEmotionSeconds = 2.4f;

        /// <summary>Seconds of the smooth move between two shots on the same axis (front view), or 0 = cut.</summary>
        public static float MoveBetween(string a, string b)
        {
            var pa = Get(a);
            var pb = Get(b);
            if (!IsFront(pa) || !IsFront(pb)) return 0f;
            return a == E || b == E ? MoveToEmotionSeconds : MoveSeconds;
        }

        static bool IsFront(Preset p) => p.Yaw == 0f && p.Roll == 0f && p.Name != G;
    }

    /// <summary>
    /// Decides which shot the messenger window shows and when it changes. No Unity objects: time and locks come in
    /// as arguments, so the rules can be tested with a fake clock.
    /// Rules: a shot stays at least 8 s and 3 Harin lines (a script cam shot skips this), at most 3 changes a minute;
    /// changes happen only at a line boundary, never while her voice plays (0.2 s after it ends), never while choices are up;
    /// only the latest wish is kept; the glitch moment alone cuts to G for 0.6 s and goes back before the next line.
    /// </summary>
    public class CamPlanner
    {
        public const float MinHoldSeconds = 8f;
        public const int MinHarinLines = 3;
        public const int MaxChangesPerMinute = 3;
        public const float VoiceGapSeconds = 0.2f;
        public const float GlitchSeconds = 0.6f;
        public const float PushSeconds = 10f;
        public const float SlideSeconds = 12f;
        /// <summary>Slow drift: the width shrinks by this share ...</summary>
        public const float PushShare = 0.03f;
        /// <summary>... or the camera slides this far sideways (m).</summary>
        public const float SlideMeters = 0.004f;
        /// <summary>E opens from this day on; before it the same wish gives C.</summary>
        public const int EmotionFromDay = 3;

        public string Shot { get; private set; } = CamShots.M;
        public string From { get; private set; } = CamShots.M;
        /// <summary>Counts every shot start (slot start, change, glitch cut and its return); the view re-anchors on a change.</summary>
        public int Serial { get; private set; }
        /// <summary>Shot changes made by the rules (slot starts, forced shots and the glitch cut are not counted).</summary>
        public int Changes { get; private set; }
        public bool InGlitch { get; private set; }
        public string Pending => _pending;
        public int Day { get; private set; } = 1;
        /// <summary>Tests and shots turn the slow drift off.</summary>
        public bool DriftOn = true;
        /// <summary>Reduced motion: no drift, every change is a cut.</summary>
        public bool ReduceMotion;

        string _pending;
        bool _pendingExplicit;
        bool _boundary;
        float _shotStart, _moveStart, _moveDur;
        int _lines;
        readonly List<float> _times = new List<float>();
        float _voiceBusyAt = -1000f;
        float _lastNow;
        float _driftTime;
        int _driftKind;
        bool _reactionUsed, _emotionUsed;
        string _returnShot;
        float _glitchEnd;

        /// <summary>A slot starts: back to its default shot (morning M, evening S, night C).</summary>
        public void BeginSlot(Slot slot, int day, float now)
        {
            Day = day;
            _times.Clear();
            _reactionUsed = _emotionUsed = false;
            _pending = null;
            InGlitch = false;
            Start(CamShots.DefaultFor(slot), now, true, 0f);
        }

        /// <summary>Puts the window on a named shot right away (shots, dev tools). False for an unknown name.</summary>
        public bool Force(string name, float now)
        {
            string shot = CamShots.Canonical(name);
            if (shot == null) return false;
            _pending = null;
            InGlitch = false;
            Start(shot, now, true, 0f);
            return true;
        }

        void Start(string shot, float now, bool reset, float moveSeconds)
        {
            From = Shot;
            Shot = shot;
            Serial++;
            _moveStart = now;
            _moveDur = ReduceMotion ? 0f : moveSeconds;
            _lastNow = now;
            if (reset)
            {
                _shotStart = now;
                _lines = 0;
                _driftTime = 0f;
                _driftKind = Changes % 2;
            }
        }

        /// <summary>A script line is shown. Wishes: its cam field, else its beat's candidate (Harin lines only).</summary>
        public void OnLine(Line line, float now)
        {
            if (InGlitch) EndGlitch(now);
            bool harin = line.Who == "harin";
            if (harin) _lines++;
            string target = CamShots.Canonical(line.Cam);
            bool isExplicit = target != null;
            if (!isExplicit && harin) target = Candidate(line.Beat);
            if (target != null)
            {
                if (target == Shot) _pending = null;
                else { _pending = target; _pendingExplicit = isExplicit; }
            }
            _boundary = true;
        }

        // Messenger column of the beat table (camera_plan.md, section 5). Null = keep the shot.
        string Candidate(string beat)
        {
            switch (beat)
            {
                case "teases": case "mock_indignation": case "tilts_head":
                    return CamShots.S;
                case "proud_pout":
                    return CamShots.CLow;
                case "surprised": case "flustered": // the first important reaction only
                    if (_reactionUsed) return null;
                    _reactionUsed = true;
                    return CamShots.C;
                case "looks_down_smiling": case "avoids_eye_contact":
                    return Shot == CamShots.S ? null : CamShots.H;
                case "peeks_for_reaction": case "waits_for_answer":
                case "sad": case "brief_pause": case "leans_in": case "leans_close_then_back": case "camera_closeup":
                    return CamShots.C;
                case "stares_then_smiles": case "holds_eye_contact": case "quiet_confession": case "soft_smile":
                    if (_emotionUsed) return null; // C to E once
                    _emotionUsed = true;
                    return (Shot == CamShots.C || Shot == CamShots.CLow) && Day >= EmotionFromDay ? CamShots.E : CamShots.C;
                default:
                    return null;
            }
        }

        /// <summary>The glitch moment starts: cut to G, back after 0.6 s or at the next line.</summary>
        public void GlitchCut(float now)
        {
            if (InGlitch || Shot == CamShots.G) return;
            _returnShot = Shot;
            InGlitch = true;
            _glitchEnd = now + GlitchSeconds;
            From = Shot;
            Shot = CamShots.G;
            Serial++;
            _moveStart = now;
            _moveDur = 0f;
        }

        void EndGlitch(float now)
        {
            InGlitch = false;
            From = Shot;
            Shot = _returnShot;
            Serial++;
            _moveStart = now;
            _moveDur = 0f;
        }

        /// <summary>Called every frame. Returns true when the shot changed.</summary>
        public bool Tick(float now, bool voiceBusy, bool choicesUp)
        {
            float dt = Mathf.Max(0f, now - _lastNow);
            _lastNow = now;
            if (voiceBusy) _voiceBusyAt = now;
            if (!choicesUp && !InGlitch && MoveProgress(now) >= 1f && DriftOn && !ReduceMotion) _driftTime += dt;
            if (InGlitch && now >= _glitchEnd) { EndGlitch(now); return true; }

            if (_pending == null || !_boundary || choicesUp || InGlitch) return false;
            if (now - _voiceBusyAt < VoiceGapSeconds) return false; // her voice plays, or ended less than 0.2 s ago
            if (!_pendingExplicit)
            {
                _times.RemoveAll(t => now - t >= 60f);
                bool ok = now - _shotStart >= MinHoldSeconds && _lines >= MinHarinLines && _times.Count < MaxChangesPerMinute;
                if (!ok) { _pending = null; _boundary = false; return false; } // that wish was for that line only
            }
            string target = _pending;
            _pending = null;
            _boundary = false;
            _times.Add(now);
            Changes++;
            Start(target, now, true, CamShots.MoveBetween(Shot, target));
            return true;
        }

        /// <summary>0 to 1, eased; 1 = the move is over (cuts are over at once).</summary>
        public float MoveProgress(float now)
        {
            if (_moveDur <= 0f) return 1f;
            float t = Mathf.Clamp01((now - _moveStart) / _moveDur);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Slow drift of the shot: 0 to 1, eased, then it stays. Frozen while choices are up.</summary>
        public float Drift01
        {
            get
            {
                float t = Mathf.Clamp01(_driftTime / (DriftIsPush ? PushSeconds : SlideSeconds));
                return t * t * (3f - 2f * t);
            }
        }

        /// <summary>True: the width shrinks; false: the camera slides sideways. One kind per shot.</summary>
        public bool DriftIsPush => _driftKind == 0;
        public float DriftWidthScale => DriftIsPush && Shot != CamShots.G ? 1f - PushShare * Drift01 : 1f;
        public float DriftSlideMeters => !DriftIsPush && Shot != CamShots.G ? SlideMeters * Drift01 : 0f;
    }

    /// <summary>
    /// Moves the messenger window's camera by the plan of <see cref="CamPlanner"/>. The point it looks at is taken from
    /// her head only when a shot starts, so her breathing and glances do not make the picture sway.
    /// </summary>
    public class AppStageDirector : MonoBehaviour
    {
        public CamPlanner Planner { get; } = new CamPlanner();
        /// <summary>False during the prologue, the emergence and act 2 (the window is not drawn then).</summary>
        public bool Enabled { get; set; } = true;

        Camera _cam;
        Func<HarinPuppet> _harin;
        Func<bool> _voiceBusy;
        bool _choicesUp;
        int _serial = -1;
        Vector3 _eyes, _facing = Vector3.forward;
        UnityEngine.Pose _from;
        bool _hasAnchor;

        public string Shot => Planner.Shot;

        static float Now => Time.unscaledTime;

        public void Init(Camera cam, Func<HarinPuppet> harin, Func<bool> voiceBusy)
        {
            _cam = cam;
            _harin = harin;
            _voiceBusy = voiceBusy;
        }

        public void BeginSlot(Slot slot, int day)
        {
            Enabled = true;
            _choicesUp = false;
            Planner.BeginSlot(slot, day, Now);
            Apply(true);
        }

        public void OnLine(Line line)
        {
            if (!Enabled) return;
            _choicesUp = false;
            Planner.OnLine(line, Now);
            Apply(false);
        }

        public void SetChoices(bool up) { _choicesUp = up; }

        public void GlitchCut()
        {
            if (!Enabled) return;
            Planner.GlitchCut(Now);
            Apply(false);
        }

        /// <summary>Shots and dev tools: show this shot at once, standing still.</summary>
        public void Force(string name, bool drift = false)
        {
            Enabled = true;
            Planner.DriftOn = drift;
            if (Planner.Force(name, Now)) Apply(true);
        }

        void LateUpdate()
        {
            if (!Enabled || _cam == null) return;
            bool busy = _voiceBusy != null && _voiceBusy();
            Planner.Tick(Now, busy, _choicesUp);
            Apply(false);
        }

        // Re-anchors when the shot has changed, then sets the camera pose.
        void Apply(bool snap)
        {
            if (_cam == null) return;
            var harin = _harin != null ? _harin() : null;
            if (harin == null) return;
            if (Planner.Serial != _serial || !_hasAnchor)
            {
                var head = harin.BonePosition(HumanBodyBones.Head);
                if (head == Vector3.zero && !_hasAnchor) return;
                if (head != Vector3.zero)
                {
                    _eyes = head + Vector3.up * 0.075f * harin.transform.lossyScale.y;
                    _facing = harin.transform.forward;
                }
                _from = new UnityEngine.Pose(_cam.transform.position, _cam.transform.rotation);
                _hasAnchor = true;
                _serial = Planner.Serial;
            }
            var preset = CamShots.Get(Planner.Shot);
            var target = PoseFor(_eyes, _facing, _cam.fieldOfView, preset, Planner.DriftWidthScale, Planner.DriftSlideMeters);
            float p = snap ? 1f : Planner.MoveProgress(Now);
            var pose = p >= 1f
                ? target
                : new UnityEngine.Pose(Vector3.Lerp(_from.position, target.position, p), Quaternion.Slerp(_from.rotation, target.rotation, p));
            _cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>
        /// Camera pose for a shot: the frame is <c>width * widthScale</c> wide at her eyes, her eyes sit
        /// <c>EyeLine</c> of the way down the part of the square picture the chat window shows, and the camera stands
        /// yaw degrees to the side and pitch degrees above (or below) her front, looking at her eyes.
        /// </summary>
        public static UnityEngine.Pose PoseFor(Vector3 eyes, Vector3 facing, float fov, CamShots.Preset shot,
            float widthScale = 1f, float slideMeters = 0f)
        {
            facing.y = 0f;
            facing = facing.sqrMagnitude < 1e-6f ? Vector3.forward : facing.normalized;
            float w = shot.Width * widthScale;                                   // square picture: height = width
            float d = w * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var side = Quaternion.AngleAxis(shot.Yaw, Vector3.up) * facing;
            float pitch = shot.Pitch * Mathf.Deg2Rad;
            var dir = side * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);   // from her eyes to the camera
            var look = Quaternion.LookRotation(-dir, Vector3.up);
            float below = w * (0.5f - shot.EyeLine * ChatView.WindowShare);     // camera this far under her eyes
            var pos = eyes + dir * d - look * Vector3.up * below + look * Vector3.right * slideMeters;
            var rot = look * Quaternion.AngleAxis(shot.Roll, Vector3.forward);
            return new UnityEngine.Pose(pos, rot);
        }
    }
}
