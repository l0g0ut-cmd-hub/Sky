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
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float camDist : TEXCOORD1; float rim : TEXCOORD2; };
            v2f vert (a2v v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.camDist = distance(wp, _WorldSpaceCameraPos);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 vd = normalize(_WorldSpaceCameraPos - wp);
                o.rim = abs(dot(n, vd));          // поверхность конуса «лицом» к камере — середина луча, ребром — край
                return o;
            }
            float hash(float n) { return frac(sin(n) * 43758.5453); }
            fixed4 frag (v2f i) : SV_Target
            {
                float along = i.uv.y;
                float fall = pow(saturate(1.0 - along), 1.6);                  // гаснет к концу
                float core = pow(saturate(i.rim), 2.0);                        // ярче по оси луча
                float dust = 0.85 + 0.15 * hash(floor(along * 40.0 + _Time.y * 2.0) + floor(i.uv.x * 12.0));
                float nearCam = saturate((i.camDist - 15.0) / 80.0);
                float a = fall * core * dust * nearCam * _Color.a;
                return fixed4(_Color.rgb, saturate(a));
            }
            ENDCG
        }
    }
}
