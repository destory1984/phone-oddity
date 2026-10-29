using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VRM10.MToon10;

namespace Oddity.Game
{
    /// <summary>Body poses used by the emergence (storyboard cuts 10 to 12).</summary>
    public enum Pose
    {
        None,
        /// <summary>Cut 10: the right arm follows the targets given to <see cref="HarinPuppet.SetReachTargets"/>.</summary>
        Cut10Reach,
        /// <summary>Cut 11: standing, looking down at both hands. t 0..1 walks through the angle ranges.</summary>
        LookAtHands,
        /// <summary>Between cut 11 and 12: knees and hips start to bend. t 0..1.</summary>
        SitDown,
        /// <summary>Cut 12: sitting on the bed edge, hands on the thighs, feet on the floor. t 0..1.</summary>
        Seated,
    }

    public partial class HarinPuppet
    {
        // ------------------------------------------------------------------ pose numbers
        // Every hand-tuned pose value is here. Angles in degrees, lengths in metres (model units).
        // Directions are in model space: +Y up, +Z her front. Her right side is measured from the
        // T-pose (the sign of the right upper arm's x), not assumed.
        static class P
        {
            // cut 11, looking at her hands
            public const float ArmOut0 = 15f, ArmOut1 = 25f;       // upper arm away from the body
            public const float ArmForward = 20f;                    // upper arm raised to the front
            public const float Elbow0 = 70f, Elbow1 = 95f;          // elbow flexion (0 = straight)
            public const float LeftElbowExtra = 14f;                // left hand 5 to 8 cm higher
            public const float ForearmInward = 0.35f;               // forearms turn in towards the middle
            public const float WristBack = 10f;
            public const float KneeWeighted = 5f, KneeFree = 10f;   // weight on the right foot
            public const float LookNeckDown = 10f, LookHeadDown = 26f;
            public const float LookLean = 3f;

            // 6.0 → 6.5 s, starting to sit
            public const float SitDownKnee = 45f;
            public const float SitDownLean = 12f;

            // cut 12, seated on the bed
            public const float SeatHip0 = 80f, SeatHip1 = 95f;      // thigh from straight down
            public const float SeatKnee0 = 85f, SeatKnee1 = 100f;   // knee flexion
            public const float SeatLean0 = 8f, SeatLean1 = 12f;     // upper body forward
            public const float SeatHeadDown0 = 5f, SeatHeadDown1 = 8f;
            public const float HandOnThigh = 0.55f;                 // wrist at this fraction from hip to knee (knee side, clear of the skirt front)
            public const float HandAboveThigh = 0.02f;              // wrist height above the thigh surface
            public const float HandTipDown = 0.25f;                 // fingers tip down onto the thigh
            public const float ElbowOut = 0.5f;                     // elbow pole: out ...
            public const float ElbowBack = 1f;                      // ... and back

            // cut 10 fingers
            public const float FingerCurlMax = 35f;
        }

        /// <summary>Hip joint to the mattress surface under it (thigh thickness), metres.</summary>
        public const float ThighRadius = 0.08f;

        class Bone
        {
            public Transform T;
            public Quaternion Local;     // T-pose local rotation
            public Vector3 LocalPos;     // T-pose local position
            public Quaternion Rel;       // T-pose rotation in model space
            public Vector3 ModelPos;     // T-pose position in model space
        }

        static readonly HumanBodyBones[] PoseBoneIds =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal,
        };

        static readonly HumanBodyBones[] RightFingers =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        readonly Dictionary<HumanBodyBones, Bone> _bones = new Dictionary<HumanBodyBones, Bone>();
        Pose _pose;
        float _poseT;
        bool _poseDirty;
        float _rightSide = 1f;  // +1 when her right is +X in model space
        Vector3 _reachElbow, _reachWrist, _reachTip, _reachPalmN;
        float _fingerCurl;

        public Pose CurrentPose => _pose;
        /// <summary>Wrist (hand bone) to the middle finger tip, model units.</summary>
        public float HandLength { get; private set; } = 0.17f;
        public float UpperArmLength { get; private set; } = 0.25f;
        public float ForearmLength { get; private set; } = 0.23f;
        /// <summary>Foot bone height above the sole in the T-pose, model units.</summary>
        public float AnkleHeight { get; private set; } = 0.07f;
        /// <summary>Seat contact point (between the hip joints, one thigh radius lower) in model space.</summary>
        public Vector3 SeatContactLocal { get; private set; }

