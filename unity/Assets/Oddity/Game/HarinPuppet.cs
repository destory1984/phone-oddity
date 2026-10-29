using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VRM10.MToon10;

namespace Oddity.Game
{
    /// <summary>
    /// Drives Harin's VRM 1.0 model: expression, blink, glitch jitter, fade, and placement.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before Vrm10Instance's LateUpdate so spring bones see the pose
    [RequireComponent(typeof(Vrm10Instance))]
    public partial class HarinPuppet : MonoBehaviour
    {
        const float BlinkMinInterval = 3f;
        const float BlinkMaxInterval = 6f;
        const float BlinkDuration = 0.15f;
        const float MaxJitterMeters = 0.01f; // 1 cm at glitch = 1

        static readonly Dictionary<string, ExpressionKey> Faces = new Dictionary<string, ExpressionKey>
        {
            { "neutral", ExpressionKey.Neutral },
            { "happy", ExpressionKey.Happy },
            { "sad", ExpressionKey.Sad },
            { "angry", ExpressionKey.Angry },
            { "surprised", ExpressionKey.Surprised },
            { "relaxed", ExpressionKey.Relaxed },
        };

        // Original MToon10 state, saved the first time a material is made transparent.
        class MatState
        {
            public Material Material;
            public int AlphaMode;
            public int ZWrite;
            public int QueueOffset;
            public float BaseAlpha;
        }

        [SerializeField] bool lowerArms = true;

        Vrm10Instance _vrm;
        ExpressionKey _currentFace = ExpressionKey.Neutral;
        float _nextBlinkAt;
        float _blinkStartedAt = -1f;
        Vector3 _basePosition;
        float _fade = 1f;
        List<MatState> _mats;
        List<(Transform, Quaternion)> _armPose;

        // T-pose data (model space) for the right-arm and head overrides used by the emergence.
        Transform _rightUpper;
        Quaternion _rightUpperRel;
        Vector3 _rightArmDirT;
        Transform _head;
        Quaternion _headRel;
        bool _reach;
        Vector3 _reachDir;
        Vector3 _reachPalm;
        float _headYaw;
        float _headPitch;

        public float Glitch { get; private set; }
        public float Fade => _fade;
        public string CurrentFace { get; private set; } = "neutral";
        /// <summary>Right shoulder (upper arm bone) in model space, measured in the T-pose.</summary>
        public Vector3 RightShoulderLocal { get; private set; }
        /// <summary>Shoulder to finger tips in model units (approximate).</summary>
        public float RightArmLength { get; private set; } = 0.65f;

        void Awake()
        {
            _vrm = GetComponent<Vrm10Instance>();
            _basePosition = transform.position;
            DampSkirt();
            CaptureOverrideBones();
            CapturePoseBones();
            CaptureLeanBones();
            CaptureIdleBones();
            if (lowerArms) CaptureRestPose();
            ScheduleBlink();
        }

        /// <summary>
        /// Drag on the skirt spring joints. VRoid exported the summer-B pleated skirt with 0.05, so it kept
        /// swinging for about 5 s after any move (and 1 cm at the hem 1.5 s after loading); 0.3 settles it
        /// in about a second without making it stiff. Hair keeps its own values.
        /// </summary>
        public const float SkirtDrag = 0.3f;

        // Runs in Awake, before the VRM runtime builds its spring bones from these components.
        void DampSkirt()
        {
            foreach (var j in GetComponentsInChildren<VRM10SpringBoneJoint>(true))
                if (j.name.Contains("Skirt") && j.m_dragForce < SkirtDrag) j.m_dragForce = SkirtDrag;
        }

        // The VRM is exported in a T-pose; swing both arms down so she stands naturally.
        // The Animator on the model rewrites the bones every frame, so the target local
        // rotations are computed once (in the T-pose) and re-applied in LateUpdate.
        void CaptureRestPose()
        {
            var animator = GetComponent<Animator>();
            if (animator == null) return;
            _armPose = new List<(Transform, Quaternion)>();
            AddArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            AddArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
        }

        void CaptureOverrideBones()
        {
            var animator = GetComponent<Animator>();
            if (animator == null) return;
            var root = transform;
            _rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (_rightUpper != null && lower != null)
            {
                _rightUpperRel = Quaternion.Inverse(root.rotation) * _rightUpper.rotation;
                _rightArmDirT = root.InverseTransformDirection(lower.position - _rightUpper.position).normalized;
                RightShoulderLocal = root.InverseTransformPoint(_rightUpper.position);
                if (hand != null)
                    RightArmLength = Vector3.Distance(RightShoulderLocal, root.InverseTransformPoint(hand.position)) + 0.17f;
            }
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (_head != null) _headRel = Quaternion.Inverse(root.rotation) * _head.rotation;
        }

        /// <summary>
        /// Points the right arm straight along <paramref name="modelDir"/> (model space) with the palm facing
        /// <paramref name="palmNormal"/>. Null returns the arm to its rest pose.
        /// </summary>
        public void SetRightArmReach(Vector3? modelDir, Vector3 palmNormal = default)
        {
            _reach = modelDir.HasValue && _rightUpper != null;
            if (!_reach) return;
            _reachDir = modelDir.Value.normalized;
            _reachPalm = palmNormal == Vector3.zero ? Vector3.forward : palmNormal.normalized;
        }

        /// <summary>Turns the head in model space: yaw &gt; 0 looks to her left, pitch &gt; 0 looks down.</summary>
        public void SetHeadTurn(float yawDegrees, float pitchDegrees)
        {
            _headYaw = yawDegrees;
            _headPitch = pitchDegrees;
        }

