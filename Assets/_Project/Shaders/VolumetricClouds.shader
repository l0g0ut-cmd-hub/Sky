// Объёмные облака (встроенный рендер, OnRenderImage).
// Проход 0 — марш луча в пониженном разрешении: слой облаков между _Layer.x и _Layer.y метров,
//   форма = покрытие (2D-шум, едет по ветру) × профиль по высоте × 3D Perlin-Worley, края съедает Worley-детализация.
//   Свет: несколько шагов к солнцу (Beer + «порошок» на краях), двойная фаза Хеньи-Гринштейна —
//   серебряный край и сияние против солнца; заполняющий свет — от неба сверху и от моря облаков снизу.
//   Луч останавливается на глубине сцены (корабль заслоняет облака за ним). Вдали — растворение в панораме.
//   Результат: rgb — свет облаков, a — пропускание.
// Проход 1 — сведение с кадром: цвет = кадр × пропускание + свет облаков.
Shader "Hidden/SkyTide/VolumetricClouds"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "SkyTide.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _CloudTex;
    sampler2D _LowDepthTex;
    float4 _LowRes;         // xy размер RT облаков, zw 1/размер
    sampler3D _Noise3D;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float4x4 _InvProj;
    float4x4 _CamToWorld;
    float4 _Layer;          // x низ, y верх (м), z дальность марша (м), w шагов
    float4 _Shape;          // x масштаб формы (м), y масштаб детали (м), z покрытие 0..1, w масштаб покрытия (м)
    float4 _DensityParams;  // x плотность, y сила эрозии, z поглощение света, w «порошок»
    float4 _Wind;           // xyz ветер (м/с), w время
    float4 _SunColor;
    float4 _AmbientTop;
    float4 _AmbientBottom;
    float4 _Phase;          // x g вперёд, y g назад, z смесь, w сила серебряного края
    float _Debug;           // 0 — норма; 1 — поля (R погода, G форма, B плотность) в точке 200 м по лучу; 2 — только облака; 3 — пропускание

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert (appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    float Remap(float v, float lo, float hi, float nlo, float nhi)
    {
        return nlo + (v - lo) / max(hi - lo, 1e-4) * (nhi - nlo);
    }

    float HG(float c, float g)
    {
        float g2 = g * g;
        return (1.0 - g2) / (12.566 * pow(max(1.0 + g2 - 2.0 * g * c, 1e-3), 1.5));
    }

    // Плотность (по рекомендациям GPT): погода → отдельные массы; высота вершин — своя для каждой массы;
    // форма — порог по 3D-шуму; эрозия работает в переходной зоне, а не гаснет в плотных местах.
    float Density(float3 p, bool detail)
    {
        float3 wp = p + _Wind.xyz * _Wind.w;
        float2 weatherUV = wp.xz / _Shape.w;
        float weather = tex2Dlod(_SkyTideNoise, float4(weatherUV, 0, 0)).r * 0.7
                      + tex2Dlod(_SkyTideNoise, float4(weatherUV / 0.365 + 0.37, 0, 0)).g * 0.3;
        float cover = smoothstep(0.54 - _Shape.z * 0.20, 0.72 - _Shape.z * 0.08, weather);
        if (cover < 0.03) return 0.0;

        float heightNoise = tex2Dlod(_SkyTideNoise, float4(weatherUV / 0.6 + 0.71, 0, 0)).g;
        float tower = smoothstep(0.48, 0.73, heightNoise) * smoothstep(0.28, 0.85, cover);
        float cloudTop = lerp(38.0, 190.0, tower);   // низкий ковёр и редкие высокие башни
        if (p.y < _Layer.x || p.y > cloudTop) return 0.0;

        float h = saturate((p.y - _Layer.x) / max(cloudTop - _Layer.x, 1.0));
        float profile = smoothstep(0.0, 0.14, h) * (1.0 - smoothstep(0.68, 1.0, h));

        float3 qRot = float3(wp.z * 0.8 + wp.x * 0.6, wp.y, wp.x * 0.8 - wp.z * 0.6);
        float base = tex3Dlod(_Noise3D, float4(wp / _Shape.x, 0)).r * 0.8
                   + tex3Dlod(_Noise3D, float4(qRot / (_Shape.x * 2.0) + 0.31, 0)).r * 0.2;
        float mass = saturate((base - lerp(0.58, 0.36, cover)) / 0.25) * profile;
        if (mass <= 0.001) return 0.0;

        if (detail)
        {
            float3 dq = (wp + float3(0, _Wind.w * 1.5, 0)) / _Shape.y;   // детали медленно клубятся вверх
            float4 n = tex3Dlod(_Noise3D, float4(dq, 0));
            float erosionNoise = n.g * 0.7 + n.b * 0.3;
            float edgeFactor = 1.0 - smoothstep(0.45, 0.85, mass);
            mass -= erosionNoise * _DensityParams.y * 0.75 * edgeFactor;
        }
        mass = smoothstep(0.08, 0.55, mass);
        return mass * _DensityParams.x * saturate(cover * 1.15);
    }

    // Оптическая толщина по пути к солнцу (5 проб, отрезки растут ×1.85).
    float LightMarch(float3 p, float3 sunDir)
    {
        float opticalDepth = 0.0;
        float distanceToSample = 0.0;
        float segment = 12.0;
        for (int k = 0; k < 5; k++)
        {
            float3 samplePos = p + sunDir * (distanceToSample + segment * 0.5);
            opticalDepth += Density(samplePos, false) * segment;
            distanceToSample += segment;
            segment *= 1.85;
            if (opticalDepth > 5.0) break;
        }
        return opticalDepth;
    }

    // Белый шум для смещения начала шагов (убирает ступени; зерно размывается апскейлом).
    float Hash(float2 px)
    {
        float3 p3 = frac(float3(px.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float4 fragMarch (v2f i) : SV_Target
    {
        float4 view = mul(_InvProj, float4(i.uv * 2.0 - 1.0, 1.0, 1.0));
        view.xyz /= view.w;
        float3 rd = normalize(mul((float3x3)_CamToWorld, view.xyz));
        float3 ro = _WorldSpaceCameraPos;
        if (_Debug > 0.5 && _Debug < 1.5)
        {
            float3 pp = ro + rd * 200.0;
            float3 wpp = pp + _Wind.xyz * _Wind.w;
            float wq = tex2Dlod(_SkyTideNoise, float4(wpp.xz / _Shape.w, 0, 0)).r;
            float bq = tex3Dlod(_Noise3D, float4(wpp / _Shape.x, 0)).r;
            return float4(wq, bq, Density(pp, true) / max(_DensityParams.x, 1e-5), 0);
        }

        // Дистанция до непрозрачной сцены вдоль луча.
        float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        float sceneDist = 1e7;
        if (Linear01Depth(rawDepth) < 0.999)
        {
            float3 fwd = mul((float3x3)_CamToWorld, float3(0, 0, -1));
            sceneDist = LinearEyeDepth(rawDepth) / max(dot(rd, fwd), 1e-3);
        }

        // Пересечение с горизонтальным слоем облаков.
        float tA, tB;
        if (abs(rd.y) < 1e-4)
        {
            if (ro.y < _Layer.x || ro.y > _Layer.y) return float4(0, 0, 0, 1);
            tA = 0; tB = _Layer.z;
        }
        else
        {
            float t0 = (_Layer.x - ro.y) / rd.y;
            float t1 = (_Layer.y - ro.y) / rd.y;
            tA = max(0, min(t0, t1));
            tB = max(t0, t1);
        }
        tB = min(tB, min(sceneDist, _Layer.z));
        if (tB <= tA) return float4(0, 0, 0, 1);

        int steps = (int)_Layer.w;
        float stepLen = (tB - tA) / steps;
        float jitter = Hash(floor(i.uv * _LowRes.xy));
        float t = tA + stepLen * (0.5 + 0.2 * (jitter - 0.5));

        float3 sunDir = normalize(_SkyTideSunDir.xyz);
        float cosT = dot(rd, sunDir);
        float phase = min(lerp(HG(cosT, _Phase.y), HG(cosT, _Phase.x), saturate(_Phase.z)) * 12.566371, 2.3);

        float3 light = 0;
        float trans = 1;
        float distAcc = 0;
        float weightAcc = 0;
        [loop]
        for (int s = 0; s < steps; s++)
        {
            float3 p = ro + rd * t;
            float d = Density(p, t < 700.0);
            // Растворение у камеры и перед концом дальности марша.
            d *= smoothstep(25.0, 105.0, t) * (1.0 - smoothstep(_Layer.z * 0.75, _Layer.z, t));
            if (d > 0.0003)
            {
                float sunT = exp(-LightMarch(p, sunDir) * _DensityParams.z);
                // Кайма против солнца — только на тонких краях.
                float edge = pow(1.0 - saturate(d / max(_DensityParams.x * 0.7, 1e-5)), 1.6);
                float rim = edge * pow(saturate(cosT), 4.0);
                float sunLight = sunT * phase * (1.0 + _Phase.w * rim);
                // Дешёвое затенение фонового света в плотной середине.
                float ambientFactor = lerp(0.12, 0.8, exp(-d * 90.0));
                float hh = saturate((p.y - _Layer.x) / max(_Layer.y - _Layer.x, 1.0));
                float3 ambient = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, hh) * ambientFactor;
                const float albedo = 0.86;
                float3 source = _SunColor.rgb * sunLight + ambient;
                float stepTrans = exp(-d * stepLen);
                float stepOpacity = 1.0 - stepTrans;
                light += trans * source * albedo * stepOpacity;
                float wgt = trans * stepOpacity;
                distAcc += t * wgt;
                weightAcc += wgt;
                trans *= stepTrans;
                if (trans < 0.04) { trans = 0; break; }
            }
            t += stepLen;
        }

        // Мягкая воздушная перспектива: не заменять форму облаков цветом неба.
        float meanDist = weightAcc > 1e-4 ? distAcc / weightAcc : tB;
        light = lerp(light, SkyTidePanorama(rd) * (1.0 - trans), 0.2 * saturate(SkyTideAerial(meanDist)));
        return float4(light, trans);
    }

    float SceneDepthForClouds(float2 uv)
    {
        float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
        if (Linear01Depth(raw) >= 0.999) return 4096.0;
        return min(LinearEyeDepth(raw), 4096.0);
    }

    float4 fragLowDepth (v2f i) : SV_Target
    {
        return float4(SceneDepthForClouds(i.uv), 0, 0, 1);
    }

    // Сведение с учётом глубины: облака не «перетекают» через силуэт корабля.
    half4 fragComposite (v2f i) : SV_Target
    {
        half4 scene = tex2D(_MainTex, i.uv);
        float2 uv = i.uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
        #endif
        float2 lowSize = _LowRes.xy;
        float2 pixelPos = uv * lowSize - 0.5;
        float2 basePixel = floor(pixelPos);
        float2 f = frac(pixelPos);
        float referenceDepth = SceneDepthForClouds(uv);
        float4 result = 0;
        float totalWeight = 0;
        float nearestDifference = 1e20;
        float4 nearestCloud = float4(0, 0, 0, 1);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                float2 pixel = clamp(basePixel + float2(x, y), float2(0, 0), lowSize - 1.0);
                float2 sampleUV = (pixel + 0.5) / lowSize;
                float4 cloud = tex2D(_CloudTex, sampleUV);
                float diff = abs(tex2D(_LowDepthTex, sampleUV).r - referenceDepth);
                float spatial = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y);
                float w = spatial * exp(-diff / max(3.0, referenceDepth * 0.02));
                result += cloud * w;
                totalWeight += w;
                if (diff < nearestDifference) { nearestDifference = diff; nearestCloud = cloud; }
            }
        }
        float4 c = totalWeight > 1e-5 ? result / totalWeight : nearestCloud;
        if (_Debug > 0.5 && _Debug < 2.5) return half4(c.rgb, 1);
        if (_Debug > 2.5) return half4(c.aaa, 1);
        return half4(scene.rgb * c.a + c.rgb, scene.a);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragMarch
            #pragma target 3.5
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            #pragma target 3.5
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragLowDepth
            #pragma target 3.5
            ENDCG }
    }
    Fallback Off
}
