using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Rect.y is world z. These are destinations only; the caller owns route finding.
    public static class PacePlan
    {
        public const float MinDistance = .8f; // Metres from the previous destination.
        public const float BlockerClearance = .35f; // Euclidean distance from each closed rectangle.
        public const float StandMin = 1.5f, StandMax = 4f; // Seconds spent at a destination.
        const int RandomAttempts = 512; // Bounded rejection sampling before a deterministic fallback.
        const float GridStep = .05f; // Metres between fallback samples; failure never returns an unsafe point.

        public readonly struct Step
        {
            public readonly Vector2 Target;
            public readonly float StandSeconds;
            public Step(Vector2 target, float standSeconds) { Target = target; StandSeconds = standSeconds; }
        }

        public static Step Next(List<Rect> floors, List<Rect> blockers, Vector2 previous, System.Random rng)
        {
            if (TryNext(floors, blockers, previous, rng, out var step)) return step;
            throw new InvalidOperationException("No clear pace target found in the supplied floor rectangles.");
        }

        public static Step Next(Rect floor, List<Rect> blockers, Vector2 previous, System.Random rng)
            => Next(new List<Rect> { floor }, blockers, previous, rng);

        public static bool TryNext(List<Rect> floors, List<Rect> blockers, Vector2 previous, System.Random rng, out Step step)
        {
            if (floors == null) throw new ArgumentNullException(nameof(floors));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            step = default;
            if (floors.Count == 0) return false;
            for (int i = 0; i < RandomAttempts; i++)
            {
                var r = floors[rng.Next(floors.Count)];
                if (r.width <= 0 || r.height <= 0) continue;
                var p = new Vector2(r.xMin + (float)rng.NextDouble() * r.width, r.yMin + (float)rng.NextDouble() * r.height);
                if (!Clear(p, blockers, previous)) continue;
                step = Make(p, rng); return true;
            }
            foreach (var r in floors)
            {
                if (r.width <= 0 || r.height <= 0) continue;
                int nx = Mathf.CeilToInt(r.width / GridStep), nz = Mathf.CeilToInt(r.height / GridStep);
                for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                {
                    var p = new Vector2(r.xMin + (x + .5f) * r.width / nx, r.yMin + (z + .5f) * r.height / nz);
                    if (!Clear(p, blockers, previous)) continue;
                    step = Make(p, rng); return true;
                }
            }
            return false;
        }

        static Step Make(Vector2 p, System.Random rng)
            => new Step(p, StandMin + (float)rng.NextDouble() * (StandMax - StandMin));

        static bool Clear(Vector2 p, List<Rect> blockers, Vector2 previous)
        {
            if ((p - previous).sqrMagnitude < MinDistance * MinDistance) return false;
            if (blockers == null) return true;
            foreach (var b in blockers)
            {
                var nearest = new Vector2(Mathf.Clamp(p.x, b.xMin, b.xMax), Mathf.Clamp(p.y, b.yMin, b.yMax));
                if ((p - nearest).sqrMagnitude < BlockerClearance * BlockerClearance) return false;
            }
            return true;
        }
    }

    // Construct on the loaded humanoid before playing a clip, as with SofaRig's hand calibration.
    // Tick advances time once per frame. Apply writes only the upper body after the Animator.
    public sealed class PhoneCallRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Start, Stop and left-hand pose transitions.
        public const float TalkMin = 2f, TalkMax = 5f, ListenMin = 2f, ListenMax = 6f; // State durations in seconds.
        public const float BeatMin = 3f, BeatMax = 7f; // Seconds between left-hand beats.
        public const float ReactionMin = 10f, ReactionMax = 20f; // Seconds between reaction starts.
        public const float LaughSeconds = .6f, SighSeconds = 1.2f; // Reaction pulse lengths.
        const float GestureMin = 1f, GestureMax = 2f; // Brief wave or neck touch, then return to the waist.
        const float SyllableMin = 6f, SyllableMax = 9f; // Seeded syllable frequency in Hz.
        const float MouthJitterHz = 2.3f, MouthJitterDepth = .3f; // Irregular syllable amplitude.
        const float RollMin = 8f, RollMax = 12f; // Tilt toward the right ear, degrees.
        const float ListenPitch = 10f, TalkPitch = -3f, TalkYaw = 14f; // Downward listening / raised talking gaze.
        const float GazeHz = .17f, NodHz = .7f, NodDegrees = 1.5f; // Slow searching and conversational nods.
        const float LaughPitch = -9f, SighPitch = 12f; // Reaction head offsets, degrees.
        const float ShoulderDegrees = 4f, LaughBobHz = 5f, ChestDegrees = 1.2f; // Shoulder rise and torso breath.
        const float BreathHz = .24f; // Breathing cycles per second.
        const float EarSide = .125f, EarUp = .07f, EarForward = .0f; // Ear area relative to head pivot, metres.
        const float ReferenceShoulders = .3f, ScaleMin = .75f, ScaleMax = 1.3f; // Scale anatomy offsets to the model.
        const float ElbowDown = .30f, ElbowForward = .30f, ElbowOut = .12f; // Forward/down pole, about 45 degrees.
        const float SideOut = .06f, SideDown = .12f, SideForward = .03f; // Lowered phone hand relative to hips.
        const float SupportDown = .045f, SupportBack = .025f; // Left palm under the right elbow.
        const float HipOut = .14f, HipUp = .035f, HipForward = .045f; // Hand-on-hip offset from hips.
        const float WaveOut = .16f, WaveUp = -.03f, WaveForward = .27f; // Open hand in front of chest.
        const float WaveSize = .035f, WaveHz = 1.5f; // Small open-palm wave.
        const float NeckSide = -.085f, NeckDown = -.025f, NeckBack = -.025f; // Neck / hair-end touch.
        const float CurlProximal = 22f, CurlIntermediate = 28f, CurlDistal = 14f; // Grip angles, degrees.
        const float ThumbCurl = 18f, ThumbTipCurl = 12f, RelaxedCurl = .3f; // Thumb grip and left-hand relaxation.
        const float ScreenGap = .0004f, ScreenBorder = .006f; // Screen offset and inset, metres.
        const float PhoneShade = .045f, ScreenShade = .12f; // Dark phone and inactive call screen.
        const float AxisEpsilon = .000001f, RotationEpsilon = .001f; // Degenerate-axis and unchanged-pose tolerances.
        const float Tau = 2f * Mathf.PI; // Radians per cycle.

        readonly Animator anim;
        readonly Transform spine, chest, neck, head, hips;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2], shoulder = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 4, 3];
        readonly Transform[] thumb = new Transform[3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly Vector3[] fingerAxis = new Vector3[2], palmAxis = new Vector3[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly Quaternion headFrame;
        readonly float anatomy;
        readonly System.Random rng;
        readonly Material bodyMaterial, screenMaterial;
        Quaternion[] before, written;
        bool hasWritten, wanted, disposed;
        float time, stateElapsed, beatElapsed, beatDuration, gestureDuration, reactionAt, reactionStart = -1;
        float syllableHz, roll, talkBlend, beatBlend;
        int beat, previousBeat;

        public Transform Phone { get; }
        public bool Active => !disposed && (wanted || Weight > 0);
        public float Weight { get; private set; }
        public bool Talking { get; private set; }
        public float MouthOpen { get; private set; }
        public float Smile { get; private set; }
        public string LastReaction { get; private set; } = "";
        public int ReactionCount { get; private set; }
        public float StateDuration { get; private set; }
        public float StateElapsed => stateElapsed;
        public Vector3 EarTarget => head.position + HeadAxes() * (new Vector3(EarSide, EarUp, EarForward) * anatomy);

        Quaternion HeadAxes() => head.rotation * headFrame;

        public PhoneCallRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("PhoneCallRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; rng = new System.Random(seed);
            hips = Bone(HumanBodyBones.Hips); spine = Bone(HumanBodyBones.Spine);
            chest = Bone(HumanBodyBones.Chest) ?? spine; neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("PhoneCallRig needs hips, spine and head.");
            headFrame = Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                shoulder[s] = Bone(s == 0 ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
                if (upper[s] == null || lower[s] == null || hand[s] == null) throw new InvalidOperationException("PhoneCallRig needs both arms and hands.");
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                    finger[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j));
                var direction = finger[s, 1, 0] != null ? finger[s, 1, 0].position - hand[s].position : hand[s].position - lower[s].position;
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < AxisEpsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                fingerAxis[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                palmAxis[s] = Quaternion.Inverse(hand[s].rotation) * palm;
                handFrame[s] = Quaternion.LookRotation(fingerAxis[s], palmAxis[s]);
            }
            for (int j = 0; j < 3; j++) thumb[j] = Bone((HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + j));
            anatomy = Mathf.Clamp(Vector3.Distance(upper[0].position, upper[1].position) / ReferenceShoulders, ScaleMin, ScaleMax);
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            // Unparented pivot preserves metre dimensions even under scaled humanoid hierarchies.
            Phone = new GameObject("PhoneCallPhone").transform;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) { SofaRig.Kill(Phone.gameObject); throw new InvalidOperationException("No phone material shader found."); }
            bodyMaterial = new Material(shader); bodyMaterial.color = new Color(PhoneShade, PhoneShade, PhoneShade);
            screenMaterial = new Material(shader); screenMaterial.color = new Color(ScreenShade, ScreenShade, ScreenShade);
            Part("body", PrimitiveType.Cube, Vector3.zero, new Vector3(SofaGeometry.PhoneW, SofaGeometry.PhoneH, SofaGeometry.PhoneT), bodyMaterial);
            Part("screen", PrimitiveType.Quad, new Vector3(0, 0, -SofaGeometry.PhoneT / 2 - ScreenGap),
                new Vector3(SofaGeometry.PhoneW - ScreenBorder, SofaGeometry.PhoneH - ScreenBorder * 2, 1), screenMaterial);
            Phone.gameObject.SetActive(false);
        }

        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) controlled.Add(b);
            return b;
        }

        void Part(string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false;
            UnityEngine.Object.DestroyImmediate(collider);
            g.transform.SetParent(Phone, false); g.transform.localPosition = position; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = material;
        }

        float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        static float Smooth(float value) => Mathf.SmoothStep(0, 1, value);
        static float Pulse(float age, float length) => age < 0 || age > length ? 0 : Mathf.Sin(Mathf.PI * age / length);
        static void Rotate(Transform bone, Vector3 axis, float degrees)
        { if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PhoneCallRig));
            if (wanted) return;
            wanted = true; Talking = true; time = stateElapsed = beatElapsed = 0;
            StateDuration = R(TalkMin, TalkMax); syllableHz = R(SyllableMin, SyllableMax); roll = R(RollMin, RollMax);
            beat = previousBeat = 0; beatBlend = 1; beatDuration = R(BeatMin, BeatMax); gestureDuration = R(GestureMin, GestureMax);
            reactionAt = R(ReactionMin, ReactionMax); reactionStart = -1;
            ReactionCount = 0; LastReaction = ""; MouthOpen = Smile = talkBlend = 0;
        }

        public void Stop() { wanted = false; Talking = false; MouthOpen = Smile = 0; }

        public void Tick(float dt)
        {
            if (disposed || float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) return;
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (!wanted) { MouthOpen = Smile = 0; return; }
            time += dt; stateElapsed += dt;
            while (stateElapsed >= StateDuration)
            {
                stateElapsed -= StateDuration; Talking = !Talking;
                StateDuration = Talking ? R(TalkMin, TalkMax) : R(ListenMin, ListenMax);
                syllableHz = R(SyllableMin, SyllableMax);
            }
            talkBlend = Mathf.MoveTowards(talkBlend, Talking ? 1 : 0, dt / BlendSeconds);
            beatElapsed += dt;
            while (beatElapsed >= beatDuration)
            {
                beatElapsed -= beatDuration; previousBeat = beat;
                beat = (beat + 1 + rng.Next(3)) % 4; // Never repeat the same beat.
                beatDuration = R(BeatMin, BeatMax); gestureDuration = R(GestureMin, GestureMax);
            }
            beatBlend = Smooth(Mathf.Clamp01(beatElapsed / BlendSeconds));
            while (time >= reactionAt)
            {
                reactionStart = reactionAt; LastReaction = rng.Next(2) == 0 ? "Laugh" : "Sigh";
                ReactionCount++; reactionAt += R(ReactionMin, ReactionMax);
            }
            float jitter = 1 - MouthJitterDepth + MouthJitterDepth * Mathf.Sin(time * Tau * MouthJitterHz);
            float syllable = Mathf.Max(0, Mathf.Sin(stateElapsed * Tau * syllableHz));
            MouthOpen = Talking ? Mathf.Clamp01(syllable * jitter * Smooth(Weight)) : 0;
            Smile = LastReaction == "Laugh" ? ReactionPulse() * Smooth(Weight) : 0;
        }

        float ReactionPulse() => reactionStart < 0 ? 0 : Pulse(time - reactionStart, LastReaction == "Laugh" ? LaughSeconds : SighSeconds);

        // Remove our previous overlay when a paused Animator has not supplied a new pose.
        // Animated bones with a new local rotation are left exactly as the caller supplied them.
        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var b = controlled[i];
                if (hasWritten && Quaternion.Angle(b.localRotation, written[i]) < RotationEpsilon) b.localRotation = before[i];
                before[i] = b.localRotation;
            }
        }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose();
            float w = Smooth(Weight);
            Phone.gameObject.SetActive(Active && Weight > 0);
            if (w > 0)
            {
                var root = anim.transform; var up = root.up; var right = root.right; var forward = root.forward;
                float pulse = ReactionPulse();
                float laugh = LastReaction == "Laugh" ? pulse : 0, sigh = LastReaction == "Sigh" ? pulse : 0;
                Rotate(chest, right, ChestDegrees * Mathf.Sin(time * Tau * BreathHz) * w);
                float bob = laugh * Mathf.Sin((time - reactionStart) * Tau * LaughBobHz);
                for (int s = 0; s < 2; s++) Rotate(shoulder[s], forward, (s == 0 ? -1 : 1) * ShoulderDegrees * (sigh + bob) * w);
                float pitch = Mathf.Lerp(ListenPitch, TalkPitch, talkBlend) + NodDegrees * Mathf.Sin(time * Tau * NodHz) * talkBlend + LaughPitch * laugh + SighPitch * sigh;
                float yaw = TalkYaw * Mathf.Sin(time * Tau * GazeHz) * talkBlend;
                Rotate(neck ?? head, right, pitch * w / 2);
                Rotate(head, right, pitch * w / 2);
                Rotate(head, up, yaw * w);
                Rotate(head, forward, -roll * w);

                // Recompute after all head/neck/shoulder edits so there is no frame of lag.
                var axes = HeadAxes(); var phoneUp = axes * Vector3.up; var screenNormal = axes * Vector3.left;
                var phoneRight = Vector3.Cross(screenNormal, phoneUp).normalized;
                SofaGeometry.Grip(EarTarget, phoneUp, screenNormal, phoneRight, 1, out var wrist, out var fingers, out var palm);
                var side = hips.position + (right * (ReferenceShoulders / 2 + SideOut) - up * SideDown + forward * SideForward) * anatomy;
                var target = Vector3.Lerp(side, wrist, w);
                var pole = upper[1].position + (right * ElbowOut - up * ElbowDown + forward * ElbowForward) * anatomy;
                SofaRig.TwoBone(upper[1], lower[1], hand[1], target, pole, w);
                var hold = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(handFrame[1]);
                var relaxed = Quaternion.LookRotation(-up, -right) * Quaternion.Inverse(handFrame[1]);
                hand[1].rotation = Quaternion.Slerp(hand[1].rotation, Quaternion.Slerp(relaxed, hold, w), w);
                Curl(1, w);
                // A rigid grip offset transformed by the actual wrist, including unreachable IK targets.
                var localOffset = Quaternion.Inverse(hold) * (EarTarget - wrist);
                var localRotation = Quaternion.Inverse(hold) * Quaternion.LookRotation(-screenNormal, phoneUp);
                Phone.SetPositionAndRotation(hand[1].position + hand[1].rotation * localOffset, hand[1].rotation * localRotation);

                LeftPose(previousBeat, up, right, forward, out var p0, out var f0, out var n0);
                LeftPose(beat, up, right, forward, out var p1, out var f1, out var n1);
                var leftTarget = Vector3.Lerp(p0, p1, beatBlend);
                SofaRig.TwoBone(upper[0], lower[0], hand[0], leftTarget,
                    upper[0].position + (-right * ElbowOut - up * ElbowDown + forward * SideForward) * anatomy, w);
                var leftRotation = Quaternion.Slerp(Quaternion.LookRotation(f0, n0), Quaternion.LookRotation(f1, n1), beatBlend) * Quaternion.Inverse(handFrame[0]);
                hand[0].rotation = Quaternion.Slerp(hand[0].rotation, leftRotation, w);
                Curl(0, w * RelaxedCurl * (1 - Brief(beat)));
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }

        float Brief(int kind) => kind < 2 ? 0 : Smooth(Mathf.Clamp01(beatElapsed / BlendSeconds)) *
            (1 - Smooth(Mathf.Clamp01((beatElapsed - gestureDuration + BlendSeconds) / BlendSeconds))) *
            (kind == 2 ? talkBlend : 1);

        void LeftPose(int kind, Vector3 up, Vector3 right, Vector3 forward, out Vector3 target, out Vector3 fingers, out Vector3 palm)
        {
            target = lower[1].position - up * SupportDown - forward * SupportBack;
            fingers = right; palm = up;
            if (kind == 1)
            {
                target = hips.position + (-right * HipOut + up * HipUp + forward * HipForward) * anatomy;
                fingers = -up; palm = right; return;
            }
            float brief = Brief(kind);
            if (kind == 2)
            {
                var wave = chest.position + (-right * WaveOut + up * WaveUp + forward * WaveForward +
                    right * (WaveSize * Mathf.Sin(beatElapsed * Tau * WaveHz))) * anatomy;
                target = Vector3.Lerp(target, wave, brief);
                fingers = Vector3.Slerp(right, up, brief); palm = Vector3.Slerp(up, forward, brief);
            }
            else if (kind == 3)
            {
                var touch = head.position + HeadAxes() * (new Vector3(NeckSide, NeckDown, NeckBack) * anatomy);
                target = Vector3.Lerp(target, touch, brief);
                // Interpolate a frame: independently swapping fingers/palm would make them parallel halfway.
                var frame = Quaternion.Slerp(Quaternion.LookRotation(right, up), Quaternion.LookRotation(up, right), brief);
                fingers = frame * Vector3.forward; palm = frame * Vector3.up;
            }
        }

        void Curl(int side, float weight)
        {
            var axis = hand[side].rotation * Vector3.Cross(fingerAxis[side], palmAxis[side]).normalized;
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                Rotate(finger[side, f, j], axis, weight * (j == 0 ? CurlProximal : j == 1 ? CurlIntermediate : CurlDistal));
            if (side == 1) for (int j = 0; j < thumb.Length; j++) Rotate(thumb[j], axis, weight * (j == 0 ? ThumbCurl : ThumbTipCurl));
        }

        public void Dispose()
        {
            if (disposed) return;
            if (hasWritten) ReadPose();
            disposed = true; wanted = Talking = false; Weight = MouthOpen = Smile = 0;
            SofaRig.Kill(Phone != null ? Phone.gameObject : null);
            Release(bodyMaterial); Release(screenMaterial);
        }

        static void Release(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
