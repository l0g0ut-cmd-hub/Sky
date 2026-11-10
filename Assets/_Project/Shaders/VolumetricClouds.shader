// Объёмные облака — «героические» кучевые башни (встроенный рендер, OnRenderImage).
// Форма: каждая башня собрана из 28 сфер шаблона (широкое плоское основание, средний ярус, 3–5 голов наверху);
//   поле пухлости = max(1 - d/r) по сферам, поверхность лепит 3D-шум (крупные лопасти + клубы), края разъедает
//   Worley-детализация. Шаблоны и параметры подобраны в офлайн-лаборатории (tools/cloudlab).
// Свет: 6 шагов к солнцу; цвет солнца по глубине — от золота на открытых вершинах до красно-оранжевого в полутени;
//   розовое многократное рассеяние; фиолетовое заполнение, гаснущее в глубине; кайма против солнца на тонких краях.
// Марш идёт только по отрезку, где луч пересекает габариты башен; остановка по глубине сцены; растворение у камеры.
// Форма башен запечена в 3D-текстуру (_TowerField) при старте — одна выборка вместо 28 сфер.
// Дымка у моря облаков — отдельный дешёвый проход по 2D-погоде.
// Проходы: 0 — марш (1/N разрешения), 1 — сведение с учётом глубины, 2 — глубина в низком разрешении, 3 — размытие.
Shader "Hidden/SkyTide/VolumetricClouds"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "SkyTide.cginc"

    #define MAX_TOWERS 8
    #define PUFFS 28

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _CloudTex;
    sampler2D _LowDepthTex;
    sampler3D _Noise3D;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float4x4 _InvProj;
    float4x4 _CamToWorld;
    float4 _LowRes;               // xy размер RT облаков
    float4 _Towers[MAX_TOWERS];   // x, y основания, z, R (м)
    float4 _TowerRot[MAX_TOWERS]; // cos, sin поворота, номер шаблона, высота шаблона (в R)
    sampler3D _TowerField;        // запечённое поле пухлости 3 шаблонов (сложены по z): (f + 1) / 2
    float4 _FieldBox;             // x полуширина (R), y низ (R), z высота (R), w — шаблонов
    float4 _Haze;                 // x плотность (1/м), y верх слоя (м), z дальность (м), w шагов
    float _TowerCount;
    float4 _Shape;                // x масштаб лопастей (м), y масштаб детали (м), z «лепка» лопастей, w эрозия
    float4 _DensityParams;        // x плотность (1/м), y поглощение к солнцу, z шагов, w дальность (м)
    float4 _Wind;                 // xyz ветер, w время
    float4 _SunGold;              // цвет солнца на открытых местах × сила
    float4 _SunEmber;             // цвет солнца в полутени × сила
    float4 _MultiScatter;         // rgb цвет, a сила
    float4 _AmbientTop;
    float4 _AmbientBottom;
    float4 _Phase;                // x g вперёд, y g назад, z смесь, w кайма
    float4 _Aerial;               // x доля воздушной перспективы
    float _Debug;

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

    float HG(float c, float g)
    {
        float g2 = g * g;
        return (1.0 - g2) / (12.566 * pow(max(1.0 + g2 - 2.0 * g * c, 1e-3), 1.5));
    }

    float Hash(float2 px)
    {
        float3 p3 = frac(float3(px.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    // Поле пухлости ближайшей башни: 1 в центре сферы, 0 на её поверхности, < 0 снаружи.
    // hN — высота точки в долях высоты башни.
    float TowerField(float3 p, out float hN)
    {
        float best = -1.0;
        hN = 0.0;
        for (int i = 0; i < MAX_TOWERS; i++)
        {
            if (i >= (int)_TowerCount) break;
            float4 T = _Towers[i];
            float4 Rr = _TowerRot[i];
            float3 q = (p - T.xyz) / T.w;
            float ys = Rr.w / _FieldBox.z;               // растяжение башни по высоте
            float qy = q.y / ys;
            if (abs(q.x) > _FieldBox.x || abs(q.z) > _FieldBox.x || qy < _FieldBox.y || qy > _FieldBox.y + _FieldBox.z) continue;
            float2 xz = float2(q.x * Rr.x - q.z * Rr.y, q.x * Rr.y + q.z * Rr.x);
            float3 uvw;
            uvw.x = xz.x / (2.0 * _FieldBox.x) + 0.5;
            uvw.y = (qy - _FieldBox.y) / _FieldBox.z;
            uvw.z = (clamp(xz.y / (2.0 * _FieldBox.x) + 0.5, 0.02, 0.98) + Rr.z) / _FieldBox.w;
            float f = tex3Dlod(_TowerField, float4(uvw, 0)).r * 2.0 - 1.0;
            if (f > best)
            {
                best = f;
                hN = q.y / Rr.w;
            }
        }
        return best;
    }

    float Density(float3 p, bool detail)
    {
        float hN;
        float f = TowerField(p, hN);
        if (f < -0.35) return 0.0;
        float3 wp = p + float3(0, _Wind.w * 0.6, 0);   // поверхность медленно «кипит» вверх
        float n = tex3Dlod(_Noise3D, float4(wp / _Shape.x, 0)).r;
        float bump = tex3Dlod(_Noise3D, float4(wp / (_Shape.x * 0.32), 0)).r;
        float m = saturate((f + (n - 0.5) * _Shape.z + (bump - 0.5) * 0.2) * 4.0);
        m *= saturate(hN / 0.06);                      // плоское основание
        if (m <= 0.0) return 0.0;
        if (detail)
        {
            float4 dn4 = tex3Dlod(_Noise3D, float4(wp / _Shape.y, 0));
            float dn = dn4.g * 0.7 + tex3Dlod(_Noise3D, float4(wp / (_Shape.y * 0.45), 0)).b * 0.3;
            float edge = 1.0 - saturate((m - 0.4) / 0.45);
            m = saturate(m - dn * _Shape.w * edge);
        }
        m = m * m * (3.0 - 2.0 * m);
        return m * _DensityParams.x;
    }

    float LightMarch(float3 p, float3 sunDir)
    {
        float od = 0.0;
        float acc = 0.0;
        float seg = 14.0;
        for (int k = 0; k < 4; k++)
        {
            od += Density(p + sunDir * (acc + seg * 0.5), false) * seg;
            acc += seg;
            seg *= 2.2;
            if (od > 6.0) break;
        }
        return od;
    }

    // Отрезок луча, пересекающий габариты башен (сферы вокруг каждой).
    bool TowerInterval(float3 ro, float3 rd, out float tA, out float tB)
    {
        tA = 1e7;
        tB = -1.0;
        for (int i = 0; i < MAX_TOWERS; i++)
        {
            if (i >= (int)_TowerCount) break;
            float4 T = _Towers[i];
            float hh = _TowerRot[i].w;
            float3 c = T.xyz + float3(0, T.w * hh * 0.5, 0);
            float r = T.w * max(1.5, hh * 0.62);
            float3 oc = ro - c;
            float b = dot(oc, rd);
            float cc = dot(oc, oc) - r * r;
            float disc = b * b - cc;
            if (disc <= 0.0) continue;
            float sq = sqrt(disc);
            float t0 = -b - sq;
            float t1 = -b + sq;
            if (t1 <= 0.0) continue;
            tA = min(tA, max(t0, 0.0));
            tB = max(tB, t1);
        }
        return tB > tA;
    }

    // Дымка: дешёвый слой у моря облаков по 2D-«погоде», без 3D-шума и без марша к солнцу.
    float4 Haze(float3 ro, float3 rd, float tEnd, float jitter, float phase, float3 sunCol)
    {
        float top = _Haze.y;
        float t0 = 0.0, t1 = min(tEnd, _Haze.z);
        if (rd.y > 1e-4 && ro.y > top) return float4(0, 0, 0, 1);
        if (abs(rd.y) > 1e-4)
        {
            float tTop = (top - ro.y) / rd.y;
            if (ro.y > top) t0 = max(t0, tTop); else if (rd.y > 0) t1 = min(t1, tTop);
        }
        if (t1 <= t0) return float4(0, 0, 0, 1);
        int n = (int)_Haze.w;
        float dt = (t1 - t0) / n;
        float t = t0 + dt * jitter;
        float3 light = 0;
        float trans = 1;
        for (int k = 0; k < n; k++)
        {
            float3 p = ro + rd * t;
            float2 wuv = (p.xz + _Wind.xz * _Wind.w) / 2600.0;
            float w = tex2Dlod(_SkyTideNoise, float4(wuv, 0, 0)).r * 0.65 + tex2Dlod(_SkyTideNoise, float4(wuv * 3.1 + 0.4, 0, 0)).g * 0.35;
            float hf = saturate(1.0 - max(p.y, 0.0) / top);
            float d = _Haze.x * smoothstep(0.35, 0.75, w) * hf * hf * smoothstep(25.0, 120.0, t);
            float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, 0.6);
            float3 src = sunCol * phase * 0.35 + amb * 1.1;
            float st = exp(-d * dt);
            light += trans * src * (1.0 - st);
            trans *= st;
            t += dt;
        }
        return float4(light, trans);
    }

    float4 fragMarch (v2f i) : SV_Target
    {
        float4 view = mul(_InvProj, float4(i.uv * 2.0 - 1.0, 1.0, 1.0));
        view.xyz /= view.w;
        float3 rd = normalize(mul((float3x3)_CamToWorld, view.xyz));
        float3 ro = _WorldSpaceCameraPos;

        if (_Debug > 0.5 && _Debug < 1.5)
        {
            float hN;
            float f = TowerField(ro + rd * 400.0, hN);
            return float4(saturate(f + 0.5), hN, Density(ro + rd * 400.0, true) / max(_DensityParams.x, 1e-6), 0);
        }

        float sceneDist = 1e7;
        float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        if (Linear01Depth(rawDepth) < 0.999)
        {
            float3 fwd = mul((float3x3)_CamToWorld, float3(0, 0, -1));
            sceneDist = LinearEyeDepth(rawDepth) / max(dot(rd, fwd), 1e-3);
        }
        float jit = 0.5 + 0.25 * (Hash(floor(i.uv * _LowRes.xy)) - 0.5);
        float3 sunDirH = normalize(_SkyTideSunDir.xyz);
        float cosH = dot(rd, sunDirH);
        float phaseH = min(lerp(HG(cosH, _Phase.y), HG(cosH, _Phase.x), _Phase.z) * 12.566, 2.6);
        float4 haze = Haze(ro, rd, sceneDist, jit, phaseH, _SunGold.rgb);

        float tA, tB;
        if (!TowerInterval(ro, rd, tA, tB)) return haze;
        tB = min(tB, min(sceneDist, _DensityParams.w));
        if (tB <= tA) return haze;

        int steps = (int)_DensityParams.z;
        float stepLen = (tB - tA) / steps;
        float t = tA + stepLen * jit;

        float3 sunDir = normalize(_SkyTideSunDir.xyz);
        float cosT = dot(rd, sunDir);
        float phase = min(lerp(HG(cosT, _Phase.y), HG(cosT, _Phase.x), _Phase.z) * 12.566, 2.6);
        float rimDir = pow(saturate(cosT), 4.0);

        float3 light = 0;
        float trans = 1;
        float distAcc = 0;
        float wAcc = 0;
        [loop]
        for (int s = 0; s < steps; s++)
        {
            float3 p = ro + rd * t;
            float d = Density(p, true) * smoothstep(30.0, 140.0, t);
            if (d > 1e-5)
            {
                float od = LightMarch(p, sunDir) * _DensityParams.y;
                float sunT = exp(-od);
                float ms = exp(-od * 0.22) * _MultiScatter.a;
                float edge = pow(1.0 - saturate(d / (_DensityParams.x * 0.7)), 1.6);
                float3 sunCol = lerp(_SunEmber.rgb, _SunGold.rgb, pow(saturate(sunT), 0.6));
                float hh = saturate((p.y - 0.0) / 450.0);
                float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, hh) * (0.15 + 0.85 * exp(-d * 35.0)) * (0.35 + 0.65 * hh);
                float3 src = sunCol * (sunT * phase * (1.0 + _Phase.w * edge * rimDir)) + _MultiScatter.rgb * ms + amb;
                float st = exp(-d * stepLen);
                light += trans * src * 0.86 * (1.0 - st);
                float w = trans * (1.0 - st);
                distAcc += t * w;
                wAcc += w;
                trans *= st;
                if (trans < 0.02) { trans = 0; break; }
            }
            t += stepLen;
        }

        float meanDist = wAcc > 1e-4 ? distAcc / wAcc : tB;
        light = lerp(light, SkyTidePanorama(rd) * (1.0 - trans), _Aerial.x * saturate(SkyTideAerial(meanDist)));
        // Дымка ближе башен (приближение): облака видны сквозь неё.
        return float4(haze.rgb + light * haze.a, trans * haze.a);
    }

    // Мягкое размытие облаков в низком разрешении (тент 3×3): убирает зерно и «пиксельные» кромки.
    float4 fragBlur (v2f i) : SV_Target
    {
        float2 d = _LowRes.zw;
        float4 c = tex2D(_MainTex, i.uv) * 4.0;
        c += (tex2D(_MainTex, i.uv + float2(d.x, 0)) + tex2D(_MainTex, i.uv - float2(d.x, 0))
            + tex2D(_MainTex, i.uv + float2(0, d.y)) + tex2D(_MainTex, i.uv - float2(0, d.y))) * 2.0;
        c += tex2D(_MainTex, i.uv + d) + tex2D(_MainTex, i.uv - d)
           + tex2D(_MainTex, i.uv + float2(d.x, -d.y)) + tex2D(_MainTex, i.uv + float2(-d.x, d.y));
        return c / 16.0;
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
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            ENDCG }
    }
    Fallback Off
}
