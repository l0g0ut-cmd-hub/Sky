// Прицельная стрелка борта в духе Black Flag: полупрозрачная объёмная лента по траектории залпа с наконечником.
// uv.x — поперёк (0..1, края ярче), uv.y — вдоль (0 у борта .. 1 у наконечника), бегущие шевроны по длине.
Shader "SkyTide/AimArrow"
{
    Properties { _Color ("Color", Color) = (1, 0.72, 0.3, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha   // было аддитивно — на ярких закатных облаках лента пропадала
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float camDist : TEXCOORD1; };
            v2f vert (a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; o.camDist = distance(mul(unity_ObjectToWorld, v.vertex).xyz, _WorldSpaceCameraPos); return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float across = abs(i.uv.x * 2.0 - 1.0);              // 0 в центре, 1 у края
                float edge = smoothstep(0.72, 0.98, across);          // светлый кант
                float body = 0.30 + 0.30 * (1.0 - across * across);   // полупрозрачное «тело» ленты
                float fadeIn = smoothstep(0.0, 0.18, i.uv.y);         // у борта растворяется
                float chev = frac(i.uv.y * 9.0 - abs(i.uv.x - 0.5) * 1.6 - _Time.y * 0.9);
                float stripes = 0.75 + 0.25 * smoothstep(0.55, 0.62, chev) * (1.0 - smoothstep(0.85, 0.92, chev));
                float nearCam = saturate((i.camDist - 12.0) / 45.0);   // вблизи камеры лента не заливает экран
                float rim = smoothstep(0.90, 1.0, across);           // тёмная обводка снаружи канта — контраст на светлом небе
                float a = (body * stripes + edge * 0.95) * fadeIn * nearCam;
                float3 rgb = lerp(_Color.rgb * i.color.rgb * (1.15 + edge * 0.5), float3(0.18, 0.08, 0.02), rim * 0.7);
                return fixed4(saturate(rgb), saturate(a * min(1.0, i.color.a * 1.15) * _Color.a));
            }
            ENDCG
        }
    }
}
