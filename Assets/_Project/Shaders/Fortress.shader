// Небесная крепость: Standard BRDF + растворение низа в море облаков и воздушная дымка на расстоянии.
// Низ модели не обрывается ровной кромкой: ниже _DissolveTop цвет уходит в дымку горизонта этого азимута
// (та же SkyTideAerialColor, что у дальних облаков), объёмные облака дорисовывают остальное по глубине сцены.
Shader "SkyTide/Fortress"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _DetailScale ("Detail tiles per metre", Float) = 0.11
        _DetailStrength ("Detail strength", Range(0,1)) = 0.8
        _DetailFar ("Detail fade distance (m)", Float) = 380
        _Glossiness ("Smoothness", Range(0,1)) = 0.18
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionMap ("Emission", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission color", Color) = (0,0,0,1)
        _DissolveTop ("Dissolve top (world Y)", Float) = 45
        _DissolveRange ("Dissolve range (m)", Float) = 45
        _AerialStrength ("Aerial strength", Range(0,1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert finalcolor:Haze exclude_path:deferred
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "SkyTide.cginc"

        sampler2D _MainTex, _EmissionMap;
        half4 _Color, _EmissionColor;
        half _Glossiness, _Metallic;
        float _DissolveTop, _DissolveRange, _AerialStrength, _DetailScale, _DetailStrength, _DetailFar;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
            float3 hazeColor;   // дымка горизонта в азимуте вершины — по вершинам (3 выборки панорамы на пиксель дорого)
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.hazeColor = SkyTideAerialColor(normalize(wp - _WorldSpaceCameraPos));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            // Фактура вблизи: у модели ~25 см на тексель и нет карты нормалей (из плоского альбедо Meshy рельеф не
            // извлечь — проверено), поэтому мелкая неровность камня/дерева — шум в мировых координатах по трём осям.
            float3 gn = normalize(WorldNormalVector(IN, float3(0, 0, 1)));
            float fade = saturate((_DetailFar - distance(IN.worldPos, _WorldSpaceCameraPos)) / (_DetailFar * 0.6)) * _DetailStrength;
            float3 tw = gn * gn; tw /= (tw.x + tw.y + tw.z);
            float3 dp = IN.worldPos * _DetailScale;
            float2 nx = tex2D(_SkyTideNoise, dp.zy).rg, ny = tex2D(_SkyTideNoise, dp.xz).rg, nz = tex2D(_SkyTideNoise, dp.xy).rg;
            float lum = nx.r * tw.x + ny.r * tw.y + nz.r * tw.z;
            albedo.rgb *= lerp(1.0, 0.8 + 0.4 * lum, fade);
            float3 bump = float3(0, nx.r - 0.5, nx.g - 0.5) * tw.x + float3(ny.r - 0.5, 0, ny.g - 0.5) * tw.y + float3(nz.r - 0.5, nz.g - 0.5, 0) * tw.z;
            float3 wn = normalize(gn + bump * (fade * 0.9));
            float3 tanW = WorldNormalVector(IN, float3(1, 0, 0)), bitW = WorldNormalVector(IN, float3(0, 1, 0));
            o.Albedo = albedo.rgb;
            o.Normal = normalize(float3(dot(wn, tanW), dot(wn, bitW), dot(wn, gn)));
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            o.Alpha = 1;
        }

        void Haze(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            float3 rd = IN.worldPos - _WorldSpaceCameraPos;
            float dist = length(rd);
            float sink = saturate((_DissolveTop - IN.worldPos.y) / max(_DissolveRange, 1.0));
            sink = sink * sink * (3.0 - 2.0 * sink);
            float haze = max(sink, SkyTideAerialAmount(_AerialStrength, dist));
            #ifdef UNITY_PASS_FORWARDADD
                color.rgb *= 1.0 - haze;
            #else
                color.rgb = lerp(color.rgb, IN.hazeColor, haze);
            #endif
        }
        ENDCG
    }
    FallBack "Standard"
}
