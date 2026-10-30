using System;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Plays the messenger by itself the way a person reads it, for the previs video (task 49) and its test.
    /// Call <see cref="Step"/> once per frame. It only taps and picks, it changes no game rule:
    /// Harin's line waits for the typing indicator and her voice, then <see cref="HarinHoldSeconds"/>;
    /// a player line shows <see cref="PlayerBaseSeconds"/> + <see cref="PlayerSecondsPerChar"/> per character;
    /// a system line waits for its sound effect plus <see cref="SystemAfterSfxSeconds"/>;
    /// choices show <see cref="ChoiceShowSeconds"/>, then the first is picked.
    /// The last line is not left: <see cref="Done"/> turns true <see cref="EndHoldSeconds"/> after it is fully shown.
    /// </summary>
    public sealed class PrevisAutoPlay
    {
        public const float HarinHoldSeconds = 0.8f;
        public const float PlayerBaseSeconds = 1.0f;
        public const float PlayerSecondsPerChar = 0.06f;
        public const float SystemAfterSfxSeconds = 0.5f;
        public const float SystemNoSfxSeconds = 1.5f;
        public const float ChoiceShowSeconds = 2.0f;
        public const float EndHoldSeconds = 2.0f;
        const float TapSlack = 0.05f;

        readonly GameBootstrap _boot;
        readonly ChatView _chat;
        readonly string _lastScene;
        readonly int _lastLine;
        string _key;
        string _who;
        bool _final;
        float _shownAt = -1f;
        float _idleSince = -1f;
        float _choiceSince = -1f;
        float _systemWait;
        int _textLength;
        bool _tapLogged;

        /// <summary>Called with a short text at every tap and pick, and when a new line is seen (the previs timeline log).</summary>
        public Action<string> Log;
        public bool Done { get; private set; }
        /// <summary>Lines seen so far as "scene:number", in order.</summary>
        public System.Collections.Generic.List<string> Visited { get; } = new System.Collections.Generic.List<string>();
        /// <summary>The same lines with the game time they showed.</summary>
        public System.Collections.Generic.List<float> VisitedAt { get; } = new System.Collections.Generic.List<float>();
        /// <summary>Who spoke each line seen, as the script says ("harin", "player", "system").</summary>
        public System.Collections.Generic.List<string> VisitedWho { get; } = new System.Collections.Generic.List<string>();
        /// <summary>Game time of each tap that moved the script on.</summary>
        public System.Collections.Generic.List<float> AdvancedAt { get; } = new System.Collections.Generic.List<float>();
        public int ChoicesPicked { get; private set; }

        public PrevisAutoPlay(GameBootstrap boot, ChatView chat, string lastScene, int lastLine)
        {
            _boot = boot;
            _chat = chat;
            _lastScene = lastScene;
            _lastLine = lastLine;
        }

        public static float PlayerSeconds(int characters) => PlayerBaseSeconds + PlayerSecondsPerChar * characters;

        public static float SystemSeconds(float sfxLength) => sfxLength > 0f ? sfxLength + SystemAfterSfxSeconds : SystemNoSfxSeconds;

        public void Step(float now)
        {
            if (Done) return;
            if (_chat.VisibleButtonTexts.Count > 0)
            {
                if (_choiceSince < 0f) { _choiceSince = now; Log?.Invoke("CHOICES " + string.Join(" | ", _chat.VisibleButtonTexts)); }
                if (now - _choiceSince >= ChoiceShowSeconds)
                {
                    _choiceSince = -1f;
                    ChoicesPicked++;
                    Log?.Invoke("PICK 0");
                    _chat.PickChoice(0);
                }
                return;
            }
            _choiceSince = -1f;

            string scene = _boot.CurrentSceneId;
            int number = _boot.LineNumber;
            if (scene == null || number <= 0) return;
            string key = scene + ":" + number;
            if (key != _key)
            {
                _key = key;
                _shownAt = now;
                _idleSince = -1f;
                _tapLogged = false;
                _who = _chat.IsTyping ? "harin" : _chat.CurrentLineWho;
                _textLength = _chat.CurrentLineText != null ? _chat.CurrentLineText.Length : 0;
                _final = scene == _lastScene && number == _lastLine;
                _systemWait = 0f;
                if (_who == "system")
                {
                    float len = 0f;
                    if (SceneConti.Level > 0 && SceneConti.TryGet(scene, number, SceneConti.Level, out var cue) && !string.IsNullOrEmpty(cue.Sfx))
                    {
                        var clip = Resources.Load<AudioClip>("Sfx/" + cue.Sfx);
                        if (clip != null) len = clip.length;
                    }
                    _systemWait = SystemSeconds(len);
                }
                Visited.Add(key);
                VisitedAt.Add(now);
                VisitedWho.Add(_who);
                Log?.Invoke("LINE " + key + " " + _who);
            }

            if (_who == "harin")
            {
                bool voiceBusy = _boot.Voice != null && _boot.Voice.IsBusy;
                bool ready = !_chat.IsTyping && !voiceBusy;
                if (!ready) { _idleSince = -1f; return; }
                if (_idleSince < 0f) _idleSince = now;
                float held = now - _idleSince;
                if (_final) { if (held >= EndHoldSeconds) Finish(); return; }
                if (held >= HarinHoldSeconds) Advance(now);
            }
            else if (_who == "system")
            {
                if (now - _shownAt >= _systemWait + TapSlack) Advance(now);
            }
            else
            {
                float wait = PlayerSeconds(_textLength);
                if (_final) { if (now - _shownAt >= wait + EndHoldSeconds) Finish(); return; }
                if (now - _shownAt >= wait) Advance(now);
            }
        }

        void Advance(float now)
        {
            if (!_tapLogged) { _tapLogged = true; Log?.Invoke("TAP"); AdvancedAt.Add(now); } // a tap the game holds back is tried again next frame
            _chat.Tap();
        }

        void Finish()
        {
            Done = true;
            Log?.Invoke("END " + _key);
        }
    }
}
