using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Head and eye acting for the reactions to a touch on her window (shots/plans/poke_reactions.md).
    /// A reaction is a short chain of key poses that runs 0.8 to 1.2 s and ends looking back at the camera:
    /// cheek: eyes narrow, head and eyes turn away, look back; head: dip, tilt, look up; window: the eyes
    /// go to the touched spot and come back. The script's motion word sets the size of the move.
    /// Single motion words (eye contact while choices are up) still play as one ease in and out.
    /// It all rides on top of the head turn and idle gaze.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float PokeMinSeconds = 0.8f, PokeMaxSeconds = 1.2f;
        /// <summary>Eyes narrow this much (blink weight) at the start of a cheek reaction.</summary>
        public const float PokeSquint = 0.3f;
        /// <summary>A surprised reaction opens the eyes wide (surprised at this weight) for this long.</summary>
        public const float StartleWeight = 0.6f, StartleSeconds = 0.15f;

        struct PokeKey
        {
            public float T;                       // seconds from the start
            public float Yaw, Pitch, Roll;        // head, degrees (yaw &gt; 0 to her left, pitch &gt; 0 down)
            public float EyeYaw, EyePitch;        // eyes, degrees
            public float Squint;                  // extra blink weight 0..1
            public PokeKey(float t, float yaw = 0f, float pitch = 0f, float roll = 0f, float eyeYaw = 0f, float eyePitch = 0f, float squint = 0f)
            {
                T = t; Yaw = yaw; Pitch = pitch; Roll = roll; EyeYaw = eyeYaw; EyePitch = eyePitch; Squint = squint;
            }
        }

        struct PokeMove
        {
            public float Yaw, Pitch, Roll, EyeYaw, EyePitch, Seconds;
            public bool Blink;
        }

        // 갸웃 tilt, 올려봄 look up, 눈맞춤 eye contact, 기울임 deeper tilt, 깜빡 blink, 곁눈질 sidelong glance, 돌림 turn away, 끄덕 nod
        static readonly Dictionary<string, PokeMove> PokeMoves = new Dictionary<string, PokeMove>
        {
            { "갸웃", new PokeMove { Roll = 10f, Yaw = 4f, Seconds = 0.9f } },
            { "올려봄", new PokeMove { Pitch = -5f, EyePitch = -9f, Seconds = 0.9f } },
            { "눈맞춤", new PokeMove { Pitch = 3f, Seconds = 0.8f } },
            { "기울임", new PokeMove { Roll = -10f, Pitch = 4f, Seconds = 1.0f } },
            { "깜빡", new PokeMove { Pitch = -2f, Seconds = 0.8f, Blink = true } },
            { "곁눈질", new PokeMove { Yaw = 6f, EyeYaw = 16f, Seconds = 1.0f } },
            { "돌림", new PokeMove { Yaw = 14f, EyeYaw = 20f, Seconds = 1.0f } },
            { "끄덕", new PokeMove { Pitch = 8f, Seconds = 0.8f } },
        };

        float _pokeStart = -1f;
        PokeKey[] _pokeKeys;
        float _blinkAgainAt = -1f;
        bool _pokeHeadDirty;
        float _startleUntil = -1f;

        /// <summary>True while a touch-reaction motion plays.</summary>
        public bool PokeActive => _pokeStart >= 0f;
        /// <summary>Name of the motion playing or last played ("" before the first).</summary>
        public string CurrentPokeMotion { get; private set; } = "";
        /// <summary>Head offset of the motion right now (yaw, pitch, roll degrees); zero when none plays.</summary>
        public Vector3 PokeHeadOffset { get; private set; }
        /// <summary>Extra eye closing of the motion right now (0..1).</summary>
        public float PokeSquintNow { get; private set; }
        /// <summary>Length of the motion playing or last played (seconds).</summary>
        public float PokeSeconds { get; private set; }
        /// <summary>True during the short wide-eyed start of a surprised reaction.</summary>
        public bool Startled => _startleUntil >= 0f && Time.time < _startleUntil;

        public static bool IsPokeMotion(string name) => name != null && PokeMoves.ContainsKey(name);

        /// <summary>Plays a single motion by its script word (one ease in and out); unknown words are ignored.</summary>
        public void PlayPokeMotion(string name)
        {
            if (name == null || !PokeMoves.TryGetValue(name, out var m)) return;
            float s = Mathf.Clamp(m.Seconds, PokeMinSeconds, PokeMaxSeconds);
            StartPoke(name, new[]
            {
                new PokeKey(0f),
                new PokeKey(s * 0.5f, m.Yaw, m.Pitch, m.Roll, m.EyeYaw, m.EyePitch),
                new PokeKey(s),
            }, m.Blink);
        }

        /// <summary>
        /// Plays the reaction to a touch on <paramref name="zone"/>. <paramref name="side"/> is +1 when the
        /// touch landed on her left (screen right), -1 on her right. <paramref name="eyeYaw"/> and
        /// <paramref name="eyePitch"/> point the eyes at the touched spot (window touches).
        /// </summary>
        public void PlayPokeReaction(PokeHit zone, string motion, float side, float eyeYaw = 0f, float eyePitch = 0f)
        {
            side = side >= 0f ? 1f : -1f;
            PokeKey[] keys;
            switch (zone)
            {
                case PokeHit.Cheek:
                {
                    // eyes narrow, then the head and eyes turn away from the finger, then back to the camera
                    float yaw = motion == "돌림" ? 12f : motion == "곁눈질" ? 10f : 8f;
                    float away = -side * yaw;
                    keys = new[]
                    {
                        new PokeKey(0f),
                        new PokeKey(0.15f, pitch: 1f, squint: PokeSquint),
                        new PokeKey(0.4f, away, 2f, -side * 2f, away * 1.5f, 3f, PokeSquint * 0.6f),
                        new PokeKey(0.75f, away * 0.85f, 1.5f, -side * 1.5f, away * 0.5f, 1f, PokeSquint * 0.3f),
                        new PokeKey(1.1f),
                    };
                    break;
                }
                case PokeHit.Head:
                {
                    // a small dip under the hand, a tilt, then a look up at you
                    float roll = motion == "기울임" ? -10f : 7f * side;
                    keys = new[]
                    {
                        new PokeKey(0f),
                        new PokeKey(0.25f, pitch: 6f, squint: PokeSquint * 0.5f),
                        new PokeKey(0.55f, 2f * side, 1f, roll),
                        new PokeKey(0.85f, 1f * side, -4f, roll * 0.6f, eyePitch: -8f),
                        new PokeKey(1.15f),
                    };
                    break;
                }
                default:
                {
                    // the eyes go to the touched spot, then back to the camera
                    float nod = motion == "끄덕" ? 6f : 0f;
                    keys = new[]
                    {
                        new PokeKey(0f),
                        new PokeKey(0.2f, eyeYaw * 0.15f, 0f, 0f, eyeYaw, eyePitch),
                        new PokeKey(0.55f, eyeYaw * 0.15f, nod, 0f, eyeYaw, eyePitch),
                        new PokeKey(0.9f),
                    };
                    break;
                }
            }
            StartPoke(string.IsNullOrEmpty(motion) ? zone.ToString() : motion, keys, motion == "깜빡");
        }

        /// <summary>Wide eyes for <see cref="StartleSeconds"/> (the face underneath stays as set).</summary>
        public void Startle() => _startleUntil = Time.time + StartleSeconds;

        void StartPoke(string name, PokeKey[] keys, bool blink)
        {
            _pokeKeys = keys;
            _pokeStart = Time.time;
            PokeSeconds = keys[keys.Length - 1].T;
            CurrentPokeMotion = name;
            _glanceStart = -1f;
            _nextGlanceAt = Time.time + Random.Range(GlanceMinGap, GlanceMaxGap) + PokeSeconds;
            _blinkAgainAt = -1f;
            if (blink)
            {
                if (_blinkStartedAt < 0f) _nextBlinkAt = Time.time;
                _blinkAgainAt = Time.time + 0.3f;
            }
        }

        public void StopPokeMotion()
        {
            _startleUntil = -1f;
            if (_pokeStart < 0f) return;
            EndPoke();
        }

        void EndPoke()
        {
            _pokeStart = -1f;
            _blinkAgainAt = -1f;
            PokeHeadOffset = Vector3.zero;
            PokeSquintNow = 0f;
            _pokeHeadDirty = true; // write the head once more so it does not stay where the last frame left it
            SetEyeAngles(0f, 0f);
        }

        PokeKey SampleKeys(float t)
        {
            var k = _pokeKeys;
            if (t <= k[0].T) return k[0];
            for (int i = 1; i < k.Length; i++)
            {
                if (t > k[i].T) continue;
                float u = (t - k[i - 1].T) / Mathf.Max(1e-4f, k[i].T - k[i - 1].T);
                u = u * u * (3f - 2f * u);
                var a = k[i - 1];
                var b = k[i];
                return new PokeKey(t, Mathf.Lerp(a.Yaw, b.Yaw, u), Mathf.Lerp(a.Pitch, b.Pitch, u), Mathf.Lerp(a.Roll, b.Roll, u),
                    Mathf.Lerp(a.EyeYaw, b.EyeYaw, u), Mathf.Lerp(a.EyePitch, b.EyePitch, u), Mathf.Lerp(a.Squint, b.Squint, u));
            }
            return k[k.Length - 1];
        }

        // Update: eyes, squint and the second blink; ends the motion.
        void UpdatePoke()
        {
            if (_startleUntil >= 0f && Time.time >= _startleUntil) _startleUntil = -1f;
            if (_pokeStart < 0f) return;
            float t = Time.time - _pokeStart;
            if (t >= PokeSeconds)
            {
                EndPoke();
                return;
            }
            if (_blinkAgainAt >= 0f && Time.time >= _blinkAgainAt)
            {
                _blinkAgainAt = -1f;
                if (_blinkStartedAt < 0f) _nextBlinkAt = Time.time;
            }
            var p = SampleKeys(t);
            SetEyeAngles(p.EyeYaw, p.EyePitch);
            PokeHeadOffset = new Vector3(p.Yaw, p.Pitch, p.Roll);
            PokeSquintNow = p.Squint;
        }
    }
}
