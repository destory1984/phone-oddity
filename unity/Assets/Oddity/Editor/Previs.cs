using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oddity.Core;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Previs video (task 49): Unity.exe -executeMethod Oddity.EditorTools.Previs.Run
    ///   -previsScene d3_night -previsLevels 1,2,3 -previsDir ABS_DIR -previsFfmpeg ABS_FFMPEG
    /// Per conti strength: a save at day 3 night, play mode, then <see cref="PrevisAutoPlay"/> reads the scene like a
    /// person while the phone camera (540x960) is filmed in real time: ffmpeg gets one frame per 1/24 s of the wall
    /// clock (a slow engine frame repeats the picture), and every sound is stamped with the same clock.
    /// Why real time: Time.captureDeltaTime moves Time.time but not Time.unscaledTime (measured 0.007 s against 0.042 s
    /// per frame), and the typing indicator, the conti camera moves and the tap hold read unscaledTime.
    /// Out: DIR/NAME_L{level}.silent.mp4 (video only), DIR/NAME_L{level}.events.tsv (the sounds that played and
    /// when, tools/dev.py mixes them in), DIR/NAME_L{level}.timeline.txt (lines, taps, sounds by frame).
    /// Log lines: "PREVIS_DONE ...", "PREVIS FAIL ...".
    /// </summary>
    public static class Previs
    {
        public const int Fps = 24;
        public const int Width = 540, Height = 960;
        const double MaxSeconds = 400.0;
        const string PrototypeScene = "Assets/Scenes/Prototype.unity";

        sealed class Target
        {
            public string Name, LastScene;
            public int Day, LastLine;
            public Slot Slot;
        }

        static readonly Target[] Targets =
        {
            new Target { Name = "d3_night", Day = 3, Slot = Slot.Night, LastScene = SceneConti.NightEnd, LastLine = 7 },
        };

        sealed class AudioEvent
        {
            public double Start, End = -1.0;
            public float Length, Volume;
            public string Wav;
        }

        static Queue<int> _levels;
        static Target _target;
        static string _dir, _ffmpeg;
        static int _level;
        static int _failures;
        static bool _recording;
        static bool _optionsSaved, _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static bool _waitingForEdit;

        // recording state
        static GameBootstrap _boot;
        static ChatView _chat;
        static Camera _cam;
        static RenderTexture _rt;
        static Texture2D _tex;
        static byte[] _buffer;
        static Process _proc;
        static StringBuilder _procErr;
        static PrevisAutoPlay _auto;
        static int _lastEngineFrame, _written, _engineFrames;
        static double _t0, _now;          // wall clock at the first filmed frame; seconds since then at this frame
        static float _timeAtStart, _unscaledAtStart;
        static double _maxClockSkew;
        static int _slowFrames;
        static readonly List<AudioEvent> _events = new List<AudioEvent>();
        static readonly StringBuilder _timeline = new StringBuilder();
        static AudioEvent _openVoice, _openBabble;
        static int _voiceId, _babbleId;
        static string _lastSfx, _lastKey;
        static readonly Dictionary<int, string> _wavs = new Dictionary<int, string>();
        static string _videoPath;

        static readonly FieldInfo VoiceClip = typeof(HarinVoice).GetField("_clip", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo BabbleClip = typeof(PlayerBabble).GetField("_clip", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string scene = ArgValue(args, "-previsScene");
            string levels = ArgValue(args, "-previsLevels");
            _dir = ArgValue(args, "-previsDir");
            _ffmpeg = ArgValue(args, "-previsFfmpeg");
            _target = Targets.FirstOrDefault(t => t.Name == scene);
            if (_target == null)
            {
                Debug.LogError("PREVIS FAIL unknown scene '" + scene + "' (known: " + string.Join(",", Targets.Select(t => t.Name)) + ")");
                Finish(1);
                return;
            }
            if (string.IsNullOrEmpty(_dir) || string.IsNullOrEmpty(_ffmpeg) || string.IsNullOrEmpty(levels))
            {
                Debug.LogError("PREVIS usage: -previsScene NAME -previsLevels 1,2,3 -previsDir ABS_DIR -previsFfmpeg ABS_FFMPEG");
                Finish(1);
                return;
            }
            Directory.CreateDirectory(_dir);
            _levels = new Queue<int>();
            foreach (var s in levels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _levels.Enqueue(Mathf.Clamp(int.Parse(s, CultureInfo.InvariantCulture), 0, 3));

            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            _optionsSaved = true;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            EditorApplication.update += Tick;
            Next();
        }

        static string ArgValue(string[] args, string key)
        {
            var i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Next()
        {
            if (_levels.Count == 0) { Finish(_failures > 0 ? 1 : 0); return; }
            _level = _levels.Dequeue();
            _recording = false;
            try
            {
                string save = Path.Combine(Application.temporaryCachePath, "previs_save.json");
                File.Delete(save);
                File.Delete(save + "." + GameBootstrap.PrologueSeenFile);
                // a save at the target slot with the clock open: the game starts the slot by itself on its first frame
                var state = new GameState { PlayerName = "민수", Day = _target.Day, Slot = _target.Slot, NextOpenUtc = DateTime.UtcNow.AddMinutes(-1) };
                new SaveStore(save).Save(state);
                GameBootstrap.SavePathOverride = save;
                GameBootstrap.ClockOverride = null;
                GameBootstrap.SkipPrologue = true;
                ChatView.TypingEnabled = true;
                HarinVoice.ResourceDirOverride = null;
                SceneConti.SetLevelForRun(_level);
                EditorSceneManager.OpenScene(PrototypeScene, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Fail(e.Message); }
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (!_recording)
                {
                    if (_levels == null || _waitingForEdit) return;
                    // film from the frame the first line shows
                    var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    if (boot != null && boot.Stage != null && boot.CurrentSceneId != null && boot.LineNumber > 0) StartRecording();
                    return;
                }
                if (Time.frameCount == _lastEngineFrame) return; // the editor ticked without a game frame
                RecordFrame();
            }
            catch (Exception e) { Fail(e.ToString()); }
        }

        // ---------------------------------------------------------------- recording

        static void StartRecording()
        {
            _boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            _chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            var main = GameObject.Find("MainCamera");
            if (_boot == null || _chat == null || main == null) throw new Exception("no GameBootstrap, ChatView or MainCamera in the scene");
            _cam = main.GetComponent<Camera>();

            _videoPath = Path.Combine(_dir, _target.Name + "_L" + _level + ".silent.mp4");
            File.Delete(_videoPath);
            StartFfmpeg(_videoPath);

            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _tex = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            _buffer = new byte[Width * Height * 3];
            if (_boot.Dev != null) { var devCanvas = _boot.Dev.GetComponent<Canvas>(); if (devCanvas != null) devCanvas.enabled = false; } // no DEV button in the picture
            _cam.aspect = (float)Width / Height;
            _cam.targetTexture = _rt; // the camera draws itself into it every frame

            _events.Clear();
            _timeline.Clear();
            _wavs.Clear();
            _openVoice = _openBabble = null;
            _voiceId = _babbleId = 0;
            _lastSfx = null;
            _lastKey = null;
            _written = _engineFrames = _slowFrames = 0;
            _maxClockSkew = 0.0;
            _t0 = Time.realtimeSinceStartupAsDouble;
            _now = 0.0;
            _timeAtStart = Time.time;
            _unscaledAtStart = Time.unscaledTime;

            _auto = new PrevisAutoPlay(_boot, _chat, _target.LastScene, _target.LastLine);
            _auto.Log = msg => _timeline.AppendLine(Stamp() + " " + msg);
            _lastEngineFrame = Time.frameCount - 1;
            _recording = true;
            Debug.Log("PREVIS start " + _target.Name + " level " + _level + " voiceSet " + HarinVoice.VoiceSet + " babble " + PlayerBabble.Setting);
        }

        static string Stamp() => "f" + Math.Max(0, _written - 1).ToString("D5") + " t=" + _now.ToString("0.000", CultureInfo.InvariantCulture) + "s";

        static void StartFfmpeg(string output)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpeg,
                Arguments = "-y -loglevel error -f rawvideo -pix_fmt rgb24 -s " + Width + "x" + Height + " -r " + Fps +
                            " -i - -vf vflip -c:v libx264 -preset veryfast -crf 27 -pix_fmt yuv420p -movflags +faststart \"" + output + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            _procErr = new StringBuilder();
            _proc = Process.Start(psi);
            if (_proc == null) throw new Exception("cannot start ffmpeg at " + _ffmpeg);
            _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (_procErr) _procErr.AppendLine(e.Data); };
            _proc.BeginErrorReadLine();
        }

        static void RecordFrame()
        {
            _lastEngineFrame = Time.frameCount;
            _engineFrames++;
            _now = Time.realtimeSinceStartupAsDouble - _t0;

            // both game clocks must run at the wall clock's speed, or the waits of the game would not match the video
            double skew = Math.Abs((Time.time - _timeAtStart) - (Time.unscaledTime - _unscaledAtStart));
            _maxClockSkew = Math.Max(_maxClockSkew, skew);
            if (_engineFrames % 1200 == 1)
                Debug.Log("PREVIS clock t=" + _now.ToString("0.00", CultureInfo.InvariantCulture) + " time " + (Time.time - _timeAtStart).ToString("0.00", CultureInfo.InvariantCulture) +
                          " unscaled " + (Time.unscaledTime - _unscaledAtStart).ToString("0.00", CultureInfo.InvariantCulture) + " engine frames " + _engineFrames);

            Poll();
            WriteFrames();
            _auto.Step(Time.time);
            if (_auto.Done) { FinishLevel(); return; }
            if (_now > MaxSeconds) Fail("recording did not end in " + (int)MaxSeconds + " s");
        }

        // One video frame per 1/24 s: the picture the camera drew in the last engine frame fills every frame due by now.
        static void WriteFrames()
        {
            int due = (int)Math.Floor(_now * Fps) + 1;
            if (due <= _written) return;
            if (due - _written > 3) _slowFrames += due - _written - 1;
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            _tex.GetRawTextureData<byte>().CopyTo(_buffer);
            if (_proc.HasExited) throw new Exception("ffmpeg stopped: " + ErrText());
            var stream = _proc.StandardInput.BaseStream;
            while (_written < due) { stream.Write(_buffer, 0, _buffer.Length); _written++; }
        }

        static string ErrText() { lock (_procErr) return _procErr.ToString().Trim(); }

        // ---------------------------------------------------------------- sounds

        static void Poll()
        {
            string key = _boot.CurrentSceneId + ":" + _boot.LineNumber;
            if (key != _lastKey) { _lastKey = key; _timeline.AppendLine(Stamp() + " (poll) line now " + key + " cam " + _boot.Stage.ContiPreset.Width.ToString("0.000", CultureInfo.InvariantCulture) + " inConti " + _boot.Stage.InConti); }

            var voice = _boot.Voice;
            if (voice != null)
            {
                AudioClip clip = voice.IsPlaying ? VoiceClip.GetValue(voice) as AudioClip : null;
                Track(ref _voiceId, ref _openVoice, clip, HarinVoice.Volume, "voice");
            }
            var babble = _boot.Babble;
            if (babble != null)
            {
                AudioClip clip = babble.IsPlaying ? BabbleClip.GetValue(babble) as AudioClip : null;
                Track(ref _babbleId, ref _openBabble, clip, HarinVoice.Volume * PlayerBabble.VolumeRatio, "babble");
            }
            string sfx = _boot.LastSfx;
            if (sfx != _lastSfx)
            {
                _lastSfx = sfx;
                var clip = Resources.Load<AudioClip>("Sfx/" + sfx);
                if (clip != null) AddEvent(clip, SceneConti.SfxVolume, "sfx " + sfx, false);
            }
        }

        static void Track(ref int id, ref AudioEvent open, AudioClip clip, float volume, string what)
        {
            int now = clip != null ? clip.GetInstanceID() : 0;
            if (now == id) return;
            if (open != null)
            {
                open.End = _now;
                _timeline.AppendLine(Stamp() + " " + what + " stop " + Path.GetFileName(open.Wav));
                open = null;
            }
            id = now;
            if (clip != null) open = AddEvent(clip, volume, what + " " + clip.name, true);
        }

        static AudioEvent AddEvent(AudioClip clip, float volume, string label, bool cuttable)
        {
            string wav;
            if (!_wavs.TryGetValue(clip.GetInstanceID(), out wav))
            {
                wav = Path.Combine(_dir, _target.Name + "_L" + _level + ".a" + _wavs.Count.ToString("D2") + ".wav");
                WriteWav(clip, wav);
                _wavs[clip.GetInstanceID()] = wav;
            }
            var ev = new AudioEvent { Start = _now, Length = clip.length, Volume = volume, Wav = wav };
            _events.Add(ev);
            _timeline.AppendLine(Stamp() + " " + label + " start len " + clip.length.ToString("0.000", CultureInfo.InvariantCulture) + " vol " + volume.ToString("0.00", CultureInfo.InvariantCulture));
            return ev;
        }

        static void WriteWav(AudioClip clip, string path)
        {
            int channels = clip.channels;
            var data = new float[clip.samples * channels];
            if (!clip.GetData(data, 0)) throw new Exception("cannot read the samples of " + clip.name);
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = data.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(clip.frequency);
                w.Write(clip.frequency * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                for (int i = 0; i < data.Length; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(data[i] * 32767f), -32768, 32767));
            }
        }

        // ---------------------------------------------------------------- end of a level

        static void FinishLevel()
        {
            string events = Path.Combine(_dir, _target.Name + "_L" + _level + ".events.tsv");
            string timeline = Path.Combine(_dir, _target.Name + "_L" + _level + ".timeline.txt");
            try
            {
                _proc.StandardInput.BaseStream.Flush();
                _proc.StandardInput.Close();
                if (!_proc.WaitForExit(180000)) { _proc.Kill(); throw new Exception("ffmpeg did not finish"); }
                if (_proc.ExitCode != 0) throw new Exception("ffmpeg exit " + _proc.ExitCode + ": " + ErrText());

                var sb = new StringBuilder();
                foreach (var e in _events)
                {
                    double cut = e.End >= 0.0 ? e.End - e.Start : e.Length;
                    double dur = Math.Min(e.Length, Math.Max(0.02, cut));
                    sb.Append(Mathf.RoundToInt((float)(e.Start * 1000.0))).Append('\t')
                      .Append(Mathf.RoundToInt((float)(dur * 1000.0))).Append('\t')
                      .Append(e.Volume.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.Wav.Replace('\\', '/')).Append('\n');
                }
                File.WriteAllText(events, sb.ToString());
                File.WriteAllText(timeline, _timeline.ToString());

                float seconds = _written / (float)Fps;
                string wall = _now.ToString("0", CultureInfo.InvariantCulture);
                Debug.Log("PREVIS_DONE " + _target.Name + " level=" + _level + " frames=" + _written + " seconds=" + seconds.ToString("0.00", CultureInfo.InvariantCulture) +
                          " sounds=" + _events.Count + " engineFrames=" + _engineFrames + " maxClockSkew=" + _maxClockSkew.ToString("0.0000", CultureInfo.InvariantCulture) +
                          " slowFrames=" + _slowFrames + " wall=" + wall + "s video=" + _videoPath.Replace('\\', '/') + " events=" + events.Replace('\\', '/'));
                Debug.Log("PREVIS lines " + string.Join(",", _auto.Visited));
            }
            catch (Exception e) { Fail(e.Message); return; }
            LeavePlay();
        }

        static void Fail(string message)
        {
            Debug.LogError("PREVIS FAIL " + (_target != null ? _target.Name : "?") + " level " + _level + ": " + message);
            _failures++;
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch (Exception) { }
            LeavePlay();
        }

        static void LeavePlay()
        {
            _recording = false;
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); _rt = null; }
            if (_tex != null) { UnityEngine.Object.DestroyImmediate(_tex); _tex = null; }
            _proc = null;
            _waitingForEdit = true;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += WaitForEdit;
        }

        static void WaitForEdit()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += WaitForEdit;
                return;
            }
            _waitingForEdit = false;
            Next();
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            if (_optionsSaved)
            {
                EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
                EditorSettings.enterPlayModeOptions = _oldOptions;
                AssetDatabase.SaveAssets();
            }
            EditorApplication.Exit(code);
        }
    }
}
