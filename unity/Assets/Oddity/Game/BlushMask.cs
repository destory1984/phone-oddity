using System.Collections.Generic;
using UnityEngine;

namespace Oddity.Game
{
    /// <summary>
    /// Builds the cheek mask for Harin's face texture from the face mesh, so no one paints it by hand and a new
    /// VRoid export gets its own mask. Positions are measured in a frame at the Head bone with the model's axes
    /// (x her side, y up, z towards her front), in the rest pose (from the bind poses, so any current pose is fine).
    /// Each cheek is a soft ellipse (Gaussian) centred 3.5 cm to the side of the face centre and 3 cm below the
    /// eye bones, the middle of the 3 to 4 cm and 2.5 to 3.5 cm box. The face's SKIN triangles are drawn into UV
    /// space and every texel gets the ellipse's value at the 3D point it maps to, so the UV layout does not matter.
    /// </summary>
    public static class BlushMask
    {
        public const float CheekSide = 0.035f;    // m from the face centre line
        public const float CheekBelowEye = 0.030f; // m below the eye bones
        public const float SigmaSide = 0.010f;
        public const float SigmaUp = 0.007f;
        public const int Size = 256;
        const float Cutoff = 4f; // sigmas

        /// <summary>The face mesh and how its vertices map into the head frame.</summary>
        public sealed class Face
        {
            public SkinnedMeshRenderer Renderer;
            public int Submesh;
            public Vector3[] Positions;        // head frame, metres
            public Vector3[] Normals;          // head frame
            public Vector2[] Uvs;
            public int[] Triangles;            // SKIN submesh only
            public float EyeY;                 // head frame
            public float Forward = 1f;         // +1 when her front is +z in the head frame
        }

