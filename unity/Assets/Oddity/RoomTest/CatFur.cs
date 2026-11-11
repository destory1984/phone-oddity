using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Shell fur on a room cat (trial, white cat first): the body's SkinnedMeshRenderer gets N extra materials of Oddity/ShellFur. A renderer with more
    // materials than submeshes draws its last submesh once more per extra material, so the skinned body is drawn N more times, each shell further out.
    // The fur length per vertex goes into the vertex colour (R) of a copy of the mesh: full on the trunk and tail, longer under the belly and chest,
    // short on the lower legs and head, cut away round the eyes, none on the ears' inside. G = 1 on the body, 0 on the head.
    // ODDITY_FUR=0 turns it off; ODDITY_FURSHELLS (12), ODDITY_FURLEN (m, 0.016), ODDITY_FURDENSITY (strands per UV unit, 900), ODDITY_FURBELLY (1.8),
    // ODDITY_FUREYE0 / ODDITY_FUREYE1 (model units from an eye: no fur inside 0.015, full face length from 0.05).
    public static class CatFur
    {
        static float Env(string n, float d) => float.TryParse(Environment.GetEnvironmentVariable(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
        public static bool On => Environment.GetEnvironmentVariable("ODDITY_FUR") != "0";

        // how long the fur is on a bone's skin, 0..1 (the belly factor is applied on top for the trunk)
        public static float BoneLength(string bone)
        {
            string b = bone.ToLowerInvariant();
            if (b.Contains("whisk") || b.Contains("eye")) return 0f;
            if (b.Contains("ear")) return .15f;
            if (b.Contains("toes") || b.Contains("paw")) return .3f;
            if (b.Contains("jaw") || b.Contains("head")) return .35f;
            if (b.Contains("lowerleg") || b.Contains("hock")) return .5f;
            if (b.Contains("upperleg") || b.Contains("scapula")) return .8f;
            if (b.Contains("neck")) return .9f;
            return 1f;      // pelvis, spine, chest, tail
        }
        public static bool Trunk(string bone)
        {
            string b = bone.ToLowerInvariant();
            return b.Contains("pelvis") || b.Contains("spine") || b.Contains("chest");
        }
        public static bool HeadBone(string bone)
        {
            string b = bone.ToLowerInvariant();
            return b.Contains("head") || b.Contains("ear") || b.Contains("jaw") || b.Contains("eye");
        }

        static SkinnedMeshRenderer Body(GameObject cat) => cat.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
        static bool Painted(SkinnedMeshRenderer smr) => smr.sharedMesh != null && smr.sharedMesh.name.EndsWith("_fur");

        // Paints a copy of the body mesh once: vertex colour R = fur length (0..1 of the longest), G = 1 on the body and 0 on the head.
        // On the body every texel is coat (the map has dark fill patches under the tail, inside the legs and on the back: they must not stay bald and grey);
        // only the head has things that are not coat (eyes, nose, inner ear). Round the eyes the fur is cut short. The eyes are found from the map:
        // head vertices whose texel is the blue of the iris.
        static string Paint(SkinnedMeshRenderer smr, Texture map, float belly)
        {
            var src = smr.sharedMesh;
            if (Painted(smr)) return "painted";
            if (!src.isReadable) return "uniform (mesh not readable)";
            var mesh = UnityEngine.Object.Instantiate(src); mesh.name = src.name + "_fur";
            var bw = mesh.boneWeights; var normals = mesh.normals; var verts = mesh.vertices; var uv = mesh.uv; var bones = smr.bones; int n = mesh.vertexCount;
            var per = bones.Select(t => t != null ? BoneLength(t.name) : 1f).ToArray();
            var trunk = bones.Select(t => t != null && Trunk(t.name)).ToArray();
            var headB = bones.Select(t => t != null && HeadBone(t.name)).ToArray();
            var col = new Color[n]; var head = new float[n];
            Vector3 up = smr.transform.InverseTransformDirection(Vector3.up).normalized;
            for (int i = 0; i < n; i++)
            {
                float l = 1f, tr = 0f, hd = 0f;
                if (bw.Length == n)
                {
                    var w = bw[i];
                    l = per[w.boneIndex0] * w.weight0 + per[w.boneIndex1] * w.weight1 + per[w.boneIndex2] * w.weight2 + per[w.boneIndex3] * w.weight3;
                    tr = (trunk[w.boneIndex0] ? w.weight0 : 0) + (trunk[w.boneIndex1] ? w.weight1 : 0) + (trunk[w.boneIndex2] ? w.weight2 : 0) + (trunk[w.boneIndex3] ? w.weight3 : 0);
                    hd = (headB[w.boneIndex0] ? w.weight0 : 0) + (headB[w.boneIndex1] ? w.weight1 : 0) + (headB[w.boneIndex2] ? w.weight2 : 0) + (headB[w.boneIndex3] ? w.weight3 : 0);
                }
                // under the trunk (rest-pose normal pointing down) the fur is longer: the belly and chest ruff
                float under = normals.Length == n ? Mathf.Clamp01(-Vector3.Dot(normals[i], up)) : 0f;
                l *= 1f + (belly - 1f) * under * tr;
                head[i] = hd; col[i] = new Color(l, hd > .5f ? 0f : 1f, 0, 1);
            }
            // the eyes: read the map small and look for iris blue under the head's vertices
            int eyes = 0;
            if (map != null && uv.Length == n)
            {
                const int S = 1024;
                var rt = RenderTexture.GetTemporary(S, S, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(map, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
                var small = new Texture2D(S, S, TextureFormat.RGBA32, false); small.ReadPixels(new Rect(0, 0, S, S), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var px = small.GetPixels32(); UnityEngine.Object.Destroy(small);
                var eye = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    if (head[i] < .5f) continue;
                    int x = Mathf.Clamp((int)(Mathf.Repeat(uv[i].x, 1f) * S), 0, S - 1), y = Mathf.Clamp((int)(Mathf.Repeat(uv[i].y, 1f) * S), 0, S - 1);
                    var c = px[y * S + x];
                    if (c.b > 90 && c.b > c.r * 1.5f && c.b > c.g * 1.1f) eye.Add(verts[i]);
                }
                eyes = eye.Count;
                if (eyes > 600) { int step = eyes / 600 + 1; eye = eye.Where((e, k) => k % step == 0).ToList(); } // the eyes are finely meshed: a few hundred points are enough
                if (eyes > 0)
                {
                    float r0 = Env("ODDITY_FUREYE0", .015f), r1 = Env("ODDITY_FUREYE1", .05f);
                    for (int i = 0; i < n; i++)
                    {
                        if (head[i] < .5f) continue;
                        float d2 = float.MaxValue; var v = verts[i];
                        for (int k = 0; k < eye.Count; k++) { float q = (eye[k] - v).sqrMagnitude; if (q < d2) d2 = q; }
                        col[i].r *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r0, r1, Mathf.Sqrt(d2)));
                    }
                }
            }
            float sum = 0; float inv = 1f / Mathf.Max(belly, 1f);
            for (int i = 0; i < n; i++) { sum += col[i].r; col[i].r = Mathf.Clamp01(col[i].r * inv); }
            mesh.colors = col; smr.sharedMesh = mesh;
            return "per-bone mean=" + (sum / Mathf.Max(1, n)).ToString("0.00", CultureInfo.InvariantCulture) + " eyeVerts=" + eyes;
        }

        /// <summary>The coat lifted to neutral white: the body's material becomes a copy whose base map is a whitened copy of the model's own
        /// (Hidden/Oddity/CoatWhiten). On the head only bright, colourless texels are coat (eyes, nose and inner ear untouched); on the body (a UV-space mask
        /// drawn from the painted mesh) everything without colour is, however dark. ODDITY_WHITEN = gain (1.36; 0 leaves the model's warm grey). Call before Apply.</summary>
        public static string Whiten(GameObject cat)
        {
            float gain = Env("ODDITY_WHITEN", 1.36f);
            if (gain <= 0) return "off (ODDITY_WHITEN=0)";
            var shader = Shader.Find("Hidden/Oddity/CoatWhiten"); var maskShader = Shader.Find("Hidden/Oddity/UvMask");
            if (shader == null || !shader.isSupported) return "no shader Hidden/Oddity/CoatWhiten";
            var smr = Body(cat);
            var baseMat = smr != null ? smr.sharedMaterials.FirstOrDefault(m => m != null) : null;
            if (baseMat == null) return "no material";
            Texture map = baseMat.HasProperty("_BaseMap") ? baseMat.GetTexture("_BaseMap") : baseMat.mainTexture;
            if (map == null) return "no base map";
            string painted = Paint(smr, map, Env("ODDITY_FURBELLY", 1.8f));
            // body mask in UV space: white (body) everywhere, the head's triangles drawn black
            RenderTexture mask = null;
            if (maskShader != null && maskShader.isSupported && Painted(smr))
            {
                mask = RenderTexture.GetTemporary(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var mm = new Material(maskShader); var prev = RenderTexture.active;
                Graphics.SetRenderTarget(mask); GL.Clear(true, true, Color.white);
                mm.SetPass(0); Graphics.DrawMeshNow(smr.sharedMesh, Matrix4x4.identity);
                RenderTexture.active = prev; UnityEngine.Object.Destroy(mm);
            }
            int size = Mathf.Min(2048, Mathf.Max(map.width, map.height));
            var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = map.name + "_white", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, wrapMode = map.wrapMode };
            var blit = new Material(shader); blit.SetFloat("_Gain", gain); blit.SetTexture("_Mask", mask != null ? (Texture)mask : Texture2D.blackTexture);
            Graphics.Blit(map, rt, blit); UnityEngine.Object.Destroy(blit);
            if (mask != null) RenderTexture.ReleaseTemporary(mask);
            var mat = new Material(baseMat) { name = baseMat.name + "_white" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt); else mat.mainTexture = rt;
            var mats = smr.sharedMaterials; for (int i = 0; i < mats.Length; i++) if (mats[i] == baseMat) mats[i] = mat;
            smr.sharedMaterials = mats;
            return "gain=" + gain.ToString("0.00", CultureInfo.InvariantCulture) + " map=" + map.name + " " + map.width + "x" + map.height + " -> " + size + " bodyMask=" + (mask != null ? 1 : 0) + " " + painted;
        }

        /// <summary>Returns a log line. Fur length is given in metres.</summary>
        public static string Apply(GameObject cat)
        {
            if (!On) return "off (ODDITY_FUR=0)";
            var shader = Shader.Find("Oddity/ShellFur");
            if (shader == null || !shader.isSupported) return "no shader Oddity/ShellFur";
            var smr = Body(cat);
            if (smr == null || smr.sharedMesh == null) return "no skinned mesh";
            var baseMat = smr.sharedMaterials.LastOrDefault(m => m != null);
            if (baseMat == null) return "no material";
            int shells = Mathf.Clamp((int)Env("ODDITY_FURSHELLS", 12), 1, 32);
            float len = Env("ODDITY_FURLEN", .016f), density = Env("ODDITY_FURDENSITY", 900f), belly = Env("ODDITY_FURBELLY", 1.8f);
            int verts = smr.sharedMesh.vertexCount;
            Texture map = baseMat.HasProperty("_BaseMap") ? baseMat.GetTexture("_BaseMap") : baseMat.mainTexture;
            string painted = Paint(smr, map, belly);
            // the vertex colour holds length / belly, so the material's full length is len * belly
            float lossy = Mathf.Max(1e-4f, smr.transform.lossyScale.y);
            var mats = smr.sharedMaterials.ToList();
            Color tint = baseMat.HasProperty("_BaseColor") ? baseMat.GetColor("_BaseColor") : Color.white;
            for (int s = 1; s <= shells; s++)
            {
                var m = new Material(shader) { name = "Fur" + s, enableInstancing = false };
                m.SetTexture("_BaseMap", map); m.SetColor("_BaseColor", tint);
                m.SetFloat("_Shell", s / (float)shells);
                m.SetFloat("_FurLength", Painted(smr) ? len * Mathf.Max(belly, 1f) : len);
                m.SetFloat("_Density", density);
                mats.Add(m);
            }
            smr.sharedMaterials = mats.ToArray();
            return "shells=" + shells + " len=" + len.ToString("0.000", CultureInfo.InvariantCulture) + " belly=x" + belly.ToString("0.0", CultureInfo.InvariantCulture) + " density=" + density.ToString("0", CultureInfo.InvariantCulture) + " " + painted + " verts=" + verts + " scale=" + lossy.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
