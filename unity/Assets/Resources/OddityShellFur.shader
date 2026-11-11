// Shell fur for the room cats (RoomTest/CatFur.cs): the body mesh is drawn again N times, each copy pushed a little further out along the normal
// and cut into strands by a noise cell pattern, so the silhouette turns soft instead of a smooth skin line. One material per shell (_Shell 0..1).
// Vertex colour R = how long the fur is at that vertex (0 none .. 1 full, belly more), written by CatFur from the bone weights.
// Texels that are not coat (eyes, nose, inner ear: dark or coloured) grow no fur. Lives in Resources so Shader.Find keeps it in player builds.
Shader "Oddity/ShellFur"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Shell ("Shell 0..1", Float) = 0.5
        _FurLength ("Length (m)", Float) = 0.015
        _Density ("Strands per UV unit", Float) = 900
        _Thickness ("Strand thickness", Float) = 1.1
        _RootShade ("Shade at the root", Float) = 0.86
        _Droop ("Droop (m at the tip)", Float) = 0.004
        _Comb ("Comb along the surface (m at the tip)", Float) = 0.004
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Fur"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Shell, _FurLength, _Density, _Thickness, _RootShade, _Droop, _Comb;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2;
                half len : TEXCOORD3; half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                half len = v.color.r;
                float s = _Shell;
                // out along the normal; the tips hang a little and lie back along the body (toward -up projected on the surface)
                float3 down = float3(0, -1, 0);
                float3 along = normalize(down - nw * dot(down, nw) + 1e-5);
                pw += nw * (_FurLength * len * s) + (down * _Droop + along * _Comb) * (len * s * s);
                o.positionWS = pw; o.normalWS = nw; o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.len = len;
                o.positionCS = TransformWorldToHClip(pw);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            half4 frag(Varyings i) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                // coat only: eyes, nose and the inner ear are dark or coloured
                half mx = max(base.r, max(base.g, base.b)), mn = min(base.r, min(base.g, base.b));
                half coat = step(0.38, mx) * step(mx - mn, 0.16 * mx + 0.02);
                clip(i.len * coat - 0.04);
                float2 cell = i.uv * _Density; float2 id = floor(cell);
                float rnd = hash21(id);                                  // this strand's length 0..1
                float2 c = frac(cell) - 0.5 + (float2(hash21(id + 7.1), hash21(id + 3.7)) - 0.5) * 0.5;
                float r = length(c) * 2.0;
                // a strand reaches up to shell rnd and tapers to its tip
                clip(rnd - _Shell);
                clip(_Thickness * (1.0 - _Shell / max(rnd, 1e-3)) + 0.15 - r);

                float3 n = normalize(i.normalWS);
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light ml = GetMainLight(sc);
                half wrap = saturate(dot(n, ml.direction) * 0.6 + 0.4);
                half3 lit = ml.color * (wrap * ml.shadowAttenuation * ml.distanceAttenuation) + SampleSH(n);
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS; inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    lit += l.color * (saturate(dot(n, l.direction) * 0.6 + 0.4) * l.distanceAttenuation);
                LIGHT_LOOP_END
                #endif
                half shade = lerp(_RootShade, 1.0, _Shell);
                half3 col = base.rgb * lit * shade;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
