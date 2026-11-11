// Лён, заплаты и вымпел: двухсторонний Standard BRDF с тонким просветом от света за тканью.
// Просвет использует attenuated gi.light.color, поэтому сохраняет тени мачт/баллона.
Shader "SkyTide/ShipCloth"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Range(0,2)) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Translucency ("Transmission", Range(0,0.5)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf SkyCloth fullforwardshadows addshadow exclude_path:deferred
        #include "UnityPBSLighting.cginc"
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap;
        half4 _Color;
        half _BumpScale, _Glossiness, _Metallic, _Translucency;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float facing : VFACE;
        };

        inline half4 LightingSkyCloth(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            half4 c = LightingStandard(s, viewDir, gi);
            half back = saturate(-dot(s.Normal, gi.light.dir));
            c.rgb += s.Albedo * gi.light.color * (_Translucency * back);
            return c;
        }

        inline void LightingSkyCloth_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = albedo.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale)
                * (IN.facing >= 0 ? 1.0 : -1.0);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Standard"
}
