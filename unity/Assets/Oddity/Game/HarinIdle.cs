using UniVRM10;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Idle life for the phone stage: a slow breath (chest and shoulders rise by well under 1%) and a small
    /// glance away every 2 to 5 s that comes back. Only runs while <see cref="SetIdle"/> is on.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float BreathPeriod = 4f;          // seconds per breath
        public const float BreathChestScale = 0.004f;  // chest grows by 0.4% at the top of a breath
        public const float BreathShoulderLift = 0.002f; // shoulders rise 2 mm (model units)
        public const float GlanceMinGap = 2f, GlanceMaxGap = 5f;
        public const float GlanceHold = 0.8f, GlanceEase = 0.2f;
        public const float GlanceYawMax = 6f, GlancePitchMax = 2.5f;

        bool _idle;
        bool _idleCaptured;
        Transform _breathChest, _shoulderL, _shoulderR;
        Vector3 _breathChestScale0, _shoulderL0, _shoulderR0;
        bool _breathApplied;
        float _nextGlanceAt = -1f;
        float _glanceStart = -1f;
        float _glanceYaw, _glancePitch;

        public bool Idle => _idle;
        /// <summary>0..1, where the breath is now (0 = out, 1 = in).</summary>
        public float Breath { get; private set; }
        /// <summary>Current idle glance (yaw, pitch) in degrees; zero between glances.</summary>
        public Vector2 Glance { get; private set; }

        /// <summary>Breathing and idle glances on (phone stage) or off (cut scenes, the room, the prologue).</summary>
        public void SetIdle(bool on)
        {
            if (_idle == on) return;
            _idle = on;
            _glanceStart = -1f;
            _nextGlanceAt = Time.time + Random.Range(GlanceMinGap, GlanceMaxGap);
            if (on) SetEyeTarget(null);
            else
            {
                Glance = Vector2.zero;
                Breath = 0f;
                RestoreBreath();
            }
        }

        void CaptureIdleBones()
        {
            _idleCaptured = true;
            var animator = GetComponent<Animator>();
            if (animator == null) return;
            _breathChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (_breathChest == null) _breathChest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _shoulderL = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _shoulderR = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            if (_breathChest != null) _breathChestScale0 = _breathChest.localScale;
            if (_shoulderL != null) _shoulderL0 = _shoulderL.localPosition;
            if (_shoulderR != null) _shoulderR0 = _shoulderR.localPosition;
        }

        void RestoreBreath()
        {
            if (!_breathApplied) return;
            if (_breathChest != null) _breathChest.localScale = _breathChestScale0;
            if (_shoulderL != null) _shoulderL.localPosition = _shoulderL0;
            if (_shoulderR != null) _shoulderR.localPosition = _shoulderR0;
            _breathApplied = false;
        }

        // LateUpdate, after the body pose: breathing only while standing in the phone.
        void ApplyIdleBreath()
        {
            if (!_idleCaptured) CaptureIdleBones();
            if (!_idle || _pose != Pose.None)
            {
                RestoreBreath();
                return;
            }
            Breath = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Time.time / BreathPeriod);
            if (_breathChest != null) _breathChest.localScale = _breathChestScale0 * (1f + BreathChestScale * Breath);
            // shoulders move up in her model space; convert to each parent's local space
            var up = transform.up * (BreathShoulderLift * Breath * transform.lossyScale.y);
            if (_shoulderL != null) _shoulderL.localPosition = _shoulderL0 + _shoulderL.parent.InverseTransformVector(up);
            if (_shoulderR != null) _shoulderR.localPosition = _shoulderR0 + _shoulderR.parent.InverseTransformVector(up);
            _breathApplied = true;
        }

        // Update: a small glance away and back, only when nothing else steers the eyes.
        void UpdateIdleGaze()
        {
            if (!_idle || _vrm == null || _vrm.Runtime == null || _pokeStart >= 0f) return;
            if (_vrm.LookAtTarget != null || _vrm.LookAtTargetType != VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue) return;
            float now = Time.time;
            if (_glanceStart < 0f)
            {
                if (now < _nextGlanceAt) return;
                _glanceStart = now;
                _glanceYaw = Random.Range(0.5f, 1f) * GlanceYawMax * (Random.value < 0.5f ? -1f : 1f);
                _glancePitch = Random.Range(-1f, 1f) * GlancePitchMax;
            }
            float t = now - _glanceStart;
            float total = GlanceHold + 2f * GlanceEase;
            float k;
            if (t >= total)
            {
                _glanceStart = -1f;
                _nextGlanceAt = now + Random.Range(GlanceMinGap, GlanceMaxGap);
                k = 0f;
            }
            else k = Mathf.Clamp01(Mathf.Min(t, total - t) / GlanceEase);
            k = k * k * (3f - 2f * k);
            Glance = new Vector2(_glanceYaw * k, _glancePitch * k);
            _vrm.Runtime.LookAt.SetYawPitchManually(Glance.x, -Glance.y);
        }
    }
}
