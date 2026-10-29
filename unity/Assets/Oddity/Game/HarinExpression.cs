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

        void ClearFaceMix()
        {
            _faceWeight = 1f;
            _hasMix = false;
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
