using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Short head and eye motions for the reactions to a touch on her window (shots/plans/poke_reactions.md):
    /// each eases in, peaks and comes back within 0.6 to 1.0 s. They ride on top of the head turn and idle gaze.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float PokeMinSeconds = 0.6f, PokeMaxSeconds = 1.0f;

        struct PokeMove
        {
            public float Yaw, Pitch, Roll;       // head, degrees (yaw &gt; 0 to her left, pitch &gt; 0 down)
            public float EyeYaw, EyePitch;       // eyes, degrees
            public float Seconds;
            public bool Blink;
        }

        // 갸웃 tilt, 올려봄 look up, 눈맞춤 eye contact, 기울임 deeper tilt, 깜빡 blink, 곁눈질 sidelong glance, 돌림 turn away, 끄덕 nod
        static readonly Dictionary<string, PokeMove> PokeMoves = new Dictionary<string, PokeMove>
        {
            { "갸웃", new PokeMove { Roll = 10f, Yaw = 4f, Seconds = 0.9f } },
            { "올려봄", new PokeMove { Pitch = -5f, EyePitch = -9f, Seconds = 0.9f } },
            { "눈맞춤", new PokeMove { Pitch = 3f, Seconds = 0.6f } },
            { "기울임", new PokeMove { Roll = -10f, Pitch = 4f, Seconds = 1.0f } },
            { "깜빡", new PokeMove { Pitch = -2f, Seconds = 0.6f, Blink = true } },
            { "곁눈질", new PokeMove { Yaw = 6f, EyeYaw = 16f, Seconds = 1.0f } },
            { "돌림", new PokeMove { Yaw = 14f, EyeYaw = 20f, Seconds = 1.0f } },
            { "끄덕", new PokeMove { Pitch = 8f, Seconds = 0.8f } },
        };

        float _pokeStart = -1f;
        PokeMove _pokeMove;
        float _blinkAgainAt = -1f;

        /// <summary>True while a touch-reaction motion plays.</summary>
        public bool PokeActive => _pokeStart >= 0f;
        /// <summary>Name of the motion playing or last played ("" before the first).</summary>
        public string CurrentPokeMotion { get; private set; } = "";
        /// <summary>Head offset of the motion right now (yaw, pitch, roll degrees); zero when none plays.</summary>
        public Vector3 PokeHeadOffset { get; private set; }

        public static bool IsPokeMotion(string name) => name != null && PokeMoves.ContainsKey(name);

        /// <summary>Plays a motion by its script word; unknown words are ignored.</summary>
        public void PlayPokeMotion(string name)
        {
            if (name == null || !PokeMoves.TryGetValue(name, out var move)) return;
            _pokeMove = move;
            _pokeStart = Time.time;
            CurrentPokeMotion = name;
            _glanceStart = -1f;
            _nextGlanceAt = Time.time + Random.Range(GlanceMinGap, GlanceMaxGap) + move.Seconds;
            if (move.Blink)
            {
                if (_blinkStartedAt < 0f) _nextBlinkAt = Time.time;
                _blinkAgainAt = Time.time + 0.3f;
            }
        }

        public void StopPokeMotion()
        {
            if (_pokeStart < 0f) return;
            EndPoke();
        }

        void EndPoke()
        {
            _pokeStart = -1f;
            _blinkAgainAt = -1f;
            PokeHeadOffset = Vector3.zero;
            SetEyeAngles(0f, 0f);
        }

        float PokeK()
        {
            if (_pokeStart < 0f) return 0f;
            float u = (Time.time - _pokeStart) / Mathf.Clamp(_pokeMove.Seconds, PokeMinSeconds, PokeMaxSeconds);
            if (u >= 1f) return 0f;
            float s = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u));
            return s * s * (3f - 2f * s);
        }

        // Update: eyes and the second blink, ends the motion.
        void UpdatePoke()
        {
            if (_pokeStart < 0f) return;
            if (Time.time - _pokeStart >= Mathf.Clamp(_pokeMove.Seconds, PokeMinSeconds, PokeMaxSeconds))
            {
                EndPoke();
                return;
            }
            float k = PokeK();
            if (_blinkAgainAt >= 0f && Time.time >= _blinkAgainAt)
            {
                _blinkAgainAt = -1f;
                if (_blinkStartedAt < 0f) _nextBlinkAt = Time.time;
            }
            SetEyeAngles(_pokeMove.EyeYaw * k, _pokeMove.EyePitch * k);
            PokeHeadOffset = new Vector3(_pokeMove.Yaw * k, _pokeMove.Pitch * k, _pokeMove.Roll * k);
        }
    }
}
