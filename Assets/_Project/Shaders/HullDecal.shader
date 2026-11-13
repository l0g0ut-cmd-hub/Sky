// Hull battle-damage decals (holes, soot). Alpha-blended over the hull, lit by the main light + ambient SH,
// No Unity fog: in CI frames it painted the decals with the full fog colour (pink patches); the ship is close anyway.
// with a small depth bias so projected quads never z-fight with the planks they sit on.
Shader "Hidden/SkyTide/HullDecal"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Geometry+60" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB          // альфа кадра — маска для облаков/пост-обработки, наклейка её не трогает
            ZWrite Off
            Offset -2, -2
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            struct a2v { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half3 light:TEXCOORD1; half alpha:TEXCOORD2; };
            v2f vert(a2v v)
            {
                v2f o;
                // Дальше 110 м корпус мишени — облегчённая копия (ShipLod), её поверхность отходит от полной на сантиметры,
                // а наклейка спроецирована на полную. Сдвиг к камере растёт с расстоянием (ближе 67 м — нет, на 110 м — 13 см, вдали до 0.5 м):
                // наклейка не тонет в копии и не видна «парящей» — сдвиг вдоль луча зрения не меняет её места на экране.
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 toCam = _WorldSpaceCameraPos - wp;
                float camDist = length(toCam);
                wp += toCam / max(camDist, 1e-3) * clamp(camDist * 0.003 - 0.2, 0.0, 0.5);
                o.pos = UnityWorldToClipPos(wp);
                o.uv = v.uv;
                half3 n = UnityObjectToWorldNormal(v.normal);
                half ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                o.light = _LightColor0.rgb * ndl + ShadeSH9(half4(n, 1));
                o.alpha = v.color.a;
                o.light *= v.color.rgb;   // тон слоя (щепа темнее)
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                c.rgb *= i.light;
                c.a *= i.alpha;
                return c;
            }
            ENDCG
        }
    }
}
