using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Pure time/state data; usable by EditMode tests without loading a VRM.
    public sealed class FloorMealTimeline
    {
        public const float BlendSeconds = 1.4f; // Seconds for the complete overlay to enter or leave (a slow sit-down and stand-up).
        const float GatherSeconds = 2.4f, LiftSeconds = 2.6f, SlurpSeconds = 2.4f; // Eating beats, seconds.
        const float ChewSeconds = 2.2f, LowerSeconds = 1.2f; // Settle and return the bowl, seconds.
        const float PhoneSeconds = 3f, LaughSeconds = 1.6f, ReturnSeconds = 1.2f; // Phone reaction beats, seconds.
        const float DurationJitter = .15f; // Seeded duration variation as a fraction of each beat.
        const int MinBitesToDrink = 4, MaxBitesToDrink = 6; // Korean style: the bowl stays on the table; it is lifted for the broth once every this many bites (never on the first).
        const float PostureRamp = .5f; // Seconds for the lean, the left-hand rim hold and the resting chopsticks to change.
        const int MinBites = 2, MaxBites = 3; // Bites between phone checks, inclusive.
        const float AttentionRamp = .7f; // Seconds spent turning toward and away from the phone.
        const float SlurpMouth = .25f, ChewMouth = .06f, LaughMouth = .6f, SlurpPurse = .85f, DrinkPurse = .5f, DrinkMouth = .12f; // Face blend amplitudes (lips pursed forward while slurping, a small 'aa' only as the mouth pulls).
        public const int SlurpStrokes = 3; // Short feeding strokes in one slurp.
        const float SlurpEyes = .9f, DrinkEyes = .9f, ChewSmile = .3f; // Eyes close while slurping and drinking; a content little smile while chewing.
        const float LaughSmile = .9f, PhoneSmile = .15f, LaughEyes = .8f; // Face blend amplitudes.
        const float ChewHz = 1.7f, LaughHz = 3.5f, MouthFloor = .35f; // Facial pulse rates and minimum laugh openness.
        const float Tau = 2f * Mathf.PI; // Radians per cycle.
        static readonly string[] Names = { "Gathering", "Lifting", "Slurping", "Chewing", "Lowering", "Phone", "Laughing", "Returning" };
        static readonly float[] Durations = { GatherSeconds, LiftSeconds, SlurpSeconds, ChewSeconds, LowerSeconds, PhoneSeconds, LaughSeconds, ReturnSeconds };
        readonly System.Random rng;
        bool wanted, chin;
        int beat, bites, bitesUntilPhone, bitesSinceDrink, bitesUntilDrink;
        float elapsed, duration;

        public float Weight { get; private set; }
        public bool Active => wanted || Weight > 0;
        public string Phase => Active ? Names[beat] : "Stopped";
        public float Progress => duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0;
        public float Time { get; private set; }
        public float BowlLift { get; private set; }
        public bool Chin => chin; // this bite lifts the bowl for the broth
        public int BiteNumber { get; private set; } // counted from 1 at each Gathering
        public float Lean { get; private set; } // upper body over the bowl, 0..1
        public float RimHold { get; private set; } // left hand steadies the bowl rim, 0..1
        public float RestSticks { get; private set; } // chopsticks rest on the table while the bowl is drunk from, 0..1
        public float PhoneAttention { get; private set; }
        public float Laugh { get; private set; }
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public float MouthPurse { get; private set; } // 'oo' shape, 0..1
        public float StrokePull { get; private set; } // 0..1 inside each slurp stroke: the mouth pulls

        public FloorMealTimeline(int seed) { rng = new System.Random(seed); }
        public void Start()
        {
            if (wanted) return;
            wanted = true;
            // Restart while fading out resumes the held beat, avoiding a jump in pose.
            if (Weight > 0) return;
            beat = bites = 0; Time = elapsed = 0; chin = false; BiteNumber = 1; bitesSinceDrink = 0;
            Lean = RimHold = RestSticks = 0;
            bitesUntilDrink = rng.Next(MinBitesToDrink, MaxBitesToDrink + 1);
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
                        else { beat = 0; NextBite(); }
                    }
                    else if (beat == 7)
                    {
                        beat = bites = 0; NextBite();
                        bitesUntilPhone = rng.Next(MinBites, MaxBites + 1);
                    }
                    else beat++;
                    NewDuration();
                }
            }
            if (wanted)
            {
                float step = dt / PostureRamp;
                Lean = Mathf.MoveTowards(Lean, beat <= 2 && !chin ? 1 : 0, step);
                RimHold = Mathf.MoveTowards(RimHold, beat <= 3 ? 1 : 0, step);
                RestSticks = Mathf.MoveTowards(RestSticks, chin && beat <= 4 ? 1 : 0, step);
            }
            else
            {
                float step = dt / BlendSeconds;
                Lean = Mathf.MoveTowards(Lean, 0, step); RimHold = Mathf.MoveTowards(RimHold, 0, step); RestSticks = Mathf.MoveTowards(RestSticks, 0, step);
            }
            Sample();
        }
        void NextBite()
        {
            BiteNumber++; bitesSinceDrink++;
            chin = bitesSinceDrink >= bitesUntilDrink;
            if (chin) { bitesSinceDrink = 0; bitesUntilDrink = rng.Next(MinBitesToDrink, MaxBitesToDrink + 1); }
        }
        void Sample()
        {
            float p = Mathf.SmoothStep(0, 1, Progress);
            BowlLift = chin ? (beat == 1 ? p : beat == 2 || beat == 3 ? 1 : beat == 4 ? 1 - p : 0) : 0;
            PhoneAttention = beat == 5 ? Mathf.SmoothStep(0, 1, elapsed / AttentionRamp) :
                beat == 6 ? 1 : beat == 7 ? 1 - Mathf.SmoothStep(0, 1, elapsed / AttentionRamp) : 0;
            Laugh = beat == 6 ? Mathf.Sin(Mathf.PI * Progress) : 0;
            float slurpEnv = beat == 2 ? Mathf.Min(Mathf.SmoothStep(0, 1, Progress / .12f), Mathf.SmoothStep(0, 1, (1 - Progress) / .12f)) : 0;
            float stroke = beat == 2 ? Progress * SlurpStrokes - Mathf.Floor(Progress * SlurpStrokes) : 0;
            StrokePull = beat == 2 ? Mathf.SmoothStep(0, 1, (stroke - .5f) / .3f) : 0;
            Smile = Weight * (PhoneSmile * PhoneAttention + (LaughSmile - PhoneSmile) * Laugh + (beat == 3 && !chin ? ChewSmile : 0));
            EyesClosed = Weight * Mathf.Max(LaughEyes * Laugh, Mathf.Max(SlurpEyes * slurpEnv, DrinkEyes * BowlLift));
            MouthPurse = Weight * (beat == 2 ? SlurpPurse * slurpEnv : DrinkPurse * BowlLift);
            MouthOpen = Weight * (beat == 2 ? SlurpMouth * slurpEnv * (.3f + .7f * StrokePull) : beat == 3 ? (chin ? DrinkMouth * BowlLift : ChewMouth * Mathf.Abs(Mathf.Sin(Time * Tau * ChewHz))) :
                LaughMouth * Laugh * (MouthFloor + (1 - MouthFloor) * Mathf.Abs(Mathf.Sin(Time * Tau * LaughHz))) + DrinkMouth * BowlLift);
        }
    }

    // Construct in the model's upright reference pose. Tick once, then Apply in LateUpdate after the Animator.
    public sealed class FloorMealRig : IDisposable
    {
        public const float BlendSeconds = FloorMealTimeline.BlendSeconds; // Seconds for blend in/out.
        public const float HipHeight = .17f; // Hips pivot above the floor at the cushion centre, metres.
        public const float TableWidth = .6f, TableDepth = .4f, TableHeight = .25f; // Folding-table dimensions, metres.
        public const float TableAhead = .42f, TableThickness = .025f; // Table centre ahead of hips and slab thickness, metres.
        public const float CushionWidth = .72f, CushionDepth = .60f, CushionHeight = .12f; // Cushion dimensions, metres.
        public const float BowlRadius = .075f, BowlHeight = .075f, BowlAhead = .37f, BowlSide = -.025f; // Bowl dimensions and rest centre, metres.
        public const float PhoneSide = -.18f, PhoneAhead = .50f, PhoneTilt = 22f; // Propped phone x/z and back tilt, degrees.
        public static readonly Vector3 RecommendedSeat = new Vector3(1.65f, 0, 2.30f); // Clear rug location in OneRoom, metres.
        public const float RecommendedYaw = 180f; // Face the south window; local +z always faces the table.
        const float FootCross = .14f, FootAhead = .23f, FootHeight = .09f, AnkleStack = .055f; // Crossed ankle targets, metres.
        const float KneeOut = .65f, KneeAhead = .25f, KneeHeight = .10f; // Wide horizontal knee poles, metres.
        const float FootToeAhead = .35f; // Forward component of the outward-facing feet.
        const float SpineLean = 7f, NeckLean = 9f, HeadLean = 14f, PhoneYaw = -15f, HipBase = 5f; // Forward meal posture (upright-ish between bites: hips 5 + spine 7 = 12 deg) and phone turn, degrees.
        const float HipShare = .45f, LeanNeck = 20f, LeanHead = -12f; // Share of the eating lean taken by the hips (the rest curves the spine); while leaning the neck tips forward and the head tips back by this much more (degrees), pushing the face ahead of the shoulders while the eyes still reach the bowl. // Share of the eating lean taken by the hips (the rest curves the spine), 0..1.
        const float PhoneBow = 8f, LaughNod = 3f, LaughHz = 3.5f, BreathDegrees = .7f, BreathHz = .23f; // Secondary torso/head motion.
        const float MouthDown = .025f, MouthAhead = .075f; // Mouth estimate from head pivot, metres; check per VRM.
        const float ChinBowlDown = .13f, ChinBowlAhead = .07f; // Held bowl centre below/in front of the mouth, metres.
        const float LeftRestSide = .22f, LeftRestAhead = .04f, LeftRestGap = .0365f; // Resting left fingertips: left of the hips, ahead of the table's near edge, above the table top, metres.
        const float CupInset = .01f, CupUnder = .003f, TipExtension = .012f; // Middle fingertip inside the far rim, under the bowl bottom; distal pad length, metres.
        const float CupFingerCurl = .45f, CupRoll = 12f, RestFingerCurl = .5f, CupThumbCurl = 1.4f; // Left finger curl scales (1 = the grip angles); the resting hand keeps a slight natural curl (about half the grip).
        const float RestAdduct = .9f, CupAdduct = .7f, StickAdduct = .85f; // How far the four fingers are brought together (0 = the neutral spread, 1 = parallel).
        const float GatherOut = .13f, GatherUp = .10f, GatherBack = .04f; // Right wrist relative to bowl, metres.
        const float HighOut = .17f, HighUp = .15f, HighAhead = .07f; // High noodle lift relative to mouth, metres.
        const float SlurpOut = .12f, SlurpUp = .045f, SlurpAhead = .08f; // Right wrist beside the mouth, metres.
        const float SlurpApproach = .55f; // Fraction of slurp spent approaching the mouth.
        const float ElbowOut = .3f, ElbowDown = .22f, ElbowAhead = .18f; // Left arm IK pole from the shoulder, metres.
        const float RightElbowOut = .65f, RightElbowDown = .6f, RightElbowAhead = -.4f, RightElbowRiseOut = .6f, RightElbowRiseDown = .6f, RightElbowRiseAhead = -.45f, RollPalmWeight = .02f; // Right arm IK pole from the shoulder (out, down, ahead, rise while lifting; metres), and how much the roll prefers a palm facing left.
        const float StickLength = ChopstickGrip.StickLength, StickRadius = ChopstickGrip.StickRadius; // Chopstick dimensions, metres.
        const float CurlProximal = 24f, CurlIntermediate = 32f, CurlDistal = 18f, ThumbCurl = 20f; // Left-hand finger joint grip, degrees.
        const float FaceOverRim = .15f, MaxExtraBow = 60f, MaxLessBow = 24f; // Korean style: the face hovers this far over the bowl rim (m) with at most this much extra trunk lean (hips + spine, degrees).
        const float TipBowlSide = .02f, TipBowlAhead = -.02f, TipDepth = .012f, EntryHeight = .07f, HoverHeight = .04f; // Chopstick tips over the noodles: offset from the bowl centre, dip below the surface, start height, idle hover (m).
        const float StirRadius = .014f, LiftHeight = .10f, BobHeight = .008f, BobCount = 1f, StrokeDrop = .05f, NoodleStub = .03f; // Gather stir radius, lift above the surface, bob height and count, bundle length (m).
        const float BowlPitch = 40f, LiftPitch = 32f, MouthPitch = -45f, RestPitch = 8f, StickYaw = 32f, MouthYaw = 100f, MaxStickOffForearm = 12f; // Chopstick direction: degrees below horizontal, and the pull to her left.
        const float RestStickSide = .17f, RestStickHeight = .014f, RestStickBack = .03f; // Resting chopstick tips: right of the bowl, above the table, behind the table centre (m).
        const float RimTipInset = .012f, RimTipHeight = .004f, RimTipBack = .02f; // Left fingertip on the bowl rim: inset from the left rim, above it, nearer to her (m).
        const float BowlWall = .008f, BowlBase = .012f, BowlRim = .006f; // Open bowl wall, bottom and lip thickness, metres.
        const int BowlPanels = 20; // Primitive panels around the open bowl.
        const float TableLegInset = .04f, TableLegThickness = .018f, LegFoldAngle = 12f; // Inset, thickness and folding-leg splay.
        const float PhoneClearance = .004f, PhoneSupport = .035f; // Phone bottom gap and stand depth, metres.
        const int NoodleStrands = 3, NoodleSegments = 5; // Small cylinder chain count; no per-frame allocations.
        const float NoodleRadius = .002f, NoodleSpread = .006f, NoodleWave = .008f, NoodleHz = 1.2f; // Strand thickness, spacing and sway, metres/Hz.
        const float NoodleStart = .08f, NoodleEnd = .96f, MinSegment = .001f; // Visibility cutoffs in a beat and minimum rendered length.
        const float NoodleGathered = .94f, NoodleLag = .06f, NoodleMaxLag = .02f, SlurpApproachFraction = .55f; // Bundle shows after the tips close; lag behind the tips' motion (s, m).
        const float RotationEpsilon = .001f, PositionEpsilon = .00001f, AxisEpsilon = .000001f; // Pose/axis comparison tolerances.
        const float Tau = 2 * Mathf.PI; // Radians per cycle.
        static readonly Color Steel = new Color(.78f, .79f, .8f); // brushed stainless
        static readonly Color Wood = new Color(.65f, .47f, .31f), Fabric = new Color(.48f, .56f, .53f); // Prop colours.
        static readonly Color Ceramic = new Color(.86f, .88f, .80f), Metal = new Color(.22f, .23f, .24f); // Prop colours.
        static readonly Color PhoneColor = new Color(.045f, .05f, .06f), ScreenColor = new Color(.28f, .44f, .52f), NoodleColor = new Color(.94f, .81f, .5f); // Prop colours.

        readonly Animator anim;
        readonly FloorMealTimeline clock;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 5, 3];
        readonly Transform[][] proximals = { new Transform[4], new Transform[4] }, intermediates = { new Transform[4], new Transform[4] };
        readonly Quaternion[] handFrame = new Quaternion[2], footFrame = new Quaternion[2];
        readonly Vector3[] fingerAxis = new Vector3[2], palmAxis = new Vector3[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        readonly Quaternion[] before, written;
        readonly Transform[,] strands = new Transform[NoodleStrands, NoodleSegments];
        readonly Transform stickLower, stickUpper;
        ChopstickGrip grip;
        Vector3 lastTip; bool hasLastTip;
        readonly Vector3 middleTipInHand, middleTipInHandRight; // middle fingertip relative to the wrist in the neutral pose (rotation only), metres.
        readonly Quaternion headFrame; // head bone -> body frame, taken in the upright reference pose
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
        public float MouthPurse => disposed ? 0 : clock.MouthPurse;
        public Vector3 RightWristTarget { get; private set; }
        public Vector3 MouthPoint { get; private set; } // Lip estimate from the head bone's own frame, world; valid after Apply.
        public ChopstickGrip Grip => grip;
        public Vector3 RightShoulder => upper[1].position;
        public Vector3 RightElbow => lower[1].position;
        public Vector3 RightWrist => hand[1].position;
        public Vector3 LeftShoulder => upper[0].position;
        public Vector3 LeftElbow => lower[0].position;
        public Vector3 LeftWrist => hand[0].position;
        // Inner angle at the elbow (180 = straight arm), degrees; valid after Apply.
        public float RightElbowBend => Vector3.Angle(upper[1].position - lower[1].position, hand[1].position - lower[1].position);
        public float LeftElbowBend => Vector3.Angle(upper[0].position - lower[0].position, hand[0].position - lower[0].position);
        // Trunk (hips to neck) lean from vertical, and head pitch below horizontal, degrees about the table's right axis (forward positive); valid after Apply.
        public float TrunkDegrees => Vector3.SignedAngle(Props.up, Vector3.ProjectOnPlane((neck ?? head).position - hips.position, Props.right), Props.right);
        public float HeadPitchDegrees => Vector3.SignedAngle(Props.forward, head.rotation * headFrame * Vector3.forward, Props.right);
        public float ShoulderMidAhead(Vector3 world) => Vector3.Dot(world - (upper[0].position + upper[1].position) / 2, Props.forward);
        public float? GripOverride; // Tests and stills: hold the chopsticks at this opening (0 closed, 1 open) instead of the beat's.
        public float LeanAngle, MouthOverRimBeforeLean; // Degrees of extra bow and the face height over the rim before it; valid after Apply while she leans.
        public float Progress => disposed ? 0 : clock.Progress;
        public float NoodleSurfaceY => Bowl.position.y + BowlHeight / 2 - BowlBase + BowlRim / 2;
        public bool BowlStaysOnTable => clock.BowlLift <= 0;
        public bool BiteIsDrink => clock.Chin;
        public int BiteNumber => clock.BiteNumber;
        public Vector3 NoodleBottom { get; private set; } // Lower end of the noodle bundle, world; valid after Apply while the noodles show.
        public Vector3 NoodleTop { get; private set; } // Mean start of the noodle strands, world; valid after Apply while the noodles show.
        public Vector3 BowlRimCentre => Bowl.position + Props.up * (BowlHeight / 2);
        // Degrees of wrist flexion/extension (about the hand's x axis) and radial/ulnar deviation (about its palm normal) between the forearm and the hand; valid after Apply.
        public Vector2 WristAngles
        {
            get
            {
                if (grip == null) return Vector2.zero;
                var f = (hand[1].position - lower[1].position).normalized; var h = grip.HFrame;
                var local = Quaternion.Inverse(h) * f; // forearm direction in H
                return new Vector2(Mathf.Atan2(local.z, local.y) * Mathf.Rad2Deg, Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg);
            }
        }

        public FloorMealRig(Animator anim, int seed, Quaternion? restHeadFrame = null)
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
            for (int s = 0; s < 2; s++) for (int f = 0; f < 4; f++) { proximals[s][f] = finger[s, f + 1, 0]; intermediates[s][f] = finger[s, f + 1, 1]; }
            {
                var d = finger[0, 2, 2]; var m = finger[0, 2, 1];
                var tip = d != null && m != null ? d.position + (d.position - m.position).normalized * TipExtension : hand[0].position + hand[0].rotation * fingerAxis[0] * .16f;
                middleTipInHand = Quaternion.Inverse(hand[0].rotation) * (tip - hand[0].position);
                var dr = finger[1, 2, 2]; var mr = finger[1, 2, 1];
                var tipR = dr != null && mr != null ? dr.position + (dr.position - mr.position).normalized * TipExtension : hand[1].position + hand[1].rotation * fingerAxis[1] * .16f;
                middleTipInHandRight = Quaternion.Inverse(hand[1].rotation) * (tipR - hand[1].position);
            }
            headFrame = restHeadFrame ?? Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No FloorMeal material shader found.");
            var wood = MaterialFor(shader, Wood); var steel = MaterialFor(shader, Steel); steel.SetFloat("_Metallic", .8f); steel.SetFloat("_Smoothness", .6f); var cloth = MaterialFor(shader, Fabric);
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
            Chopsticks = new GameObject("FloorMealChopsticks").transform; // world space, unscaled; the two sticks are placed from the grip every frame
            for (int i = 0; i < 2; i++)
            {
                var stick = FlatStick(Chopsticks, "chopstick", steel);
                if (i == 0) stickLower = stick; else stickUpper = stick;
            }
            try { grip = new ChopstickGrip(hand[1], FingerChain(1, 0), FingerChain(1, 1), FingerChain(1, 2), FingerChain(1, 3), FingerChain(1, 4)); }
            catch (Exception e) { grip = null; Debug.LogWarning("FLOORMEAL_GRIP fallback to the plain parallel sticks: " + e.Message); }
            Noodles = new GameObject("FloorMealNoodles").transform; Noodles.SetParent(Props, false);
            for (int s = 0; s < NoodleStrands; s++) for (int j = 0; j < NoodleSegments; j++)
                strands[s, j] = Part(Noodles, "noodle", PrimitiveType.Cylinder, Vector3.zero, Vector3.one * NoodleRadius, noodleMat);
            Chopsticks.gameObject.SetActive(false); Noodles.gameObject.SetActive(false);
        }

        Transform[] FingerChain(int side, int f) => new[] { finger[side, f, 0], finger[side, f, 1], finger[side, f, 2] };
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
                float bob = clock.Laugh * Mathf.Sin(clock.Time * Tau * LaughHz);
                Rotate(hips, right, HipBase * w);
                Rotate(spine, right, (SpineLean + BreathDegrees * Mathf.Sin(clock.Time * Tau * BreathHz)) * w);
                Rotate(chest, right, LaughNod * bob * w);
                float lean = clock.Lean * w;
                Rotate(neck ?? head, right, (NeckLean + LeanNeck * lean) * w);
                Rotate(head, right, (HeadLean + LeanHead * lean + PhoneBow * clock.PhoneAttention - LaughNod * bob) * w);
                Rotate(head, up, PhoneYaw * clock.PhoneAttention * w);
                // Korean style: on a normal bite the bowl stays down and she leans the whole trunk forward (hips first, then the spine) until the face hovers FaceOverRim above the rim
                if (lean > 0)
                {
                    float MouthY() => (head.position + head.rotation * headFrame * new Vector3(0, -MouthDown, MouthAhead)).y;
                    float y0 = MouthY(); var hipsRot = hips.rotation; var spineRot = spine.rotation;
                    float wantY = Mathf.Lerp(y0, rest.y + BowlHeight / 2 + FaceOverRim, lean);
                    float lo = -MaxLessBow, hi = MaxExtraBow;
                    for (int i = 0; i < 16; i++)
                    {
                        float mid = (lo + hi) / 2;
                        Rotate(hips, right, mid * HipShare); Rotate(spine, right, mid * (1 - HipShare));
                        float y = MouthY();
                        hips.rotation = hipsRot; spine.rotation = spineRot;
                        if (y > wantY) lo = mid; else hi = mid;
                    }
                    LeanAngle = (lo + hi) / 2; MouthOverRimBeforeLean = y0 - (rest.y + BowlHeight / 2);
                    Rotate(hips, right, LeanAngle * HipShare); Rotate(spine, right, LeanAngle * (1 - HipShare));
                }
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? -1 : 1;
                    var target = Props.TransformPoint(new Vector3(-sign * FootCross, FootHeight + (s == 0 ? AnkleStack : 0), FootAhead));
                    var pole = Props.TransformPoint(new Vector3(sign * KneeOut, KneeHeight, KneeAhead));
                    SofaRig.TwoBone(thigh[s], shin[s], foot[s], target, pole, w);
                    var orientation = Quaternion.LookRotation((right * -sign + forward * FootToeAhead).normalized, up) * Quaternion.Inverse(footFrame[s]);
                    foot[s].rotation = Quaternion.Slerp(foot[s].rotation, orientation, w);
                }
                var mouth = head.position + head.rotation * headFrame * new Vector3(0, -MouthDown, MouthAhead); // from the head bone, so a turned or bowed head carries its mouth
                MouthPoint = mouth;
                float held = clock.BowlLift * w;
                var bowlWanted = Vector3.Lerp(rest, mouth - up * ChinBowlDown + forward * ChinBowlAhead, held);
                // the left hand rests flat on the table beside the bowl, steadies the bowl rim while she eats, and when the bowl is lifted slides under it, middle fingertip under the far side of the bottom
                float handW = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .7f, held));
                float rimW = Mathf.SmoothStep(0, 1, clock.RimHold);
                var restFrame = Quaternion.LookRotation(forward, -up); var cupFrame = Quaternion.AngleAxis(CupRoll, right) * Quaternion.LookRotation(right, up);
                var rimFrame = Quaternion.LookRotation(right, -up);
                var tipRest = Props.TransformPoint(new Vector3(-LeftRestSide, TableHeight + LeftRestGap, TableAhead - TableDepth / 2 + LeftRestAhead));
                var tipRim = Props.TransformPoint(new Vector3(BowlSide - BowlRadius + RimTipInset, TableHeight + BowlHeight + RimTipHeight, BowlAhead - RimTipBack));
                var tipCup = bowlWanted + right * (BowlRadius - CupInset) - up * (BowlHeight / 2 + CupUnder);
                var wristRest = Vector3.Lerp(tipRest - restFrame * Quaternion.Inverse(handFrame[0]) * middleTipInHand, tipRim - rimFrame * Quaternion.Inverse(handFrame[0]) * middleTipInHand, rimW);
                var frameRest = Quaternion.Slerp(restFrame, rimFrame, rimW);
                var wristCup = tipCup - cupFrame * Quaternion.Inverse(handFrame[0]) * middleTipInHand;
                var leftWrist = Vector3.Lerp(wristRest, wristCup, handW);
                Arm(0, leftWrist, Quaternion.Slerp(frameRest, cupFrame, handW), Mathf.Lerp(RestFingerCurl, CupFingerCurl, handW), Mathf.Lerp(RestFingerCurl, CupThumbCurl, handW), w, Mathf.Lerp(RestAdduct, CupAdduct, handW));
                // a held bowl sits on the actual hand even if a small avatar cannot reach the ideal target
                var bowlOnHand = LeftMiddleTip - right * (BowlRadius - CupInset) + up * (BowlHeight / 2 + CupUnder);
                Bowl.position = Vector3.Lerp(bowlWanted, bowlOnHand, handW);
                try { ApplyRightHand(mouth, rest, right, up, forward, w, dt, handW, bowlWanted); }
                catch (Exception e)
                {
                    if (grip != null) { Debug.LogWarning("FLOORMEAL_GRIP failed, back to the plain parallel sticks: " + e.Message); grip = null; }
                    try { ApplyRightHand(mouth, rest, right, up, forward, w, dt, handW, bowlWanted); } catch (Exception) { }
                }
                DrawNoodles(mouth, right, up, w);
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hipWritten = hips.localPosition; hasWritten = true;
        }

        static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));

        // Where the chopstick tips go, which way the sticks point and how far open they are, for the current beat.
        void ApplyRightHand(Vector3 mouth, Vector3 bowlRest, Vector3 right, Vector3 up, Vector3 forward, float w, float dt, float cupW, Vector3 bowlWanted)
        {
            var surface = bowlRest + up * (BowlHeight / 2 - BowlBase + BowlRim / 2);
            var tipBowl = surface + right * TipBowlSide + forward * TipBowlAhead;
            var hover = tipBowl + up * HoverHeight;
            var liftHigh = tipBowl + up * LiftHeight; liftHigh += Vector3.ProjectOnPlane(mouth - liftHigh, up) * .5f;
            float p = clock.Progress, ps = Smooth(p), open = 0, pitch = BowlPitch, shorten = 0, yawDeg = StickYaw;
            var tip = hover;
            bowlPoint = tipBowl;
            switch (Phase)
            {
                case "Gathering":
                    {
                        float enter = Smooth(p / .28f), stir = Mathf.InverseLerp(.28f, .72f, p), a = stir * Tau * 1.5f, amp = StirRadius * Mathf.Sin(Mathf.PI * Mathf.Clamp01(stir));
                        tip = tipBowl + up * Mathf.Lerp(EntryHeight, -TipDepth, enter) + (right * Mathf.Cos(a) + forward * Mathf.Sin(a)) * amp;
                        open = 1 - Smooth(Mathf.InverseLerp(.72f, .95f, p));
                        break;
                    }
                case "Lifting":
                    {
                        float rise = Smooth(p / .55f), bobs = p > .55f ? BobHeight * Mathf.Sin(Tau * BobCount * Mathf.InverseLerp(.55f, 1, p)) : 0;
                        tip = Vector3.Lerp(tipBowl - up * TipDepth, liftHigh, rise) + up * bobs;
                        { float tilt = Smooth(Mathf.InverseLerp(.3f, 1f, rise)); pitch = Mathf.Lerp(BowlPitch, MouthPitch, tilt); yawDeg = Mathf.Lerp(StickYaw, MouthYaw, tilt); }
                        break;
                    }
                case "Slurping":
                    {
                        // 2 - 3 short feeding strokes: the tips slide 5 cm down the bundle, pinch, push up to the lips, the mouth pulls
                        float entry = Smooth(p / .12f), scaled = p * FloorMealTimeline.SlurpStrokes, su = scaled - Mathf.Floor(scaled);
                        float drop = su < .7f ? StrokeDrop * (1 - Smooth(su / .7f)) : StrokeDrop * Smooth((su - .85f) / .15f);
                        if (p > .97f) drop = 0;
                        tip = Vector3.Lerp(liftHigh, mouth - up * drop, entry); pitch = MouthPitch; yawDeg = MouthYaw;
                        shorten = Mathf.Clamp01((Mathf.Floor(scaled) + Smooth(su / .7f)) / FloorMealTimeline.SlurpStrokes);
                        break;
                    }
                case "Chewing": tip = Vector3.Lerp(mouth, hover, ps); pitch = Mathf.Lerp(MouthPitch, BowlPitch, ps); yawDeg = Mathf.Lerp(MouthYaw, StickYaw, ps); break;
            }
            // while the bowl is lifted for the broth the chopsticks lie on the table, tips away from the face
            float restW = Smooth(clock.RestSticks);
            var restTip = Props.TransformPoint(new Vector3(RestStickSide, TableHeight + RestStickHeight, TableAhead - RestStickBack));
            if (restW > 0)
            {
                tip = Vector3.Lerp(tip, restTip, restW);
                pitch = Mathf.Lerp(pitch, RestPitch, restW); open = Mathf.Lerp(open, 0, restW);
            }
            if (GripOverride.HasValue) open = GripOverride.Value;
            float pr = pitch * Mathf.Deg2Rad;
            var flat = forward * Mathf.Cos(yawDeg * Mathf.Deg2Rad) - right * Mathf.Sin(yawDeg * Mathf.Deg2Rad);
            var dir = (flat * Mathf.Cos(pr) - up * Mathf.Sin(pr)).normalized;
            // hand attitude: the sticks point the way designed above; the roll about them is the one that keeps the wrist nearest neutral
            // (the finger axis along the forearm) with the thumb side up and to her left (semi-pronated). The elbow is found with the same arm solve.
            var qu = upper[1].rotation; var ql = lower[1].rotation; var qh = hand[1].rotation;
            noodleShorten = shorten;
            if (grip == null) { ApplyLegacyHand(tip, dir, w, dt, right, up); return; }
            var thumbWant = Vector3.ProjectOnPlane(up * .8f - right * .6f, dir).normalized;
            var palmWant = (-right * .9f - up * .35f).normalized; // semi-pronated: the palm faces left and a little down
            var shoulder = upper[1].position;
            float riseFraction = Mathf.Clamp01((tip.y - (bowlRest.y + BowlHeight / 2 - BowlBase)) / (LiftHeight + .04f));
            // Exact arm solve, elbow first: the elbow hangs low near her ribs (it rises a little while the noodles go to the mouth), the wrist is then the point
            // at forearm length from the elbow and at wrist-to-tips length from the tips (a circle); the sticks and the roll follow from that, so the tips are always where
            // they should be and the wrist is never asked to fold. Among the circle's points the one with the wrist in front of the shoulder and the least wrist bend wins.
            Vector3 Pole() => shoulder + right * Mathf.Lerp(RightElbowOut, RightElbowRiseOut, riseFraction) - up * Mathf.Lerp(RightElbowDown, RightElbowRiseDown, riseFraction) + forward * Mathf.Lerp(RightElbowAhead, RightElbowRiseAhead, riseFraction);
            float armA = Vector3.Distance(upper[1].position, lower[1].position), armB = Vector3.Distance(lower[1].position, hand[1].position);
            var pole = Pole(); var elbow = shoulder + (pole - shoulder).normalized * armA; pole = elbow;
            var tipsInHand = grip.TipsMidInHand; float reachL = tipsInHand.magnitude; var t0 = tipsInHand / reachL; float cone = Vector3.Angle(Vector3.up, t0); // hand y axis to the wrist-to-tips line
            var toTip = tip - elbow; float dTip = Mathf.Clamp(toTip.magnitude, Mathf.Abs(armB - reachL) + .005f, armB + reachL - .005f); var axis = toTip.normalized;
            float cx = (armB * armB - reachL * reachL + dTip * dTip) / (2 * dTip), cr = Mathf.Sqrt(Mathf.Max(0, armB * armB - cx * cx));
            var centre = elbow + axis * cx; var cu = Vector3.ProjectOnPlane(forward, axis); if (cu.sqrMagnitude < AxisEpsilon) cu = Vector3.ProjectOnPlane(right, axis); cu.Normalize(); var cv = Vector3.Cross(axis, cu);
            float bestPhi = lastPhi, bestPhiCost = float.MaxValue; var wrist = centre;
            for (int k = 0; k < 72; k++)
            {
                float phi = k * 5f; var cand = centre + (cu * Mathf.Cos(phi * Mathf.Deg2Rad) + cv * Mathf.Sin(phi * Mathf.Deg2Rad)) * cr;
                var v = (tip - cand).normalized;
                float cost = 8f * Mathf.Max(0, Vector3.Angle(cand - elbow, v) - cone) + 60f * Mathf.Max(0, .12f - Vector3.Dot(cand - shoulder, forward)) + 120f * Mathf.Max(0, Vector3.Dot(cand - tip, right) - .12f)
                    + 120f * Mathf.Max(0, Vector3.Dot(cand - tip, forward) + .06f) + .15f * Mathf.Abs(Mathf.DeltaAngle(phi, lastPhi));
                if (cost < bestPhiCost) { bestPhiCost = cost; bestPhi = phi; wrist = cand; }
            }
            lastPhi = bestPhi;
            var vTip = (tip - wrist).normalized; var frame0 = Quaternion.FromToRotation(t0, vTip); var forearmDir = (wrist - elbow).normalized;
            Quaternion frame = frame0; float bestRollCost = float.MaxValue, bestRoll = lastRoll;
            for (int k = 0; k < 72; k++)
            {
                var q = Quaternion.AngleAxis(k * 5f, vTip) * frame0;
                float cost = Vector3.Angle(q * Vector3.up, forearmDir) + RollPalmWeight * Vector3.Angle(q * Vector3.back, palmWant) + .02f * Vector3.Angle(Vector3.ProjectOnPlane(q * Vector3.right, vTip), thumbWant) + .3f * Mathf.Abs(Mathf.DeltaAngle(k * 5f, lastRoll));
                if (cost < bestRollCost) { bestRollCost = cost; frame = q; bestRoll = k * 5f; }
            }
            lastRoll = bestRoll;
            var handRot = frame * Quaternion.Inverse(grip.HandToFrame);
            // drinking the broth: both hands hold the bowl by its sides, the right fingertips under the near side of the bottom, the sticks lie on the table
            if (cupW > 0)
            {
                var cupFrame = Quaternion.AngleAxis(-CupRoll, right) * Quaternion.LookRotation(-right, up);
                var tipCup = bowlWanted - right * (BowlRadius - CupInset) - up * (BowlHeight / 2 + CupUnder);
                var wristCup = tipCup - cupFrame * Quaternion.Inverse(handFrame[1]) * middleTipInHandRight;
                wrist = Vector3.Lerp(wrist, wristCup, cupW);
                handRot = Quaternion.Slerp(handRot, cupFrame * Quaternion.Inverse(handFrame[1]), cupW);
                pole = Vector3.Lerp(pole, shoulder + right * ElbowOut - up * ElbowDown + forward * ElbowAhead, cupW);
            }
            RightWristTarget = wrist;
            SofaRig.TwoBone(upper[1], lower[1], hand[1], wrist, pole, w);
            hand[1].rotation = Quaternion.Slerp(hand[1].rotation, handRot, w);
            grip.Pose(open, w * (1 - cupW));
            if (cupW > 0) CurlFingers(1, Mathf.Lerp(0, CupFingerCurl, cupW), Mathf.Lerp(0, CupThumbCurl, cupW), w, CupAdduct * cupW);
            var faceUp = grip.UpperTopWorld - grip.LowerTopWorld;
            PlaceStick(stickLower, grip.LowerTopWorld, grip.LowerDirWorld, faceUp);
            PlaceStick(stickUpper, grip.UpperTopWorld, grip.UpperDirWorld, faceUp);
            if (cupW > .01f)
            {
                var lying = (forward - right * .15f).normalized; var gap = Vector3.Cross(up, lying).normalized * .011f; float k = Smooth(cupW);
                var tipLie = Props.TransformPoint(new Vector3(RestStickSide, TableHeight + StickRadius + .0005f, TableAhead - RestStickBack));
                PlaceStick(stickLower, Vector3.Lerp(grip.LowerTopWorld, tipLie - lying * StickLength - gap, k), Vector3.Slerp(grip.LowerDirWorld, lying, k), Vector3.Lerp(faceUp, up, k));
                PlaceStick(stickUpper, Vector3.Lerp(grip.UpperTopWorld, tipLie - lying * StickLength + gap, k), Vector3.Slerp(grip.UpperDirWorld, lying, k), Vector3.Lerp(faceUp, up, k));
            }
            GripOpen = open; noodleShorten = shorten;
            var mid = TipsMid;
            Velocity = dt > 0 && hasLastTip ? (mid - lastTip) / dt : Vector3.zero; lastTip = mid; hasLastTip = true;
        }
        // Shoulder, elbow, wrist and tip heights (world, m) and how far the elbow is out from the hips' centre line (m); valid after Apply.
        public string ArmNumbers() => "shoulder y " + upper[1].position.y.ToString("0.00") + " elbow y " + lower[1].position.y.ToString("0.00") + " wrist y " + hand[1].position.y.ToString("0.00") + " tips y " + grip.TipsMidWorld.y.ToString("0.00") + " elbowOut " + Vector3.Dot(lower[1].position - Props.position, Props.right).ToString("0.00") + " wristFromShoulder(r,u,f) " + new Vector3(Vector3.Dot(hand[1].position - upper[1].position, Props.right), hand[1].position.y - upper[1].position.y, Vector3.Dot(hand[1].position - upper[1].position, Props.forward)).ToString("0.00") + " arm " + Vector3.Distance(upper[1].position, lower[1].position).ToString("0.00") + "/" + Vector3.Distance(lower[1].position, hand[1].position).ToString("0.00") + " shoulderOut " + Vector3.Dot(upper[1].position - Props.position, Props.right).ToString("0.00");
        // Tip midpoint of the sticks (world), from the grip or, when the grip could not be built for this rig, from the plain parallel sticks.
        public Vector3 TipsMid => grip != null ? grip.TipsMidWorld : legacyTip;
        Vector3 legacyTip;
        // The old behaviour, kept for a rig whose hand bones do not fit the grip: two parallel sticks fixed 3.5 cm ahead of the wrist along the straight fingers.
        void ApplyLegacyHand(Vector3 tip, Vector3 dir, float w, float dt, Vector3 right, Vector3 up)
        {
            const float gripAhead = .035f, behindGrip = .055f, gap = .012f;
            var wrist = tip - dir * (gripAhead + StickLength - behindGrip);
            RightWristTarget = wrist;
            Arm(1, wrist, Quaternion.LookRotation(dir, -right), 1, 1, w, StickAdduct);
            var at = hand[1].position + hand[1].rotation * fingerAxis[1] * gripAhead;
            var side = Vector3.Cross(Vector3.ProjectOnPlane(up, dir).normalized, dir).normalized;
            PlaceStick(stickLower, at - dir * behindGrip - side * gap / 2, dir, side);
            PlaceStick(stickUpper, at - dir * behindGrip + side * gap / 2, dir, side);
            legacyTip = at + dir * (StickLength - behindGrip);
            GripOpen = 0;
            Velocity = dt > 0 && hasLastTip ? (legacyTip - lastTip) / dt : Vector3.zero; lastTip = legacyTip; hasLastTip = true;
        }
        float lastRoll, lastPhi;
        public float GripOpen { get; private set; } // Opening used in the last Apply, 0 closed .. 1 open.
        Vector3 Velocity, bowlPoint; float noodleShorten;
        // The stick's long axis follows 'direction'; its thin side faces 'face' (the other stick, or up when it lies on the table).
        static void PlaceStick(Transform stick, Vector3 top, Vector3 direction, Vector3 face)
        {
            var z = Vector3.ProjectOnPlane(face, direction); if (z.sqrMagnitude < AxisEpsilon) z = Vector3.ProjectOnPlane(Vector3.up, direction); if (z.sqrMagnitude < AxisEpsilon) z = Vector3.forward;
            stick.SetPositionAndRotation(top + direction * (StickLength / 2), Quaternion.LookRotation(z.normalized, direction));
            stick.localScale = new Vector3(1, StickLength, 1);
        }
        // A flat tapered stick of unit length along +y (handle at -y, tip at +y), 5.5 x 2.5 mm at the handle and 3.5 x 2 mm at the tip; scaled in y to StickLength.
        static Transform FlatStick(Transform parent, string name, Material material)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            float h0w = ChopstickGrip.StickHalfWide, h0t = ChopstickGrip.StickRadius, h1w = ChopstickGrip.TipHalfWide, h1t = ChopstickGrip.TipHalfThick; // x is the width, z the thickness, in metres (y is scaled by the length)
            var c = new[] { new Vector3(-h0w, -.5f, -h0t), new Vector3(h0w, -.5f, -h0t), new Vector3(h0w, -.5f, h0t), new Vector3(-h0w, -.5f, h0t),
                new Vector3(-h1w, .5f, -h1t), new Vector3(h1w, .5f, -h1t), new Vector3(h1w, .5f, h1t), new Vector3(-h1w, .5f, h1t) };
            var faces = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            var verts = new List<Vector3>(); var tris = new List<int>();
            foreach (var f in faces)
            {
                int b = verts.Count; foreach (int i in f) verts.Add(c[i]);
                var mid = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) / 4;
                bool outward = Vector3.Dot(Vector3.Cross(c[f[1]] - c[f[0]], c[f[2]] - c[f[0]]), mid) > 0; // the box is centred on the origin
                tris.AddRange(outward ? new[] { b, b + 1, b + 2, b, b + 2, b + 3 } : new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            var mesh = new Mesh { name = "flat_chopstick" }; mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = material;
            return g.transform;
        }

        // Fingertip of the left middle finger (distal pad), world; valid after Apply.
        public Vector3 LeftMiddleTip => TipOf(0, 2);
        public Vector3 TipOf(int side, int fingerIndex)
        {
            var d = finger[side, fingerIndex, 2]; var m = finger[side, fingerIndex, 1];
            return d.position + (d.position - m.position).normalized * TipExtension;
        }
        public float TableTopY => Props.position.y + TableHeight;
        public float LeftHandToBowlRim => Mathf.Max(0, BowlSurfaceDistance(LeftMiddleTip));
        // Signed distance from a world point to the bowl solid (a cylinder: radius BowlRadius + wall, BowlHeight tall); negative = inside.
        public float BowlSurfaceDistance(Vector3 world)
        {
            var l = Bowl.InverseTransformPoint(world);
            float radial = new Vector2(l.x, l.z).magnitude - (BowlRadius + BowlWall / 2), vertical = Mathf.Abs(l.y) - BowlHeight / 2;
            return radial > 0 && vertical > 0 ? Mathf.Sqrt(radial * radial + vertical * vertical) : Mathf.Max(radial, vertical);
        }

        // Largest angle (degrees) between any finger's proximal direction and the middle finger's, seen along the palm normal: 0 = fingers together; valid after Apply.
        public float FingerSpreadDegrees(int side)
        {
            var n = hand[side].rotation * palmAxis[side];
            var m = Vector3.ProjectOnPlane(intermediates[side][1].position - proximals[side][1].position, n); float worst = 0;
            for (int f = 0; f < 4; f++) if (f != 1)
                worst = Mathf.Max(worst, Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(intermediates[side][f].position - proximals[side][f].position, n), m, n)));
            return worst;
        }

        void Arm(int side, Vector3 target, Quaternion frame, float fingerCurl, float thumbCurl, float w, float adduct)
        {
            SofaRig.TwoBone(upper[side], lower[side], hand[side], target,
                upper[side].position + Props.right * (side == 0 ? -ElbowOut : ElbowOut) - Props.up * ElbowDown + Props.forward * ElbowAhead, w);
            hand[side].rotation = Quaternion.Slerp(hand[side].rotation, frame * Quaternion.Inverse(handFrame[side]), w);
            CurlFingers(side, fingerCurl, thumbCurl, w, adduct);
        }
        void CurlFingers(int side, float fingerCurl, float thumbCurl, float w, float adduct)
        {
            HandPose.Adduct(proximals[side], intermediates[side], hand[side].rotation * palmAxis[side], adduct * w);
            var axis = hand[side].rotation * Vector3.Cross(fingerAxis[side], palmAxis[side]).normalized;
            // SofaRig's Curl is private; preserve its calibrated palm-axis convention here.
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++)
                Rotate(finger[side, f, j], axis, (f == 0 ? ThumbCurl * thumbCurl : (j == 0 ? CurlProximal : j == 1 ? CurlIntermediate : CurlDistal) * fingerCurl) * w);
        }

        void DrawNoodles(Vector3 mouth, Vector3 right, Vector3 up, float w)
        {
            bool eating = !clock.Chin;
            bool gathered = eating && Phase == "Gathering" && clock.Progress > NoodleGathered;
            bool lifting = eating && Phase == "Lifting";
            bool slurping = eating && Phase == "Slurping" && clock.Progress < NoodleEnd;
            Noodles.gameObject.SetActive((gathered || lifting || slurping) && w > 0);
            if (!gathered && !lifting && !slurping) return;
            var tip = TipsMid;
            // one continuous bundle from the broth up to the tips; each feeding stroke eats some, so its free end rises off the broth and the bundle gets shorter
            var surface = bowlPoint + up * (BowlRim / 2 + .001f);
            var inBowl = new Vector3(surface.x, Mathf.Min(surface.y, tip.y), surface.z);
            var end = Vector3.Lerp(inBowl, tip - up * NoodleStub, slurping ? noodleShorten : 0);
            NoodleBottom = end;
            var top = Vector3.zero;
            for (int s = 0; s < NoodleStrands; s++)
            {
                top += NoodlePoint(tip, end, right, 0, s);
                for (int j = 0; j < NoodleSegments; j++)
                {
                    var a = NoodlePoint(tip, end, right, (float)j / NoodleSegments, s);
                    var b = NoodlePoint(tip, end, right, (float)(j + 1) / NoodleSegments, s);
                    var segment = strands[s, j]; var delta = b - a;
                    segment.position = (a + b) / 2;
                    segment.rotation = delta.sqrMagnitude > AxisEpsilon ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity;
                    segment.localScale = new Vector3(NoodleRadius * 2 * w, Mathf.Max(MinSegment, delta.magnitude) / 2, NoodleRadius * 2 * w);
                }
            }
            NoodleTop = top / NoodleStrands;
        }
        Vector3 NoodlePoint(Vector3 tip, Vector3 end, Vector3 right, float t, int strand)
        {
            float spread = (strand - (NoodleStrands - 1) / 2f) * NoodleSpread * Mathf.Lerp(.25f, 1, t);
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