        /// <summary>The face mesh renderer and the index of its SKIN material; false when there is none.</summary>
        public static bool FindSkin(GameObject root, out SkinnedMeshRenderer renderer, out int submesh)
        {
            renderer = null;
            submesh = -1;
            int bestScore = -1;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < r.sharedMesh.subMeshCount; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.Contains("SKIN")) continue;
                    // VRoid: "..._Face_00_SKIN" on the face mesh; otherwise the skin on the mesh with most morphs
                    int score = m.name.Contains("Face") ? 100000 : r.sharedMesh.blendShapeCount;
                    if (score > bestScore) { renderer = r; submesh = i; bestScore = score; }
                }
            }
            return renderer != null;
        }

        /// <summary>
        /// Reads the face mesh into the head frame. <paramref name="headRestRotation"/> is the Head bone's rotation
        /// relative to the model root in the rest pose. Null when there is no face SKIN material or the mesh is not readable.
        /// </summary>
        public static Face Find(GameObject root, Quaternion headRestRotation)
        {
            var animator = root.GetComponent<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) return null;
            if (!FindSkin(root, out var best, out int bestSub)) return null;

            var mesh = best.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning("BlushMask: face mesh is not readable"); return null; }
            int hi = System.Array.IndexOf(best.bones, head);
            var bind = hi >= 0 && hi < mesh.bindposes.Length
                ? mesh.bindposes[hi]
                : head.worldToLocalMatrix * best.transform.localToWorldMatrix;
            var toFrame = Matrix4x4.Rotate(headRestRotation) * bind;

            var face = new Face
            {
                Renderer = best,
                Submesh = bestSub,
                Uvs = mesh.uv,
                Triangles = mesh.GetTriangles(bestSub),
            };
            var verts = mesh.vertices;
            var normals = mesh.normals;
            face.Positions = new Vector3[verts.Length];
            face.Normals = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                face.Positions[i] = toFrame.MultiplyPoint3x4(verts[i]);
                face.Normals[i] = i < normals.Length ? toFrame.MultiplyVector(normals[i]).normalized : Vector3.zero;
            }

            var l = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rr = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 eyes;
            if (l != null && rr != null)
                eyes = headRestRotation * ((head.InverseTransformPoint(l.position) + head.InverseTransformPoint(rr.position)) * 0.5f);
            else
                eyes = new Vector3(0f, 0.06f, 0.05f); // VRoid-like guess
            face.EyeY = eyes.y;
            face.Forward = eyes.z >= 0f ? 1f : -1f;
            return face;
        }

        /// <summary>Mask value 0..1 at a head-frame point with that surface normal.</summary>
        public static float Value(Face f, Vector3 p, Vector3 n)
        {
            float dx = (Mathf.Abs(p.x) - CheekSide) / SigmaSide;
            float dy = (p.y - (f.EyeY - CheekBelowEye)) / SigmaUp;
            float d2 = dx * dx + dy * dy;
            if (d2 > Cutoff * Cutoff) return 0f;
            // the front of the face only (not the back of the head or the inside of the mouth)
            float facing = Mathf.InverseLerp(0.15f, 0.45f, n.z * f.Forward);
            if (p.z * f.Forward < 0f) facing = 0f;
            return Mathf.Exp(-0.5f * d2) * facing;
        }

        /// <summary>Draws the mask (R8, linear, clamp). Values are rasterized per texel inside the SKIN triangles.</summary>
        public static Texture2D Bake(Face f, int size = Size)
        {
            var px = new byte[size * size];
            var t = f.Triangles;
            float xMin = CheekSide - Cutoff * SigmaSide, xMax = CheekSide + Cutoff * SigmaSide;
            float yC = f.EyeY - CheekBelowEye, yMin = yC - Cutoff * SigmaUp, yMax = yC + Cutoff * SigmaUp;
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                int a = t[k], b = t[k + 1], c = t[k + 2];
                Vector3 pa = f.Positions[a], pb = f.Positions[b], pc = f.Positions[c];
                // skip triangles away from both cheeks
                float ax = Mathf.Min(Mathf.Abs(pa.x), Mathf.Min(Mathf.Abs(pb.x), Mathf.Abs(pc.x)));
                float bx = Mathf.Max(Mathf.Abs(pa.x), Mathf.Max(Mathf.Abs(pb.x), Mathf.Abs(pc.x)));
                float ay = Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y));
                float by = Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y));
                bool crossesCentre = Mathf.Sign(pa.x) != Mathf.Sign(pb.x) || Mathf.Sign(pa.x) != Mathf.Sign(pc.x);
                if (crossesCentre) ax = 0f;
                if (bx < xMin || ax > xMax || by < yMin || ay > yMax) continue;

                Vector2 ua = f.Uvs[a] * size, ub = f.Uvs[b] * size, uc = f.Uvs[c] * size;
                float area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                if (Mathf.Abs(area) < 1e-8f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x))));
                int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y))));
                int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y))));
                Vector3 na = f.Normals[a], nb = f.Normals[b], nc = f.Normals[c];
                const float Pad = -0.02f; // a hair outside the edges so neighbouring triangles leave no gaps
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float w1 = ((q.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (q.y - ua.y)) / area;
                    float w2 = ((ub.x - ua.x) * (q.y - ua.y) - (q.x - ua.x) * (ub.y - ua.y)) / area;
                    float w0 = 1f - w1 - w2;
                    if (w0 < Pad || w1 < Pad || w2 < Pad) continue;
                    var p = pa * w0 + pb * w1 + pc * w2;
                    var n = (na * w0 + nb * w1 + nc * w2).normalized;
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(Value(f, p, n)) * 255f);
                    int i = y * size + x;
                    if (v > px[i]) px[i] = v;
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.R8, false, true)
            {
                name = "harin_blush_mask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixelData(px, 0);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// UV of the front-facing SKIN vertex nearest a point seen from her front (x side, y up; depth ignored).
        /// </summary>
        public static Vector2 UvNear(Face f, float x, float y)
        {
            float best = float.MaxValue;
            Vector2 uv = Vector2.zero;
            foreach (int i in SkinVertices(f))
            {
                if (f.Normals[i].z * f.Forward < 0.3f) continue;
                var p = f.Positions[i];
                float d = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
                if (d < best) { best = d; uv = f.Uvs[i]; }
            }
            return uv;
        }

        /// <summary>Distinct vertex indices used by the SKIN submesh.</summary>
        public static IEnumerable<int> SkinVertices(Face f)
        {
            var seen = new HashSet<int>();
            foreach (int i in f.Triangles)
                if (seen.Add(i)) yield return i;
        }
    }
}
