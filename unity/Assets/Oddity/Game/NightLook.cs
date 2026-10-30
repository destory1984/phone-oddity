using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Oddity.Game
{
    /// <summary>
    /// The night look of the conti scenes (task 50, shots/plans/reference_her_night.md): a very dark blue-black room with a few
    /// blurred bokeh lights behind her, one soft screen-coloured key light on her face from the side the camera stands on,
    /// a faint warm rim light on her hair, a vignette on the window, and a dark chat below it.
    /// It is on while a conti scene (<see cref="SceneConti.IsContiScene"/>) plays in the phone with the conti on
    /// (<see cref="SceneConti.Level"/> above 0) and <see cref="SceneConti.NightOn"/>; leaving the scene puts back
    /// every value it changed (camera background, ambient light, the key light, the chat colours). Nothing else changes.
    /// </summary>
    [DefaultExecutionOrder(200)] // after AppStageDirector has put the camera in place
    public class NightLook : MonoBehaviour
    {
        // ---------------------------------------------------------------- the numbers

        // background (window): a dark blue-black gradient, top to bottom
        public static readonly Color BackTop = new Color32(0x0E, 0x14, 0x20, 255);
        public static readonly Color BackBottom = new Color32(0x1A, 0x22, 0x30, 255);
        // bokeh lights drawn on the backdrop texture (added to the gradient)
        public static readonly Color BokehAmber = new Color(1.00f, 0.62f, 0.26f);
        public static readonly Color BokehBlue = new Color(0.34f, 0.52f, 1.00f);
        public const int BokehAmberCount = 8, BokehBlueCount = 5;
        public const float BokehMinAlpha = 0.10f, BokehMaxAlpha = 0.30f;   // how bright a light gets over the gradient
        public const float BokehMinRadius = 0.07f, BokehMaxRadius = 0.22f; // m on the backdrop
        public const float BokehSoftness = 0.30f;                          // 0 = hard edge, 1 = a cone
        public const int BokehSeed = 20261031;
        public const int BackdropTexture = 512;
        public const float BackdropTileMeters = 1.6f, BackdropSizeMeters = 9.6f;
        public const float BackdropBehindMeters = 1.1f;                    // behind her; the camera yaws up to 40 degrees, so it is big

        // light: everything lower, a phone-screen key (bluish white) from the camera side, a faint warm rim from behind
        public static readonly Color AmbientNight = new Color(0.03f, 0.04f, 0.07f);
        // the stage's own key light (a directional light) stays on as a faint fill from the same side as the face light
        public static readonly Color FillColor = new Color(0.60f, 0.70f, 0.95f);
        public const float FillIntensity = 0.10f;
        // the face light: a soft spot where the phone is (left of the camera, above it), a bluish white like a screen
        public static readonly Color FaceColor = new Color(0.84f, 0.92f, 1.00f);
        public const float FaceIntensity = 0.42f;
        public const float FaceAngle = 44f, FaceInnerAngle = 14f;     // the cone; the shoulders and the hair edge fall into the dark
        public const float FaceDistance = 0.9f;                       // m from her eyes
        public const float FaceYawFromCamera = 25f;                   // to the left of the camera ...
        public const float FacePitchFromCamera = 25f;                 // ... and above it
        // the rim light: a faint warm spot behind her on the camera's right side, above
        public static readonly Color RimColor = new Color(1.00f, 0.70f, 0.38f);
        public const float RimIntensity = 0.22f;
        public const float RimAngle = 80f, RimInnerAngle = 30f;
        public const float RimDistance = 0.7f;
        public const float RimYawFromCamera = 55f;
        public const float RimPitchFromCamera = 30f;
        public const uint HairLayerBit = 1u << 6;     // a lighting layer only the hair draws on; the rim light uses only this one

        // her hair and skin at night (task 52): the hair must not outshine the face, the skin must not read pink
        public const float HairMatcapScale = 0.0f;   // times the day matcap colour (the teal glints on the crown)
        public const float HairRimScale = 0.20f;      // times the day rim colour
        public const float HairDim = 0.45f;           // times the hair's base colour
        public const float SkinNeutralMix = 0.8f;    // how far the skin colours go to the room look's neutral ones
        public const float BlushScale = 0.40f;        // times the shy blush (it still shows, only softer)

        // vignette over the window: black, clear in the middle
        public const float VignetteStrength = 0.90f;  // alpha in the four corners
        public const float VignetteStart = 0.40f;     // distance from the middle (0 middle, 1 corner) where it starts
        public const int VignetteTexture = 128;

        // chat below the window
        public static readonly Color ChatBack = new Color32(0x0C, 0x10, 0x1A, 255);
        public static readonly Color ChatBottom = new Color(0.05f, 0.07f, 0.11f, 0.95f);
        public static readonly Color HarinBubble = new Color32(0x2B, 0x38, 0x4C, 255);
        public static readonly Color HarinText = new Color32(0xEE, 0xF2, 0xF8, 255);
        public static readonly Color PlayerBubble = new Color32(0xC9, 0x9A, 0x4E, 255);
        public static readonly Color PlayerText = new Color32(0x1C, 0x13, 0x08, 255);
        public static readonly Color SystemBubble = new Color(0.17f, 0.21f, 0.29f, 0.85f);
        public static readonly Color SystemText = new Color32(0xB9, 0xC3, 0xD6, 255);
        public static readonly Color TypingDots = new Color32(0xA7, 0xB4, 0xCC, 255);
        public static readonly Color WaitingText = new Color32(0xC4, 0xCC, 0xDC, 255);
        public static readonly Color ChoiceButton = new Color32(0x3B, 0x47, 0x6E, 255);
        public static readonly Color FeelingButton = new Color32(0x56, 0x4B, 0x7C, 255);

        // ---------------------------------------------------------------- state

        GameBootstrap _boot;
        ChatView _chat;
        Camera _cam;
        Light _key, _face, _rim;
        GameObject _backdrop;
        Material _backMat;
        Texture2D _backTex;

        // what was there before (put back on leaving)
        Color _camBackground;
        CameraClearFlags _camClear;
        UnityEngine.Rendering.AmbientMode _ambientMode;
        Color _ambientLight;
        bool _keyEnabled;
        Color _keyColor;
        float _keyIntensity;
        Quaternion _keyRotation;
        bool _saved;
        HarinPuppet _dressed;   // the puppet whose materials the look changed
        uint _rimLayers;

        /// <summary>True while the night look is applied.</summary>
        public bool IsOn { get; private set; }
        /// <summary>The backdrop with the bokeh lights (null until the look was on once).</summary>
        public GameObject Backdrop => _backdrop;
        public Light KeyLight => _key;
        public Light FaceLight => _face;
        public Light RimLight => _rim;
        /// <summary>The lighting layers the rim light uses now (only the hair's while the hair was found), and the ones it has off.</summary>
        public uint RimLightLayers => _rim != null ? _rim.GetUniversalAdditionalLightData().renderingLayers : 0u;
        public uint RimDayLayers => _rimLayers;

        public void Init(GameBootstrap boot, ChatView chat)
        {
            _boot = boot;
            _chat = chat;
        }

        /// <summary>True when the look should be on right now.</summary>
        public bool Wanted =>
            _boot != null && SceneConti.NightOn && SceneConti.Level > 0 && SceneConti.IsContiScene(_boot.CurrentSceneId)
            && !_boot.InPrologue && !_boot.InRoom && _boot.Stage != null && _boot.Stage.Enabled && _boot.Stage.Camera != null;

        void Update() => Refresh();

        /// <summary>Turns the look on or off to match <see cref="Wanted"/>.</summary>
        public void Refresh()
        {
            bool want = Wanted;
            if (want && !IsOn) TurnOn();
            else if (!want && IsOn) TurnOff();
        }

        void OnDisable() { if (IsOn) TurnOff(); }

        void OnDestroy()
        {
            if (_backTex != null) Destroy(_backTex);
            if (_backMat != null) Destroy(_backMat);
        }

        // ---------------------------------------------------------------- on and off

        void TurnOn()
        {
            _cam = _boot.Stage.Camera;
            _key = _key != null ? _key : FindLight("AppStageKeyLight");
            _camBackground = _cam.backgroundColor;
            _camClear = _cam.clearFlags;
            _ambientMode = RenderSettings.ambientMode;
            _ambientLight = RenderSettings.ambientLight;
            if (_key != null)
            {
                _keyEnabled = _key.enabled;
                _keyColor = _key.color;
                _keyIntensity = _key.intensity;
                _keyRotation = _key.transform.rotation;
            }
            _saved = true;

            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BackTop;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientNight;
            if (_key != null)
            {
                _key.enabled = true;
                _key.color = FillColor;
                _key.intensity = FillIntensity;
            }
            EnsureLights();
            _face.enabled = true;
            _rim.enabled = true;
            EnsureBackdrop();
            _backdrop.SetActive(true);
            if (_chat != null) _chat.SetNight(true);
            DressHarin();
            IsOn = true;
            Aim();
        }

        void TurnOff()
        {
            IsOn = false;
            if (_saved)
            {
                if (_cam != null)
                {
                    _cam.clearFlags = _camClear;
                    _cam.backgroundColor = _camBackground;
                }
                RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientLight = _ambientLight;
                if (_key != null)
                {
                    _key.enabled = _keyEnabled;
                    _key.color = _keyColor;
                    _key.intensity = _keyIntensity;
                    _key.transform.rotation = _keyRotation;
                }
            }
            _saved = false;
            if (_face != null) _face.enabled = false;
            if (_rim != null) _rim.enabled = false;
            if (_backdrop != null) _backdrop.SetActive(false);
            if (_chat != null) _chat.SetNight(false);
            UndressHarin();
        }

        // Hair and skin materials, the shy blush, and the rim light that may only touch the hair. With no HAIR material found the
        // hair and the rim are left as they were (the rim then reaches the face as before).
        void DressHarin()
        {
            var harin = _boot != null ? _boot.Harin : null;
            if (harin == null) return;
            _dressed = harin;
            harin.SetNightMaterials(true, HairMatcapScale, HairRimScale, HairDim, SkinNeutralMix);
            harin.BlushScale = BlushScale;
            if (harin.NightHairMaterials > 0 && _rim != null && harin.SetNightHairLayer(true, HairLayerBit) > 0)
                _rim.GetUniversalAdditionalLightData().renderingLayers = HairLayerBit;
        }

        void UndressHarin()
        {
            if (_dressed != null)
            {
                _dressed.SetNightMaterials(false, 1f, 1f, 1f, 0f);
                _dressed.BlushScale = 1f;
                _dressed.SetNightHairLayer(false, 0u);
            }
            _dressed = null;
            if (_rim != null && _rimLayers != 0u) _rim.GetUniversalAdditionalLightData().renderingLayers = _rimLayers;
        }

        void LateUpdate()
        {
            if (!IsOn) return;
            // another outfit is another model: the old one is put back, the new one dressed
            if (_boot != null && _boot.Harin != _dressed) { UndressHarin(); DressHarin(); }
            Aim();
        }

        // The key stands where the phone is (the camera): left of it and above. The rim comes from behind her, camera right.
        void Aim()
        {
            if (_cam == null) return;
            var cr = _cam.transform.rotation;
            var eyes = _boot != null && _boot.Stage != null ? _boot.Stage.AnchorEyes : _cam.transform.position + _cam.transform.forward;
            if (_key != null) _key.transform.rotation = cr * LightForward(-FaceYawFromCamera, FacePitchFromCamera, true);
            PlaceSpot(_face, cr, eyes, -FaceYawFromCamera, FacePitchFromCamera, true, FaceDistance);
            PlaceSpot(_rim, cr, eyes, RimYawFromCamera, RimPitchFromCamera, false, RimDistance);
            if (_backdrop != null && _boot != null)
            {
                var harin = _boot.Harin;
                if (harin != null)
                {
                    var face = harin.transform.forward;
                    face.y = 0f;
                    face = face.sqrMagnitude < 1e-6f ? Vector3.forward : face.normalized;
                    var head = harin.BonePosition(HumanBodyBones.Head);
                    var centre = (head == Vector3.zero ? harin.transform.position + Vector3.up * 1.4f : head) - face * BackdropBehindMeters;
                    // a Quad shows its front towards -Z, so its +Z points away from the camera (behind her)
                    _backdrop.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(-face, Vector3.up));
                }
            }
        }

        // From her to a light that sits `right` degrees to the right of the camera's line to her (negative: left) and `up` degrees
        // above it, in front of her (the camera's side) when `front`, behind her otherwise. Camera space: +x right, +y up, +z away from the camera.
        static Vector3 ToLight(float right, float up, bool front)
        {
            float r = right * Mathf.Deg2Rad, u = up * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r) * Mathf.Cos(u), Mathf.Sin(u), (front ? -1f : 1f) * Mathf.Cos(r) * Mathf.Cos(u));
        }

        // The rotation of a directional light that comes from that side.
        static Quaternion LightForward(float right, float up, bool front) => Quaternion.LookRotation(-ToLight(right, up, front), Vector3.up);

        // A spot at `distance` m from her eyes on that side, looking at the eyes.
        static void PlaceSpot(Light spot, Quaternion cameraRotation, Vector3 eyes, float right, float up, bool front, float distance)
        {
            if (spot == null) return;
            var pos = eyes + cameraRotation * ToLight(right, up, front) * distance;
            spot.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(eyes - pos, Vector3.up));
        }

        // ---------------------------------------------------------------- pieces

        static Light FindLight(string name)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.name == name) return l;
            return null;
        }

        void EnsureLights()
        {
            if (_face != null) return;
            // the same lighting layers as the stage's key, so only the phone window's Harin is lit
            uint layers = _key != null ? _key.GetUniversalAdditionalLightData().renderingLayers : 2u;
            _face = MakeSpot("NightFaceLight", FaceColor, FaceIntensity, FaceAngle, FaceInnerAngle, layers);
            _rim = MakeSpot("NightRimLight", RimColor, RimIntensity, RimAngle, RimInnerAngle, layers);
            _rimLayers = layers;
        }

        Light MakeSpot(string name, Color color, float intensity, float angle, float inner, uint layers)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 4f;
            l.spotAngle = angle;
            l.innerSpotAngle = inner;
            l.shadows = LightShadows.None;
            l.GetUniversalAdditionalLightData().renderingLayers = layers;
            return l;
        }

        void EnsureBackdrop()
        {
            if (_backdrop != null) return;
            _backTex = MakeBackdropTexture();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            _backMat = new Material(shader) { name = "NightBackdrop", hideFlags = HideFlags.HideAndDontSave };
            _backMat.SetTexture("_BaseMap", _backTex);
            _backMat.SetTexture("_MainTex", _backTex);
            _backMat.SetColor("_BaseColor", Color.white);
            _backMat.SetFloat("_Cull", 0f);
            float tiles = BackdropSizeMeters / BackdropTileMeters;
            _backMat.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            _backMat.SetTextureScale("_MainTex", new Vector2(tiles, tiles));

            _backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _backdrop.name = "NightBackdrop";
            var col = _backdrop.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _backdrop.transform.SetParent(transform, false);
            _backdrop.transform.localScale = new Vector3(BackdropSizeMeters, BackdropSizeMeters, 1f);
            // the layer the window camera draws, and the lighting layer of the stage (it is unlit, the layer only keeps it off the room lights)
            var harin = _boot != null ? _boot.Harin : null;
            _backdrop.layer = harin != null ? harin.gameObject.layer : _cam.gameObject.layer;
            var r = _backdrop.GetComponent<MeshRenderer>();
            r.sharedMaterial = _backMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _backdrop.SetActive(false);
        }

        /// <summary>The backdrop picture: a vertical gradient with soft round lights that wrap around the edges (it is tiled).</summary>
        public static Texture2D MakeBackdropTexture()
        {
            int n = BackdropTexture;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                // a triangle wave, so the tiles meet without a seam: the bottom colour at both edges, the top colour in the middle
                var row = Color.Lerp(BackBottom, BackTop, 1f - Mathf.Abs(2f * y / (n - 1) - 1f));
                for (int x = 0; x < n; x++) px[y * n + x] = row;
            }
            var rng = new System.Random(BokehSeed);
            float pxPerMeter = n / BackdropTileMeters;
            int total = BokehAmberCount + BokehBlueCount;
            for (int i = 0; i < total; i++)
            {
                bool amber = i < BokehAmberCount;
                var tint = amber ? BokehAmber : BokehBlue;
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float radius = Mathf.Lerp(BokehMinRadius, BokehMaxRadius, (float)rng.NextDouble()) * pxPerMeter;
                float strength = Mathf.Lerp(BokehMinAlpha, BokehMaxAlpha, (float)rng.NextDouble());
                int reach = Mathf.CeilToInt(radius) + 1;
                for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (d >= 1f) continue;
                    // flat disc with a soft edge
                    float a = strength * Mathf.Clamp01((1f - d) / BokehSoftness);
                    int x = ((Mathf.RoundToInt(cx) + dx) % n + n) % n;
                    int y = ((Mathf.RoundToInt(cy) + dy) % n + n) % n;
                    var c = px[y * n + x];
                    c.r += tint.r * a;
                    c.g += tint.g * a;
                    c.b += tint.b * a;
                    px[y * n + x] = c;
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false)
            {
                name = "NightBackdropTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The vignette picture: black, clear in the middle, <see cref="VignetteStrength"/> in the corners.</summary>
        public static Texture2D MakeVignetteTexture()
        {
            int n = VignetteTexture;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float nx = (x + 0.5f) / n * 2f - 1f, ny = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny) / Mathf.Sqrt(2f);
                float t = Mathf.Clamp01((r - VignetteStart) / (1f - VignetteStart));
                px[y * n + x] = new Color(0f, 0f, 0f, VignetteStrength * t * t * (3f - 2f * t));
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "NightVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------------------------------------------------------- reading check

        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        static float Luminance(Color c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);

        /// <summary>WCAG contrast ratio of two colours (1 to 21); the text on a bubble should reach 4.5.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        /// <summary>The colour a translucent fill shows over a background.</summary>
        public static Color Over(Color fill, Color back) => new Color(
            Mathf.Lerp(back.r, fill.r, fill.a), Mathf.Lerp(back.g, fill.g, fill.a), Mathf.Lerp(back.b, fill.b, fill.a), 1f);
    }
}
