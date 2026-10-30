using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Small extras for close-up acting: an upper-body lean, eye angles without a target,
    /// and the mouth shapes used by the lip sync (<see cref="HarinVoice"/>).
    /// </summary>
    public partial class HarinPuppet
    {
        const float MaxMouth = 0.8f;

        Transform _spine;
        Transform _chest;
        Quaternion _spineLocal0;
        Quaternion _chestLocal0;
        bool _leanCaptured;
        bool _leanApplied;
        float _lean;
        float _mouthAa, _mouthIh, _mouthOu;
        float _faceWeight = 1f;
        bool _hasMix;
        ExpressionKey _mixKey;
        float _mixWeight;

        /// <summary>Upper body lean in degrees (positive = towards her front).</summary>
        public float LeanDegrees => _lean;
        /// <summary>Current weight of the "aa" mouth shape (0 when silent).</summary>
        public float MouthOpen => _mouthAa;

        public void SetLean(float degrees) => _lean = Mathf.Clamp(degrees, -30f, 30f);

        /// <summary>Mouth shapes 0..0.8 each; all 0 closes the mouth.</summary>
        public void SetMouth(float aa, float ih, float ou)
        {
            _mouthAa = Mathf.Clamp(aa, 0f, MaxMouth);
            _mouthIh = Mathf.Clamp(ih, 0f, MaxMouth);
            _mouthOu = Mathf.Clamp(ou, 0f, MaxMouth);
        }

        /// <summary>Eyes turn by these angles (yaw &gt; 0 to her left, pitch &gt; 0 down) instead of following a target.</summary>
        public void SetEyeAngles(float yaw, float pitch)
        {
            if (_vrm == null) return;
            _vrm.LookAtTarget = null;
            _vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
            if (_vrm.Runtime != null) _vrm.Runtime.LookAt.SetYawPitchManually(yaw, -pitch);
        }

        /// <summary>
        /// Face at <paramref name="weight"/> plus a second preset at <paramref name="secondWeight"/>
        /// (a softer smile: happy 0.5 + relaxed 0.5). <see cref="SetFace"/> goes back to one preset at 1.
        /// </summary>
        public void SetFaceMix(string face, float weight, string second, float secondWeight)
        {
            SetFace(face);
            var key = (second ?? "").Trim().ToLowerInvariant();
            if (!Faces.TryGetValue(key, out var expr)) return;
            _faceWeight = Mathf.Clamp01(weight);
            _hasMix = true;
            _mixKey = expr;
            _mixWeight = Mathf.Clamp01(secondWeight);
        }

        /// <summary>
        /// Holds the lips together by <paramref name="weight"/> (Fcl_MTH_Close 0..1) and lifts the mouth corners by
        /// <paramref name="smile"/> (Fcl_MTH_Fun) on the face set now; both fade as she talks.
        /// </summary>
        public void SetMouthClose(float weight, float smile = 0f)
        {
            _mouthCloseTarget = Mathf.Clamp01(weight);
            _mouthSmileTarget = Mathf.Clamp01(smile);
        }

        // "smile": a closed-mouth smile. Even 0.05 of the happy preset parts the lips enough to show the
        // teeth, so the smile is relaxed alone (happy kept in the mix at 0 so it can be tuned back up).
        public const float SmileRelaxed = 0.55f, SmileHappy = 0f;
        // "flustered": a weaker smile; the head and eyes look away (beat) and the cheeks blush (HarinBlush).
        public const float FlusteredRelaxed = 0.4f, FlusteredHappy = 0f;
        // "soft": the face between lines in the phone, a faint closed-mouth smile. On the summer-B / v58 face
        // 0.2 read as no expression at all, 0.3 with the lips shut still read as blank.
        public const float SoftRelaxed = 0.35f;

        // The v58 face rests with the lips a little apart, which looks absent between lines. The closed-mouth
        // faces add VRoid's Fcl_MTH_Close morph (not bound to any VRM expression, so the runtime leaves it alone);
        // it fades out as the lip sync opens the mouth.
        // "soft" also lifts the mouth corners with the mouth-only part of the smile (Fcl_MTH_Fun), so it reads
        // as a smile without the eyes squinting the way more of the relaxed preset would.
        public const string MouthCloseShape = "Fcl_MTH_Close", MouthSmileShape = "Fcl_MTH_Fun";
        public const float SoftMouthClose = 0.3f, SmileMouthClose = 0.35f, SoftMouthSmile = 0.35f;
        // "surprised": the VRM preset at 1.0 gapes; 0.45 with the lips drawn together reads as a small "huh?"
        public const float SurprisedWeight = 0.45f, SurprisedMouthClose = 0.3f;
        const float MouthCloseFadeAt = 0.15f; // lip-sync weight at which the extra morphs are gone

        SkinnedMeshRenderer _mouthRenderer;
        int _closeIndex = -2, _smileIndex = -1; // -2 not looked up yet, -1 the model has no such morph
        float _mouthCloseTarget, _mouthSmileTarget;

        /// <summary>Weight 0..1 of <see cref="MouthCloseShape"/> applied this frame.</summary>
        public float MouthCloseWeight { get; private set; }
        /// <summary>Weight 0..1 of <see cref="MouthSmileShape"/> added this frame.</summary>
        public float MouthSmileWeight { get; private set; }
        public const string IdleFace = "soft";

        static readonly HashSet<string> LaughBeats = new HashSet<string> { "laughs", "teases", "soft_laugh" };

        /// <summary>
        /// The open-mouthed "happy" is kept for laughing beats (laughs, teases, soft_laugh); any other
        /// "happy" line shows the closed-mouth "smile".
        /// </summary>
        public static string FaceForBeat(string face, string beat)
        {
            var key = (face ?? "").Trim().ToLowerInvariant();
            if (key == "happy" && (beat == null || !LaughBeats.Contains(beat))) return "smile";
            return key;
        }

        /// <summary>Composite faces made of two presets; false when the name is not one.</summary>
        bool TrySetCompositeFace(string key)
        {
            if (key == "surprised")
            {
                // eyes wide, mouth only a little parted: the full preset opens the mouth into a big round O
                _eyeJoy = _mouthJoy = _browJoy = 0f;
                _mouthCloseTarget = SurprisedMouthClose;
                _mouthSmileTarget = 0f;
                _currentFace = ExpressionKey.Surprised;
                CurrentFace = key;
                _faceWeight = SurprisedWeight;
                _hasMix = false;
                return true;
            }
            float relaxed, happy;
            if (key == "smile") { relaxed = SmileRelaxed; happy = SmileHappy; }
            else if (key == "flustered") { relaxed = FlusteredRelaxed; happy = FlusteredHappy; }
            else if (key == IdleFace) { relaxed = SoftRelaxed; happy = 0f; }
            else return false;
            _eyeJoy = _mouthJoy = _browJoy = 0f;
            _mouthCloseTarget = key == IdleFace ? SoftMouthClose : SmileMouthClose;
            _mouthSmileTarget = key == IdleFace ? SoftMouthSmile : 0f;
            _currentFace = ExpressionKey.Relaxed;
            CurrentFace = key;
            _faceWeight = relaxed;
            _hasMix = true;
            _mixKey = ExpressionKey.Happy;
            _mixWeight = happy;
            return true;
        }

        /// <summary>Current weight of a preset or mouth shape on the VRM (0 before the runtime starts).</summary>
        public float ExpressionWeight(ExpressionKey key)
        {
            if (_vrm == null || _vrm.Runtime == null) return 0f;
            return _vrm.Runtime.Expression.GetWeight(key);
        }

        void ClearFaceMix()
        {
            _faceWeight = 1f;
            _hasMix = false;
            _mouthCloseTarget = 0f;
            _mouthSmileTarget = 0f;
            _eyeJoy = _mouthJoy = _browJoy = 0f;
        }

        // VRoid's part morphs of the joy face. The happy preset drives Fcl_ALL_Joy (eyes, mouth and brows together,
        // so any amount of it also shuts the eyes); these let a laugh take the mouth and brows without the eyes.
        // None of them is bound to a VRM expression, so the runtime leaves them alone.
        public const string EyeJoyShape = "Fcl_EYE_Joy", MouthJoyShape = "Fcl_MTH_Joy", BrowJoyShape = "Fcl_BRW_Joy";
        int _eyeJoyIndex = -1, _mouthJoyIndex = -1, _browJoyIndex = -1;
        float _eyeJoy, _mouthJoy, _browJoy;

        /// <summary>
        /// Joy part morphs (0..1) on the face set now: eyes (Fcl_EYE_Joy), mouth (Fcl_MTH_Joy, fades as she talks)
        /// and brows (Fcl_BRW_Joy). Any <see cref="SetFace"/> puts them back to 0.
        /// </summary>
        public void SetJoyParts(float eyes, float mouth, float brows)
        {
            _eyeJoy = Mathf.Clamp01(eyes);
            _mouthJoy = Mathf.Clamp01(mouth);
            _browJoy = Mathf.Clamp01(brows);
        }

        /// <summary>Weight 0..1 of <see cref="MouthJoyShape"/> applied this frame.</summary>
        public float MouthJoyWeight { get; private set; }

        /// <summary>
        /// How much the smile presets and morphs shut her eyes this frame: happy (Fcl_ALL_Joy) + relaxed (Fcl_ALL_Fun)
        /// + Fcl_EYE_Joy. Blinks and poke squints come and go and are not counted. 0 = the resting eye.
        /// </summary>
        public float SmileEyeClosure =>
            ExpressionWeight(ExpressionKey.Happy) + ExpressionWeight(ExpressionKey.Relaxed) + (_eyeJoyIndex >= 0 ? _eyeJoy : 0f);

        // The mouth inside (teeth, tongue) is its own MToon material; a laugh shows it brightly lit.
        const string MouthInsideMaterial = "FaceMouth";
        readonly List<(Material m, Color color, Color shade)> _mouthInside = new List<(Material, Color, Color)>();
        bool _mouthInsideCaptured;

        /// <summary>Multiplies the mouth-inside material's lit and shade colours by <paramref name="brightness"/> (1 = as imported).</summary>
        public void SetMouthInsideBrightness(float brightness)
        {
            if (!_mouthInsideCaptured)
            {
                _mouthInsideCaptured = true;
                if (_mats == null) CaptureMaterials();
                foreach (var s in _mats)
                {
                    var m = s.Material;
                    if (m == null || !m.name.Contains(MouthInsideMaterial)) continue;
                    var shade = m.HasProperty("_ShadeColor") ? m.GetColor("_ShadeColor") : Color.black;
                    _mouthInside.Add((m, m.GetColor("_Color"), shade));
                }
            }
            float k = Mathf.Clamp01(brightness);
            MouthInsideBrightness = k;
            foreach (var (m, color, shade) in _mouthInside)
            {
                if (m == null) continue;
                var c = m.GetColor("_Color"); // keep the alpha SetFade may have changed
                m.SetColor("_Color", new Color(color.r * k, color.g * k, color.b * k, c.a));
                if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(shade.r * k, shade.g * k, shade.b * k, shade.a));
            }
        }

        /// <summary>The last <see cref="SetMouthInsideBrightness"/> value (<see cref="MouthInsideDefault"/> before any call).</summary>
        public float MouthInsideBrightness { get; private set; } = MouthInsideDefault;

        /// <summary>Mouth inside, every scene: 15% darker than imported (the prologue goes to 20%).</summary>
        public const float MouthInsideDefault = 0.85f;

        /// <summary>First mouth-inside material: (as imported, now) lit colour; false when the model has none.</summary>
        public bool TryGetMouthInsideColors(out Color imported, out Color now)
        {
            imported = now = Color.black;
            if (!HasMouthInsideMaterial) return false;
            var e = _mouthInside[0];
            imported = e.color;
            now = e.m.GetColor("_Color");
            return true;
        }
        /// <summary>True when the model has a separate mouth-inside material.</summary>
        public bool HasMouthInsideMaterial
        {
            get { if (!_mouthInsideCaptured) SetMouthInsideBrightness(MouthInsideBrightness); return _mouthInside.Count > 0; }
        }

        float FaceWeight(ExpressionKey key)
        {
            if (key.Equals(_currentFace)) return _faceWeight;
            if (_hasMix && key.Equals(_mixKey)) return _mixWeight;
            return 0f;
        }

        void ApplyMouth()
        {
            var expression = _vrm.Runtime.Expression;
            expression.SetWeight(ExpressionKey.Aa, _mouthAa);
            expression.SetWeight(ExpressionKey.Ih, _mouthIh);
            expression.SetWeight(ExpressionKey.Ou, _mouthOu);
            ApplyMouthClose();
        }

        void ApplyMouthClose()
        {
            if (_closeIndex == -2)
            {
                _closeIndex = -1;
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.sharedMesh == null) continue;
                    int i = r.sharedMesh.GetBlendShapeIndex(MouthCloseShape);
                    if (i < 0) continue;
                    _mouthRenderer = r;
                    _closeIndex = i;
                    _smileIndex = r.sharedMesh.GetBlendShapeIndex(MouthSmileShape);
                    _eyeJoyIndex = r.sharedMesh.GetBlendShapeIndex(EyeJoyShape);
                    _mouthJoyIndex = r.sharedMesh.GetBlendShapeIndex(MouthJoyShape);
                    _browJoyIndex = r.sharedMesh.GetBlendShapeIndex(BrowJoyShape);
                    break;
                }
            }
            float talk = Mathf.Max(_mouthAa, Mathf.Max(_mouthIh, _mouthOu));
            float k = 1f - Mathf.Clamp01(talk / MouthCloseFadeAt);
            MouthCloseWeight = (Startled ? Mathf.Max(_mouthCloseTarget, SurprisedMouthClose) : _mouthCloseTarget) * k;
            MouthSmileWeight = _mouthSmileTarget * k;
            if (_closeIndex >= 0) _mouthRenderer.SetBlendShapeWeight(_closeIndex, MouthCloseWeight * 100f);
            // Fcl_MTH_Fun is not bound to an expression either; the relaxed preset drives Fcl_ALL_Fun, a separate morph
            if (_smileIndex >= 0) _mouthRenderer.SetBlendShapeWeight(_smileIndex, MouthSmileWeight * 100f);
            MouthJoyWeight = _mouthJoyIndex >= 0 ? _mouthJoy * k : 0f;
            if (_eyeJoyIndex >= 0) _mouthRenderer.SetBlendShapeWeight(_eyeJoyIndex, _eyeJoy * 100f);
            if (_mouthJoyIndex >= 0) _mouthRenderer.SetBlendShapeWeight(_mouthJoyIndex, MouthJoyWeight * 100f);
            if (_browJoyIndex >= 0) _mouthRenderer.SetBlendShapeWeight(_browJoyIndex, _browJoy * 100f);
        }

        void CaptureLeanBones()
        {
            _leanCaptured = true;
            var animator = GetComponent<Animator>();
            if (animator == null) return;
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (_spine != null) _spineLocal0 = _spine.localRotation;
            if (_chest != null) _chestLocal0 = _chest.localRotation;
        }

        // Called from LateUpdate after the body pose, before the head override.
        void ApplyLean()
        {
            if (!_leanCaptured) CaptureLeanBones();
            if (_spine == null) return;
            if (_pose == Pose.None && _leanApplied)
            {
                // nothing else resets these bones while standing
                _spine.localRotation = _spineLocal0;
                if (_chest != null) _chest.localRotation = _chestLocal0;
                _leanApplied = false;
            }
            if (Mathf.Abs(_lean) < 0.01f) return;
            var axis = transform.right;
            float share = _chest != null ? 0.5f : 1f;
            _spine.rotation = Quaternion.AngleAxis(_lean * share, axis) * _spine.rotation;
            if (_chest != null) _chest.rotation = Quaternion.AngleAxis(_lean * share, axis) * _chest.rotation;
            _leanApplied = true;
        }
    }
}
