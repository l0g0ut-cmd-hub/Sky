// Небесная крепость: Standard BRDF + растворение низа в море облаков и воздушная дымка на расстоянии.
// Двусторонний (Cull Off): модель Meshy — не замкнутое тело, а ~100 незамкнутых пересекающихся оболочек
// (1066 открытых рёбер), и с отсечением задних граней изнанка каждой стенки была «дырой» насквозь.
// Низ модели (на −205 м при масштабе ×3) не обрывается кромкой в просветах облаков: глубоко под морем облаков,
// ниже _DissolveTop, цвет уходит в дымку горизонта. У поверхности облаков растворения нет — там опоры и скалу
// закрывают сами объёмные облака по глубине сцены (цветное растворение у поверхности красило опоры в розовый).
Shader "SkyTide/Fortress"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        // Материалы вблизи (tools/fortress_tiles.py): плитка хранит «во сколько раз светлее среднего» × 0.4.
        _WallTex ("Masonry tile", 2D) = "gray" {}
        _DeckTex ("Planks tile", 2D) = "gray" {}
        _RockTex ("Rock tile", 2D) = "gray" {}
        _TileSize ("Tile size, m (wall, deck, rock)", Vector) = (28, 16, 70, 0)
        _RockTop ("Rock below (world Y)", Float) = 110
        _RockBlend ("Rock blend (m)", Float) = 30
        _DetailStrength ("Detail strength", Range(0,1)) = 0.9
        _DetailFar ("Detail fade distance (m)", Float) = 1200
        _BumpHeight ("Relief height (m)", Float) = 0.5
        _Glossiness ("Smoothness", Range(0,1)) = 0.18
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionMap ("Emission", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission color", Color) = (0,0,0,1)
        _DissolveTop ("Dissolve top (world Y)", Float) = 45
        _DissolveRange ("Dissolve range (m)", Float) = 45
        _AerialStrength ("Aerial strength", Range(0,1)) = 0.45
        _AerialRange ("Aerial start, end (m)", Vector) = (900, 5000, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert finalcolor:Haze exclude_path:deferred
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "SkyTide.cginc"

        sampler2D _MainTex, _EmissionMap, _WallTex, _DeckTex, _RockTex;
        half4 _Color, _EmissionColor;
        half _Glossiness, _Metallic;
        float4 _TileSize;
        float4 _AerialRange;
        float _DissolveTop, _DissolveRange, _AerialStrength, _DetailStrength, _DetailFar, _RockTop, _RockBlend, _BumpHeight;

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
            half3 glowTex = tex2D(_EmissionMap, IN.uv_MainTex).rgb;
            float3 nraw = WorldNormalVector(IN, float3(0, 0, 1));
            float nlen = length(nraw);
            // Изнанка: настоящая нормаль грани (по производным позиции), развёрнутая к камере; если нормаль вершин
            // смотрит от неё — это задняя сторона, свет считаем по перевёрнутой нормали.
            float3 dpx = ddx(IN.worldPos), dpy = ddy(IN.worldPos);
            float3 fn = cross(dpx, dpy);
            float3 toEye = _WorldSpaceCameraPos - IN.worldPos;
            if (dot(fn, toEye) < 0.0) fn = -fn;
            // Нормаль вершин может выродиться (встречные листы модели): normalize(0) — это NaN, на телефоне
            // чёрное пятно с белым ореолом свечения. Тогда берём нормаль грани.
            float3 n0 = nlen > 0.3 ? nraw / nlen : normalize(fn + float3(0, 1e-12, 0));
            float3 gn = dot(n0, fn) < 0.0 ? -n0 : n0;

            // Материалы вблизи. Цвет несёт текстура модели (~75 см на тексель), резкость и рельеф — плитки в мировых
            // координатах: кладка на серый камень, скала на него же ниже _RockTop, доски на всё деревянное.
            // Проекция — по главной оси нормали (одна выборка на плитку); производные для мипов берутся от позиции,
            // иначе на смене оси проекции была бы линия из мелких мипов.
            float3 an = abs(gn);
            bool top = an.y > max(an.x, an.z);
            bool xdom = an.x > an.z;
            float2 puv = top ? IN.worldPos.xz : (xdom ? IN.worldPos.zy : IN.worldPos.xy);
            float2 gx = top ? dpx.xz : (xdom ? dpx.zy : dpx.xy);
            float2 gy = top ? dpy.xz : (xdom ? dpy.zy : dpy.xy);
            float3 inv = 1.0 / max(_TileSize.xyz, 0.1);
            half3 wallD = tex2Dgrad(_WallTex, puv * inv.x, gx * inv.x, gy * inv.x).rgb;
            half3 deckD = tex2Dgrad(_DeckTex, puv * inv.y, gx * inv.y, gy * inv.y).rgb;
            half3 rockD = tex2Dgrad(_RockTex, puv * inv.z, gx * inv.z, gy * inv.z).rgb;
            // Что это за поверхность — по цвету модели: дерево насыщенное рыже-коричневое, камень серый.
            float cmax = max(albedo.r, max(albedo.g, albedo.b)), cmin = min(albedo.r, min(albedo.g, albedo.b));
            float wood = smoothstep(0.55, 0.8, (cmax - cmin) / max(cmax, 1e-3));
            float cloth = 1.0 - smoothstep(0.06, 0.14, albedo.g / max(albedo.r, 1e-3));   // красные полотнища — без плитки
            float glowMask = saturate(max(glowTex.r, max(glowTex.g, glowTex.b)) * 4.0);       // окна и фонари — без плитки
            float rock = saturate((_RockTop - IN.worldPos.y) / max(_RockBlend, 1.0));
            float dist = length(toEye);
            float amount = saturate((_DetailFar - dist) / (_DetailFar * 0.5)) * _DetailStrength * (1.0 - cloth) * (1.0 - glowMask);
            half3 tile = lerp(lerp(wallD, rockD, rock), deckD, wood) * 2.5;   // 1 — «как в модели»
            albedo.rgb *= lerp(half3(1, 1, 1), tile, amount);
            // Рельеф из яркости плитки (швы кладки и щели досок темнее — значит глубже): нормаль по производным
            // высоты на экране (Mikkelsen, bump mapping unparametrized surfaces) — без карты нормалей и лишних выборок.
            float h = dot(tile, half3(0.3, 0.59, 0.11)) * _BumpHeight * amount;
            float slopeMax = 1.2;   // не круче ~50°: на смене проекции высота скачет
            float dhx = clamp(ddx(h), -slopeMax * length(dpx), slopeMax * length(dpx));
            float dhy = clamp(ddy(h), -slopeMax * length(dpy), slopeMax * length(dpy));
            float3 r1 = cross(dpy, gn), r2 = cross(gn, dpx);
            float det = dot(dpx, r1);
            float3 wn = normalize(abs(det) * gn - sign(det) * (dhx * r1 + dhy * r2) + gn * 1e-7);
            float3 tanW = WorldNormalVector(IN, float3(1, 0, 0)), bitW = WorldNormalVector(IN, float3(0, 1, 0));
            o.Albedo = albedo.rgb;
            o.Normal = normalize(float3(dot(wn, tanW), dot(wn, bitW), dot(wn, n0)));
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = glowTex * _EmissionColor.rgb;
            o.Alpha = 1;
        }

        void Haze(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            float3 rd = IN.worldPos - _WorldSpaceCameraPos;
            float dist = length(rd);
            float sink = saturate((_DissolveTop - IN.worldPos.y) / max(_DissolveRange, 1.0));
            sink = sink * sink * (3.0 - 2.0 * sink);
            // Своя дымка, а не облачная (та начинается у самой камеры и на 2 км превращала крепость в плоский
            // оранжевый силуэт): до 900 м её нет, к 5 км — не больше _AerialStrength. Крепость стоит тёмной громадой.
            float haze = max(sink, _AerialStrength * smoothstep(_AerialRange.x, _AerialRange.y, dist));
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
