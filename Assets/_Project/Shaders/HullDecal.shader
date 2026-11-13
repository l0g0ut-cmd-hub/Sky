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
                o.pos = UnityObjectToClipPos(v.vertex);
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
