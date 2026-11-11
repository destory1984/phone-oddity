using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Shell fur on a room cat (trial, white cat first): the body's SkinnedMeshRenderer gets N extra materials of Oddity/ShellFur. A renderer with more
    // materials than submeshes draws its last submesh once more per extra material, so the skinned body is drawn N more times, each shell further out.
    // The fur length per vertex goes into the vertex colour (R) of a copy of the mesh: full on the trunk and tail, longer under the belly and chest,
    // short on the lower legs and head, none on the whiskers, eyes and ears.
    // ODDITY_FUR=0 turns it off; ODDITY_FURSHELLS (12), ODDITY_FURLEN (m, 0.016), ODDITY_FURDENSITY (strands per UV unit, 900), ODDITY_FURBELLY (1.8).
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
            if (b.Contains("jaw") || b.Contains("head")) return .5f;
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

        /// <summary>Returns a log line. scale: the cat's world scale (fur length is given in metres).</summary>
        public static string Apply(GameObject cat)
        {
            if (!On) return "off (ODDITY_FUR=0)";
            var shader = Shader.Find("Oddity/ShellFur");
            if (shader == null || !shader.isSupported) return "no shader Oddity/ShellFur";
            var smr = cat.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
            if (smr == null || smr.sharedMesh == null) return "no skinned mesh";
            var baseMat = smr.sharedMaterials.LastOrDefault(m => m != null);
            if (baseMat == null) return "no material";
            int shells = Mathf.Clamp((int)Env("ODDITY_FURSHELLS", 12), 1, 32);
            float len = Env("ODDITY_FURLEN", .016f), density = Env("ODDITY_FURDENSITY", 900f), belly = Env("ODDITY_FURBELLY", 1.8f);

            string painted = "uniform";
            var src = smr.sharedMesh;
            if (src.isReadable)
            {
                var mesh = UnityEngine.Object.Instantiate(src); mesh.name = src.name + "_fur";
                var bw = mesh.boneWeights; var normals = mesh.normals; var bones = smr.bones;
                var per = bones.Select(t => t != null ? BoneLength(t.name) : 1f).ToArray();
                var trunk = bones.Select(t => t != null && Trunk(t.name)).ToArray();
                var col = new Color[mesh.vertexCount];
                // the mesh's down: the model's up axis in mesh space
                Vector3 up = smr.transform.InverseTransformDirection(Vector3.up).normalized;
                float sum = 0;
                for (int i = 0; i < col.Length; i++)
                {
                    float l = 1f, tr = 0f;
                    if (bw.Length == col.Length)
                    {
                        var w = bw[i];
                        l = per[w.boneIndex0] * w.weight0 + per[w.boneIndex1] * w.weight1 + per[w.boneIndex2] * w.weight2 + per[w.boneIndex3] * w.weight3;
                        tr = (trunk[w.boneIndex0] ? w.weight0 : 0) + (trunk[w.boneIndex1] ? w.weight1 : 0) + (trunk[w.boneIndex2] ? w.weight2 : 0) + (trunk[w.boneIndex3] ? w.weight3 : 0);
                    }
                    // under the trunk (rest-pose normal pointing down) the fur is longer: the belly and chest ruff
                    float under = normals.Length == col.Length ? Mathf.Clamp01(-Vector3.Dot(normals[i], up)) : 0f;
                    l *= 1f + (belly - 1f) * under * tr;
                    col[i] = new Color(Mathf.Clamp01(l / Mathf.Max(belly, 1f)), 0, 0, 1); sum += l;
                }
                mesh.colors = col; smr.sharedMesh = mesh;
                painted = "per-bone mean=" + (sum / Mathf.Max(1, col.Length)).ToString("0.00", CultureInfo.InvariantCulture);
            }
            // the vertex colour holds length / belly, so the material's full length is len * belly
            float lossy = Mathf.Max(1e-4f, smr.transform.lossyScale.y);
            var mats = smr.sharedMaterials.ToList();
            Texture map = baseMat.HasProperty("_BaseMap") ? baseMat.GetTexture("_BaseMap") : baseMat.mainTexture;
            Color tint = baseMat.HasProperty("_BaseColor") ? baseMat.GetColor("_BaseColor") : Color.white;
            for (int s = 1; s <= shells; s++)
            {
                var m = new Material(shader) { name = "Fur" + s, enableInstancing = false };
                m.SetTexture("_BaseMap", map); m.SetColor("_BaseColor", tint);
                m.SetFloat("_Shell", s / (float)shells);
                m.SetFloat("_FurLength", len * Mathf.Max(belly, 1f) * (src.isReadable ? 1f : 1f / Mathf.Max(belly, 1f)));
                m.SetFloat("_Density", density);
                mats.Add(m);
            }
            smr.sharedMaterials = mats.ToArray();
            return "shells=" + shells + " len=" + len.ToString("0.000", CultureInfo.InvariantCulture) + " belly=x" + belly.ToString("0.0", CultureInfo.InvariantCulture) + " density=" + density.ToString("0", CultureInfo.InvariantCulture) + " " + painted + " verts=" + src.vertexCount + " scale=" + lossy.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
