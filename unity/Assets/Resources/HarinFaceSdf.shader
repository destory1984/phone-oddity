// Toon skin with an anime face shadow for the 6T (N1 body) Harin models.
// A head-fitted ellipsoid (centre, axes and head frame pushed every frame by FaceSdfShading.cs)
// marks the face. The whole face is one tone: lit unless the light comes from well behind the
// head (_FaceBias), then fully shaded. No per-pixel normals on the face, so the nose, eyelids and
// mouth make no blotches, and no texture, so it works on every face material whatever its UV.
// A short shadow falls under the chin onto the neck (ray from the skin along the head's up/forward
// to a jaw ellipsoid, at most _JawReach long) and the forehead above _BangLine is shaded.
// Cast shadows are ignored on purpose: out of the sun patch the skin keeps its lit tone.
// Preview: blender_demo/output/n1_textured/91_sdf/v5_preview.py. _RealNormal is unused (kept for old materials).
Shader "Oddity/HarinFaceSdf"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Base colour", Color) = (1,1,1,1)
        _FaceShade ("Shade multiplier (linear)", Color) = (0.867,0.823,0.873,1)
        // (not named _ShadeColor on purpose: HarinPose, HarinNight, HarinExpression and RoomPlay rewrite
        // _ShadeColor on every skin material for the MToon room look, which would wipe this shade out)
        _RealNormal ("Real normal share", Range(0,1)) = 0.1
        _Edge ("Shadow edge softness", Range(0.001,0.3)) = 0.03
        _Ambient ("Ambient share", Range(0,1)) = 0.35
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        _JawAxes ("Jaw half depth, width, height", Vector) = (0.05,0.05,0.03,0)
        _FaceBias ("Face lit until light is this far behind", Range(0,1)) = 0.92
        _BodyBias ("Body skin lit until light is this far behind", Range(0,1)) = 0.5
        _JawReach ("Chin shadow length (m)", Range(0,0.15)) = 0.02
        _JawTilt ("Chin shadow forward tilt", Range(0,1)) = 0.25
        _BangLine ("Bangs shadow line (head-space height)", Range(-1,1)) = 0.29
        _Debug ("Debug view: R face mask, G chin, B bangs", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor; half4 _FaceShade; half _Edge; half _Ambient; half _Cutoff; half _RealNormal;
                float4 _JawAxes; half _FaceBias; half _BodyBias; half _JawReach; half _JawTilt; half _BangLine; half _Debug;
            CBUFFER_END
            // Head frame: globals pushed by FaceSdfShading.cs every camera render. Globals, not material
            // properties, because HarinPuppet/HarinBlush clone the skin materials (renderer.materials) after
            // Attach, and the clones kept the stale per-material values.
            float4 _HeadCentre; float4 _HeadF; float4 _HeadS; float4 _HeadU; float4 _JawCentre;
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 n:TEXCOORD1; float3 wp:TEXCOORD2; half fog:TEXCOORD3; };
            V vert(A i)
            {
                V o; VertexPositionInputs p=GetVertexPositionInputs(i.pos.xyz);
                o.pos=p.positionCS; o.wp=p.positionWS; o.n=TransformObjectToWorldNormal(i.n); o.uv=i.uv; o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 frag(V i, bool front:SV_IsFrontFace):SV_Target
            {
                half4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,TRANSFORM_TEX(i.uv,_BaseMap))*_BaseColor;
                #if defined(_ALPHATEST_ON)
                clip(base.a-_Cutoff);
                #endif
                float3 n=normalize(front?i.n:-i.n);
                Light L=GetMainLight();
                half body=smoothstep(-_Edge,_Edge,dot(n,L.direction)+_BodyBias);   // neck, arms: only real backlight shades them
                float3 d=i.wp-_HeadCentre.xyz;
                float3 q=float3(dot(d,_HeadF.xyz)/_HeadF.w,dot(d,_HeadS.xyz)/_HeadS.w,dot(d,_HeadU.xyz)/_HeadU.w);
                float2 lh=float2(dot(L.direction,_HeadF.xyz),dot(L.direction,_HeadS.xyz));
                float h=length(lh);
                // v8: the whole face is one tone - fully lit, or fully shaded when the light comes from
                // well behind the head (more than about 157 deg at _FaceBias 0.92). No partial half-face.
                half face=smoothstep(-_Edge,_Edge,lh.x/max(h,1e-4)+_FaceBias);
                face=lerp(1,face,saturate(h*4));                   // light straight above or below: no face shadow
                half mask=1-smoothstep(1.35,1.6,length(q));        // the head and the whole face, chin included
                half lit=lerp(body,face,mask);
                // chin shadow on the neck: does a short ray up the head's axis hit the jaw ellipsoid?
                float3 dj=i.wp-_JawCentre.xyz;
                float3 a=float3(dot(dj,_HeadF.xyz)/_JawAxes.x,dot(dj,_HeadS.xyz)/_JawAxes.y,dot(dj,_HeadU.xyz)/_JawAxes.z);
                float3 D=normalize(_HeadU.xyz+_HeadF.xyz*_JawTilt);
                float3 b=float3(dot(D,_HeadF.xyz)/_JawAxes.x,dot(D,_HeadS.xyz)/_JawAxes.y,dot(D,_HeadU.xyz)/_JawAxes.z);
                float qa=dot(b,b), qb=2*dot(a,b), qc=dot(a,a)-1, disc=qb*qb-4*qa*qc;
                float t=(-qb-sqrt(max(disc,0)))/(2*qa);
                float reach=_JawReach*saturate(1-a.y*a.y*0.8);      // longest under the chin, short at the sides: a crescent, not a band
                half jaw=(disc>0 && t>0 && t<reach && a.z<-1) ? 1 : 0;
                // bangs: forehead above the line, front half of the head
                half bang=(q.z>_BangLine && q.x>0 && mask>0.5) ? 1 : 0;
                lit*=(1-jaw)*(1-bang);
                if(_Debug>0.5) return half4(mask,jaw,bang*0.5+face*0.5,1);   // B mixes in the face lit term
                half3 shade=lerp(_FaceShade.rgb,1,lit);
                half3 col=base.rgb*(shade*L.color+SampleSH(n)*_Ambient);
                col=MixFog(col,i.fog);
                return half4(col,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 n:NORMAL; };
            float4 v(A i):SV_POSITION
            {
                float3 w=TransformObjectToWorld(i.pos.xyz); float3 n=TransformObjectToWorldNormal(i.n);
                float4 c=TransformWorldToHClip(ApplyShadowBias(w,n,_LightDirection));
                #if UNITY_REVERSED_Z
                c.z=min(c.z,UNITY_NEAR_CLIP_VALUE);
                #else
                c.z=max(c.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 f():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 v(float4 p:POSITION):SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half f():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
