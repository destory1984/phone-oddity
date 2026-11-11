using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Pure clock: usable without an avatar or an Apply call. Stop freezes the current beat.
    public sealed class LaundryTimeline
    {
        public const int PieceCount = 5; // Number of garments in one load.
        public const float ReachSeconds = 1.1f, LiftSeconds = 1f, ShakeSeconds = .65f; // Pickup and single snap.
        public const float LowerSeconds = .9f, FoldSeconds = 1.3f; // Lay down, then each of two folds.
        public const float PlaceSeconds = 1.1f, ReturnSeconds = .65f, PatSeconds = 1.1f; // Deposit, release, final pat.
        public const float TempoMin = .95f, TempoMax = 1.05f; // Seeded per-piece duration multiplier.
        static readonly string[] Names = { "Reach", "Lift", "Shake", "Lower", "FoldLength", "FoldWidth", "Place", "Return", "Pat", "Done" };
        static readonly float[] Durations = { ReachSeconds, LiftSeconds, ShakeSeconds, LowerSeconds, FoldSeconds, FoldSeconds, PlaceSeconds, ReturnSeconds, PatSeconds };
        readonly int seed;
        System.Random rng;
        int beat;
        float age, tempo;
        public bool Running { get; private set; }
        public bool Done => beat == Names.Length - 1;
        public string Phase => Names[beat];
        public int Completed { get; private set; }
        public int PieceIndex => Mathf.Min(Completed, PieceCount - 1);
        public float Progress => Done ? 1 : Mathf.Clamp01(age / Duration);
        public float Duration => Done ? 0 : Durations[beat] * (beat < 8 ? tempo : 1);
        public LaundryTimeline(int seed) { this.seed = seed; Reset(); }
        void Reset()
        {
            rng = new System.Random(seed); beat = 0; age = 0; Completed = 0;
            tempo = TempoMin + (float)rng.NextDouble() * (TempoMax - TempoMin);
        }
        public void Start() { Reset(); Running = true; }
        public void Stop() { Running = false; }
        public void Tick(float dt)
        {
            if (!Running || Done || dt < 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            age += dt;
            while (!Done && age >= Duration)
            {
                age -= Duration;
                if (beat == 6) Completed++; // The piece stays on the stack throughout Return.
                if (beat == 7 && Completed < PieceCount)
                {
                    beat = 0;
                    tempo = TempoMin + (float)rng.NextDouble() * (TempoMax - TempoMin);
                }
                else beat++;
            }
            if (Done) age = 0;
        }
    }

    // Construct in the imported neutral/T pose, then play a seated clip.
    // Tick once in Update; Apply once in LateUpdate, after the Animator and any seat correction.
    // The cloth is placed from the real bones every frame (hips, knees, spine, shoulders), not from fixed guesses.
    public sealed class LaundryRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Linear weight ramp, smoothstep applied to the pose.
        public const float ClothWidth = .38f, ClothLength = .46f, ClothThickness = .01f; // Metres, unfolded thin cloth (scaled by ClothScale when she cannot reach it all).
        public const float SideDistance = .37f, BedBack = .08f, HeapLayer = .014f; // Heap placement in Props local metres (her left).
        public const float StackSide = .24f, StackBack = .08f; // Folded stack on her right, close enough to the right shoulder that the arm reaches it.
        public const float HeapYaw = 12f, HeapJitter = .018f; // Seeded untidy heap angle and lateral scatter.
        public const float ThighRadius = .07f, ThighGap = .01f; // The thigh top is the hips -> knee line plus this; the cloth keeps ThighGap above it.
        public const float LapNearMin = .12f, BellyRadius = .09f, BellyMargin = .03f, KneeHang = .10f; // Lap cloth: near edge in front of hips and belly, at most KneeHang past the knees.
        public const float CsMin = .65f, ReachFraction = .95f; // Smallest cloth scale; share of the arm length a wrist target may use.
        public const float ShakeUp = -.21f, HangPast = .30f; // Held top edge relative to the shoulders; hanging cloth this far beyond the knees (clear of the shins).
        public const float HangMargin = .07f; // Reach kept in reserve for the hanging cloth's top edge, metres (was .07: the hands sat under her chin).
        public const float TorsoHalf = .14f, GatingSoft = .06f; // Torso half width for the clearance push; soft edge of every clearance gate.
        public const float SnapRise = .065f, SnapDrop = .055f, SnapPeak = .35f, SnapBottom = .65f; // One raised beat and downward snap.
        public const float TravelArc = .08f, PlaceArc = .12f, ReturnArc = .05f; // Metres above the pickup/deposit, the stack run and the hand return paths.
        public const float FoldPeak = .5f, LeftHoldAlong = .7f, LeftHoldSeconds = .3f; // Flap lift height as a share of its radius; where the left hand holds the left edge; its slide time share.
        public const float WristGap = .045f, WristLift = .018f; // Wrist behind contact and above fabric, metres.
        public const float ElbowSide = .16f, ElbowAhead = .20f, ElbowDown = .20f, HangElbowSide = .24f; // Pole relative to shoulder, metres (working); while the cloth hangs the elbows open out to HangElbowSide.
        public const float RestElbowSide = .12f, RestElbowAhead = .10f, RestElbowDown = .28f, RestAhead = .05f; // Relaxed hands on the thighs, elbows near the ribs.
        public const float SpineBow = 12f, ChestBow = 5f, HeadBow = 10f, NeckBow = 4f, SideLean = 12f; // Degrees, additive upper-body offsets (head 22 -> 10, neck 8 -> 4, chest 8 -> 5: the old 50 deg buried the chin in the chest).
        public const float EdgeSwitchSeconds = .4f; // The hands slide to the folded cloth's far corners over this long when FoldWidth turns into Place.
        public const float BreathDegrees = .6f, BreathSeconds = 4.2f; // Small chest breathing.
        public const float CurlProximal = 22f, CurlMiddle = 28f, CurlDistal = 14f, ThumbCurl = 14f; // Pinch curl degrees.
        public const float OpenCurl = .15f, GripCurl = 1.6f, HoldGrip = 1.6f; // Finger curl fractions for release and for holding the cloth (a closed pinch; .75 left the fingers splayed).
        public const float AdductAmount = .9f; // How far the four fingers are brought together (0 = neutral spread, 1 = parallel). // Finger curl fractions for release and grasp.
        public const float PatHeight = .045f, PatDepth = .008f; // Single pat above the finished stack, metres.
        public const float WorkingSmile = .12f, DoneSmile = .48f, PatEyes = .35f; // Face channel amplitudes.
        public const float BlinkPeriod = 3.7f, BlinkSeconds = .16f; // Quiet periodic blink.
        public const float RotationTolerance = .001f, AxisEpsilon = .000001f; // Idempotence and degenerate-frame tolerances.
        public const float Half = .5f, Quarter = .25f, FlatToHanging = 90f; // Fold ratios and cloth rotation in degrees.
        public const int ClothSegments = 6; // Length segments of the bendable cloth mesh (ring 3 is the fold line).
        public const float ClothSag = .035f; // Metres the held garment bows away from her while hanging.
        static readonly Color[] Colours = { new Color(.73f, .80f, .89f), new Color(.90f, .76f, .73f), new Color(.86f, .86f, .79f), new Color(.69f, .78f, .71f), new Color(.78f, .72f, .84f) }; // Five muted fabrics.

        // Body measurements of one frame, in world space.
        struct Geo
        {
            public Vector3 hp, forward, right;
            public float dk, dv, latMax, belly;
            public float ThighTopY(float d) => hp.y + dv * Mathf.Clamp01(d / dk) + ThighRadius + ThighGap;
            static float Soft(float x) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(x));
            // How far a point must move forward to clear the belly (0 = clear). Gated softly by width and height so there is no jump.
            public float TorsoPush(Vector3 p)
            {
                var o = p - hp;
                float d = Vector3.Dot(o, forward), lat = Mathf.Abs(Vector3.Dot(o, right)), h = Vector3.Dot(o, Vector3.up);
                float deficit = belly + BellyMargin - d;
                return deficit <= 0 ? 0 : deficit * (1 - Soft((lat - TorsoHalf) / GatingSoft)) * Soft((h - .02f) / GatingSoft);
            }
            // How far a point must move up to clear the thigh top (0 = clear); only over the thighs (hips to just past the knees).
            public float ThighPush(Vector3 p)
            {
                var o = p - hp;
                float d = Vector3.Dot(o, forward), lat = Mathf.Abs(Vector3.Dot(o, right));
                float gate = Soft((d - .02f) / GatingSoft) * (1 - Soft((d - (dk + .08f)) / GatingSoft)) * (1 - Soft((lat - latMax) / GatingSoft));
                float deficit = ThighTopY(d) - p.y;
                return deficit <= 0 ? 0 : deficit * gate;
            }
        }

        readonly Animator anim;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hands = new Transform[2];
        readonly Transform[] legUpper = new Transform[2], legLower = new Transform[2];
        readonly Transform[,,] fingers = new Transform[2, 4, 3];
        readonly Transform[,] thumbs = new Transform[2, 3];
        readonly Transform[][] proximals = { new Transform[4], new Transform[4] }, intermediates = { new Transform[4], new Transform[4] };
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly Transform[] pieces = new Transform[LaundryTimeline.PieceCount];
        readonly Vector3[] heapPositions = new Vector3[LaundryTimeline.PieceCount];
        readonly Quaternion[] heapRotations = new Quaternion[LaundryTimeline.PieceCount];
        readonly Material[] materials = new Material[LaundryTimeline.PieceCount];
        readonly Mesh[] meshes = new Mesh[LaundryTimeline.PieceCount];
        readonly float[] bent = new float[LaundryTimeline.PieceCount], folds = new float[LaundryTimeline.PieceCount], pieceScale = new float[LaundryTimeline.PieceCount];
        readonly Vector3[] shapeSize = new Vector3[LaundryTimeline.PieceCount];
        readonly Quaternion[] before, written;
        readonly LaundryTimeline clock;
        bool wanted, disposed, hasWritten, scaleKnown;
        float time, switchTime = 1, clothScale = 1;
        string lastPhase = "";
        Vector3 switchFromL, switchFromR;
        static readonly Vector3[] Samples = BuildSamples();

        // Caller places this WORLD prop frame on the mattress: local +x her right, +z her forward, +y up.
        // Leave scale at one. It is deliberately not parented to the moving character.
        public readonly Transform Props;
        public bool Active => !disposed && (wanted || Weight > 0);
        public float Weight { get; private set; }
        public bool Done => clock.Done;
        public string Phase => clock.Phase;
        public float PhaseProgress => clock.Progress;
        public int Completed => clock.Completed;
        public float ClothScale => clothScale; // Current garment scale (1 = full size), shrunk when the lap is short or the far edge out of reach.
        public float PieceScale(int index) => pieceScale[index];
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen => 0;
        public Vector3 LeftContact { get; private set; }
        public Vector3 RightContact { get; private set; }
        public Vector3 LeftWristTarget { get; private set; }
        public Vector3 RightWristTarget { get; private set; }
        public float GripCurlNow { get; private set; } // Finger curl fraction applied this frame (OpenCurl .. HoldGrip).
        public Transform Piece(int index) => pieces[index];

        static Vector3[] BuildSamples()
        {
            var s = new Vector3[9]; int n = 0;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) s[n++] = new Vector3(x * .5f, 0, z * .5f);
            return s;
        }

        public LaundryRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("LaundryRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new LaundryTimeline(seed);
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("LaundryRig needs hips, spine and head.");
            legUpper[0] = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); legUpper[1] = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            legLower[0] = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); legLower[1] = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hands[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[s] == null || lower[s] == null || hands[s] == null) throw new InvalidOperationException("LaundryRig needs both arms.");
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                    fingers[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j));
                for (int j = 0; j < 3; j++) thumbs[s, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + j));
                var direction = fingers[s, 1, 0] != null ? fingers[s, 1, 0].position - hands[s].position : hands[s].position - lower[s].position;
                var palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < AxisEpsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                handFrame[s] = Quaternion.Inverse(hands[s].rotation) * Quaternion.LookRotation(direction, palm);
            }
            for (int s = 0; s < 2; s++) for (int f = 0; f < 4; f++) { proximals[s][f] = fingers[s, f, 0]; intermediates[s][f] = fingers[s, f, 1]; }
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No laundry material shader found.");
            Props = new GameObject("LaundryProps").transform;
            var rng = new System.Random(seed);
            for (int i = 0; i < pieces.Length; i++)
            {
                var g = new GameObject("LaundryPiece" + i);
                g.transform.SetParent(Props, false); pieces[i] = g.transform;
                pieceScale[i] = 1; shapeSize[i] = Vector3.one;
                meshes[i] = new Mesh { name = "LaundryCloth" + i }; meshes[i].MarkDynamic(); BuildCloth(meshes[i], 0, 1, 0, Vector3.one);
                g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                materials[i] = new Material(shader); materials[i].color = Colours[i];
                g.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
                heapPositions[i] = new Vector3(-SideDistance + ((float)rng.NextDouble() * 2 - 1) * HeapJitter,
                    ClothThickness * Half + (pieces.Length - 1 - i) * HeapLayer, BedBack);
                heapRotations[i] = Quaternion.Euler(0, ((float)rng.NextDouble() * 2 - 1) * HeapYaw, 0);
            }
            Props.gameObject.SetActive(false);
        }

        // Where a unit-box point goes when the part beyond the fold line (ring 3, z = 0) is turned over about the top surface; fold 0..1 = 0..180 degrees.
        // The turn is done in metres (size is the piece's non-uniform scale). FoldPeak < 1 flattens the arc so the corners rise about half the flap length.
        static Vector3 FoldUnit(Vector3 unit, Vector3 size, float fold)
        {
            if (fold <= 0 || unit.z < -.0001f) return unit;
            float ang = fold * Mathf.PI * (unit.z < .0001f ? .5f : 1f);
            float s = Mathf.Sin(ang), c = Mathf.Cos(ang);
            float qy = (unit.y - .5f) * size.y, qz = unit.z * size.z;
            float ny = qz * s * FoldPeak + qy * c, nz = qz * c - qy * s;
            return new Vector3(unit.x, .5f + ny / size.y, nz / size.z);
        }

        // A thin box of unit width/length/thickness cut into ClothSegments along z. bend bows the middle along local -y by ClothSag metres
        // (ySize is the piece's local y scale, so the bow stays in metres; the hanging garment's local -y is "away from her").
        // fold turns the far half over onto the near half (see FoldUnit); size is the piece scale.
        static void BuildCloth(Mesh m, float bend, float ySize, float fold, Vector3 size)
        {
            int rings = ClothSegments + 1;
            var v = new Vector3[rings * 4];
            float dy = -bend * ClothSag / Mathf.Max(ySize, .0001f);
            for (int k = 0; k < rings; k++)
            {
                float u = (float)k / ClothSegments, z = u - .5f, y = dy * Mathf.Sin(Mathf.PI * u);
                v[k * 4] = FoldUnit(new Vector3(-.5f, .5f + y, z), size, fold); v[k * 4 + 1] = FoldUnit(new Vector3(.5f, .5f + y, z), size, fold);
                v[k * 4 + 2] = FoldUnit(new Vector3(.5f, -.5f + y, z), size, fold); v[k * 4 + 3] = FoldUnit(new Vector3(-.5f, -.5f + y, z), size, fold);
            }
            m.vertices = v;
            if (m.triangles.Length == 0)
            {
                var t = new List<int>();
                for (int k = 0; k < ClothSegments; k++) for (int e = 0; e < 4; e++)
                {
                    int a = k * 4 + e, b = k * 4 + (e + 1) % 4, c = (k + 1) * 4 + e, d = (k + 1) * 4 + (e + 1) % 4;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
                t.Add(0); t.Add(1); t.Add(2); t.Add(0); t.Add(2); t.Add(3);
                int o = ClothSegments * 4;
                t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2);
                m.triangles = t.ToArray();
            }
            m.RecalculateNormals(); m.RecalculateBounds();
        }
        void SetShape(int i, float bend, float fold, Vector3 size)
        {
            bool changed = Mathf.Abs(bent[i] - bend) >= .01f || Mathf.Abs(folds[i] - fold) >= .002f
                || (fold > 0 && shapeSize[i] != size) || (bend > 0 && Mathf.Abs(shapeSize[i].y - size.y) > .0001f);
            if (!changed) return;
            bent[i] = bend; folds[i] = fold; shapeSize[i] = size; BuildCloth(meshes[i], bend, size.y, fold, size);
        }

        Transform Bone(HumanBodyBones id)
        {
            var b = anim.GetBoneTransform(id);
            if (b != null && !controlled.Contains(b)) controlled.Add(b);
            return b;
        }
        static float Ease(float t) => Mathf.SmoothStep(0, 1, t);
        static Vector3 Arc(Vector3 a, Vector3 b, float t, Vector3 up, float height)
            => Vector3.Lerp(a, b, Ease(t)) + up * (Mathf.Sin(Mathf.PI * t) * height);
        static void Turn(Transform bone, Vector3 axis, float degrees)
        { if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }

        // Path from a to b measured from the hips: forward and height lead, the sideways move comes later, so a cloth leaving the heap
        // on her left does not swing through her arm and belly.
        static Vector3 PathPoint(in Geo g, Vector3 a, Vector3 b, float t, float arc)
        {
            var da = a - g.hp; var db = b - g.hp;
            float u = Ease(t), v = Ease(Mathf.Clamp01((t - .25f) / .75f));
            float lat = Mathf.Lerp(Vector3.Dot(da, g.right), Vector3.Dot(db, g.right), v);
            float fw = Mathf.Lerp(Vector3.Dot(da, g.forward), Vector3.Dot(db, g.forward), u);
            float h = Mathf.Lerp(Vector3.Dot(da, Vector3.up), Vector3.Dot(db, Vector3.up), u) + Mathf.Sin(Mathf.PI * t) * arc;
            return g.hp + g.right * lat + g.forward * fw + Vector3.up * h;
        }

        // Pushes the piece forward out of her belly and up out of her thighs (soft gates, so no pops); strength 0..1.
        static void Clear(in Geo g, ref Vector3 centre, Quaternion rot, Vector3 size, float strength)
        {
            float push = 0;
            for (int i = 0; i < Samples.Length; i++) push = Mathf.Max(push, g.TorsoPush(centre + rot * Vector3.Scale(size, Samples[i])));
            centre += g.forward * (push * strength);
            push = 0;
            for (int i = 0; i < Samples.Length; i++) push = Mathf.Max(push, g.ThighPush(centre + rot * Vector3.Scale(size, Samples[i])));
            centre += Vector3.up * (push * strength);
        }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LaundryRig));
            if (wanted) return;
            wanted = true; clock.Start(); time = 0; Smile = EyesClosed = 0; scaleKnown = false; clothScale = 1;
            for (int i = 0; i < pieceScale.Length; i++) pieceScale[i] = 1;
        }
        public void Stop() { wanted = false; clock.Stop(); Smile = EyesClosed = 0; }
        public void Tick(float dt)
        {
            if (disposed || dt < 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (!wanted) return;
            clock.Tick(dt); time += dt;
            Smile = (Done ? DoneSmile : WorkingSmile) * Weight;
            float blink = time % BlinkPeriod;
            EyesClosed = Mathf.Max(blink < BlinkSeconds ? Mathf.Sin(Mathf.PI * blink / BlinkSeconds) : 0,
                Phase == "Pat" ? PatEyes * Mathf.Sin(Mathf.PI * clock.Progress) : 0) * Weight;
        }

        Vector3 Stack(int index) => Props.TransformPoint(new Vector3(StackSide,
            ClothThickness * 4 * (index + Half), StackBack));

        // All pose and prop writes live here; time never advances in Apply.
        public void Apply(float dt)
        {
            if (disposed) return;
            // Remove the preceding overlay if Animator did not refresh a controlled bone (paused/culling).
            if (hasWritten) for (int i = 0; i < controlled.Count; i++)
                if (Quaternion.Angle(controlled[i].localRotation, written[i]) < RotationTolerance) controlled[i].localRotation = before[i];
            hasWritten = false;
            Props.gameObject.SetActive(Weight > 0);
            if (Weight <= 0) return;
            for (int i = 0; i < controlled.Count; i++) before[i] = controlled[i].localRotation;
            float w = Ease(Weight), t = clock.Progress, e = Ease(t);
            string phase = Phase;
            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(anim.transform.forward, up).normalized;
            var right = Vector3.Cross(up, forward).normalized;
            var flat = Quaternion.LookRotation(forward, up);
            // The held edge is local +z. Hanging, it is the top edge (local -z, the body of the cloth, points down); on the lap it is the far edge.
            var hanging = Quaternion.AngleAxis(-FlatToHanging, right) * flat;

            // Upper body first, so every measurement below sees her real bowed pose.
            float lean = phase == "Reach" || phase == "Lift" ? SideLean * (phase == "Reach" ? e : 1 - e)
                : phase == "Place" || phase == "Return" ? -SideLean * (phase == "Place" ? e : 1 - e) : 0;
            Turn(spine, right, SpineBow * w); Turn(spine, forward, lean * w);
            if (chest != spine) Turn(chest, right, (ChestBow + BreathDegrees * Mathf.Sin(time * 2 * Mathf.PI / BreathSeconds)) * w);
            Turn(neck, right, NeckBow * w); Turn(head, right, HeadBow * w);

            // Measurements: thighs (hips -> knees), belly, shoulders and arm length.
            var hp = hips.position;
            var kneeL = legLower[0] != null ? legLower[0].position : hp + forward * .4f - up * .05f;
            var kneeR = legLower[1] != null ? legLower[1].position : hp + forward * .4f - up * .05f;
            var kneeMid = (kneeL + kneeR) * Half;
            var g = new Geo
            {
                hp = hp, forward = forward, right = right,
                dk = Mathf.Max(.25f, Vector3.Dot(kneeMid - hp, forward)), dv = Vector3.Dot(kneeMid - hp, up),
                latMax = Mathf.Max(Mathf.Abs(Vector3.Dot(kneeL - hp, right)), Mathf.Abs(Vector3.Dot(kneeR - hp, right))) + ThighRadius,
                belly = Vector3.Dot(spine.position - hp, forward) + BellyRadius
            };
            float near = Mathf.Clamp(Mathf.Max(LapNearMin, g.belly + BellyMargin), LapNearMin, g.dk * .6f);
            float slope = Mathf.Clamp(Mathf.Atan2(g.dv, g.dk) * Mathf.Rad2Deg, -25f, 25f);
            var lapRot = Quaternion.AngleAxis(-slope, right) * flat; // thigh slope: positive = knees higher
            var lf = lapRot * Vector3.forward; var lu = lapRot * Vector3.up;
            var surface = hp + forward * near; surface.y = g.ThighTopY(near);
            float shF = 0, shUp = 0, shLat = 0, armLen = 0;
            for (int s = 0; s < 2; s++)
            {
                var o = upper[s].position - hp;
                shF += Vector3.Dot(o, forward) * Half; shUp += Vector3.Dot(o, up) * Half; shLat += Mathf.Abs(Vector3.Dot(o, right)) * Half;
                armLen += ((lower[s].position - upper[s].position).magnitude + (hands[s].position - lower[s].position).magnitude) * Half;
            }
            float arm = ReachFraction * armLen;
            float dyFar = surface.y + ClothThickness * Half - (hp.y + shUp), dzFar = Mathf.Abs(ClothWidth * Half - shLat);
            float farReach = shF + Mathf.Sqrt(Mathf.Max(0, arm * arm - dyFar * dyFar - dzFar * dzFar)) + WristGap;
            float scaleTarget = Mathf.Clamp((Mathf.Min(g.dk + KneeHang, farReach) - near) / ClothLength, CsMin, 1);
            if (!scaleKnown) { clothScale = scaleTarget; scaleKnown = true; }
            else if (phase == "Reach") clothScale = Mathf.MoveTowards(clothScale, scaleTarget, Mathf.Max(0, dt) * .5f);
            float cw = ClothWidth * clothScale, cl = ClothLength * clothScale, th = ClothThickness;

            int index = clock.PieceIndex;
            if (!(phase == "Return" || phase == "Pat" || Done)) pieceScale[index] = clothScale;
            var heap = Props.TransformPoint(heapPositions[index]);
            var heapRotation = Props.rotation * heapRotations[index];
            var centre = heap; var rotation = heapRotation;
            var size = new Vector3(cw, th, cl);
            // Lap: near edge on the thigh top, cloth following the thigh slope. Folded pieces sit on it as thicker boxes.
            var lapCentre = surface + lf * (cl * Half) + lu * (th * Half);
            var onceCentre = surface + lf * (cl * Quarter) + lu * th;
            var twiceCentre = surface + lf * (cl * Quarter) - right * (cw * Quarter) + lu * (th * 2);
            var folded = new Vector3(cw * Half, th * 4, cl * Half);
            // Hanging: top edge a little below the shoulders, beyond the knees where the shins and thighs are out of the way, within reach.
            float dyHang = ShakeUp - WristGap, dzHang = Mathf.Abs(cw * Half - shLat);
            float hangMax = shF + Mathf.Sqrt(Mathf.Max(0, arm * arm - dyHang * dyHang - dzHang * dzHang)) - HangMargin;
            float hangX = Mathf.Clamp(Mathf.Min(g.dk + HangPast, hangMax), near + .15f, g.dk + HangPast);
            var raisedEdge = hp + forward * hangX + up * (shUp + ShakeUp);
            var raised = raisedEdge - up * (cl * Half);
            float grip = GripCurl;
            var restL = Rest(0, hp, forward, right, up); var restR = Rest(1, hp, forward, right, up);
            Vector3 contactL, contactR;
            float fold = 0, bend = 0;
            // Render all settled pieces from the clock, even when several beats were skipped by Tick.
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i].position = i < Completed ? Stack(i) : Props.TransformPoint(heapPositions[i]);
                pieces[i].rotation = i < Completed ? Props.rotation : Props.rotation * heapRotations[i];
                pieces[i].localScale = i < Completed ? new Vector3(ClothWidth * pieceScale[i] * Half, th * 4, ClothLength * pieceScale[i] * Half) : size;
            }
            switch (phase)
            {
                case "Reach": break;
                case "Lift": centre = PathPoint(g, heap, raised, t, TravelArc); rotation = Quaternion.Slerp(heapRotation, hanging, e); bend = e; break;
                case "Shake":
                    float snap = t < SnapPeak ? Mathf.Lerp(0, SnapRise, Ease(t / SnapPeak)) : t < SnapBottom
                        ? Mathf.Lerp(SnapRise, -SnapDrop, Ease((t - SnapPeak) / (SnapBottom - SnapPeak)))
                        : Mathf.Lerp(-SnapDrop, 0, Ease((t - SnapBottom) / (1 - SnapBottom)));
                    centre = raised + up * snap; rotation = hanging; bend = 1; break;
                case "Lower":
                    {
                        // The hands carry the far edge down and forward; the cloth swings in toward her and lies on the thighs.
                        rotation = Quaternion.Slerp(hanging, lapRot, Ease(Mathf.Clamp01(t / .75f)));
                        var edge = PathPoint(g, raisedEdge, surface + lf * cl + lu * (th * Half), t, 0);
                        centre = edge - rotation * Vector3.forward * (cl * Half); bend = 1 - e; break;
                    }
                case "FoldLength": centre = lapCentre; rotation = lapRot; fold = e; break;
                case "FoldWidth":
                    centre = onceCentre; rotation = Quaternion.AngleAxis(90, lu) * lapRot; size = new Vector3(cl * Half, th * 2, cw); fold = e; break;
                case "Place": centre = PathPoint(g, twiceCentre, Stack(index), t, PlaceArc); rotation = Quaternion.Slerp(lapRot, Props.rotation, e); size = folded; break;
                default: centre = Stack(Mathf.Max(0, Completed - 1)); rotation = Props.rotation; size = folded; break;
            }
            if (phase == "Lift" || phase == "Shake" || phase == "Lower" || phase == "Place")
                Clear(g, ref centre, rotation, size, phase == "Lift" ? Mathf.Clamp01(t * 3) : phase == "Place" ? 1 - Ease(Mathf.Clamp01((t - .6f) / .4f)) : 1); // the stack lies beside the thigh: no push once the piece is down
            if (phase != "Return" && phase != "Pat" && !Done)
            { pieces[index].SetPositionAndRotation(centre, rotation); pieces[index].localScale = size; }
            for (int i = 0; i < pieces.Length; i++)
                SetShape(i, i == index ? bend : 0, i == index ? fold : 0, i == index ? size : pieces[i].localScale);

            // The held edge is local +z; its corners are the contacts.
            var edgeMid = centre + rotation * Vector3.forward * (size.z * Half);
            contactL = edgeMid - rotation * Vector3.right * (size.x * Half);
            contactR = edgeMid + rotation * Vector3.right * (size.x * Half);
            Vector3 FlapPoint(float ux, float uz) => centre + rotation * Vector3.Scale(size, FoldUnit(new Vector3(ux, .5f, uz), size, fold));
            if (phase == "Reach")
            { contactL = Vector3.Lerp(restL, heap, e); contactR = restR; grip = Mathf.Lerp(OpenCurl, GripCurl, e); }
            if (phase == "Lift")
            { contactL = Vector3.Lerp(heap, contactL, e); contactR = Vector3.Lerp(restR, contactR, e); }
            if (phase == "FoldLength")
            {
                // Both hands hold the far corners of the flap and turn it over onto the near half.
                contactL = FlapPoint(-.5f, .5f); contactR = FlapPoint(.5f, .5f);
            }
            if (phase == "FoldWidth")
            {
                // The right hand carries the near-right corner of the flap to the near-left corner; the left hand slides up the
                // left edge of the once-folded cloth and holds it there.
                float q = Ease(Mathf.Clamp01(t / LeftHoldSeconds)) * LeftHoldAlong;
                contactR = FlapPoint(.5f, .5f);
                contactL = Vector3.Lerp(FlapPoint(.5f, -.5f), FlapPoint(-.5f, -.5f), q);
            }
            if (phase == "Return" || phase == "Pat" || Done)
            {
                int k = Mathf.Max(0, Completed - 1);
                var stackSize = new Vector3(ClothWidth * pieceScale[k] * Half, th * 4, ClothLength * pieceScale[k] * Half);
                var top = Stack(k) + Props.up * stackSize.y * Half;
                if (phase == "Return")
                {
                    var stackEdge = Stack(k) + Props.forward * (stackSize.z * Half);
                    contactL = Arc(stackEdge - Props.right * (stackSize.x * Half), restL, t, up, ReturnArc);
                    contactR = Arc(stackEdge + Props.right * (stackSize.x * Half), restR, t, up, ReturnArc);
                }
                else
                {
                    contactL = restL; contactR = restR;
                    if (phase == "Pat") contactR = Vector3.Lerp(restR, top + up * (PatHeight - (PatHeight + PatDepth) * Mathf.Sin(Mathf.PI * t)), Mathf.Sin(Mathf.PI * t));
                }
                grip = OpenCurl;
            }
            if (phase == "Place" && lastPhase == "FoldWidth") { switchFromL = LeftContact; switchFromR = RightContact; switchTime = 0; }
            if (phase != "Place") switchTime = 1;
            if (switchTime < 1)
            {
                // the hands end FoldWidth at the near-left corner and the left edge; they slide to the folded cloth's far corners, not snap
                switchTime = Mathf.Min(1, switchTime + dt / EdgeSwitchSeconds);
                float k = Ease(switchTime);
                contactL = Vector3.Lerp(switchFromL, contactL, k); contactR = Vector3.Lerp(switchFromR, contactR, k);
            }
            lastPhase = phase; GripCurlNow = grip;
            LeftContact = contactL; RightContact = contactR;
            float heldVertical = phase == "Lift" ? e : phase == "Shake" ? 1 : phase == "Lower" ? 1 - e : 0;
            // Fingers point forward, palms down, on the lap and on the thighs; up and toward her while the cloth hangs.
            var palm = Vector3.Slerp(-up, -forward, heldVertical);
            var fingersDirection = Vector3.Slerp(forward, up, heldVertical);
            bool resting = phase == "Return" || phase == "Pat" || Done;
            for (int s = 0; s < 2; s++)
            {
                var contact = s == 0 ? contactL : contactR;
                var target = contact - fingersDirection * WristGap - palm * WristLift;
                if (s == 0) LeftWristTarget = target; else RightWristTarget = target;
                float side = s == 0 ? -1 : 1;
                var pole = resting
                    ? upper[s].position + right * (side * RestElbowSide) + forward * RestElbowAhead - up * RestElbowDown
                    : upper[s].position + right * (side * Mathf.Lerp(ElbowSide, HangElbowSide, heldVertical)) + forward * ElbowAhead - up * ElbowDown;
                SofaRig.TwoBone(upper[s], lower[s], hands[s], target, pole, w);
                hands[s].rotation = Quaternion.Slerp(hands[s].rotation, Quaternion.LookRotation(fingersDirection, palm) * Quaternion.Inverse(handFrame[s]), w);
                HandPose.Adduct(proximals[s], intermediates[s], hands[s].rotation * handFrame[s] * Vector3.up, AdductAmount * w);
                var curlAxis = Vector3.Cross(fingersDirection, palm).normalized;
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                    Turn(fingers[s, f, j], curlAxis, (j == 0 ? CurlProximal : j == 1 ? CurlMiddle : CurlDistal) * grip * w);
                for (int j = 0; j < 3; j++) Turn(thumbs[s, j], curlAxis, ThumbCurl * grip * w);
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }

        // Relaxed hand: on the thigh (hips -> knee line, 55 % down), fingers forward.
        Vector3 Rest(int s, Vector3 hp, Vector3 forward, Vector3 right, Vector3 up)
        {
            var u = legUpper[s]; var l = legLower[s];
            var thigh = u != null && l != null ? Vector3.Lerp(u.position, l.position, .55f) : hp + right * ((s == 0 ? -1 : 1) * .1f) + forward * .2f;
            return thigh + up * (ThighRadius - .005f) + forward * RestAhead;
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
            if (hasWritten) for (int i = 0; i < controlled.Count; i++)
                if (controlled[i] != null && Quaternion.Angle(controlled[i].localRotation, written[i]) < RotationTolerance) controlled[i].localRotation = before[i];
            disposed = true; wanted = false; Weight = Smile = EyesClosed = 0; clock.Stop();
            SofaRig.Kill(Props != null ? Props.gameObject : null);
            foreach (var mesh in meshes) if (mesh != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
            foreach (var material in materials)
                if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); }
        }
    }
}
