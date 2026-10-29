// Copies Harin's face texture with the cheek blush mixed in (HarinPuppet.SetBlush, used with Graphics.Blit).
// rgb = lerp(base, _BlushColor, mask * _Amount); alpha kept. Lives in Resources so Shader.Find keeps it in player builds.
Shader "Hidden/Oddity/Blush"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Mask ("Cheek mask (R)", 2D) = "black" {}
        _BlushColor ("Blush", Color) = (1, 0.54, 0.54, 1)
        _Amount ("Amount", Float) = 0
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
            fixed4 _BlushColor;
            float _Amount;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float m = saturate(tex2D(_Mask, i.uv).r * _Amount);
                c.rgb = lerp(c.rgb, _BlushColor.rgb, m);
                return c;
            }
            ENDCG
        }
    }
}
