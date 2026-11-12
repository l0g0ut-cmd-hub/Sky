Shader "Hidden/SkyTide/Hud"
{
    Properties { _MainTex ("Font atlas", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #include "UnityCG.cginc"
        struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
        struct Varying { float4 pos:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
        sampler2D _MainTex;
        Varying vert(Input v) { Varying o; o.pos=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
        float4 panel(Varying i):SV_Target { return i.color; }
        float4 glyph(Varying i):SV_Target { return float4(i.color.rgb, i.color.a * tex2D(_MainTex, i.uv).a); }
        // Нарисованные элементы набора HUD (UI/hud_kit): текстура × цвет (альфа из картинки).
        float4 sprite(Varying i):SV_Target { return tex2D(_MainTex, i.uv) * i.color; }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment panel
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment glyph
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment sprite
            ENDCG
        }
    }
}