        void CapturePoseBones()
        {
            var animator = GetComponent<Animator>();
            if (animator == null) return;
            var all = new List<HumanBodyBones>(PoseBoneIds);
            all.AddRange(RightFingers);
            foreach (var id in all)
            {
                var t = animator.GetBoneTransform(id);
                if (t == null || _bones.ContainsKey(id)) continue;
                _bones[id] = new Bone
                {
                    T = t,
                    Local = t.localRotation,
                    LocalPos = t.localPosition,
                    Rel = Quaternion.Inverse(transform.rotation) * t.rotation,
                    ModelPos = transform.InverseTransformPoint(t.position),
                };
            }

            if (Has(HumanBodyBones.RightUpperArm)) _rightSide = Mathf.Sign(M(HumanBodyBones.RightUpperArm).x);
            if (Has(HumanBodyBones.RightUpperArm) && Has(HumanBodyBones.RightLowerArm) && Has(HumanBodyBones.RightHand))
            {
                UpperArmLength = Vector3.Distance(M(HumanBodyBones.RightUpperArm), M(HumanBodyBones.RightLowerArm));
                ForearmLength = Vector3.Distance(M(HumanBodyBones.RightLowerArm), M(HumanBodyBones.RightHand));
                var tip = animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                if (tip != null)
                    HandLength = Vector3.Distance(M(HumanBodyBones.RightHand), transform.InverseTransformPoint(tip.position)) + 0.02f;
            }
            if (Has(HumanBodyBones.RightFoot)) AnkleHeight = M(HumanBodyBones.RightFoot).y;
            if (Has(HumanBodyBones.LeftUpperLeg) && Has(HumanBodyBones.RightUpperLeg))
                SeatContactLocal = (M(HumanBodyBones.LeftUpperLeg) + M(HumanBodyBones.RightUpperLeg)) * 0.5f
                    - Vector3.up * ThighRadius;
        }

        bool Has(HumanBodyBones b) => _bones.ContainsKey(b);
        Vector3 M(HumanBodyBones b) => _bones[b].ModelPos;
        Transform T(HumanBodyBones b) => _bones.TryGetValue(b, out var x) ? x.T : null;

        /// <summary>Sets the body pose; t (0..1) walks through the pose's ranges. Pose.None goes back to standing.</summary>
        public void ApplyPose(Pose pose, float t)
        {
            _pose = pose;
            _poseT = Mathf.Clamp01(t);
        }

        /// <summary>World targets for <see cref="Pose.Cut10Reach"/>: elbow, wrist, middle finger tip, and the palm normal.</summary>
        public void SetReachTargets(Vector3 elbow, Vector3 wrist, Vector3 tip, Vector3 palm, float fingerCurl)
        {
            _reachElbow = elbow;
            _reachWrist = wrist;
            _reachTip = tip;
            _reachPalmN = palm.normalized;
            _fingerCurl = Mathf.Clamp01(fingerCurl);
        }

        /// <summary>Eyes follow this transform (VRM look-at); null looks straight ahead.</summary>
        public void SetEyeTarget(Transform target)
        {
            if (_vrm == null) return;
            if (target != null)
            {
                _vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
                _vrm.LookAtTarget = target;
            }
            else
            {
                _vrm.LookAtTarget = null;
                _vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
                if (_vrm.Runtime != null) _vrm.Runtime.LookAt.SetYawPitchManually(0f, 0f);
            }
        }

        /// <summary>World position of a humanoid bone (Vector3.zero when the model has no such bone).</summary>
        public Vector3 BonePosition(HumanBodyBones b)
        {
            var t = T(b);
            return t != null ? t.position : Vector3.zero;
        }

        /// <summary>Sole of the foot in world space (foot bone minus the T-pose ankle height).</summary>
        public Vector3 SolePosition(bool right)
        {
            var foot = T(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot);
            return foot == null ? Vector3.zero : foot.position - Vector3.up * (AnkleHeight * transform.lossyScale.y);
        }

