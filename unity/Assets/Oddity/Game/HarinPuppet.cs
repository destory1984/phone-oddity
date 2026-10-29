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
    public class HarinPuppet : MonoBehaviour
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

        public float Glitch { get; private set; }
        public float Fade => _fade;
        public string CurrentFace { get; private set; } = "neutral";

        void Awake()
        {
            _vrm = GetComponent<Vrm10Instance>();
            _basePosition = transform.position;
            if (lowerArms) CaptureRestPose();
            ScheduleBlink();
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
            if (_armPose == null) return;
            foreach (var (t, q) in _armPose) t.localRotation = q;
        }

        void Update()
        {
            if (_vrm == null || _vrm.Runtime == null) return;
            var expression = _vrm.Runtime.Expression;

            // face: exactly one preset at 1, the rest at 0
            foreach (var kv in Faces)
                expression.SetWeight(kv.Value, kv.Value.Equals(_currentFace) ? 1f : 0f);

            expression.SetWeight(ExpressionKey.Blink, BlinkWeight());

            // jitter, applied on top of the base position
            if (Glitch > 0f)
                transform.position = _basePosition + Random.insideUnitSphere * (MaxJitterMeters * Glitch);
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
            if (Faces.TryGetValue(key, out var expr))
            {
                _currentFace = expr;
                CurrentFace = key;
            }
            else
            {
                Debug.LogWarning($"HarinPuppet: unknown face '{face}', using neutral");
                _currentFace = ExpressionKey.Neutral;
                CurrentFace = "neutral";
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
