using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UniVRM10;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest
{
    /// <summary>
    /// Task 54, a throwaway look test: a 2D painted room, a distant 3D Harin (C19), two 2D cut-ins. Batch mode:
    /// Unity.exe -executeMethod Oddity.RoomTest.RoomTest.Run -roomDir ABS_DIR -roomFfmpeg ABS_FFMPEG
    /// Plays an empty scene, builds everything in <see cref="RoomTestRunner"/>, films 16 s at 30 fps with a fixed time step
    /// and writes DIR/room_test.silent.mp4 (tools/dev.py cuts the contact sheet). Log lines: ROOMTEST_DONE, ROOMTEST FAIL.
    /// </summary>
    public static class RoomTest
    {
        public const string ModelPath = "Assets/Models/test/harin-indoor-C48.vrm";
        public const string ArtDir = "Assets/Oddity/RoomTest/Art";
        public static GameObject Prefab;
        public static string OutDir, Ffmpeg, ModelOverride, DebugClip, DebugShots;
        public static string Model => string.IsNullOrEmpty(ModelOverride) ? ModelPath : ModelOverride;
        public static bool Finished;
        static int _exitCode;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static bool _created;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            OutDir = Arg(args, "-roomDir");
            Ffmpeg = Arg(args, "-roomFfmpeg");
            ModelOverride = Arg(args, "-roomModel") ?? Environment.GetEnvironmentVariable("ODDITY_MODEL");
            DebugClip = Arg(args, "-roomClip");   // arm debugging: one clip on its own, stills at 1, 4, 7, 11 s into -roomShots
            DebugShots = Arg(args, "-roomShots");
            if (DebugClip != null) Ffmpeg = Ffmpeg ?? "none";
            if (string.IsNullOrEmpty(OutDir) || string.IsNullOrEmpty(Ffmpeg)) { Debug.LogError("ROOMTEST usage: -roomDir ABS_DIR -roomFfmpeg ABS_FFMPEG"); EditorApplication.Exit(1); return; }
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();
            Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (Prefab == null) { Debug.LogError("ROOMTEST FAIL no model at " + Model); EditorApplication.Exit(1); return; }

            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        static void Tick()
        {
            if (EditorApplication.isPlaying && !_created)
            {
                _created = true;
                new GameObject("RoomTestRunner").AddComponent<RoomTestRunner>();
                return;
            }
            if (Finished && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= Tick;
                EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
                EditorSettings.enterPlayModeOptions = _oldOptions;
                EditorApplication.Exit(_exitCode);
            }
        }

        public static void End(int code)
        {
            _exitCode = code;
            Finished = true;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    [DefaultExecutionOrder(-50)] // after HarinPuppet (-100), before the VRM runtime's spring bones
    public class RoomTestRunner : MonoBehaviour
    {
        // ---- frame and clock
        const int W = 1920, H = 1080, Fps = 30, WarmUp = 30;
        const float MinLength = 22f, DebugLength = 11.1f;
        float Length = MinLength; // grows if the slower walk ends late (BuildMotion)

        // ---- camera and figure
        const float Fov = 20f;              // vertical, a long lens
        const float PitchDeg = 17f;         // camera looks down at the floor a little
        const float FigureFrac = 0.60f;     // her height / frame height
        const float FeetX = 0.50f, FeetY = 0.85f; // feet position in the frame (from the left, from the top)
        const float RoomCropTopPx = 60f;    // room_clean.png is 1536x1024: the 16:9 window starts 100 px down

        // ---- timeline

        Camera _cam;
        HarinPuppet _harin;
        Animator _anim;
        Transform _head, _hips, _spine, _upper, _lower, _hand, _middle, _eyeL, _eyeR;
        Transform _cutBored, _cutSmug;
        RenderTexture _rt;
        Texture2D _tex;
        byte[] _buffer;
        Process _proc;
        StringBuilder _err = new StringBuilder();
        int _frame = -WarmUp;
        float _t;
        float _upperLen, _lowerLen;
        float _frameH, _cutDist = 1f;
        Vector3 _outward;

        // ---- task 58: walk practice. ODDITY_WALKONLY=1; ODDITY_STRIDE, ODDITY_LIFT (metres, unset = no cap), ODDITY_ARMS
        bool _walkOnly;
        float _stride = 1f, _lift = -1f, _arms = 1f;
        const float WalkXFar = -1.1f, WalkXNear = 0.7f, WalkOnlyLength = 20f, TurnLen = 0.6f, Sink = 0.015f, MaxToeUp = 15f;
        // task 61: ODDITY_RAW=1 (on top of WALKONLY) plays the clip untouched: nothing of ours writes a bone, rate 1, root at the clip's own speed.
        // ODDITY_FOOTIK=1 turns on the Humanoid foot IK of the walk playable. ODDITY_FOOTDIAG=1 logs foot muscles and rest poses (VRM against the FBX's own avatar).
        bool _rawMode, _footIK;
        readonly List<float> _lowest = new List<float>();   // raw mode: per frame, the lowest sole point (heel or ball) of either foot above the floor
        readonly List<float>[] _lowestF = { new List<float>(), new List<float>() };
        float WalkRate => _rawMode ? 1f : _walkOnly ? 0.8f : 0.7f;   // task 57: unhurried; clip rate and root speed both scale by it
        Transform[] _toes = new Transform[2];
        float[] _toeFloor = new float[2];
        float _toeP0;
        Transform[] _armT; Quaternion[] _armMean;
        int _wPhase, _wLeg; float _wPh, _legT = 99f, _slowT = -1f;
        readonly List<float> _pathVx = new List<float>();
        // task 60: upper body and foot roll. ODDITY_FRONT=1 walks along Z; ODDITY_OLDFEET=1 / ODDITY_OLDTORSO=1 bring the task 59 layers back (for before numbers)
        bool _front, _oldFeet, _oldTorso;
        const float FrontStart = -1.0f, FrontEnd = 0.8f;
        float _phL;
        readonly float[] _ps = { 0f, 0.5f }, _pe = { 0.6f, 1.1f };
        readonly Vector3[] _ofsH = new Vector3[2], _ofsB = new Vector3[2], _fvLocal = new Vector3[2];
        readonly float[] _ang0 = new float[2], _toeP0i = new float[2], _rH = { 0.03f, 0.03f };

        static float Env(string k, float d)
        {
            var v = Environment.GetEnvironmentVariable(k);
            return !string.IsNullOrEmpty(v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;
        }

        void Awake()
        {
            _walkOnly = Environment.GetEnvironmentVariable("ODDITY_WALKONLY") == "1" && RoomTest.DebugClip == null;
            if (_walkOnly)
            {
                _stride = Env("ODDITY_STRIDE", 1f); _lift = Env("ODDITY_LIFT", -1f); _arms = Env("ODDITY_ARMS", 1f);
                Length = WalkOnlyLength;
                _front = Environment.GetEnvironmentVariable("ODDITY_FRONT") == "1";
                _oldFeet = Environment.GetEnvironmentVariable("ODDITY_OLDFEET") == "1";
                _oldTorso = Environment.GetEnvironmentVariable("ODDITY_OLDTORSO") == "1";
                _rawMode = Environment.GetEnvironmentVariable("ODDITY_RAW") == "1";
                _footIK = Environment.GetEnvironmentVariable("ODDITY_FOOTIK") == "1";
                _rootYaw = _front ? 0f : -WalkYaw;   // side walk starts at frame right and goes to frame left first
                _rootPos = _front ? new Vector3(0f, 0f, FrontStart) : new Vector3(WalkXFar, 0f, 0f);
            }
            _skin = Environment.GetEnvironmentVariable("ODDITY_SKINTEST") == "1" && RoomTest.DebugClip == null;
            Time.captureDeltaTime = 1f / Fps;
            UnityEngine.Random.InitState(54);
            try { Build(); }
            catch (Exception e) { Fail(e.ToString()); }
        }

        void Fail(string msg)
        {
            Debug.LogError("ROOMTEST FAIL " + msg);
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch (Exception) { }
            enabled = false;
            RoomTest.End(1);
        }

        // ------------------------------------------------------------------ scene

        void Build()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.66f, 0.64f, 0.66f);
            RenderSettings.skybox = null;

            // the key light: from the window at the upper left, low and soft, no shadows
            var lightGo = new GameObject("KeyLight");
            var key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.94f, 0.84f);
            key.intensity = 0.55f;
            key.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            _keyLight = key;

            // Harin, built inactive so HarinPuppet's Awake sees the Vrm10Instance before it runs
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            var go = Instantiate(RoomTest.Prefab, holder.transform);
            go.name = "Harin";
            _harin = go.AddComponent<HarinPuppet>();
            // the clips move the arms, so the puppet must not hold them down (its Awake has not run yet)
            typeof(HarinPuppet).GetField("lowerArms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(_harin, false);
            go.transform.SetParent(null, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            Destroy(holder);
            go.SetActive(true);
            _anim = go.GetComponent<Animator>();
            _head = _anim.GetBoneTransform(HumanBodyBones.Head);
            _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
            _upper = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            _middle = _anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _eyeL = _anim.GetBoneTransform(HumanBodyBones.LeftEye);
            _eyeR = _anim.GetBoneTransform(HumanBodyBones.RightEye);
            _toeRest[0] = _anim.GetBoneTransform(HumanBodyBones.LeftToes) != null ? _anim.GetBoneTransform(HumanBodyBones.LeftToes).localRotation : Quaternion.identity;
            _toeRest[1] = _anim.GetBoneTransform(HumanBodyBones.RightToes) != null ? _anim.GetBoneTransform(HumanBodyBones.RightToes).localRotation : Quaternion.identity;
            _harin.SetIdle(true);
            _harin.SetFace("neutral");

            _vrm = go.GetComponent<Vrm10Instance>();
            Measure(go);
            _toes[0] = _anim.GetBoneTransform(HumanBodyBones.LeftToes);
            _toes[1] = _anim.GetBoneTransform(HumanBodyBones.RightToes);
            {
                var f0 = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftFoot), _anim.GetBoneTransform(HumanBodyBones.RightFoot) };
                float p = 0f; int n = 0;
                for (int i = 0; i < 2; i++)
                    if (_toes[i] != null) { _toeFloor[i] = _toes[i].position.y; p += ToePitch(_toes[i].position - f0[i].position); n++; }
                _toeP0 = n > 0 ? p / n : 0f;
                var kneeB = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
                float ankleB = _harin.AnkleHeight * go.transform.lossyScale.y;
                for (int i = 0; i < 2; i++)
                {
                    var fb = f0[i];
                    var fh = _toes[i] != null ? _toes[i].position - fb.position : Vector3.forward;
                    fh.y = 0f; fh = fh.sqrMagnitude > 1e-8f ? fh.normalized : Vector3.forward;
                    // the heel is a ball of radius _rH, the ball of the foot a ball of radius _toeFloor (the toe bone's height): both roll on the floor
                    var heelC = fb.position - fh * 0.05f - Vector3.up * (ankleB - _rH[i]);
                    var rb = Quaternion.Inverse(fb.rotation);
                    _ofsH[i] = rb * (heelC - fb.position);
                    _fvLocal[i] = _toes[i] != null ? rb * (_toes[i].position - fb.position) : rb * (fh * 0.12f - Vector3.up * (ankleB - 0.023f));
                    _ofsB[i] = _fvLocal[i];
                    if (_toes[i] == null) _toeFloor[i] = 0.023f;
                    _toeP0i[i] = ToePitch(fb.rotation * _fvLocal[i]);
                    _ang0[i] = Vector3.Angle(kneeB[i].position - fb.position, fb.rotation * _fvLocal[i]);
                }
                Debug.Log("ROOMTEST bind pitch per foot " + _toeP0i[0].ToString("0.0", CultureInfo.InvariantCulture) + " / " + _toeP0i[1].ToString("0.0", CultureInfo.InvariantCulture));
                Debug.Log("ROOMTEST bind pose toes " + (n == 2 ? "found" : "MISSING") + ", ankle->toes pitch " + _toeP0.ToString("0.0", CultureInfo.InvariantCulture) + " deg, toe floor " +
                          _toeFloor[0].ToString("0.000", CultureInfo.InvariantCulture) + "/" + _toeFloor[1].ToString("0.000", CultureInfo.InvariantCulture) + ", ankle " + _harin.AnkleHeight.ToString("0.000", CultureInfo.InvariantCulture));
            }

            _vrmRest = RestMetrics(_anim);
            if (_d65) { var r0 = new LeanAcc(); LeanSample(_anim, Vector3.forward, r0, true); Debug.Log("ROOMTEST d65 lean VRM rest " + LeanText(r0)); }
            if (_walkOnly)
            {
                _floorCheck = Environment.GetEnvironmentVariable("ODDITY_FLOORCHECK") == "1";
                _noFloorFix = Environment.GetEnvironmentVariable("ODDITY_NOFLOORFIX") == "1";
                BuildSoleSamples(go);
                if (_floorCheck) { _fcR = go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null).ToArray(); _fcM = _fcR.Select(r => new Mesh()).ToArray(); }
            }
            // camera
            var camGo = new GameObject("Camera");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 80f;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _cam.targetTexture = _rt;
            _cam.aspect = (float)W / H;
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _buffer = new byte[W * H * 3];
            _head0 = _head.rotation;
            _chest0 = (_anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? _anim.GetBoneTransform(HumanBodyBones.Chest)).rotation;
            if (Environment.GetEnvironmentVariable("ODDITY_BONES") == "1") BuildMannequin();
            FitCamera();
            BuildMotion();
            if (_rawMode) _harin.enabled = false;   // raw: the puppet's own LateUpdate (breath, lean, head, arms) writes no bone
            if (RoomTest.DebugClip != null) Diagnose(go);

            // painted backdrop (cover-fit, cropped to 16:9 from RoomCropTopPx), the blob shadow, the two cut-ins
            _frameH = 2f * _cutDist * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            MakeBackdrop();
            MakeShadow();
            _cutBored = MakeCut("cut_bored.png");
            _cutSmug = MakeCut("cut_smug.png");

            if (RoomTest.DebugClip != null) { Directory.CreateDirectory(RoomTest.DebugShots); return; }
            if (_skin) { Directory.CreateDirectory(SkinRawDir); return; }   // skin compare: stills only, no ffmpeg
            string video = Path.Combine(RoomTest.OutDir, "room_test.silent.mp4");
            File.Delete(video);
            var psi = new ProcessStartInfo
            {
                FileName = RoomTest.Ffmpeg,
                Arguments = "-y -loglevel error -f rawvideo -pix_fmt rgb24 -s " + W + "x" + H + " -r " + Fps +
                            " -i - -vf vflip -c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -movflags +faststart \"" + video + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            _proc = Process.Start(psi);
            if (_proc == null) throw new Exception("cannot start ffmpeg at " + RoomTest.Ffmpeg);
            _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (_err) _err.AppendLine(e.Data); };
            _proc.BeginErrorReadLine();
        }

        float _topY, _heightM;
        Vrm10Instance _vrm;

        // Real height of the model in head units. Logged once.
        void Measure(GameObject go)
        {
            var sb = new StringBuilder();
            float top = float.MinValue, bottom = float.MaxValue;
            float chin = float.MaxValue, faceTop = float.MinValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                r.BakeMesh(mesh, true);
                var verts = mesh.vertices;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var v in verts)
                {
                    float y = r.transform.TransformPoint(v).y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if (r.name == "Body")
                {
                    var hp = _hand.position;
                    for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    {
                        int near = 0, all = 0;
                        foreach (var idx in mesh.GetIndices(sm)) { all++; if (Vector3.Distance(r.transform.TransformPoint(verts[idx]), hp) < 0.07f) near++; }
                        var mt = r.sharedMaterials[sm];
                        sb.Append("  submesh ").Append(sm).Append(' ').Append(mt.name).Append(" idx ").Append(all).Append(" near hand ").Append(near)
                          .Append(" shade ").Append(mt.HasProperty("_ShadeColor") ? mt.GetColor("_ShadeColor").ToString() : "-").Append(" base ").AppendLine(mt.HasProperty("_Color") ? mt.GetColor("_Color").ToString() : "-");
                    }
                }
                string mats = string.Join("|", r.sharedMaterials.Select(x => x != null ? x.name : "null"));
                sb.Append("  ").Append(r.name).Append(" y ").Append(minY.ToString("0.000", CultureInfo.InvariantCulture)).Append("..").Append(maxY.ToString("0.000", CultureInfo.InvariantCulture)).Append(" mats ").AppendLine(mats);
                if (r.name.StartsWith("Hair")) top = Mathf.Max(top, maxY);
                if (maxY > top && !r.name.Contains("Hair")) top = Mathf.Max(top, maxY);
                bottom = Mathf.Min(bottom, minY);
                if (r.name == "Face") { chin = minY; faceTop = maxY; }
                Destroy(mesh);
            }
            _topY = top;
            _heightM = top - Mathf.Min(0f, bottom);
            float headH = top - chin;
            Debug.Log("ROOMTEST model parts\n" + sb);
            Debug.Log("ROOMTEST height " + _heightM.ToString("0.000", CultureInfo.InvariantCulture) + " m, bottom " + bottom.ToString("0.000", CultureInfo.InvariantCulture) +
                      ", top " + top.ToString("0.000", CultureInfo.InvariantCulture) + ", chin " + chin.ToString("0.000", CultureInfo.InvariantCulture) +
                      ", face top " + faceTop.ToString("0.000", CultureInfo.InvariantCulture) +
                      ", head " + headH.ToString("0.000", CultureInfo.InvariantCulture) + " m, heads tall (hair top) " + (_heightM / headH).ToString("0.00", CultureInfo.InvariantCulture) +
                      ", heads tall (face top) " + (_heightM / (faceTop - chin)).ToString("0.00", CultureInfo.InvariantCulture));
        }

        // Long lens, looking down PitchDeg; moves back and aims until her feet and head top land where the frame wants them.
        void FitCamera()
        {
            var look = new Vector3(0f, _heightM * 0.45f, 0f);
            float dist = 10f;
            var dir = new Vector3(0f, Mathf.Sin(PitchDeg * Mathf.Deg2Rad), Mathf.Cos(PitchDeg * Mathf.Deg2Rad));
            var tr = _cam.transform;
            var feet = Vector3.zero;
            var headTop = new Vector3(0f, _topY, 0f);
            for (int i = 0; i < 12; i++)
            {
                tr.position = look + dir * dist;
                tr.rotation = Quaternion.LookRotation(look - tr.position, Vector3.up);
                var vf = _cam.WorldToViewportPoint(feet);
                var vh = _cam.WorldToViewportPoint(headTop);
                float h = vh.y - vf.y;
                dist *= h / FigureFrac;
                float worldH = 2f * dist * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                float worldW = worldH * W / H;
                look += tr.right * ((vf.x - FeetX) * worldW) + tr.up * ((vf.y - (1f - FeetY)) * worldH);
            }
            tr.position = look + dir * dist;
            tr.rotation = Quaternion.LookRotation(look - tr.position, Vector3.up);
            var f = _cam.WorldToViewportPoint(feet);
            var t = _cam.WorldToViewportPoint(headTop);
            Debug.Log("ROOMTEST camera dist " + dist.ToString("0.00", CultureInfo.InvariantCulture) + " m, feet at " + f.x.ToString("0.000", CultureInfo.InvariantCulture) + "," + (1f - f.y).ToString("0.000", CultureInfo.InvariantCulture) +
                      ", figure " + (t.y - f.y).ToString("0.000", CultureInfo.InvariantCulture) + " of the frame");
        }

        static Texture2D Load(string file)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!tex.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "Oddity/RoomTest/Art", file)))) throw new Exception("cannot load " + file);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Transform MakeQuad(string name, Texture2D tex, string shader)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            var mat = new Material(Shader.Find(shader)) { mainTexture = tex };
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return q.transform;
        }

        void MakeBackdrop()
        {
            var tex = Load("room_clean.png");
            var q = MakeQuad("Backdrop", tex, "Unlit/Texture");
            const float dist = 40f;
            float frameH = 2f * dist * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float frameW = frameH * W / H;
            float quadW = frameW, quadH = frameW * tex.height / tex.width; // cover: the image is 3:2, taller than 16:9
            // the visible window's centre sits at pixel row RoomCropTopPx + (frame height in image rows) / 2
            float rowsShown = tex.height * (frameH / quadH);
            float centreRow = RoomCropTopPx * tex.height / 1024f + rowsShown * 0.5f;
            float shiftUp = (centreRow - tex.height * 0.5f) / tex.height * quadH;
            q.SetParent(_cam.transform, false);
            q.localPosition = new Vector3(0f, shiftUp, dist);
            q.localScale = new Vector3(quadW, quadH, 1f);
        }

        void MakeShadow()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
                    tex.SetPixel(x, y, new Color(0.08f, 0.05f, 0.05f, a * 0.55f));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            var q = MakeQuad("BlobShadow", tex, "Unlit/Transparent");
            q.rotation = Quaternion.Euler(90f, 0f, 0f); // lying on the floor, facing up
            q.position = new Vector3(0f, 0.004f, 0.02f);
            q.localScale = new Vector3(0.95f, 0.5f, 1f);
            _shadow = q;
        }

        Transform MakeCut(string file)
        {
            var q = MakeQuad(file, Load(file), "Unlit/Transparent");
            q.SetParent(_cam.transform, false);
            float h = _frameH * 0.34f, w = h;
            float margin = _frameH * 0.03f;
            float frameW = _frameH * W / H;
            q.localPosition = new Vector3(frameW * 0.5f - margin - w * 0.5f, -_frameH * 0.5f + margin + h * 0.5f, _cutDist);
            q.localScale = Vector3.zero;
            q.gameObject.SetActive(true);
            _cutSize = h;
            return q;
        }

        float _cutSize;
        bool _done;

        // ------------------------------------------------------------------ timeline

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static float Ramp(float t, float a, float b) => S((t - a) / (b - a));

        // Task 56: after the idle she turns to frame right, walks WalkDist metres and turns back to the camera.
        const float Fade = 0.4f, WalkTurn0 = 17.0f, WalkTurnLen = 0.5f, WalkT0 = 17.3f, WalkDist = 1.1f, TurnBackLen = 0.5f;
        const float ChinUp = 6f, GlanceYaw = 30f, GlanceIn = 0.3f, GlanceHold = 0.4f, GlanceOut = 0.4f, EyeLead = 0.15f;
        const float EndStand = 1.5f;                       // she stands facing the camera at least this long at the end
        const float WalkYaw = -90f; // camera looks along -Z, so frame right is world -X and a yaw of -90 points her that way
        static readonly Vector3 WalkDir = new Vector3(-1f, 0f, 0f);

        PlayableGraph _graph;
        AnimationClipPlayable _bored, _walk;
        AnimationMixerPlayable _mixer;
        float _boredLen, _walkLen, _closeEyes;
        float _walkSpeed, _holdT, _walkW, _rootYaw, _walkClipT, _endT, _glanceStart;
        Vector3 _rootPos;
        Transform _shadow;

        static AnimationClip Clip(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Oddity/DevMotion/Resources/Motion/" + name + ".fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            throw new Exception("no clip in Assets/Oddity/DevMotion/Resources/Motion/" + name + ".fbx");
        }

        // The whole body from two Mixamo Humanoid clips: FemaleIdle, and HarinWalk crossfaded in for the walk.
        void BuildMotion()
        {
            var cb = Clip(RoomTest.DebugClip ?? Environment.GetEnvironmentVariable("ODDITY_IDLE") ?? "FemaleIdle");
            var cw = Clip(RoomTest.DebugClip != null ? RoomTest.DebugClip : (Environment.GetEnvironmentVariable("ODDITY_WALK") ?? "HarinWalk"));
            foreach (var rn in (Environment.GetEnvironmentVariable("ODDITY_LEGREST") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) LegRest(rn);
            _boredLen = cb.length;
            _walkLen = cw.length;
            _anim.applyRootMotion = false;
            _graph = PlayableGraph.Create("RoomTestMotion");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "motion", _anim);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _bored = AnimationClipPlayable.Create(_graph, cb);
            _walk = AnimationClipPlayable.Create(_graph, cw);
            _bored.SetApplyFootIK(false);
            _walk.SetApplyFootIK(_footIK);
            _graph.Connect(_bored, 0, _mixer, 0);
            _graph.Connect(_walk, 0, _mixer, 1);
            output.SetSourcePlayable(_mixer);
            Debug.Log("ROOMTEST clips Idle " + _boredLen.ToString("0.00", CultureInfo.InvariantCulture) + " s, Walk " + _walkLen.ToString("0.00", CultureInfo.InvariantCulture) + " s");
            if (RoomTest.DebugClip != null) return;
            // MotionImport bakes the clip's XZ travel into the pose (lockRootPositionXZ), so averageSpeed is 0 and the hips
            // walk away from the root. Measure that travel and take it back out in Motion(); the root then carries it instead.
            float avg = cw.averageSpeed.magnitude;
            float feet = MeasureWalkSpeed(out _walkTravel);
            float travelSpeed = _walkTravel.magnitude / _walkLen;
            _walkSpeed = (feet > 0.1f ? feet : travelSpeed) * WalkRate;   // the planted foot sets the speed: WalkSad's is 15% under its hips travel
            if (_rawMode) _walkSpeed = travelSpeed;                       // raw: the clip's own hips travel per second, nothing measured from the feet
            if (_rawMode) Debug.Log("ROOMTEST raw clip " + cw.name + " footIK " + _footIK + " averageSpeed " + avg.ToString("0.000", CultureInfo.InvariantCulture) + " hips travel " + V(_walkTravel) + " m per " + _walkLen.ToString("0.00", CultureInfo.InvariantCulture) +
                                    " s -> root speed " + _walkSpeed.ToString("0.000", CultureInfo.InvariantCulture) + " m/s (planted-foot median speed after the travel is taken out: " + feet.ToString("0.000", CultureInfo.InvariantCulture) + " m/s)");
            if (Environment.GetEnvironmentVariable("ODDITY_LEGCYCLE") == "1") LegCycle(cw.name);
            if (Environment.GetEnvironmentVariable("ODDITY_FOOTDIAG") == "1") FootDiag(cw);
            if (_d65) Diag65Lean(cw);
            DeriveFix65();
            if (_walkOnly)
            {
                _walkSpeed *= _stride;                                      // shorter steps, same cadence: the planted foot must not skate
                Debug.Log("ROOMTEST walkonly stride " + _stride.ToString("0.00", CultureInfo.InvariantCulture) + " lift " + (_lift > 0f ? _lift.ToString("0.000", CultureInfo.InvariantCulture) : "none") +
                          " arms " + _arms.ToString("0.00", CultureInfo.InvariantCulture) + " rate " + WalkRate.ToString("0.00", CultureInfo.InvariantCulture) + " root speed " + _walkSpeed.ToString("0.000", CultureInfo.InvariantCulture) +
                          " m/s (clip planted-foot " + feet.ToString("0.000", CultureInfo.InvariantCulture) + " m/s before rate), length " + Length.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                return;
            }
            _holdT = Mathf.Max(0f, WalkDist / _walkSpeed - Fade);   // the two smoothstep ramps cover Fade metres-worth of time together
            float back0 = WalkT0 + 2f * Fade + _holdT;
            _endT = back0 + TurnBackLen;
            _glanceStart = (WalkT0 + back0) * 0.5f - (GlanceIn + GlanceHold + GlanceOut) * 0.5f;
            Length = Mathf.Max(MinLength, _endT + EndStand + 1.5f); // the last camera beat sits 0.7 s..1.4 s after the walk, then 1.5 s of standing
            Debug.Log("ROOMTEST walk clip " + cw.name + " rate " + WalkRate + " dist " + WalkDist + " end position x " + (-WalkDist).ToString("0.00", CultureInfo.InvariantCulture) + " length " + Length.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Debug.Log("ROOMTEST walk speed averageSpeed " + avg.ToString("0.000", CultureInfo.InvariantCulture) + " m/s, hips travel " + V(_walkTravel) + " m per " + _walkLen.ToString("0.00", CultureInfo.InvariantCulture) +
                      " s = " + travelSpeed.ToString("0.000", CultureInfo.InvariantCulture) + " m/s, planted-foot speed after taking the travel out " + feet.ToString("0.000", CultureInfo.InvariantCulture) +
                      " m/s, used " + _walkSpeed.ToString("0.000", CultureInfo.InvariantCulture) + " m/s, hold " + _holdT.ToString("0.00", CultureInfo.InvariantCulture) + " s, walk ends " +
                      (WalkT0 + 2f * Fade + _holdT).ToString("0.00", CultureInfo.InvariantCulture) + " s, turned back at " + (WalkT0 + 2f * Fade + _holdT + TurnBackLen).ToString("0.00", CultureInfo.InvariantCulture) + " s");
        }

        // task 62: the leg rest pose of an FBX's own Avatar, as loaded and with every muscle at 0, left against right (outward = away from the other leg)
        string LegDirs(Animator a)
        {
            var sb = new StringBuilder();
            var root = a.transform;
            Vector3 P(HumanBodyBones b) => a.GetBoneTransform(b).position;
            for (int f = 0; f < 2; f++)
            {
                bool left = f == 0;
                var hip = P(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var other = P(left ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
                var kn = P(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var an = P(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var tb = a.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                float side = Mathf.Sign(root.InverseTransformVector(hip - other).x);
                string D(Vector3 from, Vector3 to)
                {
                    var l = root.InverseTransformVector(to - from).normalized;
                    return "fwd " + (Mathf.Atan2(l.z, -l.y) * Mathf.Rad2Deg).ToString("0.0", CultureInfo.InvariantCulture) + " out " + (Mathf.Atan2(side * l.x, -l.y) * Mathf.Rad2Deg).ToString("0.0", CultureInfo.InvariantCulture);
                }
                sb.Append(Environment.NewLine + "    ").Append(left ? "L" : "R").Append(" thigh ").Append(D(hip, kn)).Append(" | shin ").Append(D(kn, an))
                  .Append(" | knee flex ").Append((180f - Vector3.Angle(hip - kn, an - kn)).ToString("0.0", CultureInfo.InvariantCulture));
                if (tb != null)
                {
                    var l = root.InverseTransformVector(tb.position - an);
                    sb.Append(" | foot pitch ").Append(ToePitch(l).ToString("0.0", CultureInfo.InvariantCulture)).Append(" shin-foot angle ").Append(Vector3.Angle(kn - an, tb.position - an).ToString("0.0", CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        void LegRest(string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Oddity/DevMotion/Resources/Motion/" + name + ".fbx");
            if (src == null) { Debug.Log("ROOMTEST legrest " + name + " no FBX"); return; }
            var inst = Instantiate(src, new Vector3(50f, 0f, 0f), Quaternion.identity);
            var an = inst.GetComponent<Animator>();
            var av = an != null ? an.avatar : null;
            Debug.Log("ROOMTEST legrest " + name + " avatar " + (av == null ? "NULL" : av.name + " valid " + av.isValid + " human " + av.isHuman) + " hasTransformHierarchy " + (an != null && an.hasTransformHierarchy));
            if (av != null && av.isValid && av.isHuman)
            {
                Debug.Log("ROOMTEST legrest " + name + " AS LOADED (node pose in the FBX)" + LegDirs(an));
                var h = new HumanPoseHandler(av, inst.transform);
                var hp = new HumanPose();
                h.GetHumanPose(ref hp);
                for (int i = 0; i < hp.muscles.Length; i++) hp.muscles[i] = 0f;
                h.SetHumanPose(ref hp);
                Debug.Log("ROOMTEST legrest " + name + " MUSCLES 0 (the Avatar's own zero pose)" + LegDirs(an));
                h.Dispose();
            }
            Destroy(inst);
        }

        // task 62: one clean cycle at 240 samples (no video aliasing), left against right: knee flexion, hip flexion, ankle angle, foot pitch, stance floor height
        void LegCycle(string name)
        {
            _mixer.SetInputWeight(0, 0f); _mixer.SetInputWeight(1, 1f);
            const int n = 240;
            string F(float x) => x.ToString("0.0", CultureInfo.InvariantCulture);
            var root = _harin.transform;
            var res = new List<string>();
            var sole = new float[2][]; var kn = new float[2][]; var hp = new float[2][]; var ag = new float[2][]; var pt = new float[2][];
            for (int f = 0; f < 2; f++) { sole[f] = new float[n]; kn[f] = new float[n]; hp[f] = new float[n]; ag[f] = new float[n]; pt[f] = new float[n]; }
            for (int i = 0; i < n; i++)
            {
                _walk.SetTime(i * _walkLen / n); _graph.Evaluate(0f);
                for (int f = 0; f < 2; f++)
                {
                    bool left = f == 0;
                    var h = _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
                    var k = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
                    var ft = _anim.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    kn[f][i] = 180f - Vector3.Angle(h - k, ft.position - k);
                    var l = root.InverseTransformVector(k - h);
                    hp[f][i] = Mathf.Atan2(l.z, -l.y) * Mathf.Rad2Deg;
                    var tv = _toes[f].position - ft.position;
                    pt[f][i] = ToePitch(tv) - _toeP0i[f];
                    ag[f][i] = _ang0[f] - Vector3.Angle(k - ft.position, tv);
                    sole[f][i] = Mathf.Min(HeelPt(f, ft.position, ft.rotation).y, BallPt(f, ft.position, ft.rotation).y);
                }
            }
            float floor = Mathf.Min(sole[0].Min(), sole[1].Min());
            for (int f = 0; f < 2; f++)
            {
                var order = Enumerable.Range(0, n).OrderBy(i => sole[f][i]).Take(n * 4 / 10).ToList();   // the lowest 40 % of the cycle = this foot's stance
                string R(float[] a) => F(a.Min()) + ".." + F(a.Max()) + " (range " + F(a.Max() - a.Min()) + ", mean " + F(a.Average()) + ")";
                Debug.Log("ROOMTEST legcycle " + name + " " + (f == 0 ? "L" : "R") + " knee flexion " + R(kn[f]) + " | hip flexion " + R(hp[f]) + " | ankle angle (dorsi +) " + R(ag[f]) + " | foot pitch (toes up +) " + R(pt[f]) +
                          " | stance foot pitch mean " + F(order.Average(i => pt[f][i])) + " | stance sole height above clip's lowest " + ((order.Average(i => sole[f][i]) - floor) * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm");
            }
            _walk.SetTime(0);
        }

        // task 62: per leg ranges over the full-speed frames: knee flexion, hip flexion (thigh swing, forward +, in the figure's frame)
        readonly float[][] _kneeR = { new[] { 999f, -999f }, new[] { 999f, -999f } }, _hipR = { new[] { 999f, -999f }, new[] { 999f, -999f } };

        void LegSample()
        {
            var root = _harin.transform;
            for (int f = 0; f < 2; f++)
            {
                bool left = f == 0;
                var hip = _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
                var kn = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
                var an = _anim.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
                float kf = 180f - Vector3.Angle(hip - kn, an - kn);
                var l = root.InverseTransformVector(kn - hip);
                float hf = Mathf.Atan2(l.z, -l.y) * Mathf.Rad2Deg;
                _kneeR[f][0] = Mathf.Min(_kneeR[f][0], kf); _kneeR[f][1] = Mathf.Max(_kneeR[f][1], kf);
                _hipR[f][0] = Mathf.Min(_hipR[f][0], hf); _hipR[f][1] = Mathf.Max(_hipR[f][1], hf);
            }
        }

        void LegReport()
        {
            string F(float x) => x.ToString("0.0", CultureInfo.InvariantCulture);
            for (int f = 0; f < 2; f++)
                Debug.Log("ROOMTEST legs " + (f == 0 ? "L" : "R") + " knee flexion " + F(_kneeR[f][0]) + ".." + F(_kneeR[f][1]) + " (range " + F(_kneeR[f][1] - _kneeR[f][0]) + ") | hip flexion " + F(_hipR[f][0]) + ".." + F(_hipR[f][1]) + " (range " + F(_hipR[f][1] - _hipR[f][0]) + ")");
        }

        // task 61: why does a foot sit toes-up through the stance? The same clip on the FBX's own Humanoid avatar and on the VRM, at the SAME clip times
        // (the lowest quarter of the FBX ankle heights, per foot): foot muscles, the clip's own muscle curves, the ankle->toe pitch against each model's own rest pose.
        static string Ang(Vector3 e) => e.x.ToString("0.0", CultureInfo.InvariantCulture) + "," + e.y.ToString("0.0", CultureInfo.InvariantCulture) + "," + e.z.ToString("0.0", CultureInfo.InvariantCulture);
        string _vrmRest = "";

        // rest = the pose as loaded, no animation yet
        string RestMetrics(Animator a)
        {
            var sb = new StringBuilder();
            for (int f = 0; f < 2; f++)
            {
                var ankle = a.GetBoneTransform(f == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var toe = a.GetBoneTransform(f == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                var knee = a.GetBoneTransform(f == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                if (toe == null) { sb.Append(" | no toe bone"); continue; }
                var rb = Quaternion.Inverse(ankle.rotation);
                sb.Append(f == 0 ? " | L " : " | R ").Append(ankle.name).Append("->").Append(toe.name).Append(" (toe parent ").Append(toe.parent == ankle ? "foot" : toe.parent.name).Append(") pitch ").Append(ToePitch(toe.position - ankle.position).ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" dist ").Append((toe.position - ankle.position).magnitude.ToString("0.000", CultureInfo.InvariantCulture)).Append(" ankle y ").Append((ankle.position.y - a.transform.position.y).ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(" shin->foot dir angle ").Append(Vector3.Angle(knee.position - ankle.position, toe.position - ankle.position).ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" foot world euler ").Append(Ang((Quaternion.Inverse(a.transform.rotation) * ankle.rotation).eulerAngles))
                  .Append(" toe dir in foot bone space ").Append(V(rb * (toe.position - ankle.position).normalized));
            }
            return sb.ToString();
        }

        void FootDiag(AnimationClip cw)
        {
            string name = Environment.GetEnvironmentVariable("ODDITY_WALK") ?? "HarinWalk";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Oddity/DevMotion/Resources/Motion/" + name + ".fbx");
            var inst = Instantiate(src, new Vector3(50f, 0f, 0f), Quaternion.identity);
            var an = inst.GetComponent<Animator>();
            Debug.Log("ROOMTEST footdiag " + name + " REST FBX (as loaded)" + RestMetrics(an));
            Debug.Log("ROOMTEST footdiag " + name + " REST VRM (as loaded)" + _vrmRest);
            var g = PlayableGraph.Create("footdiag");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var cp = AnimationClipPlayable.Create(g, cw);
            cp.SetApplyFootIK(_footIK);
            AnimationPlayableOutput.Create(g, "o", an).SetSourcePlayable(cp);
            var mus = new List<int>(); var musN = new List<string>();
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                for (int d = 0; d < 3; d++) { int m = HumanTrait.MuscleFromBone((int)b, d); if (m >= 0) { mus.Add(m); musN.Add(HumanTrait.MuscleName[m]); } }
            int n = Mathf.Max(8, Mathf.RoundToInt(cw.length * Fps));
            var stance = new List<int>[2];
            for (int f = 0; f < 2; f++)
            {
                var ankle = an.GetBoneTransform(f == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var ys = new float[n];
                for (int i = 0; i < n; i++) { cp.SetTime(i * cw.length / n); g.Evaluate(0f); ys[i] = ankle.position.y; }
                stance[f] = Enumerable.Range(0, n).OrderBy(i => ys[i]).Take(Mathf.Max(3, n / 4)).ToList();
            }
            var curves = new Dictionary<string, AnimationCurve>();
            foreach (var bd in AnimationUtility.GetCurveBindings(cw)) if (musN.Contains(bd.propertyName)) curves[bd.propertyName] = AnimationUtility.GetEditorCurve(cw, bd);
            foreach (var pair in new[] { ("FBX-own-avatar", an, false), ("VRM", _anim, true) })
            {
                var a = pair.Item2;
                var h = new HumanPoseHandler(a.avatar, a.transform);
                var hp = new HumanPose();
                for (int f = 0; f < 2; f++)
                {
                    var ankle = a.GetBoneTransform(f == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    var toe = a.GetBoneTransform(f == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                    var pit = new List<float>(); var ms = new List<float[]>(); var cm = new List<float[]>();
                    foreach (int i in stance[f])
                    {
                        float tm = i * cw.length / n;
                        if (pair.Item3) { _walk.SetTime(tm); _graph.Evaluate(0f); } else { cp.SetTime(tm); g.Evaluate(0f); }
                        h.GetHumanPose(ref hp);
                        pit.Add(ToePitch(toe.position - ankle.position));
                        ms.Add(mus.Select(m => hp.muscles[m]).ToArray());
                        cm.Add(musN.Select(k => curves.ContainsKey(k) ? curves[k].Evaluate(tm) : float.NaN).ToArray());
                    }
                    var sb = new StringBuilder();
                    for (int k = 0; k < mus.Count; k++)
                        if (musN[k].StartsWith(f == 0 ? "Left" : "Right"))
                            sb.Append("\n    ").Append(musN[k]).Append(": model ").Append(ms.Average(r => r[k]).ToString("0.00", CultureInfo.InvariantCulture)).Append(" | clip curve ").Append(float.IsNaN(cm[0][k]) ? "n/a" : cm.Average(r => r[k]).ToString("0.00", CultureInfo.InvariantCulture));
                    Debug.Log("ROOMTEST footdiag " + name + " ik" + (_footIK ? 1 : 0) + " " + pair.Item1 + " " + (f == 0 ? "L" : "R") + " stance(" + pit.Count + " frames, same clip times) mean ankle->toe pitch " + pit.Average().ToString("0.0", CultureInfo.InvariantCulture) + " deg" + sb);
                }
                h.Dispose();
            }
            g.Destroy();
            Destroy(inst);
            _walk.SetTime(0);
        }

        Vector3 _walkTravel;
        float _clipSole = float.MaxValue, _clipAnkle;   // raw: the clip's own lowest sole point and lowest ankle over one cycle (world y, root at 0): what "planted" means for this clip

        // Plays the walk alone for one cycle. travel: how far the hips go in the root's frame over the cycle (XZ). Returns the median
        // speed of whichever foot is on the floor once that travel is taken out, in the root's frame (should equal travel / length).
        float MeasureWalkSpeed(out Vector3 travel)
        {
            _mixer.SetInputWeight(0, 0f);
            _mixer.SetInputWeight(1, 1f);
            var root = _anim.transform;
            var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            var ft = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftFoot), _anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            int n = Mathf.Max(8, Mathf.RoundToInt(_walkLen * Fps));
            float dt = _walkLen / n;
            var pos = new Vector3[2][];
            pos[0] = new Vector3[n + 1];
            pos[1] = new Vector3[n + 1];
            var hp = new Vector3[n + 1];
            float minY = float.MaxValue;
            for (int i = 0; i <= n; i++)
            {
                _walk.SetTime(i * dt);
                _graph.Evaluate(0f);
                hp[i] = root.InverseTransformPoint(hips.position);
                for (int f = 0; f < 2; f++)
                {
                    _clipSole = Mathf.Min(_clipSole, Mathf.Min(HeelPt(f, ft[f].position, ft[f].rotation).y, BallPt(f, ft[f].position, ft[f].rotation).y));
                    pos[f][i] = root.InverseTransformPoint(ft[f].position);
                    minY = Mathf.Min(minY, pos[f][i].y);
                }
            }
            travel = hp[n] - hp[0];
            travel.y = 0f;
            _clipAnkle = minY - _harin.AnkleHeight * root.lossyScale.y;
            Debug.Log("ROOMTEST clip's own lowest sole point " + _clipSole.ToString("0.000", CultureInfo.InvariantCulture) + " m, lowest ankle minus ankle height " + _clipAnkle.ToString("0.000", CultureInfo.InvariantCulture) + " m (0 = the floor)");
            {
                var dirT = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, dirT);
                float lo = float.MaxValue, hi = float.MinValue, yLo = float.MaxValue, yHi = float.MinValue;
                var lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                for (int i = 0; i <= n; i++)
                {
                    var dv = hp[i] - hp[0] - travel * (i / (float)n); dv.y = 0f;
                    float sd = Vector3.Dot(dv, side); lo = Mathf.Min(lo, sd); hi = Mathf.Max(hi, sd);
                    _walk.SetTime(i * dt); _graph.Evaluate(0f);
                    var line = root.InverseTransformVector(ru.position - lu.position);
                    float yw = Mathf.Atan2(-line.z, line.x) * Mathf.Rad2Deg; yLo = Mathf.Min(yLo, yw); yHi = Mathf.Max(yHi, yw);
                }
                Debug.Log("ROOMTEST walk straightness hips sideways drift " + (hi - lo).ToString("0.000", CultureInfo.InvariantCulture) + " m, body yaw " + yLo.ToString("0.0", CultureInfo.InvariantCulture) + ".." + yHi.ToString("0.0", CultureInfo.InvariantCulture) +
                          " deg (range " + (yHi - yLo).ToString("0.0", CultureInfo.InvariantCulture) + "), travel direction " + V(dirT));
            }
            var speeds = new System.Collections.Generic.List<float>();
            for (int f = 0; f < 2; f++)
                for (int i = 1; i <= n; i++)
                {
                    if (pos[f][i].y - minY > 0.02f || pos[f][i - 1].y - minY > 0.02f) continue;
                    var d = (pos[f][i] - travel * (i / (float)n)) - (pos[f][i - 1] - travel * ((i - 1) / (float)n));
                    d.y = 0f;
                    speeds.Add(d.magnitude / dt);
                }
            if (_walkOnly)
            {
                _armT = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm), _anim.GetBoneTransform(HumanBodyBones.RightUpperArm), _anim.GetBoneTransform(HumanBodyBones.RightLowerArm) };
                var sum = new Vector4[4]; var first = new Quaternion[4];
                for (int i = 0; i < n; i++)
                {
                    _walk.SetTime(i * dt); _graph.Evaluate(0f);
                    for (int a = 0; a < 4; a++)
                    {
                        var q = _armT[a].localRotation;
                        if (i == 0) first[a] = q;
                        float sg = Quaternion.Dot(first[a], q) < 0f ? -1f : 1f;
                        sum[a] += new Vector4(q.x, q.y, q.z, q.w) * sg;
                    }
                }
                _armMean = new Quaternion[4];
                for (int a = 0; a < 4; a++) { var v = sum[a].normalized; _armMean[a] = new Quaternion(v.x, v.y, v.z, v.w); }
            }
            if (_walkOnly)
            {
                // when does each foot land in the clip: the moment it is farthest ahead of the hips along the travel, as a share of the cycle (task 60)
                var fwdT = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
                for (int f = 0; f < 2; f++)
                {
                    float bestA = float.MinValue; int bi = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float al = Vector3.Dot(pos[f][i] - hp[i], fwdT);
                        if (al > bestA) { bestA = al; bi = i; }
                    }
                    _ps[f] = bi / (float)n;
                }
                _phL = _ps[0];
                Debug.Log("ROOMTEST clip foot landings (farthest ahead of the hips): left " + _ps[0].ToString("0.000", CultureInfo.InvariantCulture) + ", right " + _ps[1].ToString("0.000", CultureInfo.InvariantCulture) + " (share of the cycle)");
            }
            _walk.SetTime(0);
            if (speeds.Count < 4) return 0f;
            speeds.Sort();
            return speeds[speeds.Count / 2];
        }

        void Motion(float t)
        {
            if (RoomTest.DebugClip != null)
            {
                _bored.SetTime(t % _boredLen);
                _mixer.SetInputWeight(0, 1f);
                _mixer.SetInputWeight(1, 0f);
                _graph.Evaluate(0f);
                if (Mathf.Abs(t - 1f) < 0.01f || Mathf.Abs(t - 4f) < 0.01f || Mathf.Abs(t - 7f) < 0.01f || Mathf.Abs(t - 11f) < 0.01f)
                    Debug.Log("ROOMTEST diag on-" + RoomTest.Model + " " + RoomTest.DebugClip + " t" + t + " " + HipsLocal(_anim));
                return;
            }
            if (_walkOnly) { MotionWalkOnly(t); return; }
            float rampEnd = WalkT0 + Fade, rd = rampEnd + _holdT, back = rd + Fade;
            // one schedule for the clip weight and the root speed: up over Fade, hold, down over Fade
            float sp = Ramp(t, WalkT0, rampEnd) - Ramp(t, rd, back);
            _walkW = sp;
            _rootYaw = WalkYaw * (Ramp(t, WalkTurn0, WalkTurn0 + WalkTurnLen) - Ramp(t, back, back + TurnBackLen));
            if (t > 0f) _rootPos += WalkDir * (_walkSpeed * sp / Fps);
            if (t > 0f) _walkClipT += WalkRate * sp / Fps;   // the clip runs at its rate only as fast as the root moves, so the ramps do not skate
            _bored.SetTime(t % _boredLen);
            _walk.SetTime(_walkClipT % _walkLen);
            _mixer.SetInputWeight(0, 1f - sp);
            _mixer.SetInputWeight(1, sp);
            _graph.Evaluate(0f);
            // the clip walks away from the root; take that linear travel back off the hips (it never leaves the cycle's start)
            float tau = _walkClipT % _walkLen;
            if (sp > 0f) _anim.GetBoneTransform(HumanBodyBones.Hips).position -= _anim.transform.TransformVector(_walkTravel * (sp * tau / _walkLen));
            _closeEyes = 0f;
        }

        // Task 58: walk x +1.3 -> -1.1 (frame right), slow down, turn on the spot through the camera, walk back to +1.3, turn, again.
        // The legs end exactly on the target: the ramp-down covers v * Fade / 2 metres.
        void MotionWalkOnly(float t)
        {
            float dt = t > 0f ? 1f / Fps : 0f;
            float v = _walkSpeed, sp = 0f;
            bool even = (_wLeg % 2 == 0) == _front;
            float target = _front ? (even ? FrontEnd : FrontStart) : (even ? WalkXFar : WalkXNear), sgn = _front ? (even ? 1f : -1f) : (even ? -1f : 1f);
            float yawA = _front ? (even ? 0f : 180f) : (even ? WalkYaw : -WalkYaw), yawB = _front ? (even ? 180f : 0f) : -yawA;
            if (_wPhase == 0)
            {
                _rootYaw = yawA;
                float cur = _front ? _rootPos.z : _rootPos.x;
                float remaining = (target - cur) * sgn;
                if (_slowT < 0f && remaining <= v * Fade * 0.5f) _slowT = 0f;
                float down = 1f;
                if (_slowT >= 0f) { _slowT += dt; down = 1f - S(_slowT / Fade); }
                sp = S(_legT / Fade) * down;
                _legT += dt;
                if (_front) _rootPos.z += sgn * v * sp * dt; else _rootPos.x += sgn * v * sp * dt;
                if (_slowT >= Fade || remaining <= 0f) { if (_front) _rootPos.z = target; else _rootPos.x = target; _wPhase = 1; _wPh = 0f; sp = 0f; Debug.Log("ROOMTEST walkonly leg " + _wLeg + " ends at t " + t.ToString("0.00", CultureInfo.InvariantCulture) + " x " + target.ToString("0.00", CultureInfo.InvariantCulture)); }
            }
            else
            {
                _wPh += dt;
                // side walk: turn to the camera, hold there for ODDITY_HOLD seconds like a model at the end of a runway, turn on
                float half = TurnLen * 0.5f, hold = _front ? 0f : Env("ODDITY_HOLD", 0f);
                if (hold <= 0f) _rootYaw = Mathf.Lerp(yawA, yawB, S(_wPh / TurnLen));
                else _rootYaw = _wPh < half + hold ? Mathf.Lerp(yawA, 0f, S(_wPh / half)) : Mathf.Lerp(0f, yawB, S((_wPh - half - hold) / half));
                if (_wPh >= TurnLen + hold) { _wPhase = 0; _wLeg++; _legT = 0f; _slowT = -1f; _rootYaw = yawB; }
            }
            _walkW = sp;
            _harin.transform.rotation = Quaternion.Euler(0f, _rootYaw, 0f);
            _walkClipT += WalkRate * sp * dt;
            _bored.SetTime(t % _boredLen);
            _walk.SetTime(_walkClipT % _walkLen);
            _mixer.SetInputWeight(0, 1f - sp);
            _mixer.SetInputWeight(1, sp);
            _graph.Evaluate(0f);
            float tau = _walkClipT % _walkLen;
            if (sp > 0f) _anim.GetBoneTransform(HumanBodyBones.Hips).position -= _anim.transform.TransformVector(_walkTravel * (sp * tau / _walkLen));
            _closeEyes = 0f;
        }

        // ---- task 64: feet must not sink through the floor. The real foot surface (body and sock vertices below 12 cm in the rest pose) is reduced to a few
        // points per foot, each kept in the local space of the foot or toes bone that drives it. Each frame the lowest of them sets the root's y; no bone is written.
        struct SolePt { public Transform bone; public Vector3 local; public int foot; public float s; }
        readonly List<SolePt> _soleSamples = new List<SolePt>();
        bool _floorCheck, _noFloorFix;
        const float FloorGap = 0.002f, SoleBand = 0.12f;
        SkinnedMeshRenderer[] _fcR; Mesh[] _fcM;
        readonly List<Vector3> _fcV = new List<Vector3>();
        readonly List<float> _fcAll = new List<float>(), _fcWalk = new List<float>(), _fcBlend = new List<float>(), _rootYWalk = new List<float>(), _rootYAll = new List<float>();
        float _prevW, _worstMiss; int _worstK; float _worstW;

        void BuildSoleSamples(GameObject go)
        {
            var feet = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftFoot), _anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftToes), _anim.GetBoneTransform(HumanBodyBones.RightToes) };
            var cand = new[] { new List<(Vector3 p, Transform b)>(), new List<(Vector3 p, Transform b)>() };
            var candI = new[] { new List<(SkinnedMeshRenderer r, int v, int sub)>(), new List<(SkinnedMeshRenderer r, int v, int sub)>() };   // task 65 diagnosis
            float baseY = go.transform.position.y;
            int rendererCount = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var verts = mesh.vertices; var bw = mesh.boneWeights; var bp = mesh.bindposes; var bones = smr.bones;
                if (bw.Length != verts.Length || bones.Length == 0) continue;
                var mats = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * bp[i] : Matrix4x4.identity;
                int before = cand[0].Count + cand[1].Count;
                int[] subOf = null;
                if (_d65) { subOf = new int[verts.Length]; for (int sm = 0; sm < mesh.subMeshCount; sm++) foreach (var ix in mesh.GetIndices(sm)) subOf[ix] = sm; }
                for (int v = 0; v < verts.Length; v++)
                {
                    var w = bw[v];
                    var db = bones[w.boneIndex0];
                    int f = db == feet[0] || (toes[0] != null && db == toes[0]) ? 0 : db == feet[1] || (toes[1] != null && db == toes[1]) ? 1 : -1;
                    if (f < 0) continue;
                    var p = mats[w.boneIndex0].MultiplyPoint3x4(verts[v]) * w.weight0;
                    if (w.weight1 > 0f) p += mats[w.boneIndex1].MultiplyPoint3x4(verts[v]) * w.weight1;
                    if (w.weight2 > 0f) p += mats[w.boneIndex2].MultiplyPoint3x4(verts[v]) * w.weight2;
                    if (w.weight3 > 0f) p += mats[w.boneIndex3].MultiplyPoint3x4(verts[v]) * w.weight3;
                    if (p.y - baseY > SoleBand) continue;
                    cand[f].Add((p, db));
                    candI[f].Add((smr, v, subOf != null ? subOf[v] : 0));
                }
                if (cand[0].Count + cand[1].Count > before) rendererCount++;
            }
            for (int f = 0; f < 2; f++)
            {
                var c = cand[f];
                if (c.Count == 0) continue;
                var fwd = toes[f] != null ? toes[f].position - feet[f].position : Vector3.forward;
                fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, fwd);
                var o = feet[f].position;
                float smin = float.MaxValue, smax = float.MinValue, tmin = float.MaxValue, tmax = float.MinValue;
                foreach (var q in c) { float s = Vector3.Dot(q.p - o, fwd), t = Vector3.Dot(q.p - o, side); smin = Mathf.Min(smin, s); smax = Mathf.Max(smax, s); tmin = Mathf.Min(tmin, t); tmax = Mathf.Max(tmax, t); }
                const int Bins = 14;
                var best = new int[Bins * 2]; for (int i = 0; i < best.Length; i++) best[i] = -1;
                int iTip = 0, iHeel = 0;
                for (int i = 0; i < c.Count; i++)
                {
                    float s = Vector3.Dot(c[i].p - o, fwd), t = Vector3.Dot(c[i].p - o, side);
                    int bi = Mathf.Clamp((int)((s - smin) / Mathf.Max(1e-5f, smax - smin) * Bins), 0, Bins - 1) * 2 + (t > (tmin + tmax) * 0.5f ? 1 : 0);
                    if (best[bi] < 0 || c[i].p.y < c[best[bi]].p.y) best[bi] = i;
                    if (s > Vector3.Dot(c[iTip].p - o, fwd)) iTip = i;
                    if (s < Vector3.Dot(c[iHeel].p - o, fwd)) iHeel = i;
                }
                var pick = new HashSet<int>(best.Where(i => i >= 0)) { iTip, iHeel };
                float lowRest = float.MaxValue;
                foreach (int i in pick)
                {
                    _soleSamples.Add(new SolePt { bone = c[i].b, local = c[i].b.worldToLocalMatrix.MultiplyPoint3x4(c[i].p), foot = f, s = Vector3.Dot(c[i].p - o, fwd) - (smin + smax) * 0.5f });
                    lowRest = Mathf.Min(lowRest, c[i].p.y);
                }
                if (_d65) Shoe65(f, c, candI[f], pick, fwd, o, smin, smax, feet[f], toes[f]);
                Debug.Log("ROOMTEST sole samples foot " + f + ": " + pick.Count + " points from " + c.Count + " vertices in " + rendererCount + " renderers, foot length " + (smax - smin).ToString("0.000", CultureInfo.InvariantCulture) +
                          " m, lowest rest y " + lowRest.ToString("0.0000", CultureInfo.InvariantCulture));
            }
        }


        // ---- task 65 diagnosis (ODDITY_DIAG65=1): lean of the body against the feet, and where the shoe sole sits. Logs only; writes no bone.
        readonly bool _d65 = Environment.GetEnvironmentVariable("ODDITY_DIAG65") == "1";
        class Shoe65Grp { public SkinnedMeshRenderer r; public List<int> heel = new List<int>(), toe = new List<int>(); public int foot; }
        readonly Dictionary<string, Shoe65Grp> _shoeG = new Dictionary<string, Shoe65Grp>();
        readonly Dictionary<string, List<float>> _shoeLow = new Dictionary<string, List<float>>();

        class LeanAcc { public List<float> hipsOff = new List<float>(), headOff = new List<float>(), hipHead = new List<float>(), hipChest = new List<float>(), chestHead = new List<float>(), headH = new List<float>(), headHeel = new List<float>(), plantCm = new List<float>(), plantDeg = new List<float>(); }
        readonly LeanAcc _leanFilm = new LeanAcc();

        void LeanSample(Animator an, Vector3 dir, LeanAcc a, bool model)
        {
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips); var head = B(HumanBodyBones.Head);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            var lf = B(HumanBodyBones.LeftFoot); var rf = B(HumanBodyBones.RightFoot);
            var mid = (lf.position + rf.position) * 0.5f;
            a.hipsOff.Add(Vector3.Dot(hips.position - mid, dir));
            a.headOff.Add(Vector3.Dot(head.position - mid, dir));
            a.headH.Add(head.position.y - Mathf.Min(lf.position.y, rf.position.y));
            float Ang(Vector3 v) => Mathf.Atan2(Vector3.Dot(v, dir), v.y) * Mathf.Rad2Deg;
            a.hipHead.Add(Ang(head.position - hips.position));
            a.hipChest.Add(Ang(chest.position - hips.position));
            a.chestHead.Add(Ang(head.position - chest.position));
            // head to the rear foot (the one farther back along dir): positive = the head is ahead of that foot
            var rear = Vector3.Dot(lf.position, dir) < Vector3.Dot(rf.position, dir) ? lf : rf;
            a.headHeel.Add(Ang(head.position - rear.position));
            if (model)
            {
                // the user's line: head to the heel of the planted foot (the one whose lowest sole point is lower)
                var fts = new[] { lf, rf }; int pl = 0; float lo = float.MaxValue;
                for (int i = 0; i < 2; i++) { float l = Mathf.Min(HeelPt(i, fts[i].position, fts[i].rotation).y, BallPt(i, fts[i].position, fts[i].rotation).y); if (l < lo) { lo = l; pl = i; } }
                var hp = HeelPt(pl, fts[pl].position, fts[pl].rotation);
                a.plantCm.Add(Vector3.Dot(head.position - hp, dir));
                a.plantDeg.Add(Ang(head.position - hp));
            }
        }

        static string LeanText(LeanAcc a)
        {
            string M(List<float> l, float k, string fmt) => l.Count == 0 ? "n/a" : (l.Average() * k).ToString(fmt, CultureInfo.InvariantCulture) + " (" + (l.Min() * k).ToString(fmt, CultureInfo.InvariantCulture) + ".." + (l.Max() * k).ToString(fmt, CultureInfo.InvariantCulture) + ")";
            return "n " + a.hipsOff.Count + " | hips ahead of feet-mid " + M(a.hipsOff, 100f, "0.0") + " cm | head ahead of feet-mid " + M(a.headOff, 100f, "0.0") + " cm | head height over lowest ankle " + M(a.headH, 100f, "0.0") +
                   " cm | lean deg (+ = forward): hips>head " + M(a.hipHead, 1f, "0.0") + ", hips>chest " + M(a.hipChest, 1f, "0.0") + ", chest>head " + M(a.chestHead, 1f, "0.0") + ", rear ankle>head " + M(a.headHeel, 1f, "0.0") + " | head ahead of planted heel " + M(a.plantCm, 100f, "0.0") + " cm = line heel>head " + M(a.plantDeg, 1f, "0.0") + " deg";
        }

        void Diag65Lean(AnimationClip cw)
        {
            var dirT = _walkTravel; dirT.y = 0f; dirT = dirT.sqrMagnitude > 1e-6f ? dirT.normalized : Vector3.forward;
            string name = Environment.GetEnvironmentVariable("ODDITY_WALK") ?? "HarinWalk";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Oddity/DevMotion/Resources/Motion/" + name + ".fbx");
            var inst = Instantiate(src, new Vector3(50f, 0f, 0f), Quaternion.identity);
            var an = inst.GetComponent<Animator>();
            var g = PlayableGraph.Create("d65");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var cp = AnimationClipPlayable.Create(g, cw);
            cp.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(g, "o", an).SetSourcePlayable(cp);
            int n = Mathf.Max(8, Mathf.RoundToInt(cw.length * Fps));
            var fbx = new LeanAcc(); var vrm = new LeanAcc();
            for (int i = 0; i < n; i++)
            {
                float tm = i * cw.length / n;
                if (i == 0) { var r0 = new LeanAcc(); LeanSample(an, Vector3.forward, r0, false); Debug.Log("ROOMTEST d65 lean FBX rest " + LeanText(r0)); }
                cp.SetTime(tm); g.Evaluate(0f); LeanSample(an, dirT, fbx, false);
                _mixer.SetInputWeight(0, 0f); _mixer.SetInputWeight(1, 1f);
                _walk.SetTime(tm); _graph.Evaluate(0f); LeanSample(_anim, dirT, vrm, true);
            }
            Debug.Log("ROOMTEST d65 lean FBX-own-avatar " + name + " " + LeanText(fbx));
            Debug.Log("ROOMTEST d65 lean VRM " + RoomTest.Model + " " + LeanText(vrm));
            g.Destroy(); Destroy(inst);
            _walk.SetTime(0);
        }

        void Shoe65(int f, List<(Vector3 p, Transform b)> c, List<(SkinnedMeshRenderer r, int v, int sub)> ci, HashSet<int> pick, Vector3 fwd, Vector3 o, float smin, float smax, Transform foot, Transform toe)
        {
            float len = smax - smin;
            var keys = new Dictionary<string, List<int>>();
            for (int i = 0; i < c.Count; i++)
            {
                var mats = ci[i].r.sharedMaterials;
                string k = ci[i].r.name + "/" + (ci[i].sub < mats.Length && mats[ci[i].sub] != null ? mats[ci[i].sub].name : "null");
                if (!keys.TryGetValue(k, out var l)) keys[k] = l = new List<int>();
                l.Add(i);
            }
            Debug.Log("ROOMTEST d65 shoe foot " + f + " ankle y " + foot.position.y.ToString("0.000", CultureInfo.InvariantCulture) + " toe bone y " + (toe != null ? toe.position.y.ToString("0.000", CultureInfo.InvariantCulture) : "n/a") + " foot length " + len.ToString("0.000", CultureInfo.InvariantCulture));
            foreach (var kv in keys)
            {
                var l = kv.Value;
                float lowAll = l.Min(i => c[i].p.y);
                var heel = l.Where(i => Vector3.Dot(c[i].p - o, fwd) < smin + len * 0.3f).ToList();
                var toeL = l.Where(i => Vector3.Dot(c[i].p - o, fwd) > smin + len * 0.7f).ToList();
                float heelLow = heel.Count > 0 ? heel.Min(i => c[i].p.y) : float.NaN, toeLow = toeL.Count > 0 ? toeL.Min(i => c[i].p.y) : float.NaN;
                float heelS = heel.Count > 0 ? Vector3.Dot(c[heel.OrderBy(i => c[i].p.y).First()].p - o, fwd) : 0f, toeS = toeL.Count > 0 ? Vector3.Dot(c[toeL.OrderBy(i => c[i].p.y).First()].p - o, fwd) : 0f;
                float pitch = heel.Count > 0 && toeL.Count > 0 ? Mathf.Atan2(heelLow - toeLow, toeS - heelS) * Mathf.Rad2Deg : float.NaN;
                int picked = l.Count(i => pick.Contains(i));
                Debug.Log("ROOMTEST d65 shoe foot " + f + " group " + kv.Key + ": " + l.Count + " verts under 12 cm, " + picked + " picked as samples, lowest rest y " + (lowAll * 1000f).ToString("0.0", CultureInfo.InvariantCulture) +
                          " mm, heel third lowest " + (heelLow * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm (" + heel.Count + " v), toe third lowest " + (toeLow * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm (" + toeL.Count + " v), rest sole pitch (+ = toes down) " + pitch.ToString("0.0", CultureInfo.InvariantCulture) + " deg");
                var gr = new Shoe65Grp { r = ci[l[0]].r, foot = f };
                gr.heel.AddRange(heel.Select(i => ci[i].v)); gr.toe.AddRange(toeL.Select(i => ci[i].v));
                _shoeG["f" + f + " " + kv.Key] = gr;
            }
        }

        void Shoe65Frame(int frame)
        {
            foreach (var kv in _shoeG)
            {
                int idx = Array.IndexOf(_fcR, kv.Value.r);
                if (idx < 0) continue;
                _fcM[idx].GetVertices(_fcV);
                var m = _fcR[idx].transform.localToWorldMatrix;
                float Lo(List<int> l) { float lo = float.MaxValue; foreach (int v in l) lo = Mathf.Min(lo, m.MultiplyPoint3x4(_fcV[v]).y); return lo; }
                float h = Lo(kv.Value.heel), t = Lo(kv.Value.toe);
                foreach (var pr in new[] { ("heel", h), ("toe", t), ("toe-heel", t - h) })
                {
                    string key = kv.Key + " " + pr.Item1;
                    if (!_shoeLow.TryGetValue(key, out var l)) _shoeLow[key] = l = new List<float>();
                    l.Add(pr.Item2);
                }
            }
        }

        void Shoe65Report()
        {
            string F(float x) => (x * 1000f).ToString("0.0", CultureInfo.InvariantCulture);
            foreach (var kv in _shoeLow)
            {
                var o = kv.Value.OrderBy(x => x).ToList();
                Debug.Log("ROOMTEST d65 shoe true-bake walking frames " + kv.Key + ": min " + F(o[0]) + " median " + F(o[o.Count / 2]) + " max " + F(o[o.Count - 1]) + " mm (n " + o.Count + ")");
            }
            foreach (var g in _shoeG.Keys)
            {
                if (!_shoeLow.ContainsKey(g + " heel")) continue;
                var h = _shoeLow[g + " heel"]; var t = _shoeLow[g + " toe"];
                var d = new List<float>();
                for (int i = 0; i < h.Count; i++) if (Mathf.Min(h[i], t[i]) < 0.012f) d.Add(t[i] - h[i]);
                if (d.Count == 0) { Debug.Log("ROOMTEST d65 shoe stance " + g + ": no frame within 12 mm of the floor"); continue; }
                d.Sort();
                Debug.Log("ROOMTEST d65 shoe stance (low end under 12 mm) " + g + ": toe minus heel lowest " + F(d[0]) + " / " + F(d[d.Count / 2]) + " / " + F(d[d.Count - 1]) + " mm (min/median/max, n " + d.Count + "); positive = heel block lower than toe");
            }
        }


        // ---- task 65 fix: two constants, nothing per frame. ODDITY_FOOTPITCH=auto|degrees tips each foot toes-up by that much (auto: read once from the
        // clip, so that the shoe sole is flat in stance); ODDITY_SPINE=degrees leans the spine forward. Both fade in with the walk weight. Off when unset.
        const float FloorSmooth = 0.05f;
        readonly bool _holdFix = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ODDITY_HOLD"));   // she stands in the clip too: the foot, spine and floor fixes stay on
        float _floorY, _floorV;
        readonly float[] _fixFoot = new float[2];
        readonly Quaternion[] _toeRest = new Quaternion[2];
        readonly bool _rigidToes = Environment.GetEnvironmentVariable("ODDITY_RIGIDTOES") != "0";
        float _fixSpine;
        LeanAcc _leanPre = new LeanAcc();

        void DeriveFix65()
        {
            if (!_walkOnly || !_rawMode) return;
            {
                // spine: lean forward by what makes hips -> head upright over one cycle of the clip on this model (at most 6 deg); ODDITY_SPINE=degrees overrides
                var dirT = _walkTravel; dirT.y = 0f; dirT = dirT.sqrMagnitude > 1e-6f ? dirT.normalized : Vector3.forward;
                var hips = _anim.GetBoneTransform(HumanBodyBones.Hips); var head = _anim.GetBoneTransform(HumanBodyBones.Head);
                int nl = Mathf.Max(8, Mathf.RoundToInt(_walkLen * Fps)); float sum = 0f;
                _mixer.SetInputWeight(0, 0f); _mixer.SetInputWeight(1, 1f);
                for (int i = 0; i < nl; i++)
                {
                    _walk.SetTime(i * _walkLen / nl); _graph.Evaluate(0f);
                    var hh = head.position - hips.position;
                    sum += Mathf.Atan2(Vector3.Dot(hh, dirT), hh.y) * Mathf.Rad2Deg;
                }
                _walk.SetTime(0);
                _fixSpine = Env("ODDITY_SPINE", Mathf.Clamp(-sum / nl, 0f, 6f));
            }
            var fp = Environment.GetEnvironmentVariable("ODDITY_FOOTPITCH");
            if (string.IsNullOrEmpty(fp)) fp = "auto";
            if (fp != "auto") { _fixFoot[0] = _fixFoot[1] = Env("ODDITY_FOOTPITCH", 0f); }
            else
            {
                // one cycle of the clip on the model: per foot, the sole pitch (rear sample vs front sample, + = toes down) in the frames where that foot's sole is on the floor
                var feet = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftFoot), _anim.GetBoneTransform(HumanBodyBones.RightFoot) };
                var dir = _harin.transform.forward; dir.y = 0f; dir.Normalize();
                int n = Mathf.Max(8, Mathf.RoundToInt(_walkLen * Fps));
                var pit = new[] { new List<float>(), new List<float>() }; var low = new[] { new List<float>(), new List<float>() };
                _mixer.SetInputWeight(0, 0f); _mixer.SetInputWeight(1, 1f);
                for (int i = 0; i < n; i++)
                {
                    _walk.SetTime(i * _walkLen / n); _graph.Evaluate(0f);
                    for (int f = 0; f < 2; f++)
                    {
                        float rLo = float.MaxValue, fLo = float.MaxValue, rS = 0f, fS = 0f;
                        foreach (var sp in _soleSamples)
                        {
                            if (sp.foot != f) continue;
                            var w = sp.bone.TransformPoint(sp.local);
                            if (sp.s < 0f) { if (w.y < rLo) { rLo = w.y; rS = Vector3.Dot(w, dir); } }
                            else if (w.y < fLo) { fLo = w.y; fS = Vector3.Dot(w, dir); }
                        }
                        pit[f].Add(Mathf.Atan2(rLo - fLo, Mathf.Max(0.02f, fS - rS)) * Mathf.Rad2Deg);
                        low[f].Add(Mathf.Min(rLo, fLo));
                    }
                }
                for (int f = 0; f < 2; f++)
                {
                    float lo = low[f].Min();
                    var st = Enumerable.Range(0, n).Where(i => low[f][i] - lo < 0.012f).Select(i => pit[f][i]).OrderBy(x => x).ToList();
                    _fixFoot[f] = st[st.Count / 2];
                    Debug.Log("ROOMTEST fix65 foot " + f + " stance sole pitch (+ toes down) median " + _fixFoot[f].ToString("0.0", CultureInfo.InvariantCulture) + " deg, min " + st[0].ToString("0.0", CultureInfo.InvariantCulture) + " max " + st[st.Count - 1].ToString("0.0", CultureInfo.InvariantCulture) + " over " + st.Count + " of " + n + " frames -> tip toes up by it");
                }
                _walk.SetTime(0);
            }
            Debug.Log("ROOMTEST fix65 foot pitch toes-up " + _fixFoot[0].ToString("0.0", CultureInfo.InvariantCulture) + " / " + _fixFoot[1].ToString("0.0", CultureInfo.InvariantCulture) + " deg, spine forward " + _fixSpine.ToString("0.0", CultureInfo.InvariantCulture) + " deg");
        }

        void Fix65()
        {
            float sp = _holdFix ? 1f : _walkW;
            if (sp <= 0.0001f) return;
            var root = _harin.transform;
            var dir = root.rotation * Vector3.forward; dir.y = 0f; dir.Normalize();
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            if (_d65 && sp > 0.99f) LeanSample(_anim, dir, _leanPre, true);
            if (_fixSpine != 0f)
            {
                var spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
                spine.rotation = Quaternion.AngleAxis(_fixSpine * sp, side) * spine.rotation;   // side = up x dir tips the head forward, always
            }
            for (int i = 0; i < 2; i++)
            {
                var ft = _anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                // a fixed axis: choosing the sign per frame flipped it when the foot swung steeply, and the whole body jumped
                if (_fixFoot[i] != 0f) ft.rotation = Quaternion.AngleAxis(-_fixFoot[i] * sp, side) * ft.rotation;
                // a shoe is rigid: the toes stay at their rest pose instead of folding the shoe at the ball of the foot
                if (_toes[i] != null && _rigidToes) _toes[i].localRotation = Quaternion.Slerp(_toes[i].localRotation, _toeRest[i], sp);
            }
        }

        float LowestSole(bool skipToes = false)
        {
            float lo = float.MaxValue;
            foreach (var s in _soleSamples)
                if (!skipToes || s.bone != _toes[s.foot]) lo = Mathf.Min(lo, s.bone.TransformPoint(s.local).y);
            return lo;
        }

        // a shoe bends only where the floor bends it: a toe that would go under the floor pivots up at the ball of the foot; in the air it stays rigid
        void ToesOnFloor()
        {
            for (int f = 0; f < 2; f++)
            {
                if (_toes[f] == null) continue;
                float lo = float.MaxValue; Vector3 w = Vector3.zero;
                foreach (var s in _soleSamples)
                {
                    if (s.foot != f || s.bone != _toes[f]) continue;
                    var p = s.bone.TransformPoint(s.local);
                    if (p.y < lo) { lo = p.y; w = p; }
                }
                if (lo >= FloorGap) continue;
                var v = w - _toes[f].position;
                var axis = Vector3.Cross(v, Vector3.up);
                if (axis.sqrMagnitude < 1e-8f || v.magnitude < 0.01f) continue;
                float a = Mathf.Asin(Mathf.Clamp01((FloorGap - lo) / v.magnitude)) * Mathf.Rad2Deg;
                _toes[f].rotation = Quaternion.AngleAxis(a, axis.normalized) * _toes[f].rotation;
            }
        }

        // an independent check: every renderer baked once per frame, the true lowest vertex in the world
        float TrueLowest()
        {
            float lo = float.MaxValue;
            for (int i = 0; i < _fcR.Length; i++)
            {
                _fcR[i].BakeMesh(_fcM[i], false);
                _fcM[i].GetVertices(_fcV);
                var m = _fcR[i].transform.localToWorldMatrix;
                foreach (var v in _fcV) lo = Mathf.Min(lo, m.MultiplyPoint3x4(v).y);
            }
            return lo;
        }

        void FloorReport()
        {
            string F(float x) => (x * 1000f).ToString("0.0", CultureInfo.InvariantCulture);
            string St(List<float> l) { if (l.Count == 0) return "n/a"; var o = l.OrderBy(x => x).ToList(); return "min " + F(o[0]) + " median " + F(o[o.Count / 2]) + " max " + F(o[o.Count - 1]) + " mm, " + o.Count(x => x < -0.003f) + " of " + o.Count + " frames below -3 mm"; }
            string R(List<float> l) => l.Count == 0 ? "n/a" : "y " + F(l.Min()) + ".." + F(l.Max()) + " mm (range " + F(l.Max() - l.Min()) + " mm)";
            Debug.Log("ROOMTEST floorcheck fix " + (_noFloorFix ? "OFF" : "ON") + " | true lowest vertex, walking (w>0.25): " + St(_fcWalk) + " | blend-in/out frames (0<w<=0.25): " + St(_fcBlend) + " | worst true-below-samples " + F(_worstMiss) + " mm at frame " + _worstK + " w " + _worstW.ToString("0.00", CultureInfo.InvariantCulture) + " | whole video: " + St(_fcAll) + " | root " + R(_rootYWalk) + " walking, " + R(_rootYAll) + " whole video");
        }

        static float ToePitch(Vector3 v) => Mathf.Asin(Mathf.Clamp(v.y / Mathf.Max(1e-5f, v.magnitude), -1f, 1f)) * Mathf.Rad2Deg;

        float Cap(float h) => _lift > 0f && h > 0f ? _lift * (float)Math.Tanh(h / _lift) : h;

        class WalkStat
        {
            public float peak, minBend = 180f, toeUp = -90f, hipMin = 9f, hipMax = -9f, kneeFwd = -9f, leanSum, leanMax = -90f; public int leanN;
            public readonly List<float> skate = new List<float>(), stance = new List<float>();
        }
        readonly WalkStat _raw = new WalkStat(), _fin = new WalkStat();
        readonly Vector3[] _prevA = new Vector3[2];
        readonly float[] _prevH = { 9f, 9f };
        bool _prevOk;

        // One sample of whatever pose the bones hold now. Swing: the foot is ahead of the hips and more than 1 cm off the floor.
        void Sample(WalkStat st, float floorY, bool skating)
        {
            var root = _harin.transform;
            float ankleH = _harin.AnkleHeight * root.lossyScale.y;
            var dir = root.rotation * Vector3.forward; dir.y = 0f; dir.Normalize();
            var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            var ids = new[] { new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot } };
            {
                var nk = _anim.GetBoneTransform(HumanBodyBones.Neck) ?? _anim.GetBoneTransform(HumanBodyBones.UpperChest);
                var nv = nk.position - hips.position;
                float lean = Mathf.Atan2(Vector3.Dot(nv, dir), nv.y) * Mathf.Rad2Deg;
                st.leanSum += lean; st.leanN++; st.leanMax = Mathf.Max(st.leanMax, lean);
                st.hipMin = Mathf.Min(st.hipMin, hips.position.y - floorY); st.hipMax = Mathf.Max(st.hipMax, hips.position.y - floorY);
            }
            for (int i = 0; i < 2; i++)
            {
                var up = _anim.GetBoneTransform(ids[i][0]); var lo = _anim.GetBoneTransform(ids[i][1]); var ft = _anim.GetBoneTransform(ids[i][2]);
                float hAnkle = ft.position.y - ankleH - floorY;
                float hLow = hAnkle;
                if (_toes[i] != null) hLow = Mathf.Min(hLow, _toes[i].position.y - _toeFloor[i] - floorY);
                float along = Vector3.Dot(ft.position - hips.position, dir);
                float bend = 180f - Vector3.Angle(up.position - lo.position, ft.position - lo.position);
                if (along > 0f && hLow > 0.01f) { st.peak = Mathf.Max(st.peak, hLow); st.minBend = Mathf.Min(st.minBend, bend); st.kneeFwd = Mathf.Max(st.kneeFwd, Vector3.Dot(lo.position - hips.position, dir)); }
                if (hAnkle < 0.01f && Mathf.Abs(along) < 0.04f) st.stance.Add(bend);
                if (_toes[i] != null && hAnkle < 0.02f) st.toeUp = Mathf.Max(st.toeUp, ToePitch(_toes[i].position - ft.position) - _toeP0);
                if (skating)
                {
                    if (_prevOk && hAnkle < 0.008f && _prevH[i] < 0.008f)
                    {
                        var d = ft.position - _prevA[i]; d.y = 0f;
                        st.skate.Add(d.magnitude * Fps);
                    }
                    _prevA[i] = ft.position; _prevH[i] = hAnkle;
                }
            }
            if (skating) _prevOk = true;
        }

        const float StanceReach = 0.9945f, MaxReach = 0.9976f, TargetLean = 2f;   // cos(6 deg) = 12 deg stance knee bend, cos(4 deg) = 8 deg at the least on the swing leg
        float _hipD; bool _hipInit;

        // The walk shaping layer on top of the clip: arms toward their mean, hips sunk, strides scaled, feet capped, soles kept flat; the legs by two-bone IK.
        void WalkLayer()
        {
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var root = _harin.transform;
            float sp = _walkW;
            if (sp <= 0.0001f) { _prevOk = false; _hipInit = false; return; }
            var hips = B(HumanBodyBones.Hips);
            var dir = root.rotation * Vector3.forward; dir.y = 0f; dir.Normalize();
            float ankleH = _harin.AnkleHeight * root.lossyScale.y;
            var feet = new[] { B(HumanBodyBones.LeftFoot), B(HumanBodyBones.RightFoot) };
            float floorRef = Mathf.Min(feet[0].position.y, feet[1].position.y) - ankleH;
            if (sp > 0.99f) Sample(_raw, floorRef, false);
            var a0 = new Vector3[2]; var hLow = new float[2]; var rot = new Quaternion[2]; var rot0 = new Quaternion[2]; var tv0 = new Vector3[2]; var pitch0 = new float[2];
            float cyc = (_walkClipT % _walkLen) / _walkLen;
            for (int i = 0; i < 2; i++)
            {
                a0[i] = feet[i].position;
                hLow[i] = a0[i].y - ankleH - floorRef;
                rot[i] = rot0[i] = feet[i].rotation;
                if (_toes[i] == null) continue;
                hLow[i] = Mathf.Min(hLow[i], _toes[i].position.y - _toeFloor[i] - floorRef);
                var tv = _toes[i].position - feet[i].position;
                tv0[i] = tv;
                float pitch = ToePitch(tv) - _toeP0;
                pitch0[i] = ToePitch(tv) - _toeP0i[i];
                if (!_oldFeet || pitch <= 0f) continue;
                // old (task 59): toes up: never more than MaxToeUp, and flat as the foot nears the floor
                float want = Mathf.Min(pitch, MaxToeUp) * S(Mathf.Max(0f, hLow[i]) / 0.06f);
                var f = tv; f.y = 0f;
                if (f.sqrMagnitude < 1e-8f) continue;
                var axis = Vector3.Cross(Vector3.up, f.normalized);
                var q = Quaternion.AngleAxis(want - pitch, axis);
                if (Mathf.Abs(ToePitch(q * tv) - _toeP0 - want) > Mathf.Abs(ToePitch(Quaternion.Inverse(q) * tv) - _toeP0 - want)) q = Quaternion.Inverse(q);
                rot[i] = Quaternion.Slerp(feet[i].rotation, q * feet[i].rotation, sp);
            }
            // the floor for the sole solve: the lowest sole point of either foot in the clip pose
            float floorS = floorRef;
            if (!_oldFeet) { floorS = float.MaxValue; for (int i = 0; i < 2; i++) if (_toes[i] != null) floorS = Mathf.Min(floorS, Mathf.Min(HeelPt(i, a0[i], rot0[i]).y, BallPt(i, a0[i], rot0[i]).y)); if (floorS > 1e6f) floorS = floorRef; }
            // arms about the clip's mean pose (task 58 layer; the task 60 layer below replaces it)
            if (_oldTorso && _armT != null && _armMean != null)
                for (int a = 0; a < 4; a++)
                    _armT[a].localRotation = Quaternion.SlerpUnclamped(_armT[a].localRotation, _armMean[a], (1f - _arms) * sp);
            if (!_oldTorso) PelvisLayer(sp, dir, cyc);
            // torso: the lean of hips -> neck. Task 59 pinned it at TargetLean; task 60 lets it breathe: 2 deg and 1.5 deg at twice the step rhythm, forward as a foot lands
            {
                float leanT = _oldTorso ? TargetLean : TargetLean + 1.5f * Mathf.Cos(4f * Mathf.PI * (cyc - _phL));
                var spine = B(HumanBodyBones.Spine);
                var nk = B(HumanBodyBones.Neck) ?? B(HumanBodyBones.UpperChest);
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                for (int it = 0; it < 3; it++)
                {
                    var nv = nk.position - hips.position;
                    float lean = Mathf.Atan2(Vector3.Dot(nv, dir), nv.y) * Mathf.Rad2Deg;
                    if (Mathf.Abs(lean - leanT) < 0.3f) break;
                    var q = Quaternion.AngleAxis((lean - leanT) * sp, side);
                    var rel = nk.position - spine.position;
                    float L1 = Mathf.Abs(Mathf.Atan2(Vector3.Dot(spine.position + q * rel - hips.position, dir), (spine.position + q * rel - hips.position).y) * Mathf.Rad2Deg - leanT);
                    float L2 = Mathf.Abs(Mathf.Atan2(Vector3.Dot(spine.position + Quaternion.Inverse(q) * rel - hips.position, dir), (spine.position + Quaternion.Inverse(q) * rel - hips.position).y) * Mathf.Rad2Deg - leanT);
                    spine.rotation = (L1 <= L2 ? q : Quaternion.Inverse(q)) * spine.rotation;
                }
            }
            if (!_oldTorso) UpperLayer(sp, dir, cyc);
            var hp0 = hips.position;
            var ids = new[] { new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot } };
            var tg = new Vector3[2]; var lowest = new float[2];
            float best = float.MaxValue; bool anyPlanted = false;
            for (int i = 0; i < 2; i++)
            {
                float along = Vector3.Dot(a0[i] - hp0, dir);
                var shift = dir * (along * (_stride - 1f) * sp);
                tg[i] = a0[i] + shift;
                bool solved = !_oldFeet && _toes[i] != null;
                if (solved) FootSolve(i, sp, cyc, rot0[i], tv0[i], pitch0[i], a0[i], shift, floorS, B(ids[i][0]), B(ids[i][1]), B(ids[i][2]), dir, out rot[i], out tg[i], out lowest[i]);
                else { tg[i].y -= hLow[i] - Cap(hLow[i]); lowest[i] = tg[i].y - ankleH - floorRef; }
                if (lowest[i] > 0.012f) continue;   // not on the floor: the stance leg sets the hip height
                // the planted leg reaches StanceReach of its length: the hips go up or down until it does
                var u = B(ids[i][0]); var kn = B(ids[i][1]);
                float lenL = Vector3.Distance(u.position, kn.position) + Vector3.Distance(kn.position, feet[i].position);
                var hd = u.position - tg[i]; hd.y = 0f;
                float r = lenL * StanceReach;
                float want = tg[i].y + Mathf.Sqrt(Mathf.Max(r * r - hd.sqrMagnitude, 0f));
                best = Mathf.Min(best, want - u.position.y);
                anyPlanted = true;
            }
            if (anyPlanted)
            {
                float a = _hipInit ? 1f - Mathf.Exp(-Time.deltaTime / 0.05f) : 1f;   // about 0.1 s to settle
                _hipD = Mathf.Lerp(_hipD, best, a); _hipInit = true;
            }
            hips.position = hp0 + Vector3.up * (_hipD * sp);
            for (int i = 0; i < 2; i++) LegBlend(ids[i][0], ids[i][1], ids[i][2], tg[i], dir, rot[i], sp, MaxReach);
            // toe-off: the foot pitches about the ball, the toes stay flat on the floor
            if (!_oldFeet)
                for (int i = 0; i < 2; i++)
                {
                    if (_toes[i] == null) continue;
                    var tvN = _toes[i].position - feet[i].position;
                    float pn = ToePitch(tvN) - _toeP0i[i];
                    float w = S(-pn / 12f) * (1f - S(lowest[i] / 0.02f)) * sp;
                    if (w > 0.001f) _toes[i].rotation = Quaternion.Slerp(_toes[i].rotation, PitchDelta(tvN, pn, 0f, _toeP0i[i]) * _toes[i].rotation, w);
                }
        }

        // ---- task 60: foot roll. Pitch first (toes up +), then the ankle from it, so the sole point that is lowest sits at the floor (plus the capped lift).
        Vector3 HeelPt(int i, Vector3 ankle, Quaternion r) => ankle + r * _ofsH[i] - Vector3.up * _rH[i];
        Vector3 BallPt(int i, Vector3 ankle, Quaternion r) => ankle + r * _ofsB[i] - Vector3.up * _toeFloor[i];

        Quaternion PitchDelta(Vector3 tv, float cur, float want, float p0)
        {
            var f = tv; f.y = 0f;
            if (f.sqrMagnitude < 1e-8f) return Quaternion.identity;
            var axis = Vector3.Cross(Vector3.up, f.normalized);
            var q = Quaternion.AngleAxis(want - cur, axis);
            if (Mathf.Abs(ToePitch(q * tv) - p0 - want) > Mathf.Abs(ToePitch(Quaternion.Inverse(q) * tv) - p0 - want)) q = Quaternion.Inverse(q);
            return q;
        }

        const float DorsiMax = 20f, PlantarMax = 30f, ToeOffMax = 38f, SwingDownMax = 20f, StrikeUp = 12f, StrikeRoll = 0.075f;

        void FootSolve(int i, float sp, float cyc, Quaternion rot0, Vector3 tv0, float pitch0, Vector3 a0, Vector3 shift, float floorRef,
                       Transform upper, Transform lower, Transform foot, Vector3 dir, out Quaternion Rn, out Vector3 T, out float L)
        {
            var heelW0 = HeelPt(i, a0, rot0);
            var ballW0 = BallPt(i, a0, rot0);
            float Lc = Mathf.Min(heelW0.y, ballW0.y) - floorRef;
            L = Cap(Lc);
            float wC = 1f - S(Lc / 0.04f);                                  // 1 with the sole on the floor
            float u = cyc - _ps[i]; u -= Mathf.Floor(u);                    // 0 at this foot's heel strike
            // the clip's pitch, kept inside the limits: toes down at most 38 on the floor (toe-off), 20 in the air; toes up only before landing
            float lim = Mathf.Lerp(SwingDownMax, ToeOffMax, wC);
            float down = pitch0 < 0f ? -lim * (float)Math.Tanh(-pitch0 / lim) : 0f;
            float want = down;                                              // the clip's own toes-up is dropped (its left foot sits 25 deg toes-up all through the stance); toes come up only in the windows below
            float su = 0f;
            if (u < StrikeRoll) su = StrikeUp * (1f - S(u / StrikeRoll));         // heel strike, then flat within 0.075 of the cycle = 0.15 of a step
            else if (u > 0.88f) su = StrikeUp * S((u - 0.88f) / 0.12f);         // toes come up before the heel lands
            if (u < StrikeRoll) want = su; else if (su > 0f) want = Mathf.Max(want, su);
            float wantEff = Mathf.Lerp(pitch0, want, sp);
            Rn = PitchDelta(tv0, pitch0, wantEff, _toeP0i[i]) * rot0;
            T = a0 + shift;
            if (Environment.GetEnvironmentVariable("ODDITY_DBG") == "1" && i == 0 && sp > 0.99f && _t > 1.0f && _t < 2.2f)
                Debug.Log("ROOMTEST dbg t " + _t.ToString("0.00", CultureInfo.InvariantCulture) + " u " + u.ToString("0.00", CultureInfo.InvariantCulture) + " Lc " + (Lc * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " wC " + wC.ToString("0.00", CultureInfo.InvariantCulture) + " pitch0 " + pitch0.ToString("0", CultureInfo.InvariantCulture) + " want " + want.ToString("0", CultureInfo.InvariantCulture));
            float la = Vector3.Distance(upper.position, lower.position), lb = Vector3.Distance(lower.position, foot.position);
            for (int it = 0; it < 3; it++)
            {
                var ho = HeelPt(i, Vector3.zero, Rn); var bo = BallPt(i, Vector3.zero, Rn);
                bool heelLow = ho.y <= bo.y;
                var piv = heelLow ? ho : bo;
                var pivClip = (heelLow ? heelW0 : ballW0) + shift;
                T = Vector3.Lerp(a0 + shift, pivClip - piv, wC);
                T.y = floorRef + L - piv.y;
                var kn = SolveJoint(upper.position + Vector3.up * (_hipD * sp), T, la, lb, dir, MaxReach);
                float ang = _ang0[i] - Vector3.Angle(kn - T, Rn * _fvLocal[i]);   // dorsiflexion +
                float exc = ang > DorsiMax ? ang - DorsiMax : ang < -PlantarMax ? ang + PlantarMax : 0f;
                if (Mathf.Abs(exc) < 0.5f) break;
                wantEff -= exc;
                Rn = PitchDelta(tv0, pitch0, wantEff, _toeP0i[i]) * rot0;
            }
        }

        // ---- task 60: body layers, closed loop on the measured angles, driven from the clip's cycle (cyc) so they stay in step with the feet
        const float PelvisYawAmp = 10f, ShoulderYawAmp = 8f, PelvisRollAmp = 4f, ArmSwingAmp = 15f;

        void Drive(Transform bone, Vector3 axis, Func<float> measure, float target, float gain)
        {
            float e0 = target - measure();
            if (Mathf.Abs(e0) < 0.01f || gain <= 0f) return;
            var r0 = bone.rotation;
            var q = Quaternion.AngleAxis(e0 * gain, axis);
            bone.rotation = q * r0;
            if (Mathf.Abs(target - measure()) > Mathf.Abs(e0)) bone.rotation = Quaternion.Inverse(q) * r0;   // wrong way round
        }

        // pelvis turns with the swinging leg (the leg going forward carries its hip forward), the swing hip drops a little
        void PelvisLayer(float sp, Vector3 dir, float cyc)
        {
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var root = _harin.transform;
            var hips = B(HumanBodyBones.Hips);
            Transform l = B(HumanBodyBones.LeftUpperLeg), r = B(HumanBodyBones.RightUpperLeg);
            float ang = 2f * Mathf.PI * (cyc - _phL);
            Func<float> yaw = () => { var v = root.InverseTransformVector(r.position - l.position); return Mathf.Atan2(-v.z, v.x) * Mathf.Rad2Deg; };
            Func<float> roll = () => { var v = root.InverseTransformVector(r.position - l.position); return Mathf.Atan2(v.y, Mathf.Sqrt(v.x * v.x + v.z * v.z)) * Mathf.Rad2Deg; };
            Drive(hips, Vector3.up, yaw, PelvisYawAmp * Mathf.Cos(ang), sp);
            Drive(hips, dir, roll, -PelvisRollAmp * Mathf.Sin(ang), sp);
        }

        // shoulders against the pelvis (spine and chest share the twist), arms swung opposite to the same-side leg, forearms, head level
        void UpperLayer(float sp, Vector3 dir, float cyc)
        {
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var root = _harin.transform;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var spine = B(HumanBodyBones.Spine);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            Transform lu = B(HumanBodyBones.LeftUpperArm), ll = B(HumanBodyBones.LeftLowerArm), lh = B(HumanBodyBones.LeftHand);
            Transform ru = B(HumanBodyBones.RightUpperArm), rl = B(HumanBodyBones.RightLowerArm), rh = B(HumanBodyBones.RightHand);
            float ang = 2f * Mathf.PI * (cyc - _phL);
            Func<float> shYaw = () => { var v = root.InverseTransformVector(ru.position - lu.position); return Mathf.Atan2(-v.z, v.x) * Mathf.Rad2Deg; };
            Drive(spine, Vector3.up, shYaw, -ShoulderYawAmp * Mathf.Cos(ang), 0.5f * sp);
            Drive(chest, Vector3.up, shYaw, -ShoulderYawAmp * Mathf.Cos(ang), 0.5f * sp / (1f - 0.5f * sp));
            float swing = ArmSwingAmp * _arms;
            Func<Transform, Transform, float> armAng = (u, lo) => { var v = root.InverseTransformVector(lo.position - u.position); return Mathf.Atan2(v.z, -v.y) * Mathf.Rad2Deg; };
            Drive(lu, side, () => armAng(lu, ll), -swing * Mathf.Cos(ang), sp);
            Drive(ru, side, () => armAng(ru, rl), swing * Mathf.Cos(ang), sp);
            // forearm bend: 15 to 25 degrees, more as the arm swings forward, trailing the swing by 0.1 of a cycle
            float fwdL = -Mathf.Cos(2f * Mathf.PI * (cyc - 0.1f - _phL));
            Func<Transform, Transform, Transform, float> bend = (u, lo, h) => Vector3.Angle(lo.position - u.position, h.position - lo.position);
            Vector3 Hinge(Transform u, Transform lo, Transform h) { var c = Vector3.Cross(lo.position - u.position, h.position - lo.position); return c.sqrMagnitude > 1e-8f ? c.normalized : -side; }
            Drive(ll, Hinge(lu, ll, lh), () => bend(lu, ll, lh), 20f + 5f * fwdL, sp);
            Drive(rl, Hinge(ru, rl, rh), () => bend(ru, rl, rh), 20f - 5f * fwdL, sp);
            // head: level and along the path, the shoulder turn is taken back out through the neck and head
            var neck = B(HumanBodyBones.Neck);
            Func<Vector3, Vector3> hd = v => Quaternion.Inverse(root.rotation) * _head.rotation * Quaternion.Inverse(_head0) * v;
            Func<float> hYaw = () => { var f = hd(Vector3.forward); return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; };
            Func<float> hRoll = () => { var f = hd(Vector3.up); return Mathf.Atan2(f.x, f.y) * Mathf.Rad2Deg; };
            if (neck != null) { Drive(neck, Vector3.up, hYaw, 0f, 0.4f * sp); Drive(_head, Vector3.up, hYaw, 0f, 0.6f * sp / (1f - 0.4f * sp)); }
            else Drive(_head, Vector3.up, hYaw, 0f, sp);
            Drive(_head, dir, hRoll, 0f, sp);
        }

        static string V(Vector3 v) => v.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.00", CultureInfo.InvariantCulture);

        static string ArmDirs(Animator an)
        {
            string s = "";
            foreach (bool left in new[] { true, false })
            {
                var sh = an.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
                var up = an.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lo = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hd = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                s += (left ? " L" : " R") + " shoulder " + (sh == null ? "NONE" : V((up.position - sh.position).normalized)) + " upper " + V((lo.position - up.position).normalized) + " fore " + V((hd.position - lo.position).normalized);
            }
            return s;
        }

        // Arm directions: as the file leaves them, and with every muscle at 0 (what the Humanoid retarget treats as the avatar's rest).
        static string RestAndZero(Animator an)
        {
            var bones = new System.Collections.Generic.List<Transform>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++) { var x = an.GetBoneTransform(b); if (x != null) bones.Add(x); }
            var rot = bones.Select(x => x.localRotation).ToArray();
            var pos = bones.Select(x => x.localPosition).ToArray();
            string asLoaded = ArmDirs(an);
            var h = new HumanPoseHandler(an.avatar, an.transform);
            var pose = new HumanPose();
            h.GetHumanPose(ref pose);
            pose.muscles = new float[pose.muscles.Length];
            pose.bodyRotation = Quaternion.identity;
            h.SetHumanPose(ref pose);
            string zero = ArmDirs(an);
            h.Dispose();
            for (int i = 0; i < bones.Count; i++) { bones[i].localRotation = rot[i]; bones[i].localPosition = pos[i]; }
            return "as loaded:" + asLoaded + " | muscles 0:" + zero;
        }

        // arm directions in the hips' own frame (x her left/right, y up, z forward) and where her chest faces in the world
        static string HipsLocal(Animator an)
        {
            // the body's yaw from the line between the two shoulders (+X = right when she faces +Z), arms given in that body frame
            var lu = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var ru = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var rootInv = Quaternion.Inverse(an.transform.rotation);
            var line = rootInv * (ru.position - lu.position);
            float yaw = Mathf.Atan2(-line.z, line.x) * Mathf.Rad2Deg;
            var unyaw = Quaternion.AngleAxis(-yaw, Vector3.up); // undoes a yaw of +yaw
            string s = "bodyYaw " + yaw.ToString("0");
            foreach (bool left in new[] { true, false })
            {
                var up = an.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lo = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hd = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                s += (left ? " | L" : " | R") + " upper " + V(unyaw * rootInv * (lo.position - up.position).normalized) + " fore " + V(unyaw * rootInv * (hd.position - lo.position).normalized);
            }
            return s;
        }

        void Diagnose(GameObject go)
        {
            foreach (var nm in new[] { "FemaleIdle", "Yawn" })
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Oddity/DevMotion/Resources/Motion/" + nm + ".fbx");
                var inst = Instantiate(src, new Vector3(50f, 0f, 0f), Quaternion.identity);
                var an = inst.GetComponent<Animator>();
                var g = PlayableGraph.Create("probe");
                g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var cp = AnimationClipPlayable.Create(g, Clip(nm));
                AnimationPlayableOutput.Create(g, "o", an).SetSourcePlayable(cp);
                foreach (float tt in new[] { 1f, 4f, 7f, 11f })
                {
                    cp.SetTime(tt % cp.GetAnimationClip().length); g.Evaluate(0f);
                    Debug.Log("ROOMTEST diag own-avatar " + nm + " t" + tt + " " + HipsLocal(an));
                }
                g.Destroy();
                Destroy(inst);
            }
            Debug.Log("ROOMTEST diag model " + RoomTest.Model + " avatar human " + _anim.avatar.isHuman + " valid " + _anim.avatar.isValid + " :: " + RestAndZero(_anim));
            var imp = AssetImporter.GetAtPath(RoomTest.Model) as UnityEngine.Object;
            foreach (var nm in new[] { "Bored", "Yawn", "Bashful" })
            {
                string path = "Assets/Oddity/DevMotion/Resources/Motion/" + nm + ".fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var inst = Instantiate(src, new Vector3(50f, 0f, 0f), Quaternion.identity);
                var an = inst.GetComponent<Animator>();
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                var hb = mi.humanDescription.human;
                string sh = string.Join(",", hb.Where(x => x.humanName.Contains("Shoulder")).Select(x => x.humanName + "=" + x.boneName));
                Debug.Log("ROOMTEST diag fbx " + nm + " avatar human " + an.avatar.isHuman + " armStretch " + mi.humanDescription.armStretch + " upperArmTwist " + mi.humanDescription.upperArmTwist + " mapped " + hb.Length + " shoulders [" + sh + "] :: " + RestAndZero(an));
                Destroy(inst);
            }
        }

        const float HalfLid = 0.15f;
        const float EyeToCamera = 14f;   // degrees; a negative head yaw turns the head to frame left
        float _toCam;
        Vector3 _footL0, _footR0;
        Quaternion _footLRot0, _footRRot0;
        bool _feetCaptured; // bored, half-lidded

        float PopScale(float t, float a, float b)
        {
            const float ease = 0.15f;
            if (t < a || t > b) return 0f;
            if (t < a + ease)
            {
                float u = (t - a) / ease;
                const float c1 = 3.2f; // overshoot a little past 1 and settle
                float x = u - 1f;
                return 1f + (c1 + 1f) * x * x * x + c1 * x * x;
            }
            if (t > b - ease)
            {
                float u = (b - t) / ease;
                return u * u * (3f - 2f * u);
            }
            return 1f;
        }

        void Update()
        {
            if (_harin == null || _done) return;
            if (_skin) { SkinUpdate(); return; }
            int total = Mathf.RoundToInt((RoomTest.DebugClip != null ? DebugLength : Length) * Fps);
            int k = _frame;                            // the state set now is frame k (negative while warming up)
            try
            {
                if (k >= 1) Capture();                 // the picture of state k - 1
                if (k >= 1 && _floorCheck && _fcR != null && k <= total) { float tl = TrueLowest(); _fcAll.Add(tl); if (_prevW > 0.25f) { _fcWalk.Add(tl); if (!_soleSamples.Count.Equals(0) && tl - LowestSole() < _worstMiss) { _worstMiss = tl - LowestSole(); _worstK = k - 1; _worstW = _prevW; } } else if (_prevW > 0.001f) _fcBlend.Add(tl); if (_d65 && _prevW > 0.99f) Shoe65Frame(k - 1); }
                if (k >= total) { _done = true; Finish(); return; }
            }
            catch (Exception e) { _done = true; Fail(e.ToString()); return; }
            _frame++;
            if (k % 60 == 0) Debug.Log("ROOMTEST frame " + k);
            _t = Mathf.Max(0f, k / (float)Fps);
            float t = _t;
            Motion(t);
            if (_rawMode) return;
            _harin.SetMouth(0.04f, 0f, 0f); // a tiny neutral mouth
            // slow beat: the eyes find the camera first (5.3 s), the head follows at 6 s, holds 1 s, then both leave again
            if (_walkOnly) { _harin.SetEyeAngles(0f, 3f); return; }
            float fin = Mathf.Max(0f, _endT - 18.9f); // the old final beat sat at 19.8..21.0 s for a walk ending at 18.9 s; it now follows the walk end
            _toCam = Ramp(t, 6f, 6.9f) - Ramp(t, 7.9f, 9f) + Ramp(t, 20.3f + fin, 21.0f + fin);
            float eyeLead = Ramp(t, 5.3f, 6f) - Ramp(t, 8.7f, 9.4f) + Ramp(t, 19.8f + fin, 20.4f + fin);
            float lead = Mathf.Clamp((HeadYaw(t + 0.2f) - HeadYaw(t)) * 0.3f, -10f, 10f); // the eyes go 0.2 s ahead of the head
            _harin.SetEyeAngles(EyeToCamera * eyeLead * (1f - _toCam) + lead * (1f - _toCam) + EyeToCamera * Glance(t + EyeLead) * _walkW, 3f);

            // cut-ins pop at the lower right
            Pop(_cutBored, 0f);
            Pop(_cutSmug, 0f);
        }

        // 0..1: the side-eye beat during the walk (head turns in over 0.3 s, holds 0.4 s, back over 0.4 s); the eyes call it 0.15 s earlier
        float Glance(float t) => Ramp(t, _glanceStart, _glanceStart + GlanceIn) - Ramp(t, _glanceStart + GlanceIn + GlanceHold, _glanceStart + GlanceIn + GlanceHold + GlanceOut);

        void Pop(Transform q, float s) { q.localScale = new Vector3(_cutSize * s, _cutSize * s, 1f); }

        static Vector3 SolveJoint(Vector3 s, Vector3 target, float a, float b, Vector3 pole, float maxReach = 1f)
        {
            var d = target - s;
            float c = Mathf.Clamp(d.magnitude, 1e-4f, (a + b) * maxReach - 1e-4f);
            var dir = d.normalized;
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            var p = Vector3.ProjectOnPlane(pole, dir);
            if (p.sqrMagnitude < 1e-6f) p = Vector3.ProjectOnPlane(Vector3.forward, dir);
            p.Normalize();
            return s + a * (dir * cosA + p * Mathf.Sqrt(1f - cosA * cosA));
        }

        // two-bone leg: the foot goes to target, the knee bends towards pole, the sole keeps the given world rotation
        void Leg(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones footId, Vector3 target, Vector3 pole, Quaternion footRot, float maxReach = 1f)
        {
            var up = _anim.GetBoneTransform(upperId);
            var lo = _anim.GetBoneTransform(lowerId);
            var ft = _anim.GetBoneTransform(footId);
            float a = Vector3.Distance(up.position, lo.position), b = Vector3.Distance(lo.position, ft.position);
            var knee = SolveJoint(up.position, target, a, b, pole, maxReach);
            up.rotation = Quaternion.FromToRotation(lo.position - up.position, knee - up.position) * up.rotation;
            lo.rotation = Quaternion.FromToRotation(ft.position - lo.position, target - knee) * lo.rotation;
            ft.rotation = footRot;
        }

        // The pose layer on top of FemaleIdle (task: bored and cat-like): weight on her left leg, hips shifted and rolled with
        // the free knee bent and that foot forward, shoulders lifted with a tilt against the hips, head away and half-lidded,
        // arms loose and a little out. The Animator rewrites every humanoid bone each Update, so nothing accumulates.
        void PoseLayer()
        {
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var root = _harin.transform;
            var R = root.rotation;                       // the root turns for the walk, so every axis is the root's own
            Vector3 fwd = R * Vector3.forward, upv = Vector3.up, rgt = R * Vector3.right;
            float P = 1f - _walkW;                       // the whole layer fades out while the walk clip drives the body
            var hips = B(HumanBodyBones.Hips);
            var lf = B(HumanBodyBones.LeftFoot);
            var rf = B(HumanBodyBones.RightFoot);
            if (!_feetCaptured)
            {
                _feetCaptured = true;
                float ankle = _harin.AnkleHeight;
                // plants live in the root's frame, so they are right wherever she has walked to and turned
                _footL0 = root.InverseTransformPoint(new Vector3(lf.position.x, ankle, lf.position.z));
                _footR0 = root.InverseTransformPoint(new Vector3(rf.position.x, ankle, rf.position.z));
                _footLRot0 = Quaternion.Inverse(R) * lf.rotation;
                _footRRot0 = Quaternion.Inverse(R) * rf.rotation;
            }
            float t = _t;
            // weight: +1 on her left leg, -1 on her right; three smooth moves, each with a 3 cm step and a 1.5 cm hip dip
            float w = 1f - 2f * Ramp(t, 1.5f, 2.2f) + 2f * Ramp(t, 9.2f, 9.9f) - 2f * Ramp(t, 14.4f, 15.1f);
            float bump = 0f, liftL = 0f, liftR = 0f;
            for (int i = 0; i < 3; i++)
            {
                float a = ShiftAt[i];
                if (t < a || t > a + 0.7f) continue;
                float bp = Mathf.Sin(Mathf.PI * (t - a) / 0.7f);
                bump = bp;
                if (i == 1) liftR = bp; else liftL = bp; // the foot that leaves the load steps
            }
            float swayH = Mathf.Sin(t * 2f * Mathf.PI / 3.7f), swayA = Mathf.Sin(t * 2f * Mathf.PI / 4.3f);
            float shrug = Ramp(t, 8f, 8.2f) - Ramp(t, 8.5f, 9f);
            float sigh = Ramp(t, 0.8f, 1.4f) - Ramp(t, 1.4f, 2.2f);
            // hips: over the weight leg, a little lower, rolled; sway on top
            hips.position += P * (R * new Vector3(-HipShift * w + 0.012f * swayH, 0f, 0f) + new Vector3(0f, -HipDrop - 0.015f * bump, 0f));
            hips.rotation = Quaternion.AngleAxis(P * (-HipRoll * w + 1.5f * swayH), fwd) * hips.rotation;
            // the torso straightens most of that out again
            var spine = B(HumanBodyBones.Spine);
            spine.rotation = Quaternion.AngleAxis(P * HipRoll * 0.5f * w, fwd) * spine.rotation;
            // head path and chest follow
            float yaw = HeadYaw(t) * (1f - _toCam);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            if (chest != null)
            {
                chest.rotation = Quaternion.AngleAxis(P * 0.2f * yaw, upv) * chest.rotation;
                chest.rotation = Quaternion.AngleAxis(P * -3f * sigh, rgt) * chest.rotation;
            }
            // legs: the free foot (a = 1: her right) a little forward and in, mirrored for the left
            float a1 = 0.5f + 0.5f * w;
            float tapU = (t - 15.3f) / 1.2f;
            float tap = tapU > 0f && tapU < 1f ? 0.5f - 0.5f * Mathf.Cos(tapU * 6f * Mathf.PI) : 0f;
            var rFree = R * (new Vector3(-0.035f, 0f, FreeFootForward) * a1);
            var lFree = R * (new Vector3(0.035f, 0f, FreeFootForward) * (1f - a1));
            var rRot = Quaternion.Slerp(R * _footRRot0, Quaternion.AngleAxis(-8f, upv) * R * _footRRot0, a1);
            var lRot = Quaternion.Slerp(R * _footLRot0, Quaternion.AngleAxis(8f, upv) * R * _footLRot0, 1f - a1);
            rRot = Quaternion.AngleAxis(-FootTap * tap * a1, rgt) * rRot;
            lRot = Quaternion.AngleAxis(-FootTap * tap * (1f - a1), rgt) * lRot;
            var lPlant = root.TransformPoint(_footL0); lPlant.y = _harin.AnkleHeight;
            var rPlant = root.TransformPoint(_footR0); rPlant.y = _harin.AnkleHeight;
            if (P > 0.001f) // no foot targets while the clip walks
            {
                LegBlend(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, lPlant + lFree + Vector3.up * (StepLift * liftL), R * new Vector3(-0.1f - 0.05f * (1f - a1), 0f, 1f), lRot, P);
                LegBlend(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rPlant + rFree + Vector3.up * (StepLift * liftR), R * new Vector3(0.1f + 0.05f * a1, 0f, 1f), rRot, P);
            }
            // shoulders: both lifted (sigh, shrug on top), tilted against the hips; outer end up = + about Z on her right
            float lift = ShoulderLift + 5f * sigh + 9f * shrug, tilt = ShoulderTilt * w;
            var shL = B(HumanBodyBones.LeftShoulder);
            var shR = B(HumanBodyBones.RightShoulder);
            shR.rotation = Quaternion.AngleAxis(P * (lift + tilt), fwd) * shR.rotation;
            shL.rotation = Quaternion.AngleAxis(P * -(lift - tilt), fwd) * shL.rotation;
            // arms stay down (the whole shoulder rotation and the torso roll are taken back off the upper arms): a slow swing in opposite phase; the forearms bend a little more during a weight shift
            float roll = -0.5f * HipRoll * w + 1.5f * swayH; // net roll the torso passes to the shoulders
            var uR = B(HumanBodyBones.RightUpperArm);
            var uL = B(HumanBodyBones.LeftUpperArm);
            uR.rotation = Quaternion.AngleAxis(P * (ArmOut - ArmKeep * (lift + tilt + roll)), fwd) * Quaternion.AngleAxis(P * 4f * swayA, rgt) * uR.rotation;
            uL.rotation = Quaternion.AngleAxis(P * (-ArmOut + ArmKeep * (lift - tilt - roll)), fwd) * Quaternion.AngleAxis(P * -4f * swayA, rgt) * uL.rotation;
            var bend = Quaternion.AngleAxis(P * -6f * bump, rgt);
            B(HumanBodyBones.RightLowerArm).rotation = bend * B(HumanBodyBones.RightLowerArm).rotation;
            B(HumanBodyBones.LeftLowerArm).rotation = bend * B(HumanBodyBones.LeftLowerArm).rotation;
            // head: turned along the path, tilted with the weight, to the camera on the beat. The neck takes part of the turn.
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            if (neck != null) neck.rotation = Quaternion.AngleAxis(P * yaw * 0.4f, upv) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(P * yaw * 0.6f, upv) * head.rotation;
            head.rotation = Quaternion.AngleAxis(P * (HeadDown * (1f - 0.5f * _toCam) - 8f * _toCam), rgt) * head.rotation;
            head.rotation = Quaternion.AngleAxis(P * (HeadTilt * w * (1f - _toCam) + 6f * shrug), fwd) * head.rotation;
            // walk-only head layer: chin up the whole walk, and the side-eye towards the camera (her right while she walks to frame right)
            float wk = _walkW;
            if (wk > 0.001f)
            {
                float g = Glance(t) * wk;
                if (neck != null) neck.rotation = Quaternion.AngleAxis(GlanceYaw * g * 0.4f, upv) * neck.rotation;
                head.rotation = Quaternion.AngleAxis(GlanceYaw * g * 0.6f, upv) * head.rotation;
                head.rotation = Quaternion.AngleAxis(-ChinUp * wk, rgt) * head.rotation;
            }
        }

        // Leg() blended with what the clip gave: blend 1 is the full pose layer, 0 leaves the clip alone.
        void LegBlend(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones footId, Vector3 target, Vector3 pole, Quaternion footRot, float blend, float maxReach = 1f)
        {
            var up = _anim.GetBoneTransform(upperId);
            var lo = _anim.GetBoneTransform(lowerId);
            var ft = _anim.GetBoneTransform(footId);
            var ru = up.rotation; var rl = lo.rotation; var rfo = ft.rotation;
            Leg(upperId, lowerId, footId, target, pole, footRot, maxReach);
            if (blend >= 0.999f) return;
            up.rotation = Quaternion.Slerp(ru, up.rotation, blend);
            lo.rotation = Quaternion.Slerp(rl, lo.rotation, blend);
            ft.rotation = Quaternion.Slerp(rfo, ft.rotation, blend);
        }

        static readonly float[] ShiftAt = { 1.5f, 9.2f, 14.4f };
        static readonly float[] PathT = { 0f, 2.6f, 3.2f, 3.8f, 4.4f, 8.9f, 9.6f, 14.2f, 14.9f, 18f };
        static readonly float[] PathYaw = { -45f, -45f, 30f, 30f, -20f, -20f, 35f, 35f, -30f, -30f };
        const float ArmKeep = 0.65f; // share of the shoulder rotation taken back off the upper arms: 1 buries the hand in the hem, 0 gives a penguin
        const float StepLift = 0.03f, FootTap = 18f;

        static float HeadYaw(float t)
        {
            for (int i = 1; i < PathT.Length; i++)
                if (t <= PathT[i]) return Mathf.Lerp(PathYaw[i - 1], PathYaw[i], Ramp(t, PathT[i - 1], PathT[i]));
            return PathYaw[PathYaw.Length - 1];
        }

        const float HipShift = 0.05f, HipDrop = 0.02f, HipRoll = 8f, FreeFootForward = 0.10f;
        const float ShoulderLift = 5f, ShoulderTilt = 5f, ArmOut = 0f, HeadDown = 1f, HeadTilt = 10f;

        // ---- task 59: grey mannequin (ODDITY_BONES=1) and posture numbers
        class Seg { public Transform a, b, go; }
        bool _bones;
        readonly List<Seg> _segs = new List<Seg>();
        Transform _mHipBox, _mShBox, _mHead, _mNose;
        Quaternion _head0, _chest0;
        Renderer[] _modelRenderers;
        Transform _tUL, _tUR, _tLL, _tLR, _tLoL, _tLoR, _tNeck, _tHips, _tChest;
        readonly List<float[]> _post = new List<float[]>();   // pelvis yaw, shoulder yaw, lean, pelvis roll, head yaw, head pitch, armL, armR

        static Material Unlit(Color c) { var m = new Material(Shader.Find("Unlit/Color")); m.color = c; return m; }

        Transform Prim(PrimitiveType t, string name, Material m, Transform parent)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        void BuildMannequin()
        {
            _bones = true;
            _modelRenderers = _harin.GetComponentsInChildren<Renderer>(true);
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var root = new GameObject("Mannequin").transform;
            var grey = Unlit(new Color(0.55f, 0.55f, 0.57f));
            var blue = Unlit(new Color(0.20f, 0.45f, 1f));
            var orange = Unlit(new Color(1f, 0.55f, 0.08f));
            var pale = Unlit(new Color(0.92f, 0.92f, 0.92f));
            void Add(Transform a, Transform b, Material m, float r)
            {
                if (a == null || b == null) return;
                var g = Prim(PrimitiveType.Capsule, "seg_" + a.name + "_" + b.name, m, root);
                g.localScale = new Vector3(2f * r, 1f, 2f * r);
                _segs.Add(new Seg { a = a, b = b, go = g });
                _segR[g] = r;
            }
            const float tr = 0.035f, lr = 0.025f;
            var torso = new List<Transform>();
            foreach (var id in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head })
                if (B(id) != null) torso.Add(B(id));
            for (int i = 1; i < torso.Count; i++) Add(torso[i - 1], torso[i], grey, tr);
            foreach (bool left in new[] { true, false })
            {
                var m = left ? blue : orange;
                Add(B(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder), B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), m, lr);
                Add(B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm), m, lr);
                Add(B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm), B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand), m, lr);
                Add(B(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg), B(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg), m, lr);
                Add(B(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg), B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot), m, lr);
                Add(B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot), B(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes), m, lr);
            }
            _mHipBox = Prim(PrimitiveType.Cube, "hipBox", grey, root);
            _mShBox = Prim(PrimitiveType.Cube, "shoulderBox", grey, root);
            _mHead = Prim(PrimitiveType.Sphere, "head", grey, root);
            _mHead.localScale = Vector3.one * 0.17f;
            _mNose = Prim(PrimitiveType.Capsule, "faceMarker", pale, root);
            _mNose.localScale = new Vector3(0.03f, 0.025f, 0.03f);
            var green = Unlit(new Color(0.15f, 0.75f, 0.35f));
            for (int i = 0; i < 2; i++) { _soles[i] = Prim(PrimitiveType.Capsule, "sole" + i, green, root); _soles[i].localScale = new Vector3(0.05f, 0.1f, 0.05f); }
            _tUL = B(HumanBodyBones.LeftUpperLeg); _tUR = B(HumanBodyBones.RightUpperLeg);
            _tLL = B(HumanBodyBones.LeftUpperArm); _tLR = B(HumanBodyBones.RightUpperArm);
            _tLoL = B(HumanBodyBones.LeftLowerArm); _tLoR = B(HumanBodyBones.RightLowerArm);
            Debug.Log("ROOMTEST mannequin built, " + _segs.Count + " capsules, renderers hidden " + _modelRenderers.Length);
        }
        readonly Transform[] _soles = new Transform[2];
        readonly Dictionary<Transform, float> _segR = new Dictionary<Transform, float>();

        // A box lying between two joints, its long axis along the line, upright otherwise.
        static void Beam(Transform t, Vector3 l, Vector3 r, float pad, float height, float depth)
        {
            var x = r - l;
            float len = x.magnitude;
            x /= Mathf.Max(len, 1e-5f);
            var z = Vector3.Cross(x, Vector3.up).normalized;
            var y = Vector3.Cross(z, x);
            t.position = (l + r) * 0.5f;
            t.rotation = Quaternion.LookRotation(z, y);
            t.localScale = new Vector3(len + pad, height, depth);
        }

        void UpdateMannequin()
        {
            foreach (var r in _modelRenderers) if (r != null) r.enabled = false;
            foreach (var s in _segs)
            {
                var d = s.b.position - s.a.position;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                s.go.position = (s.a.position + s.b.position) * 0.5f;
                s.go.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
                float r = _segR[s.go];
                s.go.localScale = new Vector3(2f * r, Mathf.Max(len * 0.5f, r), 2f * r);
            }
            // the sole: from the heel's centre to the ball's centre (green), the two rolling circles' contact line
            for (int i = 0; i < 2; i++)
            {
                var ft = _anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (_soles[i] == null || _toes[i] == null) continue;
                var hC = ft.position + ft.rotation * _ofsH[i] - Vector3.up * _rH[i] * 0.9f; var bC = _toes[i].position - Vector3.up * _toeFloor[i] * 0.9f;
                var d = bC - hC;
                _soles[i].position = (hC + bC) * 0.5f;
                _soles[i].rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                _soles[i].localScale = new Vector3(0.036f, Mathf.Max(d.magnitude * 0.5f, 0.02f), 0.036f);
            }
            Beam(_mHipBox, _tUL.position, _tUR.position, 0.07f, 0.07f, 0.10f);
            Beam(_mShBox, _tLL.position, _tLR.position, 0.07f, 0.05f, 0.08f);
            var face = _head.rotation * Quaternion.Inverse(_head0) * Vector3.forward;
            var eyeL = _eyeL != null ? _eyeL.position : _head.position + Vector3.up * 0.08f;
            var eyeR = _eyeR != null ? _eyeR.position : _head.position + Vector3.up * 0.08f;
            var c = (eyeL + eyeR) * 0.5f + Vector3.up * 0.02f - face * 0.035f;
            _mHead.position = c;
            _mNose.position = c + face * 0.095f;
            _mNose.rotation = Quaternion.FromToRotation(Vector3.up, face);
        }

        void PostureSample()
        {
            var root = _harin.transform;
            Vector3 Loc(Vector3 v) => root.InverseTransformVector(v);
            var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            var neck = _anim.GetBoneTransform(HumanBodyBones.Neck) ?? _anim.GetBoneTransform(HumanBodyBones.UpperChest);
            var chest = _anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? _anim.GetBoneTransform(HumanBodyBones.Chest);
            var pl = Loc(_anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position);
            var sl = Loc(_anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position);
            var nv = Loc(neck.position - hips.position);
            var dH = Quaternion.Inverse(root.rotation) * _head.rotation * Quaternion.Inverse(_head0);
            var dC = Quaternion.Inverse(root.rotation) * chest.rotation * Quaternion.Inverse(_chest0);
            var f = Quaternion.Inverse(dC) * dH * Vector3.forward;
            Vector3 ad(HumanBodyBones u, HumanBodyBones l) => Loc(_anim.GetBoneTransform(l).position - _anim.GetBoneTransform(u).position);
            var aL = ad(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            var aR = ad(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
            _post.Add(new[]
            {
                Mathf.Atan2(-pl.z, pl.x) * Mathf.Rad2Deg,
                Mathf.Atan2(-sl.z, sl.x) * Mathf.Rad2Deg,
                Mathf.Atan2(nv.z, nv.y) * Mathf.Rad2Deg,
                Mathf.Atan2(pl.y, Mathf.Sqrt(pl.x * pl.x + pl.z * pl.z)) * Mathf.Rad2Deg,
                Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg,
                Mathf.Atan2(aL.z, -aL.y) * Mathf.Rad2Deg,
                Mathf.Atan2(aR.z, -aR.y) * Mathf.Rad2Deg,
            });
        }

        // task 60: ankle angle (shin to foot, dorsiflexion +, 0 at the bind pose), foot pitch (toes up +) at heel strike and at the last frame on the floor
        class FootSt { public float angMin = 99f, angMax = -99f, pMin = 99f, pMax = -99f, last; public bool inC; public int since; public float stSum; public int stN; public readonly List<float> strike = new List<float>(), off = new List<float>(), strikeT = new List<float>(), knee = new List<float>(); }
        readonly FootSt[] _fs = { new FootSt(), new FootSt() };

        void FootStats()
        {
            var ft = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftFoot), _anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            var kn = new[] { _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
            for (int i = 0; i < 2; i++)
            {
                if (_toes[i] == null) continue;
                var st = _fs[i];
                var tv = _toes[i].position - ft[i].position;
                float pitch = ToePitch(tv) - _toeP0i[i];
                float ang = _ang0[i] - Vector3.Angle(kn[i].position - ft[i].position, tv);
                st.angMin = Mathf.Min(st.angMin, ang); st.angMax = Mathf.Max(st.angMax, ang);
                st.pMin = Mathf.Min(st.pMin, pitch); st.pMax = Mathf.Max(st.pMax, pitch);
                float low = Mathf.Min(HeelPt(i, ft[i].position, ft[i].rotation).y, BallPt(i, ft[i].position, ft[i].rotation).y);
                bool c = low - (_rawMode ? _clipSole : 0f) < 0.012f;
                st.since++;
                if (c != st.inC && st.since >= 4)   // a change of state must last: no chatter
                {
                    if (c)
                    {
                        st.strike.Add(pitch); st.strikeT.Add(_t);
                        var thigh = _anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                        st.knee.Add(180f - Vector3.Angle(thigh.position - kn[i].position, ft[i].position - kn[i].position));
                    }
                    else st.off.Add(st.last);
                    st.inC = c; st.since = 0;
                }
                if (st.inC && c) { st.last = pitch; st.stSum += pitch; st.stN++; }
            }
        }

        void FootReport()
        {
            string F(float x) => x.ToString("0.0", CultureInfo.InvariantCulture);
            string Md(List<float> l) { if (l.Count == 0) return "n/a"; var o = l.OrderBy(x => x).ToList(); return "median " + F(o[o.Count / 2]) + " (" + F(o[0]) + ".." + F(o[o.Count - 1]) + ", n " + l.Count + ")"; }
            for (int i = 0; i < 2; i++)
            {
                var st = _fs[i];
                Debug.Log("ROOMTEST foot " + (i == 0 ? "L" : "R") + " ankle angle (dorsi +) " + F(st.angMin) + ".." + F(st.angMax) + " | foot pitch (toes up +) " + F(st.pMin) + ".." + F(st.pMax) + " | at heel strike " + Md(st.strike) + " | at toe-off " + Md(st.off) + " | knee bend at strike " + Md(st.knee) + " | mean pitch while in contact " + (st.stN > 0 ? F(st.stSum / st.stN) : "n/a") +
                          " | strike times " + string.Join(" ", st.strikeT.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture))));
            }
        }

        void PostureReport()
        {
            if (_post.Count == 0) { Debug.Log("ROOMTEST posture n/a"); return; }
            string[] names = { "pelvis yaw", "shoulder yaw", "spine lean (hips->neck, + = forward)", "pelvis roll", "head yaw vs chest", "head pitch vs chest", "arm swing L", "arm swing R" };
            var sb = new StringBuilder("ROOMTEST posture over " + _post.Count + " full-speed frames (" + (_post.Count / (float)Fps).ToString("0.0", CultureInfo.InvariantCulture) + " s)");
            for (int k = 0; k < names.Length; k++)
            {
                float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
                foreach (var p in _post) { lo = Mathf.Min(lo, p[k]); hi = Mathf.Max(hi, p[k]); sum += p[k]; }
                sb.Append("\nROOMTEST posture ").Append(names[k]).Append(": ").Append(lo.ToString("0.0", CultureInfo.InvariantCulture)).Append("..").Append(hi.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" range ").Append((hi - lo).ToString("0.0", CultureInfo.InvariantCulture)).Append(" mean ").Append((sum / _post.Count).ToString("0.0", CultureInfo.InvariantCulture));
            }
            Debug.Log(sb.ToString());
        }

        void LateUpdate()
        {
            if (_harin == null) return;
            // FemaleIdle's body yaw is about 0 to -3 degrees (measured on its own avatar and on the VRM), so there is no
            // yaw counter-rotation any more; the root stays upright.
            var root = _harin.transform;
            root.rotation = Quaternion.Euler(0f, _rootYaw, 0f);
            root.position = new Vector3(_rootPos.x, root.position.y, _rootPos.z);
            if (_skin) { SkinLate(root); return; }
            if (_rawMode) { /* nothing of ours writes a bone, and the root keeps y 0: the clip's own hip height decides */ if (_walkOnly) Fix65(); }
            else
            {
                if (_walkOnly) WalkLayer(); else PoseLayer();
                // her feet stay on the floor: the root goes down or up by whatever the lowest sole is off it
                float soleY = Mathf.Min(_harin.SolePosition(false).y, _harin.SolePosition(true).y);
                root.position = new Vector3(root.position.x, root.position.y - soleY, root.position.z);
            }
            if (_walkOnly && _soleSamples.Count > 0 && !_noFloorFix)
            {
                // task 64: the root's y puts the lowest point of the real foot surface 2 mm above the floor, as far as she walks (_walkW); idle keeps its own
                if (_rawMode) root.position = new Vector3(root.position.x, 0f, root.position.z);
                float want = (_holdFix || _walkW > 0.001f) ? (FloorGap - LowestSole(_rawMode)) * (_holdFix ? 1f : Mathf.Clamp01(_walkW * 4f)) : 0f;   // full correction from a quarter of the ramp on: a lone pop of the idle's offset is spread over those frames
                if (_rawMode) { _floorY = Mathf.SmoothDamp(_floorY, want, ref _floorV, FloorSmooth, Mathf.Infinity, 1f / Fps); root.position += Vector3.up * _floorY; ToesOnFloor(); }   // heel and ball set the height, followed over a short time; the toes then bend to the floor
                else if (_walkW > 0.001f) root.position += Vector3.up * want;
            }
            if (_walkOnly) { _prevW = _walkW; _rootYAll.Add(root.position.y); if (_walkW > 0.99f) _rootYWalk.Add(root.position.y); }
            if (Environment.GetEnvironmentVariable("ODDITY_JUMP") == "1")
            {
                // one line per frame, to find where she jumps: t, walk weight, clip time, root y, hips xyz, head xyz, feet y, lowest sole
                Func<float, string> f4 = x => x.ToString("0.0000", CultureInfo.InvariantCulture);
                var hp = _hips.position; var hd = _head.position;
                var fl = _anim.GetBoneTransform(HumanBodyBones.LeftFoot).position; var fr = _anim.GetBoneTransform(HumanBodyBones.RightFoot).position;
                File.AppendAllText(Path.Combine(RoomTest.OutDir, "jump.csv"), string.Join(",", new[] { f4(_t), f4(_walkW), f4(_walkClipT % _walkLen), f4(root.position.y), f4(hp.x), f4(hp.y), f4(hp.z), f4(hd.x), f4(hd.y), f4(hd.z), f4(fl.y), f4(fr.y), f4(_soleSamples.Count > 0 ? LowestSole() : 0f) }) + "\n");
            }
            if (_rawMode && _walkW > 0.99f)
            {
                float lo = float.MaxValue;
                for (int i = 0; i < 2; i++)
                {
                    var ft = _anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    float l = Mathf.Min(HeelPt(i, ft.position, ft.rotation).y, BallPt(i, ft.position, ft.rotation).y);
                    _lowestF[i].Add(l); lo = Mathf.Min(lo, l);
                }
                _lowest.Add(lo);
            }
            if (_shadow != null) _shadow.position = new Vector3(root.position.x, 0.004f, root.position.z + 0.02f);
            if (_walkOnly)
            {
                if (_walkW > 0.99f) Sample(_fin, _rawMode ? _clipAnkle : 0f, true); else _prevOk = false;
                if (_frame >= 1) { while (_pathVx.Count < _frame - 1) _pathVx.Add(0f); _pathVx.Add(_cam.WorldToViewportPoint(root.position + Vector3.up * 0.5f).x); }
            }
            if (_bones) UpdateMannequin();
            if (_walkOnly && _walkW > 0.99f) { PostureSample(); FootStats(); LegSample(); }
            if (_d65 && _walkW > 0.99f) { var fd = root.rotation * Vector3.forward; fd.y = 0f; LeanSample(_anim, fd.normalized, _leanFilm, true); }
            // half-lidded
            var rt = _vrm != null ? _vrm.Runtime : null;
            if (rt != null && !_rawMode)
            {
                float cur = rt.Expression.GetWeight(ExpressionKey.Blink);
                rt.Expression.SetWeight(ExpressionKey.Blink, Mathf.Max(Mathf.Max(cur, HalfLid), _closeEyes));
            }
            // ODDITY_LOOK=45: the head turns this far towards the camera, never past it. Neck 40%, head 60%.
            if (float.TryParse(Environment.GetEnvironmentVariable("ODDITY_LOOK"), NumberStyles.Float, CultureInfo.InvariantCulture, out float look))
            {
                var toCam = Vector3.ProjectOnPlane(_cam.transform.position - _head.position, Vector3.up);
                float off = Vector3.SignedAngle(root.forward, toCam, Vector3.up);
                float turn = Mathf.Sign(off) * Mathf.Min(Mathf.Abs(off), look);
                // a turn shared from the waist up, so the neck alone does not twist: spine 10, chest 20, upper chest 20, neck 25, head 25 percent
                var neckBone = _anim.GetBoneTransform(HumanBodyBones.Neck);
                var bonesUp = new[] { _spine, _anim.GetBoneTransform(HumanBodyBones.Chest), _anim.GetBoneTransform(HumanBodyBones.UpperChest), neckBone, _head };
                var shares = new[] { 0.1f, 0.2f, 0.2f, 0.25f, 0.25f };
                for (int i = 0; i < bonesUp.Length; i++)
                    if (bonesUp[i] != null) bonesUp[i].rotation = Quaternion.AngleAxis(turn * shares[i], Vector3.up) * bonesUp[i].rotation;
                // ODDITY_CHIN=12: chin up by this many degrees about the line across the turned face, neck and head half each
                if (float.TryParse(Environment.GetEnvironmentVariable("ODDITY_CHIN"), NumberStyles.Float, CultureInfo.InvariantCulture, out float chin))
                {
                    var across = Vector3.Cross(Vector3.up, Quaternion.AngleAxis(turn, Vector3.up) * root.forward).normalized;
                    if (neckBone != null) neckBone.rotation = Quaternion.AngleAxis(-chin * 0.5f, across) * neckBone.rotation;
                    _head.rotation = Quaternion.AngleAxis(-chin * 0.5f, across) * _head.rotation;
                }
            }
            // ODDITY_SMILE=0.6: a held smile on the face only, no bones. Raw mode has no HarinPuppet to drive the face.
            if (rt != null && float.TryParse(Environment.GetEnvironmentVariable("ODDITY_SMILE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float smile))
                rt.Expression.SetWeight(ExpressionKey.Relaxed, smile);
            // ODDITY_HAPPY=0.25: a little of the open smile on top; much more and the mouth gapes
            if (rt != null && float.TryParse(Environment.GetEnvironmentVariable("ODDITY_HAPPY"), NumberStyles.Float, CultureInfo.InvariantCulture, out float happy))
                rt.Expression.SetWeight(ExpressionKey.Happy, happy);
        }

        // ------------------------------------------------------------------ skin compare (ODDITY_SKINTEST=1)

        // One frozen standing frame (the idle clip at 0 s, Relaxed 0.7), shot in six looks A..F from three cameras (whole body, legs, face)
        // inside a single run. Only per-renderer material instances and the scene's ambient/key light change; the VRM asset is never written.
        // Raw 1920x1080 stills go to OutDir/skin/raw/<look>_<view>.png; tools/dev.py crops them and builds compare.png.
        bool _skin;
        Light _keyLight;
        class SkinMat
        {
            public Material M; public Renderer R; public int Sub; public bool Face;
            public Color Shade, Rim; public float Shift, Toony, RimPower, RimLift, RimMix;
            public Texture Main, ShadeTex;
        }
        readonly List<SkinMat> _skinMats = new List<SkinMat>();
        Texture2D _skinPaint; Texture _skinPaintSrc;
        Vector3 _skinCamPos; Quaternion _skinCamRot;
        Color _skinAmbient0; float _skinKey0;
        static readonly string[] SkinLooks = { "A", "B", "C", "D", "E", "F" };
        static readonly string[] SkinViews = { "full", "close", "face" };
        static string SkinRawDir => Path.Combine(RoomTest.OutDir, "skin", "raw");

        static string Fl(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        static string Col(Color c) => "(" + Fl(c.r) + "," + Fl(c.g) + "," + Fl(c.b) + ")";

        void SkinPose()
        {
            _rootYaw = 0f; _rootPos = Vector3.zero;
            _bored.SetTime(0.0);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            _graph.Evaluate(0f);
        }

        void SkinCaptureMats()
        {
            _skinCamPos = _cam.transform.position; _skinCamRot = _cam.transform.rotation;
            _skinAmbient0 = RenderSettings.ambientLight; _skinKey0 = _keyLight.intensity;
            foreach (var r in _harin.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var ms = r.materials;   // instantiates per-renderer copies: the imported asset stays untouched
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i];
                    if (m == null || !m.name.Contains("SKIN") || !m.HasProperty("_ShadeColor")) continue;
                    var sm = new SkinMat
                    {
                        M = m, R = r, Sub = i, Face = r.name == "Face",
                        Shade = m.GetColor("_ShadeColor"),
                        Shift = m.GetFloat("_ShadingShiftFactor"), Toony = m.GetFloat("_ShadingToonyFactor"),
                        Rim = m.HasProperty("_RimColor") ? m.GetColor("_RimColor") : Color.black,
                        RimPower = m.HasProperty("_RimFresnelPower") ? m.GetFloat("_RimFresnelPower") : 5f,
                        RimLift = m.HasProperty("_RimLift") ? m.GetFloat("_RimLift") : 0f,
                        RimMix = m.HasProperty("_RimLightingMix") ? m.GetFloat("_RimLightingMix") : 1f,
                        Main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null,
                        ShadeTex = m.HasProperty("_ShadeTex") ? m.GetTexture("_ShadeTex") : null,
                    };
                    _skinMats.Add(sm);
                    Debug.Log("ROOMTEST skin mat " + r.name + "/" + m.name + " shade " + Col(sm.Shade) + " shift " + Fl(sm.Shift) + " toony " + Fl(sm.Toony) + " rim " + Col(sm.Rim) + " pow " + Fl(sm.RimPower) +
                              " lift " + Fl(sm.RimLift) + " mix " + Fl(sm.RimMix) + " main " + (sm.Main != null ? sm.Main.name + " " + sm.Main.width + "x" + sm.Main.height : "none") +
                              " shadeTex " + (sm.ShadeTex != null ? sm.ShadeTex.name + (sm.ShadeTex == sm.Main ? " (same as main)" : "") : "none"));
                }
            }
            Debug.Log("ROOMTEST skin materials found " + _skinMats.Count + ", ambient " + Col(_skinAmbient0) + ", key " + Fl(_skinKey0));
        }

        void SkinRestore()
        {
            foreach (var s in _skinMats)
            {
                var m = s.M;
                m.SetColor("_ShadeColor", s.Shade);
                m.SetFloat("_ShadingShiftFactor", s.Shift);
                m.SetFloat("_ShadingToonyFactor", s.Toony);
                if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", s.Rim);
                if (m.HasProperty("_RimFresnelPower")) m.SetFloat("_RimFresnelPower", s.RimPower);
                if (m.HasProperty("_RimLift")) m.SetFloat("_RimLift", s.RimLift);
                if (m.HasProperty("_RimLightingMix")) m.SetFloat("_RimLightingMix", s.RimMix);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", s.Main);
                if (m.HasProperty("_ShadeTex")) m.SetTexture("_ShadeTex", s.ShadeTex);
            }
            RenderSettings.ambientLight = _skinAmbient0;
            _keyLight.intensity = _skinKey0;
        }

        void SkinApply(string look)
        {
            SkinRestore();
            bool b = look == "B" || look == "F", c = look == "C" || look == "F", d = look == "D" || look == "F", e = look == "E" || look == "F";
            if (b)
                foreach (var s in _skinMats)
                {
                    s.M.SetColor("_ShadeColor", new Color(0.93f, 0.70f, 0.62f, 1f));
                    s.M.SetFloat("_ShadingShiftFactor", s.Face ? -0.30f : -0.10f);
                    s.M.SetFloat("_ShadingToonyFactor", s.Face ? 0.75f : 0.85f);
                }
            if (c)
            {
                RenderSettings.ambientLight = new Color(0.46f, 0.45f, 0.47f);
                _keyLight.intensity = 1.05f;
            }
            if (d && _skinPaint != null)
                foreach (var s in _skinMats)
                    if (!s.Face && s.Main == _skinPaintSrc)
                    {
                        s.M.SetTexture("_MainTex", _skinPaint);
                        if (s.ShadeTex == s.Main) s.M.SetTexture("_ShadeTex", _skinPaint);
                    }
            if (e)
                foreach (var s in _skinMats)
                {
                    if (!s.M.HasProperty("_RimColor")) { Debug.Log("ROOMTEST skin E: " + s.M.name + " has no _RimColor"); continue; }
                    s.M.SetColor("_RimColor", new Color(0.9f, 0.45f, 0.35f) * 0.4f);   // "low strength": 40% of the warm colour
                    s.M.SetFloat("_RimFresnelPower", 3.5f);
                    s.M.SetFloat("_RimLift", 0f);
                    s.M.SetFloat("_RimLightingMix", 1f);
                }
        }

        // Soft round blush on knees, elbows, shoulders, backs of the hands and ankles, painted into a runtime copy of the Body skin texture.
        // Every skin vertex gets a weight from its distance to the nearest joint; the weights are rasterised over the mesh triangles in UV space.
        void SkinBuildPaint()
        {
            var target = _skinMats.FirstOrDefault(s => !s.Face && s.Main != null);
            if (target == null) { Debug.Log("ROOMTEST skin D skipped: no Body SKIN material with a _MainTex"); return; }
            var src = target.Main;
            int tw = src.width, th = src.height;
            var joints = new List<(string n, Vector3 p, float r)>();
            Transform Bn(HumanBodyBones id) => _anim.GetBoneTransform(id);
            void AddJ(string n, Vector3? p, float r) { if (p.HasValue) joints.Add((n, p.Value, r)); }
            Vector3? Pos(HumanBodyBones id) => Bn(id) != null ? Bn(id).position : (Vector3?)null;
            foreach (var side in new[] { "Left", "Right" })
            {
                HumanBodyBones Id(string part) => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + part);
                AddJ(side + "Knee", Pos(Id("LowerLeg")), 0.07f);
                AddJ(side + "Elbow", Pos(Id("LowerArm")), 0.05f);
                AddJ(side + "Shoulder", Pos(Id("UpperArm")), 0.06f);
                AddJ(side + "Ankle", Pos(Id("Foot")), 0.05f);
                var h = Pos(Id("Hand")); var mid = Pos(Id("MiddleProximal"));
                AddJ(side + "HandBack", h.HasValue && mid.HasValue ? (h.Value + mid.Value) * 0.5f : h, 0.045f);
            }
            var weight = new float[tw * th];
            var near = new Dictionary<string, int>();
            foreach (var j in joints) near[j.n] = 0;
            int tris = 0;
            var baked = new Mesh();
            foreach (var g in _skinMats.Where(s => !s.Face && s.Main == src).GroupBy(s => s.R))
            {
                var smr = (SkinnedMeshRenderer)g.Key;
                var shared = smr.sharedMesh;
                var uv = shared.uv;
                smr.BakeMesh(baked, true);
                var bv = baked.vertices;
                if (uv.Length != bv.Length) { Debug.Log("ROOMTEST skin D: uv count " + uv.Length + " != vertex count " + bv.Length + " on " + smr.name); continue; }
                var vw = new float[bv.Length];
                for (int i = 0; i < bv.Length; i++)
                {
                    var wp = smr.transform.TransformPoint(bv[i]);
                    float best = 0f; string bn = null;
                    foreach (var j in joints)
                    {
                        float t = 1f - Vector3.Distance(wp, j.p) / j.r;
                        if (t <= 0f) continue;
                        float w = t * t * (3f - 2f * t);
                        if (w > best) { best = w; bn = j.n; }
                    }
                    vw[i] = best;
                    if (bn != null) near[bn]++;
                }
                foreach (var s in g)
                {
                    var idx = shared.GetIndices(s.Sub);
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        int a = idx[t], b2 = idx[t + 1], c = idx[t + 2];
                        if (vw[a] <= 0f && vw[b2] <= 0f && vw[c] <= 0f) continue;
                        tris++;
                        Raster(weight, tw, th, uv[a], uv[b2], uv[c], vw[a], vw[b2], vw[c]);
                    }
                }
            }
            Destroy(baked);
            Debug.Log("ROOMTEST skin D vertices near joints " + string.Join(", ", near.Select(kv => kv.Key + " " + kv.Value)) + "; " + tris + " triangles rasterised on a " + tw + "x" + th + " texture");
            if (tris == 0) { Debug.Log("ROOMTEST skin D skipped: no skin triangle near any joint (UV not found)"); return; }

            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true, false);
            tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            var tint = new Color(0.95f, 0.58f, 0.52f);
            const float Strength = 0.35f;
            int painted = 0;
            for (int i = 0; i < px.Length; i++)
            {
                float f = Strength * Mathf.Clamp01(weight[i]);
                if (f <= 0f) continue;
                painted++;
                var c = px[i];
                c.r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * (1f + f * (tint.r - 1f))), 0, 255);
                c.g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * (1f + f * (tint.g - 1f))), 0, 255);
                c.b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * (1f + f * (tint.b - 1f))), 0, 255);
                px[i] = c;
            }
            tex.SetPixels32(px);
            tex.wrapMode = src.wrapMode; tex.filterMode = src.filterMode; tex.anisoLevel = src.anisoLevel;
            tex.Apply(true, false);
            _skinPaint = tex; _skinPaintSrc = src;
            Debug.Log("ROOMTEST skin D painted " + painted + " of " + px.Length + " texels, strength max " + Fl(Strength) + ", tint " + Col(tint));
        }

        // fills the max of the interpolated vertex weights over the triangle's UV footprint, with a small margin so seams stay closed
        static void Raster(float[] buf, int tw, int th, Vector2 a, Vector2 b, Vector2 c, float wa, float wb, float wc)
        {
            Vector2 pa = new Vector2(a.x * tw, a.y * th), pb = new Vector2(b.x * tw, b.y * th), pc = new Vector2(c.x * tw, c.y * th);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)) - 1f)), x1 = Mathf.Min(tw - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x)) + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) - 1f)), y1 = Mathf.Min(th - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y)) + 1f));
            float den = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
            if (Mathf.Abs(den) < 1e-6f) return;
            const float Eps = 0.02f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float l1 = ((pb.y - pc.y) * (qx - pc.x) + (pc.x - pb.x) * (qy - pc.y)) / den;
                    float l2 = ((pc.y - pa.y) * (qx - pc.x) + (pa.x - pc.x) * (qy - pc.y)) / den;
                    float l3 = 1f - l1 - l2;
                    if (l1 < -Eps || l2 < -Eps || l3 < -Eps) continue;
                    l1 = Mathf.Clamp01(l1); l2 = Mathf.Clamp01(l2); l3 = Mathf.Clamp01(l3);
                    float sum = l1 + l2 + l3;
                    if (sum <= 0f) continue;
                    float w = (wa * l1 + wb * l2 + wc * l3) / sum;
                    int i = y * tw + x;
                    if (w > buf[i]) buf[i] = w;
                }
        }

        void SkinCamera(string view)
        {
            var tr = _cam.transform;
            tr.rotation = _skinCamRot;
            if (view == "full") { tr.position = _skinCamPos; return; }
            Vector3 target; float visible;
            if (view == "close")
            {
                target = (_anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position + _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg).position) * 0.5f;   // between the knees
                visible = 0.75f;                                                                                                                         // mid-thigh down to the ankles
            }
            else
            {
                target = (_eyeL.position + _eyeR.position) * 0.5f + Vector3.down * 0.02f;
                visible = 0.30f;
            }
            float dist = visible / (2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            tr.position = target - tr.forward * dist;
        }

        void SkinShot(string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(SkinRawDir, name + ".png"), _tex.EncodeToPNG());
        }

        void SkinUpdate()
        {
            int k = _frame;
            _frame++;
            try
            {
                SkinPose();
                if (k == -1) SkinCaptureMats();
                if (k < 4) return;
                int s = k - 4, job = s / 3, ph = s % 3, jobs = SkinLooks.Length * SkinViews.Length;
                if (job >= jobs)
                {
                    _done = true;
                    Debug.Log("ROOMTEST_DONE skin " + jobs + " stills in " + SkinRawDir.Replace("\\", "/"));
                    _cam.targetTexture = null;
                    RoomTest.End(0);
                    return;
                }
                string look = SkinLooks[job / SkinViews.Length], view = SkinViews[job % SkinViews.Length];
                if (ph == 0)
                {
                    if (job == 0) SkinBuildPaint();
                    SkinApply(look);
                    SkinCamera(view);
                }
                else if (ph == 2) SkinShot(look + "_" + view);   // the picture of the state set at ph 0 (drawn at the end of the frames in between)
            }
            catch (Exception e) { _done = true; Fail(e.ToString()); }
        }

        // after the clip is evaluated: feet on the floor, the held smile
        void SkinLate(Transform root)
        {
            root.position = Vector3.zero;
            if (_soleSamples.Count > 0) root.position = new Vector3(0f, FloorGap - LowestSole(true), 0f);
            if (_shadow != null) _shadow.position = new Vector3(0f, 0.004f, 0.02f);
            var rt = _vrm != null ? _vrm.Runtime : null;
            if (rt == null) return;
            float smile = Env("ODDITY_SMILE", 0.7f);
            rt.Expression.SetWeight(ExpressionKey.Relaxed, smile);
        }

        // ------------------------------------------------------------------ filming

        // The camera draws into the render texture every engine frame; at the start of the next Update that picture
        // (the state set one Update ago) is read back. WaitForEndOfFrame never fires in batchmode, so no coroutine.
        void Capture()
        {
            if (RoomTest.DebugClip != null)
            {
                int shot = _frame - 1;
                if (shot != 30 && shot != 120 && shot != 210 && shot != 330) return;
                var p0 = RenderTexture.active;
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                _tex.Apply();
                RenderTexture.active = p0;
                File.WriteAllBytes(Path.Combine(RoomTest.DebugShots, "t" + (shot / Fps).ToString("00") + ".png"), _tex.EncodeToPNG());
                return;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            _tex.GetRawTextureData<byte>().CopyTo(_buffer);
            if (_proc.HasExited) throw new Exception("ffmpeg stopped: " + Err());
            _proc.StandardInput.BaseStream.Write(_buffer, 0, _buffer.Length);
        }

        void Finish()
        {
            if (RoomTest.DebugClip != null) { Debug.Log("ROOMTEST_DONE debug " + RoomTest.DebugShots); _cam.targetTexture = null; RoomTest.End(0); return; }
            int total = Mathf.RoundToInt(Length * Fps);
            _proc.StandardInput.BaseStream.Flush();
            _proc.StandardInput.Close();
            if (!_proc.WaitForExit(120000)) { _proc.Kill(); throw new Exception("ffmpeg did not finish"); }
            if (_proc.ExitCode != 0) throw new Exception("ffmpeg exit " + _proc.ExitCode + ": " + Err());
            if (_walkOnly) { WalkReport(); PostureReport(); FootReport(); LegReport(); }
            if (_rawMode) RawReport();
            if (_walkOnly) FloorReport();
            if (_d65) { Debug.Log("ROOMTEST d65 lean FILM before fix " + LeanText(_leanPre)); Debug.Log("ROOMTEST d65 lean FILM " + LeanText(_leanFilm)); Shoe65Report(); }
            Debug.Log("ROOMTEST_DONE frames=" + total + " video=" + Path.Combine(RoomTest.OutDir, "room_test.silent.mp4").Replace("\\", "/"));
            _cam.targetTexture = null;
            RoomTest.End(0);
        }

        void RawReport()
        {
            string F(float x) => (x * 100f).ToString("0.0", CultureInfo.InvariantCulture);
            string Md(List<float> l) { if (l.Count == 0) return "n/a"; var o = l.OrderBy(x => x).ToList(); return "median " + F(o[o.Count / 2]) + " cm (" + F(o[0]) + ".." + F(o[o.Count - 1]) + ", n " + o.Count + ")"; }
            Debug.Log("ROOMTEST raw floor (lowest sole point above the floor, - = through it) both feet: " + Md(_lowest) + " | left: " + Md(_lowestF[0]) + " | right: " + Md(_lowestF[1]) +
                      " | root speed " + _walkSpeed.ToString("0.000", CultureInfo.InvariantCulture) + " m/s footIK " + _footIK);
        }

        void WalkReport()
        {
            string F(float x) => x.ToString("0.000", CultureInfo.InvariantCulture);
            string Sk(WalkStat st) { if (st.skate.Count == 0) return "n/a"; var l = st.skate.OrderBy(x => x).ToList(); return "median " + F(l[l.Count / 2]) + " p90 " + F(l[(int)(l.Count * 0.9f)]) + " m/s (" + l.Count + " samples)"; }
            Debug.Log("ROOMTEST walkstats stride " + _stride + " lift " + (_lift > 0f ? F(_lift) : "none") + " arms " + _arms +
                      " | clip leading-foot swing peak " + F(_raw.peak) + " m, straightest knee bend " + _raw.minBend.ToString("0.0", CultureInfo.InvariantCulture) + " deg, max toes-up near floor " + _raw.toeUp.ToString("0.0", CultureInfo.InvariantCulture) +
                      " | final peak " + F(_fin.peak) + " m, straightest knee bend " + _fin.minBend.ToString("0.0", CultureInfo.InvariantCulture) + " deg, max toes-up near floor " + _fin.toeUp.ToString("0.0", CultureInfo.InvariantCulture) +
                      " | final planted-ankle ground speed " + Sk(_fin));
            string St(WalkStat x) { if (x.stance.Count == 0) return "n/a"; var l = x.stance.OrderBy(y => y).ToList(); return "median " + l[l.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) + " (min " + l[0].ToString("0.0", CultureInfo.InvariantCulture) + " max " + l[l.Count - 1].ToString("0.0", CultureInfo.InvariantCulture) + ", " + l.Count + " samples)"; }
            foreach (var kv in new[] { ("clip", _raw), ("final", _fin) })
                Debug.Log("ROOMTEST walkstats2 " + kv.Item1 + " mid-stance knee bend " + St(kv.Item2) + " deg | swing knee min bend " + kv.Item2.minBend.ToString("0.0", CultureInfo.InvariantCulture) + " deg | hip bob range " +
                          ((kv.Item2.hipMax - kv.Item2.hipMin) * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " cm (hips y " + F(kv.Item2.hipMin) + ".." + F(kv.Item2.hipMax) + ") | swing foot peak " + F(kv.Item2.peak) +
                          " m | swing knee fwd of hips max " + F(kv.Item2.kneeFwd) + " m | torso lean mean " + (kv.Item2.leanN > 0 ? (kv.Item2.leanSum / kv.Item2.leanN).ToString("0.0", CultureInfo.InvariantCulture) : "n/a") + " max " + kv.Item2.leanMax.ToString("0.0", CultureInfo.InvariantCulture) + " deg");
            File.WriteAllLines(Path.Combine(RoomTest.OutDir, "walk_path.txt"), _pathVx.Select((x, i) => i + " " + F(x)).ToArray());
        }

        string Err() { lock (_err) return _err.ToString().Trim(); }
    }
}
