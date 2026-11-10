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

    float Density(float3 p, bool detail)
    {
        float h = saturate((p.y - _Layer.x) / (_Layer.y - _Layer.x));
        float3 wp = p + _Wind.xyz * _Wind.w;
        // Погода: где есть облака (два масштаба — без заметного повтора).
        float w = tex2Dlod(_SkyTideNoise, float4(wp.xz / _Shape.w, 0, 0)).r * 0.7
                + tex2Dlod(_SkyTideNoise, float4(wp.xz / (_Shape.w * 0.365) + 0.37, 0, 0)).g * 0.3;
        float cover = smoothstep(0.62 - _Shape.z * 0.25, 0.92 - _Shape.z * 0.2, w);
        if (cover <= 0.0) return 0.0;
        // Высота вершин: низкий облачный ковёр над морем, и лишь местами башни до высоты полёта и выше.
        float tall = smoothstep(0.5, 0.8, tex2Dlod(_SkyTideNoise, float4(wp.xz / (_Shape.w * 0.6) + 0.71, 0, 0)).g);
        float top = 0.24 + 0.76 * cover * tall;
        float profile = saturate(h / 0.06) * saturate((top - h) / (0.5 * top));
        // Форма: 3D Perlin-Worley в двух масштабах (второй повёрнут) — против повтора тайла.
        float3 q2 = float3(wp.z * 0.8 + wp.x * 0.6, wp.y, wp.x * 0.8 - wp.z * 0.6);
        float base = tex3Dlod(_Noise3D, float4(wp / _Shape.x, 0)).r * 0.6
                   + tex3Dlod(_Noise3D, float4(q2 / (_Shape.x * 2.45) + 0.31, 0)).r * 0.4;
        float d = saturate((base * profile - (1.0 - 0.6 * cover)) * 5.0);
        if (detail && d > 0.0)
        {
            float3 dp = (wp + float3(0, _Wind.w * 1.5, 0)) / _Shape.y;   // детали медленно клубятся вверх
            float4 n = tex3Dlod(_Noise3D, float4(dp, 0));
            float det = n.g * 0.65 + n.b * 0.35;
            float edge = lerp(det, 1.0 - det, saturate(h * 6.0));        // у дна — пушистые клочья, выше — завитки
            d = saturate(d - edge * _DensityParams.y * (1.0 - d));
        }
        return d * _DensityParams.x;
    }

    float LightMarch(float3 p, float3 sunDir)
    {
        float sum = 0;
        float stepLen = 18.0;
        [unroll]
        for (int k = 0; k < 5; k++)
        {
            p += sunDir * stepLen;
            sum += Density(p, false) * stepLen;
            stepLen *= 1.8;
        }
        return sum;
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
        float t = tA + stepLen * Hash(floor(i.uv * _ScreenParams.xy));

        float3 sunDir = normalize(_SkyTideSunDir.xyz);
        float cosT = dot(rd, sunDir);
        float phase = lerp(HG(cosT, _Phase.y), HG(cosT, _Phase.x), _Phase.z) * 12.566;
        phase += pow(saturate(cosT), 12.0) * _Phase.w;   // яркий серебряный край против солнца

        float3 light = 0;
        float trans = 1;
        float distAcc = 0;
        float wAcc = 0;
        [loop]
        for (int s = 0; s < steps; s++)
        {
            float3 p = ro + rd * t;
            float d = Density(p, true);
            if (d > 0.002)
            {
                float od = LightMarch(p, sunDir) * _DensityParams.z;
                float beer = exp(-od);
                float powder = 1.0 - exp(-d * 2.0 * _DensityParams.w);
                float sunL = beer * lerp(1.0, powder, 0.55) * phase;
                float h = saturate((p.y - _Layer.x) / (_Layer.y - _Layer.x));
                float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, h);
                float3 lum = _SunColor.rgb * sunL + amb;

                float stepTrans = exp(-d * stepLen);
                // Энергосберегающее интегрирование (Hillaire): свет внутри шага.
                float3 integ = lum * (1.0 - stepTrans);
                light += trans * integ;
                distAcc += t * trans * (1.0 - stepTrans);
                wAcc += trans * (1.0 - stepTrans);
                trans *= stepTrans;
                if (trans < 0.02) { trans = 0; break; }
            }
            t += stepLen;
        }

        // Воздушная перспектива: дальние облака растворяются в небе за ними.
        float meanDist = wAcc > 1e-4 ? distAcc / wAcc : tB;
        float fade = SkyTideAerial(meanDist);
        light = lerp(light, SkyTidePanorama(rd) * (1.0 - trans), fade);
        return float4(light, trans);
    }

    half4 fragComposite (v2f i) : SV_Target
    {
        half4 scene = tex2D(_MainTex, i.uv);
        float2 cuv = i.uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) cuv.y = 1 - cuv.y;
        #endif
        half4 c = tex2D(_CloudTex, cuv);
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
            ENDCG }
    }
    Fallback Off
}