        void AddArm(Animator animator, HumanBodyBones upperBone, HumanBodyBones lowerBone)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            if (upper == null || lower == null) return;
            var current = (lower.position - upper.position).normalized;
            var outward = new Vector3(Mathf.Sign(current.x) * 0.18f, -1f, 0f).normalized;
            var rot = Quaternion.FromToRotation(current, outward) * upper.rotation;
            _armPose.Add((upper, Quaternion.Inverse(upper.parent.rotation) * rot));
        }

        void LateUpdate()
        {
            if (_pose != Pose.None || _poseDirty)
            {
                ResetPoseBones();
                _poseDirty = _pose != Pose.None;
            }
            if (_pose != Pose.None) ApplyPoseNow();
            else if (_armPose != null)
                foreach (var (t, q) in _armPose) t.localRotation = q;

            ApplyLean();
            ApplyIdleBreath();
            var root = transform.rotation;
            if (_reach)
            {
                // the VRM T-pose has the palms facing down
                var from = Quaternion.LookRotation(_rightArmDirT, Vector3.down);
                var to = Quaternion.LookRotation(_reachDir, _reachPalm);
                _rightUpper.rotation = root * (to * Quaternion.Inverse(from)) * _rightUpperRel;
            }
            var poke = PokeHeadOffset;
            if (_head != null && (_headYaw != 0f || _headPitch != 0f || poke != Vector3.zero))
                _head.rotation = root * Quaternion.Euler(_headPitch + poke.y, _headYaw + poke.x, poke.z) * _headRel;
        }

        void Update()
        {
            if (_vrm == null || _vrm.Runtime == null) return;
            var expression = _vrm.Runtime.Expression;

            // face: one preset (plus an optional second one for mixes), the rest at 0
            foreach (var kv in Faces)
                expression.SetWeight(kv.Value, FaceWeight(kv.Value));

            UpdatePoke();
            expression.SetWeight(ExpressionKey.Blink, BlinkWeight());
            ApplyMouth();
            UpdateIdleGaze();

            // jitter, applied on top of the base position
            if (Glitch > 0f)
                transform.position = _basePosition + Random.insideUnitSphere * (MaxJitterMeters * Glitch * transform.localScale.x);
            else
                transform.position = _basePosition;
        }

        float BlinkWeight()
        {
            if (_blinkStartedAt < 0f)
            {
                if (Time.time < _nextBlinkAt) return 0f;
                _blinkStartedAt = Time.time;
            }
            var t = (Time.time - _blinkStartedAt) / BlinkDuration; // 0..1 close then open
            if (t >= 1f)
            {
                _blinkStartedAt = -1f;
                ScheduleBlink();
                return 0f;
            }
            return 1f - Mathf.Abs(2f * t - 1f);
        }

        void ScheduleBlink() => _nextBlinkAt = Time.time + Random.Range(BlinkMinInterval, BlinkMaxInterval);

        public void SetFace(string face)
        {
            var key = (face ?? "").Trim().ToLowerInvariant();
            if (TrySetCompositeFace(key)) return;
            if (Faces.TryGetValue(key, out var expr))
            {
                _currentFace = expr;
                CurrentFace = key;
                ClearFaceMix();
            }
            else
            {
                Debug.LogWarning($"HarinPuppet: unknown face '{face}', using neutral");
                _currentFace = ExpressionKey.Neutral;
                CurrentFace = "neutral";
                ClearFaceMix();
            }
        }

        public void SetGlitch(float amount)
        {
            Glitch = Mathf.Clamp01(amount);
        }

        /// <summary>
        /// alpha 1 = original opaque toon look. Below 1, MToon10 materials are switched to
        /// transparent mode (with Z write, so the layers of the body do not show through
        /// each other) and the base colour alpha is multiplied by alpha.
        /// </summary>
        public void SetFade(float alpha)
        {
            _fade = Mathf.Clamp01(alpha);
            if (_mats == null) CaptureMaterials();
            foreach (var s in _mats)
            {
                var m = s.Material;
                if (m == null) continue;
                if (_fade >= 1f)
                {
                    m.SetInt(MToon10Prop.AlphaMode, s.AlphaMode);
                    m.SetInt(MToon10Prop.TransparentWithZWrite, s.ZWrite);
                    m.SetInt(MToon10Prop.RenderQueueOffsetNumber, s.QueueOffset);
                    SetBaseAlpha(m, s.BaseAlpha);
                }
                else
                {
                    m.SetInt(MToon10Prop.AlphaMode, (int)MToon10AlphaMode.Transparent);
                    m.SetInt(MToon10Prop.TransparentWithZWrite, (int)MToon10TransparentWithZWriteMode.On);
                    SetBaseAlpha(m, s.BaseAlpha * _fade);
                }
                new MToonValidator(m).Validate();
            }
        }

        static void SetBaseAlpha(Material m, float a)
        {
            var c = m.GetColor("_Color");
            c.a = a;
            m.SetColor("_Color", c);
        }

        void CaptureMaterials()
        {
            _mats = new List<MatState>();
            var seen = new HashSet<Material>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                // .materials instantiates per-renderer copies, so the imported asset is untouched.
                foreach (var m in r.materials)
                {
                    if (m == null || !m.HasProperty("_AlphaMode") || !seen.Add(m)) continue;
                    _mats.Add(new MatState
                    {
                        Material = m,
                        AlphaMode = m.GetInt(MToon10Prop.AlphaMode),
                        ZWrite = m.GetInt(MToon10Prop.TransparentWithZWrite),
                        QueueOffset = m.GetInt(MToon10Prop.RenderQueueOffsetNumber),
                        BaseAlpha = m.GetColor("_Color").a,
                    });
                }
            }
        }

        public void MoveTo(Transform anchor)
        {
            if (anchor == null) return;
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            _basePosition = anchor.position;
        }
    }
}
