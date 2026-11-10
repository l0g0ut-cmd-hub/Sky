// Эффекты залпа: вспышки у дул (свечение, складывается со светом), пороховой дым и шлейфы ядер.
// Все частицы — один меш квадов, повёрнутых к камере в вершинном шейдере, отсортированный от дальних к ближним.
// Смешение предумноженное: свечение пишет альфу 0 (складывается), дым — обычную прозрачность.
// В объёмном режиме рисуется из VolumetricClouds после облаков (_FxExternal = 1): глубина корабля — по текстуре глубины.
// Вершины: position — центр частицы, uv0 — угол (-0.5..0.5), uv1 — размер, поворот, вид (0 свечение, 1 дым), зерно; color.
Shader "Hidden/SkyTide/Fx"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SkyTide.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _FxRight, _FxUp;
            float4x4 _FxVP;       // проекция на цель (GPU), только для внешней отрисовки
            float4x4 _FxCamVP;    // проекция камеры (OpenGL) — координаты экрана
            float4x4 _FxCamV;
            float _FxExternal;
            float4 _FxParams;     // x яркость свечения, y яркость дыма, z верх моря облаков

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 color : COLOR;
                float3 wpos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float s, c;
                sincos(v.data.y, s, c);
                float2 q = float2(v.uv.x * c - v.uv.y * s, v.uv.x * s + v.uv.y * c);
                float3 wpos = v.vertex.xyz + (_FxRight.xyz * q.x + _FxUp.xyz * q.y) * v.data.x;
                o.pos = _FxExternal > 0.5 ? mul(_FxVP, float4(wpos, 1.0)) : mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = v.uv;
                o.data = v.data;
                o.color = v.color;
                o.wpos = wpos;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv) * 2.0;
                if (r > 1.0) discard;
                float m = smoothstep(_FxParams.z - 20.0, _FxParams.z + 5.0, i.wpos.y);
                if (_FxExternal > 0.5)
                {
                    float4 cp = mul(_FxCamVP, float4(i.wpos, 1.0));
                    float2 suv = cp.xy / cp.w * 0.5 + 0.5;
                    float eye = -mul(_FxCamV, float4(i.wpos, 1.0)).z;
                    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv);
                    if (Linear01Depth(raw) < 0.999) m *= saturate((LinearEyeDepth(raw) - eye) / 1.5);
                }
                if (i.data.z < 0.5)
                {
                    float g = exp(-r * r * 5.0) + 0.35 * exp(-r * r * 40.0);
                    return half4(i.color.rgb * (i.color.a * g * m * _FxParams.x), 0);
                }
                // Дым: мягкий край, клочья — шум неба со сдвигом по зерну частицы.
                float2 nuv = i.uv * 0.35 + i.data.ww * float2(3.7, 5.1);
                float n = tex2D(_SkyTideNoise, nuv).r * 0.6 + tex2D(_SkyTideNoise, nuv * 2.3 + 0.5).g * 0.4;
                float a = saturate(smoothstep(1.0, 0.15, r) * (0.55 + 0.9 * (n - 0.5)) * 1.3) * i.color.a * m;
                // Подсветка солнцем: край к солнцу ярче (солнце в +X мира).
                float sunSide = dot(normalize(_FxRight.xyz * i.uv.x + _FxUp.xyz * i.uv.y + 1e-4), normalize(_SkyTideSunDir.xyz));
                float3 col = i.color.rgb * _FxParams.y * (0.75 + 0.45 * saturate(sunSide) * r);
                return half4(col * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
