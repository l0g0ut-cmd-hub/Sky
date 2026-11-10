// Облако-спрайт.
// • Три варианта одной формы (против солнца / нейтральный / в тени) в UV0/UV1/UV2: облако плавно переходит
//   между ними по углу между взглядом и солнцем — свет честно меняется при облёте.
// • Живые края: медленное клубление по шуму и эрозия тонких мест (плотная середина не трогается).
// • Плавное растворение вблизи камеры (можно пролететь насквозь) и в цвете неба на дистанции.
Shader "SkyTide/CloudSprite"
{
    Properties
    {
        _MainTex ("Atlas (RGBA)", 2D) = "white" {}
        _Brightness ("Brightness", Float) = 1.0
        _SunWarmth ("Extra warmth toward sun", Float) = 0.12
        _Distort ("Edge billow (UV)", Float) = 0.0035
        _Erode ("Edge erosion", Float) = 0.3
        _FlowSpeed ("Billow speed", Float) = 0.01
        _NoiseScale ("Billow size", Float) = 2.4
        _NearFade ("Near fade start/end (m)", Vector) = (70, 260, 0, 0)
        [HideInInspector] _CloudPhase ("Per-cloud phase", Float) = 0
        [HideInInspector] _EdgeOn ("Edge-on fade (cos start/end)", Vector) = (0.5, 0.8, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SkyTide.cginc"

            sampler2D _MainTex;
            float _Brightness;
            float _SunWarmth;
            float _Distort;
            float _Erode;
            float _FlowSpeed;
            float _NoiseScale;
            float4 _NearFade;
            float _CloudPhase;
            float4 _EdgeOn;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uvLit : TEXCOORD0;
                float2 uvMid : TEXCOORD1;
                float2 uvShadow : TEXCOORD2;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uvLit : TEXCOORD0;
                float2 uvMid : TEXCOORD1;
                float2 uvShadow : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
                float2 noiseUV : TEXCOORD4;
                float3 weights : TEXCOORD5;   // против солнца / нейтрально / в тени
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // Разворот к камере — прямо в шейдере по текущей матрице вида (без отставания на кадр, без дрожи):
                // цилиндрический билборд, ось — мировая вертикаль, ширина — вдоль «правой» оси камеры.
                float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float3 camRight = float3(UNITY_MATRIX_V[0].x, 0.0, UNITY_MATRIX_V[0].z);
                camRight = normalize(camRight + float3(1e-5, 0, 0));
                float3 wpos = center + camRight * (v.vertex.x * sx) + float3(0, v.vertex.y * sy, 0);
                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uvLit = v.uvLit;
                o.uvMid = v.uvMid;
                o.uvShadow = v.uvShadow;
                o.color = v.color;
                o.worldPos = wpos;

                float3 origin = center;
                // Постоянная фаза облака (задаётся один раз при создании). Хэш от мировой позиции здесь нельзя:
                // облако плывёт по ветру, хэш скачет каждый кадр — узор клубления дрожал.
                float phase = _CloudPhase;
                // Шум в локальных координатах квада: узор живёт вместе с облаком и не «едет» при повороте к камере.
                o.noiseUV = v.vertex.xy * _NoiseScale + phase * 7.31;

                // Вариант света — один на всё облако, по направлению с камеры на его центр.
                float s = SkyTideSunDot(origin - _WorldSpaceCameraPos);
                float lit = smoothstep(0.15, 0.85, s);
                float shadow = smoothstep(0.1, 0.8, -s);
                o.weights = float3(lit, 1.0 - lit - shadow, shadow);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 n1 = tex2D(_SkyTideNoise, i.noiseUV + float2(t, t * 0.7)).rg;
                float2 n2 = tex2D(_SkyTideNoise, i.noiseUV * 1.93 + float2(-t * 0.8, t * 1.3)).rg;
                float2 d = ((n1 + n2) * 0.5 - 0.5) * _Distort;

                float4 tex = tex2D(_MainTex, i.uvLit + d) * i.weights.x
                           + tex2D(_MainTex, i.uvMid + d) * i.weights.y
                           + tex2D(_MainTex, i.uvShadow + d) * i.weights.z;

                float a = tex.a * i.color.a;
                // Эрозия: тонкие края медленно тают и нарастают.
                float er = (n1.r + n2.g) * 0.5;
                a = saturate(a - er * _Erode * (1.0 - a) * 1.4);

                float3 view = i.worldPos - _WorldSpaceCameraPos;
                float dist = length(view);
                float3 vdir = view / max(dist, 1e-3);

                float3 col = tex.rgb * i.color.rgb * _Brightness;
                col *= 1.0 + _SunWarmth * i.weights.x * float3(1.0, 0.55, 0.2);

                // Воздушная перспектива: дальние облака растворяются в цвете неба за ними.
                col = lerp(col, SkyTidePanorama(vdir), SkyTideAerial(dist));

                a *= saturate((dist - _NearFade.x) / max(_NearFade.y - _NearFade.x, 1.0));
                // Картинка стоит вертикально: при взгляде сверху/снизу она видна ребром — растворяем.
                float vy = abs(i.worldPos.y - _WorldSpaceCameraPos.y) / max(dist, 1e-3);
                a *= 1.0 - smoothstep(_EdgeOn.x, _EdgeOn.y, vy);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
