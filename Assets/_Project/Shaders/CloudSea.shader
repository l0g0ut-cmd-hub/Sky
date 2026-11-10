// Море облаков: два слоя одной текстуры разного масштаба текут в разные стороны и слегка искажаются шумом,
// к солнцу поверхность светится золотом у горизонта, от солнца уходит в сиреневую тень,
// вдали бесшовно переходит в нарисованную панораму. UV берутся из мировых координат —
// плоскость может ездить за камерой, и море бесконечно.
// Один шейдер для плотного нижнего слоя (Opaque) и полупрозрачного верхнего (блендинг задаёт материал).
Shader "SkyTide/CloudSea"
{
    Properties
    {
        _MainTex ("Clouds (RGB, A = coverage)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TileSize ("Tile size (m)", Float) = 420
        _Scale2 ("Second layer scale", Float) = 0.41
        _Flow ("Flow (xy layer 1, zw layer 2), tiles/s", Vector) = (0.0035, 0.0018, -0.0026, 0.0041)
        _Distort ("Flow distortion", Float) = 0.025
        _ShadowTint ("Tint away from sun", Color) = (0.72, 0.62, 0.82, 1)
        _SunGlow ("Sun glow", Color) = (1.0, 0.55, 0.22, 1)
        _SunGlowPower ("Sun glow focus", Float) = 5
        _AlphaMul ("Opacity", Float) = 1
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SkyTide.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _TileSize;
            float _Scale2;
            float4 _Flow;
            float _Distort;
            float4 _ShadowTint;
            float4 _SunGlow;
            float _SunGlowPower;
            float _AlphaMul;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.worldPos.xz / _TileSize;
                float t = _Time.y;
                float2 d = (tex2D(_SkyTideNoise, uv * 0.35 + t * 0.0025).rg - 0.5) * _Distort;
                float4 a = tex2D(_MainTex, uv + _Flow.xy * t + d);
                float4 b = tex2D(_MainTex, uv * _Scale2 + _Flow.zw * t - d);
                // Цвет смешиваем с весом прозрачности: под прозрачными местами текстуры цвет не определён.
                float wa = a.a;
                float wb = b.a * 0.8;
                float4 c;
                c.rgb = (a.rgb * wa + b.rgb * wb) / max(wa + wb, 1e-3);
                c.a = saturate(max(a.a, b.a) * 0.75 + a.a * b.a * 0.5);

                float3 view = i.worldPos - _WorldSpaceCameraPos;
                float dist = length(view);
                float3 vdir = view / max(dist, 1e-3);
                float sunDot = SkyTideSunDot(vdir);
                float graze = 1.0 - saturate(-vdir.y * 3.0);   // 1 — у горизонта, 0 — смотрим вниз

                float3 col = c.rgb * _Color.rgb * lerp(_ShadowTint.rgb, float3(1, 1, 1), saturate(sunDot * 0.5 + 0.5));
                col += _SunGlow.rgb * pow(saturate(sunDot), _SunGlowPower) * graze * (0.35 + 0.65 * Luminance(c.rgb));

                col = lerp(col, SkyTidePanorama(vdir), SkyTideAerial(dist));
                return fixed4(col, c.a * _AlphaMul * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
