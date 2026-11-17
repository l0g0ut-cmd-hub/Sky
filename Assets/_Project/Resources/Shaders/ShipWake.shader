// След за кормой (владелец 17.11: «только красиво»). Лента TrailRenderer: uv.x — вдоль следа (0 у кормы .. 1 хвост),
// uv.y — поперёк. Мягкая пелена пара: края и хвост тают, внутри медленно плывущие разводы (не клубы — прежний след
// из отдельных клубов читался как пузыри и был убран 15.11). Вблизи камеры и вдали гаснет.
Shader "SkyTide/ShipWake"
{
    Properties { _Color ("Color", Color) = (0.93, 0.86, 0.84, 0.3) }
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wp : TEXCOORD1; };
            v2f vert (a2v v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float across = abs(i.uv.y * 2.0 - 1.0);
                float edge = 1.0 - smoothstep(0.25, 1.0, across);           // мягкие края ленты
                // Разводы привязаны к миру (не ползут вместе с лентой) и медленно плывут.
                float2 q = i.wp.xz * 0.045 + float2(_Time.y * 0.05, -_Time.y * 0.03);
                float n = noise(q) * 0.6 + noise(q * 2.3 + 7.1) * 0.4;
                float wisp = lerp(0.55, 1.0, n);
                float d = distance(i.wp, _WorldSpaceCameraPos);
                float fade = saturate((d - 12.0) / 40.0) * (1.0 - saturate((d - 900.0) / 600.0));
                float a = i.color.a * _Color.a * edge * wisp * fade;
                return fixed4(_Color.rgb * i.color.rgb, a);
            }
            ENDCG
        }
    }
}
