// Makes a copy of a cat's base colour map with the coat lifted to neutral white (CatFur.Whiten, used with Graphics.Blit).
// The white cat's own map is a warm grey (median 0.71 sRGB): coat texels become grey * _Gain without the warm tint.
// On the head (mask 0) only bright texels with hardly any colour are coat: eyes, eye rims, nose and the inner ear are left as they are.
// On the body (mask 1) every colourless texel is coat, however dark: the map has dark fill patches under the tail, inside the legs and on the back,
// and they are lifted to at least 0.88. Paw pads (pink) keep their colour. Lives in Resources so Shader.Find keeps it in player builds.
Shader "Hidden/Oddity/CoatWhiten"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Mask ("Body mask (R: 1 body, 0 head)", 2D) = "black" {}
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
            sampler2D _Mask;
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
                float grey = 1.0 - smoothstep(0.12, 0.22, (mx - mn) / max(mx, 1e-3));
                float body = step(0.5, tex2D(_Mask, i.uv).r);
                float coat = grey * lerp(smoothstep(0.30, 0.45, mx), 1.0, body);
                float w = saturate(max(mx * _Gain, 0.88 * body));
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
