using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Batch screenshots: Unity.exe -batchmode -projectPath unity
    ///   -executeMethod Oddity.EditorTools.Shots.Run -shotScenes a,b -shotDir ABS_DIR
    /// Per scene: open it, enter play mode (so Update-driven components run), apply the fixed
    /// setup from ShotScenes, draw the named camera into an 800px-wide RenderTexture, write
    /// DIR/NAME.png, and log "SHOT NAME ok WxH" or "SHOT NAME diff N.N%" against DIR/baseline/NAME.png.
    /// Domain reload is switched off for the run so these statics survive play mode.
    /// </summary>
    public static class Shots
    {
        const int Width = 800;
        const int WarmupFrames = 20;
        const int SettleFrames = 5;

        static Queue<ShotScene> _queue;
        static string _dir;
        static ShotScene _current;
        static int _frames;
        static int _failures;
        static bool _optionsSaved;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var names = ArgValue(args, "-shotScenes");
            _dir = ArgValue(args, "-shotDir");
            if (string.IsNullOrEmpty(names) || string.IsNullOrEmpty(_dir))
            {
                Debug.LogError("SHOT usage: -shotScenes a,b -shotDir ABS_DIR");
                Finish(1);
                return;
            }
            Directory.CreateDirectory(_dir);

            _queue = new Queue<ShotScene>();
            foreach (var n in names.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = ShotScenes.Find(n.Trim());
                if (s == null)
                {
                    Debug.LogError($"SHOT {n} FAIL unknown scene (known: {string.Join(",", ShotScenes.All.Select(x => x.Name))})");
                    _failures++;
                }
                else _queue.Enqueue(s);
            }

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
            if (_queue.Count == 0) { Finish(_failures > 0 ? 1 : 0); return; }
            _current = _queue.Dequeue();
            _frames = 0;
            try
            {
                // every scene starts as a new game, in a throwaway save file
                GameBootstrap.SavePathOverride = Path.Combine(Application.temporaryCachePath, "shot_save.json");
                File.Delete(GameBootstrap.SavePathOverride);
                File.Delete(GameBootstrap.SavePathOverride + "." + GameBootstrap.PrologueSeenFile);
                GameBootstrap.SkipPrologue = !_current.Prologue;
                if (_current.Prologue) ChatView.TypingEnabled = false; // no breath pause before the first line
                EditorSceneManager.OpenScene(_current.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Fail(e); }
        }

        static void Tick()
        {
            if (_current == null || !EditorApplication.isPlaying) return;
            _frames++;
            try
            {
                if (_frames == WarmupFrames) _current.Setup?.Invoke();
                if (_frames == WarmupFrames + SettleFrames)
                {
                    Capture(_current);
                    _current = null;
                    EditorApplication.ExitPlaymode();
                    EditorApplication.delayCall += WaitForEdit;
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static void WaitForEdit()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += WaitForEdit;
                return;
            }
            Next();
        }

        static void Fail(Exception e)
        {
            Debug.LogError($"SHOT {(_current != null ? _current.Name : "?")} FAIL {e.Message}");
            _failures++;
            _current = null;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += WaitForEdit;
        }

        static void Capture(ShotScene s)
        {
            var cam = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == s.CameraName);
            if (cam == null) throw new Exception($"camera '{s.CameraName}' not found");

            var src = cam.targetTexture;
            int height = src != null ? Mathf.RoundToInt(Width * (float)src.height / src.width) : Mathf.RoundToInt(Width / cam.aspect);
            var rt = new RenderTexture(Width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, height, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, Width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = src;
            rt.Release();

            File.WriteAllBytes(Path.Combine(_dir, s.Name + ".png"), tex.EncodeToPNG());

            var basePath = Path.Combine(_dir, "baseline", s.Name + ".png");
            if (File.Exists(basePath))
            {
                var b = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
                b.LoadImage(File.ReadAllBytes(basePath));
                Debug.Log($"SHOT {s.Name} diff {DiffPercent(tex, b):0.0}%");
                UnityEngine.Object.DestroyImmediate(b);
            }
            else Debug.Log($"SHOT {s.Name} ok {tex.width}x{tex.height}");
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static float DiffPercent(Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height) return 100f;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            long sum = 0;
            for (int i = 0; i < pa.Length; i++)
                sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (pa.Length * 3f * 255f) * 100f;
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
