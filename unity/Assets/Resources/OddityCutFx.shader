// Unlit see-through quad for the emergence effects (screen noise, portal ring, glow, contact shadow).
// Blend is set per material: SrcAlpha One for light, SrcAlpha OneMinusSrcAlpha for shadow.
// _ZWrite On with Blend One Zero makes it opaque (the cut 10 desk picture and the portal floor).
// _Hole (world centre xyz, radius w) discards the inside of a circle on the XZ plane: the cut 10 portal.
// The desk and the screen noise skip it, so the arm under the desk plane shows only through the portal.
// Lives in Resources so Shader.Find keeps it in player builds.
Shader "Oddity/CutFx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        _Hole ("Hole (world xyz, radius)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _Hole;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.world.xz - _Hole.xz;
                if (_Hole.w > 0.0) clip(dot(d, d) - _Hole.w * _Hole.w);
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
