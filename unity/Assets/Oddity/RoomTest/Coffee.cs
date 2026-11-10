using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Pure timing state, also usable by EditMode tests without a model or scene.
    public sealed class CoffeeTimeline
    {
        readonly System.Random random;
        int beat, cycle;
        bool wanted;
        public float Weight { get; private set; }
        public bool Active => wanted || Weight > 0;
        public float Time { get; private set; }
        public float Elapsed { get; private set; }
        public float Duration { get; private set; }
        public string Phase { get; private set; } = "Off";
        public bool LookingOut => wanted && Phase == "LookingOut";
        public float Lift { get; private set; }
        public float Look { get; private set; }
        public float Lean { get; private set; }
        public float Sigh { get; private set; }
        public float Blow { get; private set; }

        public CoffeeTimeline(int seed) { random = new System.Random(seed); }
        public void Start()
        {
            if (wanted) return;
            wanted = true; Time = Elapsed = 0; beat = cycle = 0; Enter(); Evaluate();
        }
        public void Stop() { wanted = false; }
        internal void Clear() { wanted = false; Weight = 0; Phase = "Off"; Lift = Look = Lean = Sigh = Blow = 0; }
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / CoffeeRig.BlendSeconds);
            if (!wanted) { if (Weight == 0) Clear(); return; }
            Time += dt;
            // Carry overshoot, so large time steps do not silently skip time or consume extra random draws.
            float remaining = dt;
            while (remaining >= Duration - Elapsed)
            {
                remaining -= Duration - Elapsed;
                Elapsed = 0;
                if (++beat > 10) { beat = 0; cycle++; }
                Enter();
            }
            Elapsed += remaining;
            Evaluate();
        }
        void Enter()
        {
            switch (beat)
            {
                case 0: Phase = "Blow"; Duration = CoffeeRig.BlowSeconds; break;
                case 1: Phase = "Raise"; Duration = CoffeeRig.RaiseSeconds; break;
                case 2: Phase = "Sip"; Duration = CoffeeRig.SipSeconds; break;
                case 3: Phase = "Lower"; Duration = CoffeeRig.LowerSeconds; break;
                case 4: case 10:
                    Phase = "Gaze";
                    Duration = CoffeeRig.GazeMin + (float)random.NextDouble() * (CoffeeRig.GazeMax - CoffeeRig.GazeMin); break;
                case 5: Phase = "Sigh"; Duration = CoffeeRig.SighSeconds; break;
                case 6:
                    Phase = cycle % CoffeeRig.LeanEveryCycles == 0 ? "Lean" : "Gaze";
                    Duration = CoffeeRig.LeanSeconds; break;
                case 7: Phase = "TurnOut"; Duration = CoffeeRig.LookTurnSeconds; break;
                case 8: Phase = "LookingOut"; Duration = CoffeeRig.LookHoldSeconds; break;
                default: Phase = "ReturnOut"; Duration = CoffeeRig.LookTurnSeconds; break;
            }
        }
        void Evaluate()
        {
            float p = Mathf.Clamp01(Elapsed / Duration);
            Lift = beat == 1 ? Smooth(p) : beat == 2 ? 1 : beat == 3 ? 1 - Smooth(p) : 0;
            Look = beat == 7 ? Smooth(p) : beat == 8 ? 1 : beat == 9 ? 1 - Smooth(p) : 0;
            // Squared sine gives zero speed at both ends of these small gestures.
            float pulse = Mathf.Pow(Mathf.Sin(Mathf.PI * p), 2);
            Lean = Phase == "Lean" ? pulse : 0;
            Sigh = Phase == "Sigh" ? pulse : 0;
            Blow = Phase == "Blow" ? pulse : 0;
        }
        internal static float Smooth(float t) => Mathf.SmoothStep(0, 1, t);
    }

    // Construct in the imported neutral pose before playing the base clip. Only Apply writes bones.
    public sealed class CoffeeRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Linear envelope in seconds; bone weights use smoothstep.
        public const float BlowSeconds = 2.4f, RaiseSeconds = .8f, SipSeconds = 1.2f, LowerSeconds = 1f; // Drink beats, seconds.
        public const float GazeMin = 4f, GazeMax = 7f, SighSeconds = 1.8f; // Rest and exhale lengths, seconds.
        public const float LeanSeconds = 3.4f, LookTurnSeconds = .6f, LookHoldSeconds = 2f; // Shoulder and street-look beats, seconds.
        public const int LeanEveryCycles = 2; // Lean once every two drinking cycles.
        public const float MugHeight = .085f, MugDiameter = .08f; // Body dimensions, metres, independent of avatar size.
        const int WallSegments = 32, HandleSegments = 16; // Primitive counts around the open mug and handle.
        const float WallThickness = .004f, BaseThickness = .005f; // Ceramic thickness, metres.
        const float HandleReach = .027f, HandleRadiusY = .024f, HandleTube = .008f; // Outer C handle ellipse and tube, metres.
        const float LiquidInset = .012f, LiquidThickness = .001f; // Coffee below rim, metres.
        const float PastelR = .70f, PastelG = .83f, PastelB = .82f, CeramicSmoothness = .5f; // Mint ceramic material.
        const float CoffeeR = .16f, CoffeeG = .085f, CoffeeB = .035f; // Coffee material.
        const float ChestForward = .24f, ChestUp = .025f; // Mug centre relative to chest, metres.
        const float BlowLift = .70f, BlowGap = .055f; // Near-mouth lift fraction and extra blowing clearance, metres.
        const float MouthUp = .018f, MouthForward = .075f; // Lip estimate in calibrated head frame, metres.
        const float SipTilt = 24f, SipHeadPitch = -7f, BlowHeadPitch = 12f; // Degrees; negative pitch raises chin.
        const float WristSide = .074f, WristDown = .035f, WristBack = .012f; // Wrists relative to mug, metres.
        const float GripSlant = .35f; // Finger direction rises this much while wrapping forward around mug.
        const float ElbowOut = .13f, ElbowDown = .27f, ElbowForward = .17f; // Elbow poles relative to upper arms, metres.
        const float CurlProximal = 38f, CurlIntermediate = 48f, CurlDistal = 26f, ThumbCurl = 22f; // Finger grip, degrees.
        const float GazeYaw = 16f, GazePitch = -2f, GazeHz = .045f; // Slow horizontal sweep, degrees and cycles/second.
        const float BreathDegrees = .6f, BreathHz = .23f, SwayDegrees = 1.3f, SwayHz = .10f; // Upper-body breath and weight suggestion.
        const float LeanRoll = -6f, LeanPitch = 4f, SighBow = 1.6f; // Right shoulder toward window, degrees.
        const float NeckShare = .4f, SipEyes = .5f, SighEyes = .72f; // Neck rotation fraction and eyelid values.
        const float SmileBase = .16f, SmileSigh = .38f, BlowMouth = .24f, SipMouth = .08f, SighMouth = .32f; // Face intensities.
        const float ReferenceShoulders = .30f, AnatomyMin = .75f, AnatomyMax = 1.3f; // Scale anatomical offsets only.
        const float StreetX = 1.5f, StreetY = 2.4f, StreetZ = -9f; // OneRoom peeping camera default, metres.
        const float RotationEpsilon = .001f, AxisEpsilon = .000001f; // Pose equality in degrees and vector degeneracy tolerance.
        const float Tau = 2f * Mathf.PI; // Radians per cycle.

        readonly Animator anim;
        readonly CoffeeTimeline clock;
        readonly Transform spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[,,] fingers = new Transform[2, 4, 3];
        readonly Transform[,] thumbs = new Transform[2, 3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly Vector3[] fingerLocal = new Vector3[2], palmLocal = new Vector3[2];
        readonly Quaternion headFrame;
        readonly List<Transform> controlled = new List<Transform>();
        readonly Quaternion[] before, written;
        readonly Material ceramic, coffee;
        readonly float anatomy;
        bool hasWritten, disposed;

        public Transform Mug { get; }
        public Vector3 AcrossStreetPoint = new Vector3(StreetX, StreetY, StreetZ); // Caller may move this world-space gaze point.
        public bool Active => !disposed && clock.Active;
        public float Weight => clock.Weight;
        public string Phase => clock.Phase;
        public bool LookingOut => !disposed && clock.LookingOut;
        public float Smile => Weight * (SmileBase + SmileSigh * clock.Sigh);
        public float EyesClosed => Weight * Mathf.Max(SipEyes * clock.Lift, SighEyes * clock.Sigh);
        public float MouthOpen => Weight * (BlowMouth * clock.Blow + SipMouth * clock.Lift + SighMouth * clock.Sigh);
        public Vector3 LipPoint => head.position + head.rotation * headFrame * (new Vector3(0, MouthUp, MouthForward) * anatomy);
        public Vector3 MugRimNear => Mug.TransformPoint(new Vector3(0, MugHeight / 2, -MugDiameter / 2));

        public CoffeeRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("CoffeeRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new CoffeeTimeline(seed);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (spine == null || head == null) throw new InvalidOperationException("CoffeeRig needs spine and head bones.");
            headFrame = Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            Track(spine); Track(chest); Track(neck); Track(head);
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[s] == null || lower[s] == null || hand[s] == null) throw new InvalidOperationException("CoffeeRig needs both arms and hands.");
                Track(upper[s]); Track(lower[s]); Track(hand[s]);
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                {
                    fingers[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j));
                    Track(fingers[s, f, j]);
                }
                for (int j = 0; j < 3; j++)
                {
                    thumbs[s, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + j));
                    Track(thumbs[s, j]);
                }
                Vector3 direction = fingers[s, 1, 0] != null ? fingers[s, 1, 0].position - hand[s].position : hand[s].position - lower[s].position;
                fingerLocal[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                // Neutral imported VRM has palms down, as in SofaRig and HairSweepRig.
                palmLocal[s] = Quaternion.Inverse(hand[s].rotation) * -anim.transform.up;
                handFrame[s] = Quaternion.LookRotation(fingerLocal[s], palmLocal[s]);
            }
            anatomy = Mathf.Clamp(Vector3.Distance(upper[0].position, upper[1].position) / ReferenceShoulders, AnatomyMin, AnatomyMax);
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("CoffeeRig needs URP Lit or Standard shader.");
            ceramic = new Material(shader); ceramic.color = new Color(PastelR, PastelG, PastelB); ceramic.SetFloat("_Smoothness", CeramicSmoothness);
            coffee = new Material(shader); coffee.color = new Color(CoffeeR, CoffeeG, CoffeeB);
            Mug = new GameObject("CoffeeMug").transform;
            Mug.SetParent(hand[1], false);
            BuildMug(); Mug.gameObject.SetActive(false);
        }
        Transform Bone(HumanBodyBones b) => anim.GetBoneTransform(b);
        void Track(Transform b) { if (b != null && !controlled.Contains(b)) controlled.Add(b); }
        public void Start() { if (disposed) throw new ObjectDisposedException(nameof(CoffeeRig)); clock.Start(); }
        public void Stop() { if (!disposed) clock.Stop(); }
        public void Tick(float dt) { if (!disposed) clock.Tick(dt); }

        void BuildMug()
        {
            float radius = MugDiameter / 2, middleRadius = radius - WallThickness / 2;
            float panelWidth = 2 * radius * Mathf.Tan(Mathf.PI / WallSegments);
            for (int i = 0; i < WallSegments; i++)
            {
                float a = i * Tau / WallSegments;
                var p = Part("ceramic_wall", PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(a) * middleRadius, 0, Mathf.Cos(a) * middleRadius),
                    new Vector3(panelWidth, MugHeight, WallThickness), ceramic);
                p.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
            }
            Part("base", PrimitiveType.Cylinder, new Vector3(0, -MugHeight / 2 + BaseThickness / 2, 0),
                new Vector3(MugDiameter, BaseThickness / 2, MugDiameter), ceramic);
            Part("coffee", PrimitiveType.Cylinder, new Vector3(0, MugHeight / 2 - LiquidInset, 0),
                new Vector3(MugDiameter - 2 * WallThickness, LiquidThickness / 2, MugDiameter - 2 * WallThickness), coffee);
            // C-shaped handle in the mug's x/y plane; ends meet the side wall. No hidden collider remains.
            for (int i = 0; i < HandleSegments; i++)
            {
                Vector3 a = HandlePoint(i * Mathf.PI / HandleSegments), b = HandlePoint((i + 1) * Mathf.PI / HandleSegments);
                var p = Part("handle", PrimitiveType.Cylinder, (a + b) / 2,
                    new Vector3(HandleTube, Vector3.Distance(a, b) / 2, HandleTube), ceramic);
                p.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            }
        }
        static Vector3 HandlePoint(float a) => new Vector3(MugDiameter / 2 - WallThickness + HandleReach * Mathf.Sin(a), HandleRadiusY * Mathf.Cos(a), 0);
        Transform Part(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
            g.transform.SetParent(Mug, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = material;
            return g.transform;
        }

        // Restore only unchanged overlay bones; preserve freshly sampled Animator values.
        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var b = controlled[i]; if (b == null) continue;
                if (hasWritten && Quaternion.Angle(b.localRotation, written[i]) < RotationEpsilon) b.localRotation = before[i];
                before[i] = b.localRotation;
            }
        }
        static void Rotate(Transform b, Vector3 axis, float angle)
        { if (b != null) b.rotation = Quaternion.AngleAxis(angle, axis) * b.rotation; }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose();
            float w = CoffeeTimeline.Smooth(Weight);
            Mug.gameObject.SetActive(w > 0);
            if (w > 0)
            {
                Vector3 up = anim.transform.up, right = anim.transform.right, forward = anim.transform.forward;
                float breath = Mathf.Sin(clock.Time * Tau * BreathHz);
                float sway = Mathf.Sin(clock.Time * Tau * SwayHz);
                Rotate(spine, forward, (sway * SwayDegrees + clock.Lean * LeanRoll) * w);
                Rotate(chest, right, (breath * BreathDegrees + clock.Lean * LeanPitch + clock.Sigh * SighBow) * w);
                float yaw = GazeYaw * Mathf.Sin(clock.Time * Tau * GazeHz);
                float pitch = Mathf.Lerp(GazePitch, SipHeadPitch, clock.Lift) + clock.Blow * BlowHeadPitch;
                Vector3 gaze = Quaternion.AngleAxis(yaw, up) * (Quaternion.AngleAxis(pitch, right) * forward);
                Vector3 street = AcrossStreetPoint - head.position;
                if (street.sqrMagnitude > AxisEpsilon) gaze = Vector3.Slerp(gaze, street.normalized, clock.Look);
                Quaternion correction = Quaternion.FromToRotation(head.rotation * headFrame * Vector3.forward, gaze);
                if (neck != null) RotateBy(neck, correction, w * NeckShare);
                RotateBy(head, correction, w * (neck != null ? 1 - NeckShare : 1));

                Quaternion mugRotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(-SipTilt * clock.Lift, 0, 0);
                Vector3 rest = chest.position + (forward * ChestForward + up * ChestUp) * anatomy;
                Vector3 rimOffset = mugRotation * new Vector3(0, MugHeight / 2, -MugDiameter / 2);
                Vector3 drink = LipPoint - rimOffset;
                Vector3 blowing = drink + forward * (BlowGap * anatomy);
                Vector3 centre = Vector3.Lerp(Vector3.Lerp(rest, blowing, clock.Blow * BlowLift), drink, clock.Lift);
                // Right hand drives the prop; left hand follows its actual pose, including during blending.
                Grip(centre, mugRotation, 1, out var wrist, out var rotation);
                Arm(1, wrist, rotation, up, right, forward, w);
                Mug.localPosition = Quaternion.Inverse(rotation) * (centre - wrist);
                Mug.localRotation = Quaternion.Inverse(rotation) * mugRotation;
                Grip(Mug.position, Mug.rotation, 0, out wrist, out rotation);
                Arm(0, wrist, rotation, up, right, forward, w);
            }
            for (int i = 0; i < controlled.Count; i++) if (controlled[i] != null) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }
        static void RotateBy(Transform b, Quaternion delta, float weight)
        { b.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * b.rotation; }
        void Grip(Vector3 centre, Quaternion frame, int side, out Vector3 wrist, out Quaternion rotation)
        {
            float sign = side == 0 ? -1 : 1;
            wrist = centre + frame * new Vector3(sign * WristSide, -WristDown, -WristBack);
            Vector3 fingerDirection = frame * new Vector3(0, GripSlant, 1).normalized;
            Vector3 palmDirection = frame * (Vector3.left * sign);
            rotation = Quaternion.LookRotation(fingerDirection, palmDirection) * Quaternion.Inverse(handFrame[side]);
        }
        void Arm(int side, Vector3 target, Quaternion rotation, Vector3 up, Vector3 right, Vector3 forward, float w)
        {
            float sign = side == 0 ? -1 : 1;
            Vector3 pole = upper[side].position + (right * (sign * ElbowOut) - up * ElbowDown + forward * ElbowForward) * anatomy;
            SofaRig.TwoBone(upper[side], lower[side], hand[side], target, pole, w);
            hand[side].rotation = Quaternion.Slerp(hand[side].rotation, rotation, w);
            Vector3 axis = hand[side].rotation * Vector3.Cross(fingerLocal[side], palmLocal[side]).normalized;
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                Rotate(fingers[side, f, j], axis, w * (j == 0 ? CurlProximal : j == 1 ? CurlIntermediate : CurlDistal));
            for (int j = 0; j < 3; j++) Rotate(thumbs[side, j], axis, w * ThumbCurl);
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
            disposed = true; clock.Clear();
            SofaRig.Kill(Mug != null ? Mug.gameObject : null);
            Release(ceramic); Release(coffee);
        }
        static void Release(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
