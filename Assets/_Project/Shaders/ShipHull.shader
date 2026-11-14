// Корпус корабля: тот же Standard BRDF и те же карты/ключевые слова, что у встроенного Standard
// (_NORMALMAP, _METALLICGLOSSMAP, _EMISSION), плюс контровой свет заката (золотая кромка со стороны солнца)
// и свет вспышек выстрелов на обшивке. Раньше корпус был на встроенном Standard и против солнца читался
// ровным тёмным силуэтом.
Shader "SkyTide/ShipHull"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness scale", Range(0,1)) = 1
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0
        _MetallicGlossMap ("Metallic (R) smoothness (A)", 2D) = "white" {}
        _BumpScale ("Normal scale", Float) = 1
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        [HDR] _EmissionColor ("Emission color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _RimStrength ("Rim strength", Range(0,3)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow exclude_path:deferred
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP
        #pragma shader_feature_local _EMISSION
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "SkyTide.cginc"

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _EmissionMap;
        half4 _Color, _EmissionColor;
        half _Glossiness, _GlossMapScale, _Metallic, _BumpScale, _RimStrength;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = albedo.rgb;
            #ifdef _NORMALMAP
                o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            #else
                o.Normal = half3(0, 0, 1);
            #endif
            #ifdef _METALLICGLOSSMAP
                half4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
                o.Metallic = mg.r;
                o.Smoothness = mg.a * _GlossMapScale;
            #else
                o.Metallic = _Metallic;
                o.Smoothness = _Glossiness;
            #endif
            half3 glow = half3(0, 0, 0);
            #ifdef _EMISSION
                glow = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif
            float3 wn = WorldNormalVector(IN, o.Normal);
            glow += SkyTideRimLight(wn, IN.worldPos) * _RimStrength;
            glow += albedo.rgb * SkyTideFlashLight(IN.worldPos) * 0.35;
            o.Emission = glow;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
