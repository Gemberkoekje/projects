#ifndef RF_SURFACE_INCLUDED
#define RF_SURFACE_INCLUDED

// The half of the ground and the water that both of them are.
//
// Everything a map is drawn as is one of two shapes: a sheet lying flat at y = 0, or a
// bank standing straight up out of the water. Both are painted from a single flat colour
// out of SurfaceTuning, and what this file adds is the only thing that colour cannot say -
// which way the surface is facing, a hair away from square on, everywhere.
//
// The unevenness itself is RF_Noise.hlsl, which is where the argument for having no
// textures at all is written down. What is here is the two things this project's geometry
// makes easy and a general shader could not: which two directions a surface is measured
// along, given that every piece of it is either dead flat or dead vertical, and a lit pass
// with no lightmap, no tangent frame and no maps of any kind in it.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "RF_Noise.hlsl"

// ---------------------------------------------------------------------------------------
// Which way is along the surface
// ---------------------------------------------------------------------------------------

// Picks the two directions a surface is measured along, and the metres along them.
//
// A branch rather than a triplanar blend, and it is exact rather than an approximation:
// every piece of geometry this shader paints is either a sheet lying flat or a bank
// standing straight up - SurfaceMesh builds nothing else - so there is no in-between case
// whose seam a blend would be hiding. Flat ground is measured in world x and z; a bank is
// measured along its own run and down its own face, which is what keeps the grain on a
// coastline running with the coast instead of smearing down it.
void RfFrame(
    float3 normalWS,
    float3 positionWS,
    out float3 along,
    out float3 up,
    out float2 at)
{
    if (abs(normalWS.y) > 0.5)
    {
        along = float3(1.0, 0.0, 0.0);
        up = float3(0.0, 0.0, 1.0);
        at = positionWS.xz;
    }
    else
    {
        along = normalize(cross(float3(0.0, 1.0, 0.0), normalWS));
        up = cross(normalWS, along);
        at = float2(dot(positionWS, along), positionWS.y);
    }
}

// Tilts a normal by the slope of a height field sampled around the point.
//
// Finite differences rather than an analytic gradient, because the height field is two
// octaves of interpolated noise and its derivative is not worth writing down. The step is
// in noise units, so it is the same fraction of a lattice cell whatever the scale is, and
// dividing the difference by it turns a difference back into a slope.
float3 RfRoughen(float3 normalWS, float3 positionWS, float scale, float strength)
{
    if (strength <= 0.0 || scale <= 0.0)
    {
        return normalWS;
    }

    float3 along;
    float3 up;
    float2 at;
    RfFrame(normalWS, positionWS, along, up, at);
    at /= scale;

    const float step = 0.35;
    float here = RfGrain(at);
    float east = RfGrain(at + float2(step, 0.0));
    float north = RfGrain(at + float2(0.0, step));

    float3 slope = (along * (east - here)) + (up * (north - here));
    return normalize(normalWS - (slope * (strength / step)));
}

// ---------------------------------------------------------------------------------------
// The lit pass, which is the same shape for both surfaces
// ---------------------------------------------------------------------------------------

struct RfAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;

    // Metres to the coastline, negative out to sea. Written by SurfaceMesh only for the
    // sheets that are drawn over water, and zero everywhere else - see RF_Water.shader for
    // why zero is safe on the sea slab.
    float2 shore : TEXCOORD1;

    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RfVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float shore : TEXCOORD3;
    half fogFactor : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

RfVaryings RfVertex(RfAttributes input)
{
    RfVaryings output = (RfVaryings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);

    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = normal.normalWS;
    output.uv = input.uv;
    output.shore = input.shore.x;
    output.fogFactor = ComputeFogFactor(position.positionCS.z);

    return output;
}

// Fills in everything URP's lighting wants to know about where this pixel is.
//
// No lightmaps and no tangent frame: the map has neither, and a surface shader that
// declared them would carry two keywords and an interpolator for a case that cannot arise.
// The ambient probe is what the sky contributes, which on this project is the whole of the
// fill light - see LightingTuning.
InputData RfInput(RfVaryings input, float3 normalWS)
{
    InputData data = (InputData)0;

    data.positionWS = input.positionWS;
    data.normalWS = NormalizeNormalPerPixel(normalWS);
    data.viewDirectionWS = SafeNormalize(GetWorldSpaceNormalizeViewDir(input.positionWS));

#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    data.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
#endif

    data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    data.vertexLighting = half3(0.0, 0.0, 0.0);
    data.bakedGI = SampleSHPixel(half3(0.0, 0.0, 0.0), data.normalWS);
    data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    data.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

#if defined(DEBUG_DISPLAY)
    data.positionCS = input.positionCS;
#endif

    return data;
}

// Packs a flat colour and a gloss into the shape URP's PBR wants.
SurfaceData RfSurface(half3 albedo, half smoothness, half3 emission)
{
    SurfaceData data = (SurfaceData)0;

    data.albedo = albedo;
    data.specular = half3(0.0, 0.0, 0.0);
    data.metallic = 0.0;
    data.smoothness = smoothness;
    data.normalTS = half3(0.0, 0.0, 1.0);
    data.emission = emission;
    data.occlusion = 1.0;
    data.alpha = 1.0;
    data.clearCoatMask = 0.0;
    data.clearCoatSmoothness = 0.0;

    return data;
}

// ---------------------------------------------------------------------------------------
// The depth and shadow passes, which are the same for both surfaces
// ---------------------------------------------------------------------------------------

struct RfDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RfDepthVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

RfDepthVaryings RfDepthVertex(RfDepthAttributes input)
{
    RfDepthVaryings output = (RfDepthVaryings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;

    return output;
}

half4 RfDepthFragment(RfDepthVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return 0.0;
}

// The normals half of the depth-normals prepass, which is what this project's ambient
// occlusion is computed from - PC_Renderer's SSAO reads Source: DepthNormals. A surface
// shader missing this pass is a surface missing from that texture, and the ground is most
// of the screen.
half4 RfDepthNormalsFragment(RfDepthVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
}

// The shadow pass. Sheets lie flat and cast nothing worth seeing, but a bank is a wall
// more than a metre high standing in the water, and the sun is at 52 degrees.
float3 _LightDirection;

RfDepthVaryings RfShadowVertex(RfDepthAttributes input)
{
    RfDepthVaryings output = (RfDepthVaryings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);

    float4 positionCS = TransformWorldToHClip(
        ApplyShadowBias(position.positionWS, normal.normalWS, _LightDirection));

#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    output.positionCS = positionCS;
    output.normalWS = normal.normalWS;

    return output;
}

#endif
