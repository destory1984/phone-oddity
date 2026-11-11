using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Pure timer: no Unity objects are touched, including when a large dt skips beats.
    public sealed class RamenClock
    {
        public const float BlendSeconds = .4f; // Seconds to acquire or release the overlay.
        public const float FillEnd = 7f, CarryEnd = 10f, OnEnd = 11f; // Water, transfer, power on.
        public const float WaitEnd = 17f, PhoneEnd = 21f; // Finger tapping, then a phone glance.
        public const float PacketEnd = 23f, TearEnd = 25f, DropEnd = 27f; // Pick up, tear, pour.
        public const float StirEnd = 36f, OffEnd = 38f, LoopSeconds = 40f; // Stir, power off, return empty pot.
        static readonly float[] Ends = { FillEnd, CarryEnd, OnEnd, WaitEnd, PhoneEnd, PacketEnd, TearEnd, DropEnd, StirEnd, OffEnd, LoopSeconds };
        static readonly string[] Names = { "Fill", "Carry", "SwitchOn", "Wait", "Phone", "Packet", "Tear", "Drop", "Stir", "SwitchOff", "Return" };
        bool wanted;
        public float Time { get; private set; }
        public float Weight { get; private set; }
        public int Loops { get; private set; }
        public bool Active => wanted || Weight > 0;
        public int Beat { get { for (int i = 0; i < Ends.Length; i++) if (Time < Ends[i]) return i; return 0; } }
        public float BeatStart => Beat == 0 ? 0 : Ends[Beat - 1];
        public float BeatDuration => Ends[Beat] - BeatStart;
        public string Phase => Active ? Names[Beat] : "Idle";
        public void Start() { if (wanted) return; if (!Active) { Time = 0; Loops = 0; } wanted = true; }
        public void Stop() { wanted = false; }
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            Weight = Mathf.MoveTowards(Weight, wanted ? 1 : 0, dt / BlendSeconds);
            if (!wanted) return; // Freeze the interrupted beat during the release.
            double total = (double)Time + dt;
            Loops += (int)Math.Floor(total / LoopSeconds);
            Time = (float)(total % LoopSeconds);
        }
    }

    // Construct in the model's neutral pose; Tick in Update, Apply after Animator in LateUpdate.
    // Sink/Burner are world-space pot-bottom centres, Knob is a world-space fingertip contact.
    public sealed class RamenRig : IDisposable
    {
        public const float PotDiameter = .18f, PotHeight = .10f; // Metres: small open saucepan.
        const float PotWall = .008f, PotBase = .008f, RimHeight = .012f; // Metres: primitive pot shell.
        const int RimSegments = 16; // Boxes approximate the open cylindrical wall.
        const float HandleLength = .14f, HandleWidth = .025f, HandleY = .065f; // West-facing grip in metres.
        const float HandleReach = .15f, CarryLift = .04f; // Wrist offset from pot centre and transfer arc.
        const float WristHover = .07f, RestSide = .15f, RestAhead = .22f; // Counter hand positions in body frame.
        const float TapHeight = .004f, TapHz = 2.3f, TapCurl = 10f; // Waiting finger motion in metres, Hz, degrees.
        const float PacketW = .12f, PacketH = .15f, PacketEdge = .004f, PacketMid = .0058f; // Bag width, height, edge thickness and pillowed middle thickness in metres.
        const float PacketCornerInset = .01f, PacketRestForward = .11f, PacketRestLeft = .17f, PacketThumbDefault = 60f; // Pinch inset from the top corner, resting spot beside the burner (metres), thumb closing angle (degrees).
        const float PacketGrab = .5f, PacketRaise = 1.2f; // Seconds spent reaching the flat bag, then lifting and turning it upright.
        const float PacketSide = .08f, PacketLift = .22f, TearSpread = .085f; // Pick-up and tearing distances.
        const float PacketTilt = 115f, PacketPalmOffset = -.06f; // Pour angle in degrees, palm-to-packet metres.
        const float StripWidth = .018f, StripLength = .05f, NoodleSize = .095f, NoodleThickness = .025f; // Torn edge and noodle block metres.
        const float ChopLength = .24f, ChopRadius = .003f, ChopGap = .014f; // Chopstick geometry metres.
        const float ChopGrip = .075f, StirRadius = .035f, StirHz = .7f; // Grip offset metres, stirring radius metres and Hz.
        const float StirPullIn = .05f; // Stir centre pulled toward her so the hand reaches the whole circle.
        const float StirHandHeight = .24f, StirTilt = 16f; // Wrist height above burner in metres, utensil tilt degrees.
        const float PourFall = .14f, WaterY = .065f, LiquidInset = .022f; // Noodle fall and water level metres.
        const float PhoneOffset = .24f, PhoneRaise = .015f, PhoneTilt = 72f; // Phone placement metres and tilt degrees.
        const float KnobWrist = .045f, KnobTurn = 35f; // Contact offset metres, wrist rotation degrees.
        const float SpineLean = 9f, ChestLean = 7f, BreathDegrees = .6f, BreathSeconds = 4.2f; // Torso angles and breath period.
        const float NeckShare = .3f, HeadShare = .7f, MaxLookDegrees = 48f; // Distributed gaze limit.
        const float ElbowSide = .28f, ElbowDown = .20f, ElbowAhead = .08f; // IK pole offsets metres.
        const float CurlProximal = 24f, CurlMiddle = 30f, CurlDistal = 16f; // Finger grip angles degrees.
        const float BlinkMin = 2.5f, BlinkMax = 5f, BlinkSeconds = .16f; // Seeded blink timing seconds.
        const float SmileIdle = .08f, SmileDone = .32f, TearMouth = .10f; // Facial blendshape weights.
        const float TransitionSeconds = .4f, Epsilon = .000001f, RotationTolerance = .001f; // Beat ease and numerical tolerances.
        const float Tau = 2f * Mathf.PI; // Radians per full cycle.
        const float SinkX = 2.70f, SinkY = .90f, SinkZ = 3.98f; // Default sink pot-bottom centre metres.
        const float BurnerX = 2.72f, BurnerY = .90f, BurnerZ = 4.26f; // Default burner pot-bottom centre metres.
        const float KnobX = 2.48f, KnobY = .92f, KnobZ = 4.30f; // Default switch fingertip contact metres.
        const float TapX = 2.55f, TapY = 1.02f, TapZ = 3.88f; // Near-edge tap lever contact metres.
        static readonly Color MetalColor = new Color(.55f, .58f, .61f); // Steel pot surface.
        static readonly Color DarkColor = new Color(.06f, .06f, .07f); // Handle and phone shell.
        static readonly Color PacketColor = new Color(.85f, .18f, .08f); // Red wrapper.
        static readonly Color WoodColor = new Color(.66f, .43f, .20f); // Wooden chopsticks.
        static readonly Color NoodleColor = new Color(.95f, .78f, .38f); // Dry noodle block.
        static readonly Color WaterColor = new Color(.55f, .68f, .72f); // Opaque stylized water and phone screen.
        // Default geometry respects the east-wall worktop; caller can move every contact.
        public Vector3 Sink = new Vector3(SinkX, SinkY, SinkZ);
        public Vector3 Burner = new Vector3(BurnerX, BurnerY, BurnerZ);
        public Vector3 Knob = new Vector3(KnobX, KnobY, KnobZ);
        public Vector3 TapControl = new Vector3(TapX, TapY, TapZ); // Requires a near-edge tap lever.
        public readonly Transform Props, Pot, Chopsticks, Packet, Phone;
        readonly Transform strip, noodles, water;
        public static float PacketThumb = PacketThumbDefault; // thumb closing angle while pinching, degrees
        readonly Transform[,] thumb = new Transform[2, 3];
        readonly Vector3[] pinchInHand = new Vector3[2]; // pinch point (thumb tip and index tip midpoint) relative to the wrist in the hand's own rotation frame
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Animator anim;
        readonly Transform spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 4, 3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly Vector3[] fingerAxis = new Vector3[2], palmAxis = new Vector3[2];
        readonly Quaternion headFrame;
        readonly List<Transform> controlled = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        readonly RamenClock clock = new RamenClock();
        readonly System.Random rng;
        readonly Quaternion[] before, written;
        readonly Quaternion chopLocalRotation;
        bool disposed, hasWritten;
        float elapsed, blinkAt, blinkStart = -BlinkSeconds;
        public bool Active => !disposed && clock.Active;
        public float Weight => disposed ? 0 : clock.Weight;
        public string Phase => disposed ? "Idle" : clock.Phase;
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen { get; private set; }
        public Vector3 LeftTarget { get; private set; }
        public Vector3 RightTarget { get; private set; }
        public Vector3 PotHandlePoint => Pot.TransformPoint(new Vector3(0, HandleY, -HandleReach)); // The spot of the handle her left wrist holds.
        public bool CarryingPot => Active && (clock.Beat == 1 || clock.Beat == 10);
        public Vector3 PacketHoldPoint => Packet.position; // Centre of the packet; valid after Apply.
        public bool InductionOn => Active && clock.Time >= RamenClock.OnEnd && clock.Time < RamenClock.OffEnd - TransitionSeconds;
        public bool TapOn => Active && clock.Time >= TransitionSeconds && clock.Time < RamenClock.FillEnd - TransitionSeconds;

        public RamenRig(Animator anim, int seed, Quaternion? restHeadFrame = null)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("RamenRig needs a humanoid Animator.", nameof(anim));
            this.anim = anim; rng = new System.Random(seed);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (spine == null || head == null) throw new InvalidOperationException("RamenRig needs spine and head.");
            headFrame = restHeadFrame ?? Quaternion.Inverse(head.rotation) * anim.transform.rotation;
            Track(spine); Track(chest); Track(neck); Track(head);
            for (int s = 0; s < 2; s++)
            {
                upper[s] = Bone(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = Bone(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = Bone(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[s] == null || lower[s] == null || hand[s] == null) throw new InvalidOperationException("RamenRig needs both arms.");
                Track(upper[s]); Track(lower[s]); Track(hand[s]);
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                {
                    finger[s, f, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j));
                    Track(finger[s, f, j]);
                }
                for (int j = 0; j < 3; j++) { thumb[s, j] = Bone((HumanBodyBones)((int)(s == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + j)); Track(thumb[s, j]); }
                Vector3 direction = finger[s, 1, 0] != null ? finger[s, 1, 0].position - hand[s].position : hand[s].position - lower[s].position;
                Vector3 palm = Vector3.ProjectOnPlane(-anim.transform.up, direction).normalized;
                if (palm.sqrMagnitude < Epsilon) palm = Vector3.ProjectOnPlane(anim.transform.forward, direction).normalized;
                fingerAxis[s] = Quaternion.Inverse(hand[s].rotation) * direction.normalized;
                palmAxis[s] = Quaternion.Inverse(hand[s].rotation) * palm;
                handFrame[s] = Quaternion.LookRotation(fingerAxis[s], palmAxis[s]);
            }
            for (int s = 0; s < 2; s++) pinchInHand[s] = Quaternion.Inverse(hand[s].rotation) * (PinchPoint(s) - hand[s].position);
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
            Props = new GameObject("RamenProps").transform;
            var metal = Mat(MetalColor); var dark = Mat(DarkColor);
            var red = Mat(PacketColor); var wood = Mat(WoodColor);
            var yellow = Mat(NoodleColor); var liquid = Mat(WaterColor);
            Pot = Root("Pot");
            Part(Pot, "base", PrimitiveType.Cylinder, Vector3.up * (PotBase / 2), new Vector3(PotDiameter, PotBase / 2, PotDiameter), metal);
            for (int i = 0; i < RimSegments; i++)
            {
                float a = i * Tau / RimSegments;
                var p = Part(Pot, "wall", PrimitiveType.Cube, new Vector3(Mathf.Sin(a) * (PotDiameter - PotWall) / 2, PotHeight / 2, Mathf.Cos(a) * (PotDiameter - PotWall) / 2),
                    new Vector3(PotDiameter * Mathf.PI / RimSegments + PotWall, PotHeight, PotWall), metal);
                p.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
            }
            Part(Pot, "handle", PrimitiveType.Cube, new Vector3(0, HandleY, -PotDiameter / 2 - HandleLength / 2), new Vector3(HandleWidth, HandleWidth, HandleLength), dark);
            water = Part(Pot, "water", PrimitiveType.Cylinder, Vector3.up * WaterY, new Vector3(PotDiameter - LiquidInset, RimHeight / 2, PotDiameter - LiquidInset), liquid);
            Packet = Root("Packet"); MeshPart(Packet, "wrapper", BagMesh(), red);
            strip = Root("TornEdge"); Part(strip, "edge", PrimitiveType.Cube, Vector3.zero, new Vector3(StripLength, StripWidth, PacketEdge), red);
            noodles = Root("Noodles"); Part(noodles, "block", PrimitiveType.Cube, Vector3.zero, new Vector3(NoodleSize, NoodleThickness, NoodleSize), yellow);
            Chopsticks = Root("Chopsticks");
            for (int i = 0; i < 2; i++) Part(Chopsticks, "stick", PrimitiveType.Cylinder, new Vector3((i - .5f) * ChopGap, -ChopGrip, 0), new Vector3(ChopRadius * 2, ChopLength / 2, ChopRadius * 2), wood);
            Phone = Root("Phone");
            Part(Phone, "body", PrimitiveType.Cube, Vector3.zero, new Vector3(SofaGeometry.PhoneW, SofaGeometry.PhoneH, SofaGeometry.PhoneT), dark);
            Part(Phone, "screen", PrimitiveType.Cube, -Vector3.forward * SofaGeometry.PhoneT / 2, new Vector3(SofaGeometry.PhoneW - PotWall, SofaGeometry.PhoneH - PotWall, PotBase / 2), liquid);
            // Chopsticks' canonical +y goes up, tips go down. Mount using calibrated hand axes.
            var canonicalHand = Quaternion.LookRotation(Vector3.forward, Vector3.down) * Quaternion.Inverse(handFrame[1]);
            chopLocalRotation = Quaternion.Inverse(canonicalHand);
            Chopsticks.SetParent(hand[1], false); Chopsticks.localPosition = Vector3.zero; Chopsticks.localRotation = chopLocalRotation;
            Props.gameObject.SetActive(false); Chopsticks.gameObject.SetActive(false);
        }

        // Pinch point: midpoint of the index fingertip and the thumb tip, world.
        Vector3 PinchPoint(int s)
        {
            var d = finger[s, 0, 2]; var m = finger[s, 0, 1]; var td = thumb[s, 2];
            Vector3 index = d != null && m != null ? d.position + (d.position - m.position).normalized * .01f : hand[s].position;
            Vector3 thumbTip = td != null && thumb[s, 1] != null ? td.position + (td.position - thumb[s, 1].position).normalized * .01f : index;
            return (index + thumbTip) / 2;
        }
        // A thin ramen bag: 4 mm at the sealed edges, pillowed to 5.8 mm in the middle; local x = width, y = height (open top at +y), z = thickness.
        Mesh BagMesh()
        {
            const int nx = 8, ny = 10;
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int face = 0; face < 2; face++)
            {
                float sign = face == 0 ? 1 : -1; int baseIndex = v.Count;
                for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                {
                    float u = (float)i / nx * 2 - 1, w = (float)j / ny * 2 - 1;
                    float bump = (1 - u * u) * (1 - w * w);
                    v.Add(new Vector3(u * PacketW / 2, w * PacketH / 2, sign * (PacketEdge + (PacketMid - PacketEdge) * bump) / 2));
                }
                for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                {
                    int a = baseIndex + j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    if (face == 0) { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d); }
                    else { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c); }
                }
            }
            // thin side walls along the four edges
            Action<Vector3, Vector3> wall = (p, q) =>
            {
                int k = v.Count; var o = Vector3.forward * PacketEdge / 2;
                v.Add(p + o); v.Add(q + o); v.Add(q - o); v.Add(p - o);
                tri.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 }); tri.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            };
            float hx = PacketW / 2, hy = PacketH / 2;
            wall(new Vector3(-hx, -hy, 0), new Vector3(hx, -hy, 0)); wall(new Vector3(hx, -hy, 0), new Vector3(hx, hy, 0));
            wall(new Vector3(hx, hy, 0), new Vector3(-hx, hy, 0)); wall(new Vector3(-hx, hy, 0), new Vector3(-hx, -hy, 0));
            var mesh = new Mesh { name = "RamenBag" }; mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh); return mesh;
        }
        static void MeshPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        public float PacketThickness => Packet.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.z;
        public bool PacketHeld => Packet.parent != Props;
        public Vector3 LeftPinch => PinchPoint(0);
        Transform Bone(HumanBodyBones id) => anim.GetBoneTransform(id);
        void Track(Transform t) { if (t != null && !controlled.Contains(t)) controlled.Add(t); }
        Transform Root(string name) { var t = new GameObject(name).transform; t.SetParent(Props, false); return t; }
        Material Mat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader); m.color = color; if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0); materials.Add(m); return m;
        }
        static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 p, Vector3 size, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false; DestroyOwned(collider);
            g.transform.SetParent(parent, false); g.transform.localPosition = p; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = material; return g.transform;
        }
        static void DestroyOwned(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
        public void Start()
        {
            if (disposed || clock.Active) return;
            elapsed = 0; blinkStart = -BlinkSeconds; blinkAt = RandomBlink(); clock.Start();
        }
        public void Stop() { if (!disposed) clock.Stop(); }
        float RandomBlink() => BlinkMin + (float)rng.NextDouble() * (BlinkMax - BlinkMin);
        public void Tick(float dt)
        {
            if (disposed || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            clock.Tick(dt);
            if (!Active) { Smile = EyesClosed = MouthOpen = 0; return; }
            elapsed += dt;
            while (elapsed >= blinkAt) { blinkStart = blinkAt; blinkAt += RandomBlink(); }
            float blink = (elapsed - blinkStart) / BlinkSeconds;
            EyesClosed = (blink >= 0 && blink <= 1 ? Mathf.Sin(blink * Mathf.PI) : 0) * Weight;
            Smile = (clock.Beat >= 9 ? SmileDone : SmileIdle) * Weight;
            MouthOpen = (clock.Beat == 6 ? TearMouth * Mathf.Sin(Mathf.PI * (clock.Time - clock.BeatStart) / clock.BeatDuration) : 0) * Weight;
        }

        static float Ease(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        Vector3 Forward => Vector3.ProjectOnPlane(anim.transform.forward, Vector3.up).normalized;
        Vector3 Right => Vector3.Cross(Vector3.up, Forward).normalized;
        Vector3 Handle(Vector3 pot) => pot - Forward * HandleReach + Vector3.up * HandleY;
        Vector3 PacketRest => new Vector3(Burner.x, Burner.y + PacketMid / 2, Burner.z) - Forward * PacketRestForward - Right * PacketRestLeft; // bag centre, lying flat beside the burner
        Quaternion PacketFlat => Quaternion.LookRotation(Vector3.down, Forward); // x = right, y = away from her, z = down: the bag lies on the counter
        Vector3 CornerLeft => new Vector3(-PacketW / 2 + PacketCornerInset, PacketH / 2 - .003f, 0);
        Vector3 CornerRight => new Vector3(PacketW / 2 - PacketCornerInset, PacketH / 2 - .003f, 0);
        Vector3 PacketLifted => Burner + Vector3.up * PacketLift - Right * PacketSide; // left pinch while the bag hangs over the pot
        Quaternion IdealHand(int s) => Quaternion.LookRotation(Forward, Vector3.down) * Quaternion.Inverse(handFrame[s]);
        Vector3 WristFor(int s, Vector3 pinch) => pinch - IdealHand(s) * pinchInHand[s];
        Vector3 PhoneRest => Sink - Right * PhoneOffset - Forward * HandleReach + Vector3.up * PhoneRaise;
        Vector3 Rest(int side) => anim.transform.position + Forward * RestAhead + Right * (side == 0 ? -RestSide : RestSide) + Vector3.up * (Burner.y + WristHover - anim.transform.position.y);
        Vector3 PotAt(float t)
        {
            if (t < RamenClock.FillEnd) return Sink;
            if (t < RamenClock.CarryEnd)
            {
                float u = Ease((t - RamenClock.FillEnd) / (RamenClock.CarryEnd - RamenClock.FillEnd));
                return Vector3.Lerp(Sink, Burner, u) + Vector3.up * (CarryLift * Mathf.Sin(Mathf.PI * u));
            }
            if (t < RamenClock.OffEnd) return Burner;
            float v = Ease((t - RamenClock.OffEnd) / (RamenClock.LoopSeconds - RamenClock.OffEnd));
            return Vector3.Lerp(Burner, Sink, v) + Vector3.up * (CarryLift * Mathf.Sin(Mathf.PI * v));
        }
        void Targets(int beat, float t, out Vector3 l, out Vector3 r)
        {
            l = Rest(0); r = Rest(1);
            if (beat == 0) { l = Handle(Sink); r = TapControl - Forward * KnobWrist; }
            else if (beat == 1 || beat == 10) l = Handle(PotAt(t));
            else if (beat == 2 || beat == 9) { l = Handle(Burner); r = Knob - Forward * KnobWrist; }
            else if (beat == 3) l += Vector3.up * (TapHeight * Mathf.Max(0, Mathf.Sin(t * Tau * TapHz)));
            else if (beat == 5)
            {
                float u = Ease((t - RamenClock.PhoneEnd - PacketGrab) / PacketRaise);
                var restL = PacketRest + PacketFlat * CornerLeft; var restR = PacketRest + PacketFlat * CornerRight;
                var liftedR = PacketLifted + Right * (PacketW - 2 * PacketCornerInset);
                l = WristFor(0, Vector3.Lerp(restL, PacketLifted, u)); r = WristFor(1, Vector3.Lerp(restR, liftedR, u));
            }
            else if (beat == 6)
            {
                float tear = Ease((t - RamenClock.PacketEnd) / (RamenClock.TearEnd - RamenClock.PacketEnd));
                l = WristFor(0, PacketLifted - Right * (TearSpread * tear)); r = WristFor(1, PacketLifted + Right * (PacketW - 2 * PacketCornerInset));
            }
            else if (beat == 7) { l = WristFor(0, PacketLifted); r = Rest(1); }
            else if (beat == 8) { l = Handle(Burner); r = Burner - Forward * StirPullIn + Vector3.up * StirHandHeight + (Right * Mathf.Cos(t * Tau * StirHz) + Forward * Mathf.Sin(t * Tau * StirHz)) * StirRadius; }
        }
        void RestoreLast()
        {
            if (!hasWritten) return;
            for (int i = 0; i < controlled.Count; i++)
                if (controlled[i] != null && Quaternion.Angle(controlled[i].localRotation, written[i]) < RotationTolerance) controlled[i].localRotation = before[i];
            hasWritten = false;
        }
        void Turn(Transform b, Vector3 axis, float degrees, float weight)
        { if (b != null) b.rotation = Quaternion.AngleAxis(degrees * weight, axis) * b.rotation; }
        void Curl(int side, float grip, float w)
        {
            Vector3 axis = Vector3.Cross(hand[side].rotation * fingerAxis[side], hand[side].rotation * palmAxis[side]).normalized;
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
            {
                var b = finger[side, f, j]; if (b == null) continue;
                float angle = j == 0 ? CurlProximal : j == 1 ? CurlMiddle : CurlDistal;
                if (clock.Beat == 3 && side == 0) angle += TapCurl * Mathf.Sin(clock.Time * Tau * TapHz + f);
                Turn(b, axis, angle * grip, w);
            }
            float pinch = clock.Beat == 5 || clock.Beat == 6 || (clock.Beat == 7 && side == 0) ? Ease((clock.Time - RamenClock.PhoneEnd) / TransitionSeconds) : 0;
            if (pinch <= 0 || finger[side, 0, 2] == null || thumb[side, 2] == null) return;
            // close the thumb onto the index fingertip: each joint swings the thumb tip toward it (limited per joint), two passes from the tip joint to the base
            for (int pass = 0; pass < 4; pass++) for (int j = 2; j >= 0; j--)
            {
                var b = thumb[side, j]; if (b == null) continue;
                var index = finger[side, 0, 2].position + (finger[side, 0, 2].position - finger[side, 0, 1].position).normalized * .01f;
                var tip = thumb[side, 2].position + (thumb[side, 2].position - thumb[side, 1].position).normalized * .01f;
                var q = Quaternion.FromToRotation(tip - b.position, index - b.position);
                q.ToAngleAxis(out float angle, out Vector3 swing); if (angle > 180) angle -= 360;
                b.rotation = Quaternion.AngleAxis(Mathf.Clamp(angle, -PacketThumb, PacketThumb) * pinch * w, swing) * b.rotation;
            }
        }
        public void Apply(float dt)
        {
            if (disposed) return;
            RestoreLast();
            Props.gameObject.SetActive(Active); Chopsticks.gameObject.SetActive(Active && clock.Beat == 8);
            if (!Active || Weight <= 0) { Packet.gameObject.SetActive(false); return; }
            for (int i = 0; i < controlled.Count; i++) before[i] = controlled[i].localRotation;
            float w = Ease(Weight), t = clock.Time;
            Turn(spine, Right, SpineLean, w);
            if (chest != spine) Turn(chest, Right, ChestLean + BreathDegrees * Mathf.Sin(elapsed * Tau / BreathSeconds), w);
            Vector3 look = clock.Beat == 4 ? PhoneRest : clock.Beat == 0 ? Sink : Burner;
            Vector3 current = head.rotation * headFrame * Vector3.forward;
            Quaternion gaze = Quaternion.FromToRotation(current, (look - head.position).normalized);
            gaze.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180) degrees -= 360;
            degrees = Mathf.Clamp(degrees, -MaxLookDegrees, MaxLookDegrees);
            Turn(neck, axis, degrees * NeckShare, w); Turn(head, axis, degrees * (neck == null ? 1 : HeadShare), w);
            Targets(clock.Beat, t, out var left, out var right);
            int previous = clock.Beat == 0 ? 10 : clock.Beat - 1;
            float previousEnd = clock.Beat == 0 ? RamenClock.LoopSeconds : clock.BeatStart;
            Targets(previous, previousEnd, out var oldLeft, out var oldRight);
            float transition = Ease((t - clock.BeatStart) / TransitionSeconds);
            LeftTarget = Vector3.Lerp(oldLeft, left, transition); RightTarget = Vector3.Lerp(oldRight, right, transition);
            for (int s = 0; s < 2; s++)
            {
                Vector3 target = s == 0 ? LeftTarget : RightTarget;
                var pole = upper[s].position + Right * (s == 0 ? -ElbowSide : ElbowSide) - Vector3.up * ElbowDown + Forward * ElbowAhead;
                SofaRig.TwoBone(upper[s], lower[s], hand[s], target, pole, w);
                Quaternion rotation = Quaternion.LookRotation(Forward, Vector3.down) * Quaternion.Inverse(handFrame[s]);
                if (s == 1 && clock.Beat == 8)
                {
                    // the stirring wrist tilt eases in and out over TransitionSeconds instead of switching at the beat edges
                    float stirEnd = clock.BeatStart + clock.BeatDuration;
                    float stirW = Ease((t - clock.BeatStart) / TransitionSeconds) * Ease((stirEnd - t) / TransitionSeconds);
                    rotation = Quaternion.AngleAxis(StirTilt * stirW, Right) * rotation;
                }
                if (s == 1 && (clock.Beat == 2 || clock.Beat == 9)) rotation = Quaternion.AngleAxis(KnobTurn * Mathf.Sin(Mathf.PI * (t - clock.BeatStart) / clock.BeatDuration), Forward) * rotation;
                hand[s].rotation = Quaternion.Slerp(hand[s].rotation, rotation, w);
                Curl(s, clock.Beat == 3 || clock.Beat == 4 ? SmileDone : 1, w);
            }
            ApplyProps(t);
            if (w >= 1 && clock.Beat >= 5 && clock.Beat <= 6) for (int s = 0; s < 2; s++) pinchInHand[s] = Quaternion.Inverse(hand[s].rotation) * (PinchPoint(s) - hand[s].position);
            for (int i = 0; i < controlled.Count; i++) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }
        void ApplyProps(float t)
        {
            Pot.SetPositionAndRotation(PotAt(t), Quaternion.LookRotation(Forward, Vector3.up));
            if (CarryingPot)
            {
                // the pot hangs on her real left hand: where the arm cannot reach the ideal path the pot follows the wrist, and the grip stays on the handle
                float edge = Mathf.Min(t - clock.BeatStart, clock.BeatStart + clock.BeatDuration - t);
                float k = Ease(edge / TransitionSeconds) * Ease(Weight);
                Pot.position = Vector3.Lerp(Pot.position, hand[0].position + Forward * HandleReach - Vector3.up * HandleY, k);
            }
            water.gameObject.SetActive(t >= TransitionSeconds && t < RamenClock.OffEnd);
            // Fill rises from the bottom; the return is an explicit empty-pot loop reset.
            water.localPosition = Vector3.up * Mathf.Lerp(PotBase, WaterY, Ease(t / RamenClock.FillEnd));
            Phone.SetPositionAndRotation(PhoneRest, Quaternion.LookRotation(Forward, Vector3.up) * Quaternion.Euler(PhoneTilt, 0, 0));
            bool holdingPacket = clock.Beat == 6 || clock.Beat == 7 || (clock.Beat == 5 && t >= RamenClock.PhoneEnd + PacketGrab - .2f);
            Packet.SetParent(holdingPacket ? hand[0] : Props, true);
            var flatRotation = PacketFlat;
            if (holdingPacket)
            {
                // the bag is pinched at its left top corner between the thumb and index fingertips and fixed in the hand's own frame, so it turns with the wrist
                float u = clock.Beat == 5 ? Ease((t - RamenClock.PhoneEnd - PacketGrab) / PacketRaise) : 1;
                var hanging = hand[0].rotation * handFrame[0] * Quaternion.Euler(0, 0, 180);
                var rotation = Quaternion.Slerp(flatRotation, hanging, u) * Quaternion.Euler(clock.Beat == 7 ? PacketTilt * Ease((t - RamenClock.TearEnd) / (RamenClock.DropEnd - RamenClock.TearEnd)) : 0, 0, 0);
                float attach = clock.Beat == 5 ? Ease((t - (RamenClock.PhoneEnd + PacketGrab - .2f)) / .2f) : 1;
                Packet.rotation = rotation;
                Packet.position = Vector3.Lerp(PacketRest, PinchPoint(0) - rotation * CornerLeft, attach);
            }
            else Packet.SetPositionAndRotation(PacketRest, flatRotation);
            Packet.gameObject.SetActive(clock.Beat <= 7);
            strip.gameObject.SetActive(clock.Beat == 6);
            strip.position = PinchPoint(1) - Right * (StripLength / 2); strip.rotation = Quaternion.LookRotation(Forward, Vector3.up);
            noodles.gameObject.SetActive(clock.Beat == 7 || clock.Beat == 8 || clock.Beat == 9);
            float fall = Ease((t - RamenClock.TearEnd) / (RamenClock.DropEnd - RamenClock.TearEnd));
            noodles.position = Burner + Vector3.up * Mathf.Lerp(PotHeight + PourFall, WaterY, fall);
            Chopsticks.localPosition = Vector3.zero; Chopsticks.localRotation = chopLocalRotation;
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
            try { RestoreLast(); } catch (System.Exception) { } disposed = true; Smile = EyesClosed = MouthOpen = 0;
            DestroyOwned(Chopsticks != null ? Chopsticks.gameObject : null);
            // Packet may currently be parented to a hand rather than Props.
            DestroyOwned(Packet != null ? Packet.gameObject : null);
            DestroyOwned(Props != null ? Props.gameObject : null);
            foreach (var m in materials) DestroyOwned(m);
            foreach (var mesh in meshes) DestroyOwned(mesh);
        }
    }
}
