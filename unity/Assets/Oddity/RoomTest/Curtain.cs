using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Room-owned, reusable prop. Coordinates are OneRoom metres; keep Root upright at unit scale.
    // Setting Openness only changes data. Apply updates primitives, separately from the rig's Tick.
    public sealed class CurtainPanel : IDisposable
    {
        public const float West = .3f, East = 2.7f, Top = 2.25f, Bottom = .35f; // Rod span and fabric height, metres.
        public const float PlaneZ = .20f, OpenGap = 1.8f, ReachGap = .8f; // Indoor fabric plane, fully open central gap (each panel bunched .3 m at its end) and the gap her hands can reach, metres.
        const float FoldDepth = .025f, Thickness = .006f, SeamOverlap = .004f; // Fold relief, fabric thickness and strip overlap, metres.
        const float RodRadius = .012f; // Cylinder radius, metres.
        const float CreamR = .96f, CreamG = .92f, CreamB = .81f, Alpha = .58f; // Cream sheer tint and opacity.
        const float RodShade = .65f, RoughSmoothness = .08f; // Rod grey and cloth smoothness.
        const int Strips = 16, Folds = 4; // Primitive strips and vertical folds per panel.
        const float Half = .5f, Tau = 2 * Mathf.PI; // Geometric midpoint and radians per cycle.
        readonly Transform[,] strips = new Transform[2, Strips];
        readonly Material fabric, metal;
        bool disposed;
        float openness = 1;
        public Transform Root { get; }
        public float Openness { get => openness; set => openness = Mathf.Clamp01(value); }

        public CurtainPanel()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("CurtainPanel requires the URP/Lit shader.");
            fabric = new Material(shader) { name = "Curtain cream sheer" };
            fabric.SetColor("_BaseColor", new Color(CreamR, CreamG, CreamB, Alpha));
            fabric.SetFloat("_Surface", 1); fabric.SetFloat("_Blend", 0);
            fabric.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fabric.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fabric.SetFloat("_ZWrite", 0); fabric.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            fabric.SetFloat("_Smoothness", RoughSmoothness);
            fabric.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fabric.SetOverrideTag("RenderType", "Transparent");
            fabric.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            metal = new Material(shader) { name = "Curtain rod" };
            metal.SetColor("_BaseColor", new Color(RodShade, RodShade, RodShade));
            Root = new GameObject("CurtainPanel").transform;
            var rod = Part("rod", PrimitiveType.Cylinder, metal);
            rod.localPosition = new Vector3((West + East) * Half, Top, PlaneZ);
            rod.localRotation = Quaternion.Euler(0, 0, 90);
            rod.localScale = new Vector3(RodRadius * 2, (East - West) * Half, RodRadius * 2);
            for (int side = 0; side < 2; side++) for (int i = 0; i < Strips; i++)
                strips[side, i] = Part((side == 0 ? "west_" : "east_") + i, PrimitiveType.Cube, fabric);
            Apply();
        }

        Transform Part(string name, PrimitiveType type, Material material)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            var collider = g.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
            g.transform.SetParent(Root, false);
            var renderer = g.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        float Inner(int side) => (West + East) * Half + (side == 0 ? -1 : 1) * OpenGap * Half * openness;

        // The exact fabric edge (fold endpoints have zero relief), at a world-space grip height.
        public Vector3 Edge(int panel, float worldHeight)
        {
            if (panel < 0 || panel > 1) throw new ArgumentOutOfRangeException(nameof(panel));
            var p = Root.TransformPoint(new Vector3(Inner(panel), 0, PlaneZ));
            p.y = worldHeight; return p;
        }

        public void Apply()
        {
            if (disposed) return;
            for (int side = 0; side < 2; side++)
            {
                float a = side == 0 ? West : Inner(side), b = side == 0 ? Inner(side) : East;
                float width = (b - a) / Strips;
                for (int i = 0; i < Strips; i++)
                {
                    float t0 = (float)i / Strips, t1 = (float)(i + 1) / Strips;
                    float z0 = FoldDepth * Mathf.Sin(t0 * Tau * Folds), z1 = FoldDepth * Mathf.Sin(t1 * Tau * Folds);
                    var strip = strips[side, i];
                    strip.localPosition = new Vector3(a + (i + Half) * width, (Top + Bottom) * Half, PlaneZ + (z0 + z1) * Half);
                    strip.localRotation = Quaternion.Euler(0, -Mathf.Atan2(z1 - z0, width) * Mathf.Rad2Deg, 0);
                    strip.localScale = new Vector3(Mathf.Sqrt(width * width + (z1 - z0) * (z1 - z0)) + SeamOverlap, Top - Bottom, Thickness);
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SofaRig.Kill(Root != null ? Root.gameObject : null);
            Release(fabric); Release(metal);
        }

        static void Release(Material material)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }

    // Construct in the imported neutral pose before playing the base clip, as with SofaRig.
    // The caller owns locomotion, lower-body animation and the room-owned CurtainPanel lifetime.
    public sealed class CurtainRig : IDisposable
    {
        public const float BlendSeconds = .4f; // Linear Start/Stop fade, smoothed for posing.
        public const float LookMin = 1.5f, LookMax = 2.5f; // Seeded street-check duration, seconds.
        public const float ReachSeconds = .65f, PullSeconds = 1f, HoldSeconds = .3f; // Reach, decisive sweep, held grip.
        public const float OpenSlideSeconds = .6f; // Extra ease-out slide of the opened cloth after the pull, seconds.
        public const float ReleaseSeconds = .4f, TurnSeconds = .65f; // Let go before turning away, seconds.
        const float ScanYaw = 28f, WaryPitch = 7f, LeanDegrees = 5f; // Wary head sweep, chin-down and forward torso lean, degrees.
        const float TurnSpine = 20f, TurnChest = 12f, TurnHead = 48f; // Upper-body turn toward the east, degrees; root stays caller-owned.
        const float GripRise = .18f, HandStagger = .075f, PalmLength = .04f; // Above shoulders, vertical hand separation and wrist-to-contact, metres.
        const float PoleOut = .2f, PoleDown = .3f, PoleForward = .18f; // Elbow pole offsets, metres.
        const float ProximalCurl = 24f, IntermediateCurl = 32f, DistalCurl = 16f, ThumbCurl = 16f; // Cloth pinch, degrees.
        const float MorningSmile = .18f, NightSmile = .04f, BlinkSeconds = .14f; // Facial amounts and release blink duration.
        const float RotationEpsilon = .001f; // Degrees used to recognize an unchanged previous overlay.
        const float Half = .5f, Tau = 2 * Mathf.PI; // Midpoint and radians per cycle.
        readonly Animator anim;
        readonly System.Random rng;
        readonly Transform spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[,,] finger = new Transform[2, 4, 3];
        readonly Transform[,] thumb = new Transform[2, 3];
        readonly Quaternion[] handFrame = new Quaternion[2];
        readonly Vector3[] fingerAxis = new Vector3[2], palmAxis = new Vector3[2];
        readonly float[] gripHeight = new float[2];
        readonly List<Transform> controlled = new List<Transform>();
        Quaternion[] before, written;
        bool wanted, disposed, hasWritten, opening;
        float time, initialOpenness;
        CurtainPanel panel;

        public CurtainPanel Curtain; // Caller creates and places this once; assign before Start, never disposed by the rig.
        public bool Open; // False = night close, true = morning open; latched by Start.
        public bool Active => !disposed && (wanted || Weight > 0);
        public float Weight { get; private set; }
        public float Smile { get; private set; }
        public float EyesClosed { get; private set; }
        public float MouthOpen => 0;
        public string Phase { get; private set; } = "Idle";
        public float LookSeconds { get; private set; }
        float ReachEnd => LookSeconds + ReachSeconds;
        float PullEnd => ReachEnd + PullSeconds;
        float HoldEnd => PullEnd + HoldSeconds;
        float ReleaseEnd => HoldEnd + ReleaseSeconds;
        float TurnEnd => ReleaseEnd + TurnSeconds;
        float End => TurnEnd + BlendSeconds;
        static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        float Arms => time < LookSeconds ? 0 : time < ReachEnd ? Smooth((time - LookSeconds) / ReachSeconds)
            : 1 - Smooth((time - HoldEnd) / ReleaseSeconds);

        public CurtainRig(Animator anim, int seed)
        {
            if (anim == null) throw new ArgumentNullException(nameof(anim));
            if (!anim.isHuman) throw new ArgumentException("CurtainRig requires a humanoid Animator.", nameof(anim));
            this.anim = anim; rng = new System.Random(seed);
            spine = Bone(HumanBodyBones.Spine); chest = Bone(HumanBodyBones.Chest) ?? spine;
            neck = Bone(HumanBodyBones.Neck); head = Bone(HumanBodyBones.Head);
            if (spine == null || head == null) throw new InvalidOperationException("CurtainRig requires spine and head.");
            Track(spine); Track(chest); Track(neck); Track(head);
            for (int side = 0; side < 2; side++)
            {
                upper[side] = Bone(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[side] = Bone(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[side] = Bone(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper[side] == null || lower[side] == null || hand[side] == null)
                    throw new InvalidOperationException("CurtainRig requires both complete arms.");
                Track(upper[side]); Track(lower[side]); Track(hand[side]);
                fingerAxis[side] = Quaternion.Inverse(hand[side].rotation) * (hand[side].position - lower[side].position).normalized;
                palmAxis[side] = Quaternion.Inverse(hand[side].rotation) * -anim.transform.up;
                handFrame[side] = Quaternion.LookRotation(fingerAxis[side], palmAxis[side]);
                for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                {
                    var bone = (HumanBodyBones)((int)(side == 0 ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) + f * 3 + j);
                    finger[side, f, j] = Bone(bone); Track(finger[side, f, j]);
                }
                for (int j = 0; j < 3; j++)
                {
                    thumb[side, j] = Bone((HumanBodyBones)((int)(side == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + j));
                    Track(thumb[side, j]);
                }
            }
            before = new Quaternion[controlled.Count]; written = new Quaternion[controlled.Count];
        }

        Transform Bone(HumanBodyBones bone) => anim.GetBoneTransform(bone);
        void Track(Transform bone) { if (bone != null && !controlled.Contains(bone)) controlled.Add(bone); }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CurtainRig));
            if (Active) return;
            if (Curtain == null || Curtain.Root == null) throw new InvalidOperationException("Assign a live room-owned CurtainPanel before Start.");
            panel = Curtain; initialOpenness = panel.Openness; opening = Open;
            LookSeconds = LookMin + (LookMax - LookMin) * (float)rng.NextDouble();
            for (int side = 0; side < 2; side++) gripHeight[side] = upper[side].position.y + GripRise + (side == 0 ? HandStagger : 0);
            time = 0; wanted = true; Weight = Smile = EyesClosed = 0; Phase = "LookLeft";
        }

        public void Stop() { wanted = false; if (Weight > 0) Phase = "FadeOut"; else Phase = "Idle"; }

        // No Transform writes, even when called without Apply for minutes at a time.
        public void Tick(float dt)
        {
            if (disposed || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            if (!wanted)
            {
                Weight = Mathf.MoveTowards(Weight, 0, dt / BlendSeconds);
                if (Weight <= 0) Phase = "Idle";
                Smile = EyesClosed = 0; return;
            }
            time += dt;
            Weight = Mathf.Clamp01(Mathf.Min(time / BlendSeconds, (End - time) / BlendSeconds));
            float slide = Mathf.Clamp01((time - ReachEnd) / (opening ? PullSeconds + OpenSlideSeconds : PullSeconds));
            panel.Openness = Mathf.Lerp(initialOpenness, opening ? 1 : 0, opening ? 1 - (1 - slide) * (1 - slide) : Smooth(slide)); // Opening eases out and keeps sliding after the hands let go.
            Phase = time < LookSeconds * Half ? "LookLeft" : time < LookSeconds ? "LookRight"
                : time < ReachEnd ? "Reach" : time < PullEnd ? "Pull" : time < HoldEnd ? "Hold"
                : time < ReleaseEnd ? "Release" : time < TurnEnd ? "TurnAway" : time < End ? "FadeOut" : "Idle";
            if (time >= End) { wanted = false; Weight = 0; }
            Smile = (opening ? MorningSmile : NightSmile) * Smooth(Weight) * Smooth((time - PullEnd) / HoldSeconds);
            float blink = (time - HoldEnd) / BlinkSeconds;
            EyesClosed = blink > 0 && blink < 1 ? Mathf.Sin(blink * Mathf.PI) * Smooth(Weight) : 0;
        }

        public Vector3 GripPoint(int side)
        {
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            // Facing south: anatomical left holds east, right holds west.
            var p = panel.Edge(1 - side, gripHeight[side]);
            float centre = panel.Root.TransformPoint(new Vector3((CurtainPanel.West + CurtainPanel.East) * Half, 0, 0)).x;
            p.x = centre + Mathf.Clamp(p.x - centre, -CurtainPanel.ReachGap * Half, CurtainPanel.ReachGap * Half); // Beyond reach the hand waits at the limit and the cloth slides past it.
            return p;
        }

        public Vector3 HandContact(int side)
        {
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            return hand[side].position + hand[side].rotation * fingerAxis[side] * PalmLength;
        }

        // With a paused Animator, remove the last overlay before applying the next one.
        // A changed local rotation is a newly evaluated base pose and is preserved.
        void ReadPose()
        {
            for (int i = 0; i < controlled.Count; i++)
            {
                var bone = controlled[i]; if (bone == null) continue;
                if (hasWritten && Quaternion.Angle(bone.localRotation, written[i]) < RotationEpsilon) bone.localRotation = before[i];
                before[i] = bone.localRotation;
            }
        }

        public void Apply(float dt)
        {
            if (disposed) return;
            ReadPose();
            if (panel != null) panel.Apply();
            float w = Smooth(Weight);
            if (w > 0)
            {
                var up = Vector3.up; var forward = Vector3.ProjectOnPlane(anim.transform.forward, up).normalized;
                var right = Vector3.Cross(up, forward).normalized;
                float wary = (1 - Smooth((time - LookSeconds) / ReachSeconds)) * w;
                float turn = Smooth((time - ReleaseEnd) / TurnSeconds) * w;
                Rotate(spine, right, LeanDegrees * wary);
                // Negative yaw from south turns her toward the room's east wall.
                Rotate(spine, up, -TurnSpine * turn);
                if (chest != spine) Rotate(chest, up, -TurnChest * turn);
                float yaw = time < LookSeconds ? -ScanYaw * Mathf.Sin(time / LookSeconds * Tau) * w : -TurnHead * turn;
                if (neck != null) Rotate(neck, up, yaw * Half);
                Rotate(head, up, yaw * (neck != null ? Half : 1));
                Rotate(head, right, WaryPitch * wary);
                float grip = Arms * w;
                if (grip > 0)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        var target = GripPoint(side) - up * PalmLength;
                        var pole = upper[side].position + right * (side == 0 ? -PoleOut : PoleOut) - up * PoleDown + forward * PoleForward;
                        SofaRig.TwoBone(upper[side], lower[side], hand[side], target, pole, grip);
                        hand[side].rotation = Quaternion.Slerp(hand[side].rotation,
                            Quaternion.LookRotation(up, forward) * Quaternion.Inverse(handFrame[side]), grip);
                        Curl(side, grip);
                    }
                }
            }
            for (int i = 0; i < controlled.Count; i++) if (controlled[i] != null) written[i] = controlled[i].localRotation;
            hasWritten = true;
        }

        void Curl(int side, float w)
        {
            var axis = hand[side].rotation * Vector3.Cross(fingerAxis[side], palmAxis[side]).normalized;
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
                Rotate(finger[side, f, j], axis, w * (j == 0 ? ProximalCurl : j == 1 ? IntermediateCurl : DistalCurl));
            for (int j = 0; j < 3; j++) Rotate(thumb[side, j], axis, w * ThumbCurl);
        }

        static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        public void Dispose()
        {
            if (disposed) return;
            if (hasWritten) ReadPose();
            disposed = true; wanted = false; Weight = Smile = EyesClosed = 0; Phase = "Idle";
            // The curtain deliberately remains where the last pull left it.
        }
    }
}
