Shader "Crusher/ArenaFloorGrid"
{
    Properties
    {
        [MainColor] _CenterColor("Cast Steel Center Plate Color", Color) = (0.62, 0.67, 0.74, 1.0)
        _EdgeColor("Outer Foundry Deck Vignette Color", Color) = (0.44, 0.49, 0.56, 1.0)
        _GridColor("Plate Seam Bevel Color", Color) = (0.34, 0.39, 0.46, 1.0)
        _MajorRingColor("Sector & Vernier Tick Color", Color) = (0.28, 0.33, 0.40, 1.0)

        _RangeColor("Tower Range Hazard Color", Color) = (1.00, 0.68, 0.04, 1.0)
        _OrbRingColor("Orb Saw Track Color", Color) = (0.08, 0.68, 0.98, 1.0)
        [HDR] _GoldenFloorGlow("Golden Smelter Floor Glow", Color) = (2.40, 1.35, 0.18, 1.0)

        _TowerRange("Tower Range Radius (World Meters)", Float) = 7.5
        _OrbTrackActive("Orb Track Visibility", Range(0.0, 1.0)) = 0.0
        _GridScale("Plate Spacing (m)", Float) = 3.5
        _NoiseStrength("Steel Scuff & Oil Contrast", Range(0.0, 0.2)) = 0.055
        _VignetteRadius("Outer Vignette Radius (m)", Float) = 19.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry-10"
        }
        LOD 250

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

        half3 EvaluateFloorLightPBR(Light light, half3 N, half3 V, half3 albedo, half roughness, half specMask, half shadowFloor)
        {
            half3 L = normalize(light.direction);
            half NdotL = saturate(dot(N, L));
            if (NdotL <= 0.0001h)
                return half3(0.0h, 0.0h, 0.0h);

            half atten = (half)light.distanceAttenuation * lerp(shadowFloor, 1.0h, (half)light.shadowAttenuation);
            half3 H = SafeNormalize(L + V);
            half NdotH = max(0.0001h, saturate(dot(N, H)));
            half NdotV = max(0.002h, saturate(dot(N, V)));

            half a2 = max(0.008h, roughness * roughness * roughness * roughness);
            float dTerm = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
            half D = (half)(a2 / max(0.0002, PI * dTerm * dTerm));
            half spec = min(8.5h, D * 0.28h / max(0.05h, NdotV)) * specMask;

            half3 F0 = lerp(half3(0.05h, 0.06h, 0.07h), albedo * 0.65h, 0.55h);
            return (albedo * 0.68h + F0 * spec) * (light.color * (atten * NdotL));
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
                float safeScale = max(0.5, _GridScale);

                // Unconditional top-scope derivatives (WebGL2 ANGLE GLSL ES 3.00 safe!)
                float2 plateUV   = xz / safeScale;
                float2 subUV     = xz / (safeScale * 0.5);
                float2 dPlate    = max(fwidth(plateUV), 0.0005);
                float2 dSub      = max(fwidth(subUV), 0.0005);
                float  aaR       = max(fwidth(r), 0.004);

                float ringCoord  = r / (safeScale * 1.5);
                float ringWidth  = max(fwidth(ringCoord), 0.0005);
                float secCoord   = (angle / PI) * 4.0; // 8 radial foundry structural ribs
                float secWidth   = max(fwidth(secCoord), 0.0005);
                float tick36     = (angle / TWO_PI) * 36.0; // 10-degree vernier ticks
                float tickWidth  = max(fwidth(tick36), 0.001);

                // 1. Multi-octave cast-steel plate tone + oil slicks + per-tile tone variation
                float tileIdHash = FloorHash21(floor(subUV));
                float nCoarse = FloorNoise2D(xz * 0.55);
                float nMed    = FloorNoise2D(xz * 2.35);
                float nFine   = FloorNoise2D(xz * 10.5);

                half oilPatch = (half)smoothstep(0.58, 0.84, nCoarse * 0.72 + nMed * 0.28);
                half grain = (half)((nMed * 0.62 + nFine * 0.38 - 0.5) * 2.0) * _NoiseStrength;
                half tileVar = (half)((tileIdHash - 0.5) * 0.032);

                half vig = (half)saturate(r / max(1.0, _VignetteRadius));
                half3 albedo = lerp(_CenterColor.rgb, _EdgeColor.rgb, vig * vig) + grain + tileVar;
                // Subtle dark oil sheen patches (lower roughness -> catches colored beam & spark specular!)
                albedo = lerp(albedo, albedo * half3(0.78h, 0.81h, 0.86h), oilPatch * 0.55h);
                half roughness = lerp(0.54h, 0.22h, oilPatch) + (half)((nFine - 0.5) * 0.08);
                roughness = clamp(roughness, 0.16h, 0.78h);

                // 2. Interlocking Cast-Steel Plate Seams + 3D Chamfer Normal + Corner Rivet Domes
                float2 plateSigned = frac(plateUV - 0.5) - 0.5;
                float2 plateEdgeDist = abs(plateSigned) / dPlate;
                half mainSeam = (half)(1.0 - saturate(min(plateEdgeDist.x, plateEdgeDist.y) - 0.45));

                float2 subSigned = frac(subUV - 0.5) - 0.5;
                float2 subEdgeDist = abs(subSigned) / dSub;
                half subSeam = (half)(1.0 - saturate(min(subEdgeDist.x, subEdgeDist.y) - 0.30));

                // Corner rivets (4 bolts inset inside each sub-plate)
                float2 subLocal = frac(subUV) - 0.5; // [-0.5..0.5]
                float2 rivetOffset = abs(subLocal) - float2(0.39, 0.39);
                float rivetDist = length(rivetOffset);
                half rivetMask = (half)(1.0 - smoothstep(0.022, 0.034, rivetDist));
                half rivetRing = (half)(smoothstep(0.018, 0.026, rivetDist) * (1.0 - smoothstep(0.028, 0.038, rivetDist)));

                // Analytic 3D bevel & rivet normal perturbation for real directional/point specular glints!
                float2 bevelSlope = float2(
                    (subLocal.x > 0.46 ? 1.0 : (subLocal.x < -0.46 ? -1.0 : 0.0)),
                    (subLocal.y > 0.46 ? 1.0 : (subLocal.y < -0.46 ? -1.0 : 0.0)));
                float2 rivetNrmXZ = (rivetDist < 0.034 && rivetDist > 0.0001)
                    ? ((subLocal >= 0.0 ? 1.0 : -1.0) * rivetOffset / 0.034)
                    : float2(0.0, 0.0);

                half3 N = SafeNormalize(half3(
                    (half)(bevelSlope.x * 0.16 + rivetNrmXZ.x * 0.28 * rivetMask),
                    1.0h,
                    (half)(bevelSlope.y * 0.16 + rivetNrmXZ.y * 0.28 * rivetMask)));

                albedo = lerp(albedo, _GridColor.rgb, max(mainSeam * 0.68h, subSeam * 0.38h));
                albedo = lerp(albedo, _GridColor.rgb * 0.72h, rivetRing * 0.65h);
                albedo = lerp(albedo, albedo * 1.12h, rivetMask * 0.45h);

                // 3. Radial Soot / Scorch Blast Streaks (r = 1.8m .. 5.8m around shredder)
                float radNoise = FloorNoise2D(float2(angle * 7.5, r * 1.15));
                half scorchZone = (half)(smoothstep(1.3, 2.3, r) * (1.0 - smoothstep(3.2, 6.2, r)));
                half scorchMask = scorchZone * (half)saturate(radNoise * 0.85 + 0.15);
                albedo = lerp(albedo, half3(0.24h, 0.25h, 0.29h), scorchMask * 0.26h);

                // 4. Concentric Sector Rings + 8 Radial Foundry Guidelines
                half concRing = (half)(1.0 - saturate(abs(frac(ringCoord - 0.5) - 0.5) / ringWidth - 0.42));
                half secLine  = (half)(1.0 - saturate(abs(frac(secCoord - 0.5) - 0.5) / secWidth - 0.28)) * (half)smoothstep(2.4, 3.1, r);
                albedo = lerp(albedo, _MajorRingColor.rgb, max(concRing * 0.42h, secLine * 0.32h));

                // 5. Central Recessed Shredder Furnace Pit (r < 2.45m) with Glowing Under-Grate Smelter!
                half pitOuter = (half)(1.0 - smoothstep(2.15, 2.52, r));
                half pitWell  = (half)(1.0 - smoothstep(1.10, 2.22, r));
                // Radial steel grate bars over glowing furnace core
                half grateSpoke = (half)smoothstep(-0.15, 0.20, cos(angle * 16.0));
                half grateRing  = (half)smoothstep(-0.20, 0.25, sin(r * 14.0));
                half grateOpen  = saturate((1.0h - grateSpoke) * (1.0h - grateRing)) * (half)smoothstep(0.95, 1.28, r) * pitWell;

                albedo = lerp(albedo, half3(0.14h, 0.16h, 0.20h), pitOuter * 0.82h);

                // 6. Precision Tactical Range Perimeter: Vernier Ticks + Hazard Chevrons + Radar Sonar Sweep
                float dRange = abs(r - _TowerRange);
                half rangeBand = (half)(1.0 - smoothstep(0.058, 0.058 + aaR * 1.5, dRange));
                half rangeInnerGlow = (half)exp(-dRange * 3.0) * 0.16h;

                // Vernier degree ticks etched right inside _TowerRange (r in [Range-0.38 .. Range-0.10])
                half inTickBand = (half)(smoothstep(_TowerRange - 0.42, _TowerRange - 0.36, r) * (1.0 - smoothstep(_TowerRange - 0.12, _TowerRange - 0.08, r)));
                half tickLine = (half)(1.0 - saturate(abs(frac(tick36 - 0.5) - 0.5) / tickWidth - 0.35)) * inTickBand;
                albedo = lerp(albedo, _RangeColor.rgb * 0.85h, tickLine * 0.55h);

                float dashWave = sin(angle * 32.0 - _Time.y * 3.5);
                half chevronMask = (half)smoothstep(-0.15, 0.15, dashWave);
                half3 hazardCol = lerp(half3(0.12h, 0.14h, 0.18h), _RangeColor.rgb, chevronMask);
                albedo = lerp(albedo, hazardCol, rangeBand * 0.94h);

                // Expanding tactical radar sonar pulse inside combat perimeter
                float radarR = frac(_Time.y * 0.36) * _TowerRange;
                half radarWave = (half)(exp(-abs(r - radarR) * 4.5) * smoothstep(1.6, 2.6, r) * (1.0 - smoothstep(_TowerRange - 0.4, _TowerRange, radarR)));

                // Twin-rail Cyan Plasma Saw Orbit Groove at 0.95 * Range
                half orbGlowMask = 0.0h;
                if (_OrbTrackActive > 0.01h)
                {
                    float dOrb = abs(r - _TowerRange * 0.95);
                    half orbCore = (half)(1.0 - smoothstep(0.028, 0.028 + aaR * 1.5, dOrb)) * _OrbTrackActive;
                    half orbHalo = (half)exp(-dOrb * 7.5) * 0.24h * _OrbTrackActive;
                    albedo = lerp(albedo, _OrbRingColor.rgb, orbCore * 0.72h);
                    orbGlowMask = orbCore * 0.45h + orbHalo;
                }

                // 7. Direct + Dynamic Additional Lights PBR Evaluation (realtime shadows + specular glints!)
                half3 V = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif

                half specMask = lerp(0.55h, 1.25h, max(oilPatch, max(mainSeam, rivetMask)));
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half3 trilightFloor = lerp(half3(0.34h, 0.39h, 0.46h), half3(0.50h, 0.56h, 0.65h), saturate(N.y));
                half3 litFloor = albedo * (trilightFloor * 0.56h);
                litFloor += EvaluateFloorLightPBR(mainLight, N, V, albedo, roughness, specMask, 0.52h);

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    litFloor += EvaluateFloorLightPBR(addLight, N, V, albedo, roughness * 0.85h, specMask * 1.35h, 0.65h);
                LIGHT_LOOP_END
            #endif

                // 8. Emissive Foundry Pit Smelter + Tactical Range / Radar / Orb Accents
                half furnacePulse = 0.82h + 0.18h * (half)sin(_Time.y * 4.2 - r * 3.0);
                half furnaceDrive = 0.55h + 0.50h * saturate((half)_GlobalRotorHeat) + 0.75h * saturate((half)_GlobalGoldenOverdrive);
                half3 pitGlowRGB = half3(1.10h, 0.34h, 0.06h) * (grateOpen * furnacePulse * furnaceDrive);
                pitGlowRGB += half3(0.55h, 0.16h, 0.03h) * (pitWell * 0.09h * furnaceDrive);

                litFloor += pitGlowRGB;
                litFloor += _RangeColor.rgb * (rangeInnerGlow * 0.48h + radarWave * 0.14h);
                litFloor += _OrbRingColor.rgb * (orbGlowMask * 0.55h);

                half goldHeat = saturate((half)_GlobalGoldenOverdrive);
                if (goldHeat > 0.001h)
                {
                    half furnaceRing = (half)exp(-r * 0.28) * (0.75h + 0.25h * (half)sin(r * 2.4 - _Time.y * 6.0));
                    litFloor += _GoldenFloorGlow.rgb * (goldHeat * furnaceRing * 0.52h);
                }

                return half4(litFloor, 1.0h);
            }
            ENDHLSL
        }
    }
}
