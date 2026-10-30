using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Hair look (Task 27): the teal read before her face did. Her HAIR materials get copies of their lit and
    /// shade textures with the HSV saturation times <see cref="HairSaturation"/> (hue and value kept, so it stays
    /// near the art's #1fb5b8), and the matcap highlight and the warm emission are scaled down. It runs once
    /// when the model wakes, so the phone, the prologue room and act 2 all show the same hair.
    /// </summary>
    public partial class HarinPuppet
    {
        public const float HairSaturation = 0.85f;
        public const float HairMatcapScale = 0.5f, HairEmissionScale = 0.5f;

        // desaturated copies, shared by every puppet that uses the same source texture
        static readonly Dictionary<int, (Texture2D tex, float ratio)> HairTextures = new Dictionary<int, (Texture2D, float)>();

        /// <summary>Mean saturation of the hair's lit texture after / before (1 when nothing was changed).</summary>
        public float HairSaturationRatio { get; private set; } = 1f;
        /// <summary>Number of HAIR materials the look was applied to.</summary>
        public int HairMaterials { get; private set; }

        void ApplyHairLook()
        {
            if (_mats == null) CaptureMaterials();
            float ratioSum = 0f;
            int ratioCount = 0;
            foreach (var s in _mats)
            {
                var m = s.Material;
                if (m == null || !m.name.Contains("HAIR")) continue;
                HairMaterials++;
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") is Texture2D main)
                {
                    var (tex, ratio) = Desaturated(main);
                    if (tex != null) { m.SetTexture("_MainTex", tex); ratioSum += ratio; ratioCount++; }
                }
                if (m.HasProperty("_ShadeTex") && m.GetTexture("_ShadeTex") is Texture2D shade)
                {
                    var (tex, _) = Desaturated(shade);
                    if (tex != null) m.SetTexture("_ShadeTex", tex);
                }
                if (m.HasProperty("_Color")) m.SetColor("_Color", Desat(m.GetColor("_Color")));
                if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", Desat(m.GetColor("_ShadeColor")));
                if (m.HasProperty("_MatcapColor")) m.SetColor("_MatcapColor", m.GetColor("_MatcapColor") * HairMatcapScale);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", m.GetColor("_EmissionColor") * HairEmissionScale);
            }
            if (ratioCount > 0) HairSaturationRatio = ratioSum / ratioCount;
        }

        static Color Desat(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return new Color(max - HairSaturation * (max - c.r), max - HairSaturation * (max - c.g), max - HairSaturation * (max - c.b), c.a);
        }

        // A readable copy of the texture with the saturation lowered: channel' = max - k (max - channel).
        static (Texture2D, float) Desaturated(Texture2D src)
        {
            int id = src.GetInstanceID();
            if (HairTextures.TryGetValue(id, out var cached) && cached.tex != null) return cached;
            Color32[] px;
            try
            {
                if (src.isReadable) px = src.GetPixels32();
                else
                {
                    var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    var read = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
                    read.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    px = read.GetPixels32();
                    Object.Destroy(read);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("HarinPuppet: hair texture not desaturated (" + e.GetType().Name + ")");
                return (null, 1f);
            }
            double before = 0, after = 0;
            float k = HairSaturation;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (max == 0) continue;
                byte r = (byte)Mathf.RoundToInt(max - k * (max - c.r));
                byte g = (byte)Mathf.RoundToInt(max - k * (max - c.g));
                byte b = (byte)Mathf.RoundToInt(max - k * (max - c.b));
                px[i] = new Color32(r, g, b, c.a);
                if (c.a < 128 || (i & 7) != 0) continue; // every 8th visible pixel for the measure
                int min2 = Mathf.Min(r, Mathf.Min(g, b));
                before += (max - min) / (double)max;
                after += (max - min2) / (double)max;
            }
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, false)
            {
                name = src.name + "_desat",
                wrapMode = src.wrapMode,
                filterMode = src.filterMode,
                anisoLevel = src.anisoLevel,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            float ratio = before > 0 ? (float)(after / before) : 1f;
            HairTextures[id] = (tex, ratio);
            return (tex, ratio);
        }
    }
}
