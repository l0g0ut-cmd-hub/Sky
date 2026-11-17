// Луч маяка (владелец 17.11: «маяк не светит», выбран вариант 3 — вращающийся луч).
// Конус: uv.y — вдоль луча 0 (фонарь) .. 1 (конец), uv.x — по окружности. Ярче у фонаря и по оси, мягко гаснет к концу
// и по краям; лёгкая пыль в луче. Вблизи камеры не заливает экран.
Shader "SkyTide/LighthouseBeam"
{
    Properties { _Color ("Color", Color) = (1, 0.82, 0.5, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent+6" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float camDist : TEXCOORD1; float rim : TEXCOORD2; float side : TEXCOORD3; };
            v2f vert (a2v v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.camDist = distance(wp, _WorldSpaceCameraPos);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 vd = normalize(_WorldSpaceCameraPos - wp);
                o.rim = abs(dot(n, vd));          // поверхность конуса «лицом» к камере — середина луча, ребром — край
                float3 axis = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                float al = abs(dot(axis, vd));    // смотрим вдоль луча — гасим (снимок 17.11: белая полоса на пол-экрана)
                o.side = 1.0 - al * al * al;
                return o;
            }
            float hash(float n) { return frac(sin(n) * 43758.5453); }
            fixed4 frag (v2f i) : SV_Target
            {
                float along = i.uv.y;
                float fall = pow(saturate(1.0 - along), 2.2) * smoothstep(0.0, 0.03, along);   // гаснет к концу, мягкое начало у линзы
                float core = pow(saturate(i.rim), 4.0);                        // мягкие края, ярче по оси
                float dust = 0.85 + 0.15 * hash(floor(along * 40.0 + _Time.y * 2.0) + floor(i.uv.x * 12.0));
                float nearCam = saturate((i.camDist - 15.0) / 80.0);
                float a = fall * core * dust * nearCam * i.side * 0.45 * _Color.a;
                return fixed4(_Color.rgb, saturate(a));
            }
            ENDCG
        }
    }
}
