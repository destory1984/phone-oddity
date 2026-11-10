using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    public enum SofaMode { None = 0, Phone = 1, Tv = 2, Bed = 3 }

    // Geometry of the held phone, kept apart from the rig so the tests can read the numbers.
    public static class SofaGeometry
    {
        public const float PhoneW = .072f, PhoneH = .150f, PhoneT = .008f;
        // wrist to the phone's lower corner: sideways, down along the phone, and behind its back plane
        public const float WristLateral = .040f, WristDown = .062f, WristBehind = .012f;
        public const float PalmLength = .045f; // wrist to the middle of the palm
        public const float PalmInward = 30f, FingerInward = 10f; // degrees the palm and the fingers lean toward the phone

        // the phone's axes in the body frame: U along its length (top end), S its screen normal (toward her face), R its right (her right). tilt is the top's lean away from the vertical
        public static void PhoneAxes(Vector3 right, Vector3 up, Vector3 forward, float tiltDeg, float rollDeg, out Vector3 U, out Vector3 S, out Vector3 R)
        {
            float a = tiltDeg * Mathf.Deg2Rad;
            U = (up * Mathf.Cos(a) + forward * Mathf.Sin(a)).normalized;
            S = (-forward * Mathf.Cos(a) + up * Mathf.Sin(a)).normalized;
            R = Vector3.Cross(S, U).normalized;
            if (Mathf.Abs(rollDeg) > 1e-4f) { var q = Quaternion.AngleAxis(rollDeg, S); U = q * U; R = q * R; }
        }

        // where a hand's wrist is and how the hand is turned when it holds the phone: side = -1 left hand, +1 right hand
        public static void Grip(Vector3 C, Vector3 U, Vector3 S, Vector3 R, int side, out Vector3 wrist, out Vector3 fingers, out Vector3 palm)
        {
            float sign = side == 0 ? -1 : 1;
            wrist = C + R * (sign * WristLateral) - U * WristDown - S * WristBehind;
            var inward = -R * sign;
            fingers = (U + inward * Mathf.Tan(FingerInward * Mathf.Deg2Rad)).normalized;
            palm = (S * Mathf.Cos(PalmInward * Mathf.Deg2Rad) + inward * Mathf.Sin(PalmInward * Mathf.Deg2Rad)).normalized;
        }
    }

    // The two sofa motions (P phone, T TV) and the bed one (B phone, lying on the stomach), made by hand on top of whatever pose is playing:
    // after the animation and the seat fixes, torso, neck and head are turned by small angles, both arms go to their places by a two-bone IK,
    // and a phone prop rides on the right hand. All of it is blended in and out by weights (0.4 s), so at weight 0 nothing changes.
    public sealed class SofaRig : IDisposable
    {
        public const float BlendSeconds = .4f;
        readonly Animator anim;
        readonly Transform spine, chest, neck, head;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        readonly Transform[] legUpper = new Transform[2], legLower = new Transform[2], foot = new Transform[2];
        readonly Transform thumbP, thumbI, thumbD;
        readonly Transform[,,] finger = new Transform[2, 4, 3]; // side, finger (index, middle, ring, little), joint (proximal, intermediate, distal)
        readonly Vector3[] palmLocalOf = new Vector3[2]; readonly float[] curlSign = new float[2];
        readonly Quaternion[] frameLocal = new Quaternion[2];
        readonly Vector3[] fingersLocal = new Vector3[2];
        readonly Vector3 headFrontLocal;
        public readonly Transform Phone;
        readonly Renderer screenRenderer; readonly Material screenMat;
        readonly Light glow;
        public readonly Transform LookTarget;
        readonly System.Random rng;

        public SofaMode Mode { get; private set; }
        public float WPhone, WTv, WBed;
        public float Weight => Mathf.Max(WPhone, Mathf.Max(WTv, WBed));
        public Vector3 TvCentre = new Vector3(0, 1.25f, 1.5f); // the TV screen's middle, set by the room
        public Vector3 BedForward = Vector3.back; public float MattressTop; // lying: the way her head points along the bed, the mattress surface under her (y)
        public float PhoneTilt = 36f;                         // current phone tilt, degrees from vertical, for the seated hold
        public float Smile, Blink;                            // wanted face blend 0..1 and the blink pulse
        // B on the stomach: the phone rests low in front of her face, below the eyes: centre 0.13 m over the quilt, 0.32 m ahead of the head bone (about 0.27 in front of the eyes) (the eyes are about 0.07 m in front of the head bone), screen tilted 55 degrees toward her
        public const float BedPhoneHeight = .13f, BedPhoneAhead = .32f, BedPhoneTilt = 55f;
        public Vector3 EyePoint { get; private set; } // about where the eyes are: the head bone, a little forward and up
        public bool HasThumb => thumbP != null;
        public bool LyingTv;                                  // the TV from the bed (Shift+T): she lies on her side, only the head turns to the screen; no lap hands, no seated lean
        public float LastHeadPitch, LastHandLeft, LastHandRight;
        public Vector3 LookPoint { get; private set; }
        public Vector3 PhoneCentre { get; private set; }
        public Vector3 BodyForward { get; private set; } = Vector3.forward; // the horizontal way she faces (head direction when lying)

        public SofaRig(Animator anim, Vector3[] fingersLocal, Vector3[] palmLocal, Vector3 headFrontLocal)
        {
            this.anim = anim;
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? spine;
            neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            for (int s = 0; s < 2; s++)
            {
                upper[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                legUpper[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                legLower[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                foot[s] = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            }
            if (spine == null || head == null || upper[0] == null || upper[1] == null || lower[0] == null || lower[1] == null || hand[0] == null || hand[1] == null)
                throw new InvalidOperationException("SofaRig needs the spine, head and both arms");
            thumbP = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            thumbI = anim.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
            thumbD = anim.GetBoneTransform(HumanBodyBones.RightThumbDistal);
            for (int s = 0; s < 2; s++) for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++) finger[s, f, j] = anim.GetBoneTransform((HumanBodyBones)((s == 0 ? 27 : 42) + f * 3 + j));
            for (int s = 0; s < 2; s++) { this.palmLocalOf[s] = palmLocal[s]; this.fingersLocal[s] = fingersLocal[s]; frameLocal[s] = Quaternion.LookRotation(fingersLocal[s], palmLocal[s]); }
            this.headFrontLocal = headFrontLocal;
            var seed = Environment.GetEnvironmentVariable("ODDITY_CATSEED");
            rng = int.TryParse(seed, out var sd) ? new System.Random(sd + 7) : new System.Random();

            // the phone: a pivot at the phone's centre (x right, y up the length, z away from her face), the screen on its -z face
            var pivot = new GameObject("SofaPhone"); Phone = pivot.transform;
            Child(pivot.transform, "body", PrimitiveType.Cube, Vector3.zero, new Vector3(SofaGeometry.PhoneW, SofaGeometry.PhoneH, SofaGeometry.PhoneT), new Color(.05f, .05f, .06f), true);
            var scr = Child(pivot.transform, "screen", PrimitiveType.Quad, new Vector3(0, 0, -SofaGeometry.PhoneT / 2 - .0004f), new Vector3(SofaGeometry.PhoneW - .006f, SofaGeometry.PhoneH - .012f, 1), new Color(.80f, .90f, 1f), false);
            screenRenderer = scr.GetComponent<Renderer>(); screenMat = screenRenderer.sharedMaterial;
            var lg = new GameObject("PhoneGlow"); lg.transform.SetParent(pivot.transform, false); lg.transform.localPosition = new Vector3(0, 0, -.07f);
            glow = lg.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(.70f, .82f, 1f); glow.range = .5f; glow.intensity = .35f; glow.shadows = LightShadows.None;
            pivot.SetActive(false);
            LookTarget = new GameObject("SofaLook").transform;
            BuildPhoneOffset();
        }

        static GameObject Child(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, Color c, bool lit)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            var mat = new Material(Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit")); mat.SetColor("_BaseColor", c); mat.color = c;
            if (lit) { mat.SetFloat("_Smoothness", .7f); }
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        // the phone's pose in the right hand's own frame, from a canonical pose (so it does not depend on what the clip does)
        Vector3 phoneLocalPos; Quaternion phoneLocalRot = Quaternion.identity;
        void BuildPhoneOffset()
        {
            var r = Vector3.right; var u = Vector3.up; var f = Vector3.forward;
            SofaGeometry.PhoneAxes(r, u, f, 36f, 0, out var U, out var S, out var R);
            var C = Vector3.zero;
            SofaGeometry.Grip(C, U, S, R, 1, out var wrist, out var fingers, out var palm);
            var handRot = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(frameLocal[1]);
            var phoneRot = Quaternion.LookRotation(-S, U);
            phoneLocalRot = Quaternion.Inverse(handRot) * phoneRot;
            phoneLocalPos = Quaternion.Inverse(handRot) * (C - wrist);
        }

        public void SetMode(SofaMode m)
        {
            bool fresh = Mode == SofaMode.None && Weight <= 0; Mode = m;
            if (m == SofaMode.None || !fresh) return;
            activeT = 0; tapAt = R(.4f, .9f); scrollAt = R(1.5f, 3f); tiltAt = R(1f, 3f); liftAt = R(4f, 7f); smileAt = R(2.5f, 5f); blinkAt = R(1f, 2.5f); reactAt = R(5f, 9f); chinAt = R(8f, 14f);
            tapT = scrollT = liftT = smileT = blinkT = reactT = chinT = -1;
        }
        // everything off at once (she stood up, walked off): no blend, the phone is put away
        public void Clear() { Mode = SofaMode.None; WPhone = WTv = WBed = 0; if (Phone != null) Phone.gameObject.SetActive(false); }
        public void SetImmediate(SofaMode m) { SetMode(m); WPhone = m == SofaMode.Phone ? 1 : 0; WTv = m == SofaMode.Tv ? 1 : 0; WBed = m == SofaMode.Bed ? 1 : 0; }

        // ---- the small lives: each is a timer and a pulse
        float activeT, tapAt, tapT = -1, scrollAt, scrollT = -1, tiltAt, tiltPitch, tiltRoll, tiltPitchWant, tiltRollWant, liftAt, liftT = -1, smileAt, smileT = -1, blinkAt = 2, blinkT = -1;
        float reactAt, reactT = -1; int reactKind; float shiftSide; float kickPhase; float chinAt, chinT = -1;
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float Bump(float t, float d) => t < 0 || t > d ? 0 : Mathf.Sin(Mathf.PI * t / d);

        float tapNow, scrollNow, liftNow, smileNow, laughNow, tiltHead, breathe, shiftNow, chinNow;
        public void Tick(float dt)
        {
            WPhone = Mathf.MoveTowards(WPhone, Mode == SofaMode.Phone ? 1 : 0, dt / BlendSeconds);
            WTv = Mathf.MoveTowards(WTv, Mode == SofaMode.Tv ? 1 : 0, dt / BlendSeconds);
            WBed = Mathf.MoveTowards(WBed, Mode == SofaMode.Bed ? 1 : 0, dt / BlendSeconds);
            if (Weight <= 0) return;
            activeT += dt;
            breathe = Mathf.Sin(activeT * Mathf.PI * 2 / 4.2f);
            // blink every 2.5 - 5 s, 0.16 s long
            if (blinkT >= 0) { blinkT += dt; if (blinkT > .16f) blinkT = -1; } else if (activeT > blinkAt) { blinkT = 0; blinkAt = activeT + R(2.5f, 5f); }
            Blink = Bump(blinkT, .16f);
            bool phone = WPhone + WBed > .01f;
            // phone: thumb taps every 0.6 - 1.5 s, a scroll now and then, the phone tilts a little every few seconds
            if (tapT >= 0) { tapT += dt; if (tapT > .2f) tapT = -1; } else if (activeT > tapAt) { tapT = 0; tapAt = activeT + R(.6f, 1.5f); }
            if (scrollT >= 0) { scrollT += dt; if (scrollT > .35f) scrollT = -1; } else if (activeT > scrollAt) { scrollT = 0; scrollAt = activeT + R(2.5f, 5f); }
            tapNow = Bump(tapT, .2f); scrollNow = Bump(scrollT, .35f);
            if (activeT > tiltAt) { tiltAt = activeT + R(3f, 6f); tiltPitchWant = R(-4f, 4f); tiltRollWant = R(-4f, 4f); }
            tiltPitch = Mathf.MoveTowards(tiltPitch, tiltPitchWant, dt * 3f); tiltRoll = Mathf.MoveTowards(tiltRoll, tiltRollWant, dt * 3f);
            // every 6 - 10 s she lifts her head for a breath (1.4 s) and looks back down
            if (liftT >= 0) { liftT += dt; if (liftT > 1.4f) liftT = -1; } else if (activeT > liftAt) { liftT = 0; liftAt = activeT + R(6f, 10f); }
            liftNow = Bump(liftT, 1.4f);
            // a tiny smile now and then: 0.6 for a second
            if (smileT >= 0) { smileT += dt; if (smileT > 1f) smileT = -1; } else if (activeT > smileAt) { smileT = 0; smileAt = activeT + R(7f, 12f); }
            smileNow = Bump(smileT, 1f);
            // the TV: every 8 - 15 s a reaction: 0 soft laugh with the shoulders (0.6 s), 1 slow head tilt (2.2 s), 2 shifting her weight (2 s)
            float reactLen = reactKind == 0 ? .6f : reactKind == 1 ? 2.2f : 2f;
            if (reactT >= 0) { reactT += dt; if (reactT > reactLen) { reactT = -1; if (reactKind == 2) shiftSide = -shiftSide; } }
            else if (activeT > reactAt && WTv > .5f) { reactT = 0; reactKind = rng.Next(3); if (shiftSide == 0) shiftSide = 1; reactAt = activeT + R(8f, 15f); }
            else if (reactAt <= 0) reactAt = activeT + R(4f, 8f);
            laughNow = reactT >= 0 && reactKind == 0 ? Bump(reactT, .6f) : 0;
            tiltHead = reactT >= 0 && reactKind == 1 ? Bump(reactT, 2.2f) : 0;
            shiftNow = reactT >= 0 && reactKind == 2 ? Bump(reactT, 2f) * shiftSide : 0;
            // the bed: the shins swing, alternating every 1 - 2 s, and now and then her chin rests on a hand
            kickPhase += dt * Mathf.PI * 2 / 3.2f;
            if (chinT >= 0) { chinT += dt; if (chinT > 3f) chinT = -1; } else if (activeT > chinAt) { chinT = 0; chinAt = activeT + R(10f, 18f); }
            chinNow = WBed > .5f ? Bump(chinT, 3f) : 0;
            float smileBase = WTv > WPhone ? .25f : .12f;
            Smile = Mathf.Max(smileBase, smileNow * .6f, laughNow * .9f);
            if (!phone) { /* no phone */ }
        }

        // ---- posing
        float Pitch(Vector3 dir) => Mathf.Asin(Mathf.Clamp(-dir.y, -1, 1)) * Mathf.Rad2Deg; // positive = looking down

        public void Apply(float dt)
        {
            float wp = Smooth(WPhone), wt = Smooth(WTv), wb = Smooth(WBed), wa = Mathf.Max(wp, Mathf.Max(wt, wb));
            float wts = LyingTv ? 0 : wt; // the seated TV parts (torso lean, hands on the thighs)
            bool showPhone = wp + wb > .02f;
            if (Phone != null && Phone.gameObject.activeSelf != showPhone) Phone.gameObject.SetActive(showPhone);
            if (wa <= 0) return;
            var root = anim.transform;
            var up = Vector3.up;
            var fwd = wb > 0 ? Vector3.ProjectOnPlane(BedForward, up).normalized : Vector3.ProjectOnPlane(root.forward, up).normalized;
            var right = Vector3.Cross(up, fwd).normalized; BodyForward = fwd;
            float breath = breathe;

            // ---- torso: the seated ones lean back a little, the TV more; she breathes
            float spineBow = wp * -2f + wts * (-5f + 1.2f * shiftNow * 0) + wb * 0f;
            float chestBow = wp * 1f + wts * -3f + (wp + wts) * breath * .6f + wb * breath * .5f + laughNow * wts * (Mathf.Sin(activeT * 40f) * 2.2f);
            Rot(spine, right, spineBow);
            if (chest != spine) Rot(chest, right, chestBow); else Rot(spine, right, chestBow);
            if (wts > 0 && Mathf.Abs(shiftNow) > .001f) Rot(spine, fwd, 3.5f * shiftNow * wts); // weight onto one hip
            if (wts > 0 && laughNow > 0) { Rot(chest, fwd, Mathf.Sin(activeT * 31f) * 1.4f * laughNow * wts); }

            // ---- phone frame
            Vector3 U = Vector3.up, S = Vector3.forward, Rr = Vector3.right, C = Vector3.zero;
            if (wp + wb > .01f)
            {
                var anchor = (chest ?? spine).position;
                float tilt = wb > .5f ? BedPhoneTilt + tiltPitch : PhoneTilt + tiltPitch;
                SofaGeometry.PhoneAxes(right, up, fwd, tilt, tiltRoll, out U, out S, out Rr);
                if (wb > .5f)
                {
                    var hp = head.position;
                    C = new Vector3(hp.x, MattressTop + BedPhoneHeight, hp.z) + fwd * BedPhoneAhead;
                }
                else C = anchor + fwd * .34f - up * .02f;
                // a tap presses the phone in, a scroll slides it up
                C += -S * (.003f * tapNow) + U * (.010f * scrollNow);
                PhoneCentre = C;
            }
            Vector3 gaze = head.position + fwd;

            // ---- head: P and B look down at the screen (absolute pitch), T turns toward the TV
            var headNow = head.rotation * headFrontLocal;
            Vector3 want = headNow;
            if (wp + wb > .01f)
            {
                float pitch = wb > wp ? 36f : 27f - 14f * liftNow;
                var d = (fwd * Mathf.Cos(pitch * Mathf.Deg2Rad) - up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
                want = d; gaze = C;
            }
            if (wt > 0 && LyingTv)
            {   // lying: the head follows the TV by up to 60 degrees from where the pose has it
                var dd = (TvCentre - head.position).normalized;
                var lim = Vector3.RotateTowards(headNow, dd, 60f * Mathf.Deg2Rad, 0);
                want = Vector3.Slerp(want, lim, wt / Mathf.Max(.001f, wp + wt + wb));
                gaze = Vector3.Lerp(gaze, TvCentre, wt / Mathf.Max(.001f, wp + wt + wb));
            }
            else if (wt > 0)
            {
                var toTv = TvCentre - head.position;
                var flat = Vector3.ProjectOnPlane(toTv, up);
                float yaw = Vector3.SignedAngle(fwd, flat, up); yaw = Mathf.Clamp(yaw, -40f, 40f);
                float pit = Mathf.Clamp(Pitch(toTv.normalized), -15f, 25f);
                var d = Quaternion.AngleAxis(yaw, up) * fwd; d = (d * Mathf.Cos(pit * Mathf.Deg2Rad) - up * Mathf.Sin(pit * Mathf.Deg2Rad)).normalized;
                want = Vector3.Slerp(want, d, wt / Mathf.Max(.001f, wp + wt + wb));
                gaze = Vector3.Lerp(gaze, TvCentre, wt / Mathf.Max(.001f, wp + wt + wb));
            }
            var turn = Quaternion.FromToRotation(headNow, want);
            var k = wa;
            var turnK = Quaternion.Slerp(Quaternion.identity, turn, k);
            if (neck != null) { neck.rotation = Quaternion.Slerp(Quaternion.identity, turnK, .4f) * neck.rotation; head.rotation = Quaternion.Slerp(Quaternion.identity, turnK, .6f) * head.rotation; }
            else head.rotation = turnK * head.rotation;
            if (tiltHead > 0) head.rotation = Quaternion.AngleAxis(8f * tiltHead * wt, head.rotation * headFrontLocal) * head.rotation; // the slow head tilt
            LastHeadPitch = Pitch(head.rotation * headFrontLocal);
            EyePoint = head.position + (head.rotation * headFrontLocal).normalized * .07f + up * .06f;
            LookPoint = gaze; LookTarget.position = gaze;

            // ---- arms
            if (wp + wb > .01f)
            {
                for (int s = 0; s < 2; s++)
                {
                    SofaGeometry.Grip(C, U, S, Rr, s, out var wrist, out var fingers, out var palm);
                    float hw = wp + wb > 1 ? 1 : wp + wb;
                    if (s == 0 && chinNow > .01f)
                    {   // the left hand lets go and holds the chin
                        var chin = head.position + (head.rotation * headFrontLocal) * .07f - up * .08f - right * .02f;
                        wrist = Vector3.Lerp(wrist, chin - Vector3.Cross(U, S) * 0f, Smooth(chinNow));
                    }
                    var pole = upper[s].position - up * .35f + right * (s == 0 ? -.2f : .2f) - fwd * .05f;
                    if (wb > .5f) pole = upper[s].position - up * .5f + right * (s == 0 ? -.15f : .15f);
                    TwoBone(upper[s], lower[s], hand[s], wrist, pole, hw);
                    var wantRot = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(frameLocal[s]);
                    TwistShare(lower[s], hand[s], wantRot, hw);
                    hand[s].rotation = Quaternion.Slerp(hand[s].rotation, wantRot, hw);
                    Curl(s, hw, HoldCurl);
                }
                if (Phone != null)
                {
                    float sc = Mathf.SmoothStep(0, 1, Mathf.Clamp01((wp + wb) * 1.6f - .1f));
                    var ls = hand[1].lossyScale.x > 1e-4f ? 1f / hand[1].lossyScale.x : 1f;
                    if (Phone.parent != hand[1]) Phone.SetParent(hand[1], false);
                    Phone.localPosition = phoneLocalPos * ls; Phone.localRotation = phoneLocalRot; Phone.localScale = Vector3.one * ls * sc;
                    PhoneCentre = Phone.position;
                    glow.intensity = .35f * sc;
                    ThumbTap(S, Rr, tapNow, wp + wb);
                }
            }
            if (wts > .01f)
            {
                for (int s = 0; s < 2; s++)
                {
                    var thigh = legUpper[s] != null && legLower[s] != null ? Vector3.Lerp(legUpper[s].position, legLower[s].position, .55f) : hand[s].position;
                    float sign = s == 0 ? -1 : 1;
                    var wrist = thigh + up * .085f + fwd * -.04f + right * (-sign * .02f);
                    if (s == 1) wrist += right * -.05f; // the right hand rests over the left
                    var pole = upper[s].position - up * .35f + right * sign * .2f - fwd * .05f;
                    TwoBone(upper[s], lower[s], hand[s], wrist, pole, wt);
                    var fingers = (fwd + right * (-sign) * .35f).normalized; var palm = -up;
                    var wantRot = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(frameLocal[s]);
                    TwistShare(lower[s], hand[s], wantRot, wt);
                    hand[s].rotation = Quaternion.Slerp(hand[s].rotation, wantRot, wt);
                    Curl(s, wt, LapCurl);
                }
            }

            // ---- bed: the lower legs bend up from the knees and swing, alternately
            if (wb > .01f && legLower[0] != null && legLower[1] != null) Kick(right, up, wb);

            if (Phone != null && Phone.gameObject.activeSelf)
            {
                LastHandLeft = Vector3.Distance(hand[0].position + hand[0].rotation * fingersLocal[0] * SofaGeometry.PalmLength, Phone.position);
                LastHandRight = Vector3.Distance(hand[1].position + hand[1].rotation * fingersLocal[1] * SofaGeometry.PalmLength, Phone.position);
            }
        }

        // B, the last word, called after the model has been lifted onto the quilt and the legs and neck have been fixed (LieFinish):
        // the knees never bend past MaxKneeFlex (the clip's own bend plus the 75 - 105 degree swing could add up to more), and the head is turned so the nose points
        // from the real eyes to the real phone, not at where the phone was before the body was placed
        public const float MaxKneeFlex = 125f;
        public float LastKneeFlexLeft, LastKneeFlexRight, LastBedGazeError;
        public static float KneeFlex(Transform thigh, Transform shin, Transform foot) => Vector3.Angle(shin.position - thigh.position, foot.position - shin.position); // degrees, 0 = straight
        public void FinishBed()
        {
            float wb = Smooth(WBed);
            if (wb <= .01f || head == null) return;
            for (int s = 0; s < 2; s++)
            {
                if (legUpper[s] == null || legLower[s] == null || foot[s] == null) continue;
                float flex = KneeFlex(legUpper[s], legLower[s], foot[s]);
                if (flex > MaxKneeFlex)
                {
                    var axis = Vector3.Cross(legLower[s].position - legUpper[s].position, foot[s].position - legLower[s].position);
                    if (axis.sqrMagnitude > 1e-8f) legLower[s].rotation = Quaternion.AngleAxis(-(flex - MaxKneeFlex), axis.normalized) * legLower[s].rotation;
                }
                float after = KneeFlex(legUpper[s], legLower[s], foot[s]);
                if (s == 0) LastKneeFlexLeft = after; else LastKneeFlexRight = after;
            }
            if (Phone == null || !Phone.gameObject.activeSelf) return;
            var up = Vector3.up;
            Vector3 nose = (head.rotation * headFrontLocal).normalized;
            for (int pass = 0; pass < 4; pass++) // the neck turns about a different pivot than the head, so each pass leaves a little: a few passes close it
            {
                nose = (head.rotation * headFrontLocal).normalized;
                Vector3 eye = head.position + nose * .07f + up * .06f;
                Vector3 want = (Phone.position - eye).normalized;
                var turn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(nose, want), wb);
                if (neck != null) { neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, .4f) * neck.rotation; head.rotation = Quaternion.Slerp(Quaternion.identity, turn, .6f) * head.rotation; }
                else head.rotation = turn * head.rotation;
            }
            nose = (head.rotation * headFrontLocal).normalized;
            EyePoint = head.position + nose * .07f + up * .06f;
            LastBedGazeError = Vector3.Angle(nose, (Phone.position - EyePoint).normalized);
            LastHeadPitch = Pitch(nose);
            PhoneCentre = Phone.position; LookPoint = Phone.position; LookTarget.position = Phone.position;
        }

        internal static void Kill(GameObject g) { if (g == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(g); else UnityEngine.Object.DestroyImmediate(g); }
        static float Smooth(float w) => Mathf.SmoothStep(0, 1, w);
        static void Rot(Transform b, Vector3 axis, float deg) { if (b != null && Mathf.Abs(deg) > 1e-4f) b.rotation = Quaternion.AngleAxis(deg, axis) * b.rotation; }

        // the hand is turned to a wanted world rotation; half of the twist about the forearm goes into the forearm so the sleeve is not wrung at the wrist
        static void TwistShare(Transform forearm, Transform hand, Quaternion wantHand, float w)
        {
            var axis = (hand.position - forearm.position).normalized;
            var delta = wantHand * Quaternion.Inverse(hand.rotation);
            delta.ToAngleAxis(out float ang, out var ax); if (ang > 180) ang -= 360;
            float along = ang * Vector3.Dot(ax, axis);
            if (Mathf.Abs(along) < .5f) return;
            forearm.rotation = Quaternion.AngleAxis(along * .5f * w, axis) * forearm.rotation;
        }

        void Kick(Vector3 right, Vector3 up, float w)
        {
            for (int s = 0; s < 2; s++)
            {
                // the knee bends about the lateral axis; the sign that lifts the foot is chosen by trying both
                float swing = Mathf.Sin(kickPhase + (s == 0 ? 0 : Mathf.PI)) * .5f + .5f; // 0..1
                float bend = Mathf.Lerp(75f, 105f, swing) * w;
                var axis = right;
                var f0 = foot[s].position;
                legLower[s].rotation = Quaternion.AngleAxis(bend, axis) * legLower[s].rotation;
                if (foot[s].position.y < f0.y) legLower[s].rotation = Quaternion.AngleAxis(-2 * bend, axis) * legLower[s].rotation;
            }
        }

        // the four fingers curl toward the palm: proximal, intermediate and distal joints by these degrees (the sign is found by trying the middle finger once)
        static readonly float[] HoldCurl = { 22f, 28f, 14f }, LapCurl = { 26f, 32f, 18f };
        void Curl(int s, float amount, float[] deg)
        {
            if (amount <= .001f || finger[s, 1, 0] == null) return;
            var F = (hand[s].rotation * fingersLocal[s]).normalized; var P = (hand[s].rotation * palmLocalOf[s]).normalized;
            var axis = Vector3.Cross(F, P).normalized;
            if (curlSign[s] == 0)
            {
                var prox = finger[s, 1, 0]; var tip = finger[s, 1, 2] ?? finger[s, 1, 1] ?? prox;
                var before = tip.position; var keep = prox.rotation;
                prox.rotation = Quaternion.AngleAxis(20f, axis) * prox.rotation;
                curlSign[s] = Vector3.Dot(tip.position - before, P) > 0 ? 1 : -1;
                prox.rotation = keep;
            }
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++)
            { var b = finger[s, f, j]; if (b != null) b.rotation = Quaternion.AngleAxis(curlSign[s] * deg[j] * amount, axis) * b.rotation; }
        }

        void ThumbTap(Vector3 S, Vector3 R, float tap, float w)
        {
            if (thumbP == null || tap <= 0) return;
            var before = (thumbD ?? thumbP).position;
            thumbP.rotation = Quaternion.AngleAxis(14f * tap * w, R) * thumbP.rotation;
            if (thumbI != null) thumbI.rotation = Quaternion.AngleAxis(10f * tap * w, R) * thumbI.rotation;
            var after = (thumbD ?? thumbP).position;
            if (Vector3.Dot(after - before, -S) < 0)
            {
                thumbP.rotation = Quaternion.AngleAxis(-28f * tap * w, R) * thumbP.rotation;
                if (thumbI != null) thumbI.rotation = Quaternion.AngleAxis(-20f * tap * w, R) * thumbI.rotation;
            }
        }

        // turns upper then lower so the wrist reaches target with the elbow bent toward pole, by weight w
        public static void TwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, float w)
        {
            Vector3 s = upper.position, e = lower.position, h = hand.position;
            float a = (e - s).magnitude, b = (h - e).magnitude; var d = target - s;
            float len = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f); var dir = d.normalized;
            var bend = Vector3.ProjectOnPlane(pole - s, dir); if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(e - s, dir); bend.Normalize();
            float cosA = Mathf.Clamp((a * a + len * len - b * b) / (2 * a * len), -1, 1); var elbowAt = s + dir * a * cosA + bend * a * Mathf.Sqrt(1 - cosA * cosA);
            upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(e - s, elbowAt - s), w) * upper.rotation;
            e = lower.position; h = hand.position;
            lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(h - e, s + dir * len - e), w) * lower.rotation;
        }

        public void Dispose()
        {
            Kill(Phone != null ? Phone.gameObject : null);
            Kill(LookTarget != null ? LookTarget.gameObject : null);
        }
    }

    // The wall TV: a slim black body, a screen that is dark when off and shows a slowly changing colour when on, and a soft light in front of it.
    public sealed class SofaTv : IDisposable
    {
        public readonly Transform Root;
        public readonly Vector3 ScreenCentre;
        readonly Material screenMat; readonly Light light;
        public bool On;
        static readonly Color[] Palette = { new Color(.80f, .84f, .95f), new Color(.62f, .74f, 1f), new Color(.88f, .90f, .96f), new Color(.72f, .84f, .92f) };
        static readonly Color Off = new Color(.025f, .028f, .035f);
        float t, hold; int from, to; Color shown = Off; float fade;

        public SofaTv(Transform parent, Vector3 centre, Vector3 face, Vector3 size)
        {
            Root = new GameObject("TV").transform; Root.SetParent(parent, false);
            Root.position = centre; Root.rotation = Quaternion.LookRotation(face, Vector3.up); // local z = the way the screen faces
            Part("tv_body", PrimitiveType.Cube, Vector3.zero, new Vector3(size.x, size.y, size.z), new Color(.04f, .04f, .045f), true);
            var scr = Part("tv_screen", PrimitiveType.Quad, new Vector3(0, 0, size.z / 2 + .0006f), new Vector3(size.x - .03f, size.y - .03f, 1), Off, false);
            scr.transform.localRotation = Quaternion.Euler(0, 180, 0); // a Quad shows its face toward -z, this one looks out along +z
            screenMat = scr.GetComponent<Renderer>().sharedMaterial;
            ScreenCentre = Root.position + face.normalized * (size.z / 2 + .0006f);
            var lg = new GameObject("TvGlow"); lg.transform.SetParent(Root, false); lg.transform.localPosition = new Vector3(0, 0, size.z / 2 + .7f);
            light = lg.AddComponent<Light>(); light.type = LightType.Point; light.range = 2.8f; light.intensity = 0; light.shadows = LightShadows.None; light.color = Palette[0];
        }
        GameObject Part(string name, PrimitiveType type, Vector3 pos, Vector3 size, Color c, bool lit)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(Root, false); g.transform.localPosition = pos; g.transform.localScale = size;
            var mat = new Material(Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit")); mat.SetColor("_BaseColor", c); mat.color = c;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }
        public Color Shown => shown;
        public void Tick(float dt)
        {
            fade = Mathf.MoveTowards(fade, On ? 1 : 0, dt / .4f);
            t += dt;
            if (t > hold) { from = to; to = (to + 1 + UnityEngine.Random.Range(0, Palette.Length - 1)) % Palette.Length; t = 0; hold = UnityEngine.Random.Range(1.2f, 2.6f); }
            var live = Color.Lerp(Palette[from], Palette[to], Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .6f)));
            shown = Color.Lerp(Off, live, fade);
            screenMat.SetColor("_BaseColor", shown); screenMat.color = shown;
            light.color = Color.Lerp(live, new Color(.75f, .82f, 1f), .6f); light.intensity = .55f * fade;
        }
        public void Dispose() { if (Root != null) SofaRig.Kill(Root.gameObject); }
    }
}
