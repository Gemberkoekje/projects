// What a mark left on the ground is drawn with: a scorch where something burned, and the
// tracks a vehicle leaves behind it on soft ground.
//
// It multiplies rather than blends. That is the whole design, and it buys three things at
// once. A stain is a thing that darkens what is under it, so multiplying is what a stain
// actually does; it needs no lighting of its own, because whatever it sits on has already
// been lit and the mark simply takes a share of that; and a mark that crosses from grass
// onto sand comes out the right colour on both without knowing that either exists.
//
// It is also why there are no URP Decal Projectors anywhere in this project. A projector
// exists to lay a mark across geometry whose shape is unknown, and the shape here is known
// exactly: every square metre of ground a vehicle can reach is at y = 0, flat, because
// CombatPlane resolves every round on that plane. A quad a centimetre above it is the same
// picture for none of the cost, no renderer feature on either renderer asset, and no
// texture.
Shader "IronFlag/Mark"
{
    Properties
    {
        [MainColor] _BaseColor("Stain Colour", Color) = (0.35, 0.33, 0.30, 1.0)

        _Edge("Edge", Range(0.0, 1.0)) = 0.45
        _Ragged("Ragged", Range(0.0, 1.0)) = 0.35
        _RaggedScale("Ragged Scale (metres)", Float) = 0.6

        // 1 draws a disc from the middle of the quad, 0 a ribbon that only falls off across
        // its width. A scorch is the first and a wheel track is the second, and the two are
        // otherwise the same mark.
        [ToggleUI] _Round("Round", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
        }

        LOD 100

        Pass
        {
            Name "Mark"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // Multiply, writing no depth. White is the identity, so anything this shader
            // decides is not part of the mark leaves the ground exactly as it found it.
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MarkVertex
            #pragma fragment MarkFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RF_Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Edge;
                float _Ragged;
                float _RaggedScale;
                float _Round;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;

                // How much of the mark is left. A scorch fades through this with a property
                // block; a wheel track has it written per vertex by the trail renderer's own
                // gradient, which is what makes the far end of a track older than the near
                // end without anything having to age it.
                half4 colour : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 ground : TEXCOORD1;
                half fade : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MarkVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = position.positionCS;
                output.uv = input.uv;

                // World metres, so the ragged edge is the same size on a scorch the tank
                // left and on one the grenade left, and does not stretch along a track.
                output.ground = position.positionWS.xz;
                output.fade = input.colour.a;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);

                return output;
            }

            half4 MarkFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // How far out from the middle of the mark this pixel is, in 0..1. A disc
                // measures that from the centre; a ribbon only across its width, because it
                // has no middle to be far from along its length.
                float2 from = input.uv - 0.5;
                float across = _Round > 0.5
                    ? saturate(length(from) * 2.0)
                    : saturate(abs(from.y) * 2.0);

                // Eaten in from the edge, so a scorch is a burn rather than a coin.
                across += (RfGrain(input.ground / max(_RaggedScale, 0.01)) - 0.5) * _Ragged;

                half mask = (1.0 - smoothstep(_Edge, 1.0, saturate(across))) * input.fade;
                half3 stain = lerp(half3(1.0, 1.0, 1.0), _BaseColor.rgb, mask);

                // Fog whitens a multiplier rather than greying it: at the far end of the
                // draw distance there is no ground behind the haze left to stain. Handing
                // MixFogColor white as the fog colour is what says that, and it is also the
                // only form of this that survives fog being switched off - which the art
                // preview and the map overview both do. ComputeFogIntensity looks like the
                // obvious way to get the number and is not: with no fog mode enabled it
                // answers zero, the same as fully fogged out, and a mark multiplied by that
                // is white. The first marks contact sheet came out completely blank.
                return half4(MixFogColor(stain, half3(1.0, 1.0, 1.0), input.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