        /// <summary>Seat contact point in world space for the current pose (between the hip joints, minus the thigh radius).</summary>
        public Vector3 SeatContactWorld
        {
            get
            {
                var l = T(HumanBodyBones.LeftUpperLeg);
                var r = T(HumanBodyBones.RightUpperLeg);
                if (l == null || r == null) return transform.position;
                return (l.position + r.position) * 0.5f - Vector3.up * (ThighRadius * transform.lossyScale.y);
            }
        }

        // room look (prologue, cuts 10 to 12, act 2). The summer-B SKIN texture is already pink and the room lights
        // push red far past green (the close-up measured 255/160/152, salmon), so the base and shade take red down
        // and keep green up until she reads as a light neutral apricot. The prologue's 3,500 K lamp needs more of
        // that than the act 2 room, which has the 6,500 K face light. Tuned against the shots, not computed.
        public static readonly Color SkinShade = new Color(0.84f, 0.86f, 0.80f, 1f);
        public static readonly Color SkinBase = new Color(0.94f, 1f, 0.92f, 1f);
        public static readonly Color PrologueSkinShade = new Color(0.70f, 0.78f, 0.74f, 1f);
        public static readonly Color PrologueSkinBase = new Color(0.80f, 0.96f, 0.90f, 1f);
        // MToon shading ramp: width = 2 (1 - toony). VRoid exported face 0.55 (width 0.9) and body 0.675 (0.65),
        // so the jaw over the neck cut off sharply and the two meshes did not match. One value for both,
        // width 1.8 (2x the face, 2.8x the body); the shift keeps the lit share near the face's old value.
        public const float RoomSkinToony = 0.1f, RoomSkinShift = -0.2f;
        public static readonly Color HairTeal = new Color(0x1f / 255f, 0xb5 / 255f, 0xb8 / 255f, 1f);
        public const float HairGlow = 0.04f;

        class LookState { public Material M; public Color Emission, Shade, Base, Matcap; public float Toony, Shift; public bool Skin, Hair; }
        List<LookState> _look;

        /// <summary>
        /// On: MToon emission 0 (the hair glowed), skin shade colour from pink to a warm grey, a skin base that
        /// cancels the lamp's red, a softer shading ramp on the skin and no matcap on it, so the room lights do
        /// not wash her out. <paramref name="prologue"/> picks the skin colours for the prologue's warmer lamp.
        /// Off: the imported values.
        /// </summary>
        public void SetRoomLook(bool on, bool prologue = false)
        {
            if (_mats == null) CaptureMaterials();
            if (_look == null)
            {
                _look = new List<LookState>();
                foreach (var s in _mats)
                {
                    var m = s.Material;
                    if (m == null || !m.HasProperty("_EmissionColor") || !m.HasProperty("_ShadeColor")) continue;
                    _look.Add(new LookState
                    {
                        M = m,
                        Emission = m.GetColor("_EmissionColor"),
                        Shade = m.GetColor("_ShadeColor"),
                        Base = m.GetColor("_Color"),
                        Matcap = m.HasProperty("_MatcapColor") ? m.GetColor("_MatcapColor") : Color.black,
                        Toony = m.GetFloat(MToon10Prop.ShadingToonyFactor),
                        Shift = m.GetFloat(MToon10Prop.ShadingShiftFactor),
                        Skin = m.name.Contains("SKIN"),
                        Hair = m.name.Contains("HAIR"),
                    });
                }
            }
            foreach (var l in _look)
            {
                l.M.SetColor("_EmissionColor", !on ? l.Emission : l.Hair ? HairTeal * HairGlow : Color.black);
                if (!l.Skin) continue;
                l.M.SetColor("_ShadeColor", !on ? l.Shade : prologue ? PrologueSkinShade : SkinShade);
                var c = !on ? l.Base : prologue ? PrologueSkinBase : SkinBase;
                c.a = l.M.GetColor("_Color").a; // keep the fade alpha
                l.M.SetColor("_Color", c);
                l.M.SetFloat(MToon10Prop.ShadingToonyFactor, on ? RoomSkinToony : l.Toony);
                l.M.SetFloat(MToon10Prop.ShadingShiftFactor, on ? RoomSkinShift : l.Shift);
                if (l.M.HasProperty("_MatcapColor")) l.M.SetColor("_MatcapColor", on ? Color.black : l.Matcap);
            }
        }

