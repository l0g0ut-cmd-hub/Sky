#ifndef SKYTIDE_INCLUDED
#define SKYTIDE_INCLUDED

// Глобальные параметры неба (выставляет SkyTideBootstrap через Shader.SetGlobal*).
sampler2D _SkyTidePanorama;   // панорама неба 2:1, та же, что в скайбоксе
sampler2D _SkyTideNoise;      // бесшовный шум: R и G — два независимых поля
float4 _SkyTideSunDir;        // xyz — направление НА солнце
float4 _SkyTideAerial;
float4 _SkyTideFlashes[4];       // xyz позиция, w сила; до четырёх одновременных вспышек
float4 _SkyTideFlashColors[4];   // rgb цвет, w радиус в метрах
float _SkyTideFlashCount;

// Локальная подсветка облаков и дыма. Вне залпа — ранний выход без четырёх расчётов на каждом шаге марша.
// Цвет воздушной дымки для облаков: горизонт в этом азимуте (а не пиксель неба прямо за облаком —
// иначе дальнее облако перекрашивается в рисунок панорамы за ним и выглядит полупрозрачным).
// Доля ограничена: облако остаётся облаком.
inline float SkyTideAerialAmount(float strength, float dist) { return min(strength * saturate(SkyTideAerial(dist)), 0.55); }
inline float3 SkyTideAerialColor(float3 rd)
{
    float3 h = normalize(float3(rd.x, 0.03, rd.z));
    float3 side = normalize(float3(-rd.z, 0.0, rd.x)) * 0.08;
    return (SkyTidePanorama(h) + SkyTidePanorama(normalize(h + side)) + SkyTidePanorama(normalize(h - side))) / 3.0;
}

inline float3 SkyTideFlashLight(float3 p)
{
    if (_SkyTideFlashCount < 0.5) return float3(0, 0, 0);
    float3 light = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float3 d = p - _SkyTideFlashes[k].xyz;
        float r = max(_SkyTideFlashColors[k].w, 1.0);
        float dist2 = dot(d, d);
        float falloff = saturate(1.0 - dist2 / (r * r));
        light += _SkyTideFlashColors[k].rgb * (_SkyTideFlashes[k].w * falloff * falloff / (1.0 + dist2 / 180.0));
    }
    return min(light, float3(6, 6, 6));
}

// Цвет панорамы в направлении dir (та же развёртка, что у Skybox/Panoramic: центр картинки = +X).
inline float3 SkyTidePanorama(float3 dir)
{
    dir = normalize(dir);
    float lon = atan2(dir.z, dir.x);
    float lat = acos(clamp(dir.y, -1.0, 1.0));
    float2 uv = float2(0.5, 1.0) - float2(lon, lat) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
    return tex2Dlod(_SkyTidePanorama, float4(uv, 0, 0)).rgb;
}

// Доля «воздушной перспективы»: насколько далёкий объект растворяется в цвете неба за ним.
inline float SkyTideAerial(float dist)
{
    return saturate((dist - _SkyTideAerial.x) / max(_SkyTideAerial.y - _SkyTideAerial.x, 1.0)) * _SkyTideAerial.z;
}

// Косинус угла между направлением взгляда и солнцем по горизонтали: 1 — смотрим на солнце, -1 — от солнца.
inline float SkyTideSunDot(float3 viewDir)
{
    float2 a = viewDir.xz;
    float2 s = _SkyTideSunDir.xz;
    float la = length(a);
    float ls = length(s);
    return (la > 1e-4 && ls > 1e-4) ? dot(a / la, s / ls) : 0.0;
}

#endif
