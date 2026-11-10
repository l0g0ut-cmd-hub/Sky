#ifndef SKYTIDE_INCLUDED
#define SKYTIDE_INCLUDED

// Глобальные параметры неба (выставляет SkyTideBootstrap через Shader.SetGlobal*).
sampler2D _SkyTidePanorama;   // панорама неба 2:1, та же, что в скайбоксе
sampler2D _SkyTideNoise;      // бесшовный шум: R и G — два независимых поля
float4 _SkyTideSunDir;        // xyz — направление НА солнце
float4 _SkyTideAerial;
float4 _SkyTideFlash;         // xyz — последняя вспышка (залп/взрыв), w — сила (гаснет за ~0.2 с; FxSystem)

// Подсветка облаков вспышкой: тёплый свет, спадает с расстоянием (радиус ~45 м). База — развивать дальше.
inline float3 SkyTideFlashLight(float3 p)
{
    float3 d = p - _SkyTideFlash.xyz;
    return float3(1.0, 0.55, 0.22) * (_SkyTideFlash.w / (1.0 + dot(d, d) / 2000.0));
}        // x — начало растворения в небе (м), y — конец, z — максимальная доля

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
