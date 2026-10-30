using System;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// The 10 second "Harin comes out of the phone" scene (storyboard cuts 10 to 12), written as a script
    /// instead of a Timeline: <see cref="Evaluate"/> sets every object from the time alone, so tests can run
    /// it faster with <see cref="Speed"/> and screenshots can jump to any moment with <see cref="Seek"/>.
    ///   0.0 → 0.5  cut 9: the phone screen, glitch 0 → 1, she looks to the side
    ///   0.5 → 4.0  cut 10: fixed Cut10Camera over a 2D desk-and-phone picture; her right arm comes out of
    ///              the phone screen and the hand lands on the desk (the rest of her is hidden under the desk plane)
    ///   4.0 → 6.5  cut 11: the room, full body, looking down at her hands; turns to the bed at 5.5, starts to sit at 6.0
    ///   6.5 → 10   cut 12: sitting on the bed edge; eyes to the camera at 8.0; room lamp dims
    /// Harin is never disabled or replaced; only her transform, pose and face change.
    /// </summary>
    public class EmergenceSequence : MonoBehaviour
    {
        public const float Cut10At = 0.5f;
        public const float RoomAt = 4f;
        public const float TurnAt = 5.5f;
        public const float SitAt = 6f;
        public const float SeatAt = 6.5f;
        public const float EyesAt = 8f;
        public const float LampDownAt = 8.5f;

        // ---------------------------------------------------------------- cut 10 (2D picture + real arm)
        // The picture is laid onto a flat desk plane, as seen from a camera 0.56 m above it, pitched 40°
        // down with a 38° vertical field of view. At 0.56 m the phone screen in the picture comes out
        // about 7.9 by 14.6 cm on the plane, a real phone's size, so her hand has the right size next to it.
        // The rig sits far from the room so nothing else shows.
        static readonly Vector3 Cut10DeskOrigin = new Vector3(0f, 5f, -40f);
        const float Cut10CamHeight = 0.56f;
        const float Cut10Pitch = 40f;
        const float Cut10Fov = 38f;
        const float ImageW = 1600f, ImageH = 2844f;
        // phone screen corners in the picture (pixels, y down): top left, top right, bottom right, bottom left
        static readonly Vector2[] ScreenPx = { new Vector2(71, 1803), new Vector2(426, 1683), new Vector2(824, 2166), new Vector2(383, 2357) };
        static readonly Vector2 HandTowardsPx = new Vector2(1300, 2350); // the hand reaches towards here (Task 29: less towards the camera)
        const int DeskGridX = 24, DeskGridY = 40;
        // Task 29: the wrist was 7 cm up and the hand hung down towards the camera, so it showed 1.45x the phone's
        // long side on screen (a 14.6 cm hand next to a 14.6 cm phone). Lower, the palm rests on the desk
        // and the hand is foreshortened like the phone.
        // The forearm still rises steeply (49°) out of the screen: a flatter one showed its top 6 cm behind
        // the crossing, outside the ring. The wrist bends back so the palm lies on the desk.
        const float WristAbove = 0.045f;     // wrist height over the screen once out (the palm rests on the desk)
        const float WristOut = 0.06f;        // wrist distance from the crossing point along the forearm (6 cm of forearm)
        const float BodyTiltDeg = 5f;        // her up axis under the desk, lifted this much from the reach direction (head and feet both stay under the plane)
        const float UpperArmSteeper = 10f;   // upper arm angle over the forearm angle (under the desk)
        const float RingRadius = 0.055f;    // covers the forearm's cut through the screen (drawn over the arm)
        const float GlowRadius = 0.13f;
        // contact shadow: a broad soft blob under the whole hand and a darker one under palm and fingers,
        // nudged away from the lamp; faint while the hand is over the desk, dark once it rests on it
        const float ShadowWidth = 0.1f, ShadowCoreWidth = 0.06f, ShadowLampShift = 0.02f;
        const float ShadowNear = 0.15f, ShadowTouch = 0.6f, ShadowCoreTouch = 0.75f;
        // violet light over the forearm where it goes into the screen, drawn over the arm, so the cut
        // through the desk plane reads as the arm sinking into light
        // (set back from the reach: the forearm's top meets the plane behind the crossing, the wrist stays clear)
        const float VeilRadius = 0.05f, VeilAlpha = 0.9f, VeilBack = 0.025f;
        // blended, not added: arm and screen under it turn the same colour, so the edge of the cut disappears
        static readonly Color VeilColor = new Color(0.78f, 0.62f, 1f);
        // violet light from the portal, low on the arm (a rim from below)
        static readonly Color PortalLightColor = new Color(0.55f, 0.35f, 1f);
        // (5 cm under the desk plane: right at the arm a point light burns it white)
        const float PortalLightIntensity = 0.004f, PortalLightRange = 0.3f, PortalLightDepth = 0.05f;
        // wrist light: the swirl picture lies over the ring and turns 20 → 40 °/s from 1.0 to 4.0 s,
        // brightest while the wrist comes out (2.0 to 3.0 s)
        const float SwirlFrom = 1f, SwirlPeakAt = 2.5f, SwirlSpeed0 = 20f, SwirlSpeed1 = 40f, SwirlRadius = 0.065f;
        const float SwirlEllipseW = 0.92f, SwirlEllipseH = 0.55f;   // the swirl's ellipse in its picture (fraction of the side)
        // warm lamp light on her in cut 10 only, from the upper left of the picture (the desk lamp side)
        // low (about 20° over the desk) so the sides of the fingers away from the lamp fall into shade
        const float Cut10WarmKelvin = 4800f, Cut10WarmIntensity = 0.85f;
        static readonly Vector3 Cut10WarmDir = new Vector3(0.85f, -0.35f, -0.4f);
        const float Cut10Glitch = 0.2f;      // body jitter while the arm is out (1 cm at 1)

        // ---------------------------------------------------------------- cut 11 and 12 (the room)
        static readonly Vector3 StandPosition = new Vector3(-0.6f, 0f, -0.2f);
        const float StandFacingDeg = 55f;    // yaw of her front and of the camera direction (0 = +Z)
        const float TurnDeg = 15f;
        const float Cut11CamHeight = 1.25f, Cut11Dist0 = 2.6f, Cut11Dist1 = 3f, Cut11Fov0 = 38f, Cut11Fov1 = 42f;
        const float Cut11LookHeight = 0.8f;
        const float SeatInFromEdge = 0.15f;  // hip contact this far in from the mattress front edge
        const float SeatZ = -0.55f;
        // cut 12 and act 2: a waist-up shot of her seated. The camera sits Act2EyeDrop under her eyes, her eyes
        // Act2EyeLine down the frame (above the subtitle band), and waist to head top fills Act2BodyShare of the
        // frame height at the end. The camera still pulls back during cut 12, from Cut12PullFrom of that distance.
        public const float Act2EyeLine = 0.32f; // Head + 7.5 cm; the drawn eyes land about 2% lower
        // Task 27: 2 cm lower (7 -> 9 cm under her eyes) and 0.9 of the distance, so the shot looks at her
        // from her eye level instead of down at a small face.
        public const float Act2EyeDrop = 0.09f;
        public const float Act2Closer = 0.9f;
        /// <summary>On her own act 2 lines her head turns this much towards the camera and lifts as much.</summary>
        public const float Act2HeadToCamera = 3f;
        /// <summary>Extra degrees the seated head lifts on lines that look at the camera (on top of Act2HeadToCamera).</summary>
        public const float Act2HeadLift = 6f;
        public const float Act2EaseSeconds = 0.4f;
        public const float Act2BodyShare = 0.6f;
        public const float Act2HeadTopAboveEye = 0.11f; // model units
        const float Cut12Fov0 = 35f, Cut12Fov1 = 38f;
        const float Cut12PullFrom = 0.8f;
        const float Cut12SideDeg = 20f;
        // the 6500 K face light and catch light of the prologue, used in the room too (cuts 11, 12 and act 2)
        // (the prologue's light sits about 1.1 m from her face; further away here, so it is scaled by distance squared)
        const float FaceLightIntensity = FaceLights.KeyIntensity;
        const float FaceLightRefDistance = 1.1f, FaceLightMaxBoost = 3f;
        // lights (Kelvin; intensity multipliers on the scene values)
        const float LampKelvin = 3500f, WindowKelvin = 7000f;
        const float LampCut11 = 0.8f, LampCut12 = 0.55f, LampLate = 0.4f;
        const float WindowScale = 0.6f;
        const float Cut10KeyKelvin = 4200f, Cut10KeyScale = 1f;

        [SerializeField] HarinPuppet harin;
        [SerializeField] ChatView chat;
        [SerializeField] Camera mainCamera;
        [SerializeField] Transform phone;
        [SerializeField] Transform roomAnchor;
        [SerializeField] Texture2D deskPicture;
        [SerializeField] Texture2D screenNoise;
        [SerializeField] Texture2D portalRing;
        [SerializeField] Texture2D wristSwirl;

        /// <summary>Playback speed multiplier (tests use more than 1).</summary>
        public float Speed = 1f;

        public event Action Finished;

        public float Duration => 10f;
        public float Time { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool HasFinished { get; private set; }
        /// <summary>Where she sits on the bed at the end (her root transform).</summary>
        public Transform RoomAnchor => roomAnchor;
        public Camera MainCamera => mainCamera;
        public Camera Cut10Camera => _cut10Cam;
        /// <summary>Phone screen corners on the cut 10 desk plane (TL, TR, BR, BL as in the picture).</summary>
        public Vector3[] ScreenCorners => _screenCorners;
        public float DeskPlaneY => Cut10DeskOrigin.y;
        /// <summary>The cut 10 contact shadow under her hand (null before the first play).</summary>
        public Renderer ContactShadow => _shadow != null ? _shadow.GetComponent<Renderer>() : null;
        public float ContactShadowAlpha => _shadowMat != null ? _shadowMat.GetColor("_Color").a : 0f;
        public float MattressTop { get; private set; } = 0.45f;
        public float MattressFrontX { get; private set; } = -0.92f;

        Transform _home;       // where Harin stood in the app stage
        Vector3 _homeScale;
        Transform _anchor;     // moved every frame for cut 10 and 11
        Vector3 _camStartPos;
        Quaternion _camStartRot;
        float _camStartFov;
        PhoneCameraFit _fit;
        bool _prepared;

        Camera _cut10Cam;
        Transform _rig;
        Vector3[] _screenCorners;
        Vector3 _cross;        // where the arm passes through the screen
        Vector3 _reachDir;     // horizontal direction of the reach on the desk
        Material _noiseMat, _ringMat, _glowMat, _shadowMat, _shadowCoreMat, _swirlMat;
        Transform _ring, _glow, _shadow, _shadowCore, _swirlPivot, _veil;
        Material _veilMat;
        Light _portalLight;

        Light _lamp, _window, _keyLight;
        Light _faceLight, _catchLight;
        bool _seatedFraming;   // cut 12 / act 2: LateUpdate frames her once the seated pose is on the bones
        float _seatedU;
        Vector3 _eyes;
        Color _lampColor, _windowColor, _keyColor;
        float _lampIntensity, _windowIntensity, _keyIntensity;

        void Awake() => FindReferences();

        /// <summary>Art for cut 10 (GameBootstrap passes its serialized textures).</summary>
        public void SetArt(Texture2D desk, Texture2D noise, Texture2D ring, Texture2D swirl = null)
        {
            if (desk != null) deskPicture = desk;
            if (noise != null) screenNoise = noise;
            if (ring != null) portalRing = ring;
            if (swirl != null) wristSwirl = swirl;
        }

        void FindReferences()
        {
            if (harin == null) harin = HarinWardrobe.ActivePuppet();
            if (chat == null) chat = FindFirstObjectByType<ChatView>();
            if (mainCamera == null)
            {
                var go = GameObject.Find("MainCamera");
                mainCamera = go != null ? go.GetComponent<Camera>() : Camera.main;
            }
            if (phone == null)
            {
                var go = GameObject.Find("Phone");
                if (go != null) phone = go.transform;
            }
            var mattress = GameObject.Find("Mattress");
            if (mattress != null)
            {
                var b = mattress.GetComponent<Renderer>().bounds;
                MattressTop = b.max.y;
                MattressFrontX = b.max.x;
            }
            if (roomAnchor == null)
            {
                var go = GameObject.Find("RoomAnchor");
                if (go == null) go = new GameObject("RoomAnchor");
                roomAnchor = go.transform;
                // seated on the bed edge, facing away from the wall (+X)
                var rot = Quaternion.LookRotation(Vector3.right);
                var seat = new Vector3(MattressFrontX - SeatInFromEdge, MattressTop, SeatZ);
                var local = harin != null ? Vector3.Scale(harin.SeatContactLocal, harin.transform.localScale) : new Vector3(0f, 0.75f, 0f);
                roomAnchor.SetPositionAndRotation(seat - rot * local, rot);
            }
        }

        public void Play()
        {
            Prepare();
            Time = 0f;
            HasFinished = false;
            IsPlaying = true;
            Evaluate(0f);
        }

        /// <summary>Jumps to time t (seconds) and holds there while <see cref="Speed"/> is 0.</summary>
        public void Seek(float t)
        {
            if (!IsPlaying && !HasFinished) Play();
            Time = Mathf.Clamp(t, 0f, Duration);
            Evaluate(Time);
            if (Time >= Duration) Finish();
        }

        void Update()
        {
            if (!IsPlaying) return;
            Time = Mathf.Min(Duration, Time + UnityEngine.Time.deltaTime * Speed);
            Evaluate(Time);
            if (Time >= Duration) Finish();
        }

        void Finish()
        {
            if (HasFinished) return;
            IsPlaying = false;
            HasFinished = true;
            Finished?.Invoke();
        }

        void Prepare()
        {
            if (_prepared) return;
            // she comes out of the phone in the summer outfit and keeps it all night (one model for the whole cut)
            var wardrobe = FindFirstObjectByType<HarinWardrobe>();
            if (wardrobe != null && wardrobe.Summer != null)
            {
                wardrobe.Wear(HarinWardrobe.Outfit.Summer);
                harin = wardrobe.Summer;
            }
            FindReferences();
            if (harin == null || mainCamera == null || phone == null)
                throw new InvalidOperationException("EmergenceSequence: Harin, MainCamera or Phone missing");

            var ht = harin.transform;
            _home = new GameObject("HarinHome").transform;
            _home.SetPositionAndRotation(ht.position, ht.rotation);
            _home.SetParent(ht.parent, true);
            _homeScale = ht.localScale;
            _anchor = new GameObject("HarinCutAnchor").transform;

            _camStartPos = mainCamera.transform.position;
            _camStartRot = mainCamera.transform.rotation;
            _camStartFov = mainCamera.fieldOfView;
            _fit = mainCamera.GetComponent<PhoneCameraFit>();

            // let the room lights (rendering layer 1) light her too once she is in the room
            uint harinMask = 0;
            foreach (var r in harin.GetComponentsInChildren<Renderer>(true))
            {
                harinMask |= r.renderingLayerMask;
                r.renderingLayerMask |= 1u;
            }
            BuildFaceLights(harinMask == 0 ? 1u : harinMask);

            _lamp = FindLight("RoomLamp");
            _window = FindLight("WindowMoonLight");
            _keyLight = FindLight("AppStageKeyLight");
            if (_lamp != null) { _lampColor = _lamp.color; _lampIntensity = _lamp.intensity; }
            if (_window != null) { _windowColor = _window.color; _windowIntensity = _window.intensity; }
            if (_keyLight != null) { _keyColor = _keyLight.color; _keyIntensity = _keyLight.intensity; }

            BuildCut10Rig();
            _prepared = true;
        }

        // the same soft key as the prologue's: from the camera's upper right, lighting only Harin
        void BuildFaceLights(uint harinMask)
        {
            (_faceLight, _catchLight) = FaceLights.Build("RoomFaceLight", "RoomCatchLight", harinMask);
            _faceLight.enabled = false;
            _catchLight.enabled = false;
        }

        /// <summary>The room face light (null before the first play).</summary>
        public Light FaceLight => _faceLight;

        static Light FindLight(string name)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.name == name) return l;
            return null;
        }

        /// <summary>Sets every object for time t. Safe to call repeatedly and in any order.</summary>
        public void Evaluate(float t)
        {
            if (!_prepared) Prepare();
            var ht = harin.transform;

            // glitch: 0 → 1 over cut 9, small while the arm is out, gone by 5 s in the room
            float glitch = t < Cut10At ? t / Cut10At : t < RoomAt ? 1f : Mathf.Clamp01(1f - (t - RoomAt));
            harin.SetGlitch(t < Cut10At ? glitch : t < RoomAt ? Cut10Glitch : glitch);
            if (chat != null) chat.SetGlitch(Mathf.Min(glitch, 1f));
            harin.SetRoomLook(t >= Cut10At, desk: t >= Cut10At && t < RoomAt);
            harin.SetHeadTurn(t < Cut10At ? 35f * Smooth(t / Cut10At) : 0f, 0f);
            harin.SetRightArmReach(null);
            harin.SetEyeTarget(t >= EyesAt ? mainCamera.transform : null);
            // seated and past EyesAt she looks at the player: the head lifts a little more than the pose's own
            harin.EaseHeadTurn(0f, t >= SeatAt && t >= EyesAt ? -(Act2HeadToCamera + Act2HeadLift) : 0f, Act2EaseSeconds);

            bool inPhone = t < Cut10At;
            bool cut10 = t >= Cut10At && t < RoomAt;
            _seatedFraming = t >= SeatAt;
            if (chat != null) chat.SetVisible(inPhone);
            if (_fit != null) _fit.enabled = inPhone;
            mainCamera.enabled = !cut10;
            if (_cut10Cam != null) _cut10Cam.enabled = cut10;
            if (_rig != null) _rig.gameObject.SetActive(cut10);
            SetLights(t);

            if (inPhone)
            {
                ht.localScale = _homeScale;
                harin.MoveTo(_home);
                harin.SetFace("relaxed");
                harin.ApplyPose(Pose.None, 0f);
                mainCamera.transform.SetPositionAndRotation(_camStartPos, _camStartRot);
                if (_fit == null) mainCamera.fieldOfView = _camStartFov;
            }
            else if (cut10)
            {
                ht.localScale = _homeScale;
                PlaceCut10(t);
            }
            else if (t < SeatAt)
            {
                ht.localScale = _homeScale;
                float turn = TurnDeg * Smooth((t - TurnAt) / 0.5f);
                _anchor.SetPositionAndRotation(StandPosition, Quaternion.Euler(0f, StandFacingDeg + turn, 0f));
                harin.MoveTo(_anchor);
                harin.SetFace(t < TurnAt ? "surprised" : "neutral");
                if (t < SitAt) harin.ApplyPose(Pose.LookAtHands, Smooth((t - RoomAt) / (SitAt - RoomAt)));
                else harin.ApplyPose(Pose.SitDown, Smooth((t - SitAt) / (SeatAt - SitAt)));
                PlaceCut11Camera((t - RoomAt) / (SeatAt - RoomAt));
                _eyes = ht.position + Vector3.up * 1.45f * _homeScale.y;
                AimFaceLights();
            }
            else
            {
                ht.localScale = _homeScale;
                harin.MoveTo(roomAnchor);
                harin.SetFace(t < 9f ? "neutral" : "relaxed");
                harin.ApplyPose(Pose.Seated, Smooth((t - SeatAt) / 1f));
                _seatedU = (t - SeatAt) / (Duration - SeatAt);
                if (_eyes == Vector3.zero) _eyes = roomAnchor.position + roomAnchor.rotation * new Vector3(0f, 1.05f, 0.1f);
                PlaceCut12Camera(_seatedU);
            }
        }

        void SetLights(float t)
        {
            bool room = t >= RoomAt;
            bool cut10 = t >= Cut10At && !room;
            if (_faceLight != null) _faceLight.enabled = room;
            if (_catchLight != null) _catchLight.enabled = room;
            if (_keyLight != null)
            {
                // the app-stage fill light turned her skin pink in the room; in cut 10 it plays the desk lamp
                _keyLight.enabled = !room && !cut10; // cut 10 has its own warm light
                _keyLight.color = cut10 ? Mathf.CorrelatedColorTemperatureToRGB(Cut10KeyKelvin) : _keyColor;
                _keyLight.intensity = cut10 ? _keyIntensity * Cut10KeyScale : _keyIntensity;
            }
            if (_lamp != null)
            {
                float k = t < SeatAt ? LampCut11 : Mathf.Lerp(LampCut12, LampLate, Smooth((t - LampDownAt) / 0.5f));
                _lamp.color = room ? Mathf.CorrelatedColorTemperatureToRGB(LampKelvin) : _lampColor;
                _lamp.intensity = room ? _lampIntensity * k : _lampIntensity;
            }
            if (_window != null)
            {
                _window.color = room ? Mathf.CorrelatedColorTemperatureToRGB(WindowKelvin) : _windowColor;
                _window.intensity = room ? _windowIntensity * WindowScale : _windowIntensity;
            }
        }

        // ---------------------------------------------------------------- cut 10

        void BuildCut10Rig()
        {
            _rig = new GameObject("Cut10Rig").transform;
            _rig.position = Cut10DeskOrigin;

            var camGo = new GameObject("Cut10Camera");
            camGo.transform.SetParent(_rig, false);
            camGo.transform.SetPositionAndRotation(Cut10DeskOrigin + Vector3.up * Cut10CamHeight, Quaternion.Euler(Cut10Pitch, 0f, 0f));
            _cut10Cam = camGo.AddComponent<Camera>();
            _cut10Cam.clearFlags = CameraClearFlags.SolidColor;
            _cut10Cam.backgroundColor = Color.black;
            _cut10Cam.nearClipPlane = 0.01f;
            _cut10Cam.farClipPlane = 5f;
            _cut10Cam.fieldOfView = Cut10Fov;
            _cut10Cam.depth = mainCamera.depth + 1f;
            _cut10Cam.cullingMask = ~(1 << 5); // not the UI layer
            _cut10Cam.enabled = false;
            camGo.AddComponent<Cut10CameraFit>().Init(Cut10Fov, ImageW / ImageH);

            // the desk picture on the desk plane: a fine grid whose UVs are each vertex's place in the picture,
            // so it looks exactly like the flat picture from this camera and hides whatever is under the desk
            var desk = new GameObject("Cut10Desk");
            desk.transform.SetParent(_rig, false);
            desk.transform.position = Vector3.zero;
            var mesh = new Mesh { name = "Cut10Desk" };
            var verts = new Vector3[(DeskGridX + 1) * (DeskGridY + 1)];
            var uvs = new Vector2[verts.Length];
            for (int y = 0; y <= DeskGridY; y++)
                for (int x = 0; x <= DeskGridX; x++)
                {
                    var px = new Vector2(x * ImageW / DeskGridX, y * ImageH / DeskGridY);
                    int i = y * (DeskGridX + 1) + x;
                    verts[i] = PixelOnDesk(px);
                    uvs[i] = new Vector2(px.x / ImageW, 1f - px.y / ImageH);
                }
            var tris = new int[DeskGridX * DeskGridY * 6];
            for (int y = 0, k = 0; y < DeskGridY; y++)
                for (int x = 0; x < DeskGridX; x++)
                {
                    int i = y * (DeskGridX + 1) + x;
                    tris[k++] = i; tris[k++] = i + 1; tris[k++] = i + DeskGridX + 1;
                    tris[k++] = i + 1; tris[k++] = i + DeskGridX + 2; tris[k++] = i + DeskGridX + 1;
                }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            desk.AddComponent<MeshFilter>().sharedMesh = mesh;
            var deskMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            deskMat.SetTexture("_BaseMap", deskPicture);
            deskMat.SetColor("_BaseColor", deskPicture != null ? Color.white : new Color(0.2f, 0.14f, 0.1f));
            deskMat.SetFloat("_Cull", 0f);
            var deskRenderer = desk.AddComponent<MeshRenderer>();
            deskRenderer.sharedMaterial = deskMat;
            deskRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // phone screen corners on the plane, and the noise over them
            _screenCorners = new Vector3[4];
            for (int i = 0; i < 4; i++) _screenCorners[i] = PixelOnDesk(ScreenPx[i]);
            _cross = (_screenCorners[0] + _screenCorners[1] + _screenCorners[2] + _screenCorners[3]) * 0.25f;
            var towards = PixelOnDesk(HandTowardsPx) - _cross;
            towards.y = 0f;
            _reachDir = towards.normalized;

            var lift = Vector3.up * 0.001f;
            var noise = new GameObject("Cut10ScreenNoise");
            noise.transform.SetParent(_rig, false);
            noise.transform.position = Vector3.zero;
            var nm = new Mesh { name = "Cut10ScreenNoise" };
            // TL, TR, BR, BL: the picture's top edge is the phone's top
            nm.vertices = new[] { _screenCorners[0] + lift, _screenCorners[1] + lift, _screenCorners[2] + lift, _screenCorners[3] + lift };
            nm.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            nm.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            nm.RecalculateBounds();
            noise.AddComponent<MeshFilter>().sharedMesh = nm;
            _noiseMat = FxMaterial(screenNoise, true);
            _noiseMat.renderQueue = 3000;
            noise.AddComponent<MeshRenderer>().sharedMaterial = _noiseMat;

            var soft = SoftDot();
            _glow = FxQuad("Cut10Glow", soft, true, 3001, out _glowMat);
            _glow.position = _cross + Vector3.up * 0.0015f;
            _glow.localScale = new Vector3(GlowRadius * 2f, GlowRadius * 1.6f, 1f);
            _glow.rotation = Quaternion.LookRotation(Vector3.down, _reachDir);

            // the ring texture is a flat ellipse (about 0.88 by 0.2 of the image); stretch it back into a circle
            // lying on the desk plane, so the arm hides its far half and its near half covers the arm
            _ring = FxQuad("Cut10Ring", portalRing, true, 3003, out _ringMat);
            _ringMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _ring.position = _cross + Vector3.up * 0.002f;
            _ring.localScale = new Vector3(RingRadius * 2f / 0.88f, RingRadius * 2f / 0.2f, 1f);
            _ring.rotation = Quaternion.LookRotation(Vector3.down, _reachDir) * Quaternion.Euler(0f, 0f, 90f);

            // wrist swirl: a pivot that turns about the desk normal, with the picture stretched into a circle on it
            _swirlPivot = new GameObject("Cut10SwirlPivot").transform;
            _swirlPivot.SetParent(_rig, true);
            _swirlPivot.position = _cross + Vector3.up * 0.0025f;
            var swirl = FxQuad("Cut10Swirl", wristSwirl, true, 3004, out _swirlMat);
            swirl.SetParent(_swirlPivot, true);
            swirl.localPosition = Vector3.zero;
            swirl.rotation = Quaternion.LookRotation(Vector3.down, _reachDir);
            swirl.localScale = new Vector3(SwirlRadius * 2f / SwirlEllipseW, SwirlRadius * 2f / SwirlEllipseH, 1f);

            var warm = new GameObject("Cut10WarmLight");
            warm.transform.SetParent(_rig, false);
            warm.transform.rotation = Quaternion.LookRotation(Cut10WarmDir);
            var wl = warm.AddComponent<Light>();
            wl.type = LightType.Directional;
            wl.color = Mathf.CorrelatedColorTemperatureToRGB(Cut10WarmKelvin);
            wl.intensity = Cut10WarmIntensity;
            wl.shadows = LightShadows.None;
            var wd = warm.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            wd.renderingLayers = uint.MaxValue;

            _veil = FxQuad("Cut10WristVeil", SoftDisc(0.4f), false, 3005, out _veilMat);
            _veilMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            _veil.SetPositionAndRotation(_cross - _reachDir * VeilBack + Vector3.up * 0.003f, Quaternion.LookRotation(Vector3.down, _reachDir));
            _veil.localScale = new Vector3(VeilRadius * 2f, VeilRadius * 2f, 1f);

            var portal = new GameObject("Cut10PortalLight");
            portal.transform.SetParent(_rig, false);
            portal.transform.position = _cross - Vector3.up * PortalLightDepth;
            _portalLight = portal.AddComponent<Light>();
            _portalLight.type = LightType.Point;
            _portalLight.color = PortalLightColor;
            _portalLight.range = PortalLightRange;
            _portalLight.intensity = 0f;
            _portalLight.shadows = LightShadows.None;
            portal.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().renderingLayers = uint.MaxValue;

            var blob = SoftDisc(0.35f);
            _shadow = FxQuad("Cut10ContactShadow", blob, false, 3002, out _shadowMat);
            _shadowCore = FxQuad("Cut10ContactShadowCore", blob, false, 3002, out _shadowCoreMat);
            _rig.gameObject.SetActive(false);
        }

        Transform FxQuad(string name, Texture tex, bool additive, int queue, out Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_rig, true);
            mat = FxMaterial(tex, additive);
            mat.renderQueue = queue;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Material FxMaterial(Texture tex, bool additive)
        {
            var shader = Shader.Find("Oddity/CutFx");
            var m = new Material(shader);
            m.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
            return m;
        }

        // round spot, alpha 1 out to flat of the radius, then a smooth fall to 0 at the edge, made in code
        static Texture2D SoftDisc(float flat)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDisc" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Smooth((1f - Mathf.Sqrt(dx * dx + dy * dy)) / (1f - flat));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // round soft spot (white, alpha 1 in the middle → 0 at the edge), made in code
        static Texture2D SoftDot()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // A pixel of the cut 10 picture as a point on the desk plane (ray from the cut 10 camera).
        Vector3 PixelOnDesk(Vector2 px)
        {
            var camPos = Cut10DeskOrigin + Vector3.up * Cut10CamHeight;
            var rot = Quaternion.Euler(Cut10Pitch, 0f, 0f);
            float halfH = Mathf.Tan(Cut10Fov * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * ImageW / ImageH;
            float x = (px.x / ImageW * 2f - 1f) * halfW;
            float y = (1f - px.y / ImageH * 2f) * halfH;
            var dir = rot * new Vector3(x, y, 1f);
            float k = (Cut10DeskOrigin.y - camPos.y) / dir.y;
            return camPos + dir * k;
        }

        // Timeline (seconds):
        //   0.5 the screen starts to churn; 1.0 ring, finger tips 2 cm out; 1.5 fingers 6 cm out;
        //   2.0 the wrist passes; 2.5 the hand goes over the phone edge; 3.0 finger tips on the desk;
        //   3.5 fingers bend a little; 3.8 → 4.0 the ring flares
        void PlaceCut10(float t)
        {
            float s = _homeScale.x;
            float hl = harin.HandLength * s, lf = harin.ForearmLength * s, la = harin.UpperArmLength * s;

            float sinA = Mathf.Clamp(WristAbove / WristOut, 0f, 0.99f);
            float alpha = Mathf.Asin(sinA);
            var axis = (_reachDir * Mathf.Cos(alpha) + Vector3.up * sinA).normalized; // forearm through the screen

            // tip distance beyond the crossing point while the hand is still straight
            float tip = t < 1f ? Mathf.Lerp(-0.04f, 0.02f, (t - Cut10At) / 0.5f)
                : t < 1.5f ? Mathf.Lerp(0.02f, 0.06f, (t - 1f) / 0.5f)
                : t < 2f ? Mathf.Lerp(0.06f, hl, Smooth((t - 1.5f) / 0.5f))
                : hl;
            float wristOut = t < 2f ? tip - hl : Mathf.Lerp(0f, WristOut, Smooth((t - 2f) / 1f));
            float bend = Smooth((t - 2f) / 1f);
            // a resting hand from the start, a little more once it takes her weight on the desk
            float curl = 0.6f + 0.4f * bend + 0.25f * Smooth((t - 3f) / 0.5f);

            var wrist = _cross + axis * wristOut;
            var elbow = wrist - axis * lf;
            float beta = alpha + UpperArmSteeper * Mathf.Deg2Rad;
            var upperDir = (_reachDir * Mathf.Cos(beta) + Vector3.up * Mathf.Sin(beta)).normalized;
            var shoulder = elbow - upperDir * la;

            // hand: straight along the forearm, then hanging over to the desk
            float hW = wrist.y - DeskPlaneY;
            var down = (_reachDir * Mathf.Sqrt(Mathf.Max(0f, hl * hl - hW * hW)) - Vector3.up * Mathf.Max(0f, hW)).normalized;
            var handDir = Vector3.Slerp(axis, down, bend);
            var tipPos = wrist + handDir * hl;

            // her body hangs under the desk: her up axis along the upper arm, her sides level
            // (nearly level, so hair and feet both stay under the desk plane)
            var bodyUp = (_reachDir * Mathf.Cos(BodyTiltDeg * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(BodyTiltDeg * Mathf.Deg2Rad)).normalized;
            var side = Vector3.Cross(bodyUp, Vector3.up).normalized;
            var front = Vector3.Cross(side, bodyUp).normalized;
            var rot = Quaternion.LookRotation(front, bodyUp);
            var root = shoulder - rot * Vector3.Scale(harin.RightShoulderLocal, _homeScale);
            _anchor.SetPositionAndRotation(root, rot);
            harin.MoveTo(_anchor);
            harin.SetFace("relaxed");
            harin.SetReachTargets(elbow, wrist, tipPos, Vector3.down, curl, t >= 2f ? DeskPlaneY : float.NegativeInfinity);
            harin.ApplyPose(Pose.Cut10Reach, bend);

            // effects
            float t0 = (t - Cut10At) / (RoomAt - Cut10At);
            float churn = Mathf.Lerp(0.35f, 0.9f, Smooth((t - Cut10At) / 0.5f));
            _noiseMat.SetColor("_Color", new Color(1.3f, 0.9f, 1.8f, churn));
            float tiling = Mathf.Lerp(1f, 0.7f, Smooth((t - 1.5f) / 1f)); // pulls in towards the wrist
            _noiseMat.mainTextureScale = new Vector2(tiling, tiling);
            _noiseMat.mainTextureOffset = new Vector2(Mathf.Repeat(t0 * 7.3f, 1f - tiling), Mathf.Repeat(t0 * 3.1f, 1f - tiling) * 0.5f);

            float flare = Smooth((t - 3.8f) / 0.2f);
            float ringA = Smooth((t - 1f) / 0.2f);
            _ringMat.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(ringA * (0.85f + flare))));
            float ringGrow = 1f + 0.25f * flare;
            _ring.localScale = new Vector3(RingRadius * 2f / 0.88f * ringGrow, RingRadius * 2f / 0.2f * ringGrow, 1f);
            _glowMat.SetColor("_Color", new Color(0.6f, 0.35f, 1f, 0.25f * ringA + 0.3f * flare));
            _portalLight.intensity = PortalLightIntensity * (ringA + flare);
            _veilMat.SetColor("_Color", new Color(VeilColor.r, VeilColor.g, VeilColor.b, VeilAlpha * ringA));

            // wrist light: turns 20 → 40 °/s from 1.0 s, brightest at 2.5 s (the wrist coming out), then eases to 40%
            float st = Mathf.Max(0f, t - SwirlFrom), sd = RoomAt - SwirlFrom;
            float spin = SwirlSpeed0 * st + (SwirlSpeed1 - SwirlSpeed0) / sd * st * st * 0.5f;
            _swirlPivot.rotation = Quaternion.AngleAxis(spin, Vector3.up);
            float swirlA = Smooth((t - SwirlFrom) / (SwirlPeakAt - SwirlFrom))
                * Mathf.Lerp(1f, 0.4f, Smooth((t - SwirlPeakAt) / (RoomAt - SwirlPeakAt)));
            _swirlMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.9f * swirlA));

            // contact shadow along the hand, from under the wrist to the finger tips
            float touch = 1f - Mathf.Clamp01((tipPos.y - DeskPlaneY) / 0.02f);
            float near = 1f - Mathf.Clamp01((tipPos.y - DeskPlaneY) / 0.08f);
            var lampShift = new Vector3(Cut10WarmDir.x, 0f, Cut10WarmDir.z).normalized * ShadowLampShift;
            var w0 = new Vector3(wrist.x, DeskPlaneY, wrist.z);
            var w1 = new Vector3(tipPos.x, DeskPlaneY, tipPos.z);
            float len = Mathf.Max(0.03f, Vector3.Distance(w0, w1));
            var along = len > 1e-4f ? (w1 - w0).normalized : _reachDir;
            var look = Quaternion.LookRotation(Vector3.down, along);
            _shadow.SetPositionAndRotation((w0 + w1) * 0.5f + lampShift + Vector3.up * 0.0012f, look);
            _shadow.localScale = new Vector3(ShadowWidth, len * 1.35f, 1f);
            _shadowMat.SetColor("_Color", new Color(0.02f, 0.01f, 0.03f, Mathf.Clamp01((ShadowNear * near + ShadowTouch * touch) * Mathf.Max(bend, 0.3f))));
            _shadowCore.SetPositionAndRotation(Vector3.Lerp(w0, w1, 0.6f) + lampShift * 0.5f + Vector3.up * 0.0014f, look);
            _shadowCore.localScale = new Vector3(ShadowCoreWidth, len * 0.9f, 1f);
            _shadowCoreMat.SetColor("_Color", new Color(0.02f, 0.01f, 0.02f, ShadowCoreTouch * touch * bend));
        }

        // ---------------------------------------------------------------- cuts 11 and 12

        void PlaceCut11Camera(float u)
        {
            u = Mathf.Clamp01(u);
            var dir = Quaternion.Euler(0f, StandFacingDeg, 0f) * Vector3.forward;
            float d = Mathf.Lerp(Cut11Dist0, Cut11Dist1, Smooth(u));
            var pos = StandPosition + dir * d;
            pos.y = Cut11CamHeight;
            var look = StandPosition + Vector3.up * Cut11LookHeight;
            mainCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            mainCamera.fieldOfView = Mathf.Lerp(Cut11Fov0, Cut11Fov1, Smooth(u));
        }

        // After every LateUpdate of hers (she runs at -100): from cut 12 on, through act 2, measure her posed
        // eyes and waist and frame her. Measured in her model space and put back on the seat anchor, so the
        // glitch jitter shows on screen instead of being followed by the camera.
        void LateUpdate()
        {
            if (!_prepared || !_seatedFraming || !(IsPlaying || HasFinished)) return;
            var ht = harin.transform;
            var head = harin.BonePosition(HumanBodyBones.Head);
            if (head == Vector3.zero) return;
            var eyesWorld = head + Vector3.up * 0.075f * ht.lossyScale.y;
            _eyes = OnSeat(ht.InverseTransformPoint(eyesWorld));
            var spine = harin.BonePosition(HumanBodyBones.Spine);
            _waistY = spine != Vector3.zero ? OnSeat(ht.InverseTransformPoint(spine)).y : _eyes.y - 0.5f;
            PlaceCut12Camera(_seatedU);
            AimFaceLights();
        }

        float _waistY = float.NaN;

        Vector3 OnSeat(Vector3 local) => roomAnchor.position + roomAnchor.rotation * Vector3.Scale(local, _homeScale);

        /// <summary>
        /// Seated framing: camera <see cref="Act2EyeDrop"/> under her eyes at horizontal distance d, tilted so
        /// her eyes sit <see cref="Act2EyeLine"/> of the way down the frame (same idea as the prologue's face shots).
        /// </summary>
        public static UnityEngine.Pose SeatedCameraPose(Vector3 eyes, Vector3 dir, float d, float fov)
        {
            var pos = eyes + dir * d - Vector3.up * Act2EyeDrop;
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float above = Mathf.Atan((1f - 2f * Act2EyeLine) * tanHalf) * Mathf.Rad2Deg; // eyes above the optical axis
            float rise = Mathf.Atan2(Act2EyeDrop, d) * Mathf.Rad2Deg;                    // eyes above the camera
            var yaw = Quaternion.LookRotation(-dir, Vector3.up);
            return new UnityEngine.Pose(pos, yaw * Quaternion.Euler(above - rise, 0f, 0f));
        }

        /// <summary>Camera distance at which waist to head top fills <see cref="Act2BodyShare"/> of the frame.</summary>
        public static float SeatedDistance(float eyeY, float waistY, float headTopAboveEye, float fov)
        {
            float span = Mathf.Max(0.2f, eyeY + headTopAboveEye - waistY);
            float frame = span / Act2BodyShare;
            return frame * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        }

        void PlaceCut12Camera(float u)
        {
            u = Mathf.Clamp01(u);
            var dir = Quaternion.Euler(0f, -Cut12SideDeg, 0f) * roomAnchor.forward; // from her front, 20° to the side
            dir.y = 0f;
            dir.Normalize();
            float fov = Mathf.Lerp(Cut12Fov0, Cut12Fov1, Smooth(u));
            float waist = float.IsNaN(_waistY) ? _eyes.y - 0.5f * _homeScale.y : _waistY;
            float end = SeatedDistance(_eyes.y, waist, Act2HeadTopAboveEye * _homeScale.y, Cut12Fov1);
            float d = end * Act2Closer * Mathf.Lerp(Cut12PullFrom, 1f, Smooth(u));
            var pose = SeatedCameraPose(_eyes, dir, d, fov);
            mainCamera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            mainCamera.fieldOfView = fov;
        }

        void AimFaceLights()
        {
            if (_faceLight == null) return;
            var ct = mainCamera.transform;
            var p = ct.position + ct.right * 0.35f + Vector3.up * 0.4f;
            _faceLight.transform.SetPositionAndRotation(p, Quaternion.LookRotation(_eyes - p));
            float k = Vector3.Distance(p, _eyes) / FaceLightRefDistance;
            _faceLight.intensity = FaceLightIntensity * Mathf.Clamp(k * k, 1f, FaceLightMaxBoost);
            var toCam = ct.position - _eyes;
            _catchLight.transform.position = _eyes + toCam.normalized * Mathf.Min(0.35f, toCam.magnitude * 0.6f) + Vector3.up * 0.05f;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }

    /// <summary>Keeps the cut 10 picture filling the screen: narrows the field of view on screens wider than the picture.</summary>
    [RequireComponent(typeof(Camera))]
    public class Cut10CameraFit : MonoBehaviour
    {
        float _fov = 38f;
        float _imageAspect = 0.5626f;
        Camera _cam;

        public void Init(float fov, float imageAspect)
        {
            _fov = fov;
            _imageAspect = imageAspect;
            _cam = GetComponent<Camera>();
            Fit();
        }

        void OnPreCull() => Fit();
        void LateUpdate() => Fit();

        void Fit()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            float half = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            if (_cam.aspect > _imageAspect) half *= _imageAspect / _cam.aspect;
            _cam.fieldOfView = 2f * Mathf.Atan(half) * Mathf.Rad2Deg;
        }
    }
}
