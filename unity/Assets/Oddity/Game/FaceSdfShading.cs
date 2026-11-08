using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oddity.Game
{
    /// <summary>
    /// Test of an anime two-tone face shadow on the 6T (N1 body) Harin models. Swaps the MToon skin
    /// materials of the body mesh for Oddity/HarinFaceSdf and feeds a head-fitted ellipsoid
    /// (centre, axes, head frame) every frame. Call Attach right after Instantiate, while the
    /// model is still in its rest pose. ODDITY_FACESDF=0 turns it off.
    /// Ellipsoid from blender_demo/output/n1_textured/91_sdf/sdf_meta.json (Blender to Unity: x -> -x, y -> -z, z -> y).
    /// </summary>
    // RoomPlay runs in the editor without Play mode, so LateUpdate never fires there. The head frame is
    // pushed from the render pipeline's beginCameraRendering instead, which runs in both modes.
    [ExecuteAlways]
    public class FaceSdfShading : MonoBehaviour
    {
        public const string BodyMesh = "c1072142_e68c_44f5_9593_20be05727183";
        static readonly string[] Skip = { "Oral", "Nail", "Hair", "Teeth", "Tongue" }; // eyelid, lash-underside and eye-corner patches are skin and must shade too
        static readonly string[] Meshes = { BodyMesh, "N1_Inner_Eyelids", "N1_Lip_Lining51" };
        static readonly int Centre = Shader.PropertyToID("_HeadCentre"), F = Shader.PropertyToID("_HeadF"), S = Shader.PropertyToID("_HeadS"), U = Shader.PropertyToID("_HeadU");
        static readonly int Jaw = Shader.PropertyToID("_JawCentre");
        static readonly Vector3 RestCentre = new Vector3(0f, 1.481f, -0.035f); // model space, metres
        static readonly Vector3 RestJaw = new Vector3(0f, 1.40f, 0.005f);
        const float AxisF = 0.102f, AxisS = 0.093f, AxisU = 0.118f;            // half depth, width, height

        Transform _head;
        Vector3 _forwardLocal, _sideLocal, _upLocal, _centreLocal, _jawLocal;
        float _scale = 1;
        Material[] _mats = Array.Empty<Material>();

        public static FaceSdfShading Attach(GameObject model)
        {
            if (Environment.GetEnvironmentVariable("ODDITY_FACESDF") == "0") return null;
            var anim = model.GetComponent<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            var all = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = all.FirstOrDefault(r => r.name == BodyMesh);
            var shader = Shader.Find("Oddity/HarinFaceSdf");
            if (head == null || body == null || shader == null)
            {
                Debug.Log("FACESDF skipped head=" + (head != null) + " body=" + (body != null) + " shader=" + (shader != null));
                return null;
            }
            var c = model.AddComponent<FaceSdfShading>();
            c._head = head;
            var inv = Quaternion.Inverse(head.rotation);
            c._forwardLocal = inv * model.transform.forward;
            c._sideLocal = inv * model.transform.right;
            c._upLocal = inv * model.transform.up;
            c._centreLocal = head.InverseTransformPoint(model.transform.TransformPoint(RestCentre));
            c._jawLocal = head.InverseTransformPoint(model.transform.TransformPoint(RestJaw));
            c._scale = model.transform.lossyScale.y;
            var swapped = new System.Collections.Generic.List<Material>();
            int total = 0;
            foreach (var rend in all.Where(r => Meshes.Contains(r.name)))
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || Skip.Any(s => m.name.Contains(s))) continue;
                    int alphaMode = m.HasProperty("_AlphaMode") ? Mathf.RoundToInt(m.GetFloat("_AlphaMode")) : 0;
                    if (alphaMode == 2 || m.renderQueue > 2450) continue; // transparent layers keep MToon
                    var n = new Material(shader) { name = m.name + " (FaceSdf)" };
                    var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    if (tex != null) { n.SetTexture("_BaseMap", tex); n.SetTextureScale("_BaseMap", m.GetTextureScale("_MainTex")); n.SetTextureOffset("_BaseMap", m.GetTextureOffset("_MainTex")); }
                    if (m.HasProperty("_Color")) n.SetColor("_BaseColor", m.GetColor("_Color"));
                    if (alphaMode == 1) { n.EnableKeyword("_ALPHATEST_ON"); n.SetFloat("_AlphaClip", 1); n.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : .5f); n.renderQueue = 2450; }
                    var shadeEnv = Environment.GetEnvironmentVariable("ODDITY_FACESHADE"); // "r,g,b" linear shade multiplier, for tuning
                    if (!string.IsNullOrEmpty(shadeEnv))
                    {
                        var v = shadeEnv.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        if (v.Length == 3) n.SetColor("_FaceShade", new Color(v[0], v[1], v[2], 1));
                    }
                    if (Environment.GetEnvironmentVariable("ODDITY_FACESDF_DEBUG") == "1") n.SetFloat("_Debug", 1);
                    mats[i] = n; swapped.Add(n);
                }
                rend.sharedMaterials = mats; total += mats.Length;
            }
            c._mats = swapped.ToArray();
            Debug.Log("FACESDF on " + model.name + " materials=" + swapped.Count + "/" + total);
            c.LateUpdate();
            return c;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCamera;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        void BeforeCamera(ScriptableRenderContext ctx, Camera cam) => LateUpdate();

        void LateUpdate()
        {
            if (_head == null) return;
            var r = _head.rotation;
            Vector4 f = r * _forwardLocal, s = r * _sideLocal, u = r * _upLocal;
            f.w = AxisF * _scale; s.w = AxisS * _scale; u.w = AxisU * _scale;
            Vector4 c = _head.TransformPoint(_centreLocal), j = _head.TransformPoint(_jawLocal);
            Shader.SetGlobalVector(Centre, c); Shader.SetGlobalVector(Jaw, j); Shader.SetGlobalVector(F, f); Shader.SetGlobalVector(S, s); Shader.SetGlobalVector(U, u);
        }

        void OnDestroy()
        {
            foreach (var m in _mats) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        }
    }
}
