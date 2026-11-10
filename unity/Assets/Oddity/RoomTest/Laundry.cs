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
    public sealed class LaundryRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Linear weight ramp, smoothstep applied to the pose.
        public const float ClothWidth = .38f, ClothLength = .46f, ClothThickness = .01f; // Metres, unfolded thin cloth.
        public const float SideDistance = .37f, BedBack = -.14f, HeapLayer = .014f; // Heap/stack placement in Props local metres.
        public const float HeapYaw = 12f, HeapJitter = .018f; // Seeded untidy heap angle and lateral scatter.
        public const float LapAhead = .18f, LapUp = .10f, LiftAhead = .30f, LiftUp = .34f; // Cloth centre / top edge relative to hips.
        public const float SnapRise = .065f, SnapDrop = .055f, SnapPeak = .35f, SnapBottom = .65f; // One raised beat and downward snap.
        public const float TravelArc = .08f, FoldArc = .06f; // Metres above the pickup/deposit and folding paths.
        public const float WristGap = .045f, WristLift = .018f; // Wrist behind contact and above fabric, metres.
        public const float ElbowSide = .24f, ElbowAhead = .15f, ElbowDown = .22f; // Pole relative to shoulder, metres.
        public const float SpineBow = 12f, ChestBow = 8f, HeadBow = 22f, NeckBow = 8f, SideLean = 12f; // Degrees, additive upper-body offsets.
        public const float BreathDegrees = .6f, BreathSeconds = 4.2f; // Small chest breathing.
        public const float CurlProximal = 22f, CurlMiddle = 28f, CurlDistal = 14f, ThumbCurl = 14f; // Pinch curl degrees.
        public const float OpenCurl = .15f, GripCurl = .75f; // Finger curl fractions for release and grasp.
        public const float PatHeight = .045f, PatDepth = .008f; // Single pat above the finished stack, metres.
        public const float WorkingSmile = .12f, DoneSmile = .48f, PatEyes = .35f; // Face channel amplitudes.
        public const float BlinkPeriod = 3.7f, BlinkSeconds = .16f; // Quiet periodic blink.
        public const float RotationTolerance = .001f, AxisEpsilon = .000001f; // Idempotence and degenerate-frame tolerances.
        public const float Half = .5f, Quarter = .25f, FlatToHanging = 90f; // Fold ratios and cloth rotation in degrees.
        public const int ClothSegments = 6; // Length segments of the bendable cloth mesh.
        public const float ClothSag = .035f; // Metres the held garment bows away from her while hanging.
        static readonly Color[] Colours = { new Color(.73f, .80f, .89f), new Color(.90f, .76f, .73f), new Color(.86f, .86f, .79f), new Color(.69f, .78f, .71f), new Color(.78f, .72f, .84f) }; // Five muted fabrics.

        readonly Animator anim;
        readonly Transform hips, spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hands = new Transform[2];
        readonly Transform[,,] fingers = new Transform[2, 4, 3];
        readonly Transform[,] thumbs = new Transform[2, 3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly List<Transform> controlled = new List<Transform>();
        readonly Transform[] pieces = new Transform[LaundryTimeline.PieceCount];
        readonly Vector3[] heapPositions = new Vector3[LaundryTimeline.PieceCount];
        readonly Quaternion[] heapRotations = new Quaternion[LaundryTimeline.PieceCount];
        readonly Material[] materials = new Material[LaundryTimeline.PieceCount];
        readonly Mesh[] meshes = new Mesh[LaundryTimeline.PieceCount];
        readonly float[] bent = new float[LaundryTimeline.PieceCount];
        readonly Quaternion[] before, written;
        readonly LaundryTimeline clock;
        bool wanted, disposed, hasWritten;
        float time;

        // Caller places this WORLD prop frame on the mattress: local +x her right, +z her forward, +y up.
        // Leave scale at one. It is deliberately not parented to the moving character.
        public readonly Transform Props;
        public bool Active => !disposed && (wanted || Weight > 0);
        public float Weight { get; private set; }
        public bool Done => clock.Done;
        public string Phase => clock.Phase;
        public int Completed => clock.Completed;
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen => 0;
        public Vector3 LeftContact { get; private set; }
        public Vector3 RightContact { get; private set; }
        public Vector3 LeftWristTarget { get; private set; }
        public Vector3 RightWristTarget { get; private set; }
        public Transform Piece(int index) => pieces[index];

        public LaundryRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("LaundryRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; clock = new LaundryTimeline(seed);
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (hips == null || spine == null || head == null) throw new InvalidOperationException("LaundryRig needs hips, spine and head.");
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
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No laundry material shader found.");
            Props = new GameObject("LaundryProps").transform;
            var rng = new System.Random(seed);
            for (int i = 0; i < pieces.Length; i++)
            {
                var g = new GameObject("LaundryPiece" + i);
                g.transform.SetParent(Props, false); pieces[i] = g.transform;
                meshes[i] = new Mesh { name = "LaundryCloth" + i }; meshes[i].MarkDynamic(); BuildCloth(meshes[i], 0, 1);
                g.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                materials[i] = new Material(shader); materials[i].color = Colours[i];
                g.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
                heapPositions[i] = new Vector3(-SideDistance + ((float)rng.NextDouble() * 2 - 1) * HeapJitter,
                    ClothThickness * Half + (pieces.Length - 1 - i) * HeapLayer, BedBack);
                heapRotations[i] = Quaternion.Euler(0, ((float)rng.NextDouble() * 2 - 1) * HeapYaw, 0);
            }
            Props.gameObject.SetActive(false);
        }

        // A thin box of unit width/length/thickness cut into ClothSegments along z; bend bows the middle along local +y by ClothSag metres
        // (ySize is the piece's local y scale, so the bow stays in metres). Local +y becomes "away from her" when the garment hangs.
        static void BuildCloth(Mesh m, float bend, float ySize)
        {
            int rings = ClothSegments + 1;
            var v = new Vector3[rings * 4];
            float dy = bend * ClothSag / Mathf.Max(ySize, .0001f);
            for (int k = 0; k < rings; k++)
            {
                float u = (float)k / ClothSegments, z = u - .5f, y = dy * Mathf.Sin(Mathf.PI * u);
                v[k * 4] = new Vector3(-.5f, .5f + y, z); v[k * 4 + 1] = new Vector3(.5f, .5f + y, z);
                v[k * 4 + 2] = new Vector3(.5f, -.5f + y, z); v[k * 4 + 3] = new Vector3(-.5f, -.5f + y, z);
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
        void SetBend(int i, float amount, float ySize)
        {
            if (Mathf.Abs(bent[i] - amount) < .01f) return;
            bent[i] = amount; BuildCloth(meshes[i], amount, ySize);
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

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LaundryRig));
            if (wanted) return;
            wanted = true; clock.Start(); time = 0; Smile = EyesClosed = 0;
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

        Vector3 Stack(int index) => Props.TransformPoint(new Vector3(SideDistance,
            ClothThickness * 4 * (index + Half), BedBack));

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
            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(anim.transform.forward, up).normalized;
            var right = Vector3.Cross(up, forward).normalized;
            var flat = Quaternion.LookRotation(forward, up);
            var hanging = Quaternion.AngleAxis(FlatToHanging, right) * flat;
            var lap = hips.position + forward * LapAhead + up * LapUp;
            var raised = hips.position + forward * LiftAhead + up * LiftUp - up * ClothLength * Half;
            int index = clock.PieceIndex;
            var heap = Props.TransformPoint(heapPositions[index]);
            var heapRotation = Props.rotation * heapRotations[index];
            var centre = heap; var rotation = heapRotation;
            var size = new Vector3(ClothWidth, ClothThickness, ClothLength);
            var onceCentre = lap - forward * ClothLength * Quarter;
            var twiceCentre = onceCentre - right * ClothWidth * Quarter;
            var folded = new Vector3(ClothWidth * Half, ClothThickness * 4, ClothLength * Half);
            float grip = GripCurl;
            var restL = lap - right * ClothWidth * Half;
            var restR = lap + right * ClothWidth * Half;
            Vector3 contactL, contactR;
            // Render all settled pieces from the clock, even when several beats were skipped by Tick.
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i].position = i < Completed ? Stack(i) : Props.TransformPoint(heapPositions[i]);
                pieces[i].rotation = i < Completed ? Props.rotation : Props.rotation * heapRotations[i];
                pieces[i].localScale = i < Completed ? folded : size;
            }
            switch (Phase)
            {
                case "Reach": break;
                case "Lift": centre = Arc(heap, raised, t, up, TravelArc); rotation = Quaternion.Slerp(heapRotation, hanging, e); break;
                case "Shake":
                    float snap = t < SnapPeak ? Mathf.Lerp(0, SnapRise, Ease(t / SnapPeak)) : t < SnapBottom
                        ? Mathf.Lerp(SnapRise, -SnapDrop, Ease((t - SnapPeak) / (SnapBottom - SnapPeak)))
                        : Mathf.Lerp(-SnapDrop, 0, Ease((t - SnapBottom) / (1 - SnapBottom)));
                    centre = raised + up * snap; rotation = hanging; break;
                case "Lower": centre = Vector3.Lerp(raised, lap, e); rotation = Quaternion.Slerp(hanging, flat, e); break;
                case "FoldLength":
                    centre = Vector3.Lerp(lap, onceCentre, e); rotation = flat;
                    size.z *= Mathf.Lerp(1, Half, e); size.y *= Mathf.Lerp(1, 2, e); break;
                case "FoldWidth":
                    centre = Vector3.Lerp(onceCentre, twiceCentre, e); rotation = flat;
                    size = new Vector3(ClothWidth * Mathf.Lerp(1, Half, e), ClothThickness * Mathf.Lerp(2, 4, e), ClothLength * Half); break;
                case "Place": centre = Arc(twiceCentre, Stack(index), t, up, TravelArc); rotation = Quaternion.Slerp(flat, Props.rotation, e); size = folded; break;
                default: centre = Stack(Mathf.Max(0, Completed - 1)); rotation = Props.rotation; size = folded; break;
            }
            if (Phase != "Return" && Phase != "Pat" && !Done)
            { pieces[index].SetPositionAndRotation(centre, rotation); pieces[index].localScale = size; }
            for (int i = 0; i < pieces.Length; i++)
                SetBend(i, i == index && (Phase == "Lift" || Phase == "Shake" || Phase == "Lower") ? (Phase == "Lift" ? e : Phase == "Shake" ? 1 : 1 - e) : 0, i == index ? size.y : pieces[i].localScale.y);

            // The held edge is local -z: it becomes the upper edge when the garment hangs down.
            float edgeSide = Phase == "Place" ? 1 : -1;
            var edge = centre + rotation * Vector3.forward * size.z * Half * edgeSide;
            contactL = edge - rotation * Vector3.right * size.x * Half;
            contactR = edge + rotation * Vector3.right * size.x * Half;
            if (Phase == "Reach")
            { contactL = Vector3.Lerp(restL, heap, e); contactR = restR; grip = Mathf.Lerp(OpenCurl, GripCurl, e); }
            if (Phase == "Lift")
            { contactL = Vector3.Lerp(heap, contactL, e); contactR = Vector3.Lerp(restR, contactR, e); }
            if (Phase == "FoldLength")
            {
                // Both hands turn the near half inward; the box shortens to half length.
                var movingEdge = Arc(lap - forward * ClothLength * Half, lap, t, up, FoldArc);
                contactL = movingEdge - right * ClothWidth * Half; contactR = movingEdge + right * ClothWidth * Half;
            }
            if (Phase == "FoldWidth")
            {
                var farEdge = onceCentre + forward * ClothLength * Quarter;
                contactL = farEdge - right * ClothWidth * Half;
                contactR = Arc(farEdge + right * ClothWidth * Half, farEdge, t, up, FoldArc);
            }
            if (Phase == "Return" || Phase == "Pat" || Done)
            {
                var top = Stack(Completed - 1) + Props.up * folded.y * Half;
                if (Phase == "Return")
                {
                    var stackEdge = top + Props.forward * folded.z * Half;
                    contactL = Vector3.Lerp(stackEdge - Props.right * folded.x * Half, restL, e);
                    contactR = Vector3.Lerp(stackEdge + Props.right * folded.x * Half, restR, e);
                }
                else
                {
                    contactL = restL; contactR = restR;
                    if (Phase == "Pat") contactR = Vector3.Lerp(restR, top + up * (PatHeight - (PatHeight + PatDepth) * Mathf.Sin(Mathf.PI * t)), Mathf.Sin(Mathf.PI * t));
                }
                grip = OpenCurl;
            }
            LeftContact = contactL; RightContact = contactR;
            float lean = Phase == "Reach" || Phase == "Lift" ? SideLean * (Phase == "Reach" ? e : 1 - e)
                : Phase == "Place" || Phase == "Return" ? -SideLean * (Phase == "Place" ? e : 1 - e) : 0;
            Turn(spine, right, SpineBow * w); Turn(spine, forward, lean * w);
            if (chest != spine) Turn(chest, right, (ChestBow + BreathDegrees * Mathf.Sin(time * 2 * Mathf.PI / BreathSeconds)) * w);
            Turn(neck, right, NeckBow * w); Turn(head, right, HeadBow * w);
            float heldVertical = Phase == "Lift" ? e : Phase == "Shake" ? 1 : Phase == "Lower" ? 1 - e : 0;
            var palm = Vector3.Slerp(-up, -forward, heldVertical);
            var fingersDirection = Vector3.Slerp(-forward, up, heldVertical);
            for (int s = 0; s < 2; s++)
            {
                var contact = s == 0 ? contactL : contactR;
                var target = contact - fingersDirection * WristGap - palm * WristLift;
                if (s == 0) LeftWristTarget = target; else RightWristTarget = target;
                var pole = upper[s].position + right * ((s == 0 ? -1 : 1) * ElbowSide) + forward * ElbowAhead - up * ElbowDown;
                SofaRig.TwoBone(upper[s], lower[s], hands[s], target, pole, w);
                hands[s].rotation = Quaternion.Slerp(hands[s].rotation, Quaternion.LookRotation(fingersDirection, palm) * Quaternion.Inverse(handFrame[s]), w);
                var curlAxis = Vector3.Cross(fingersDirection, palm).normalized;
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                    Turn(fingers[s, f, j], curlAxis, (j == 0 ? CurlProximal : j == 1 ? CurlMiddle : CurlDistal) * grip * w);
                for (int j = 0; j < 3; j++) Turn(thumbs[s, j], curlAxis, ThumbCurl * grip * w);
            }
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
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
