// Draws a mesh flat in its UV space with its vertex colour G (CatFur: 1 on a cat's body, 0 on its head), to make a texture-space mask of the body.
// Lives in Resources so Shader.Find keeps it in player builds.
Shader "Hidden/Oddity/UvMask"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed g : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
                #endif
                o.pos = float4(p, 0.5, 1.0);
                o.g = v.color.g;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(i.g, i.g, i.g, 1); }
            ENDCG
        }
    }
}
