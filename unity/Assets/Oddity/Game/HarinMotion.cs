using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Oddity.Game
{
    /// <summary>
    /// A Mixamo motion (Humanoid clip) on her upper body only (task 51). A manual PlayableGraph with an AvatarMask is
    /// evaluated in LateUpdate, right after the rest pose is written, and the result is blended with that rest pose:
    /// 0.4 s in, 0.4 s out, so when the clip ends the arms are back where they were. Spine and above, arms, hands and
    /// fingers are taken from the clip; the hips, legs and the model root never are (the window shows her from the chest up).
    /// Breath, blink, face and the glance run on top as before. The clip is loaded from Resources/Motion/NAME; the files are
    /// not in the repository (Assets/Oddity/DevMotion is git-ignored), so a missing file is a motion that does not play.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float MotionBlendSeconds = 0.4f;
        public const string MotionResourceFolder = "Motion/";

        /// <summary>True: the head and neck come from the clip. False: the conti's head values keep the head.</summary>
        public static bool MotionHeadFromClip = false;

        static readonly HumanBodyBones[] MotionTorso =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        };

        static readonly HumanBodyBones[] MotionNeckHead = { HumanBodyBones.Neck, HumanBodyBones.Head };

        /// <summary>The bones the motion may move (spine and above, arms, hands, fingers; the head only when <see cref="MotionHeadFromClip"/>).</summary>
        public static IEnumerable<HumanBodyBones> MotionUpperBones()
        {
            foreach (var b in MotionTorso) yield return b;
            if (MotionHeadFromClip) foreach (var b in MotionNeckHead) yield return b;
            for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++) yield return b;
        }

        PlayableGraph _mGraph;
        AnimationClipPlayable _mPlayable;
        AvatarMask _mMask;
        string _mName;
        float _mTime, _mLength;
        bool _mOn;
        Transform[] _mAll;                 // every humanoid bone transform, for the snapshot
        Quaternion[] _mRot;                // the rotations just before this frame's evaluation
        Quaternion[] _mBase;               // the rotations when the motion started: nothing else resets the lower arm, hand or spine
        Vector3[] _mPos;
        bool[] _mUpper;

        /// <summary>The motion playing now (null when none).</summary>
        public string MotionName => _mOn ? _mName : null;
        public bool MotionPlaying => _mOn;
        /// <summary>0..1 share of the clip's pose that is on the bones now (the 0.4 s blends at both ends).</summary>
        public float MotionWeight { get; private set; }
        public float MotionLength => _mLength;
        /// <summary>Seconds into the clip.</summary>
        public float MotionTime => _mTime;

        /// <summary>The clip of a motion file, or null when the file is not there.</summary>
        public static AnimationClip LoadMotionClip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in Resources.LoadAll<AnimationClip>(MotionResourceFolder + name))
                if (c != null && !c.name.StartsWith("__preview__")) return c;
            return null;
        }

        /// <summary>Starts a motion from its first frame. False when the file is missing or the model has no humanoid avatar.</summary>
        public bool PlayMotion(string name)
        {
            var animator = GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return false;
            var clip = LoadMotionClip(name);
            if (clip == null) return false;
            StopMotion();
            EnsureMotionBones(animator);
            animator.applyRootMotion = false;
            _mMask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                _mMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                    || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            }
            _mGraph = PlayableGraph.Create("HarinMotion");
            _mGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_mGraph, "motion", animator);
            var mixer = AnimationLayerMixerPlayable.Create(_mGraph, 1);
            _mPlayable = AnimationClipPlayable.Create(_mGraph, clip);
            _mPlayable.SetApplyFootIK(false);
            _mGraph.Connect(_mPlayable, 0, mixer, 0);
            mixer.SetInputWeight(0, 1f);
            mixer.SetLayerMaskFromAvatarMask(0, _mMask);
            output.SetSourcePlayable(mixer);
            for (int i = 0; i < _mAll.Length; i++) _mBase[i] = _mAll[i].localRotation;
            _mName = name;
            _mLength = clip.length;
            _mTime = 0f;
            _mOn = true;
            MotionWeight = 0f;
            return true;
        }

        /// <summary>Drops the motion at once; the rest pose is written again in the same LateUpdate.</summary>
        public void StopMotion()
        {
            if (_mGraph.IsValid()) _mGraph.Destroy();
            if (_mOn) for (int i = 0; i < _mAll.Length; i++) if (_mUpper[i]) _mAll[i].localRotation = _mBase[i];
            if (_mMask != null) Destroy(_mMask);
            _mMask = null;
            _mOn = false;
            MotionWeight = 0f;
        }

        void EnsureMotionBones(Animator animator)
        {
            var upper = new HashSet<HumanBodyBones>(MotionUpperBones());
            var list = new List<Transform>();
            var flags = new List<bool>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = animator.GetBoneTransform(b);
                if (t == null) continue;
                list.Add(t);
                flags.Add(upper.Contains(b));
            }
            _mAll = list.ToArray();
            _mUpper = flags.ToArray();
            _mRot = new Quaternion[_mAll.Length];
            _mBase = new Quaternion[_mAll.Length];
            _mPos = new Vector3[_mAll.Length];
        }

        static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        // LateUpdate, after the rest pose (so the snapshot is the pose she would have), before lean, breath and the head override.
        void ApplyMotion()
        {
            if (!_mOn) return;
            if (!_mGraph.IsValid()) { _mOn = false; return; }
            _mTime += Time.deltaTime;
            if (_mTime >= _mLength) { StopMotion(); return; }
            for (int i = 0; i < _mAll.Length; i++) { _mRot[i] = _mAll[i].localRotation; _mPos[i] = _mAll[i].localPosition; }
            var rootPos = transform.position;
            var rootRot = transform.rotation;

            _mPlayable.SetTime(_mTime);
            _mGraph.Evaluate(0f);

            float w = Mathf.Min(Smooth(_mTime / MotionBlendSeconds), Smooth((_mLength - _mTime) / MotionBlendSeconds));
            MotionWeight = w;
            for (int i = 0; i < _mAll.Length; i++)
            {
                if (_mUpper[i]) _mAll[i].localRotation = Quaternion.Slerp(_mBase[i], _mAll[i].localRotation, w);
                else _mAll[i].localRotation = _mRot[i];
                _mAll[i].localPosition = _mPos[i]; // only the hips carry a position in a humanoid clip, and they stay
            }
            transform.SetPositionAndRotation(rootPos, rootRot);
        }
    }
}
