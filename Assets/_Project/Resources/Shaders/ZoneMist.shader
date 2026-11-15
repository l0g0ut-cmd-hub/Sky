// Граница безопасной зоны крепости: светящаяся стена тумана (вариант 4, зелёный; при тревоге — красный).
// uv.x — вдоль кольца (метры/100), uv.y — высота 0 (у облаков) .. 1 (верх). Две волны шума — дымка «дышит»,
// у самого низа — тонкая яркая нить. Вблизи камеры растворяется, издалека бледнеет к горизонту.
Shader "SkyTide/ZoneMist"
{
    Properties { _Color ("Color", Color) = (0.45, 1, 0.6, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float camDist : TEXCOORD1; };
            v2f vert (a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.camDist = distance(mul(unity_ObjectToWorld, v.vertex).xyz, _WorldSpaceCameraPos); return o; }
            float hash(float n) { return frac(sin(n) * 43758.5453); }
            float noise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(hash(i), hash(i + 1.0), f); }
            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float x = i.uv.x;
                // Неровный верх дымки: высота «языков» плавает вдоль кольца и во времени.
                float top = 0.45 + 0.35 * noise1(x * 3.0 + t * 0.15) + 0.2 * noise1(x * 9.0 - t * 0.4);
                float h = i.uv.y / max(top, 0.05);
                float body = saturate(1.0 - h);
                body = body * body * (0.6 + 0.4 * noise1(x * 14.0 + i.uv.y * 3.0 + t * 0.6));
                float thread = exp(-i.uv.y * 38.0) * 1.6;                       // тонкая нить у облаков
                float nearCam = saturate((i.camDist - 25.0) / 120.0);         // вблизи не заливает экран
                float farFade = 1.0 - saturate((i.camDist - 2600.0) / 1800.0);
                float a = (body * 0.55 + thread) * nearCam * farFade * _Color.a;
                return fixed4(_Color.rgb * (0.8 + thread * 0.6), saturate(a));
            }
            ENDCG
        }
    }
}
