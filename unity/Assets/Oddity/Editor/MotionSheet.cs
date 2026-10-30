using System;
using System.IO;
using Oddity.Core;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Contact sheets of a motion on Harin from a wide camera (task 51): Unity.exe -executeMethod Oddity.EditorTools.MotionSheet.Run
    /// -sheetMotion Bashful -sheetDir ABS_DIR. Writes DIR/NAME_front.png and DIR/NAME_side.png, one cell per 0.25 s of the clip,
    /// upper body in frame, so arms through the body, broken fingers and odd shoulders can be judged where the phone window
    /// does not show them. Not used by the game.
    /// </summary>
    public static class MotionSheet
    {
        const float Step = 0.25f;
        const int CellW = 240, CellH = 320, Cols = 6;

        static string _name, _dir;
        static bool _started, _wasPlaying;
        static int _frames;
        static HarinPuppet _h;
        static Camera _cam;
        static RenderTexture _rt;
        static readonly System.Collections.Generic.List<Color32[]> Front = new System.Collections.Generic.List<Color32[]>();
        static readonly System.Collections.Generic.List<Color32[]> Side = new System.Collections.Generic.List<Color32[]>();
        static float _next;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            _name = Arg(args, "-sheetMotion");
            _dir = Arg(args, "-sheetDir");
            Directory.CreateDirectory(_dir);
            string save = Path.Combine(Application.temporaryCachePath, "sheet_save.json");
            File.Delete(save);
            File.Delete(save + "." + GameBootstrap.PrologueSeenFile);
            new SaveStore(save).Save(new GameState { PlayerName = "민수", Day = 1, Slot = Slot.Morning, NextOpenUtc = DateTime.UtcNow.AddMinutes(-1) });
            GameBootstrap.SavePathOverride = save;
            GameBootstrap.ClockOverride = null;
            GameBootstrap.SkipPrologue = true;
            ChatView.TypingEnabled = false;
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.update += Tick;
            EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (!_started)
                {
                    if (++_frames < 40) return;
                    _h = HarinWardrobe.ActivePuppet();
                    if (_h == null) return;
                    SetUpCamera();
                    if (!_h.PlayMotion(_name)) throw new Exception("motion " + _name + " did not start");
                    _started = true;
                    _next = 0f;
                    return;
                }
                if (_h.MotionPlaying)
                {
                    _wasPlaying = true;
                    if (_h.MotionTime >= _next)
                    {
                        _next += Step;
                        Front.Add(Shoot(0f));
                        Side.Add(Shoot(50f));
                    }
                }
                else if (_wasPlaying) Done();
            }
            catch (Exception e) { Debug.LogError("MOTIONSHEET FAIL " + e); Exit(1); }
        }

        static Bounds _bounds;

        static void SetUpCamera()
        {
            var stage = GameObject.Find("AppStageCamera");
            var go = new GameObject("SheetCam");
            _cam = go.AddComponent<Camera>();
            if (stage != null) _cam.CopyFrom(stage.GetComponent<Camera>());
            _cam.enabled = false;
            _cam.fieldOfView = 30f;
            _cam.aspect = (float)CellW / CellH;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            _rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = _rt;
            bool first = true;
            foreach (var r in _h.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (first) { _bounds = r.bounds; first = false; } else _bounds.Encapsulate(r.bounds);
            }
        }

        static Color32[] Shoot(float yaw)
        {
            float height = _bounds.size.y;
            var focus = new Vector3(_bounds.center.x, _bounds.min.y + height * 0.70f, _bounds.center.z);
            float dist = height * 0.36f / Mathf.Tan(15f * Mathf.Deg2Rad);
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * _h.transform.forward;
            _cam.transform.position = focus + dir * dist;
            _cam.transform.LookAt(focus);
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(CellW, CellH, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        static void Write(System.Collections.Generic.List<Color32[]> cells, string path)
        {
            int rows = (cells.Count + Cols - 1) / Cols;
            var sheet = new Texture2D(Cols * CellW, rows * CellH, TextureFormat.RGBA32, false);
            var clear = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(clear);
            for (int i = 0; i < cells.Count; i++)
            {
                int x = (i % Cols) * CellW, y = (rows - 1 - i / Cols) * CellH;
                sheet.SetPixels32(x, y, CellW, CellH, cells[i]);
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
        }

        static void Done()
        {
            Write(Front, Path.Combine(_dir, _name + "_front.png"));
            Write(Side, Path.Combine(_dir, _name + "_side.png"));
            Debug.Log("MOTIONSHEET_DONE cells=" + Front.Count + " length=" + _h.MotionLength);
            Exit(0);
        }

        static void Exit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }
    }
}
