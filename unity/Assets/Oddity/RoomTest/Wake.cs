using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Shared by WakeRig and model-free timer tests. No Transform access here.
    public sealed class WakeClock
    {
        public const float BlendSeconds = .4f; // Seconds to enter and leave the overlay.
        public const float RaiseEnd = .8f, StretchSeconds = 1.8f; // Raise, then hold the full stretch for 1.8 seconds.
        public const float StretchEnd = RaiseEnd + StretchSeconds, LowerEnd = 3.3f; // Absolute seconds from Start.
        public const float RubTouch = 3.7f, RubRelease = 4.5f, RubEnd = 4.9f; // Eye approach starts at LowerEnd.
        public const float ScratchTouch = 5.3f, ScratchRelease = 5.7f, ScratchEnd = 6.1f; // One downward stroke, not a loop.
        public const float ShakeEnd = 7.1f, FadeStart = 7.6f, Length = FadeStart + BlendSeconds; // Settle then release; total 8 seconds.
        public const float YawnIn = .5f, YawnPeak = 1.1f, YawnClose = 2.2f, YawnEnd = 3f; // Yawn envelope boundaries.
        public const float YawnMouth = .9f, SleepyEyes = .25f; // Face output maxima and neutral sleepy eyelids.
        public const float RubCycles = 2f, ShakeCycles = 2f; // Two eye-rub strokes and two gentle head oscillations.
        public const float Tau = 2f * Mathf.PI; // Radians per cycle.

        bool running;
        public bool Active => running || Weight > 0;
        public bool Done { get; private set; }
        public float Weight { get; private set; }
        public float Time { get; private set; }
        public float Smile => 0f;
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public string Phase { get; private set; } = "Stopped";
        public float Stretch => Envelope(Time, 0, RaiseEnd, StretchEnd, LowerEnd);
        public float Rub => Envelope(Time, LowerEnd, RubTouch, RubRelease, RubEnd);
        public float Scratch => Envelope(Time, RubEnd, ScratchTouch, ScratchRelease, ScratchEnd);
        public float RubStroke => Mathf.Sin(Tau * RubCycles * Mathf.InverseLerp(RubTouch, RubRelease, Time));
        public float ScratchStroke => Ease(ScratchTouch, ScratchRelease, Time);
        public float Shake => Time < ScratchEnd || Time > ShakeEnd ? 0f :
            Mathf.Sin(Tau * ShakeCycles * Mathf.InverseLerp(ScratchEnd, ShakeEnd, Time)) *
            Mathf.Sin(Mathf.PI * Mathf.InverseLerp(ScratchEnd, ShakeEnd, Time));

        public void Start()
        {
            running = true; Done = false; Time = Weight = MouthOpen = EyesClosed = 0;
            Phase = "Raise";
        }

        public void Stop() { running = false; if (!Done) Phase = Weight > 0 ? "Stopping" : "Stopped"; }

        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (running)
            {
                Time = Mathf.Min(Length, Time + dt);
                Weight = Mathf.Min(Mathf.MoveTowards(Weight, 1, dt / BlendSeconds), Mathf.Clamp01((Length - Time) / BlendSeconds));
                if (Time >= Length) { running = false; Done = true; Phase = "Done"; }
                else Phase = Time < RaiseEnd ? "Raise" : Time < StretchEnd ? "Stretch" : Time < LowerEnd ? "Lower" :
                    Time < RubEnd ? "RubEye" : Time < ScratchEnd ? "ScratchHead" : Time < ShakeEnd ? "Shake" : "Settle";
            }
            else
            {
                Weight = Mathf.MoveTowards(Weight, 0, dt / BlendSeconds);
                if (!Done && Weight <= 0) Phase = "Stopped";
            }
            float w = Mathf.SmoothStep(0, 1, Weight);
            MouthOpen = YawnMouth * Envelope(Time, YawnIn, YawnPeak, YawnClose, YawnEnd) * w;
            EyesClosed = Mathf.Lerp(SleepyEyes, 1, Mathf.Max(Stretch, Rub)) * w;
        }

        public static float Ease(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));
        public static float Envelope(float t, float a, float b, float c, float d) => Ease(a, b, t) * (1 - Ease(c, d, t));
    }

    // Construct in the imported neutral T-pose, before playing the sitting clip.
    // Tick once per frame; Apply once in LateUpdate after Animator and seating adjustments.
    public sealed class WakeRig : IDisposable
    {
        public const float BlendSeconds = WakeClock.BlendSeconds; // Public blend contract, seconds.
        const float SpineArch = -9f, ChestArch = -12f, HeadLift = -9f; // Stretch backward about body right, degrees.
        const float SleepyPitch = 7f, RubTilt = 4f, ShakeDegrees = 5f; // Sleepy chin drop, rub lean, small head shake.
        const float NeckShare = .4f; // Neck takes 40 percent of head pitch and yaw.
        const float WristHalfGap = .055f, LaceStagger = .012f, StretchForward = .015f; // Interlaced wrist placement, metres.
        const float ReachFraction = .994f; // Near-straight arm without a locked/singular elbow.
        const float ReferenceShoulders = .3f, ScaleMin = .75f, ScaleMax = 1.3f; // Scale face/hand offsets to the humanoid.
        const float LapSide = .14f, LapUp = .05f, LapForward = .22f; // Resting hands relative to hips, metres.
        const float ElbowOut = .3f, ElbowForward = .16f, ElbowDown = .2f; // IK pole offsets, metres.
        const float EyeSide = .035f, EyeUp = .075f, EyeFront = .10f; // Right eye area from the calibrated head pivot, metres.
        const float KnuckleLength = .045f, HandThickness = .018f; // Wrist-to-knuckle and back-of-hand clearance, metres.
        const float RubTravel = .008f, RubCurl = .85f; // Eye rubbing excursion in metres and closed-hand amount.
        const float NapeSide = .055f, NapeUp = .075f, NapeBack = .115f; // Rear head/hair contact anchor, metres.
        const float ScratchTravel = .025f, ScratchCurl = .65f; // One downward scratch distance and finger curl.
        const float LaceForward = .25f, LaceCurl = .55f, LaceSplay = 5f; // Interleaved fingers: forward bias, curl, splay degrees.
        const float FingerSplayCenter = 2.5f; // Midpoint of index through little in the thumb-first array.
        const float RestCurl = .2f, ProximalCurl = 32f, MiddleCurl = 45f, DistalCurl = 25f, ThumbCurl = 18f; // Finger bend degrees.
        const float VariationMin = .95f, VariationMax = 1.05f; // Seeded variation of rub travel and head-shake amplitude only.
        const float AxisEpsilon = .000001f, RotationEpsilon = .001f; // Degenerate direction and unchanged rotation thresholds.

        readonly Animator anim;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 5, 3]; // Thumb, index, middle, ring, little.
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly Vector3[] fingerLocal = new Vector3[2], palmLocal = new Vector3[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly Quaternion[] before, written;
        readonly Quaternion headFrame;
        readonly WakeClock clock = new WakeClock();
        readonly float anatomy, variation;
        bool hasWritten, disposed;

        public bool Active => !disposed && clock.Active;
        public bool Done => clock.Done;
        public float Weight => disposed ? 0 : clock.Weight;
        public float Smile => disposed ? 0 : clock.Smile;
        public float EyesClosed => disposed ? 0 : clock.EyesClosed;
        public float MouthOpen => disposed ? 0 : clock.MouthOpen;
        public string Phase => disposed ? "Disposed" : clock.Phase;

        public WakeRig(Animator anim, int seed, Quaternion? restHeadFrame = null)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("WakeRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim;
            var rng = new System.Random(seed);
            variation = VariationMin + (VariationMax - VariationMin) * (float)rng.NextDouble();
            hips = anim.GetBoneTransform(HumanBodyBones.Hips); // Read only: the caller owns hips and all leg bones.
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("WakeRig needs hips, spine and head.");
            headFrame = restHeadFrame ?? Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[s] == null || lower[s] == null || hand[s] == null) throw new InvalidOperationException("WakeRig needs both complete arms.");
                for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                    finger[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + f * 3 + j));
                var direction = finger[s, 2, 0] != null ? finger[s, 2, 0].position - hand[s].position : hand[s].position - lower[s].position;
                if (direction.sqrMagnitude < AxisEpsilon) throw new InvalidOperationException("WakeRig cannot calibrate a zero-length hand.");
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < AxisEpsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                fingerLocal[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                palmLocal[s] = Quaternion.Inverse(hand[s].rotation) * palm;
                handFrame[s] = Quaternion.LookRotation(fingerLocal[s], palmLocal[s]);
            }
            anatomy = Mathf.Clamp(Vector3.Distance(upper[0].position, upper[1].position) / ReferenceShoulders, ScaleMin, ScaleMax);
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
        }

        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) controlled.Add(b);
            return b;
        }

        public void Start() { if (!disposed) clock.Start(); }
        public void Stop() { if (!disposed) clock.Stop(); }
        public void Tick(float dt) { if (!disposed) clock.Tick(dt); }

        // The Animator may be paused in EditMode: undo only rotations still equal to our last write.
        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var b = controlled[i];
                if (hasWritten && Quaternion.Angle(b.localRotation, written[i]) < RotationEpsilon) b.localRotation = before[i];
                before[i] = b.localRotation;
            }
        }

        static void Rotate(Transform b, Vector3 axis, float degrees)
        {
            if (b != null) b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation;
        }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose();
            float w = Mathf.SmoothStep(0, 1, Weight);
            if (w > 0)
            {
                var right = anim.transform.right; var up = anim.transform.up; var forward = anim.transform.forward;
                float stretch = clock.Stretch;
                Rotate(spine, right, SpineArch * stretch * w);
                Rotate(chest, right, ChestArch * stretch * w);
                float pitch = Mathf.Lerp(SleepyPitch, HeadLift, stretch) * w;
                float yaw = clock.Shake * ShakeDegrees * variation * w;
                Rotate(neck, right, neck == null ? 0 : pitch * NeckShare);
                Rotate(head, right, pitch * (neck == null ? 1 : 1 - NeckShare));
                Rotate(neck, up, yaw * NeckShare);
                Rotate(head, up, yaw * (neck == null ? 1 : 1 - NeckShare));
                Rotate(head, forward, RubTilt * clock.Rub * w);

                // Shared height is reachable by both arms, measured after the torso arch.
                var center = (upper[0].position + upper[1].position) / 2 + forward * (StretchForward * anatomy);
                float height = float.PositiveInfinity;
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1 : 1;
                    var flat = center + right * (side * WristHalfGap * anatomy) + forward * (side * LaceStagger * anatomy);
                    var delta = flat - upper[s].position;
                    float reach = (Vector3.Distance(upper[s].position, lower[s].position) + Vector3.Distance(lower[s].position, hand[s].position)) * ReachFraction;
                    float horizontal = Vector3.ProjectOnPlane(delta, up).sqrMagnitude;
                    height = Mathf.Min(height, -Vector3.Dot(delta, up) + Mathf.Sqrt(Mathf.Max(0, reach * reach - horizontal)));
                }
                var headAxes = head.rotation * headFrame;
                var headUp = headAxes * Vector3.up; var headForward = headAxes * Vector3.forward; var headRight = headAxes * Vector3.right;
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1 : 1;
                    var lap = hips.position + (right * (side * LapSide) + up * LapUp + forward * LapForward) * anatomy;
                    var overhead = center + up * height + right * (side * WristHalfGap * anatomy) + forward * (side * LaceStagger * anatomy);
                    var target = Vector3.Lerp(lap, overhead, stretch);
                    var restFrame = Quaternion.LookRotation(forward, up);
                    var laceFrame = Quaternion.LookRotation(-right * side + forward * LaceForward, up);
                    var frame = Quaternion.Slerp(restFrame, laceFrame, stretch);
                    float curl = Mathf.Lerp(RestCurl, LaceCurl, stretch);
                    if (s == 1)
                    {
                        // Palm faces away from the eye: the hand's back/knuckles make contact.
                        var eye = head.position + (headRight * EyeSide + headUp * EyeUp + headForward * EyeFront) * anatomy;
                        var rub = eye - headUp * (KnuckleLength * anatomy) + headForward * (HandThickness * anatomy) +
                            headRight * (RubTravel * variation * clock.RubStroke * anatomy);
                        target = Vector3.Lerp(target, rub, clock.Rub);
                        frame = Quaternion.Slerp(frame, Quaternion.LookRotation(headUp, headForward), clock.Rub);
                        curl = Mathf.Lerp(curl, RubCurl, clock.Rub);
                        var nape = head.position + (headRight * NapeSide + headUp * (NapeUp - KnuckleLength - ScratchTravel * clock.ScratchStroke) -
                            headForward * (NapeBack + HandThickness)) * anatomy;
                        target = Vector3.Lerp(target, nape, clock.Scratch);
                        frame = Quaternion.Slerp(frame, Quaternion.LookRotation(headUp, headForward), clock.Scratch);
                        curl = Mathf.Lerp(curl, ScratchCurl, clock.Scratch);
                    }
                    var pole = upper[s].position + (right * (side * ElbowOut) + forward * ElbowForward - up * ElbowDown) * anatomy;
                    SofaRig.TwoBone(upper[s], lower[s], hand[s], target, pole, w);
                    hand[s].rotation = Quaternion.Slerp(hand[s].rotation, frame * Quaternion.Inverse(handFrame[s]), w);
                    Curl(s, curl * w, stretch * w);
                }
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }

        // SofaRig.Curl is private: use the same calibrated finger/palm convention here.
        void Curl(int s, float amount, float lace)
        {
            var axis = hand[s].rotation * Vector3.Cross(fingerLocal[s], palmLocal[s]).normalized;
            var palm = hand[s].rotation * palmLocal[s];
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
            {
                var b = finger[s, f, j];
                if (b == null) continue;
                float degrees = f == 0 ? ThumbCurl : j == 0 ? ProximalCurl : j == 1 ? MiddleCurl : DistalCurl;
                Rotate(b, axis, degrees * amount);
                if (f > 0 && j == 0) Rotate(b, palm, (f - FingerSplayCenter) * LaceSplay * lace * (s == 0 ? 1 : -1));
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            try { DisposeCore(); }
            catch (System.Exception) { disposed = true; }
        }

        void DisposeCore()
        {
            if (disposed) return;
            if (hasWritten) { try { ReadPose(); } catch (System.Exception) { } }
            clock.Stop(); disposed = true;
            // No props or materials are allocated by this gesture.
        }
    }
}
