// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)
// Modified version with configurable culling for Cozen/Enigma Launchpad

Shader "Cozen/Surface/Standard"
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

        // Realtime emission enable. Drives the _EMISSION keyword via the
        // built-in Toggle drawer, so the default material inspector shows an
        // "Enable Emission" checkbox even without a CustomEditor.
        [Toggle(_EMISSION)] _EmissionEnabled ("Enable Emission", Float) = 0
        _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        // Brightness multiplier on the assembled emission. Enigma's button/
        // fader runtime drives this per-renderer (SetFloat "_EmissionStrength")
        // to glow active/inactive rings and dim non-interactable/empty slots
        // to zero. Range allows HDR-ish over-drive for a bloom/glow look.
        _EmissionStrength ("Emission Strength", Range(0, 10)) = 1

        // Emission source: Texture uses _EmissionMap, Cubemap samples _EmissionCube,
        // SkyboxProbe samples unity_SpecCube0 (so a realtime reflection probe auto-feeds it).
        [KeywordEnum(Texture, Cubemap, SkyboxProbe)] _EmissionSource ("Emission Source", Float) = 0
        _EmissionCube ("Emission Cubemap", Cube) = "black" {}
        // Mip selector for cubemap/skybox probe source. 0 = sharp, higher = blurrier.
        _EmissionSkyMip ("Emission Sky Mip", Range(0, 8)) = 0

        // Emission mask gates which parts emit (applies to any source above).
        [Toggle(_EMISSIONMASK_ON)] _EmissionMaskEnabled ("Enable Emission Mask", Float) = 0
        _EmissionMask("Emission Mask", 2D) = "white" {}
        [Enum(R,0,G,1,B,2,A,3)] _EmissionMaskChannel ("Emission Mask Channel", Float) = 0
        [Toggle] _EmissionMaskInvert ("Invert Emission Mask", Float) = 0

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
        
        // Culling mode - exposed property like Mochie Standard
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2.0
    }

    CGINCLUDE
        #define UNITY_SETUP_BRDF_INPUT MetallicSetup

        // --- Emission mask + source override (Cozen addition) ---
        // Skip the whole block in the ShadowCaster pass — that pass uses
        // UnityStandardShadow.cginc which declares _Color itself, and we
        // don't need emission during shadow rendering anyway.
        #ifndef UNITY_PASS_SHADOWCASTER

        #pragma shader_feature_local _EMISSIONMASK_ON
        #pragma shader_feature_local _EMISSIONSOURCE_TEXTURE _EMISSIONSOURCE_CUBEMAP _EMISSIONSOURCE_SKYBOXPROBE

        // Mask uniforms. Declared unconditionally so setting the properties
        // doesn't break when the keyword is off.
        sampler2D _EmissionMask;
        float4 _EmissionMask_ST;
        half _EmissionMaskInvert;
        half _EmissionMaskChannel; // 0=R, 1=G, 2=B, 3=A

        // Cubemap source
        samplerCUBE _EmissionCube;
        half _EmissionSkyMip;

        // Emission brightness multiplier (see Properties). Declared
        // unconditionally; overridden per-renderer via MaterialPropertyBlock
        // by the Enigma runtime. Defaults to 1 so non-Enigma materials are
        // unaffected.
        half _EmissionStrength;

        // Rename Unity's built-in Emission so we can wrap it. We pull in
        // UnityStandardInput here under the renamed symbol; subsequent chain
        // includes from the Standard pipeline are no-ops thanks to the header
        // guard, so our replacement wins everywhere Emission() is called.
        #define Emission Emission_Base
        #include "UnityStandardInput.cginc"
        #undef Emission

        half CozenSampleEmissionMask(float2 uv)
        {
        #if defined(_EMISSIONMASK_ON)
            float2 muv = TRANSFORM_TEX(uv, _EmissionMask);
            half4 ms = tex2D(_EmissionMask, muv);
            half m = ms.r;
            if (_EmissionMaskChannel > 2.5) m = ms.a;
            else if (_EmissionMaskChannel > 1.5) m = ms.b;
            else if (_EmissionMaskChannel > 0.5) m = ms.g;
            if (_EmissionMaskInvert > 0.5) m = 1.0 - m;
            return saturate(m);
        #else
            return 1.0;
        #endif
        }

        // Assemble emission from the selected source and apply mask + color tint.
        // normalWorld is only used for Cubemap / SkyboxProbe sources; ignored for Texture.
        half3 CozenEmission(float2 uv, float3 normalWorld)
        {
        #ifndef _EMISSION
            return half3(0,0,0);
        #else
            half3 em;
            #if defined(_EMISSIONSOURCE_CUBEMAP)
                em = texCUBElod(_EmissionCube, float4(normalize(normalWorld), _EmissionSkyMip)).rgb * _EmissionColor.rgb;
            #elif defined(_EMISSIONSOURCE_SKYBOXPROBE)
                half4 skyHdr = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, normalize(normalWorld), _EmissionSkyMip);
                em = DecodeHDR(skyHdr, unity_SpecCube0_HDR) * _EmissionColor.rgb;
            #else
                em = Emission_Base(uv); // default: texture path, already includes color tint
            #endif

            #if defined(_EMISSIONMASK_ON)
                em *= CozenSampleEmissionMask(uv);
            #endif
            return em * _EmissionStrength;
        #endif
        }

        // Replace Emission(uv) with a macro so the call site's `s` (FragmentCommonData,
        // has normalWorld) gets forwarded to our function. Forward Base & Deferred both
        // expand FRAGMENT_SETUP(s) before calling Emission. Meta pass doesn't have `s`,
        // so we degrade to texture-only emission there (lightmap baking doesn't need sky).
        #if defined(UNITY_PASS_META)
            #define Emission(uv) Emission_Base(uv)
        #else
            #define Emission(uv) CozenEmission(uv, s.normalWorld)
        #endif

        #endif // !UNITY_PASS_SHADOWCASTER
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 300

        // Apply culling setting
        Cull [_Cull]

        // ------------------------------------------------------------------
        //  Base forward pass (directional light, emission, lightmaps, ...)
        PASS
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
            #pragma shader_feature _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _DETAIL_MULX2
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex vertBase
            #pragma fragment fragBase
            #include "UnityStandardCoreForward.cginc"

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
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _DETAIL_MULX2
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex vertAdd
            #pragma fragment fragAdd
            #include "UnityStandardCoreForward.cginc"

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
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _PARALLAXMAP
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex vertShadowCaster
            #pragma fragment fragShadowCaster

            #include "UnityStandardShadow.cginc"

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
            #pragma shader_feature _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _DETAIL_MULX2
            #pragma shader_feature_local _PARALLAXMAP

            #pragma multi_compile_prepassfinal
            #pragma multi_compile_instancing
            // Uncomment the following line to enable dithering LOD crossfade. Note: there are more in the file to uncomment for other passes.
            //#pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma vertex vertDeferred
            #pragma fragment fragDeferred

            #include "UnityStandardCore.cginc"

            ENDCG
        }

        // ------------------------------------------------------------------
        // Extracts information for lightmapping, GI (emission, albedo, ...)
        // This pass is not used during regular rendering.
        Pass
        {
            Name "META" 
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta

            #pragma shader_feature _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _DETAIL_MULX2
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "UnityStandardMeta.cginc"
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 150

        // Apply culling setting
        Cull [_Cull]

        // ------------------------------------------------------------------
        //  Base forward pass (directional light, emission, lightmaps, ...)
        Pass
        {
            Name "FORWARD" 
            Tags { "LightMode" = "ForwardBase" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma target 2.0
            
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
            // SM2.0: NOT SUPPORTED shader_feature_local _DETAIL_MULX2
            // SM2.0: NOT SUPPORTED shader_feature_local _PARALLAXMAP

            #pragma skip_variants SHADOWS_SOFT DIRLIGHTMAP_COMBINED

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #pragma vertex vertBase
            #pragma fragment fragBase
            #include "UnityStandardCoreForward.cginc"

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
            #pragma target 2.0

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _DETAIL_MULX2
            // SM2.0: NOT SUPPORTED shader_feature_local _PARALLAXMAP
            #pragma skip_variants SHADOWS_SOFT
            
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            
            #pragma vertex vertAdd
            #pragma fragment fragAdd
            #include "UnityStandardCoreForward.cginc"

            ENDCG
        }
        // ------------------------------------------------------------------
        //  Shadow rendering pass
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On ZTest LEqual

            CGPROGRAM
            #pragma target 2.0

            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma skip_variants SHADOWS_SOFT
            #pragma multi_compile_shadowcaster

            #pragma vertex vertShadowCaster
            #pragma fragment fragShadowCaster

            #include "UnityStandardShadow.cginc"

            ENDCG
        }

        // ------------------------------------------------------------------
        // Extracts information for lightmapping, GI (emission, albedo, ...)
        // This pass is not used during regular rendering.
        Pass
        {
            Name "META" 
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta

            #pragma shader_feature _EMISSION
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _DETAIL_MULX2
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "UnityStandardMeta.cginc"
            ENDCG
        }
    }


    FallBack "VertexLit"
}
