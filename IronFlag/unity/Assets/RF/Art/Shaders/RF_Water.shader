// What the open sea and the shelf that rims every coast are painted with.
//
// Opaque, like the flat material it replaces. Water on this map has nothing underneath it
// to be seen through - the sea is a slab whose top face is the surface, so a transparent
// water plane would be reading the depth of itself - and the soft shore edge that a depth
// fade is normally for comes from somewhere better here: SurfaceField already measures
// signed metres to the coastline for the whole map, and SurfaceMesh writes that number
// into UV1 of the sheets drawn over water. Foam and the shoreward wash are both driven off
// it, which costs no depth texture, works the same on Mobile_Renderer as on PC_Renderer,
// and puts the foam exactly on the coastline rather than approximately near it.
//
// The sea slab is a box rather than one of those sheets and so has no UV1 at all, which
// reads as a shore distance of zero - the coastline - everywhere on it. That would be foam
// over the whole ocean, so the slab's material carries _FoamWidth = 0 and the shoreward
// half of this shader switches itself off. See SurfaceLook.
//
// Everything moves off _RF_WaterTime, a global that WaterClock advances while a match is
// running and that is zero at every other moment. That is deliberate: a headless still of
// the sandbox renders a flat calm, so two renders of an unchanged map are the same image.
Shader "IronFlag/Water"
{
    Properties
    {
        [MainColor] _BaseColor("Base Colour", Color) = (0.035, 0.075, 0.135, 1.0)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.0

        _SwellStrength("Swell Strength", Range(0.0, 1.0)) = 0.35
        _SwellScale("Swell Scale (metres)", Float) = 9.0
        _SwellSpeed("Swell Speed", Float) = 1.0

        _ChopScale("Chop Scale (metres)", Float) = 1.8
        _ChopStrength("Chop Strength", Range(0.0, 1.0)) = 0.12

        _GlintColour("Glint Colour", Color) = (1.0, 0.98, 0.94, 1.0)
        _Glint("Glint Strength", Range(0.0, 2.0)) = 0.12
        _GlintSharpness("Glint Sharpness", Float) = 220.0

        _FresnelColour("Fresnel Colour", Color) = (0.42, 0.58, 0.72, 1.0)
        _Fresnel("Fresnel Strength", Range(0.0, 1.0)) = 0.12
        _FresnelPower("Fresnel Power", Float) = 2.0

        _FoamColour("Foam Colour", Color) = (0.62, 0.69, 0.72, 1.0)
        _FoamWidth("Foam Width (metres)", Float) = 0.0
        _FoamEdge("Foam Edge", Range(0.0, 1.0)) = 0.80
        _FoamSpeed("Foam Speed", Float) = 1.6
        _ShoreWash("Shore Wash", Range(0.0, 1.0)) = 0.30
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

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;

            float _SwellStrength;
            float _SwellScale;
            float _SwellSpeed;

            float _ChopScale;
            float _ChopStrength;

            half4 _GlintColour;
            half _Glint;
            float _GlintSharpness;

            half4 _FresnelColour;
            half _Fresnel;
            float _FresnelPower;

            half4 _FoamColour;
            float _FoamWidth;
            float _FoamEdge;
            float _FoamSpeed;
            float _ShoreWash;
        CBUFFER_END

        // Seconds of water, set by WaterClock and zero when nothing is driving it. Outside
        // UnityPerMaterial on purpose: it is a global, and a global inside that block is a
        // material property the SRP batcher would keep overwriting with zero.
        float _RF_WaterTime;

        // The slope one travelling swell puts into the surface at a point.
        //
        // Written as slope rather than as height because nothing here displaces a vertex:
        // the water is a flat sheet two centimetres above another flat sheet, and moving
        // its vertices would open a crack along every coastline. What a swell is allowed to
        // move is which way the surface faces, which is where all of the light is anyway.
        float2 RfSwell(float2 at, float2 way, float wavelength, float speed, float height)
        {
            float k = 6.2831853 / max(wavelength, 0.0001);
            float phase = (dot(at, way) * k) + (_RF_WaterTime * speed * _SwellSpeed * k);
            return way * (cos(phase) * height * k);
        }

        // Three crossing swells, at a wavelength and a speed the whole sea shares.
        //
        // Three rather than one because two crossing waves read as a moire and one reads as
        // corduroy; three at incommensurate angles and lengths never quite repeat. The
        // directions are unit vectors written out rather than computed, so the sea on a
        // given map is the same sea every time it is drawn.
        float2 RfSwellSlope(float2 at, float scale)
        {
            float2 slope = RfSwell(at, float2(0.940, 0.342), scale, 1.00, 0.055 * scale);
            slope += RfSwell(at, float2(-0.174, 0.985), scale * 0.63, 1.35, 0.030 * scale);
            slope += RfSwell(at, float2(0.643, -0.766), scale * 1.71, 0.72, 0.070 * scale);
            return slope;
        }
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
            #pragma fragment WaterFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            half4 WaterFragment(RfVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 facing = normalize(input.normalWS);

                // The swell, laid along whichever two directions this face is measured in -
                // the same frame the ground's grain uses, so the sides of the sea slab are
                // not a smear.
                float3 along;
                float3 up;
                float2 at;
                RfFrame(facing, input.positionWS, along, up, at);

                float2 slope = RfSwellSlope(at, _SwellScale);
                float3 normalWS = normalize(
                    facing - (((along * slope.x) + (up * slope.y)) * _SwellStrength));

                // ...and the chop on top of it, which is what stops a calm sea reading as
                // three sine waves.
                normalWS = RfRoughen(normalWS, input.positionWS, _ChopScale, _ChopStrength);

                InputData lighting = RfInput(input, normalWS);

                // Metres to the coastline, positive towards the land. Zero width switches
                // the whole shoreward half off, which is what the sea slab carries.
                float toward = 0.0;
                float foam = 0.0;
                if (_FoamWidth > 0.0)
                {
                    toward = saturate(1.0 + (input.shore / _FoamWidth));

                    // The line wanders, and it breathes. Without the first it is a contour
                    // of the distance field; without the second it is a decal.
                    float wander = RfGrain((at / 2.2) + float2(_RF_WaterTime * 0.05, 0.0)) - 0.5;
                    float pulse = 0.5 + (0.5 * sin((_RF_WaterTime * _FoamSpeed) + (wander * 8.0)));
                    float lip = (toward + (wander * 0.45)) * lerp(0.72, 1.0, pulse);
                    foam = smoothstep(_FoamEdge, 1.0, lip);
                }

                // A broad, weak lightening across the whole band under the foam line, so
                // the step from the shelf to the beach is a gradient rather than an edge.
                float wash = toward * toward * _ShoreWash;
                half3 albedo = lerp(_BaseColor.rgb, _FoamColour.rgb, saturate(wash + foam));

                // The sun, off the wave normal rather than off the flat plane. This is why
                // SurfaceTuning still says the two waters are matte and means it: a gloss
                // there is a broad lobe over one enormous flat sheet, which is what made
                // M7's first sea read lighter than the land it has to contrast with. A
                // glint at this sharpness is only ever a few pixels wide, and it is the
                // only thing in the frame that says where the sun is.
                Light sun = GetMainLight(lighting.shadowCoord);
                half3 halfway = SafeNormalize(sun.direction + lighting.viewDirectionWS);
                half glint = pow(saturate(dot(normalWS, halfway)), _GlintSharpness)
                    * _Glint * sun.shadowAttenuation * (1.0 - foam);

                // Fresnel, and on this map it is not a garnish - it is the only thing that
                // can show the sea at all from where the game is played. The two waters are
                // the darkest colours on the map, so a swell moving their diffuse shading by
                // a quarter moves it by nothing anybody can see; what does show is what is
                // added on top. The far half of the frame is near grazing and the near half
                // is not, so this is also the one term that varies across a flat sea.
                half rim = pow(
                    1.0 - saturate(dot(normalWS, lighting.viewDirectionWS)), _FresnelPower)
                    * _Fresnel;

                // What that reflection is looking at: the sky, except where it is looking at
                // the sun, and then the sun. This is how sun direction reads on the water in
                // the gameplay view, where the glint above provably cannot appear - the sun's
                // mirror direction is 34 degrees off vertical there and a swell can only tilt
                // a normal 19, so the highlight is never within reach. It costs nothing in
                // the overhead views because it is inside the fresnel, which is zero when the
                // camera is looking straight down.
                half sunward = saturate(dot(
                    reflect(-lighting.viewDirectionWS, normalWS), sun.direction));
                half3 sheen = lerp(
                    _FresnelColour.rgb, _GlintColour.rgb * sun.color, sunward * sunward);

                half3 emission = (_GlintColour.rgb * sun.color * glint) + (sheen * rim);

                SurfaceData surface = RfSurface(albedo, _Smoothness, emission);

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
