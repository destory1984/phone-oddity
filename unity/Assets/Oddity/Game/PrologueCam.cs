using System;
using System.Collections.Generic;
using Oddity.Core;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>Prologue shot presets (camera_plan.md, section 4): the room, daily life and hands, not only the face.</summary>
    public static class PrologueShots
    {
        public struct Preset
        {
            public string Name;
            public float Width;    // m, across the frame at the point it looks at (horizontal)
            public float EyeLine;  // 0 = top of the screen, 1 = bottom
            public float Yaw;      // deg, camera to the side of her front (sign comes from the stage)
            public float Pitch;    // deg, + = camera above, looking down
        }

        /// <summary>R master of the room, B daily-life medium, P close-up from the other person, T two-shot, I action insert.</summary>
        public const string R = "R", B = "B", P = "P", T = "T", I = "I";

        // R: the plan says 1.15 m; the walls cap the distance (see RoomMin and RoomMax), so the real width can be a little less.
        static readonly Dictionary<string, Preset> Table = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase)
        {
            { R, new Preset { Name = R, Width = 1.15f, EyeLine = 0.27f, Yaw = 25f, Pitch = 3f } },
            { B, new Preset { Name = B, Width = 0.68f, EyeLine = 0.30f, Yaw = 20f } },
            { P, new Preset { Name = P, Width = 0.38f, EyeLine = 0.34f, Yaw = 5f } },
            { T, new Preset { Name = T, Width = 0.90f, EyeLine = 0.30f, Yaw = 30f } },
            { I, new Preset { Name = I, Width = 0.24f, EyeLine = 0.50f, Yaw = 20f, Pitch = 35f } },
        };

        public static IEnumerable<string> Names => Table.Keys;

        /// <summary>Canonical name for a script's cam value, or null when it is not a prologue shot.</summary>
        public static string Canonical(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Table.TryGetValue(name.Trim(), out var p) ? p.Name : null;
        }

        public static Preset Get(string name) => Table[name];

        /// <summary>The room the camera must stay inside: walls at x 2.0 and z 2.5, with 15 cm to spare.</summary>
        public static readonly Vector3 RoomMin = new Vector3(-1.85f, 0.3f, -2.35f), RoomMax = new Vector3(1.85f, 2.25f, 2.35f);

        /// <summary>Seconds of the smooth move between two shots: only B to P, the same axis. Everything else cuts.</summary>
        public const float MoveSeconds = 3f;

        public static float MoveBetween(string a, string b) => a == B && b == P ? MoveSeconds : 0f;

        /// <summary>Distance from the looked-at point to the camera for a width, with the lens and the screen's aspect (w / h).</summary>
        public static float DistanceFor(float width, float fov, float aspect) =>
            width * 0.5f / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect);

        /// <summary>The longest distance along dir from point that keeps the camera inside the room.</summary>
        public static float FitInRoom(Vector3 point, Vector3 dir, float distance)
        {
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(dir[a]) < 1e-4f) continue;
                float limit = dir[a] > 0f ? RoomMax[a] : RoomMin[a];
                float t = (limit - point[a]) / dir[a];
                if (t < distance) distance = Mathf.Max(0.3f, t);
            }
            return distance;
        }

        /// <summary>
        /// Camera pose for a shot looking at <paramref name="point"/> (her eyes, or the object of an insert).
        /// <paramref name="side"/> is +1 or -1: which side of her front the camera stands on.
        /// </summary>
        public static UnityEngine.Pose PoseFor(Vector3 point, Vector3 facing, float side, Preset shot, float fov, float aspect)
        {
            facing.y = 0f;
            facing = facing.sqrMagnitude < 1e-6f ? Vector3.forward : facing.normalized;
            float d = DistanceFor(shot.Width, fov, aspect);
            var around = Quaternion.AngleAxis(shot.Yaw * side, Vector3.up) * facing;
            float pitch = shot.Pitch * Mathf.Deg2Rad;
            var dir = around * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch); // from the point to the camera
            d = FitInRoom(point, dir, d);
            var rot = Quaternion.LookRotation(-dir, Vector3.up);
            float frameH = 2f * d * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var pos = point + dir * d - rot * Vector3.up * (frameH * (0.5f - shot.EyeLine));
            return new UnityEngine.Pose(pos, rot);
        }
    }

    /// <summary>
    /// Decides which prologue shot is on and when it changes. No Unity objects: time and locks come in as arguments.
    /// Rules: a line's <c>cam</c> names the shot; the cut happens at the line (never while her voice plays, 0.2 s after it ends,
    /// never while choices are up), only the latest wish is kept; only B to P moves (3 s), everything else cuts;
    /// an insert (I) starts only in silence after her voice, lasts 1.5 s and the face shot comes back (a cut);
    /// the notice and the rewind keep their own framing (<see cref="Legacy"/>).
    /// </summary>
    public class PrologueCamPlanner
    {
        public const float VoiceGapSeconds = 0.2f;
        public const float InsertSeconds = 1.5f;     // plan: 1.2 to 1.8 s
        public const float InsertMin = 1.2f, InsertMax = 1.8f;
        /// <summary>A line with no voice file (no voice ever heard): the insert waits this long after the line shows.</summary>
        public const float NoVoiceInsertDelay = 2.5f;

        /// <summary>The face shot now (R, B, P or T); null before the first line.</summary>
        public string Shot { get; private set; }
        public string From { get; private set; }
        /// <summary>Counts every shot start (first shot, cut, move, insert in, insert out).</summary>
        public int Serial { get; private set; }
        public bool InsertActive { get; private set; }
        /// <summary>Beat of the line that asked for the insert (tells the director which object to show).</summary>
        public string InsertBeat { get; private set; }
        /// <summary>True from the notice line on: the old framing is used (notice and rewind are untouched).</summary>
        public bool Legacy { get; private set; }
        public string Pending => _pending;
        public bool InsertPending => _pendingInsert;
        /// <summary>Shots keep an insert on screen.</summary>
        public bool InsertHold;

        string _pending;
        bool _pendingInsert;
        string _pendingInsertBeat;
        float _moveStart, _moveDur;
        float _voiceBusyAt = -1000f;
        float _lineStart;
        bool _sawVoice;
        float _insertEnd;

        /// <summary>A prologue line is shown. Its cam field is the wish; the first line starts the shot at once.</summary>
        public void OnLine(Line line, float now)
        {
            if (InsertActive) EndInsert(now);
            _pendingInsert = false;
            _lineStart = now;
            _sawVoice = false;
            if (line.Beat == PrologueDirector.NoticeBeat || line.Beat == PrologueDirector.RewindBeat)
            {
                Legacy = true;
                _pending = null;
                return;
            }
            string cam = PrologueShots.Canonical(line.Cam);
            if (cam == PrologueShots.I)
            {
                _pendingInsert = true;
                _pendingInsertBeat = line.Beat;
                if (Shot == null) Begin(PrologueShots.B, now);
                return;
            }
            if (Shot == null) { Begin(cam ?? PrologueShots.B, now); return; }
            if (cam == null || cam == Shot) { if (cam == Shot) _pending = null; return; }
            _pending = cam;
        }

        void Begin(string shot, float now)
        {
            From = Shot;
            Shot = shot;
            Serial++;
            _moveStart = now;
            _moveDur = 0f;
            _pending = null;
        }

        /// <summary>Every frame. Returns true when the picture changed.</summary>
        public bool Tick(float now, bool voiceBusy, bool choicesUp)
        {
            if (voiceBusy)
            {
                _voiceBusyAt = now;
                _sawVoice = true;
                if (InsertActive && !InsertHold) { EndInsert(now); return true; } // her voice never plays under an insert
            }
            if (choicesUp) return false;
            if (InsertActive)
            {
                if (!InsertHold && now >= _insertEnd) { EndInsert(now); return true; }
                return false;
            }
            bool quiet = now - _voiceBusyAt >= VoiceGapSeconds;
            bool changed = false;
            if (_pending != null && quiet)
            {
                Cut(_pending, now);
                changed = true;
            }
            if (_pendingInsert && _pending == null && quiet && MoveProgress(now) >= 1f
                && (_sawVoice || now - _lineStart >= NoVoiceInsertDelay))
            {
                StartInsert(now);
                changed = true;
            }
            return changed;
        }

        void Cut(string shot, float now)
        {
            string old = Shot;
            From = Shot;
            Shot = shot;
            Serial++;
            _moveStart = now;
            _moveDur = PrologueShots.MoveBetween(old, shot);
            _pending = null;
        }

        void StartInsert(float now)
        {
            InsertActive = true;
            InsertBeat = _pendingInsertBeat;
            _pendingInsert = false;
            _insertEnd = now + InsertSeconds;
            Serial++;
            _moveStart = now;
            _moveDur = 0f;
        }

        void EndInsert(float now)
        {
            InsertActive = false;
            Serial++;
            _moveStart = now;
            _moveDur = 0f;
        }

        /// <summary>Shots and tests: apply what is waiting at once, ignoring the locks.</summary>
        public void Settle(float now)
        {
            if (_pending != null) { Cut(_pending, now); _moveDur = 0f; }
            if (InsertActive) EndInsert(now);
        }

        /// <summary>Shots and tests: start the waiting insert at once and keep it on screen.</summary>
        public void ForceInsert(float now)
        {
            if (!_pendingInsert) return;
            InsertHold = true;
            Settle(now);
            StartInsert(now);
        }

        /// <summary>0 to 1, eased; 1 = the move is over (cuts are over at once).</summary>
        public float MoveProgress(float now)
        {
            if (_moveDur <= 0f) return 1f;
            float t = Mathf.Clamp01((now - _moveStart) / _moveDur);
            return t * t * (3f - 2f * t);
        }
    }
}
