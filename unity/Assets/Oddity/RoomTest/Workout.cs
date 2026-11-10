using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // A bone-free clock, also usable by a caller's UI and EditMode timer tests.
    public sealed class WorkoutTimeline
    {
        readonly float breathOffset;
        double elapsed;
        bool wanted;
        public float Time => (float)(elapsed % WorkoutRig.LoopSeconds);
        public float Weight { get; private set; }
        public bool Active => wanted || Weight > 0f;
        public int CompletedLoops => (int)(elapsed / WorkoutRig.LoopSeconds);
        public int Rep { get; private set; }
        public int TotalReps { get; private set; }
        public string Phase { get; private set; } = "Idle";
        public float Breath => Mathf.Sin((float)(elapsed % WorkoutRig.BreathSeconds) * WorkoutRig.Tau / WorkoutRig.BreathSeconds + breathOffset);
        public WorkoutTimeline(int seed) { breathOffset = (float)new System.Random(seed).NextDouble() * WorkoutRig.Tau; }
        public void Start()
        {
            if (wanted) return;
            if (Weight <= 0f) elapsed = 0;
            wanted = true; UpdateState();
        }
        public void Stop() { wanted = false; }
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            Weight = Mathf.MoveTowards(Weight, wanted ? 1f : 0f, dt / WorkoutRig.BlendSeconds);
            if (wanted) elapsed += dt;
            UpdateState();
        }
        void UpdateState()
        {
            if (!Active) { Phase = "Idle"; Rep = 0; return; }
            float t = Time;
            int done = 0;
            if (t < WorkoutRig.StretchTotal)
            {
                done = Mathf.FloorToInt(t / WorkoutRig.StretchSeconds); Rep = done + 1;
                Phase = t % WorkoutRig.StretchSeconds < WorkoutRig.StretchSeconds / 2f ? "StretchLeft" : "StretchRight";
            }
            else if ((t -= WorkoutRig.StretchTotal) < WorkoutRig.SquatTotal)
            {
                done = Mathf.FloorToInt(t / WorkoutRig.SquatSeconds); Rep = done + 1;
                Phase = "Squat"; done += WorkoutRig.StretchReps;
            }
            else if ((t -= WorkoutRig.SquatTotal) < WorkoutRig.LungeTotal)
            {
                int index = Mathf.FloorToInt(t / WorkoutRig.LungeSeconds); Rep = index + 1;
                Phase = index % 2 == 0 ? "LungeLeft" : "LungeRight";
                done = WorkoutRig.StretchReps + WorkoutRig.SquatReps + index;
            }
            else
            {
                t -= WorkoutRig.LungeTotal; Rep = 0; done = WorkoutRig.RepsPerLoop;
                Phase = t < WorkoutRig.CatchBreathSeconds ? "Breathe" : "Wipe";
            }
            TotalReps = CompletedLoops * WorkoutRig.RepsPerLoop + done;
        }
    }

    // Calibrate in a standing, upright imported pose before playing the base clip.
    // Tick once in Update; Apply once in LateUpdate after Animator and before VRM spring bones.
    public sealed class WorkoutRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Start/Stop and transitions, seconds.
        public const float MatWidth = .6f, MatLength = 1.7f, MatThickness = .006f; // World-space metres, x/y/z = width/thickness/length.
        public const int StretchReps = 4, SquatReps = 6, LungeReps = 4; // Stretch rep means one left/right pair.
        public const int RepsPerLoop = StretchReps + SquatReps + LungeReps; // Completed exercise reps per circuit.
        public const float StretchSeconds = 4f, SquatSeconds = 2f, LungeSeconds = 3f; // Seconds per rep.
        public const float CatchBreathSeconds = 2f, WipeSeconds = 2f; // Recovery and forehead sweep durations.
        public const float StretchTotal = StretchReps * StretchSeconds, SquatTotal = SquatReps * SquatSeconds, LungeTotal = LungeReps * LungeSeconds; // Section lengths.
        public const float LoopSeconds = StretchTotal + SquatTotal + LungeTotal + CatchBreathSeconds + WipeSeconds; // 44 seconds.
        public const float Tau = 2f * Mathf.PI; // Radians in a full cycle.
        public const float BreathSeconds = 2.4f, BreathRise = .008f, BreathDegrees = 1.8f; // Visible chest breathing period, metres and degrees.
        const float StanceHalf = .16f, LungeReach = .56f, LungeHipForward = .28f; // Foot lateral spacing, front step and hip shift, metres.
        const float LungeStepSeconds = .4f, StepLift = .012f; // Step setup/return duration and sole clearance, below the 2 cm contact tolerance.
        const float SquatDrop = .24f, LungeDrop = .22f, RecoveryDrop = .08f; // Pelvis lowering in metres.
        const float StretchDegrees = 17f, SquatLean = 16f, LungeLean = 8f, RecoveryLean = 30f; // Torso angles, degrees.
        const float ChestShare = .45f, NeckCounter = -.25f; // Share of torso bend and head counterbend.
        const float LegReachRatio = .97f, ReachMargin = .004f; // Bent neutral stance and IK reach safety in metres.
        const float KneeForward = .65f, KneeOut = .03f; // Forward knee pole and lateral separation in metres.
        const float ElbowOut = .30f, ElbowDown = .20f, ElbowForward = .10f; // Arm pole offsets in metres.
        const float OverheadSide = .10f, OverheadRise = .20f, OverheadForward = .02f; // Wrists relative to head in metres.
        const float ForwardReach = .43f, ForwardHandSide = .16f, ForwardHandDown = .04f; // Wrists relative to chest during squat/lunge.
        const float RestHandSide = .06f, RestHandDown = .04f; // Relaxed hands relative to upper-leg roots.
        const float KneeHandUp = .08f, KneeHandFront = .03f; // Hands rest on thighs just above knees, metres.
        const float ForeheadRise = .11f, ForeheadFront = .10f, WipeTravel = .10f, WipeClearance = .025f; // Forehead path and clearance, metres.
        const float SmileBase = .12f, SmileRecovery = .24f, MouthEffort = .18f, MouthBreath = .32f, EyeEffort = .15f; // Face blend weights.
        const float MatRed = .72f, MatGreen = .80f, MatBlue = .88f, MatRoughness = .08f; // Pastel blue mat and smoothness.
        const float SoleInfluence = .25f, FallbackAnkleMin = .02f, FallbackAnkleMax = .14f; // Foot skin-weight cutoff and fallback ankle height, metres.
        const float PositionEpsilon = .000001f, AngleEpsilon = .001f, AxisEpsilon = .000001f; // Pose equality and degenerate-axis tolerances.

        readonly Animator anim;
        readonly WorkoutTimeline clock;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        readonly Transform[] arm = new Transform[2], forearm = new Transform[2], hand = new Transform[2];
        readonly Quaternion[] footFrame = new Quaternion[2], handFrame = new Quaternion[2];
        readonly Vector3[] soleLocal = new Vector3[2];
        readonly Vector3[] targets = new Vector3[2];
        readonly float[] legLength = new float[2];
        readonly List<Transform> bones = new List<Transform>();
        readonly Quaternion[] neutral, beforeRot, writtenRot;
        readonly Vector3[] beforePos, writtenPos;
        readonly Material matMaterial;
        bool written, disposed;
        float standHeight;
        public readonly Transform Mat; // Caller places this independent world prop; top surface must be y=0.
        public bool Active => !disposed && clock.Active;
        public float Weight => disposed ? 0f : clock.Weight;
        public string Phase => disposed ? "Idle" : clock.Phase;
        public int Rep => clock.Rep;
        public int TotalReps => clock.TotalReps;
        public int CompletedLoops => clock.CompletedLoops;
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public Vector3 LeftContact => foot[0].TransformPoint(soleLocal[0]);
        public Vector3 RightContact => foot[1].TransformPoint(soleLocal[1]);
        public float MaxFootError { get; private set; }

        public WorkoutRig(Animator anim, int seed)
        {
            if (anim == null || !anim.isHuman) throw new ArgumentException("Workout needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new WorkoutTimeline(seed);
            hips = Bone(HumanBodyBones.Hips); spine = Bone(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? spine;
            neck = anim.GetBoneTransform(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            for (int s = 0; s < 2; s++)
            {
                thigh[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                shin[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                foot[s] = Bone(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                arm[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                forearm[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                footFrame[s] = Quaternion.Inverse(anim.transform.rotation) * foot[s].rotation;
                var fingers = hand[s].InverseTransformDirection((hand[s].position - forearm[s].position).normalized);
                var palm = hand[s].InverseTransformDirection(-anim.transform.up);
                handFrame[s] = Quaternion.Inverse(Quaternion.LookRotation(fingers, palm));
                legLength[s] = Vector3.Distance(thigh[s].position, shin[s].position) + Vector3.Distance(shin[s].position, foot[s].position);
                soleLocal[s] = foot[s].InverseTransformPoint(new Vector3(foot[s].position.x,
                    foot[s].position.y - Mathf.Clamp(foot[s].position.y - anim.transform.position.y, FallbackAnkleMin, FallbackAnkleMax), foot[s].position.z));
            }
            MeasureSoles();
            standHeight = Mathf.Min(legLength[0], legLength[1]) * LegReachRatio +
                (foot[0].position.y - thigh[0].position.y + hips.position.y - foot[0].TransformPoint(soleLocal[0]).y);
            // Parent-first order matters for both posing and restoring complete local poses.
            foreach (var t in anim.GetComponentsInChildren<Transform>(true))
                for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                    if (anim.GetBoneTransform((HumanBodyBones)b) == t) { bones.Add(t); break; }
            neutral = new Quaternion[bones.Count]; beforeRot = new Quaternion[bones.Count]; writtenRot = new Quaternion[bones.Count];
            beforePos = new Vector3[bones.Count]; writtenPos = new Vector3[bones.Count];
            for (int i = 0; i < bones.Count; i++) neutral[i] = bones[i].localRotation;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Workout mat requires a Lit or Standard shader.");
            matMaterial = new Material(shader); matMaterial.color = new Color(MatRed, MatGreen, MatBlue);
            matMaterial.SetFloat("_Smoothness", MatRoughness);
            var mat = GameObject.CreatePrimitive(PrimitiveType.Cube); mat.name = "WorkoutMat"; Mat = mat.transform;
            Release(mat.GetComponent<Collider>());
            Mat.localScale = new Vector3(MatWidth, MatThickness, MatLength);
            Mat.position = new Vector3(anim.transform.position.x, -MatThickness / 2f, anim.transform.position.z);
            Mat.rotation = Quaternion.Euler(0f, anim.transform.eulerAngles.y, 0f);
            mat.GetComponent<Renderer>().sharedMaterial = matMaterial;
        }

        Transform Bone(HumanBodyBones b) => anim.GetBoneTransform(b) ?? throw new ArgumentException("Workout missing bone: " + b);

        void MeasureSoles()
        {
            var low = new[] { float.PositiveInfinity, float.PositiveInfinity };
            foreach (var skin in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var weights = skin.sharedMesh.boneWeights; var skinBones = skin.bones;
                if (weights.Length != skin.sharedMesh.vertexCount) continue;
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh); var verts = mesh.vertices;
                    for (int v = 0; v < verts.Length; v++)
                    {
                        var bw = weights[v];
                        for (int s = 0; s < 2; s++)
                        {
                            float influence = Influence(bw.boneIndex0, bw.weight0, s, skinBones) + Influence(bw.boneIndex1, bw.weight1, s, skinBones)
                                + Influence(bw.boneIndex2, bw.weight2, s, skinBones) + Influence(bw.boneIndex3, bw.weight3, s, skinBones);
                            if (influence >= SoleInfluence) low[s] = Mathf.Min(low[s], skin.transform.TransformPoint(verts[v]).y);
                        }
                    }
                }
                finally { Release(mesh); }
            }
            for (int s = 0; s < 2; s++) if (!float.IsPositiveInfinity(low[s]))
                soleLocal[s] = foot[s].InverseTransformPoint(new Vector3(foot[s].position.x, low[s], foot[s].position.z));
        }
        float Influence(int index, float weight, int side, Transform[] skinBones)
            => index >= 0 && index < skinBones.Length && skinBones[index] != null &&
                (skinBones[index] == foot[side] || skinBones[index].IsChildOf(foot[side])) ? weight : 0f;

        public void Start() { if (!disposed) clock.Start(); }
        public void Stop() { clock.Stop(); }
        public void Tick(float dt)
        {
            if (disposed) return;
            clock.Tick(dt);
            bool recovery = Phase == "Breathe" || Phase == "Wipe";
            Smile = Weight * (recovery ? SmileRecovery : SmileBase);
            EyesClosed = Weight * EyeEffort * Mathf.Max(0f, clock.Breath);
            MouthOpen = Weight * (recovery ? MouthBreath : MouthEffort) * (clock.Breath + 1f) / 2f;
        }

        // Restore only values still equal to our last output; fresh Animator values win.
        void ReadBase()
        {
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                if (written && Quaternion.Angle(b.localRotation, writtenRot[i]) < AngleEpsilon) b.localRotation = beforeRot[i];
                if (written && (b.localPosition - writtenPos[i]).sqrMagnitude < PositionEpsilon * PositionEpsilon) b.localPosition = beforePos[i];
                beforeRot[i] = b.localRotation; beforePos[i] = b.localPosition;
            }
            written = false;
        }

        static float Envelope(float t, float duration)
            => Mathf.SmoothStep(0f, 1f, t / BlendSeconds) * Mathf.SmoothStep(0f, 1f, (duration - t) / BlendSeconds);
        static float Pulse(float t, float duration) { float s = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration)); return s * s; }
        static void Rotate(Transform b, Vector3 axis, float degrees)
        { if (b != null) b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation; }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadBase(); if (Weight <= 0f) return;
            var root = anim.transform;
            var facing = Quaternion.Euler(0f, root.eulerAngles.y, 0f);
            var f = facing * Vector3.forward; var r = facing * Vector3.right; var u = Vector3.up;
            var center = new Vector3(root.position.x, 0f, root.position.z);
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = neutral[i];
            float t = clock.Time, stretch = 0f, squat = 0f, lunge = 0f, step = 0f, lift = 0f, recovery = 0f, wipe = 0f, sideBend = 0f;
            int lead = 0;
            if (t < StretchTotal) { stretch = Envelope(t, StretchTotal); sideBend = Mathf.Sin(Tau * t / StretchSeconds) * stretch; }
            else if ((t -= StretchTotal) < SquatTotal) squat = Pulse(t % SquatSeconds, SquatSeconds);
            else if ((t -= SquatTotal) < LungeTotal)
            {
                lead = Mathf.FloorToInt(t / LungeSeconds) % 2;
                float beat = t % LungeSeconds;
                step = Mathf.SmoothStep(0f, 1f, beat / LungeStepSeconds) * Mathf.SmoothStep(0f, 1f, (LungeSeconds - beat) / LungeStepSeconds);
                if (beat < LungeStepSeconds) lift = StepLift * Mathf.Sin(Mathf.PI * beat / LungeStepSeconds);
                else if (beat > LungeSeconds - LungeStepSeconds) lift = StepLift * Mathf.Sin(Mathf.PI * (LungeSeconds - beat) / LungeStepSeconds);
                lunge = beat >= LungeStepSeconds && beat <= LungeSeconds - LungeStepSeconds
                    ? Pulse(beat - LungeStepSeconds, LungeSeconds - 2f * LungeStepSeconds) : 0f;
            }
            else
            {
                t -= LungeTotal; recovery = Envelope(t, CatchBreathSeconds + WipeSeconds);
                if (t >= CatchBreathSeconds) wipe = Envelope(t - CatchBreathSeconds, WipeSeconds);
            }
            hips.position = center + f * (LungeHipForward * step) + u * (standHeight - SquatDrop * squat - LungeDrop * lunge - RecoveryDrop * recovery);
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                var contact = center + r * (sign * StanceHalf) + (s == lead ? f * (LungeReach * step) + u * lift : Vector3.zero);
                var rot = facing * footFrame[s]; targets[s] = contact - rot * soleLocal[s];
            }
            // Lower the pelvis if either ankle would exceed its leg's reach; do not teleport bones or change bone lengths.
            float lowerBy = 0f;
            for (int s = 0; s < 2; s++)
            {
                var d = thigh[s].position - targets[s]; float max = legLength[s] - ReachMargin;
                float vertical = Mathf.Sqrt(Mathf.Max(0f, max * max - d.x * d.x - d.z * d.z));
                lowerBy = Mathf.Max(lowerBy, d.y - vertical);
            }
            hips.position -= u * lowerBy;
            float lean = SquatLean * squat + LungeLean * lunge + RecoveryLean * recovery;
            Rotate(spine, r, lean); Rotate(spine, f, StretchDegrees * sideBend);
            if (chest != spine) Rotate(chest, f, StretchDegrees * sideBend * ChestShare);
            Rotate(chest, r, BreathDegrees * clock.Breath);
            chest.position += u * (BreathRise * clock.Breath);
            Rotate(neck, r, lean * NeckCounter); Rotate(head, f, StretchDegrees * sideBend * NeckCounter);
            MaxFootError = 0f;
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                SofaRig.TwoBone(thigh[s], shin[s], foot[s], targets[s], thigh[s].position + f * KneeForward + r * (sign * KneeOut), 1f);
                foot[s].rotation = facing * footFrame[s];
                MaxFootError = Mathf.Max(MaxFootError, Vector3.Distance(foot[s].position, targets[s]));
            }
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                var target = thigh[s].position + r * (sign * RestHandSide) - u * RestHandDown;
                var frame = Quaternion.LookRotation(-u, -r * sign);
                var overhead = head.position + r * (sign * OverheadSide) + u * OverheadRise + f * OverheadForward;
                target = Vector3.Lerp(target, overhead, stretch);
                frame = Quaternion.Slerp(frame, Quaternion.LookRotation(u, -f), stretch);
                float forward = Mathf.Max(squat, lunge);
                target = Vector3.Lerp(target, chest.position + f * ForwardReach + r * (sign * ForwardHandSide) - u * ForwardHandDown, forward);
                frame = Quaternion.Slerp(frame, Quaternion.LookRotation(f, -u), forward);
                target = Vector3.Lerp(target, shin[s].position + u * KneeHandUp + f * KneeHandFront, recovery);
                frame = Quaternion.Slerp(frame, Quaternion.LookRotation(f, -u), recovery);
                if (s == 1 && wipe > 0f)
                {
                    float progress = Mathf.Clamp01((t - CatchBreathSeconds) / WipeSeconds);
                    var forehead = head.position + u * ForeheadRise + f * (ForeheadFront + WipeClearance) + r * Mathf.Lerp(-WipeTravel / 2f, WipeTravel / 2f, progress);
                    target = Vector3.Lerp(target, forehead, wipe);
                    frame = Quaternion.Slerp(frame, Quaternion.LookRotation(r, -f), wipe);
                }
                SofaRig.TwoBone(arm[s], forearm[s], hand[s], target, arm[s].position + r * (sign * ElbowOut) - u * ElbowDown + f * ElbowForward, 1f);
                hand[s].rotation = frame * handFrame[s];
            }
            float w = Mathf.SmoothStep(0f, 1f, Weight);
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i]; b.localRotation = Quaternion.Slerp(beforeRot[i], b.localRotation, w);
                b.localPosition = Vector3.Lerp(beforePos[i], b.localPosition, w);
                writtenRot[i] = b.localRotation; writtenPos[i] = b.localPosition;
            }
            written = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            if (written) ReadBase();
            disposed = true; Smile = EyesClosed = MouthOpen = 0f;
            SofaRig.Kill(Mat != null ? Mat.gameObject : null); Release(matMaterial);
        }
        static void Release(UnityEngine.Object obj)
        { if (obj != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); } }
    }
}
