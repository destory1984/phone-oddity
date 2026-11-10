using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    public enum CatPlayMode { Pet, Wand }

    // Pure clock: usable in EditMode without a model or any Transform writes.
    public sealed class CatPlayTiming
    {
        public const float BlendSeconds = .4f; // Seconds for entry, exit and mode transitions.
        public const float StrokeSeconds = 1.2f, ReturnSeconds = .35f; // Head-to-tail contact and lifted return.
        public const float ReturnHeight = .07f; // Metres above the back during return.
        public const float FlickMin = .35f, FlickMax = .8f; // Seeded seconds per wand beat.
        const float FlickSide = .18f, FlickUp = .16f; // Wrist travel in metres.
        const float SmileBase = .45f, SmilePulse = .15f; // Petting smile, 0..1.
        const float BlinkPeriod = 3.7f, BlinkSeconds = .16f; // Blink interval and pulse duration.
        const float WandSmile = .55f, MouthAmount = .08f; // Wand expression amplitudes.
        readonly System.Random rng;
        bool wanted;
        float petTime, blinkTime, beatTime, beatLength;
        Vector3 from, to;
        int pattern;
        public CatPlayMode Mode = CatPlayMode.Pet;
        public bool Active => wanted || Weight > 0;
        public float Weight { get; private set; }
        public float WandWeight { get; private set; }
        public float PetProgress { get; private set; }
        public float PetLift { get; private set; }
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public Vector3 WandOffset { get; private set; }
        public string Phase { get; private set; } = "Off";
        public CatPlayTiming(int seed) { rng = new System.Random(seed); NextBeat(); }
        public void Start() { wanted = true; }
        public void Stop() { wanted = false; }
        void NextBeat()
        {
            from = to;
            pattern = rng.Next(3);
            beatLength = FlickMin + (float)rng.NextDouble() * (FlickMax - FlickMin);
            to = pattern == 0 ? Vector3.left * FlickSide : pattern == 1 ? Vector3.right * FlickSide : Vector3.up * FlickUp;
        }
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (!Active) { Phase = "Off"; Smile = EyesClosed = MouthOpen = 0; return; }
            WandWeight = Mathf.MoveTowards(WandWeight, Mode == CatPlayMode.Wand ? 1 : 0, dt / BlendSeconds);
            petTime = Mathf.Repeat(petTime + dt, StrokeSeconds + ReturnSeconds);
            bool returning = petTime >= StrokeSeconds;
            float p = returning ? (petTime - StrokeSeconds) / ReturnSeconds : petTime / StrokeSeconds;
            PetProgress = returning ? 1 - Mathf.SmoothStep(0, 1, p) : Mathf.SmoothStep(0, 1, p);
            PetLift = returning ? Mathf.Sin(Mathf.PI * p) * ReturnHeight : 0;
            beatTime += dt;
            while (beatTime >= beatLength) { beatTime -= beatLength; NextBeat(); }
            WandOffset = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, beatTime / beatLength));
            blinkTime = Mathf.Repeat(blinkTime + dt, BlinkPeriod);
            EyesClosed = (blinkTime < BlinkSeconds ? Mathf.Sin(Mathf.PI * blinkTime / BlinkSeconds) : 0) * Weight;
            Smile = Mathf.Lerp(SmileBase + SmilePulse * Mathf.Sin(Mathf.PI * PetProgress), WandSmile, WandWeight) * Weight;
            MouthOpen = MouthAmount * WandWeight * Mathf.Sin(Mathf.PI * beatTime / beatLength) * Weight;
            Phase = wanted ? (Mode == CatPlayMode.Pet ? (returning ? "PetReturn" : "PetStroke")
                : pattern == 0 ? "WandLeft" : pattern == 1 ? "WandRight" : "WandUp") : "BlendOut";
        }
    }

    // Construct in the imported T-pose, before the caller starts its base clip.
    // One Tick per frame; Apply belongs in LateUpdate after Animator and root placement.
    public sealed class CatPlayRig : IDisposable
    {
        public const float BlendSeconds = CatPlayTiming.BlendSeconds; // Entry and exit seconds.
        public const float SquatHeight = .32f, AnkleHeight = .08f; // Hip and ankle pivots above FloorY, metres.
        public const float StickLength = .6f, StringLength = .38f; // World-space metres, independent of model scale.
        const float FootHalfWidth = .17f, FootAhead = .08f, HipBack = .10f; // Squat support geometry, metres; HipBack is how far BEHIND the root the hips sit (positive = back).
        const float KneeAhead = .55f, KneeOut = .18f; // Knee pole relative to each hip, metres.
        const float HipFlex = 22f, SpineLean = 25f, ChestLean = 10f, GazeLimit = 65f, NeckShare = .3f, MaxHeadDown = 45f; // Degrees: the bend is split over hip joint 22 + spine 25 + chest 10 (was 62 + 24 on the waist), head/neck split, final nose limit.
        const float ContactGap = .014f, PalmLength = .055f; // Back-to-palm gap and wrist-to-palm centre, metres.
        const float LeftRestOut = .13f, LeftRestAhead = .25f, LeftRestDown = .04f; // Left wrist from hips, metres.
        const float HandAhead = .38f, HandSide = .10f, HandHeight = .53f; // Wand wrist relative to root/floor, metres.
        const float StickPitch = .52f, GripAhead = .025f; // Upward axis component and grip offset, metres.
        const float ElbowOut = .3f, ElbowDown = .16f, ElbowAhead = .20f; // Arm pole from shoulder, metres.
        const float CurlPet = 5f, CurlGrip = 65f, ThumbGrip = 30f; // Finger joint rotation degrees.
        const float StickWidth = .009f, StringWidth = .003f, FeatherWidth = .035f, FeatherLength = .10f; // Prop dimensions, metres.
        const float StickShade = .22f, FeatherRed = .8f, FeatherGreen = .3f, FeatherBlue = .55f; // Material RGB.
        const float Gravity = 9.81f, PhysicsStep = 1f / 120f, Drag = 1.2f; // Pendulum acceleration, maximum substep and drag per second.
        const float Epsilon = .000001f, RestoreAngle = .001f, VisibleWeight = .001f; // Degenerate and unchanged-pose tolerances.

        readonly Animator anim;
        readonly CatPlayTiming clock;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] arm = new Transform[2], forearm = new Transform[2], hand = new Transform[2];
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        readonly Transform[,,] fingers = new Transform[2, 5, 3];
        readonly Quaternion[] handFrame = new Quaternion[2], footFrame = new Quaternion[2];
        readonly Vector3[] palmLocal = new Vector3[2], fingerLocal = new Vector3[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly List<Quaternion> baseline = new List<Quaternion>(), written = new List<Quaternion>();
        readonly Quaternion hipsFrame, headFrame;
        readonly Material wood, featherMaterial;
        readonly Transform stringProp, feather;
        readonly Transform stick;
        Vector3 hipsBefore, hipsWritten, particle, previousParticle, anchor, previousAnchor;
        bool hasWritten, physicsReady, disposed;

        public float FloorY; // Caller supplies world floor top, including a rug if used.
        public Vector3 CatHead, CatTail; // Caller updates dorsal surface points in WORLD space every frame.
        public CatPlayMode Mode { get => clock.Mode; set => clock.Mode = value; }
        public bool Active => !disposed && clock.Active;
        public float Weight => disposed ? 0 : clock.Weight;
        public float Smile => disposed ? 0 : clock.Smile;
        public float EyesClosed => disposed ? 0 : clock.EyesClosed;
        public float MouthOpen => disposed ? 0 : clock.MouthOpen;
        public string Phase => disposed ? "Disposed" : clock.Phase;
        public Transform Wand { get; }
        public Vector3 WandBaseWorld { get; private set; }
        public Vector3 WandTipWorld { get; private set; }
        public Vector3 FeatherTipWorld { get; private set; } // Centre of the feather at the end of the string; valid after Apply.
        public Vector3 RightWristTarget { get; private set; }
        public float LastHeadDown { get; private set; } // Degrees the nose pointed below the horizon after the last Apply.
        public float TorsoLeanDegrees => HipFlex + SpineLean + ChestLean;

        public CatPlayRig(Animator anim, int seed, Quaternion? restHeadFrame = null)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("CatPlayRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new CatPlayTiming(seed);
            hips = Bone(HumanBodyBones.Hips); spine = Bone(HumanBodyBones.Spine);
            chest = Bone(HumanBodyBones.Chest) ?? spine; neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("CatPlayRig needs hips, spine and head.");
            hipsFrame = Quaternion.Inverse(anim.transform.rotation) * hips.rotation;
            headFrame = restHeadFrame ?? Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            for (int s = 0; s < 2; s++)
            {
                arm[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                forearm[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                thigh[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                shin[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                foot[s] = Bone(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (arm[s] == null || forearm[s] == null || hand[s] == null || thigh[s] == null || shin[s] == null || foot[s] == null)
                    throw new InvalidOperationException("CatPlayRig needs both complete arms and legs.");
                for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                    fingers[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + f * 3 + j));
                Vector3 direction = fingers[s, 2, 0] != null ? fingers[s, 2, 0].position - hand[s].position : hand[s].position - forearm[s].position;
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < Epsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                fingerLocal[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                palmLocal[s] = Quaternion.Inverse(hand[s].rotation) * palm;
                handFrame[s] = Quaternion.LookRotation(fingerLocal[s], palmLocal[s]);
                footFrame[s] = Quaternion.Inverse(anim.transform.rotation) * foot[s].rotation;
            }
            CatHead = anim.transform.position + anim.transform.forward * HandAhead + Vector3.up * (SquatHeight / 2);
            CatTail = CatHead + anim.transform.right * HandAhead;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No prop shader available.");
            wood = new Material(shader) { color = new Color(StickShade, StickShade, StickShade) };
            featherMaterial = new Material(shader) { color = new Color(FeatherRed, FeatherGreen, FeatherBlue) };
            // Hand-parented pivot; Apply compensates hierarchy scale to keep the stick 0.6 m.
            Wand = new GameObject("CatPlayWand").transform; Wand.SetParent(hand[1], false);
            stick = Part(Wand, "stick", PrimitiveType.Cylinder, wood);
            stringProp = Part(null, "CatPlayString", PrimitiveType.Cylinder, wood);
            feather = Part(null, "CatPlayFeather", PrimitiveType.Sphere, featherMaterial);
            HideProps();
        }

        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) { controlled.Add(b); baseline.Add(b.localRotation); written.Add(b.localRotation); }
            return b;
        }
        static Transform Part(Transform parent, string name, PrimitiveType type, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
            g.transform.SetParent(parent, false);
            g.GetComponent<Renderer>().sharedMaterial = material;
            return g.transform;
        }
        public void Start() { if (!disposed) clock.Start(); }
        public void Stop() { if (!disposed) clock.Stop(); }
        public void Tick(float dt)
        {
            if (disposed) return;
            clock.Tick(dt);
            if (!Active || !physicsReady) return;
            // Verlet particle under gravity, constrained to a moving anchor. No bone or prop writes.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / PhysicsStep));
            float h = dt / steps;
            if (h <= 0) return;
            for (int i = 0; i < steps; i++)
            {
                var pivot = Vector3.Lerp(previousAnchor, anchor, (i + 1f) / steps);
                var velocity = (particle - previousParticle) / PhysicsStep;
                // Stored history always represents one PhysicsStep, even for a fractional substep.
                var next = particle + velocity * (Mathf.Exp(-Drag * h) * h) + Vector3.down * (Gravity * h * h);
                var direction = next - pivot;
                if (direction.sqrMagnitude < Epsilon) direction = Vector3.down;
                next = pivot + direction.normalized * StringLength;
                previousParticle = next - (next - particle) * (PhysicsStep / h);
                particle = next;
            }
            previousAnchor = anchor;
        }

        void RestoreUnchanged()
        {
            if (!hasWritten) return;
            // Restore only values still equal to our last output; preserve a newly sampled Animator pose.
            for (int i = 0; i < controlled.Count; i++)
                if (controlled[i] != null && Quaternion.Angle(controlled[i].localRotation, written[i]) < RestoreAngle)
                    controlled[i].localRotation = baseline[i];
            if (hips != null && (hips.localPosition - hipsWritten).sqrMagnitude < Epsilon * Epsilon) hips.localPosition = hipsBefore;
            hasWritten = false;
        }
        public void Apply(float dt)
        {
            if (disposed) return;
            RestoreUnchanged();
            bool show = Active && clock.WandWeight > VisibleWeight;
            Wand.gameObject.SetActive(show); stringProp.gameObject.SetActive(show); feather.gameObject.SetActive(show);
            if (!Active) { physicsReady = false; return; }
            for (int i = 0; i < controlled.Count; i++) baseline[i] = controlled[i].localRotation;
            hipsBefore = hips.localPosition;
            float w = Mathf.SmoothStep(0, 1, Weight);
            var root = anim.transform;
            var forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < Epsilon) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var bodyFrame = Quaternion.LookRotation(forward, Vector3.up);
            var origin = new Vector3(root.position.x, FloorY, root.position.z);
            hips.position = Vector3.Lerp(hips.position, origin + Vector3.up * SquatHeight - forward * HipBack, w);
            hips.rotation = Quaternion.Slerp(hips.rotation, bodyFrame * hipsFrame, w);
            Turn(hips, right, HipFlex * w); // the lean starts at the hip joint; the legs are solved after it
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1 : 1;
                var target = origin + right * (sign * FootHalfWidth) + forward * FootAhead + Vector3.up * AnkleHeight;
                SofaRig.TwoBone(thigh[s], shin[s], foot[s], target, thigh[s].position + forward * KneeAhead + right * (sign * KneeOut), w);
                foot[s].rotation = Quaternion.Slerp(foot[s].rotation, bodyFrame * footFrame[s], w);
            }
            Turn(spine, right, SpineLean * w);
            if (chest != spine) Turn(chest, right, ChestLean * w);
            var petDirection = Vector3.ProjectOnPlane(CatTail - CatHead, Vector3.up).normalized;
            if (petDirection.sqrMagnitude < Epsilon) petDirection = right;
            var petPalm = Vector3.Lerp(CatHead, CatTail, clock.PetProgress) + Vector3.up * (ContactGap + clock.PetLift);
            var petWrist = petPalm - petDirection * PalmLength;
            var offset = clock.WandOffset;
            var wandWrist = origin + right * (HandSide + offset.x) + forward * HandAhead + Vector3.up * (HandHeight + offset.y);
            float mode = Mathf.SmoothStep(0, 1, clock.WandWeight);
            RightWristTarget = Vector3.Lerp(petWrist, wandWrist, mode);
            PoseHand(0, hips.position - right * LeftRestOut + forward * LeftRestAhead - Vector3.up * LeftRestDown, forward, Vector3.down, w, right, forward);
            var stickDirection = (forward + Vector3.up * StickPitch + right * offset.x).normalized;
            var gripFingers = Vector3.Cross(right, stickDirection).normalized;
            PoseHand(1, RightWristTarget, Vector3.Slerp(petDirection, gripFingers, mode),
                Vector3.Slerp(Vector3.down, stickDirection, mode), w, right, forward);
            Curl(0, CurlPet * w); Curl(1, Mathf.Lerp(CurlPet, CurlGrip, mode) * w);
            UpdateProps(stickDirection, show);
            var look = Vector3.Lerp(petPalm, FeatherTipWorld, mode) - head.position;
            if (look.sqrMagnitude > Epsilon)
            {
                var current = head.rotation * headFrame * Vector3.forward;
                var delta = Quaternion.FromToRotation(current, look.normalized);
                float angle = Quaternion.Angle(Quaternion.identity, delta);
                delta = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Min(1, GazeLimit / Mathf.Max(Epsilon, angle)) * w);
                if (neck != null) neck.rotation = Quaternion.Slerp(Quaternion.identity, delta, NeckShare) * neck.rotation;
                head.rotation = Quaternion.Slerp(Quaternion.identity, delta, neck != null ? 1 - NeckShare : 1) * head.rotation;
            }
            CoffeeRig.ClampNose(head, headFrame * Vector3.forward, MaxHeadDown, right); // the real nose never points more than 45 degrees below the horizon
            LastHeadDown = -Mathf.Asin(Mathf.Clamp((head.rotation * headFrame * Vector3.forward).y, -1, 1)) * Mathf.Rad2Deg;
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hipsWritten = hips.localPosition; hasWritten = true;
        }

        static void Turn(Transform bone, Vector3 axis, float degrees) { if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }
        void PoseHand(int side, Vector3 target, Vector3 direction, Vector3 palm, float w, Vector3 right, Vector3 forward)
        {
            SofaRig.TwoBone(arm[side], forearm[side], hand[side], target,
                arm[side].position + right * (side == 0 ? -ElbowOut : ElbowOut) - Vector3.up * ElbowDown + forward * ElbowAhead, w);
            palm = Vector3.ProjectOnPlane(palm, direction).normalized;
            if (palm.sqrMagnitude < Epsilon) palm = Vector3.ProjectOnPlane(right, direction).normalized;
            hand[side].rotation = Quaternion.Slerp(hand[side].rotation, Quaternion.LookRotation(direction, palm) * Quaternion.Inverse(handFrame[side]), w);
        }
        void Curl(int side, float degrees)
        {
            // SofaRig.Curl is private. Rotate toward the measured palm about Cross(fingers,palm).
            var axis = Vector3.Cross(hand[side].rotation * fingerLocal[side], hand[side].rotation * palmLocal[side]).normalized;
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                Turn(fingers[side, f, j], axis, f == 0 ? degrees * ThumbGrip / CurlGrip : degrees);
        }
        void UpdateProps(Vector3 direction, bool show)
        {
            Wand.position = hand[1].position + direction * GripAhead;
            Wand.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            var scale = hand[1].lossyScale;
            Wand.localScale = new Vector3(1 / Mathf.Max(Epsilon, Mathf.Abs(scale.x)), 1 / Mathf.Max(Epsilon, Mathf.Abs(scale.y)), 1 / Mathf.Max(Epsilon, Mathf.Abs(scale.z)));
            stick.localPosition = Vector3.up * (StickLength / 2);
            stick.localRotation = Quaternion.identity;
            stick.localScale = new Vector3(StickWidth, StickLength / 2, StickWidth);
            WandBaseWorld = Wand.position;
            WandTipWorld = WandBaseWorld + direction * StickLength;
            anchor = WandTipWorld;
            if (!physicsReady || !show)
            {
                particle = previousParticle = anchor + Vector3.down * StringLength;
                previousAnchor = anchor; physicsReady = show;
            }
            var d = particle - anchor;
            if (d.sqrMagnitude < Epsilon) d = Vector3.down;
            FeatherTipWorld = anchor + d.normalized * StringLength;
            stringProp.position = (anchor + FeatherTipWorld) / 2;
            stringProp.rotation = Quaternion.FromToRotation(Vector3.up, FeatherTipWorld - anchor);
            stringProp.localScale = new Vector3(StringWidth, StringLength / 2, StringWidth);
            feather.position = FeatherTipWorld;
            feather.rotation = stringProp.rotation;
            feather.localScale = new Vector3(FeatherWidth, FeatherLength, FeatherWidth);
        }
        void HideProps()
        {
            if (Wand != null) Wand.gameObject.SetActive(false);
            if (stringProp != null) stringProp.gameObject.SetActive(false);
            if (feather != null) feather.gameObject.SetActive(false);
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
            try { RestoreUnchanged(); HideProps(); } catch (System.Exception) { } disposed = true;
            SofaRig.Kill(Wand != null ? Wand.gameObject : null);
            SofaRig.Kill(stringProp != null ? stringProp.gameObject : null);
            SofaRig.Kill(feather != null ? feather.gameObject : null);
            if (Application.isPlaying) { UnityEngine.Object.Destroy(wood); UnityEngine.Object.Destroy(featherMaterial); }
            else { UnityEngine.Object.DestroyImmediate(wood); UnityEngine.Object.DestroyImmediate(featherMaterial); }
        }
    }
}
