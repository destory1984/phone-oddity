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
        static readonly Vector2 HandTowardsPx = new Vector2(1250, 2550); // the hand reaches towards here
        const int DeskGridX = 24, DeskGridY = 40;
        const float WristAbove = 0.045f;     // wrist height over the screen once out
        const float WristOut = 0.064f;       // wrist distance from the crossing point along the forearm (45° rise)
        const float UpperArmSteeper = 25f;   // upper arm angle over the forearm angle (under the desk)
        const float RingRadius = 0.05f;
        const float GlowRadius = 0.13f;
        const float ShadowSize = 0.05f;
        const float Cut10Glitch = 0.2f;      // body jitter while the arm is out (1 cm at 1)

        // ---------------------------------------------------------------- cut 11 and 12 (the room)
        static readonly Vector3 StandPosition = new Vector3(-0.6f, 0f, -0.2f);
        const float StandFacingDeg = 55f;    // yaw of her front and of the camera direction (0 = +Z)
        const float TurnDeg = 15f;
        const float Cut11CamHeight = 1.25f, Cut11Dist0 = 2.6f, Cut11Dist1 = 3f, Cut11Fov0 = 38f, Cut11Fov1 = 42f;
        const float Cut11LookHeight = 0.8f;
        const float SeatInFromEdge = 0.15f;  // hip contact this far in from the mattress front edge
        const float SeatZ = -0.55f;
        const float Cut12CamHeight = 1.05f, Cut12Dist0 = 2.4f, Cut12Dist1 = 2.8f, Cut12Fov0 = 35f, Cut12Fov1 = 40f;
        const float Cut12SideDeg = 20f;
        const float Cut12LookAboveSeat = 0.1f;
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
        Material _noiseMat, _ringMat, _glowMat, _shadowMat;
        Transform _ring, _glow, _shadow;

        Light _lamp, _window, _keyLight;
        Color _lampColor, _windowColor, _keyColor;
        float _lampIntensity, _windowIntensity, _keyIntensity;

        void Awake() => FindReferences();

        /// <summary>Art for cut 10 (GameBootstrap passes its serialized textures).</summary>
        public void SetArt(Texture2D desk, Texture2D noise, Texture2D ring)
        {
            if (desk != null) deskPicture = desk;
            if (noise != null) screenNoise = noise;
            if (ring != null) portalRing = ring;
        }

        void FindReferences()
        {
            if (harin == null) harin = FindFirstObjectByType<HarinPuppet>();
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
            foreach (var r in harin.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= 1u;

            _lamp = FindLight("RoomLamp");
            _window = FindLight("WindowMoonLight");
            _keyLight = FindLight("AppStageKeyLight");
            if (_lamp != null) { _lampColor = _lamp.color; _lampIntensity = _lamp.intensity; }
            if (_window != null) { _windowColor = _window.color; _windowIntensity = _window.intensity; }
            if (_keyLight != null) { _keyColor = _keyLight.color; _keyIntensity = _keyLight.intensity; }

            BuildCut10Rig();
            _prepared = true;
        }

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
            harin.SetRoomLook(t >= Cut10At);
            harin.SetHeadTurn(t < Cut10At ? 35f * Smooth(t / Cut10At) : 0f, 0f);
            harin.SetRightArmReach(null);
            harin.SetEyeTarget(t >= EyesAt ? mainCamera.transform : null);

            bool inPhone = t < Cut10At;
            bool cut10 = t >= Cut10At && t < RoomAt;
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
            }
            else
            {
                ht.localScale = _homeScale;
                harin.MoveTo(roomAnchor);
                harin.SetFace(t < 9f ? "neutral" : "relaxed");
                harin.ApplyPose(Pose.Seated, Smooth((t - SeatAt) / 1f));
                PlaceCut12Camera((t - SeatAt) / (Duration - SeatAt));
            }
        }

        void SetLights(float t)
        {
            bool room = t >= RoomAt;
            bool cut10 = t >= Cut10At && !room;
            if (_keyLight != null)
            {
                // the app-stage fill light turned her skin pink in the room; in cut 10 it plays the desk lamp
                _keyLight.enabled = !room;
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
            _ring.position = _cross + Vector3.up * 0.002f;
            _ring.localScale = new Vector3(RingRadius * 2f / 0.88f, RingRadius * 2f / 0.2f, 1f);
            _ring.rotation = Quaternion.LookRotation(Vector3.down, _reachDir) * Quaternion.Euler(0f, 0f, 90f);

            _shadow = FxQuad("Cut10ContactShadow", soft, false, 3002, out _shadowMat);
            _shadow.localScale = new Vector3(ShadowSize, ShadowSize * 0.7f, 1f);
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
            float curl = Smooth((t - 3f) / 0.5f) * 0.5f;

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
            var side = Vector3.Cross(upperDir, Vector3.up).normalized;
            var front = Vector3.Cross(side, upperDir).normalized;
            var rot = Quaternion.LookRotation(front, upperDir);
            var root = shoulder - rot * Vector3.Scale(harin.RightShoulderLocal, _homeScale);
            _anchor.SetPositionAndRotation(root, rot);
            harin.MoveTo(_anchor);
            harin.SetFace("relaxed");
            harin.SetReachTargets(elbow, wrist, tipPos, Vector3.down, curl);
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

            float touch = 1f - Mathf.Clamp01((tipPos.y - DeskPlaneY) / 0.02f);
            var shadowAt = tipPos - _reachDir * 0.02f;
            _shadow.position = new Vector3(shadowAt.x, DeskPlaneY + 0.0012f, shadowAt.z);
            _shadow.rotation = Quaternion.LookRotation(Vector3.down, _reachDir);
            _shadowMat.SetColor("_Color", new Color(0f, 0f, 0.02f, 0.55f * touch * bend));
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

        void PlaceCut12Camera(float u)
        {
            u = Mathf.Clamp01(u);
            var seat = roomAnchor.position + roomAnchor.rotation * Vector3.Scale(harin.SeatContactLocal, _homeScale);
            var look = seat + Vector3.up * Cut12LookAboveSeat;
            var dir = Quaternion.Euler(0f, -Cut12SideDeg, 0f) * roomAnchor.forward; // from her front, 20° to the side
            float d = Mathf.Lerp(Cut12Dist0, Cut12Dist1, Smooth(u));
            var pos = look + dir * d;
            pos.y = Cut12CamHeight;
            mainCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            mainCamera.fieldOfView = Mathf.Lerp(Cut12Fov0, Cut12Fov1, Smooth(u));
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
