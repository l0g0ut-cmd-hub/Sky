// Облако-спрайт: живые края (медленное клубление по шуму и эрозия тонких мест),
// свет от направления взгляда на солнце (контровое золото / сиреневая тень),
// плавное растворение вблизи камеры и в цвете неба на дистанции.
Shader "SkyTide/CloudSprite"
{
    Properties
    {
        _MainTex ("Atlas (RGBA)", 2D) = "white" {}
        _LitTint ("Tint toward sun", Color) = (1.12, 0.86, 0.66, 1)
        _ShadowTint ("Tint away from sun", Color) = (0.66, 0.55, 0.74, 1)
        _GlowColor ("Backlight rim glow", Color) = (1.0, 0.6, 0.24, 1)
        _GlowStrength ("Rim glow strength", Float) = 1.8
        _Distort ("Edge billow (UV)", Float) = 0.010
        _Erode ("Edge erosion", Float) = 0.45
        _FlowSpeed ("Billow speed", Float) = 0.012
        _NoiseScale ("Billow size", Float) = 2.2
        _NearFade ("Near fade start/end (m)", Vector) = (20, 85, 0, 0)
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
            float4 _LitTint;
            float4 _ShadowTint;
            float4 _GlowColor;
            float _GlowStrength;
            float _Distort;
            float _Erode;
            float _FlowSpeed;
            float _NoiseScale;
            float4 _NearFade;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD1;
                float2 noiseUV : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float phase = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                // Шум в локальных координатах квада: узор живёт вместе с облаком и не «едет» при повороте к камере.
                o.noiseUV = v.vertex.xy * _NoiseScale + phase * 7.31;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 n1 = tex2D(_SkyTideNoise, i.noiseUV + float2(t, t * 0.7)).rg;
                float2 n2 = tex2D(_SkyTideNoise, i.noiseUV * 1.93 + float2(-t * 0.8, t * 1.3)).rg;
                float2 n = (n1 + n2) * 0.5 - 0.5;

                float4 tex = tex2D(_MainTex, i.uv + n * _Distort);
                float a = tex.a * i.color.a;
                // Эрозия: тонкие края медленно тают и нарастают, плотная середина не трогается.
                float er = (n1.r + n2.g) * 0.5;
                a = saturate(a - er * _Erode * (1.0 - a) * 1.6);

                float3 view = i.worldPos - _WorldSpaceCameraPos;
                float dist = length(view);
                float3 vdir = view / max(dist, 1e-3);
                float sunDot = SkyTideSunDot(vdir);
                float toward = saturate(sunDot * 0.5 + 0.5);

                float3 col = tex.rgb * i.color.rgb * lerp(_ShadowTint.rgb, _LitTint.rgb, toward * toward);
                float back = pow(saturate(sunDot), 4.0);
                float thin = saturate(a * (1.0 - a) * 4.0);
                col += _GlowColor.rgb * (thin * _GlowStrength + 0.18 * a) * back;

                // Воздушная перспектива: дальние облака растворяются в цвете неба за ними.
                col = lerp(col, SkyTidePanorama(vdir), SkyTideAerial(dist));

                a *= saturate((dist - _NearFade.x) / max(_NearFade.y - _NearFade.x, 1.0));
                return fixed4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
