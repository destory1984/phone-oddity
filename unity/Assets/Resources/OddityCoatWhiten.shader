// Makes a copy of a cat's base colour map with the coat lifted to neutral white (CatFur.Whiten, used with Graphics.Blit).
// The white cat's own map is a warm grey (median 0.71 sRGB): coat texels (bright, hardly any colour) become grey * _Gain without the warm tint;
// eyes, nose and the inner ear (dark or coloured) are left as they are. Lives in Resources so Shader.Find keeps it in player builds.
Shader "Hidden/Oddity/CoatWhiten"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Gain ("Gain", Float) = 1.36
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Gain;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                float3 g = LinearToGammaSpace(c.rgb);
                #else
                float3 g = c.rgb;
                #endif
                float mx = max(g.r, max(g.g, g.b)), mn = min(g.r, min(g.g, g.b));
                float coat = smoothstep(0.30, 0.45, mx) * (1.0 - smoothstep(0.12, 0.22, (mx - mn) / max(mx, 1e-3)));
                float w = saturate(mx * _Gain);
                g = lerp(g, float3(w, w, w), coat);
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(g);
                #else
                c.rgb = g;
                #endif
                return c;
            }
            ENDCG
        }
    }
}
