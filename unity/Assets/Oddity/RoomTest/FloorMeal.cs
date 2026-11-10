using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Pure time/state data; usable by EditMode tests without loading a VRM.
    public sealed class FloorMealTimeline
    {
        public const float BlendSeconds = 1.4f; // Seconds for the complete overlay to enter or leave (a slow sit-down and stand-up).
        const float GatherSeconds = 1.8f, LiftSeconds = 2f, SlurpSeconds = 2.4f; // Eating beats, seconds.
        const float ChewSeconds = 2.2f, LowerSeconds = 1.2f; // Settle and return the bowl, seconds.
        const float PhoneSeconds = 3f, LaughSeconds = 1.6f, ReturnSeconds = 1.2f; // Phone reaction beats, seconds.
        const float DurationJitter = .15f; // Seeded duration variation as a fraction of each beat.
        const float ChinChance = .65f; // Chance of lifting the bowl on later bites; first bite always does.
        const int MinBites = 2, MaxBites = 3; // Bites between phone checks, inclusive.
        const float AttentionRamp = .7f; // Seconds spent turning toward and away from the phone.
        const float SlurpMouth = .65f, ChewMouth = .12f, LaughMouth = .6f; // Face blend amplitudes.
        const float LaughSmile = .9f, PhoneSmile = .15f, LaughEyes = .8f; // Face blend amplitudes.
        const float ChewHz = 1.7f, LaughHz = 3.5f, MouthFloor = .35f; // Facial pulse rates and minimum laugh openness.
        const float Tau = 2f * Mathf.PI; // Radians per cycle.
        static readonly string[] Names = { "Gathering", "Lifting", "Slurping", "Chewing", "Lowering", "Phone", "Laughing", "Returning" };
        static readonly float[] Durations = { GatherSeconds, LiftSeconds, SlurpSeconds, ChewSeconds, LowerSeconds, PhoneSeconds, LaughSeconds, ReturnSeconds };
        readonly System.Random rng;
        bool wanted, chin;
        int beat, bites, bitesUntilPhone;
        float elapsed, duration;

        public float Weight { get; private set; }
        public bool Active => wanted || Weight > 0;
        public string Phase => Active ? Names[beat] : "Stopped";
        public float Progress => duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0;
        public float Time { get; private set; }
        public float BowlLift { get; private set; }
        public float PhoneAttention { get; private set; }
        public float Laugh { get; private set; }
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }

        public FloorMealTimeline(int seed) { rng = new System.Random(seed); }
        public void Start()
        {
            if (wanted) return;
            wanted = true;
            // Restart while fading out resumes the held beat, avoiding a jump in pose.
            if (Weight > 0) return;
            beat = bites = 0; Time = elapsed = 0; chin = true;
            bitesUntilPhone = rng.Next(MinBites, MaxBites + 1);
            NewDuration(); Sample();
        }
        public void Stop() { wanted = false; }
        void NewDuration() => duration = Durations[beat] * (1 + ((float)rng.NextDouble() * 2 - 1) * DurationJitter);
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) return;
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (wanted)
            {
                Time += dt; elapsed += dt;
                while (elapsed >= duration)
                {
                    elapsed -= duration;
                    if (beat == 4)
                    {
                        bites++;
                        if (bites >= bitesUntilPhone) beat = 5;
                        else { beat = 0; chin = rng.NextDouble() < ChinChance; }
                    }
                    else if (beat == 7)
                    {
                        beat = bites = 0; chin = rng.NextDouble() < ChinChance;
                        bitesUntilPhone = rng.Next(MinBites, MaxBites + 1);
                    }
                    else beat++;
                    NewDuration();
                }
            }
            Sample();
        }
        void Sample()
        {
            float p = Mathf.SmoothStep(0, 1, Progress);
            BowlLift = chin ? (beat == 1 ? p : beat == 2 || beat == 3 ? 1 : beat == 4 ? 1 - p : 0) : 0;
            PhoneAttention = beat == 5 ? Mathf.SmoothStep(0, 1, elapsed / AttentionRamp) :
                beat == 6 ? 1 : beat == 7 ? 1 - Mathf.SmoothStep(0, 1, elapsed / AttentionRamp) : 0;
            Laugh = beat == 6 ? Mathf.Sin(Mathf.PI * Progress) : 0;
            Smile = Weight * (PhoneSmile * PhoneAttention + (LaughSmile - PhoneSmile) * Laugh);
            EyesClosed = Weight * LaughEyes * Laugh;
            MouthOpen = Weight * (beat == 2 ? SlurpMouth : beat == 3 ? ChewMouth * Mathf.Abs(Mathf.Sin(Time * Tau * ChewHz)) :
                LaughMouth * Laugh * (MouthFloor + (1 - MouthFloor) * Mathf.Abs(Mathf.Sin(Time * Tau * LaughHz))));
        }
    }

    // Construct in the model's upright reference pose. Tick once, then Apply in LateUpdate after the Animator.
    public sealed class FloorMealRig : IDisposable
    {
        public const float BlendSeconds = FloorMealTimeline.BlendSeconds; // Seconds for blend in/out.
        public const float HipHeight = .06f; // Hips pivot above the floor at the cushion centre, metres.
        public const float TableWidth = .6f, TableDepth = .4f, TableHeight = .30f; // Folding-table dimensions, metres.
        public const float TableAhead = .52f, TableThickness = .025f; // Table centre ahead of hips and slab thickness, metres.
        public const float CushionWidth = .72f, CushionDepth = .60f, CushionHeight = .06f; // Cushion dimensions, metres.
        public const float BowlRadius = .075f, BowlHeight = .075f, BowlAhead = .43f, BowlSide = -.025f; // Bowl dimensions and rest centre, metres.
        public const float PhoneSide = -.18f, PhoneAhead = .60f, PhoneTilt = 22f; // Propped phone x/z and back tilt, degrees.
        public static readonly Vector3 RecommendedSeat = new Vector3(1.65f, 0, 2.30f); // Clear rug location in OneRoom, metres.
        public const float RecommendedYaw = 180f; // Face the south window; local +z always faces the table.
        const float FootCross = .14f, FootAhead = .23f, FootHeight = .09f, AnkleStack = .055f; // Crossed ankle targets, metres.
        const float KneeOut = .65f, KneeAhead = .25f, KneeHeight = .10f; // Wide horizontal knee poles, metres.
        const float FootToeAhead = .35f; // Forward component of the outward-facing feet.
        const float SpineLean = 7f, NeckLean = 9f, HeadLean = 14f, PhoneYaw = -15f; // Forward meal posture and phone turn, degrees.
        const float PhoneBow = 8f, LaughNod = 3f, LaughHz = 3.5f, BreathDegrees = .7f, BreathHz = .23f; // Secondary torso/head motion.
        const float MouthDown = .025f, MouthAhead = .075f; // Mouth estimate from head pivot, metres; check per VRM.
        const float ChinBowlDown = .13f, ChinBowlAhead = .07f; // Held bowl centre below/in front of the mouth, metres.
        const float LeftGripOut = .085f, LeftGripDown = .015f; // Left wrist relative to bowl centre, metres.
        const float GatherOut = .13f, GatherUp = .10f, GatherBack = .04f; // Right wrist relative to bowl, metres.
        const float HighOut = .14f, HighUp = .26f, HighAhead = .07f; // High noodle lift relative to mouth, metres.
        const float SlurpOut = .12f, SlurpUp = .045f, SlurpAhead = .08f; // Right wrist beside the mouth, metres.
        const float SlurpApproach = .55f; // Fraction of slurp spent approaching the mouth.
        const float ElbowOut = .3f, ElbowDown = .22f, ElbowAhead = .18f; // Arm IK poles from shoulder, metres.
        const float HandGripAhead = .035f, StickLength = .23f, StickBehindGrip = .055f, StickGap = .012f, StickRadius = .003f; // Chopstick dimensions and grip, metres.
        const float PinchDegrees = 3f, PinchHz = 1.6f; // Upper chopstick movement while gathering.
        const float CurlProximal = 24f, CurlIntermediate = 32f, CurlDistal = 18f, ThumbCurl = 20f; // Finger joint grip, degrees.
        const float BowlWall = .008f, BowlBase = .012f, BowlRim = .006f; // Open bowl wall, bottom and lip thickness, metres.
        const int BowlPanels = 20; // Primitive panels around the open bowl.
        const float TableLegInset = .04f, TableLegThickness = .018f, LegFoldAngle = 12f; // Inset, thickness and folding-leg splay.
        const float PhoneClearance = .004f, PhoneSupport = .035f; // Phone bottom gap and stand depth, metres.
        const int NoodleStrands = 3, NoodleSegments = 5; // Small cylinder chain count; no per-frame allocations.
        const float NoodleRadius = .002f, NoodleSpread = .006f, NoodleWave = .008f, NoodleHz = 1.2f; // Strand thickness, spacing and sway, metres/Hz.
        const float NoodleStart = .08f, NoodleEnd = .96f, MinSegment = .001f; // Visibility cutoffs in a beat and minimum rendered length.
        const float RotationEpsilon = .001f, PositionEpsilon = .00001f, AxisEpsilon = .000001f; // Pose/axis comparison tolerances.
        const float Tau = 2 * Mathf.PI; // Radians per cycle.
        static readonly Color Wood = new Color(.65f, .47f, .31f), Fabric = new Color(.48f, .56f, .53f); // Prop colours.
        static readonly Color Ceramic = new Color(.86f, .88f, .80f), Metal = new Color(.22f, .23f, .24f); // Prop colours.
        static readonly Color PhoneColor = new Color(.045f, .05f, .06f), ScreenColor = new Color(.28f, .44f, .52f), NoodleColor = new Color(.94f, .81f, .5f); // Prop colours.

        readonly Animator anim;
        readonly FloorMealTimeline clock;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 5, 3];
        readonly Quaternion[] handFrame = new Quaternion[2], footFrame = new Quaternion[2];
        readonly Vector3[] fingerAxis = new Vector3[2], palmAxis = new Vector3[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        readonly Quaternion[] before, written;
        readonly Transform[,] strands = new Transform[NoodleStrands, NoodleSegments];
        readonly Transform movingStick;
        Vector3 hipBefore, hipWritten;
        bool hasWritten, disposed;

        // Caller places this unit-scale frame at floor level and rotates it to face the table.
        // All generated world props belong to it; Dispose removes them. Do not place the bowl independently.
        public readonly Transform Props, Table, Cushion, Bowl, Phone, Chopsticks, Noodles;
        public bool Active => !disposed && clock.Active;
        public float Weight => disposed ? 0 : clock.Weight;
        public string Phase => disposed ? "Stopped" : clock.Phase;
        public float Smile => disposed ? 0 : clock.Smile;
        public float EyesClosed => disposed ? 0 : clock.EyesClosed;
        public float MouthOpen => disposed ? 0 : clock.MouthOpen;
        public Vector3 RightWristTarget { get; private set; }

        public FloorMealRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("FloorMeal needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new FloorMealTimeline(seed);
            hips = Bone(HumanBodyBones.Hips); spine = Bone(HumanBodyBones.Spine);
            chest = Bone(HumanBodyBones.Chest); neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("FloorMeal needs hips, spine and head.");
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                thigh[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                shin[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                foot[s] = Bone(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (upper[s] == null || lower[s] == null || hand[s] == null || thigh[s] == null || shin[s] == null || foot[s] == null)
                    throw new InvalidOperationException("FloorMeal needs both arms and legs.");
                for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                    finger[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + f * 3 + j));
                var direction = finger[s, 2, 0] != null ? finger[s, 2, 0].position - hand[s].position : hand[s].position - lower[s].position;
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < AxisEpsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                fingerAxis[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                palmAxis[s] = Quaternion.Inverse(hand[s].rotation) * palm;
                handFrame[s] = Quaternion.LookRotation(fingerAxis[s], palmAxis[s]);
                footFrame[s] = Quaternion.Inverse(foot[s].rotation) * anim.transform.rotation;
            }
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No FloorMeal material shader found.");
            var wood = MaterialFor(shader, Wood); var cloth = MaterialFor(shader, Fabric);
            var ceramic = MaterialFor(shader, Ceramic); var metal = MaterialFor(shader, Metal);
            var phoneMat = MaterialFor(shader, PhoneColor); var screen = MaterialFor(shader, ScreenColor);
            var noodleMat = MaterialFor(shader, NoodleColor);
            Props = new GameObject("FloorMealProps").transform;
            Props.SetPositionAndRotation(RecommendedSeat, Quaternion.Euler(0, RecommendedYaw, 0));
            Cushion = Part(Props, "cushion", PrimitiveType.Sphere, new Vector3(0, CushionHeight / 2, 0),
                new Vector3(CushionWidth, CushionHeight, CushionDepth), cloth);
            Table = Part(Props, "folding_table_top", PrimitiveType.Cube, new Vector3(0, TableHeight - TableThickness / 2, TableAhead),
                new Vector3(TableWidth, TableThickness, TableDepth), wood);
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
            {
                var leg = Part(Props, "folding_leg", PrimitiveType.Cube,
                    new Vector3(x * (TableWidth / 2 - TableLegInset), (TableHeight - TableThickness) / 2, TableAhead + z * (TableDepth / 2 - TableLegInset)),
                    new Vector3(TableLegThickness, (TableHeight - TableThickness) / Mathf.Cos(LegFoldAngle * Mathf.Deg2Rad), TableLegThickness), metal);
                leg.localRotation = Quaternion.Euler(z * LegFoldAngle, 0, 0);
            }
            Bowl = new GameObject("FloorMealBowl").transform; Bowl.SetParent(Props, false);
            Bowl.localPosition = BowlRestLocal;
            Part(Bowl, "bottom", PrimitiveType.Cylinder, new Vector3(0, -BowlHeight / 2 + BowlBase / 2, 0),
                new Vector3(BowlRadius * 2, BowlBase / 2, BowlRadius * 2), ceramic);
            for (int i = 0; i < BowlPanels; i++)
            {
                float a = Tau * i / BowlPanels;
                var panel = Part(Bowl, "bowl_wall", PrimitiveType.Cube, new Vector3(Mathf.Sin(a) * BowlRadius, 0, Mathf.Cos(a) * BowlRadius),
                    new Vector3(2 * BowlRadius * Mathf.Tan(Mathf.PI / BowlPanels) + BowlRim, BowlHeight, BowlWall), ceramic);
                panel.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
            }
            Part(Bowl, "noodles_in_bowl", PrimitiveType.Cylinder, new Vector3(0, BowlHeight / 2 - BowlBase, 0),
                new Vector3((BowlRadius - BowlWall) * 2, BowlRim / 2, (BowlRadius - BowlWall) * 2), noodleMat);
            Phone = new GameObject("FloorMealPhone").transform; Phone.SetParent(Props, false);
            Phone.localRotation = Quaternion.Euler(PhoneTilt, 0, 0);
            Phone.localPosition = new Vector3(PhoneSide, TableHeight + PhoneClearance +
                (SofaGeometry.PhoneH * Mathf.Cos(PhoneTilt * Mathf.Deg2Rad) + SofaGeometry.PhoneT * Mathf.Sin(PhoneTilt * Mathf.Deg2Rad)) / 2, PhoneAhead);
            Part(Phone, "body", PrimitiveType.Cube, Vector3.zero, new Vector3(SofaGeometry.PhoneW, SofaGeometry.PhoneH, SofaGeometry.PhoneT), phoneMat);
            Part(Phone, "screen", PrimitiveType.Cube, new Vector3(0, 0, -SofaGeometry.PhoneT / 2),
                new Vector3(SofaGeometry.PhoneW - PhoneClearance * 2, SofaGeometry.PhoneH - PhoneClearance * 2, PhoneClearance), screen);
            Part(Props, "phone_stand", PrimitiveType.Cube, new Vector3(PhoneSide, TableHeight + PhoneSupport / 2, PhoneAhead + PhoneSupport),
                new Vector3(SofaGeometry.PhoneW, PhoneSupport, PhoneSupport), metal);
            Chopsticks = new GameObject("FloorMealChopsticks").transform;
            Chopsticks.SetParent(hand[1], false);
            for (int i = 0; i < 2; i++)
            {
                var stick = Part(Chopsticks, "chopstick", PrimitiveType.Cylinder,
                    new Vector3((i == 0 ? -1 : 1) * StickGap / 2, 0, StickLength / 2 - StickBehindGrip),
                    new Vector3(StickRadius * 2, StickLength / 2, StickRadius * 2), wood);
                stick.localRotation = Quaternion.Euler(90, 0, 0);
                if (i == 1) movingStick = stick;
            }
            Noodles = new GameObject("FloorMealNoodles").transform; Noodles.SetParent(Props, false);
            for (int s = 0; s < NoodleStrands; s++) for (int j = 0; j < NoodleSegments; j++)
                strands[s, j] = Part(Noodles, "noodle", PrimitiveType.Cylinder, Vector3.zero, Vector3.one * NoodleRadius, noodleMat);
            Chopsticks.gameObject.SetActive(false); Noodles.gameObject.SetActive(false);
        }

        Vector3 BowlRestLocal => new Vector3(BowlSide, TableHeight + BowlHeight / 2, BowlAhead);
        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) controlled.Add(b);
            return b;
        }
        Material MaterialFor(Shader shader, Color color)
        {
            var m = new Material(shader) { color = color }; materials.Add(m); return m;
        }
        static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var c = g.GetComponent<Collider>(); if (c != null) { c.enabled = false; Release(c); }
            g.transform.SetParent(parent, false); g.transform.localPosition = position; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = material;
            return g.transform;
        }
        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        static void Rotate(Transform b, Vector3 axis, float degrees)
        { if (b != null) b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation; }
        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FloorMealRig));
            clock.Start();
        }
        public void Stop() { if (!disposed) clock.Stop(); }
        public void Tick(float dt) { if (!disposed) clock.Tick(dt); }

        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var b = controlled[i];
                if (hasWritten && Quaternion.Angle(b.localRotation, written[i]) < RotationEpsilon) b.localRotation = before[i];
                before[i] = b.localRotation;
            }
            if (hasWritten && Vector3.Distance(hips.localPosition, hipWritten) < PositionEpsilon) hips.localPosition = hipBefore;
            hipBefore = hips.localPosition;
        }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose();
            float w = Mathf.SmoothStep(0, 1, Weight);
            Chopsticks.gameObject.SetActive(w > 0); Noodles.gameObject.SetActive(false);
            var right = Props.right; var up = Props.up; var forward = Props.forward;
            var rest = Props.TransformPoint(BowlRestLocal);
            Bowl.SetPositionAndRotation(rest, Props.rotation);
            if (w > 0)
            {
                hips.position = Vector3.Lerp(hips.position, Props.TransformPoint(new Vector3(0, HipHeight, 0)), w);
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? -1 : 1;
                    var target = Props.TransformPoint(new Vector3(-sign * FootCross, FootHeight + (s == 0 ? AnkleStack : 0), FootAhead));
                    var pole = Props.TransformPoint(new Vector3(sign * KneeOut, KneeHeight, KneeAhead));
                    SofaRig.TwoBone(thigh[s], shin[s], foot[s], target, pole, w);
                    var orientation = Quaternion.LookRotation((right * -sign + forward * FootToeAhead).normalized, up) * Quaternion.Inverse(footFrame[s]);
                    foot[s].rotation = Quaternion.Slerp(foot[s].rotation, orientation, w);
                }
                float bob = clock.Laugh * Mathf.Sin(clock.Time * Tau * LaughHz);
                Rotate(spine, right, (SpineLean + BreathDegrees * Mathf.Sin(clock.Time * Tau * BreathHz)) * w);
                Rotate(chest, right, LaughNod * bob * w);
                Rotate(neck ?? head, right, NeckLean * w);
                Rotate(head, right, (HeadLean + PhoneBow * clock.PhoneAttention - LaughNod * bob) * w);
                Rotate(head, up, PhoneYaw * clock.PhoneAttention * w);
                var mouth = head.position + up * -MouthDown + forward * MouthAhead;
                float held = clock.BowlLift * w;
                var bowlWanted = Vector3.Lerp(rest, mouth - up * ChinBowlDown + forward * ChinBowlAhead, held);
                var leftWrist = bowlWanted - right * LeftGripOut - up * LeftGripDown;
                Arm(0, leftWrist, forward, up, w);
                // A held bowl follows the actual hand even if a small avatar cannot reach the ideal target.
                Bowl.position = bowlWanted + (hand[0].position - leftWrist) * held;
                var gather = Bowl.position + right * GatherOut + up * GatherUp - forward * GatherBack;
                var high = mouth + right * HighOut + up * HighUp + forward * HighAhead;
                var slurp = mouth + right * SlurpOut + up * SlurpUp + forward * SlurpAhead;
                float p = Mathf.SmoothStep(0, 1, clock.Progress);
                var tipAim = Bowl.position + up * (BowlHeight / 2 - BowlBase);
                RightWristTarget = gather;
                if (Phase == "Lifting")
                {
                    RightWristTarget = Vector3.Lerp(gather, high, p);
                    tipAim = Vector3.Lerp(tipAim, mouth + up * HighUp, p);
                }
                else if (Phase == "Slurping")
                {
                    float approach = Mathf.SmoothStep(0, 1, clock.Progress / SlurpApproach);
                    RightWristTarget = Vector3.Lerp(high, slurp, approach);
                    tipAim = Vector3.Lerp(mouth + up * HighUp, mouth, approach);
                }
                else if (Phase == "Chewing")
                {
                    RightWristTarget = Vector3.Lerp(slurp, gather, p);
                    tipAim = Vector3.Lerp(mouth, tipAim, p);
                }
                Arm(1, RightWristTarget, forward, -right, w);
                // Scale compensation keeps primitive sticks in metres under scaled VRM bones.
                var scale = hand[1].lossyScale;
                Chopsticks.localScale = new Vector3(1 / Mathf.Max(Mathf.Abs(scale.x), PositionEpsilon),
                    1 / Mathf.Max(Mathf.Abs(scale.y), PositionEpsilon), 1 / Mathf.Max(Mathf.Abs(scale.z), PositionEpsilon));
                var grip = hand[1].position + forward * HandGripAhead;
                var direction = tipAim - grip;
                if (direction.sqrMagnitude < AxisEpsilon) direction = forward;
                var stickUp = Vector3.ProjectOnPlane(up, direction).normalized;
                if (stickUp.sqrMagnitude < AxisEpsilon) stickUp = right;
                Chopsticks.SetPositionAndRotation(grip, Quaternion.LookRotation(direction, stickUp));
                movingStick.localRotation = Quaternion.Euler(90, Phase == "Gathering" ? PinchDegrees * Mathf.Sin(clock.Time * Tau * PinchHz) : 0, 0);
                DrawNoodles(mouth, right, up, w);
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hipWritten = hips.localPosition; hasWritten = true;
        }

        void Arm(int side, Vector3 target, Vector3 fingers, Vector3 palm, float w)
        {
            SofaRig.TwoBone(upper[side], lower[side], hand[side], target,
                upper[side].position + Props.right * (side == 0 ? -ElbowOut : ElbowOut) - Props.up * ElbowDown + Props.forward * ElbowAhead, w);
            hand[side].rotation = Quaternion.Slerp(hand[side].rotation,
                Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(handFrame[side]), w);
            var axis = hand[side].rotation * Vector3.Cross(fingerAxis[side], palmAxis[side]).normalized;
            // SofaRig's Curl is private; preserve its calibrated palm-axis convention here.
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                Rotate(finger[side, f, j], axis, (f == 0 ? ThumbCurl : j == 0 ? CurlProximal : j == 1 ? CurlIntermediate : CurlDistal) * w);
        }

        void DrawNoodles(Vector3 mouth, Vector3 right, Vector3 up, float w)
        {
            bool lifting = Phase == "Lifting" && clock.Progress > NoodleStart;
            bool slurping = Phase == "Slurping" && clock.Progress < NoodleEnd;
            Noodles.gameObject.SetActive((lifting || slurping) && w > 0);
            if (!lifting && !slurping) return;
            var tip = Chopsticks.position + Chopsticks.forward * (StickLength - StickBehindGrip);
            float swallow = slurping ? Mathf.SmoothStep(0, 1, clock.Progress / SlurpApproach) : 0;
            var end = Vector3.Lerp(Bowl.position + up * BowlHeight / 2, mouth, swallow);
            for (int s = 0; s < NoodleStrands; s++) for (int j = 0; j < NoodleSegments; j++)
            {
                var a = NoodlePoint(tip, end, right, (float)j / NoodleSegments, s);
                var b = NoodlePoint(tip, end, right, (float)(j + 1) / NoodleSegments, s);
                var segment = strands[s, j]; var delta = b - a;
                segment.position = (a + b) / 2;
                segment.rotation = delta.sqrMagnitude > AxisEpsilon ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity;
                segment.localScale = new Vector3(NoodleRadius * 2 * w, Mathf.Max(MinSegment, delta.magnitude) / 2, NoodleRadius * 2 * w);
            }
        }
        Vector3 NoodlePoint(Vector3 tip, Vector3 end, Vector3 right, float t, int strand)
        {
            float spread = (strand - (NoodleStrands - 1) / 2f) * NoodleSpread;
            float sway = Mathf.Sin(t * Tau + clock.Time * Tau * NoodleHz + strand) * NoodleWave * Mathf.Sin(Mathf.PI * t);
            return Vector3.Lerp(tip, end, t) + right * (spread + sway);
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
            disposed = true;
            SofaRig.Kill(Chopsticks != null ? Chopsticks.gameObject : null);
            SofaRig.Kill(Props != null ? Props.gameObject : null);
            foreach (var m in materials) Release(m);
        }
    }
}
