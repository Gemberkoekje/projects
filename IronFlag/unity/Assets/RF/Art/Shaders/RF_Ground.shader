// What every sheet of land and every bank below one is painted with.
//
// URP's Lit shader with one thing added and several taken away. What is added is a
// perturbation of the normal from procedural noise, at a scale and a strength read out of
// SurfaceLook - so sand is dune-scale and coarse, grass is fine, and a road is very nearly
// as flat as it was. What is taken away is everything a map has no use for: no base map, no
// normal map, no metallic, no detail set, no alpha clip, no emission. The base colour is
// the whole of the albedo and it comes out of SurfaceTuning exactly as before.
//
// Nothing here changes what a surface is painted. The measured value ramp that M7 and the
// surfaces pass argued over is the _BaseColor, untouched, and a normal that leans a couple
// of degrees off vertical moves what the sun does to it and not what colour it is.
Shader "IronFlag/Ground"
{
    Properties
    {
        [MainColor] _BaseColor("Base Colour", Color) = (0.5, 0.5, 0.5, 1.0)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.1

        _DetailScale("Detail Scale (metres)", Float) = 1.5
        _DetailStrength("Detail Strength", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        LOD 300

        HLSLINCLUDE
        #include "RF_Surface.hlsl"

        // One block, so the SRP batcher can draw a map's dozen sheets without rebinding
        // anything between them. A property outside this is a property that silently costs
        // a draw call per material.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;
            float _DetailScale;
            float _DetailStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RfVertex
            #pragma fragment GroundFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            half4 GroundFragment(RfVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = RfRoughen(
                    normalize(input.normalWS), input.positionWS, _DetailScale, _DetailStrength);

                InputData lighting = RfInput(input, normalWS);
                SurfaceData surface = RfSurface(
                    _BaseColor.rgb, _Smoothness, half3(0.0, 0.0, 0.0));

                half4 colour = UniversalFragmentPBR(lighting, surface);
                colour.rgb = MixFog(colour.rgb, lighting.fogCoord);
                colour.a = 1.0;
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RfShadowVertex
            #pragma fragment RfDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RfDepthVertex
            #pragma fragment RfDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RfDepthVertex
            #pragma fragment RfDepthNormalsFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
