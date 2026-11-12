// Объёмные облака — «героические» кучевые башни (встроенный рендер, OnRenderImage).
// Форма: каждая башня собрана из 28 сфер шаблона (широкое плоское основание, средний ярус, 3–5 голов наверху);
//   поле пухлости = max(1 - d/r) по сферам, поверхность лепит 3D-шум (крупные лопасти + клубы), края разъедает
//   Worley-детализация. Шаблоны и параметры подобраны в офлайн-лаборатории (tools/cloudlab).
// Свет: толщина к солнцу и видимость неба запечены в поле (солнце неподвижно); цвет солнца по глубине — от золота на открытых вершинах до красно-оранжевого в полутени;
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
    float _FilterDetail;          // сохранение контура только в последнем проходе фильтра
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
    float4 _NearClear;            // x — радиус чистого шара у камеры (м)
    float4 _ShipClear;            // xyz — корабль, w — радиус «коридора» камера→корабль (м); w = 0 — нет корабля

    // Облако расступается только в узком коридоре от камеры до корабля (и чуть дальше него), а не шаром
    // вокруг камеры — шар был виден как круглая дыра в облаке.
    float ClearFactor(float3 p, float3 ro)
    {
        float k = smoothstep(8.0, 22.0, length(p - ro));
        if (_ShipClear.w > 0.0)
        {
            float3 ab = _ShipClear.xyz - ro;
            float L = length(ab);
            float3 dirS = ab / max(L, 1e-3);
            float s = dot(p - ro, dirS);
            float3 q = p - (ro + dirS * clamp(s, 0.0, L + 15.0));
            float r = _ShipClear.w * (0.6 + 0.4 * saturate(s / max(L, 1e-3)));
            k *= smoothstep(r * 0.55, r * 1.6, length(q));
        }
        return k;
    }
    float4 _Deck;                 // x низ слоя, y верх (м), z плотность (1/м), w шагов
    float4 _Deck2;                // x масштаб «погоды», y порог покрытия, z масштаб клубов, w масштаб детали (м)
    float4 _Deck3;                // x дальность (м), y макс. длина пути в слое (м), z поглощение к солнцу
    float _Bake;
    float4 _Temporal;             // x номер кадра, y вес истории (0 — нет истории), z верх моря облаков
    float4x4 _PrevVP;             // прошлый кадр: projection * view (OpenGL)
    sampler2D _History;
    sampler2D _CloudDistanceTex, _HistoryDistanceTex;
    float3 _PrevCameraPos;
    float4 _BakeOrigin, _BakeRight, _BakeUp, _BakeFwd;            // x начало, y конец растворения у камеры (м)

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

    // Упорядоченный сдвиг 4×4 (Байер): после размытия усредняется почти без остатка — нет «грязного стекла».
    static const float kBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    float Bayer4(float2 px)
    {
        int2 q = int2(fmod(px, 4.0));
        return (kBayer[q.y * 4 + q.x] + 0.5) / 16.0;
    }

    float Hash(float2 px)
    {
        float3 p3 = frac(float3(px.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    // Поле пухлости ближайшей башни: 1 в центре сферы, 0 на её поверхности, < 0 снаружи.
    // Ближайшая башня: x — поле пухлости (1 в центре сферы, < 0 снаружи), y — оптическая толщина к солнцу
    // в метрах при плотности 1 (запечена), z — видимость неба, w — высота в долях башни.
    float4 TowerField(float3 p)
    {
        float4 best = float4(-1.0, 0.0, 1.0, 0.0);
        for (int i = 0; i < MAX_TOWERS; i++)
        {
            if (i >= (int)_TowerCount) break;
            float4 T = _Towers[i];
            float4 Rr = _TowerRot[i];
            float3 q = (p - T.xyz) / T.w;
            float ys = Rr.w / _FieldBox.z;
            float qy = q.y / ys;
            if (abs(q.x) > _FieldBox.x || abs(q.z) > _FieldBox.x || qy < _FieldBox.y || qy > _FieldBox.y + _FieldBox.z) continue;
            float3 uvw;
            uvw.x = q.x / (2.0 * _FieldBox.x) + 0.5;
            uvw.y = (qy - _FieldBox.y) / _FieldBox.z;
            uvw.z = (clamp(q.z / (2.0 * _FieldBox.x) + 0.5, 0.02, 0.98) + Rr.z) / _FieldBox.w;
            float4 b = tex3Dlod(_TowerField, float4(uvw, 0));
            float f = b.r * 2.0 - 1.0;
            if (f > best.x) best = float4(f, b.g * 5.0 * T.w, b.b, q.y / Rr.w);
        }
        return best;
    }

    // Плотность (1/м); попутно — запечённая толщина к солнцу и видимость неба.
    float Density(float3 p, bool detail, out float tau, out float sky, out float fld)
    {
        float4 tf = TowerField(p);
        fld = tf.x;
        tau = tf.y;
        sky = tf.z;
        float f = tf.x;
        if (f < -0.35) return 0.0;
        float3 wp = p + float3(0, _Wind.w * 0.6, 0);   // поверхность медленно «кипит» вверх
        float n = tex3Dlod(_Noise3D, float4(wp / _Shape.x, 0)).r;
        float bump = tex3Dlod(_Noise3D, float4(wp / (_Shape.x * 0.32), 0)).r;
        float m = saturate((f + (n - 0.5) * _Shape.z + (bump - 0.5) * 0.2) * 4.0);
        m *= saturate(tf.w / 0.06);                   // плоское основание
        if (m <= 0.0) return 0.0;
        float edgeW = 1.0 - saturate((m - 0.4) / 0.45);
        if (detail && edgeW > 0.0)
        {
            float dn = tex3Dlod(_Noise3D, float4(wp / _Shape.y, 0)).g * 0.7
                     + tex3Dlod(_Noise3D, float4(wp / (_Shape.y * 0.45), 0)).b * 0.3;
            m = saturate(m - dn * _Shape.w * edgeW);
        }
        m = m * m * (3.0 - 2.0 * m);
        return m * _DensityParams.x;
    }

    // Отрезок луча, пересекающий габариты башен (сферы вокруг каждой).
    bool TowerInterval(float3 ro, float3 rd, out float tA, out float tB)
    {
        // Габарит башни — коробка её поля (а не шар): шар срезал низ и бока облака,
        // и срез был виден как полупрозрачная сфера.
        tA = 1e7;
        tB = -1.0;
        float3 inv = 1.0 / (rd + 1e-6 * (step(0.0, rd) * 2.0 - 1.0));
        for (int i = 0; i < MAX_TOWERS; i++)
        {
            if (i >= (int)_TowerCount) break;
            float4 T = _Towers[i];
            float ys = _TowerRot[i].w / _FieldBox.z;
            float3 lo = T.xyz + float3(-_FieldBox.x, _FieldBox.y * ys, -_FieldBox.x) * T.w;
            float3 hi = T.xyz + float3(_FieldBox.x, (_FieldBox.y + _FieldBox.z) * ys, _FieldBox.x) * T.w;
            float3 t0 = (lo - ro) * inv;
            float3 t1 = (hi - ro) * inv;
            float3 tmin = min(t0, t1), tmax = max(t0, t1);
            float n = max(max(tmin.x, tmin.y), tmin.z);
            float f = min(min(tmax.x, tmax.y), tmax.z);
            if (f <= max(n, 0.0)) continue;
            tA = min(tA, max(n, 0.0));
            tB = max(tB, f);
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
            float d = _Haze.x * lerp(0.25, 1.0, smoothstep(0.2, 0.85, w)) * hf * hf * smoothstep(25.0, 120.0, t) * (1.0 - smoothstep(_Haze.z * 0.6, _Haze.z, t));
            float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, 0.6);
            float3 src = sunCol * phase * 0.1 + amb * 0.9;
            float st = exp(-d * dt);
            light += trans * src * (1.0 - st);
            trans *= st;
            t += dt;
        }
        return float4(light, trans);
    }

    // ---------- объёмное море облаков ----------
    // Слой между _Deck.x и _Deck.y (м): покрытие — 2D-«погода», верх слоя бугрится 3D-шумом (клубы),
    // края разъедает детализация. Свет: два шага к солнцу (бока клубов к солнцу светятся, задние — в тени),
    // верх ярче низа, фиолетовое заполнение в глубине — та же палитра, что у башен.
    // Покрытие (2D-«погода») меняется на сотнях метров — для шагов к солнцу (18 и 55 м) берётся то же, что в точке:
    // 1 выборка 3D-шума вместо 6 выборок на шаг к солнцу, картинка та же.
    float DeckCoverage(float3 p)
    {
        float2 drift = _Wind.xz * _Wind.w;
        // «Погода» без рядов: текстура шума мала (256², повтор каждые ~1.4 км), а в канале G есть тёмная полоса
        // на шве — в перспективе это давало ровные ряды клубов. Теперь три октавы канала R с поворотом на
        // «нерациональные» углы, некратными масштабами и искажением координат — повтор не складывается в сетку.
        float2 q = p.xz + drift;
        float2 warp = float2(tex2Dlod(_SkyTideNoise, float4(q / (_Deck2.x * 2.3) + 0.13, 0, 0)).r,
                             tex2Dlod(_SkyTideNoise, float4(q.yx / (_Deck2.x * 2.9) + 0.57, 0, 0)).r) - 0.5;
        float2 qa = float2(q.x * 0.819 - q.y * 0.574, q.x * 0.574 + q.y * 0.819) / _Deck2.x + warp * 0.55;
        float2 qb = float2(q.x * -0.666 - q.y * 0.746, q.x * 0.746 + q.y * -0.666) / (_Deck2.x * 0.43) + 0.31;
        float2 qc = float2(q.x * 0.122 - q.y * 0.993, q.x * 0.993 + q.y * 0.122) / (_Deck2.x * 0.19) + 0.71;
        float w = tex2Dlod(_SkyTideNoise, float4(qa, 0, 0)).r * 0.55
                + tex2Dlod(_SkyTideNoise, float4(qb, 0, 0)).r * 0.3
                + tex2Dlod(_SkyTideNoise, float4(qc, 0, 0)).r * 0.15;
        return saturate((w - _Deck2.y) / 0.35);
    }

    float DeckShape(float3 p, float cov, bool detail)
    {
        float h = saturate((p.y - _Deck.x) / (_Deck.y - _Deck.x));
        if (cov * 1.0 + 0.12 <= h) return 0.0;            // выше любого клуба в этой точке — шум не нужен
        float2 drift = _Wind.xz * _Wind.w;
        float3 wp = p + float3(drift.x, _Wind.w * 0.4, drift.y);
        float n = tex3Dlod(_Noise3D, float4(wp / _Deck2.z, 0)).r;
        float topH = cov * (0.45 + 0.55 * n) + 0.12;
        float m = saturate((topH - h) * 6.0) * saturate(h * 5.0 + 0.2);
        if (detail && m > 0.0 && m < 0.9)
        {
            float dn = tex3Dlod(_Noise3D, float4(wp / _Deck2.w, 0)).g;
            m = saturate(m - dn * 0.45 * (1.0 - m));
        }
        return m * _Deck.z;
    }

    float DeckDensity(float3 p, bool detail) { return DeckShape(p, DeckCoverage(p), detail); }

    // front — часть моря перед башней; обе части считаются за один прежний марш.
    float4 Deck(float3 ro, float3 rd, float tEnd, float jitter, float phase, float split,
                out float4 front, out float2 moments)
    {
        front = float4(0, 0, 0, 1);
        moments = 0;
        if (_Deck.w < 0.5) return float4(0, 0, 0, 1);   // море выключено (отладочная панель)
        float base = _Deck.x, top = _Deck.y;
        float t0 = 0.0, t1 = min(tEnd, _Deck3.x);
        if (abs(rd.y) > 1e-4)
        {
            float ta = (base - ro.y) / rd.y, tb = (top - ro.y) / rd.y;
            float tn = min(ta, tb), tf = max(ta, tb);
            t0 = max(t0, tn);
            t1 = min(t1, tf);
        }
        else if (ro.y < base || ro.y > top) return float4(0, 0, 0, 1);
        t1 = min(t1, t0 + _Deck3.y);              // у горизонта слой не тянется бесконечно
        if (t1 <= t0) return float4(0, 0, 0, 1);

        int n = (int)_Deck.w;
        // Шаги растут геометрически: у камеры ~10 м (крупный первый шаг 30–40 м давал «рябь полосами» —
        // слои равного расстояния пересекали бугристый верх моря), к 18-му шагу покрывают ~1.6 км пути в слое.
        const float ratio = 1.22;
        float sumK = (pow(ratio, n) - 1.0) / (ratio - 1.0);
        float stepLen = min(10.0, (t1 - t0) / sumK);
        float t = t0 + stepLen * jitter;
        float3 L = normalize(_SkyTideSunDir.xyz);
        float3 light = 0;
        float trans = 1;
        float distAcc = 0, wAcc = 0;
        [loop]
        for (int k = 0; k < n; k++)
        {
            if (t > t1) break;
            float3 p = ro + rd * t;
            float cov = DeckCoverage(p);
            float d = DeckShape(p, cov, t < 1500.0) * ClearFactor(p, ro);
            if (d > 1e-5)
            {
                float d1 = DeckShape(p + L * 18.0, cov, false);
                float d2 = DeckShape(p + L * 55.0, cov, false);
                float tau = (d1 * 18.0 + d2 * 37.0) * _Deck3.z;
                float sunT = exp(-tau);
                float ms = exp(-tau * 0.25) * _MultiScatter.a;
                float h = saturate((p.y - base) / (top - base));
                float3 sunCol = lerp(_SunEmber.rgb, _SunGold.rgb, pow(sunT, 0.6));
                float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, h) * lerp(0.3, 0.85, h);
                amb *= lerp(float3(0.6, 0.68, 0.88), float3(1, 1, 1), h);
                float3 src = (sunCol * sunT * min(phase, 1.6) + _MultiScatter.rgb * ms * h) * _Deck3.w + amb + SkyTideFlashLight(p);
                float st = exp(-d * stepLen);
                float wgt = trans * (1.0 - st);
                float frontFraction = saturate((split - t) / max(stepLen, 1e-4) + 0.5);
                float frontSt = exp(-d * stepLen * frontFraction);
                float frontWeight = trans * (1.0 - frontSt);
                front.rgb += src * 0.86 * frontWeight;
                front.a *= frontSt;
                moments.y += t * frontWeight;
                light += src * 0.86 * wgt;
                distAcc += t * wgt;
                wAcc += wgt;
                trans *= st;
                if (trans < 0.03) { trans = 0; if (frontFraction >= 1.0) front.a = 0; break; }
            }
            t += stepLen;
            stepLen *= ratio;
        }
        float meanDist = wAcc > 1e-4 ? distAcc / wAcc : t1;
        float aerial = _Aerial.x * saturate(SkyTideAerial(meanDist));
        float3 panorama = SkyTidePanorama(rd);
        light = lerp(light, panorama * (1.0 - trans), aerial);
        front.rgb = lerp(front.rgb, panorama * (1.0 - front.a), aerial);
        moments.x = distAcc;
        return float4(light, trans);
    }

    float4 March(v2f i, out float cloudDistance)
    {
        cloudDistance = 60000.0; // конечный sentinel помещается в RHalf
        float4 view = mul(_InvProj, float4(i.uv * 2.0 - 1.0, 1.0, 1.0));
        view.xyz /= view.w;
        float3 rd = normalize(mul((float3x3)_CamToWorld, view.xyz));
        float3 ro = _WorldSpaceCameraPos;
        bool bake = _Bake > 0.5;
        if (bake)
        {
            ro = _BakeOrigin.xyz + _BakeRight.xyz * i.uv.x + _BakeUp.xyz * i.uv.y;
            rd = _BakeFwd.xyz;
        }

        if (_Debug > 0.5 && _Debug < 1.5)
        {
            float4 tf = TowerField(ro + rd * 400.0);
            return float4(saturate(tf.x + 0.5), saturate(tf.y / 400.0), tf.z, 0);
        }

        float sceneDist = 1e7;
        float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        if (!bake && Linear01Depth(rawDepth) < 0.999)
        {
            float3 fwd = mul((float3x3)_CamToWorld, float3(0, 0, -1));
            sceneDist = LinearEyeDepth(rawDepth) / max(dot(rd, fwd), 1e-3);
        }
        float2 jpx = floor(i.uv * _LowRes.xy);
        // Сдвиг шагов меняется от кадра к кадру — временное накопление (проход 5) усредняет узор вместо размытия.
        float jit = bake ? Hash(jpx) : frac(Bayer4(jpx) + _Temporal.x * 0.618034);  // в кадре — Байер (размытие его съедает), в снимках — хэш
        float3 sunDirH = normalize(_SkyTideSunDir.xyz);
        float cosH = dot(rd, sunDirH);
        float phaseH = min(lerp(HG(cosH, _Phase.y), HG(cosH, _Phase.x), _Phase.z) * 12.566, 2.6);
        // 1) Героические башни: свет, пропускание и где луч впервые вошёл в облако.
        float3 light = 0;
        float trans = 1;
        float firstHit = 1e7;
        float towerMoment = 0;
        float tA, tB;
        if (TowerInterval(ro, rd, tA, tB))
        {
            tB = min(tB, min(sceneDist, _DensityParams.w));
            if (tB > tA)
            {
                int steps = (int)_DensityParams.z;
                float stepBase = (tB - tA) / steps;
                // Шаги растут с расстоянием: у камеры мелкие (без зерна внутри облака), вдали крупнее.
                float stepGrow = 1.3 / steps;
                float stepLen = stepBase * 0.35;
                float t = tA + stepLen * jit;

                float3 sunDir = normalize(_SkyTideSunDir.xyz);
                float cosT = dot(rd, sunDir);
                float phase = min(lerp(HG(cosT, _Phase.y), HG(cosT, _Phase.x), _Phase.z) * 12.566, 2.6);
                float rimDir = pow(saturate(cosT), 4.0);

                float distAcc = 0;
                float wAcc = 0;
                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float3 p = ro + rd * t;
                    float tau, sky, fld;
                    float d = Density(p, t < 1800.0, tau, sky, fld) * (bake ? 1.0 : ClearFactor(p, ro));
                    if (d > 1e-5)
                    {
                        // Свет из запечённого поля: никаких шагов к солнцу.
                        float od = tau * _DensityParams.x * _DensityParams.y;
                        float sunT = exp(-od);
                        float ms = exp(-od * 0.22) * _MultiScatter.a;
                        float edge = pow(1.0 - saturate(d / (_DensityParams.x * 0.7)), 1.6);
                        float3 sunCol = lerp(_SunEmber.rgb, _SunGold.rgb, pow(saturate(sunT), 0.6));
                        float hh = saturate((p.y - 0.0) / 450.0);
                        float3 amb = lerp(_AmbientBottom.rgb, _AmbientTop.rgb, hh) * (0.6 + 0.4 * hh);
                        // Тень — фиолетовая, как на рисованных облаках, а не серая: глушим яркость, но не насыщенность.
                        amb *= lerp(float3(0.55, 0.64, 0.86), float3(1.0, 1.0, 1.0), sky);
                        float3 src = sunCol * (sunT * phase * (1.0 + _Phase.w * edge * rimDir)) + _MultiScatter.rgb * ms * sky + amb
                                   + (bake ? float3(0, 0, 0) : SkyTideFlashLight(p));
                        float st = exp(-d * stepLen);
                        light += trans * src * 0.86 * (1.0 - st);
                        float w = trans * (1.0 - st);
                        distAcc += t * w;
                        wAcc += w;
                        if (firstHit > 1e6 && w > 1e-4) firstHit = t;
                        trans *= st;
                        if (trans < 0.02) { trans = 0; break; }
                    }
                    // Пустота вдали от поверхности — шагаем шире (поле ≈ доля радиуса сферы до поверхности).
                    t += stepLen * (bake ? 1.0 : 1.0 + 2.0 * saturate((-fld - 0.45) * 2.0));
                    stepLen += stepBase * stepGrow;
                    if (t > tB) break;
                }
                float meanDist = wAcc > 1e-4 ? distAcc / wAcc : tB;
                towerMoment = distAcc;
                light = lerp(light, SkyTideAerialColor(rd) * (1.0 - trans), SkyTideAerialAmount(_Aerial.x, meanDist));
            }
        }
        if (bake) return float4(light, trans);

        // Море продолжается за полупрозрачным краем башни. Жёсткого порога trans < 0.9 нет.
        // Полностью закрытую часть пропускаем; шаги и выборки плотности не удваиваются.
        float deckEnd = trans <= 0.0 ? min(sceneDist, firstHit) : sceneDist;
        float4 front;
        float2 deckMoments;
        float4 deck = Deck(ro, rd, deckEnd, jit, phaseH, firstHit, front, deckMoments);
        float totalTrans = trans * deck.a;
        float moment = deckMoments.y + front.a * towerMoment + trans * max(0.0, deckMoments.x - deckMoments.y);
        if (totalTrans < 0.9999) cloudDistance = clamp(moment / max(1.0 - totalTrans, 1e-4), 1.0, 60000.0);
        return float4(front.rgb + front.a * light + trans * max(0.0, deck.rgb - front.rgb), totalTrans);
    }

    float4 fragMarch(v2f i) : SV_Target
    {
        float distance;
        return March(i, distance);
    }

    struct MarchOutput
    {
        float4 color : SV_Target0;
        float distance : SV_Target1;
    };

    // Та же интеграция сразу записывает среднюю глубину: второго марша нет.
    MarchOutput fragMarchDistance(v2f i)
    {
        MarchOutput o;
        o.color = March(i, o.distance);
        return o;
    }

    v2f vertMarchDistance(uint id : SV_VertexID)
    {
        v2f o;
        o.uv = float2((id << 1) & 2, id & 2);
        o.pos = float4(o.uv * 2.0 - 1.0, 0, 1);
        #if UNITY_UV_STARTS_AT_TOP
        o.pos.y = -o.pos.y;
        #endif
        return o;
    }

    // Перепроецирование по глубине облака, отсечение открывшихся областей и быстрых изменений света.
    float4 fragTemporal (v2f i) : SV_Target
    {
        float4 cur = tex2D(_MainTex, i.uv);
        if (_Temporal.y <= 0.0) return cur;
        float4 view = mul(_InvProj, float4(i.uv * 2.0 - 1.0, 1.0, 1.0));
        view.xyz /= view.w;
        float3 rd = normalize(mul((float3x3)_CamToWorld, view.xyz));
        float3 ro = _WorldSpaceCameraPos;
        float dist = _Temporal.w > 0.5 ? tex2D(_CloudDistanceTex, i.uv).r
            : (rd.y < -0.02 ? clamp((_Temporal.z - ro.y) / rd.y, 40.0, 6000.0) : 1500.0);
        if (cur.a > 0.999 || dist > 59000.0) return cur;
        float3 worldPoint = ro + rd * dist;
        float4 pc = mul(_PrevVP, float4(worldPoint, 1.0));
        float2 puv = pc.xy / pc.w * 0.5 + 0.5;
        if (pc.w <= 0.0 || any(puv < 0.0) || any(puv > 1.0)) return cur;
        float confidence = 1.0;
        if (_Temporal.w > 0.5)
        {
            float oldDistance = tex2D(_HistoryDistanceTex, puv).r;
            float expectedDistance = length(worldPoint - _PrevCameraPos);
            float tolerance = max(12.0, expectedDistance * 0.08);
            confidence *= 1.0 - smoothstep(tolerance, tolerance * 2.0, abs(oldDistance - expectedDistance));
        }
        float4 mn = cur, mx = cur;
        float2 px = _LowRes.zw;
        [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float4 c = tex2D(_MainTex, i.uv + float2(x, y) * px);
            mn = min(mn, c); mx = max(mx, c);
        }
        float4 old = tex2D(_History, puv);
        confidence *= 1.0 - saturate(abs(old.a - cur.a) * 4.0);
        float lightChange = abs(Luminance(old.rgb) - Luminance(cur.rgb));
        confidence *= 1.0 - saturate(lightChange / max(0.15, Luminance(cur.rgb) * 0.6));
        float motion = length((puv - i.uv) * _LowRes.xy);
        confidence *= lerp(1.0, 0.3, saturate(motion / 20.0));
        float4 h = clamp(old, mn, mx);
        return lerp(cur, h, _Temporal.y * confidence);
    }

    // Снимок для импостора: свет (уже умножен на покрытие) в кодировке sqrt(свет/6), альфа — покрытие.
    float4 fragBake (v2f i) : SV_Target
    {
        float4 c = fragMarch(i);
        return float4(sqrt(saturate(c.rgb / 6.0)), 1.0 - c.a);
    }

    // Тент 3×3 с глубиной: шум усредняется, но море за кораблём не затекает на его силуэт.
    // При запекании импосторов сцены нет — остаётся прежний обычный тент.
    float4 fragBlur (v2f i) : SV_Target
    {
        float2 d = _LowRes.zw;
        float centerDepth = _Bake > 0.5 ? 0.0 : tex2D(_LowDepthTex, i.uv).r;
        float4 c = 0;
        float4 mn = 1e6, mx = -1e6;
        float total = 0;
        [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 uv = i.uv + float2(x, y) * d;
            float weight = (x == 0 ? 2.0 : 1.0) * (y == 0 ? 2.0 : 1.0);
            if (_Bake < 0.5)
            {
                float diff = abs(tex2D(_LowDepthTex, uv).r - centerDepth);
                weight *= exp(-diff / max(2.0, centerDepth * 0.015));
            }
            float4 tap = tex2D(_MainTex, uv);
            c += tap * weight;
            total += weight;
            if (_FilterDetail > 0.0 && _Bake < 0.5 && weight > 0.01)
            {
                mn = min(mn, tap);
                mx = max(mx, tap);
            }
        }
        float4 filtered = c / max(total, 1e-5);
        if (_Bake > 0.5 || _FilterDetail <= 0.0) return filtered;
        float4 center = tex2D(_MainTex, i.uv);
        float contrast = (Luminance(mx.rgb) - Luminance(mn.rgb)) / max(0.2, Luminance(center.rgb));
        float contour = max(contrast, (mx.a - mn.a) * 2.0);
        float keep = saturate(_FilterDetail) * smoothstep(0.12, 0.4, contour);
        // Положительная смесь двух уже сглаженных оценок: без выбросов яркости и тёмных ореолов.
        return lerp(filtered, center, keep);
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
        // Ни один соседний пиксель облаков не совпал по глубине (тонкие части корабля: снасти, баллоны,
        // край корпуса) — пиксель принадлежит кораблю, облака за ним. Раньше брался ближайший — и море облаков
        // позади корабля ложилось на него «призраком».
        bool sceneObject = referenceDepth < 4000.0 && nearestDifference > max(4.0, referenceDepth * 0.08);
        float4 c = totalWeight > 1e-5 ? result / totalWeight : (sceneObject ? float4(0, 0, 0, 1) : nearestCloud);
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
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBake
            #pragma target 3.5
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragTemporal
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vertMarchDistance
            #pragma fragment fragMarchDistance
            #pragma target 3.5
            ENDCG }
    }
    Fallback Off
}
