using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Cheek blush. Only the face mesh's SKIN material changes: its base and shade textures are swapped for a
    /// copy with <see cref="BlushMask"/> x <see cref="BlushColor"/> x amount mixed in (one GPU blit, redone only
    /// when the amount changes). At 0 the original textures go back, so nothing else (fade, glitch, room look,
    /// which only touch colours and alpha mode) sees a difference.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float BlushRise = 0.3f, BlushHold = 1.2f, BlushFall = 0.6f;
        /// <summary>Share of the blush colour at the middle of a cheek at amount 1. Tuned against prologue_blush.</summary>
        public const float BlushStrength = 0.35f;
        public static readonly Color BlushColor = new Color(1f, 0x8a / 255f, 0x8a / 255f, 1f);
        static readonly HashSet<string> BlushBeats = new HashSet<string> { "flustered", "looks_down_smiling", "quiet_confession" };
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ShadeTexId = Shader.PropertyToID("_ShadeTex");

        /// <summary>Optional pre-baked mask (Oddity/Bake Blush Mask); empty, the mask is made from the face mesh on first use.</summary>
        [SerializeField] Texture2D blushMask;

        Material _blushSkin;
        bool _blushReady;
        Material _blushMat;
        Texture _origMain, _origShade;
        RenderTexture _blushMain, _blushShade;
        float _blush;
        float _blushApplied = -1f;
        float _pulseStart = -1f;

        /// <summary>Current blush amount 0..1.</summary>
        public float Blush => _blush;
        /// <summary>The cheek mask in use (made on first use).</summary>
        public Texture2D BlushMaskTexture { get { EnsureBlush(); return blushMask; } }
        /// <summary>The face mesh in the head frame, as the mask is made from it (null if not readable).</summary>
        public BlushMask.Face BlushFace => MeasureFace();
        /// <summary>The face SKIN material instance that the blush changes.</summary>
        public Material BlushMaterial { get { EnsureBlush(); return _blushSkin; } }
        /// <summary>The original base texture of the face SKIN material.</summary>
        public Texture BlushOriginalTexture { get { EnsureBlush(); return _origMain; } }
        /// <summary>True while a rise-hold-fall pulse runs.</summary>
        public bool BlushPulsing => _pulseStart >= 0f;
        /// <summary>No automatic blush (the prologue rewind flips through faces).</summary>
        public bool BlushSuppressed { get; set; }

        /// <summary>Sets the blush amount at once and stops any running pulse.</summary>
        public void SetBlush(float amount)
        {
            _pulseStart = -1f;
            ApplyBlush(amount);
        }

        /// <summary>Rises to 1 in 0.3 s, holds 1.2 s, falls to 0 in 0.6 s (restarts if one is running).</summary>
        public void PulseBlush()
        {
            if (BlushSuppressed) return;
            _pulseStart = Time.time;
            UpdateBlush();
        }

        /// <summary>A line starts: blush when the face is flustered or the beat is a shy one.</summary>
        public void BlushForLine(string face, string beat)
        {
            if (WantsBlush(face, beat)) PulseBlush();
        }

        public static bool WantsBlush(string face, string beat) =>
            (face ?? "").Trim().ToLowerInvariant() == "flustered" || (beat != null && BlushBeats.Contains(beat));

        /// <summary>Envelope of a pulse <paramref name="t"/> seconds after it started.</summary>
        public static float BlushEnvelope(float t)
        {
            if (t < 0f) return 0f;
            if (t < BlushRise) return Mathf.SmoothStep(0f, 1f, t / BlushRise);
            t -= BlushRise + BlushHold;
            if (t < 0f) return 1f;
            if (t < BlushFall) return Mathf.SmoothStep(1f, 0f, t / BlushFall);
            return 0f;
        }

        // Called from Update.
        void UpdateBlush()
        {
            if (_pulseStart < 0f) return;
            float t = Time.time - _pulseStart;
            if (t >= BlushRise + BlushHold + BlushFall) _pulseStart = -1f;
            ApplyBlush(BlushEnvelope(t));
        }

        void OnFaceChanged(string before)
        {
            if (CurrentFace == "flustered" && before != "flustered") PulseBlush();
        }

        void ApplyBlush(float amount)
        {
            _blush = Mathf.Clamp01(amount);
            // 1/255 steps: the blit is redone only when the result can change
            float q = Mathf.Round(_blush * 255f) / 255f;
            if (q == _blushApplied) return;
            if (!EnsureBlush()) return;
            _blushApplied = q;
            var m = _blushSkin;
            if (q <= 0f)
            {
                m.SetTexture(MainTexId, _origMain);
                if (m.HasProperty(ShadeTexId)) m.SetTexture(ShadeTexId, _origShade);
                return;
            }
            _blushMat.SetTexture("_Mask", blushMask);
            _blushMat.SetColor("_BlushColor", BlushColor);
            _blushMat.SetFloat("_Amount", q * BlushStrength * _blushScale);
            if (_origMain != null)
            {
                Blend(_origMain, ref _blushMain);
                m.SetTexture(MainTexId, _blushMain);
            }
            if (m.HasProperty(ShadeTexId) && _origShade != null)
            {
                if (_origShade == _origMain) m.SetTexture(ShadeTexId, _blushMain);
                else
                {
                    Blend(_origShade, ref _blushShade);
                    m.SetTexture(ShadeTexId, _blushShade);
                }
            }
        }

        void Blend(Texture src, ref RenderTexture rt)
        {
            if (rt == null)
            {
                bool mips = src is Texture2D t2 && t2.mipmapCount > 1;
                rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = src.name + " (blush)",
                    useMipMap = mips,
                    autoGenerateMips = mips,
                    wrapMode = src.wrapMode,
                    filterMode = src.filterMode,
                    anisoLevel = src.anisoLevel,
                };
                rt.Create();
            }
            Graphics.Blit(src, rt, _blushMat);
        }

        // Finds the face material (and makes the mask if none was baked) once; false when the model has no face SKIN material.
        bool EnsureBlush()
        {
            if (_blushReady) return _blushSkin != null;
            _blushReady = true;
            var shader = Shader.Find("Hidden/Oddity/Blush");
            if (shader == null) { Debug.LogWarning("HarinPuppet: blush shader missing"); return false; }
            if (!BlushMask.FindSkin(gameObject, out var renderer, out int submesh))
            {
                Debug.LogWarning("HarinPuppet: no face SKIN material, no blush");
                return false;
            }
            // .materials gives the per-renderer instances that SetFade and the room look also change
            var mats = renderer.materials;
            if (submesh >= mats.Length || mats[submesh] == null) return false;
            if (blushMask == null)
            {
                var face = MeasureFace();
                if (face == null) { Debug.LogWarning("HarinPuppet: face mesh not readable and no baked blush mask"); return false; }
                blushMask = BlushMask.Bake(face);
            }
            _blushSkin = mats[submesh];
            _origMain = _blushSkin.GetTexture(MainTexId);
            _origShade = _blushSkin.HasProperty(ShadeTexId) ? _blushSkin.GetTexture(ShadeTexId) : null;
            _blushMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        BlushMask.Face MeasureFace() => BlushMask.Find(gameObject, _head != null ? _headRel : Quaternion.identity);

        void OnDestroy()
        {
            if (_mGraph.IsValid()) _mGraph.Destroy();
            if (_blushSkin != null && _blushApplied > 0f)
            {
                _blushSkin.SetTexture(MainTexId, _origMain);
                if (_blushSkin.HasProperty(ShadeTexId)) _blushSkin.SetTexture(ShadeTexId, _origShade);
            }
            if (_blushMain != null) { _blushMain.Release(); Destroy(_blushMain); }
            if (_blushShade != null) { _blushShade.Release(); Destroy(_blushShade); }
            if (_blushMat != null) Destroy(_blushMat);
        }
    }
}
