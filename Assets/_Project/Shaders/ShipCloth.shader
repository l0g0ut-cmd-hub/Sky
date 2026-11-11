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
        #pragma surface surf SkyCloth fullforwardshadows addshadow vertex:AnimateCloth exclude_path:deferred
        #include "UnityPBSLighting.cginc"
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap;
        half4 _Color;
        half _BumpScale, _Glossiness, _Metallic, _Translucency;
        float4 _ShipClothWind;
        float _ShipClothAmount;

        void AnimateCloth(inout appdata_full v)
        {
            float freedom = v.color.r;
            if (_ShipClothAmount <= 0 || freedom <= 0) return;
            float2 gradient = (round(v.color.gb * 255.0) - 128.0) / 64.0;
            float flag = step(0.75, v.color.a);
            float3 flow = mul((float3x3)unity_WorldToObject, _ShipClothWind.xyz);
            float speed = max(length(flow), 0.01);
            // Continuous through a turn into the wind: no sign() flip at zero crosswind.
            float pressure = 0.35 + 0.65 * abs(flow.x) / speed;
            float gust = 0.85 + 0.15 * sin(_ShipClothWind.w * 0.6);
            float amplitude = lerp(0.30,0.65,flag) * _ShipClothAmount * pressure * gust * clamp(speed/6.0,0.65,1.25);
            float2 k1 = lerp(float2(0.22,0.45),float2(0.0,1.55),flag);
            float2 k2 = lerp(float2(-0.28,0.8),float2(0.7,2.7),flag);
            float p1 = dot(v.vertex.yz,k1) - _ShipClothWind.w * lerp(1.7,5.3,flag);
            float p2 = dot(v.vertex.yz,k2) - _ShipClothWind.w * lerp(2.9,7.8,flag);
            float wave = 0.75*sin(p1) + 0.2*sin(p2);
            float2 slope = amplitude * (gradient * wave + freedom * (0.75*cos(p1)*k1 + 0.2*cos(p2)*k2));
            v.vertex.x += amplitude * freedom * wave;
            // J^-T normal and J tangent for x'=x+d(y,z). Texture normals retain their original UV basis.
            float3 n = v.normal;
            float3 t = v.tangent.xyz;
            v.normal = normalize(float3(n.x,n.y-n.x*slope.x,n.z-n.x*slope.y));
            v.tangent.xyz = normalize(float3(t.x+slope.x*t.y+slope.y*t.z,t.y,t.z));
        }

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
