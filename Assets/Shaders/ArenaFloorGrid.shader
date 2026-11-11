Shader "Crusher/ArenaFloorGrid"
{
    Properties
    {
        [MainColor] _CenterColor("Concrete Center Color", Color) = (0.81, 0.84, 0.88, 1.0)
        _EdgeColor("Concrete Outer Vignette Color", Color) = (0.68, 0.72, 0.78, 1.0)
        _GridColor("Meter Grid Line Color", Color) = (0.58, 0.63, 0.69, 1.0)
        _MajorRingColor("Major Sector Ring Color", Color) = (0.46, 0.51, 0.58, 1.0)

        _RangeColor("Tower Range Hazard Color", Color) = (0.98, 0.74, 0.08, 1.0)
        _OrbRingColor("Orb Saw Track Color", Color) = (0.18, 0.68, 0.96, 1.0)
        [HDR] _GoldenFloorGlow("Golden Smelter Floor Glow", Color) = (2.40, 1.35, 0.18, 1.0)

        _TowerRange("Tower Range Radius (World Meters)", Float) = 7.5
        _OrbTrackActive("Orb Track Visibility", Range(0.0, 1.0)) = 0.0
        _GridScale("Grid Spacing (m)", Float) = 2.5
        _NoiseStrength("Concrete Micro-Grain", Range(0.0, 0.2)) = 0.045
        _VignetteRadius("Outer Vignette Radius (m)", Float) = 18.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry-10"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        float _GlobalGoldenOverdrive;
        float _GlobalRotorHeat;

        CBUFFER_START(UnityPerMaterial)
            half4 _CenterColor;
            half4 _EdgeColor;
            half4 _GridColor;
            half4 _MajorRingColor;
            half4 _RangeColor;
            half4 _OrbRingColor;
            half4 _GoldenFloorGlow;
            float _TowerRange;
            half  _OrbTrackActive;
            float _GridScale;
            half  _NoiseStrength;
            float _VignetteRadius;
        CBUFFER_END

        float FloorHash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float FloorNoise2D(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = FloorHash21(i);
            float b = FloorHash21(i + float2(1.0, 0.0));
            float c = FloorHash21(i + float2(0.0, 1.0));
            float d = FloorHash21(i + float2(1.0, 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardFloor"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertFloor
            #pragma fragment FragFloor

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertFloor(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 FragFloor(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 xz = input.positionWS.xz;
                float r = length(xz);
                float angle = atan2(xz.y, xz.x);

                // 2-octave concrete micro-grain (keeps floor crisp & under HDR Bloom threshold 1.05!)
                float n1 = FloorNoise2D(xz * 2.2);
                float n2 = FloorNoise2D(xz * 9.5);
                half grain = (half)((n1 * 0.65 + n2 * 0.35 - 0.5) * 2.0) * _NoiseStrength;

                half vig = (half)saturate(r / max(1.0, _VignetteRadius));
                half3 albedo = lerp(_CenterColor.rgb, _EdgeColor.rgb, vig * vig) + grain;

                // Anti-aliased square engineering grid (1 sim unit = 2.5m default)
                float2 gridUV = xz / max(0.25, _GridScale);
                float2 gridDeriv = max(fwidth(gridUV), 0.0005);
                float2 gridDist = abs(frac(gridUV - 0.5) - 0.5) / gridDeriv;
                half gridLine = (half)(1.0 - saturate(min(gridDist.x, gridDist.y) - 0.35));
                albedo = lerp(albedo, _GridColor.rgb, gridLine * 0.38h);

                // Anti-aliased radial 8-sector lines + concentric distance rings every 5m
                float ringCoord = r / (max(0.25, _GridScale) * 2.0);
                float ringWidth = max(fwidth(ringCoord), 0.0005);
                half concRing = (half)(1.0 - saturate(abs(frac(ringCoord - 0.5) - 0.5) / ringWidth - 0.45));

                float secCoord = (angle / PI) * 4.0; // 8 radial guidelines
                float secWidth = max(fwidth(secCoord), 0.0005);
                half secLine = (half)(1.0 - saturate(abs(frac(secCoord - 0.5) - 0.5) / secWidth - 0.30)) * (half)smoothstep(1.3, 2.2, r);
                albedo = lerp(albedo, _MajorRingColor.rgb, max(concRing, secLine * 0.6h) * 0.42h);

                // Center rotor mounting pit shadow ring (radius ~ 1.25m)
                half centerPit = (half)(1.0 - smoothstep(1.05, 1.55, r));
                albedo *= lerp(1.0h, 0.58h, centerPit);

                // Dynamic Tower Range hazard ring (yellow-black animated magnetic chevrons)
                float dRange = abs(r - _TowerRange);
                float aaR = max(fwidth(r), 0.005);
                half rangeBand = (half)(1.0 - smoothstep(0.055, 0.055 + aaR * 1.5, dRange));
                half rangeInnerGlow = (half)exp(-dRange * 2.8) * 0.14h;
                float dashWave = sin(angle * 32.0 - _Time.y * 3.5);
                half chevronMask = (half)smoothstep(-0.15, 0.15, dashWave);
                half3 hazardCol = lerp(half3(0.14h, 0.16h, 0.20h), _RangeColor.rgb, chevronMask);
                albedo = lerp(albedo, hazardCol, rangeBand * 0.92h);

                // Optional Orb saw orbit groove at 0.95 * Range
                if (_OrbTrackActive > 0.01h)
                {
                    float dOrb = abs(r - _TowerRange * 0.95);
                    half orbLine = (half)(1.0 - smoothstep(0.025, 0.025 + aaR * 1.5, dOrb)) * _OrbTrackActive;
                    albedo = lerp(albedo, _OrbRingColor.rgb, orbLine * 0.55h);
                }

                // Direct & cluster lighting with realtime shadow reception
                half3 N = NormalizeNormalPerPixel(input.normalWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half shadow = lerp(0.58h, 1.0h, (half)mainLight.shadowAttenuation);
                half NdotL = saturate(dot(N, mainLight.direction));
                half3 litFloor = albedo * (SampleSH(N) * 0.55h + mainLight.color * (NdotL * shadow * 0.62h));

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addNdotL = saturate(dot(N, addLight.direction));
                    litFloor += albedo * addLight.color * ((half)(addLight.distanceAttenuation * addLight.shadowAttenuation) * addNdotL * 0.55h);
                LIGHT_LOOP_END
            #endif

                // Subtle Range pulse + Golden Smelter radial furnace glow around core
                litFloor += _RangeColor.rgb * (rangeInnerGlow * 0.45h);
                half goldHeat = saturate((half)_GlobalGoldenOverdrive);
                if (goldHeat > 0.001h)
                {
                    half furnaceRing = (half)exp(-r * 0.34) * (0.75h + 0.25h * (half)sin(r * 2.4 - _Time.y * 6.0));
                    litFloor += _GoldenFloorGlow.rgb * (goldHeat * furnaceRing * 0.55h);
                }

                return half4(litFloor, 1.0h);
            }
            ENDHLSL
        }
    }
}
