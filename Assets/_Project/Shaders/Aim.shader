// Прицельная дуга: квадраты уже в мировых координатах (их строит FxSystem по камере), текстура Resources/UI/aim_kit.
// Рисуется поверх облаков вместе с эффектами (в объёмном режиме — из VolumetricClouds, _AimExternal = 1).
Shader "Hidden/SkyTide/Aim"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4 }
    SubShader
    {
        Tags { "Queue" = "Transparent+60" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4x4 _AimVP;
            float _AimExternal;
            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            v2f vert (a2v v)
            {
                v2f o;
                o.pos = _AimExternal > 0.5 ? mul(_AimVP, float4(v.vertex.xyz, 1)) : mul(UNITY_MATRIX_VP, float4(v.vertex.xyz, 1));
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            half4 frag (v2f i) : SV_Target
            {
                half4 t = tex2D(_MainTex, i.uv) * i.color;
                return half4(t.rgb * t.a * 1.6, t.a);   // чуть ярче: светится на ярких облаках
            }
            ENDCG
        }
    }
    Fallback Off
}
