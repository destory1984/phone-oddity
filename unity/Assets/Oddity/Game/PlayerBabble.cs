using System;
using Oddity.Core;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// The player's chatter: a line spoken by "player" plays as meaningless short sounds that follow the letters
    /// (synthesized by <see cref="Babble"/>, no sound files). Half as loud as Harin's voice. When Harin's voice is
    /// playing the babble waits until it ends. The clip of a line is freed when the next line or Stop comes, and
    /// when this component is destroyed.
    /// </summary>
    public class PlayerBabble : MonoBehaviour
    {
        public const float VolumeRatio = 0.5f;
        public const string SettingPref = "oddity.babble.v1";
        public const string DefaultSetting = "mid";
        /// <summary>Choices of the dev panel button, in order.</summary>
        public static readonly string[] Settings = { "low", "mid", "high", "off" };

        static string _setting;

        /// <summary>"low", "mid", "high" or "off", remembered in PlayerPrefs (default "mid").</summary>
        public static string Setting
        {
            get
            {
                if (_setting == null)
                {
                    try { _setting = PlayerPrefs.GetString(SettingPref, DefaultSetting); }
                    catch (Exception) { _setting = DefaultSetting; }
                    if (Array.IndexOf(Settings, _setting) < 0) _setting = DefaultSetting;
                }
                return _setting;
            }
            set
            {
                _setting = Array.IndexOf(Settings, value) < 0 ? DefaultSetting : value;
                try { PlayerPrefs.SetString(SettingPref, _setting); PlayerPrefs.Save(); }
                catch (Exception) { }
            }
        }

        /// <summary>Forget the cached setting so the next read goes to PlayerPrefs again (tests).</summary>
        public static void ClearSettingCache() { _setting = null; }

        /// <summary>The setting after the current one (low, mid, high, off, low ...).</summary>
        public static string NextSetting() => Settings[(Array.IndexOf(Settings, Setting) + 1) % Settings.Length];

        public static bool Enabled => Setting != "off";

        public static BabbleVoice VoiceOf(string setting)
        {
            switch (setting)
            {
                case "low": return BabbleVoice.Low;
                case "high": return BabbleVoice.High;
                default: return BabbleVoice.Mid;
            }
        }

        AudioSource _source;
        AudioClip _clip;
        string _pendingText;
        Func<bool> _ready;
        float _startedAt;

        /// <summary>Set by the game: returns true while Harin's voice is playing, so the babble waits.</summary>
        public Func<bool> HarinBusy;

        /// <summary>True while a clip plays (logically; does not need an audio device).</summary>
        public bool IsPlaying => _clip != null && Time.time - _startedAt < _clip.length;
        /// <summary>True while a line waits for Harin's voice to end.</summary>
        public bool IsPending => _pendingText != null;
        /// <summary>True when something is playing or waiting.</summary>
        public bool IsBusy => IsPending || IsPlaying;
        /// <summary>Length in seconds of the clip playing now, or 0.</summary>
        public float ClipSeconds => _clip != null ? _clip.length : 0f;

        void EnsureSource()
        {
            if (_source != null) return;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = HarinVoice.Volume * VolumeRatio;
        }

        /// <summary>
        /// Starts babbling the text (the previous line is cut). Does nothing when the setting is "off" or the text has
        /// no sound. Waits while <see cref="HarinBusy"/> says Harin is talking.
        /// </summary>
        public void Play(string text, Func<bool> ready = null)
        {
            Stop();
            if (!Enabled || string.IsNullOrEmpty(text)) return;
            _pendingText = text;
            _ready = ready;
            TryStart();
        }

        public void Stop()
        {
            _pendingText = null;
            _ready = null;
            if (_source != null) _source.Stop();
            Release();
        }

        void Release()
        {
            if (_clip == null) return;
            if (_source != null) _source.clip = null;
            Destroy(_clip);
            _clip = null;
        }

        void TryStart()
        {
            if (_pendingText == null) return;
            if (HarinBusy != null && HarinBusy()) return;
            if (_ready != null && !_ready()) return;
            string text = _pendingText;
            _pendingText = null;
            _ready = null;
            float[] samples = Babble.Render(text, VoiceOf(Setting), Babble.DefaultSampleRate);
            if (samples.Length == 0) return;
            EnsureSource();
            Release();
            _clip = AudioClip.Create("babble", samples.Length, 1, Babble.DefaultSampleRate, false);
            _clip.SetData(samples, 0);
            _startedAt = Time.time;
            _source.clip = _clip;
            _source.volume = HarinVoice.Volume * VolumeRatio;
            _source.Play();
        }

        void Update()
        {
            if (_pendingText != null) TryStart();
        }

        void OnDestroy()
        {
            _pendingText = null;
            if (_clip != null) { Destroy(_clip); _clip = null; }
        }
    }
}
