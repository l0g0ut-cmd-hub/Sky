// Unity 6000.0.36f1 Standard, with rigid cannon recoil vertex wrappers.
// Original source: https://download.unity3d.com/download_unity/9fe3b5f71dbb/builtin_shaders-6000.0.36f1.zip
// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see UnityShaderLicense.txt)

Shader "SkyTide/ShipIron"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "white" {}

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Glossiness("Smoothness", Range(0.0, 1.0)) = 0.5
        _GlossMapScale("Smoothness Scale", Range(0.0, 1.0)) = 1.0
        [Enum(Metallic Alpha,0,Albedo Alpha,1)] _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0

        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _GlossyReflections("Glossy Reflections", Float) = 1.0

        _BumpScale("Scale", Float) = 1.0
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax ("Height Scale", Range (0.005, 0.08)) = 0.02
        _ParallaxMap ("Height Map", 2D) = "black" {}

        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        _DetailMask("Detail Mask", 2D) = "white" {}

        _DetailAlbedoMap("Detail Albedo x2", 2D) = "grey" {}
        _DetailNormalMapScale("Scale", Float) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}

        [Enum(UV0,0,UV1,1)] _UVSec ("UV Set for secondary textures", Float) = 0


        // Blending state
        [HideInInspector] _Mode ("__mode", Float) = 0.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
    }

    CGINCLUDE
        #define UNITY_SETUP_BRDF_INPUT MetallicSetup
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 300


        // ------------------------------------------------------------------
        //  Base forward pass (directional light, emission, lightmaps, ...)
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma target 3.0

            // -------------------------------------

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _GLOSSYREFLECTIONS_OFF
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex SkyTideVertBase
            #pragma fragment fragBase
            #include "UnityStandardCoreForward.cginc"

            #include "ShipGunRecoil.cginc"
            #if UNITY_STANDARD_SIMPLE
                #define SKYTIDE_BASE_OUTPUT VertexOutputBaseSimple
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAddSimple
            #else
                #define SKYTIDE_BASE_OUTPUT VertexOutputForwardBase
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAdd
            #endif
            SKYTIDE_BASE_OUTPUT SkyTideVertBase(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertBase(v);
            }
            SKYTIDE_ADD_OUTPUT SkyTideVertAdd(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertAdd(v);
            }


            ENDCG
        }
        // ------------------------------------------------------------------
        //  Additive forward pass (one light per pass)
        Pass
        {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            Blend [_SrcBlend] One
            Fog { Color (0,0,0,0) } // in additive pass fog should be black
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.0

            // -------------------------------------


            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex SkyTideVertAdd
            #pragma fragment fragAdd
            #include "UnityStandardCoreForward.cginc"

            #include "ShipGunRecoil.cginc"
            #if UNITY_STANDARD_SIMPLE
                #define SKYTIDE_BASE_OUTPUT VertexOutputBaseSimple
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAddSimple
            #else
                #define SKYTIDE_BASE_OUTPUT VertexOutputForwardBase
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAdd
            #endif
            SKYTIDE_BASE_OUTPUT SkyTideVertBase(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertBase(v);
            }
            SKYTIDE_ADD_OUTPUT SkyTideVertAdd(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertAdd(v);
            }


            ENDCG
        }
        // ------------------------------------------------------------------
        //  Shadow rendering pass
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On ZTest LEqual

            CGPROGRAM
            #pragma target 3.0

            // -------------------------------------


            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _PARALLAXMAP
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex SkyTideVertShadowCaster
            #pragma fragment fragShadowCaster

            #include "UnityStandardShadow.cginc"

            #include "ShipGunRecoil.cginc"
            void SkyTideVertShadowCaster(VertexInput v, float4 color : COLOR, out VertexOutput output
                #ifdef UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT
                    , out VertexOutputShadowCaster o
                #endif
                #ifdef UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT
                    , out VertexOutputStereoShadowCaster os
                #endif
            )
            {
                SkyTideGunVertex(v.vertex, color);
                vertShadowCaster(v, output
                    #ifdef UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT
                        , o
                    #endif
                    #ifdef UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT
                        , os
                    #endif
                );
            }


            ENDCG
        }
        // ------------------------------------------------------------------
        //  Deferred pass
        Pass
        {
            Name "DEFERRED"
            Tags { "LightMode" = "Deferred" }

            CGPROGRAM
            #pragma target 3.0
            #pragma exclude_renderers nomrt


            // -------------------------------------

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_prepassfinal
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex SkyTideVertDeferred
            #pragma fragment fragDeferred

            #include "UnityStandardCore.cginc"
            #include "ShipGunRecoil.cginc"
            VertexOutputDeferred SkyTideVertDeferred(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertDeferred(v);
            }


            ENDCG
        }

        // ------------------------------------------------------------------
        // Extracts information for lightmapping, GI (emission, albedo, ...)
        // This pass it not used during regular rendering.
        Pass
        {
            Name "META"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta

            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "UnityStandardMeta.cginc"
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 150

        // ------------------------------------------------------------------
        //  Base forward pass (directional light, emission, lightmaps, ...)
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma target 3.0

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _GLOSSYREFLECTIONS_OFF
            // SM2.0: NOT SUPPORTED shader_feature_local _DETAIL_MULX2
            // SM2.0: NOT SUPPORTED shader_feature_local _PARALLAXMAP

            #pragma skip_variants SHADOWS_SOFT DIRLIGHTMAP_COMBINED

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #pragma vertex SkyTideVertBase
            #pragma fragment fragBase
            #include "UnityStandardCoreForward.cginc"

            #include "ShipGunRecoil.cginc"
            #if UNITY_STANDARD_SIMPLE
                #define SKYTIDE_BASE_OUTPUT VertexOutputBaseSimple
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAddSimple
            #else
                #define SKYTIDE_BASE_OUTPUT VertexOutputForwardBase
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAdd
            #endif
            SKYTIDE_BASE_OUTPUT SkyTideVertBase(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertBase(v);
            }
            SKYTIDE_ADD_OUTPUT SkyTideVertAdd(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertAdd(v);
            }


            ENDCG
        }
        // ------------------------------------------------------------------
        //  Additive forward pass (one light per pass)
        Pass
        {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            Blend [_SrcBlend] One
            Fog { Color (0,0,0,0) } // in additive pass fog should be black
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.0

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            // SM2.0: NOT SUPPORTED shader_feature_local _PARALLAXMAP
            #pragma skip_variants SHADOWS_SOFT

            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog

            #pragma vertex SkyTideVertAdd
            #pragma fragment fragAdd
            #include "UnityStandardCoreForward.cginc"

            #include "ShipGunRecoil.cginc"
            #if UNITY_STANDARD_SIMPLE
                #define SKYTIDE_BASE_OUTPUT VertexOutputBaseSimple
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAddSimple
            #else
                #define SKYTIDE_BASE_OUTPUT VertexOutputForwardBase
                #define SKYTIDE_ADD_OUTPUT VertexOutputForwardAdd
            #endif
            SKYTIDE_BASE_OUTPUT SkyTideVertBase(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertBase(v);
            }
            SKYTIDE_ADD_OUTPUT SkyTideVertAdd(VertexInput v, float4 color : COLOR)
            {
                SkyTideGunVertex(v.vertex, color);
                return vertAdd(v);
            }


            ENDCG
        }
        // ------------------------------------------------------------------
        //  Shadow rendering pass
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On ZTest LEqual

            CGPROGRAM
            #pragma target 3.0

            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma skip_variants SHADOWS_SOFT
            #pragma multi_compile_shadowcaster

            #pragma vertex SkyTideVertShadowCaster
            #pragma fragment fragShadowCaster

            #include "UnityStandardShadow.cginc"

            #include "ShipGunRecoil.cginc"
            void SkyTideVertShadowCaster(VertexInput v, float4 color : COLOR, out VertexOutput output
                #ifdef UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT
                    , out VertexOutputShadowCaster o
                #endif
                #ifdef UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT
                    , out VertexOutputStereoShadowCaster os
                #endif
            )
            {
                SkyTideGunVertex(v.vertex, color);
                vertShadowCaster(v, output
                    #ifdef UNITY_STANDARD_USE_SHADOW_OUTPUT_STRUCT
                        , o
                    #endif
                    #ifdef UNITY_STANDARD_USE_STEREO_SHADOW_OUTPUT_STRUCT
                        , os
                    #endif
                );
            }


            ENDCG
        }

        // ------------------------------------------------------------------
        // Extracts information for lightmapping, GI (emission, albedo, ...)
        // This pass it not used during regular rendering.
        Pass
        {
            Name "META"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta

            #pragma shader_feature_fragment _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _DETAIL_MULX2
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "UnityStandardMeta.cginc"
            ENDCG
        }
    }


    FallBack "VertexLit"
    CustomEditor "StandardShaderGUI"
}