        void ResetPoseBones()
        {
            foreach (var b in _bones.Values)
            {
                b.T.localRotation = b.Local;
                b.T.localPosition = b.LocalPos;
            }
        }

        // ------------------------------------------------------------------ helpers (world space)

        Vector3 W(Vector3 modelDir) => transform.rotation * modelDir;

        // Absolute rotation relative to the T-pose, given in model space.
        void SetModelRot(HumanBodyBones id, Quaternion modelDelta)
        {
            if (!_bones.TryGetValue(id, out var b)) return;
            b.T.rotation = transform.rotation * modelDelta * b.Rel;
        }

        // Turns the bone (smallest rotation) so its child lies along dir.
        static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            if (bone == null || child == null || dir.sqrMagnitude < 1e-8f) return;
            var cur = child.position - bone.position;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        // Twists the bone about its own axis (towards child) so palmLocal points as close to palm as it can.
        static void Twist(Transform bone, Transform child, Vector3 palmLocal, Vector3 palm, float amount = 1f)
        {
            if (bone == null || child == null) return;
            var axis = (child.position - bone.position).normalized;
            var cur = Vector3.ProjectOnPlane(bone.rotation * palmLocal, axis);
            var want = Vector3.ProjectOnPlane(palm, axis);
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            float angle = Vector3.SignedAngle(cur, want, axis) * amount;
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        // Upper arm along u with the elbow bending towards v, forearm along v, hand along h, palms facing palm.
        void ArmChain(bool right, Vector3 u, Vector3 v, Vector3 h, Vector3 palm)
        {
            var upper = T(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = T(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = T(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var finger = T(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            if (upper == null || lower == null || hand == null) return;

            // the elbow crease faces her front in the T-pose (palms down), so the bend direction
            // is matched to the model's +Z on the upper arm: the elbow then bends as a hinge
            var root = transform.rotation;
            var bones = _bones[right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm];
            var axisL = Quaternion.Inverse(bones.Rel) * (M(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm) - bones.ModelPos);
            var creaseL = Quaternion.Inverse(bones.Rel) * Vector3.forward;
            var bend = Vector3.ProjectOnPlane(v, u);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(root * Vector3.forward, u);
            upper.rotation = Quaternion.LookRotation(u, bend) * Quaternion.Inverse(Quaternion.LookRotation(axisL, creaseL));

            Aim(lower, hand, v);
            var palmLower = Quaternion.Inverse(_bones[right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm].Rel) * Vector3.down;
            // half the forearm roll on the forearm, the rest on the hand, so the elbow does not twist
            Twist(lower, hand, palmLower, palm, 0.5f);
            if (finger != null)
            {
                Aim(hand, finger, h);
                var palmHand = Quaternion.Inverse(_bones[right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand].Rel) * Vector3.down;
                Twist(hand, finger, palmHand, palm);
            }
        }

        // ------------------------------------------------------------------ poses

        void ApplyPoseNow()
        {
            switch (_pose)
            {
                case Pose.Cut10Reach: PoseReach(); break;
                case Pose.LookAtHands: PoseLookAtHands(_poseT, 0f); break;
                case Pose.SitDown: PoseLookAtHands(1f, _poseT); break;
                case Pose.Seated: PoseSeated(_poseT); break;
            }
        }

        void PoseReach()
        {
            var upper = T(HumanBodyBones.RightUpperArm);
            if (upper == null) return;
            var u = _reachElbow - upper.position;
            var v = _reachWrist - _reachElbow;
            var h = _reachTip - _reachWrist;
            ArmChain(true, u, v, h, _reachPalmN);
            CurlFingers(_fingerCurl * P.FingerCurlMax, _reachPalmN);
        }

        void CurlFingers(float degrees, Vector3 palm)
        {
            if (degrees <= 0f) return;
            // each finger bone bends towards the palm side
            for (int i = 0; i < RightFingers.Length; i++)
            {
                var b = T(RightFingers[i]);
                if (b == null) continue;
                Transform next = (i % 3) < 2 ? T(RightFingers[i + 1]) : null;
                var dir = next != null ? next.position - b.position : b.position - b.parent.position;
                if (dir.sqrMagnitude < 1e-8f) continue;
                var target = Vector3.RotateTowards(dir, -palm * dir.magnitude, degrees * Mathf.Deg2Rad, 0f);
                b.rotation = Quaternion.FromToRotation(dir, target) * b.rotation;
            }
        }

        void Lean(float spineDeg, float neckDeg, float headDeg)
        {
            SetModelRot(HumanBodyBones.Spine, Quaternion.Euler(spineDeg * 0.4f, 0f, 0f));
            SetModelRot(HumanBodyBones.Chest, Quaternion.Euler(spineDeg * 0.75f, 0f, 0f));
            SetModelRot(HumanBodyBones.UpperChest, Quaternion.Euler(spineDeg, 0f, 0f));
            SetModelRot(HumanBodyBones.Neck, Quaternion.Euler(spineDeg + neckDeg, 0f, 0f));
            SetModelRot(HumanBodyBones.Head, Quaternion.Euler(spineDeg + neckDeg + headDeg, 0f, 0f));
        }

        // Both legs bend at hip and knee by half the knee angle each, so the ankle stays under the hip;
        // the hips drop so the straighter (weighted) leg keeps its foot on the floor.
        void StandLegs(float kneeLeft, float kneeRight)
        {
            if (!Has(HumanBodyBones.LeftUpperLeg) || !Has(HumanBodyBones.LeftLowerLeg) || !Has(HumanBodyBones.LeftFoot)) return;
            float thigh = Vector3.Distance(M(HumanBodyBones.LeftUpperLeg), M(HumanBodyBones.LeftLowerLeg));
            float shin = Vector3.Distance(M(HumanBodyBones.LeftLowerLeg), M(HumanBodyBones.LeftFoot));
            float k = Mathf.Min(kneeLeft, kneeRight) * 0.5f * Mathf.Deg2Rad;
            float drop = (thigh + shin) * (1f - Mathf.Cos(k));
            var hips = T(HumanBodyBones.Hips);
            hips.position -= transform.up * (drop * transform.lossyScale.y);

            foreach (bool right in new[] { false, true })
            {
                float a = (right ? kneeRight : kneeLeft) * 0.5f;
                SetModelRot(right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg, Quaternion.Euler(-a, 0f, 0f));
                SetModelRot(right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg, Quaternion.Euler(a, 0f, 0f));
                SetModelRot(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot, Quaternion.identity);
            }
        }

        void PoseLookAtHands(float t, float sit)
        {
            float knee = Mathf.Lerp(P.KneeWeighted, P.SitDownKnee, sit);
            float kneeFree = Mathf.Lerp(P.KneeFree, P.SitDownKnee, sit);
            // weight on her right foot
            StandLegs(kneeFree, knee);
            Lean(Mathf.Lerp(P.LookLean, P.SitDownLean, sit), P.LookNeckDown, P.LookHeadDown);

            float a = Mathf.Lerp(P.ArmOut0, P.ArmOut1, t) * Mathf.Deg2Rad;
            float f = P.ArmForward * Mathf.Deg2Rad;
            float elbow = Mathf.Lerp(P.Elbow0, P.Elbow1, t);
            foreach (bool right in new[] { false, true })
            {
                float sx = right ? _rightSide : -_rightSide; // outward along model x
                var u = new Vector3(sx * Mathf.Sin(a), -Mathf.Cos(a) * Mathf.Cos(f), Mathf.Cos(a) * Mathf.Sin(f)).normalized;
                var fwd = Vector3.ProjectOnPlane(new Vector3(-sx * P.ForearmInward, 0f, 1f), u).normalized;
                float e = (elbow + (right ? 0f : P.LeftElbowExtra)) * Mathf.Deg2Rad;
                var v = (u * Mathf.Cos(e) + fwd * Mathf.Sin(e)).normalized;
                var palm = new Vector3(-sx * 0.4f, 0.6f, -0.6f).normalized; // palms up, turned towards her face
                var h = (v - palm * Mathf.Tan(P.WristBack * Mathf.Deg2Rad)).normalized;
                ArmChain(right, W(u), W(v), W(h), W(palm));
            }
        }

        void PoseSeated(float t)
        {
            var up = transform.up;
            float s = transform.lossyScale.y;
            Lean(Mathf.Lerp(P.SeatLean0, P.SeatLean1, t), 0f, Mathf.Lerp(P.SeatHeadDown0, P.SeatHeadDown1, t));

            float hipWant = Mathf.Lerp(P.SeatHip0, P.SeatHip1, t);
            float kneeWant = Mathf.Lerp(P.SeatKnee0, P.SeatKnee1, t);
            const float floorY = 0f;
            foreach (bool right in new[] { false, true })
            {
                var upperId = right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg;
                var lowerId = right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg;
                var footId = right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;
                if (!Has(upperId) || !Has(lowerId) || !Has(footId)) continue;
                float thigh = Vector3.Distance(M(upperId), M(lowerId)) * s;
                float shin = Vector3.Distance(M(lowerId), M(footId)) * s;
                float hipY = T(upperId).position.y;
                float ankleY = floorY + AnkleHeight * s;

                // thigh angle h and knee flexion k from their ranges: the pair whose leg drop
                // (thigh·cos h + shin·cos(k − h)) is closest to hip-to-ankle height, nearest the wanted pair
                float drop = hipY - ankleY;
                float h = hipWant, best = float.MaxValue;
                for (float hh = P.SeatHip0; hh <= P.SeatHip1 + 0.01f; hh += 0.5f)
                    for (float kk = P.SeatKnee0; kk <= P.SeatKnee1 + 0.01f; kk += 0.5f)
                    {
                        float d = thigh * Mathf.Cos(hh * Mathf.Deg2Rad) + shin * Mathf.Cos((kk - hh) * Mathf.Deg2Rad);
                        float cost = Mathf.Abs(d - drop) * 100f + (Mathf.Abs(hh - hipWant) + Mathf.Abs(kk - kneeWant)) * 0.01f;
                        if (cost < best) { best = cost; h = hh; }
                    }
                SetModelRot(upperId, Quaternion.Euler(-h, 0f, 0f));

                // shin: whatever angle puts the ankle on the floor, tucked back or forward,
                // whichever gives a knee flexion closer to the wanted one
                float kneeY = T(lowerId).position.y;
                float c = Mathf.Clamp((kneeY - ankleY) / shin, -1f, 1f);
                float phi = Mathf.Acos(c) * Mathf.Rad2Deg; // shin from straight down
                float back = h + phi, fore = h - phi;
                float shinBack = Mathf.Abs(back - kneeWant) <= Mathf.Abs(fore - kneeWant) ? phi : -phi;
                SetModelRot(lowerId, Quaternion.Euler(shinBack, 0f, 0f));
                SetModelRot(footId, Quaternion.identity);
            }

            // hands on the thighs (two-bone IK per arm)
            foreach (bool right in new[] { false, true })
            {
                var upperLeg = T(right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
                var knee = T(right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg);
                var shoulder = T(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                if (upperLeg == null || knee == null || shoulder == null) continue;
                var thighDir = (knee.position - upperLeg.position).normalized;
                var wrist = Vector3.Lerp(upperLeg.position, knee.position, P.HandOnThigh)
                    + up * ((ThighRadius + P.HandAboveThigh) * s);
                float sx = right ? _rightSide : -_rightSide;
                var pole = W(new Vector3(sx * P.ElbowOut, 0f, -P.ElbowBack));
                var elbow = SolveElbow(shoulder.position, wrist, UpperArmLength * s, ForearmLength * s, pole);
                var h = (thighDir - up * P.HandTipDown).normalized;
                ArmChain(right, elbow - shoulder.position, wrist - elbow, h, -up);
            }
        }

        static Vector3 SolveElbow(Vector3 s, Vector3 target, float a, float b, Vector3 pole)
        {
            var d = target - s;
            float c = Mathf.Clamp(d.magnitude, 1e-4f, a + b - 1e-4f);
            var dir = d.normalized;
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            var p = Vector3.ProjectOnPlane(pole, dir);
            if (p.sqrMagnitude < 1e-6f) p = Vector3.ProjectOnPlane(Vector3.down, dir);
            p.Normalize();
            return s + a * (dir * cosA + p * Mathf.Sqrt(1f - cosA * cosA));
        }
    }
}
