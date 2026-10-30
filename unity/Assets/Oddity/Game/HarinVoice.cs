using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Placeholder voice for Harin's lines. Clips are loaded from Resources/Voice (the folder lives under
    /// Assets/Oddity/Audio/Placeholder, which is git-ignored): "harin_&lt;sceneId&gt;_&lt;lineNumber&gt;_&lt;set&gt;" for a whole
    /// line, where the voice set (default "harinA") is picked in the dev panel and kept in PlayerPrefs;
    /// otherwise a short sound picked by face or beat, half of the time. Missing files are silently skipped,
    /// so a clone without the placeholder audio plays without sound. While a clip plays, its loudness
    /// (RMS over 0.05 s) opens Harin's mouth ("aa" 0 to 0.8, with a little "ih" and "ou").
    /// </summary>
    public class HarinVoice : MonoBehaviour
    {
        public const string DefaultResourceDir = "Voice/";
        public const float Volume = 0.9f;
        public const float SfxChance = 0.5f;
        public const string DefaultVoiceSet = "harinA";
        public static readonly string[] VoiceSets = { "harinA", "harinB", "harinC", "harinD", "karina" };
        const string VoiceSetPref = "oddity.voiceSet";
        static string _voiceSet;
        const float WindowSeconds = 0.05f;
        const float MouthGain = 4f;
        const float MouthMax = 0.8f;

        /// <summary>Tests point this at an empty folder to check the silent path.</summary>
        public static string ResourceDirOverride;

        static readonly Dictionary<string, string[]> FaceSfx = new Dictionary<string, string[]>
        {
            { "happy", new[] { "laugh_1", "laugh_2", "giggle" } },
            { "neutral", new[] { "hm", "itjanha" } },
            { "relaxed", new[] { "eung", "breath" } },
            { "surprised", new[] { "eo", "huh" } },
            { "sad", new[] { "a", "sigh" } },
            { "angry", new[] { "pout" } },
        };

        static readonly Dictionary<string, string> BeatSfx = new Dictionary<string, string>
        {
            { "teases", "giggle" },
            { "proud_pout", "pout" },
            { "mock_indignation", "pout" },
            { "soft_laugh", "laugh_1" },
            { "laughs", "laugh_2" },
            { "holds_back_laughter", "giggle" },
            { "flustered", "eo" },
        };

        readonly Dictionary<string, float[]> _samples = new Dictionary<string, float[]>();
        AudioSource _source;
        HarinPuppet _harin;
        /// <summary>When set, the mouth moves on the puppet this returns (the model worn now) instead of the one from Init.</summary>
        public System.Func<HarinPuppet> Follow;
        HarinPuppet Puppet => Follow != null ? Follow() : _harin;
        AudioClip _clip;
        AudioClip _pending;
        Func<bool> _ready;
        float _startedAt;
        float _aa;
        bool _hasLast;
        string _lastScene, _lastFace, _lastBeat;
        int _lastNumber;

        /// <summary>Voice set used for file names (remembered in PlayerPrefs).</summary>
        public static string VoiceSet
        {
            get
            {
                if (_voiceSet == null)
                {
                    try { _voiceSet = PlayerPrefs.GetString(VoiceSetPref, DefaultVoiceSet); }
                    catch (Exception) { _voiceSet = DefaultVoiceSet; }
                    if (string.IsNullOrEmpty(_voiceSet)) _voiceSet = DefaultVoiceSet;
                }
                return _voiceSet;
            }
            set
            {
                _voiceSet = string.IsNullOrEmpty(value) ? DefaultVoiceSet : value;
                try { PlayerPrefs.SetString(VoiceSetPref, _voiceSet); PlayerPrefs.Save(); }
                catch (Exception) { }
            }
        }

        /// <summary>Clip every voice set records; a set counts as present when this clip loads.</summary>
        public const string ProbeClip = "harin_sfx_breath_";

        /// <summary>True when the set's files are in the voice folder.</summary>
        public static bool HasVoiceSet(string set) =>
            !string.IsNullOrEmpty(set) && Resources.Load<AudioClip>(Dir + ProbeClip + set) != null;

        /// <summary>
        /// The next set after the current one in <see cref="VoiceSets"/> whose files are present
        /// (sets without files are skipped). With no files at all it is simply the next in the list.
        /// </summary>
        public static string NextVoiceSet()
        {
            int i = Array.IndexOf(VoiceSets, VoiceSet);
            for (int k = 1; k <= VoiceSets.Length; k++)
            {
                var set = VoiceSets[(i + k + VoiceSets.Length) % VoiceSets.Length];
                if (HasVoiceSet(set)) return set;
            }
            return VoiceSets[(i + 1) % VoiceSets.Length];
        }

        /// <summary>Resource name of a whole-line clip in the current voice set.</summary>
        public static string LineClipName(string sceneId, int lineNumber) =>
            "harin_" + sceneId + "_" + lineNumber + "_" + VoiceSet;

        static string Dir => string.IsNullOrEmpty(ResourceDirOverride) ? DefaultResourceDir : ResourceDirOverride;

        /// <summary>True while the AudioSource itself is playing (false without an audio device).</summary>
        public bool SourcePlaying => _source != null && _source.isPlaying;
        /// <summary>True while a clip is (logically) playing; does not depend on an audio device.</summary>
        public bool IsPlaying => _clip != null && Time.time - _startedAt < _clip.length;
        /// <summary>Name of the clip playing or waiting, or null.</summary>
        public string CurrentClip => _clip != null ? _clip.name : _pending != null ? _pending.name : null;

        public void Init(HarinPuppet harin)
        {
            _harin = harin;
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = Volume;
        }

        /// <summary>
        /// Plays the line's own clip if there is one, else (half of the time) a short sound for the face or beat.
        /// <paramref name="ready"/> delays the start (the messenger waits for the typing indicator).
        /// </summary>
        public void PlayLine(string sceneId, int lineNumber, string face, string beat, Func<bool> ready = null)
        {
            Stop();
            _hasLast = true;
            _lastScene = sceneId;
            _lastNumber = lineNumber;
            _lastFace = face;
            _lastBeat = beat;
            var clip = Load(LineClipName(sceneId, lineNumber));
            if (clip == null && UnityEngine.Random.value < SfxChance)
            {
                string sfx = PickSfx(face, beat);
                // a set without its own short sounds borrows the default set's
                if (sfx != null) clip = Load("harin_sfx_" + sfx + "_" + VoiceSet) ?? Load("harin_sfx_" + sfx + "_" + DefaultVoiceSet);
            }
            if (clip == null) return;
            _pending = clip;
            _ready = ready;
            TryStart();
        }

        /// <summary>Resource name of the clip for poke.json line <paramref name="number"/> (1 to 18) in the current set.</summary>
        public static string PokeClipName(int number) => "harin_poke_" + number.ToString("00") + "_" + VoiceSet;

        /// <summary>
        /// Plays the touch reaction's own clip (no short sound in its place). Sets without poke clips
        /// (karina) and missing files stay silent. Returns true when a clip started.
        /// </summary>
        public bool PlayPoke(int number)
        {
            Stop();
            if (number <= 0) return false;
            var clip = Load(PokeClipName(number));
            if (clip == null) return false;
            _pending = clip;
            _ready = null;
            TryStart();
            return _clip != null;
        }

        /// <summary>Plays the last line again (after switching the voice set).</summary>
        public void Replay()
        {
            if (_hasLast) PlayLine(_lastScene, _lastNumber, _lastFace, _lastBeat);
        }

        /// <summary>Plays a clip right away (tests use generated clips).</summary>
        public void PlayClip(AudioClip clip)
        {
            Stop();
            _pending = clip;
            _ready = null;
            TryStart();
        }

        public void Stop()
        {
            _pending = null;
            _ready = null;
            _clip = null;
            _aa = 0f;
            if (_source != null) _source.Stop();
            { var p = Puppet; if (p != null) p.SetMouth(0f, 0f, 0f); }
        }

        static string PickSfx(string face, string beat)
        {
            if (!string.IsNullOrEmpty(beat) && BeatSfx.TryGetValue(beat, out var b)) return b;
            if (FaceSfx.TryGetValue((face ?? "neutral").ToLowerInvariant(), out var list))
                return list[UnityEngine.Random.Range(0, list.Length)];
            return null;
        }

        static AudioClip Load(string name)
        {
            try { return Resources.Load<AudioClip>(Dir + name); }
            catch (Exception) { return null; }
        }

        void TryStart()
        {
            if (_pending == null || (_ready != null && !_ready())) return;
            _clip = _pending;
            _pending = null;
            _ready = null;
            _startedAt = Time.time;
            if (_source == null) Init(_harin);
            _source.clip = _clip;
            _source.volume = Volume;
            _source.Play();
        }

        void Update()
        {
            if (_pending != null) TryStart();
            if (_clip == null) return;
            if (!IsPlaying)
            {
                _clip = null;
                _aa = 0f;
                { var p = Puppet; if (p != null) p.SetMouth(0f, 0f, 0f); }
                return;
            }
            float t = Time.time - _startedAt;
            float rms = Rms(SamplesOf(_clip), _clip.channels, _clip.frequency, t, WindowSeconds);
            float target = Mathf.Min(MouthMax, rms * MouthGain);
            _aa = Mathf.Lerp(_aa, target, 0.6f);
            float ih = _aa * 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 9f));
            float ou = _aa * 0.25f * (0.5f + 0.5f * Mathf.Sin(t * 6f + 1f));
            { var p = Puppet; if (p != null) p.SetMouth(_aa, ih, ou); }
        }

        float[] SamplesOf(AudioClip clip)
        {
            string key = clip.GetInstanceID().ToString();
            if (_samples.TryGetValue(key, out var data)) return data;
            data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) data = new float[0];
            _samples[key] = data;
            return data;
        }

        /// <summary>RMS of the first channel over <paramref name="window"/> seconds starting at <paramref name="time"/>.</summary>
        public static float Rms(float[] data, int channels, int frequency, float time, float window)
        {
            if (data == null || data.Length == 0 || channels <= 0 || frequency <= 0) return 0f;
            int frames = data.Length / channels;
            int start = Mathf.Clamp((int)(time * frequency), 0, frames);
            int end = Mathf.Clamp(start + Mathf.Max(1, (int)(window * frequency)), 0, frames);
            if (end <= start) return 0f;
            double sum = 0;
            for (int i = start; i < end; i++)
            {
                float v = data[i * channels];
                sum += v * v;
            }
            return (float)Math.Sqrt(sum / (end - start));
        }
    }
}
