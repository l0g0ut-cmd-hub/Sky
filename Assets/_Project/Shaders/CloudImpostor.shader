// Импосторы объёмных облаков: дальние и средние облака — снимки настоящих объёмных башен с 16 сторон.
// Снимки запекает VolumetricClouds при старте тем же шейдером (проход 4). Здесь квад поворачивается к камере,
// выбираются два ближайших по азимуту снимка и плавно смешиваются — облетая облако, видишь его с разных сторон.
// Рисуется после сведения объёмных облаков (из VolumetricClouds.OnRenderImage), одной сеткой, отсортированной
// от дальних к ближним. Корабль закрывает облако по глубине сцены; героические башни — по их пропусканию.
// Вершины: position — центр основания облака, uv0 — угол квада (0..1), uv1 — ширина, высота, вариант, —.
Shader "Hidden/SkyTide/CloudImpostor"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #include "UnityCG.cginc"
            #include "SkyTide.cginc"

            #define FRAMES 16
            #define MAX_TOWERS 8

            UNITY_DECLARE_TEX2DARRAY(_ImpTex);
            sampler2D _CloudTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4x4 _ImpVP;      // проекция на текущую цель (GPU)
            float4x4 _CamVP;      // проекция камеры (OpenGL) — для координат экрана
            float4x4 _CamV;
            float4 _ImpCam;       // xyz — позиция камеры
            float4 _ImpParams;    // x доля воздушной перспективы, y/z — чистая зона у камеры, w — низ кадра (доля высоты)
            float4 _Towers[MAX_TOWERS];
            float4 _TowerRot[MAX_TOWERS];
            float _TowerCount;
            float _ImpDeckTop;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 slices : TEXCOORD1;   // x, y — слои массива, z — смесь, w — затухание
                float3 wpos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 c = v.vertex.xyz;
                float W = v.data.x, H = v.data.y;
                float3 to = c - _ImpCam.xyz;
                float2 f = to.xz;
                float lf = length(f);
                f = lf > 1e-3 ? f / lf : float2(0, 1);
                // Квад целиком к камере (наклоняется при взгляде сверху), опора — середина облака:
                // вертикальная картинка сверху сплющивалась бы в полосу.
                float3 cc = c + float3(0, 0.5 * H, 0);
                float3 f3 = normalize(cc - _ImpCam.xyz);
                float3 right = float3(f.y, 0, -f.x);
                float3 upv = normalize(cross(f3, right));
                float vy = v.uv.y * (1.0 + 2.0 * _ImpParams.w) - _ImpParams.w - 0.5;
                float3 wpos = cc + right * ((v.uv.x - 0.5) * W) + upv * (vy * H);
                o.pos = mul(_ImpVP, float4(wpos, 1.0));
                o.uv = v.uv;
                o.wpos = wpos;

                // Азимут камеры относительно облака (направление от облака к камере).
                float az = atan2(-f.y, -f.x);
                float k = frac(az / (2.0 * UNITY_PI)) * FRAMES;
                float k0 = floor(k);
                float base = v.data.z * FRAMES;
                o.slices.x = base + fmod(k0, FRAMES);
                o.slices.y = base + fmod(k0 + 1.0, FRAMES);
                o.slices.z = k - k0;

                // Камера внутри облака — квад растворяется (настоящего объёма вблизи у него нет).
                float R = W / 2.7;
                float inside = (_ImpCam.y > c.y - 0.1 * H && _ImpCam.y < c.y + 1.05 * H) ? 1.0 : 0.0;
                float fadeIn = lerp(1.0, smoothstep(R * 0.9, R * 1.9, lf), inside);
                // Снимки сделаны сбоку: круто сверху/снизу облако растворяется (там видно море облаков).
                float steep = 1.0 - smoothstep(0.72, 0.93, abs(f3.y));
                // Близко к камере облако гаснет целиком (по центру, а не по пикселям — иначе в нём «дыра»).
                float near = smoothstep(_ImpParams.z + 0.4 * R, _ImpParams.z + 1.6 * R, length(cc - _ImpCam.xyz));
                o.slices.w = fadeIn * steep * near;
                return o;
            }

            bool HeroInFront(float3 ro, float3 rd, float dist)
            {
                for (int i = 0; i < MAX_TOWERS; i++)
                {
                    if (i >= (int)_TowerCount) break;
                    float4 T = _Towers[i];
                    float hh = _TowerRot[i].w;
                    float3 cc = T.xyz + float3(0, T.w * hh * 0.5, 0);
                    float r = T.w * max(1.5, hh * 0.62);
                    float3 oc = ro - cc;
                    float b = dot(oc, rd);
                    float q = dot(oc, oc) - r * r;
                    float disc = b * b - q;
                    if (disc <= 0.0) continue;
                    float t0 = -b - sqrt(disc);
                    if (t0 < dist && -b + sqrt(disc) > 0.0) return true;
                }
                return false;
            }

            half4 frag (v2f i) : SV_Target
            {
                float4 s0 = UNITY_SAMPLE_TEX2DARRAY(_ImpTex, float3(i.uv, i.slices.x));
                float4 s1 = UNITY_SAMPLE_TEX2DARRAY(_ImpTex, float3(i.uv, i.slices.y));
                float4 s = lerp(s0, s1, i.slices.z);
                float a = s.a;
                if (a < 0.003) discard;
                float3 light = s.rgb * s.rgb * 6.0;

                float3 ro = _ImpCam.xyz;
                float3 d = i.wpos - ro;
                float dist = length(d);
                float3 rd = d / max(dist, 1e-3);

                // Воздушная перспектива — как у объёмных башен.
                light = lerp(light, SkyTidePanorama(rd) * a, _ImpParams.x * saturate(SkyTideAerial(dist)));

                float m = i.slices.w;
                // Низ облака тонет в объёмном море облаков.
                m *= smoothstep(_ImpDeckTop - 25.0, _ImpDeckTop + 15.0, i.wpos.y);

                // Экранные координаты (как у проходов объёмных облаков).
                float4 cp = mul(_CamVP, float4(i.wpos, 1.0));
                float2 suv = cp.xy / cp.w * 0.5 + 0.5;
                float eye = -mul(_CamV, float4(i.wpos, 1.0)).z;

                // Корабль (непрозрачная сцена) ближе плоскости облака — облако за ним.
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, suv);
                if (Linear01Depth(raw) < 0.999)
                    m *= saturate((LinearEyeDepth(raw) - eye) / 25.0 + 0.5);

                // Героическая объёмная башня перед облаком — закрывает его по своему пропусканию.
                if (HeroInFront(ro, rd, dist))
                    m *= tex2D(_CloudTex, suv).a;

                return half4(light * m, a * m);
            }
            ENDCG
        }
    }
    Fallback Off
}
