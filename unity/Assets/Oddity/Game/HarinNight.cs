using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// What the night look (<see cref="NightLook"/>) changes on Harin herself (task 52). Hair materials are found by the
    /// "HAIR" in the material name (the VRoid export names them ..._HAIR), skin by "SKIN", so a change of clothes
    /// does not matter. Only the per-renderer material instances are touched (never the imported asset), and every
    /// value is put back exactly when the look ends. A model with no HAIR material is left as it is.
    /// </summary>
    public partial class HarinPuppet
    {
        class NightMat
        {
            public Material M;
            public bool Hair, Skin;
            public bool HasMatcap, HasRim, HasEmission;
            public Color Matcap, Rim, Emission, Base, Shade;
        }

        List<NightMat> _nightMats;
        readonly List<(Renderer renderer, uint mask)> _nightHairMasks = new List<(Renderer, uint)>();
        float _blushScale = 1f;

        /// <summary>True while <see cref="SetNightMaterials"/> has the night values in.</summary>
        public bool NightMaterialsOn => _nightMats != null;
        /// <summary>Number of HAIR materials the night look changed (0 when none was found).</summary>
        public int NightHairMaterials { get; private set; }
        /// <summary>The hair's (and only the hair's) renderers while <see cref="SetNightHairLayer"/> is on.</summary>
        public IReadOnlyList<(Renderer renderer, uint mask)> NightHairRenderers => _nightHairMasks;

        /// <summary>Multiplies the blush strength (1 in the day). A new value redraws a blush that is showing.</summary>
        public float BlushScale
        {
            get => _blushScale;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(value, _blushScale)) return;
                _blushScale = value;
                _blushApplied = -1f;
                if (_blush > 0f) ApplyBlush(_blush);
            }
        }

        /// <summary>
        /// On: the hair's matcap, rim and base colour go down by the given factors (so it does not outshine the face) and
        /// the skin colours move <paramref name="skinMix"/> of the way to the room look's neutral ones (cancels the pink under the cool
        /// light). Off: every changed value goes back to what it was when it was switched on.
        /// </summary>
        public void SetNightMaterials(bool on, float hairMatcap, float hairRim, float hairDim, float skinMix)
        {
            if (!on)
            {
                RestoreNightMaterials();
                return;
            }
            if (_nightMats != null) return;
            if (_mats == null) CaptureMaterials();
            _nightMats = new List<NightMat>();
            NightHairMaterials = 0;
            foreach (var s in _mats)
            {
                var m = s.Material;
                if (m == null) continue;
                bool hair = m.name.Contains("HAIR"), skin = m.name.Contains("SKIN");
                if (!hair && !skin) continue;
                var n = new NightMat
                {
                    M = m, Hair = hair, Skin = skin,
                    HasMatcap = m.HasProperty("_MatcapColor"), HasRim = m.HasProperty("_RimColor"), HasEmission = m.HasProperty("_EmissionColor"),
                };
                if (n.HasMatcap) n.Matcap = m.GetColor("_MatcapColor");
                if (n.HasRim) n.Rim = m.GetColor("_RimColor");
                if (n.HasEmission) n.Emission = m.GetColor("_EmissionColor");
                n.Base = m.GetColor("_Color");
                n.Shade = m.HasProperty("_ShadeColor") ? m.GetColor("_ShadeColor") : n.Base;
                _nightMats.Add(n);
                if (hair)
                {
                    NightHairMaterials++;
                    if (n.HasMatcap) m.SetColor("_MatcapColor", n.Matcap * hairMatcap);
                    if (n.HasRim) m.SetColor("_RimColor", n.Rim * hairRim);
                    var b = n.Base * hairDim;
                    b.a = m.GetColor("_Color").a; // keep the fade alpha
                    m.SetColor("_Color", b);
                }
                else
                {
                    var b = Color.Lerp(n.Base, SkinBase, skinMix);
                    b.a = m.GetColor("_Color").a;
                    m.SetColor("_Color", b);
                    if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", Color.Lerp(n.Shade, SkinShade, skinMix));
                }
            }
        }

        void RestoreNightMaterials()
        {
            if (_nightMats == null) return;
            foreach (var n in _nightMats)
            {
                var m = n.M;
                if (m == null) continue;
                if (n.HasMatcap) m.SetColor("_MatcapColor", n.Matcap);
                if (n.HasRim) m.SetColor("_RimColor", n.Rim);
                if (n.HasEmission) m.SetColor("_EmissionColor", n.Emission);
                var b = n.Base;
                b.a = m.GetColor("_Color").a;
                m.SetColor("_Color", b);
                if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", n.Shade);
            }
            _nightMats = null;
        }

        /// <summary>
        /// On: the renderers that draw only HAIR materials also get the lighting layer <paramref name="layerBit"/>, which only a
        /// hair-only light (the night rim) uses. Off: their layer masks go back. Returns how many renderers were found.
        /// </summary>
        public int SetNightHairLayer(bool on, uint layerBit)
        {
            if (!on)
            {
                foreach (var (r, mask) in _nightHairMasks) if (r != null) r.renderingLayerMask = mask;
                _nightHairMasks.Clear();
                return 0;
            }
            if (_nightHairMasks.Count > 0) return _nightHairMasks.Count;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats.Length == 0) continue;
                bool allHair = true;
                foreach (var m in mats) if (m == null || !m.name.Contains("HAIR")) { allHair = false; break; }
                if (!allHair) continue;
                _nightHairMasks.Add((r, r.renderingLayerMask));
                r.renderingLayerMask |= layerBit;
            }
            return _nightHairMasks.Count;
        }
    }
}
