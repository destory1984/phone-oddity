using System;
using System.Collections.Generic;
using Oddity.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Oddity.Game
{
    /// <summary>
    /// Stages the prologue (day 12, evening, in the room): places Harin (seated on the bed edge for dinner,
    /// standing for the dishes), frames her face with the main camera, turns each line's <c>beat</c> into
    /// small acting (head, eyes, lean, face, camera distance), shows the lock-screen notice, and plays the
    /// 2.5 s VHS-style rewind into day 1. Everything it changes is restored by <see cref="End"/>.
    /// Unknown beats are ignored with one warning each.
    /// </summary>
    public class PrologueDirector : MonoBehaviour
    {
        public enum Stage { Seated, Standing }

        // camera: vertical field of view and distances from her eyes. The beat table below still uses the
        // older 45 degree distances; DistScale turns them into the 35 degree framing (Medium 0.86 m, Extreme 0.74 m;
        // at 0.85 the v58 bangs covered the top half of the close-up).
        public const float Fov = 35f;
        public const float Wide = 1.6f, Medium = 0.9f, Close = 0.78f, Extreme = 0.78f;
        public const float DistScale = 0.95f;
        /// <summary>Camera height below her eyes (m): at eye level or a little under, never looking down on her.</summary>
        public const float EyeDrop = 0.03f;
        /// <summary>
        /// Her eyes sit this far down from the top of the frame (0 = top, 1 = bottom). 0.35 left the v58 bangs and
        /// the top of her head over nearly the top half of the close-up; 0.28 trades some hair for neck and collar.
        /// </summary>
        public const float EyeLine = 0.28f;
        // 0.55 opened the v58 mouth wide (tongue and mouth inside showed). Task 27: happy 0.35 + relaxed 0.65
        // half shut the eyes over an open mouth and read as sleepy; a little surprised instead keeps the eyes open.
        // Task 30: happy 0.3 still half shut them (the preset is Fcl_ALL_Joy, eyes and mouth in one morph), so the
        // preset is cut to 0.12 and the laugh comes from the mouth and brow parts of the joy face instead.
        public const float HappyWeight = 0.12f;
        public const string HappyEyeOpener = "surprised";
        public const float HappyEyeOpenerWeight = 0.05f;
        // the lips half together and the corners up, so the laugh reads as a grin instead of a gaping mouth
        public const float HappyMouthClose = 0.5f, HappyMouthSmile = 0.5f;
        /// <summary>Joy part morphs of the laugh: a hint of smiling eyes, the grin, raised brows.</summary>
        public const float LaughEyeJoy = 0.08f, LaughMouthJoy = 0.2f, LaughBrowJoy = 0.4f;
        /// <summary>The mouth inside is lit as brightly as her skin; 20% darker in the room so a laugh does not glow.</summary>
        public const float MouthInsideBrightness = 0.8f;
        const float CameraSmooth = 0.3f;     // SmoothDamp time; settles in about 1 s
        const float SeatedSideDeg = 12f, StandingSideDeg = -16f;
        static readonly Vector3 StandPosition = new Vector3(-0.45f, 0f, -0.6f);
        const float StandFacingDeg = 90f + 18f; // +X, turned a little towards the camera side

        public const string RewindBeat = "rewind_to_day_one";
        public const string NoticeBeat = "deletion_notice";
        public const float RewindDuration = 2.5f;
        // Task 30, the rewind in four steps so the effects do not pile up:
        // (a) 0 -> 0.3 s the notice card and the subtitle fade out (CanvasGroup alpha), her face as it was;
        // (b) 0.3 -> 1.32 s noise and tearing at 70% strength (60% of the old 1.7 s), the face held for the first
        //     0.3 s, then flipping back through the lines; (c) black from 1.2 s, fully black by 1.8 s, the outfit
        //     changes at 2.2 s (task 25) while the screen is black; (d) day 1 at 2.5 s.
        public const float RewindUiFadeEnd = 0.3f;
        public const float RewindNoiseStart = 0.3f, RewindNoiseEnd = 0.3f + 0.6f * 1.7f, RewindNoiseStrength = 0.7f;
        public const float RewindFaceHoldEnd = RewindNoiseStart + 0.3f;
        public const float RewindBlackStart = 1.2f, RewindBlackFull = 1.8f, RewindBlackEnd = 2.2f;

        /// <summary>
        /// While the notice card is up her eyes sit this far down the frame (under the card, whose lower edge is
        /// at 0.22) and the camera comes in to <see cref="NoticeDistance"/> so the chest does not fill the bottom half.
        /// </summary>
        public const float NoticeEyeLine = 0.475f, NoticeDistance = 0.62f;

        static readonly string[] KnownBeatList =
        {
            "camera_closeup", "leans_in", "laughs", "holds_back_laughter", "soft_laugh", "avoids_eye_contact",
            "soft_smile", "holds_eye_contact", "teases", "flustered", "stares_then_smiles", "leans_close_then_back",
            "proud_pout", "quiet_confession", "fingertips_brush", "peeks_for_reaction", "looks_down_smiling",
            "waits_for_answer", "mock_indignation", "sets_table", "covers_plate", "dinner_finished", "offers_sponge",
            "brief_pause", "tilts_head", NoticeBeat, RewindBeat,
        };
        public static readonly HashSet<string> KnownBeats = new HashSet<string>(KnownBeatList);
        static readonly HashSet<string> Warned = new HashSet<string>();

        struct Frame
        {
            public string Face;          // null keeps the line's face
            public float HeadYaw, HeadPitch, Lean;
            public bool EyesOnCamera;
            public float EyeYaw, EyePitch;
            public float Dist;
            public bool Reach, HandCut;
        }

        struct HistoryEntry { public Stage Stage; public string Face; public string Beat; }

        HarinPuppet _harin;
        EmergenceSequence _emergence;
        Camera _cam;
        SubtitleBar _subtitle;
        Texture2D _noise;
        Font _font;

        // saved state
        Vector3 _harinPos, _harinScale;
        Quaternion _harinRot;
        Vector3 _camPos;
        Quaternion _camRot;
        float _camFov;
        PhoneCameraFit _fit;
        bool _fitEnabled;
        readonly List<(Renderer r, uint mask)> _masks = new List<(Renderer, uint)>();
        readonly List<(Light l, bool on, Color c, float i)> _lights = new List<(Light, bool, Color, float)>();

        // runtime objects
        readonly List<GameObject> _made = new List<GameObject>();
        Transform _standAnchor;
        Light _faceLight;
        Light _catchLight;
        Canvas _overlay;
        RectTransform _overlayRect;
        GameObject _notice;
        CanvasGroup _noticeGroup;
        bool _noticeShown;
        Text _noticeText;
        RawImage _noiseImg, _noiseRed, _noiseCyan;
        Image _black;
        RectTransform[] _tears;
        Text _osd;

        Stage _stage;
        bool _staged;
        string _baseFace = "relaxed";
        string _beat;
        float _beatStart;
        bool _eyesOnCamera;
        Vector3 _eyes;            // her eye point, measured with no lean
        int _measureFrames = 2;   // after a stage change the bones move on the next LateUpdate
        float _dist = Medium, _distVel;
        float _eyeLine = EyeLine, _eyeLineVel;
        bool _snap = true;
        readonly List<HistoryEntry> _history = new List<HistoryEntry>();

        bool _rewinding;
        bool _covered;
        Action _rewindDone;

        /// <summary>Fired once per rewind when the screen is fully black (the game changes her outfit then).</summary>
        public event Action RewindCovered;

        public bool Active { get; private set; }
        public Stage CurrentStage => _stage;
        public string CurrentBeat => _beat;
        public bool IsRewinding => _rewinding;
        public float RewindTime { get; private set; }
        /// <summary>Rewind playback speed (shots set 0 and use <see cref="SeekRewind"/>).</summary>
        public float RewindSpeed = 1f;
        public bool IsNoticeVisible => _notice != null && _notice.activeSelf;
        /// <summary>Alpha of the notice card (0 when hidden).</summary>
        public float NoticeAlpha => IsNoticeVisible && _noticeGroup != null ? _noticeGroup.alpha : 0f;
        /// <summary>The notice card's lower edge on screen (viewport y, 0 bottom, 1 top).</summary>
        public float NoticeBottomViewportY
        {
            get
            {
                if (_notice == null) return 1f;
                var corners = new Vector3[4];
                ((RectTransform)_notice.transform).GetWorldCorners(corners);
                return _cam.WorldToViewportPoint(corners[0]).y;
            }
        }
        /// <summary>Alpha of the rewind noise layer and of the black cover (0..1).</summary>
        public float NoiseAlpha => _noiseImg != null && _noiseImg.gameObject.activeSelf ? _noiseImg.color.a : 0f;
        public float BlackAlpha => _black != null && _black.gameObject.activeSelf ? _black.color.a : 0f;
        /// <summary>Eye line (share of the frame from the top) the camera is using now.</summary>
        public float CurrentEyeLine => _eyeLine;
        public string NoticeText => _noticeText != null ? _noticeText.text : "";
        /// <summary>Camera distance from her eyes the current beat asks for.</summary>
        public float TargetDistance { get; private set; } = Medium;
        public Camera Camera => _cam;

        public static PrologueDirector Create(GameObject host, HarinPuppet harin, EmergenceSequence emergence,
            Camera camera, SubtitleBar subtitle, Texture2D noise, Font font)
        {
            var d = host.AddComponent<PrologueDirector>();
            d._harin = harin;
            d._emergence = emergence;
            d._cam = camera;
            d._subtitle = subtitle;
            d._noise = noise;
            d._font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return d;
        }

        public static bool IsKnown(string beat) => !string.IsNullOrEmpty(beat) && KnownBeats.Contains(beat);

        // ------------------------------------------------------------------ begin / end

        public void Begin()
        {
            if (Active) return;
            Active = true;
            var ht = _harin.transform;
            _harinPos = ht.position;
            _harinRot = ht.rotation;
            _harinScale = ht.localScale;
            _camPos = _cam.transform.position;
            _camRot = _cam.transform.rotation;
            _camFov = _cam.fieldOfView;
            _fit = _cam.GetComponent<PhoneCameraFit>();
            if (_fit != null) { _fitEnabled = _fit.enabled; _fit.enabled = false; }

            uint harinMask = 0;
            foreach (var r in _harin.GetComponentsInChildren<Renderer>(true))
            {
                _masks.Add((r, r.renderingLayerMask));
                harinMask |= r.renderingLayerMask;
                r.renderingLayerMask |= 1u; // room lights
            }

            SetRoomLights();
            BuildProps();
            BuildFaceLight(harinMask == 0 ? 1u : harinMask);
            BuildOverlay();

            _harin.SetGlitch(0f);
            _harin.SetFade(1f);
            _harin.SetRoomLook(true, prologue: true);
            _harin.SetMouthInsideBrightness(MouthInsideBrightness);
            _cam.fieldOfView = Fov;
        }

        /// <summary>Puts back everything <see cref="Begin"/> changed and removes the props and overlays.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            _rewinding = false;
            if (_harin != null) _harin.BlushSuppressed = false;
            if (_harin != null) _harin.SetMouthInsideBrightness(HarinPuppet.MouthInsideDefault);
            if (_subtitle != null) _subtitle.SetAlpha(1f);
            foreach (var (r, mask) in _masks) if (r != null) r.renderingLayerMask = mask;
            _masks.Clear();
            foreach (var (l, on, c, i) in _lights)
            {
                if (l == null) continue;
                l.enabled = on;
                l.color = c;
                l.intensity = i;
            }
            _lights.Clear();

            _harin.ApplyPose(Pose.None, 0f);
            _harin.SetHeadTurn(0f, 0f);
            _harin.SetLean(0f);
            _harin.SetMouth(0f, 0f, 0f);
            _harin.SetRightArmReach(null);
            _harin.SetEyeTarget(null);
            _harin.SetRoomLook(false);
            _harin.SetFace("neutral");
            _harin.transform.localScale = _harinScale;
            var home = new GameObject("HarinPrologueHome").transform;
            home.SetPositionAndRotation(_harinPos, _harinRot);
            _harin.MoveTo(home);
            Destroy(home.gameObject);

            _cam.transform.SetPositionAndRotation(_camPos, _camRot);
            _cam.fieldOfView = _camFov;
            if (_fit != null) _fit.enabled = _fitEnabled;

            foreach (var go in _made) if (go != null) Destroy(go);
            _made.Clear();
            Destroy(this);
        }

        void SetRoomLights()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.name != "RoomLamp" && l.name != "WindowMoonLight" && l.name != "AppStageKeyLight") continue;
                _lights.Add((l, l.enabled, l.color, l.intensity));
                if (l.name == "AppStageKeyLight") l.enabled = false; // turned her skin pink in the room
                else if (l.name == "RoomLamp")
                {
                    l.enabled = true;
                    l.color = Mathf.CorrelatedColorTemperatureToRGB(3500f);
                    l.intensity *= 0.9f; // evening, the room light is on
                }
                else
                {
                    l.color = Mathf.CorrelatedColorTemperatureToRGB(7500f);
                    l.intensity *= 0.45f; // night outside
                }
            }
        }

        void BuildProps()
        {
            var room = GameObject.Find("Room");
            var wood = MaterialOf("DeskTop");
            var light = MaterialOf("Pillow");
            var seat = _emergence != null ? _emergence.RoomAnchor : null;
            if (seat != null)
            {
                // a low table in front of the bed with two bowls
                var fwd = seat.forward; fwd.y = 0f; fwd.Normalize();
                var basePos = seat.position; basePos.y = 0f;
                var top = basePos + fwd * 0.62f + Vector3.up * 0.34f;
                var rot = Quaternion.LookRotation(fwd);
                MakeProp(PrimitiveType.Cube, "PrologueTable", top - Vector3.up * 0.015f, rot, new Vector3(0.8f, 0.03f, 0.5f), wood, room);
                MakeProp(PrimitiveType.Cube, "PrologueTableLegs", top - Vector3.up * 0.17f, rot, new Vector3(0.7f, 0.31f, 0.4f), wood, room);
                MakeProp(PrimitiveType.Cylinder, "PrologueBowlA", top + rot * new Vector3(-0.14f, 0.025f, -0.08f), rot, new Vector3(0.13f, 0.025f, 0.13f), light, room);
                MakeProp(PrimitiveType.Cylinder, "PrologueBowlB", top + rot * new Vector3(0.16f, 0.025f, 0.06f), rot, new Vector3(0.15f, 0.025f, 0.15f), light, room);
            }
            var stand = new GameObject("PrologueStandAnchor");
            _made.Add(stand);
            stand.transform.SetPositionAndRotation(StandPosition, Quaternion.Euler(0f, StandFacingDeg, 0f));
            _standAnchor = stand.transform;
        }

        static Material MaterialOf(string name)
        {
            var go = GameObject.Find(name);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }

        void MakeProp(PrimitiveType type, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, GameObject parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            if (parent != null) go.transform.SetParent(parent.transform, true);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            _made.Add(go);
        }

        // A soft neutral-white key from the camera's upper right that lights only Harin, so her face reads in
        // every shot, plus a weak point light just in front of her face for the eye highlights.
        void BuildFaceLight(uint harinMask)
        {
            var go = new GameObject("PrologueFaceLight");
            _made.Add(go);
            _faceLight = go.AddComponent<Light>();
            _faceLight.type = LightType.Spot;
            _faceLight.color = Mathf.CorrelatedColorTemperatureToRGB(6500f);
            _faceLight.intensity = 0.5f;
            _faceLight.range = 4f;
            _faceLight.spotAngle = 50f;
            _faceLight.innerSpotAngle = 25f;
            _faceLight.shadows = LightShadows.None;
            var data = _faceLight.GetUniversalAdditionalLightData();
            data.renderingLayers = harinMask;

            var c = new GameObject("PrologueCatchLight");
            _made.Add(c);
            _catchLight = c.AddComponent<Light>();
            _catchLight.type = LightType.Point;
            _catchLight.color = Mathf.CorrelatedColorTemperatureToRGB(6500f);
            _catchLight.intensity = 0.015f;
            _catchLight.range = 1.2f;
            _catchLight.shadows = LightShadows.None;
            _catchLight.GetUniversalAdditionalLightData().renderingLayers = harinMask;
        }

        // ------------------------------------------------------------------ lines and beats

        public static Stage StageFor(string sceneId)
        {
            return sceneId == "p_washing_up" || sceneId == "p_side_by_side" ? Stage.Standing : Stage.Seated;
        }

        /// <summary>Called for each prologue line before its text shows.</summary>
        public void ShowLine(Line line, string sceneId)
        {
            var stage = StageFor(sceneId);
            if (!_staged || stage != _stage) SetStage(stage);
            if (line.Who == "harin") _baseFace = string.IsNullOrEmpty(line.Face) ? "neutral" : line.Face;
            if (!string.IsNullOrEmpty(line.Beat)) PlayBeat(line.Beat);
            else if (line.Who == "harin") PlayBeat(null);
            if (line.Who == "harin") _harin.BlushForLine(line.Face, line.Beat);
            if (_notice != null && line.Beat == NoticeBeat)
            {
                _noticeText.text = line.Text ?? "";
                _notice.SetActive(true);
                _noticeShown = true;
                _noticeGroup.alpha = 1f;
            }
            _history.Add(new HistoryEntry { Stage = _stage, Face = _baseFace, Beat = _beat });
        }

        /// <summary>Starts a beat; null goes back to the default framing. Returns false (and warns once) for unknown beats.</summary>
        public bool PlayBeat(string beat)
        {
            if (beat != null && !KnownBeats.Contains(beat))
            {
                if (Warned.Add(beat)) Debug.LogWarning("PrologueDirector: unknown beat '" + beat + "' ignored");
                return false;
            }
            _beat = beat;
            _beatStart = Time.time;
            return true;
        }

        /// <summary>Shots: hold the current beat at <paramref name="t"/> seconds and jump the camera there.</summary>
        public void SeekBeat(float t)
        {
            _beatStart = Time.time - t;
            _snap = true;
        }

        void SetStage(Stage stage)
        {
            _staged = true;
            _stage = stage;
            if (stage == Stage.Seated && _emergence != null && _emergence.RoomAnchor != null)
            {
                _harin.MoveTo(_emergence.RoomAnchor);
                _harin.ApplyPose(Pose.Seated, 1f);
            }
            else
            {
                _harin.MoveTo(_standAnchor);
                _harin.ApplyPose(Pose.None, 0f);
            }
            _measureFrames = 2;
            _snap = true;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static float Ramp(float t, float seconds) => Smooth(t / seconds);

        static Frame Evaluate(string beat, float t)
        {
            var f = new Frame { Dist = Medium, EyesOnCamera = true };
            float r = Ramp(t, 0.4f);
            switch (beat)
            {
                case "camera_closeup": f.Dist = Close; break;
                case "leans_in": f.Dist = 0.88f; f.Lean = 10f * r; f.HeadPitch = -5f * r; break;
                case "sets_table":
                    f.Dist = Wide;
                    if (t < 0.9f) { f.HeadPitch = 14f; f.EyesOnCamera = false; f.EyePitch = 12f; }
                    break;
                case "teases": f.Dist = 0.86f; f.HeadYaw = -7f * r; f.HeadPitch = -3f * r; f.Lean = 6f * r; break;
                case "soft_laugh":
                case "laughs":
                {
                    float amp = beat == "laughs" ? 3.5f : 2.2f;
                    f.Face = "happy";
                    f.Dist = 0.9f;
                    f.HeadPitch = 5f * r;
                    f.Lean = amp * Mathf.Sin(t * 15f) * Mathf.Exp(-t * 1.6f) + 2f * r;
                    break;
                }
                case "holds_back_laughter":
                    f.Face = "happy"; f.Dist = 0.9f; f.HeadPitch = 9f * r;
                    f.EyesOnCamera = false; f.EyeYaw = 12f; f.EyePitch = 8f;
                    f.Lean = 1.2f * Mathf.Sin(t * 18f) * Mathf.Exp(-t * 1.2f);
                    break;
                case "covers_plate":
                    f.HeadPitch = 16f * r; f.EyesOnCamera = false; f.EyePitch = 15f; f.Lean = 10f * r; break;
                case "proud_pout": f.Dist = 0.8f; f.HeadYaw = 12f * Ramp(t, 0.3f); f.HeadPitch = -4f * r; break;
                case "flustered":
                    f.Face = "flustered"; f.Dist = 0.78f; f.HeadPitch = 13f * r; f.HeadYaw = 10f * r; f.Lean = -3f * r;
                    f.EyesOnCamera = false; f.EyeYaw = 14f; f.EyePitch = 12f;
                    break;
                case "looks_down_smiling":
                    f.Face = "smile"; f.Dist = 0.86f; f.HeadPitch = 15f * r;
                    f.EyesOnCamera = false; f.EyePitch = 14f;
                    break;
                case "peeks_for_reaction": f.Dist = Close; f.HeadPitch = 11f * r; f.Lean = 6f * r; break;
                case "leans_close_then_back":
                {
                    float lean = 24f * (Ramp(t, 0.5f) - Smooth((t - 1.3f) / 0.6f));
                    f.Dist = 0.95f; f.Lean = lean; f.HeadPitch = -0.55f * lean;
                    break;
                }
                case "avoids_eye_contact": f.HeadYaw = 15f * r; f.EyesOnCamera = false; f.EyeYaw = 18f; break;
                case "soft_smile": f.Face = "smile"; f.Dist = 0.78f; f.HeadPitch = 3f * r; break;
                case "holds_eye_contact": f.Face = "smile"; f.Dist = 0.72f; f.HeadPitch = -1f; break;
                case "stares_then_smiles": f.Dist = 0.72f; f.Face = t < 1f ? "neutral" : "smile"; break;
                case "quiet_confession": f.Face = "smile"; f.Dist = Extreme; f.HeadPitch = 3f * r; f.Lean = 2f * r; break;
                case "fingertips_brush":
                case "offers_sponge":
                    f.Reach = t < 1.6f;
                    f.HandCut = beat == "fingertips_brush" && t >= 0.15f && t < 1.35f;
                    f.Lean = 5f * r;
                    break;
                case "tilts_head": f.Dist = 0.86f; f.HeadYaw = -10f * r; f.HeadPitch = -3f * r; break;
                case "mock_indignation": f.Dist = 0.92f; f.Lean = -6f * r; f.HeadPitch = -5f * r; f.HeadYaw = 6f * r; break;
                case "dinner_finished": f.Dist = 1.3f; f.Face = "smile"; break;
                case "brief_pause":
                    f.Dist = 0.88f;
                    if (t < 0.7f) { f.EyesOnCamera = false; f.EyeYaw = -12f; f.EyePitch = 6f; }
                    break;
                case "waits_for_answer": f.Dist = 0.72f; f.Lean = 6f * r; f.HeadPitch = 4f * r; break;
                case NoticeBeat:
                    // she freezes and lifts her eyes to the card above her (eyes only, about 6.5 degrees up)
                    f.Face = "neutral"; f.HeadYaw = -6f * r; f.EyesOnCamera = false; f.EyeYaw = -4f; f.EyePitch = -6.5f;
                    break;
                case RewindBeat: f.Dist = Close; break;
            }
            return f;
        }

        void LateUpdate()
        {
            if (!Active) return;
            if (_rewinding)
            {
                StepRewind();
                return;
            }
            float t = Time.time - _beatStart;
            ApplyFrame(Evaluate(_beat, t), t, _snap);
            _snap = false;
        }

        void ApplyFrame(Frame f, float t, bool snap)
        {
            string face = HarinPuppet.FaceForBeat(f.Face ?? _baseFace, _beat);
            if (face == "happy") ApplyLaughFace(_harin);
            else _harin.SetFace(face);
            _harin.SetHeadTurn(f.HeadYaw, f.HeadPitch);
            _harin.SetLean(f.Lean);
            _harin.SetRightArmReach(f.Reach ? new Vector3(0f, -0.35f, 1f) : (Vector3?)null, Vector3.up);
            if (f.EyesOnCamera != _eyesOnCamera || snap)
            {
                _eyesOnCamera = f.EyesOnCamera;
                if (_eyesOnCamera) _harin.SetEyeTarget(_cam.transform);
            }
            if (!f.EyesOnCamera) _harin.SetEyeAngles(f.EyeYaw, f.EyePitch);

            // her eye point, measured only while she is not leaning so the camera does not follow a lean
            if (_measureFrames > 0 || Mathf.Abs(f.Lean) < 0.5f) _eyes = EyePoint();
            if (_measureFrames > 0)
            {
                _measureFrames--;
                snap = true;
            }

            f.Dist *= DistScale;
            // the notice card covers the top fifth of the screen: frame her under it while it is up
            bool underNotice = NoticeFraming;
            if (underNotice) f.Dist = Mathf.Min(f.Dist, NoticeDistance);
            float eyeLine = underNotice ? NoticeEyeLine : EyeLine;
            TargetDistance = f.Dist;
            if (snap) { _dist = f.Dist; _distVel = 0f; _eyeLine = eyeLine; _eyeLineVel = 0f; }
            else
            {
                _dist = Mathf.SmoothDamp(_dist, f.Dist, ref _distVel, CameraSmooth);
                _eyeLine = Mathf.SmoothDamp(_eyeLine, eyeLine, ref _eyeLineVel, CameraSmooth);
            }

            if (f.HandCut) PlaceHandCamera();
            else PlaceFaceCamera(_dist);
        }

        bool NoticeFraming => _noticeShown && (!_rewinding || RewindTime < RewindFaceHoldEnd);

        /// <summary>
        /// The prologue laugh: a little of the happy preset, the joy mouth and brows on their own, the lips half
        /// together with the corners up. Her eyes stay open (<see cref="HarinPuppet.SmileEyeClosure"/> about 0.2).
        /// </summary>
        public static void ApplyLaughFace(HarinPuppet harin)
        {
            harin.SetFaceMix("happy", HappyWeight, HappyEyeOpener, HappyEyeOpenerWeight);
            harin.SetMouthClose(HappyMouthClose, HappyMouthSmile);
            harin.SetJoyParts(LaughEyeJoy, LaughMouthJoy, LaughBrowJoy);
        }

        Vector3 EyePoint()
        {
            var l = _harin.BonePosition(HumanBodyBones.LeftEye);
            var r = _harin.BonePosition(HumanBodyBones.RightEye);
            if (l != Vector3.zero && r != Vector3.zero) return (l + r) * 0.5f;
            return _harin.BonePosition(HumanBodyBones.Head) + Vector3.up * 0.075f * _harin.transform.lossyScale.y;
        }

        Vector3 FacingDir()
        {
            var fwd = _harin.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            float side = _stage == Stage.Seated ? SeatedSideDeg : StandingSideDeg;
            return Quaternion.AngleAxis(side, Vector3.up) * fwd.normalized;
        }

        /// <summary>
        /// Camera for a face shot at distance d: just under her eye level, tilted so her eyes sit
        /// <see cref="EyeLine"/> of the way down the frame. With the 35 degree lens the bottom edge is
        /// about 0.41 d below her eyes (0.27 m at Extreme: shoulder line; 0.32 m at Medium: above the chest).
        /// </summary>
        public static UnityEngine.Pose FaceCameraPose(Vector3 eyes, Vector3 facingDir, float d) =>
            FaceCameraPose(eyes, facingDir, d, EyeLine);

        /// <summary>The same with her eyes <paramref name="eyeLine"/> of the way down the frame.</summary>
        public static UnityEngine.Pose FaceCameraPose(Vector3 eyes, Vector3 facingDir, float d, float eyeLine)
        {
            var pos = eyes + facingDir * d - Vector3.up * EyeDrop;
            float tanHalf = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float above = Mathf.Atan((1f - 2f * eyeLine) * tanHalf) * Mathf.Rad2Deg; // eyes above the optical axis
            float rise = Mathf.Atan2(EyeDrop, d) * Mathf.Rad2Deg;                    // eyes above the camera
            var yaw = Quaternion.LookRotation(-facingDir, Vector3.up);
            return new UnityEngine.Pose(pos, yaw * Quaternion.Euler(above - rise, 0f, 0f));
        }

        void PlaceFaceCamera(float d)
        {
            var p = FaceCameraPose(_eyes, FacingDir(), d, _eyeLine);
            _cam.fieldOfView = Fov;
            _cam.transform.SetPositionAndRotation(p.position, p.rotation);
            AimFaceLight();
        }

        void PlaceHandCamera()
        {
            var hand = _harin.BonePosition(HumanBodyBones.RightMiddleIntermediate);
            if (hand == Vector3.zero) hand = _harin.BonePosition(HumanBodyBones.RightHand);
            var dir = FacingDir();
            var pos = hand + dir * 0.3f + Vector3.up * 0.12f;
            _cam.fieldOfView = Fov;
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(hand - pos));
            AimFaceLight();
        }

        void AimFaceLight()
        {
            if (_faceLight == null) return;
            var ct = _cam.transform;
            var p = ct.position + ct.right * 0.35f + Vector3.up * 0.4f;
            _faceLight.transform.SetPositionAndRotation(p, Quaternion.LookRotation(_eyes - p));
            if (_catchLight != null)
            {
                // between the camera and her face, a little above the lens
                var toCam = ct.position - _eyes;
                _catchLight.transform.position = _eyes + toCam.normalized * Mathf.Min(0.35f, toCam.magnitude * 0.6f) + Vector3.up * 0.05f;
            }
        }

        // ------------------------------------------------------------------ overlay (notice + rewind)

        void BuildOverlay()
        {
            var go = new GameObject("PrologueOverlay", typeof(RectTransform));
            _made.Add(go);
            go.transform.SetParent(_cam.transform, false);
            _overlay = go.AddComponent<Canvas>();
            _overlay.renderMode = RenderMode.WorldSpace;
            _overlay.worldCamera = _cam;
            _overlay.sortingOrder = 50;
            _overlayRect = (RectTransform)go.transform;
            FitOverlay();

            // lock-screen style notification card near the top
            _notice = new GameObject("Notice", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _noticeGroup = _notice.GetComponent<CanvasGroup>();
            _noticeGroup.interactable = false;
            _noticeGroup.blocksRaycasts = false;
            _notice.transform.SetParent(go.transform, false);
            var nr = (RectTransform)_notice.transform;
            nr.anchorMin = new Vector2(0f, 1f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.pivot = new Vector2(0.5f, 1f);
            nr.offsetMin = new Vector2(40f, -420f);
            nr.offsetMax = new Vector2(-40f, -220f);
            var ni = _notice.GetComponent<Image>();
            ni.color = new Color(0.1f, 0.1f, 0.14f, 0.86f);
            ni.raycastTarget = false;
            var icon = MakeImage(_notice.transform, "Icon", new Color(0.45f, 0.36f, 0.78f, 1f));
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 1f);
            ir.sizeDelta = new Vector2(56f, 56f);
            ir.anchoredPosition = new Vector2(30f, -28f);
            var app = MakeText(_notice.transform, "되감기", 32, new Color(0.8f, 0.8f, 0.86f, 1f), TextAnchor.UpperLeft);
            Place(app.rectTransform, new Vector2(104f, -34f), new Vector2(-30f, -80f));
            var now = MakeText(_notice.transform, "지금", 30, new Color(0.7f, 0.7f, 0.76f, 1f), TextAnchor.UpperRight);
            Place(now.rectTransform, new Vector2(104f, -34f), new Vector2(-30f, -80f));
            _noticeText = MakeText(_notice.transform, "", 42, Color.white, TextAnchor.UpperLeft);
            Place(_noticeText.rectTransform, new Vector2(30f, -104f), new Vector2(-30f, -180f));
            _notice.SetActive(false);

            // rewind layers: noise (plus red and cyan copies), tearing bands, the OSD label, then black
            _noiseImg = MakeRaw(go.transform, "Noise", new Color(1f, 1f, 1f, 0f));
            _noiseRed = MakeRaw(go.transform, "NoiseRed", new Color(1f, 0.15f, 0.2f, 0f));
            _noiseCyan = MakeRaw(go.transform, "NoiseCyan", new Color(0.1f, 0.9f, 1f, 0f));
            _tears = new RectTransform[6];
            for (int i = 0; i < _tears.Length; i++)
            {
                var band = MakeImage(go.transform, "Tear" + i, new Color(1f, 1f, 1f, 0f));
                var br = band.rectTransform;
                br.anchorMin = new Vector2(0f, 0.5f);
                br.anchorMax = new Vector2(1f, 0.5f);
                br.sizeDelta = new Vector2(0f, 4f + 4f * (i % 3));
                _tears[i] = br;
            }
            _osd = MakeText(go.transform, "◀◀  되감기", 64, new Color(1f, 1f, 1f, 0f), TextAnchor.UpperLeft);
            Place(_osd.rectTransform, new Vector2(60f, -150f), new Vector2(-60f, -260f));
            _black = MakeImage(go.transform, "Black", new Color(0f, 0f, 0f, 0f));
            Stretch(_black.rectTransform);
            SetRewindVisible(false);
        }

        void SetRewindVisible(bool on)
        {
            _noiseImg.gameObject.SetActive(on);
            _noiseRed.gameObject.SetActive(on);
            _noiseCyan.gameObject.SetActive(on);
            foreach (var t in _tears) t.gameObject.SetActive(on);
            _osd.gameObject.SetActive(on);
            _black.gameObject.SetActive(on);
        }

        void FitOverlay()
        {
            const float distance = 0.045f, refH = 1920f;
            float viewH = 2f * distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = _cam.aspect > 0f ? _cam.aspect : 9f / 16f;
            _overlayRect.sizeDelta = new Vector2(refH * aspect, refH);
            float s = viewH / refH;
            _overlayRect.localScale = new Vector3(s, s, s);
            _overlayRect.localPosition = new Vector3(0f, 0f, distance);
            _overlayRect.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (Active && _overlayRect != null) FitOverlay();
        }

        Text MakeText(Transform parent, string text, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        RawImage MakeRaw(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.texture = _noise != null ? _noise : Texture2D.whiteTexture;
            img.color = color;
            img.raycastTarget = false;
            Stretch(img.rectTransform);
            return img;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform r, Vector2 topLeft, Vector2 bottomRight)
        {
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(topLeft.x, bottomRight.y);
            r.offsetMax = new Vector2(bottomRight.x, topLeft.y);
        }

        // ------------------------------------------------------------------ rewind

        /// <summary>Plays the 2.5 s rewind; <paramref name="done"/> runs once at the end (screen black).</summary>
        public void PlayRewind(Action done)
        {
            _rewinding = true;
            _rewindDone = done;
            _covered = false;
            _harin.BlushSuppressed = true; // the rewind flips through faces; no blush flashes
            _harin.SetBlush(0f);
            RewindTime = 0f;
            SetRewindVisible(true);
            if (_history.Count == 0) _history.Add(new HistoryEntry { Stage = _stage, Face = _baseFace, Beat = null });
            EvaluateRewind(0f);
        }

        /// <summary>Jumps the rewind to time t (seconds).</summary>
        public void SeekRewind(float t)
        {
            RewindTime = Mathf.Clamp(t, 0f, RewindDuration);
            EvaluateRewind(RewindTime);
        }

        void StepRewind()
        {
            RewindTime = Mathf.Min(RewindDuration, RewindTime + Time.deltaTime * RewindSpeed);
            EvaluateRewind(RewindTime);
            if (RewindTime >= RewindDuration && _rewindDone != null)
            {
                var done = _rewindDone;
                _rewindDone = null;
                done();
            }
        }

        void EvaluateRewind(float t)
        {
            // (a) the notice card and the subtitle go first
            float ui = 1f - Smooth(t / RewindUiFadeEnd);
            if (_noticeShown)
            {
                _noticeGroup.alpha = ui;
                _notice.SetActive(ui > 0.001f);
            }
            if (_subtitle != null) _subtitle.SetAlpha(ui);

            int n = _history.Count;
            Frame f;
            if (t < RewindFaceHoldEnd)
            {
                // her face as it was, through the fade and the first moment of noise
                var last = _history[n - 1];
                if (last.Stage != _stage) SetStage(last.Stage);
                _baseFace = last.Face;
                f = Evaluate(last.Beat, 10f);
            }
            else
            {
                // (b) back through the lines seen so far, faster and faster
                float u = Mathf.Clamp01((t - RewindFaceHoldEnd) / (RewindNoiseEnd - RewindFaceHoldEnd));
                int index = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(n - 1, 0, u * u)), 0, n - 1);
                var h = _history[index];
                if (h.Stage != _stage) SetStage(h.Stage);
                _baseFace = h.Face;
                // play each remembered beat backwards from its end, so faces flip in reverse
                float local = Mathf.Repeat(t * 7f, 1f);
                f = Evaluate(h.Beat, 1.6f * (1f - local));
            }
            f.Reach = false;
            f.HandCut = false;
            ApplyFrame(f, 0f, true);

            float env = t < RewindNoiseStart ? 0f
                : Smooth((t - RewindNoiseStart) / 0.08f) * (1f - Smooth((t - RewindNoiseEnd) / 0.15f));
            // a little vertical camera judder while the tape runs
            if (env > 0f) _cam.transform.position += Vector3.up * (Mathf.Sin(t * 53f) * 0.006f * env);

            float noise = RewindNoiseStrength * env *
                Mathf.Lerp(0.35f, 0.85f, Smooth((t - RewindNoiseStart) / (RewindNoiseEnd - RewindNoiseStart)));
            float uvY = Mathf.Repeat(t * 9f, 1f);
            var uv = new Rect(0f, uvY, 1f, 1f);
            _noiseImg.uvRect = uv;
            _noiseImg.color = new Color(1f, 1f, 1f, noise * 0.55f);
            _noiseRed.uvRect = uv;
            _noiseRed.color = new Color(1f, 0.15f, 0.2f, noise * 0.35f);
            _noiseRed.rectTransform.anchoredPosition = new Vector2(-16f, 0f);
            _noiseCyan.uvRect = new Rect(0.02f, uvY + 0.01f, 1f, 1f);
            _noiseCyan.color = new Color(0.1f, 0.9f, 1f, noise * 0.35f);
            _noiseCyan.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            float halfH = _overlayRect.sizeDelta.y * 0.5f;
            for (int i = 0; i < _tears.Length; i++)
            {
                float y = Mathf.Repeat(i * 0.173f - t * (0.9f + 0.25f * i), 1f) * 2f - 1f;
                _tears[i].anchoredPosition = new Vector2(Mathf.Sin(t * 31f + i) * 40f, y * halfH);
                _tears[i].GetComponent<Image>().color = new Color(1f, 1f, 1f, noise * 0.18f);
            }
            _osd.color = new Color(1f, 1f, 1f, (Mathf.Repeat(t * 2f, 1f) < 0.65f ? 0.9f : 0.2f) * env);
            // (c) black, and the outfit changes only once it is complete
            float black = Smooth((t - RewindBlackStart) / (RewindBlackFull - RewindBlackStart));
            _black.color = new Color(0f, 0f, 0f, black);
            if (t >= RewindBlackEnd && !_covered && black >= 0.999f)
            {
                _covered = true;
                RewindCovered?.Invoke();
            }
        }
    }
}
