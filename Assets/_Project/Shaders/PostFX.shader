// Постобработка «Небесного прилива» (встроенный рендер, OnRenderImage):
// 0 — порог блума, 1 — уменьшение, 2 — увеличение с наложением (тент 3×3),
// 3 — маска лучей солнца (яркое небо и края облаков вокруг солнца), 4 — радиальное размытие лучей,
// 5 — итог: блум + лучи → экспозиция, контраст, насыщенность → ACES → раздельное тонирование, виньетка, дизеринг.
Shader "Hidden/SkyTide/Post"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D _ShaftTex;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    float4 _Threshold;      // x порог, y мягкость колена, z ограничение яркости
    float _SampleScale;
    float4 _SunScreen;      // xy солнце в UV экрана, z видимость, w соотношение сторон
    float4 _ShaftParams;    // x длина, y затухание, z вес шага, w сила
    float4 _ShaftColor;
    float _BloomIntensity;
    float4 _BloomTint;
    float _Exposure;
    float _Contrast;
    float _Saturation;
    float4 _ShadowTone;
    float4 _HighlightTone;
    float _ToneStrength;
    float _Vignette;
    float4 _VignetteColor;

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

    half3 Box4(float2 uv)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb
                + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25;
    }

    half4 fragPrefilter (v2f i) : SV_Target
    {
        half3 c = min(Box4(i.uv), _Threshold.zzz);
        half br = max(c.r, max(c.g, c.b));
        half soft = clamp(br - _Threshold.x + _Threshold.y, 0, 2 * _Threshold.y);
        soft = soft * soft / (4 * _Threshold.y + 1e-4);
        half contrib = max(soft, br - _Threshold.x) / max(br, 1e-4);
        return half4(c * contrib, 1);
    }

    half4 fragDown (v2f i) : SV_Target
    {
        return half4(Box4(i.uv), 1);
    }

    half4 fragUp (v2f i) : SV_Target
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0) * _SampleScale;
        half3 s = tex2D(_MainTex, i.uv - d.xy).rgb;
        s += tex2D(_MainTex, i.uv - d.wy).rgb * 2;
        s += tex2D(_MainTex, i.uv - d.zy).rgb;
        s += tex2D(_MainTex, i.uv + d.zw).rgb * 2;
        s += tex2D(_MainTex, i.uv).rgb * 4;
        s += tex2D(_MainTex, i.uv + d.xw).rgb * 2;
        s += tex2D(_MainTex, i.uv + d.zy).rgb;
        s += tex2D(_MainTex, i.uv + d.wy).rgb * 2;
        s += tex2D(_MainTex, i.uv + d.xy).rgb;
        return half4(s * (1.0 / 16), 1);
    }

    half4 fragShaftMask (v2f i) : SV_Target
    {
        float2 duv = i.uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) duv.y = 1 - duv.y;
        #endif
        float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, duv));
        half sky = step(0.995, depth);   // небо и облака-спрайты (они не пишут глубину); корабль заслоняет
        half3 c = tex2D(_MainTex, i.uv).rgb;
        float2 v = (i.uv - _SunScreen.xy) * float2(_SunScreen.w, 1);
        half fall = saturate(1 - length(v) * 1.1);
        // Маска не должна повторно возводить HDR-яркость облака в квадрат:
        // это давало белую завесу через корпус даже при умеренном блуме.
        half lum = saturate((dot(c, half3(0.2126, 0.7152, 0.0722)) - 0.7) / 1.8);
        half3 source = min(c, half3(2.0, 2.0, 2.0));
        return half4(source * lum * sky * fall * fall, 1);
    }

    half4 fragShaftBlur (v2f i) : SV_Target
    {
        float2 delta = (_SunScreen.xy - i.uv) * (_ShaftParams.x / 12.0);
        float2 uv = i.uv;
        half3 acc = 0;
        half w = 1;
        for (int k = 0; k < 12; k++)
        {
            acc += tex2D(_MainTex, uv).rgb * w;
            w *= _ShaftParams.y;
            uv += delta;
        }
        return half4(acc * _ShaftParams.z, 1);
    }

    half3 ACES (half3 x)
    {
        const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    half4 fragFinal (v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb;
        c += tex2D(_BloomTex, i.uv).rgb * _BloomIntensity * _BloomTint.rgb;
        c += tex2D(_ShaftTex, i.uv).rgb * _ShaftColor.rgb * (_ShaftParams.w * _SunScreen.z);

        c *= _Exposure;
        // Контраст вокруг средне-серого (в линейном пространстве), насыщенность.
        c = 0.18 * pow(max(c, 1e-5) / 0.18, _Contrast);
        half l = Luminance(c);
        c = max(0, lerp(l.xxx, c, _Saturation));

        c = ACES(c);

        // Раздельное тонирование: холодные тени, тёплые света (как на концепте).
        half lt = Luminance(c);
        half3 tone = lerp(_ShadowTone.rgb, _HighlightTone.rgb, smoothstep(0.05, 0.75, lt));
        c = lerp(c, c * tone * 2.0, _ToneStrength);

        float2 v = (i.uv - 0.5) * float2(1.0, 0.8);
        half vig = saturate(dot(v, v) * 2.2 * _Vignette);
        c = lerp(c, c * _VignetteColor.rgb, vig);

        // Дизеринг от полос на плавных градиентах неба.
        float n = frac(sin(dot(i.uv * _ScreenParams.xy, float2(12.9898, 78.233))) * 43758.5453);
        c += (n - 0.5) / 255.0;
        return half4(c, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG }
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragShaftMask
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragShaftBlur
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFinal
            #pragma target 3.0
            ENDCG }
    }
    Fallback Off
}
