// Эффекты залпа: вспышки у дул (свечение, складывается со светом), пороховой дым и шлейфы ядер.
// Все частицы — один меш квадов, повёрнутых к камере в вершинном шейдере, отсортированный от дальних к ближним.
// Смешение предумноженное: свечение пишет альфу 0 (складывается), дым — обычную прозрачность.
// В объёмном режиме рисуется из VolumetricClouds после облаков (_FxExternal = 1): глубина корабля — по текстуре глубины.
// uv1: размер/поворот/вид/зерно; uv2: ось/ширина; uv3: возраст/фаза/высота контакта с облаком.
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
            float4 _FxParams;     // x яркость свечения, y яркость дыма, z верх моря облаков, w объёмный вид эффектов

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 axis : TEXCOORD2;
                float4 anim : TEXCOORD3;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 color : COLOR;
                float3 wpos : TEXCOORD2;
                float4 anim : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float s, c;
                sincos(v.data.y, s, c);
                float2 q = float2(v.uv.x * c - v.uv.y * s, v.uv.x * s + v.uv.y * c);
                float3 offset = _FxRight.xyz * q.x + _FxUp.xyz * q.y;
                float kind = v.data.z;
                float headOn = 0.0;
                if ((kind > 2.5 && kind < 4.5) || kind > 6.5)
                {
                    // Факел и искры вытянуты вдоль ствола/скорости в плоскости экрана.
                    float2 along = float2(dot(v.axis.xyz, _FxRight.xyz), dot(v.axis.xyz, _FxUp.xyz));
                    float len2 = dot(along, along);
                    along = len2 > 1e-4 ? along * rsqrt(max(len2, 1e-4)) : float2(1, 0);
                    if (kind > 6.5) along = float2(along.x * c - along.y * s, along.x * s + along.y * c);
                    float2 side = float2(-along.y, along.x);
                    float width = v.axis.w;
                    float projectedLength = 1.0;
                    if (kind < 3.5 && _FxParams.w > 0.5)
                    {
                        float projection = saturate(sqrt(len2));
                        headOn = 1.0 - smoothstep(0.2, 0.75, projection);
                        width = lerp(width, 0.65, headOn);
                        projectedLength = max(width, projection);
                    }
                    float2 quad = along * v.uv.x * projectedLength + side * v.uv.y * width;
                    offset = _FxRight.xyz * quad.x + _FxUp.xyz * quad.y;
                }
                else if (kind > 5.5 && kind < 6.5)
                    offset = float3(q.x, 0, q.y);   // волна лежит на поверхности, не поворачивается за камерой
                float3 wpos = v.vertex.xyz + offset * v.data.x;
                o.pos = _FxExternal > 0.5 ? mul(_FxVP, float4(wpos, 1.0)) : mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = v.uv;
                o.data = v.data;
                o.color = v.color;
                o.wpos = wpos;
                o.anim = v.anim;
                o.anim.w = headOn;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv) * 2.0;
                float kind = i.data.z;
                bool directional = (kind > 2.5 && kind < 4.5) || kind > 6.5;
                if (!directional && r > 1.0) discard;
                bool cloud = kind > 4.5 && kind < 6.5;
                float m = cloud ? smoothstep(i.anim.z - 2.0, i.anim.z + 1.0, i.wpos.y)
                                : smoothstep(_FxParams.z - 20.0, _FxParams.z + 5.0, i.wpos.y);
                if (_FxExternal > 0.5)
                {
                    float4 cp = mul(_FxCamVP, float4(i.wpos, 1.0));
                    float2 suv = cp.xy / cp.w * 0.5 + 0.5;
                    float eye = -mul(_FxCamV, float4(i.wpos, 1.0)).z;
                    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv);
                    if (Linear01Depth(raw) < 0.999) m *= saturate((LinearEyeDepth(raw) - eye) / 1.5);
                }
                if (kind < 0.5)
                {
                    float g = exp(-r * r * 5.0) + 0.35 * exp(-r * r * 40.0);
                    return half4(i.color.rgb * (i.color.a * g * m * _FxParams.x), 0);
                }
                if (kind > 2.5 && kind < 3.5)
                {
                    float x = i.uv.x + 0.5;
                    float2 nuv = i.uv * float2(1.4, 0.8) + i.data.ww + float2(-i.anim.x * 8.0, 0);
                    float n = tex2D(_SkyTideNoise, nuv).r;
                    float edge = saturate((1.0 - x) * 1.4 - abs(i.uv.y) * 2.2 + (n - 0.5) * 0.35);
                    float core = exp(-i.uv.y * i.uv.y * 95.0) * (1.0 - x);
                    float envelope = smoothstep(0.0, 0.12, x) * (1.0 - smoothstep(0.75, 1.0, x));
                    float3 col = lerp(i.color.rgb, float3(1, 0.88, 0.57), saturate(core * 1.8));
                    float headCore = exp(-r * r * 25.0);
                    float headShape = exp(-r * r * 6.0) * lerp(0.65, 1.15, n) + 0.18 * headCore;
                    float3 headColor = lerp(i.color.rgb, float3(1, 0.88, 0.57), saturate(headCore * 1.5));
                    float3 flame = lerp(col * (edge + core * 0.6) * envelope, headColor * headShape, i.anim.w);
                    return half4(flame * i.color.a * m * _FxParams.x, 0);
                }
                if (kind > 3.5 && kind < 4.5)
                {
                    float g = exp(-i.uv.y * i.uv.y * 28.0) * (1.0 - smoothstep(0.1, 0.5, abs(i.uv.x)));
                    return half4(i.color.rgb * g * i.color.a * m * _FxParams.x, 0);
                }
                if (kind > 1.5 && kind < 2.5)
                {
                    float z = sqrt(saturate(1.0 - r * r));
                    float3 normal = normalize(_FxRight.xyz * i.uv.x * 2.0 + _FxUp.xyz * i.uv.y * 2.0 + normalize(_WorldSpaceCameraPos - i.wpos) * z);
                    float sun = saturate(dot(normal, normalize(_SkyTideSunDir.xyz)));
                    float3 col = i.color.rgb * (0.45 + 0.8 * sun) + float3(0.15, 0.09, 0.03) * pow(sun, 16.0);
                    float a = (1.0 - smoothstep(0.8, 1.0, r)) * i.color.a * m;
                    return half4(col * a, a);   // железное ядро, не светящийся шар
                }
                if (kind > 6.5)
                {
                    float edge = (1.0 - smoothstep(0.32, 0.5, abs(i.uv.x))) * (1.0 - smoothstep(0.25, 0.5, abs(i.uv.y)));
                    float a = edge * i.color.a * m;
                    float3 col = i.color.rgb * (0.65 + 0.35 * (i.uv.y + 0.5)) + SkyTideFlashLight(i.wpos) * 0.12;
                    return half4(col * a, a);
                }
                if (kind > 5.5 && kind < 6.5)
                {
                    float n = tex2D(_SkyTideNoise, i.uv * 1.6 + i.data.ww).g;
                    float ring = 1.0 - smoothstep(0.035, 0.16, abs(r - (0.74 + (n - 0.5) * 0.1)));
                    float a = ring * lerp(0.45, 1.0, n) * i.color.a * m;
                    return half4(i.color.rgb * a, a);
                }
                // Дым: мягкий край, клочья — шум неба со сдвигом по зерну частицы.
                float2 nuv = i.uv * 0.8 + i.data.ww * float2(3.7, 5.1) + float2(i.anim.x * 0.035, -i.anim.x * 0.08);
                float n = tex2D(_SkyTideNoise, nuv).r * 0.6 + tex2D(_SkyTideNoise, nuv * 2.3 + 0.5).g * 0.4;
                float edge = r + (n - 0.5) * 0.45;
                float a = (1.0 - smoothstep(0.25, 1.0, edge)) * lerp(0.55, 1.15, n) * i.color.a * m;
                // Подсветка солнцем: край к солнцу ярче (солнце в +X мира).
                float sunSide = dot(normalize(_FxRight.xyz * i.uv.x + _FxUp.xyz * i.uv.y + 1e-4), normalize(_SkyTideSunDir.xyz));
                float volume = lerp(0.68, 1.1, n) + 0.22 * saturate(sunSide) * r;
                float3 col = i.color.rgb * _FxParams.y * volume + SkyTideFlashLight(i.wpos) * 0.32;
                if (kind < 1.5 && _FxParams.w > 0.5)
                {
                    // Нормаль клубка вместо плоского диска: холоднее в тени, теплее к солнцу.
                    float3 toCam = _WorldSpaceCameraPos - i.wpos;
                    toCam *= rsqrt(max(dot(toCam, toCam), 1e-4));
                    float3 normal = normalize(_FxRight.xyz * i.uv.x * 2.0 + _FxUp.xyz * i.uv.y * 2.0
                        + toCam * sqrt(saturate(1.0 - r * r)) + 1e-5);
                    float direct = saturate(dot(normal, normalize(_SkyTideSunDir.xyz)));
                    float3 tint = lerp(float3(0.9, 0.94, 1.04), float3(1.08, 1.02, 0.92), direct);
                    volume = lerp(0.75, 1.1, n) * (0.87 + 0.28 * direct);
                    col = i.color.rgb * _FxParams.y * volume * tint + SkyTideFlashLight(i.wpos) * 0.32;
                }
                return half4(col * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
