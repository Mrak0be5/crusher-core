#ifndef CRUSHER_ANISO_METAL_COMMON_INCLUDED
#define CRUSHER_ANISO_METAL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Global runtime parameters driven by Rotor / UW controllers (0 by default if unset)
float _GlobalGoldenOverdrive;
float _GlobalRotorHeat;

struct CrusherSurfaceParams
{
    half3 baseColor;
    half3 zoneArmorColor;
    half3 zoneTeethColor;
    half3 zoneCoilColor;
    half  metallic;
    half  roughness;
    half  anisotropy;
    half  rustAmount;
    half  edgeWearStrength;
    half  hitFlash;
    half  heatGlow;
    half  goldenMelt;
    half  moltenSkin;
    half  emissionBoost;
    half  rimOutlineStrength;
};

// Fast hash for micro-grain and molten cracks (0 textures required)
float CrusherHash31(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float CrusherValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = CrusherHash31(i + float3(0, 0, 0));
    float n100 = CrusherHash31(i + float3(1, 0, 0));
    float n010 = CrusherHash31(i + float3(0, 1, 0));
    float n110 = CrusherHash31(i + float3(1, 1, 0));
    float n001 = CrusherHash31(i + float3(0, 0, 1));
    float n101 = CrusherHash31(i + float3(1, 0, 1));
    float n011 = CrusherHash31(i + float3(0, 1, 1));
    float n111 = CrusherHash31(i + float3(1, 1, 1));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);

    float nxy0 = lerp(nx00, nx10, f.y);
    float nxy1 = lerp(nx01, nx11, f.y);
    return lerp(nxy0, nxy1, f.z);
}

// Planck-like blackbody ramp (heat in [0..1.5]) -> HDR RGB
half3 CrusherBlackbodyHDR(half heat)
{
    half h = max(0.0h, heat);
    half3 darkCherry = half3(0.45h, 0.04h, 0.01h);
    half3 hotOrange  = half3(2.20h, 0.52h, 0.04h);
    half3 whiteGold  = half3(6.20h, 4.60h, 2.20h);

    half3 col = lerp(darkCherry, hotOrange, saturate(h * 1.35h));
    col = lerp(col, whiteGold, saturate((h - 0.55h) * 1.45h));
    return col * smoothstep(0.02h, 0.18h, h);
}

// Procedural industrial softbox reflection (crisp studio overhead strips + controlled sky/floor bounce)
half3 CrusherProceduralStudioEnv(half3 reflectWS, half roughness)
{
    half y = reflectWS.y;
    half3 skyCol = half3(0.46h, 0.52h, 0.60h);
    half3 eqCol  = half3(0.28h, 0.33h, 0.40h);
    half3 gndCol = half3(0.14h, 0.16h, 0.20h);

    half3 baseEnv = (y >= 0.0h) ? lerp(eqCol, skyCol, saturate(y)) : lerp(eqCol, gndCol, saturate(-y));

    // Overhead industrial light strips blurred by roughness
    float2 ceilUV = reflectWS.xz / max(0.18, abs(reflectWS.y) + 0.25);
    float stripA = smoothstep(0.52 + roughness * 0.40, 0.12, abs(frac(ceilUV.x * 0.45 + 0.5) - 0.5));
    float stripB = smoothstep(1.75, 0.30, abs(ceilUV.y));
    half stripMask = (half)(stripA * stripB * step(0.08, reflectWS.y));
    half stripPower = lerp(1.15h, 0.20h, roughness);

    return baseEnv + half3(1.0h, 0.97h, 0.92h) * (stripMask * stripPower);
}

// Anisotropic GGX direct lighting evaluation
half3 EvaluateAnisoDirectLight(
    Light light,
    half3 N,
    half3 T,
    half3 B,
    half3 V,
    half3 diffColor,
    half3 F0,
    half roughness,
    half anisotropy)
{
    half3 L = normalize(light.direction);
    half NdotL = saturate(dot(N, L));
    if (NdotL <= 0.0001h)
        return half3(0.0h, 0.0h, 0.0h);

    half NdotV = max(0.001h, saturate(dot(N, V)));
    half3 H = SafeNormalize(L + V);
    half TdotH = dot(T, H);
    half BdotH = dot(B, H);
    half NdotH = max(0.0001h, saturate(dot(N, H)));
    half LdotH = saturate(dot(L, H));

    half aspect = sqrt(1.0h - 0.85h * anisotropy);
    half ax = max(0.045h, (roughness * roughness) / aspect);
    half ay = max(0.045h, (roughness * roughness) * aspect);

    float denom = (TdotH * TdotH) / (ax * ax) + (BdotH * BdotH) / (ay * ay) + (NdotH * NdotH);
    half D = (half)(1.0 / max(0.0001, PI * ax * ay * denom * denom));

    // Smith-GGX correlated approximation fast for mobile tile GPUs
    half k = (roughness + 1.0h);
    k = (k * k) * 0.125h;
    half Gv = NdotV / (NdotV * (1.0h - k) + k);
    half Gl = NdotL / (NdotL * (1.0h - k) + k);
    half specTerm = min(14.0h, (D * Gv * Gl) / max(0.004h, 4.0h * NdotV * NdotL));

    half oneMinusLH = 1.0h - LdotH;
    half oneMinusLH5 = oneMinusLH * oneMinusLH * oneMinusLH * oneMinusLH * oneMinusLH;
    half3 F = F0 + (1.0h - F0) * oneMinusLH5;

    half3 radiance = light.color * (half)(light.distanceAttenuation * light.shadowAttenuation) * NdotL;
    return (diffColor * INV_PI + F * specTerm) * radiance;
}

// Vertex deformation helper (squash-and-stretch + optional wheel/cylinder rolling)
void ApplyCrusherVertexMotion(
    inout float3 posOS,
    inout float3 normOS,
    float squash,
    float rollAngle)
{
    if (abs(rollAngle) > 0.0001)
    {
        float s, c;
        sincos(rollAngle, s, c);
        // Roll around local X axis
        float2 yzPos = float2(posOS.y * c - posOS.z * s, posOS.y * s + posOS.z * c);
        float2 yzNrm = float2(normOS.y * c - normOS.z * s, normOS.y * s + normOS.z * c);
        posOS.yz = yzPos;
        normOS.yz = yzNrm;
    }

    if (abs(squash) > 0.0001)
    {
        float sy = max(0.45, 1.0 - squash * 0.32);
        float sxz = 1.0 / sqrt(sy);
        posOS.xz *= sxz;
        posOS.y *= sy;
        normOS = SafeNormalize(normOS * float3(1.0 / sxz, 1.0 / sy, 1.0 / sxz));
    }
}

half4 ShadeCrusherAnisoMetal(
    float3 positionWS,
    float3 positionOS,
    float4 positionCS,
    float3 normalWS,
    float4 vertexColor,
    CrusherSurfaceParams p)
{
    half zoneId    = (half)vertexColor.r; // 0.0 body, 0.35 armor, 0.70 teeth, 1.0 coils/voronoi cut
    half edgeWear  = (half)vertexColor.g;
    half bakedAO   = lerp(1.0h, (half)max(0.10, vertexColor.b), 0.90h);
    half partHash  = (half)vertexColor.a;

    half3 N = NormalizeNormalPerPixel(normalWS);
    half3 V = SafeNormalize(GetWorldSpaceViewDir(positionWS));

    // Radial brushed-metal tangent around local Y axis (lathe / rotor machining marks)
    float3 radialOS = float3(-positionOS.z, 0.0, positionOS.x);
    if (dot(radialOS, radialOS) < 1e-5)
        radialOS = float3(1.0, 0.0, 0.0);
    half3 T = SafeNormalize(TransformObjectToWorldDir(normalize(radialOS)));
    T = SafeNormalize(T - N * dot(N, T));
    half3 B = SafeNormalize(cross(N, T));

    // Select base zone albedo
    half isArmor = smoothstep(0.20h, 0.32h, zoneId) * (1.0h - smoothstep(0.50h, 0.62h, zoneId));
    half isTeeth = smoothstep(0.52h, 0.68h, zoneId) * (1.0h - smoothstep(0.84h, 0.94h, zoneId));
    half isCoil  = smoothstep(0.85h, 0.96h, zoneId);

    half3 albedo = p.baseColor;
    albedo = lerp(albedo, p.zoneArmorColor, isArmor);
    albedo = lerp(albedo, p.zoneTeethColor, isTeeth);
    albedo = lerp(albedo, p.zoneCoilColor, isCoil * 0.5h);

    // Procedural brushed grain & edge wear / G0 rust
    float grain = CrusherValueNoise3D(positionOS * float3(18.0, 8.0, 18.0));
    half metallic = p.metallic;
    half roughness = clamp(p.roughness + (half)((grain - 0.5) * 0.08), 0.06h, 0.96h);

    // Edge bevel highlight (bright carbide/steel edge)
    half bevelMask = saturate(edgeWear * p.edgeWearStrength);
    albedo = lerp(albedo, half3(0.84h, 0.88h, 0.93h), bevelMask * (1.0h - p.rustAmount) * 0.45h);
    roughness = lerp(roughness, max(0.12h, roughness * 0.65h), bevelMask * (1.0h - p.rustAmount));

    // Rusty Grade 0 oxidation (teaches FTUE fragility visually!)
    if (p.rustAmount > 0.001h)
    {
        half rustPatch = saturate((half)grain * 1.15h + edgeWear * 0.55h - (1.0h - p.rustAmount));
        half3 rustCol = half3(0.42h, 0.16h, 0.07h);
        albedo = lerp(albedo, rustCol, rustPatch * p.rustAmount);
        metallic = lerp(metallic, 0.20h, rustPatch * p.rustAmount);
        roughness = lerp(roughness, 0.86h, rustPatch * p.rustAmount);
    }

    // Molten Core W80 cosmetic skin override (obsidian hull + glowing magma seams)
    half3 emission = p.zoneCoilColor * (isCoil * p.emissionBoost);
    if (p.moltenSkin > 0.001h)
    {
        half3 basalt = half3(0.09h, 0.07h, 0.06h);
        albedo = lerp(albedo, basalt, p.moltenSkin * (1.0h - isTeeth * 0.4h));
        roughness = lerp(roughness, 0.48h, p.moltenSkin);
        half crack = saturate(bevelMask * 0.9h + isCoil + isTeeth * 0.65h);
        half pulse = 0.80h + 0.20h * (half)sin(_Time.y * 4.5 + partHash * 6.28);
        emission += half3(3.8h, 0.75h, 0.06h) * (crack * p.moltenSkin * pulse);
    }

    // Golden Tower ("Golden Smelter Overdrive") global melt transformation masked by material _GoldenMelt
    half goldFactor = saturate((half)_GlobalGoldenOverdrive * p.goldenMelt * (1.0h - p.moltenSkin * 0.58h));
    if (goldFactor > 0.001h)
    {
        half3 gold24k = half3(1.00h, 0.72h, 0.16h);
        albedo = lerp(albedo, gold24k, goldFactor);
        metallic = lerp(metallic, 0.98h, goldFactor);
        roughness = lerp(roughness, 0.17h, goldFactor);
        half furnaceWave = 0.65h + 0.35h * (half)sin(_Time.y * 7.0 - length(positionOS.xz) * 5.0);
        emission += half3(2.20h, 1.25h, 0.18h) * (goldFactor * furnaceWave * (0.18h + 0.82h * max(isCoil, isTeeth)));
    }

    // Carbide teeth friction heat + Voronoi inner fracture hot cut glow
    half totalHeat = p.heatGlow * max(isTeeth * 0.65h, isCoil) + (half)_GlobalRotorHeat * isTeeth * 0.42h;
    if (totalHeat > 0.02h)
    {
        emission += CrusherBlackbodyHDR(totalHeat);
    }

    // PBR Dielectric / Metallic split
    half oneMinusReflectivity = OneMinusReflectivityMetallic(metallic);
    half3 diffColor = albedo * oneMinusReflectivity;
    half3 F0 = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);

    // Build URP 17 InputData for Forward / Forward+ (_CLUSTER_LIGHT_LOOP)
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = N;
    inputData.viewDirectionWS = V;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    // Main directional light
    Light mainLight = GetMainLight(inputData.shadowCoord);
    half3 directLighting = EvaluateAnisoDirectLight(mainLight, N, T, B, V, diffColor, F0, roughness, p.anisotropy);

    // Additional punctual lights (Forward & Forward+ Cluster loop)
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
        directLighting += EvaluateAnisoDirectLight(addLight, N, T, B, V, diffColor, F0, roughness, p.anisotropy);
    LIGHT_LOOP_END
#endif

    // Indirect GI + Procedural studio environment reflection with anisotropy bend
    half3 bentNormal = SafeNormalize(lerp(N, N - T * dot(V, T) * (0.45h * p.anisotropy), p.anisotropy));
    half3 reflectVec = reflect(-V, bentNormal);
    half NdotV = saturate(dot(N, V));
    half fresnel = (half)pow(1.0 - NdotV, 4.0);
    half3 F_ind = F0 + (max(half3(1.0h - roughness, 1.0h - roughness, 1.0h - roughness), F0) - F0) * fresnel;

    half3 shAmbient = SampleSH(N) * diffColor * bakedAO;
    half3 envSpec = CrusherProceduralStudioEnv(reflectVec, roughness) * F_ind * bakedAO;

    half3 finalRGB = directLighting + shAmbient + envSpec + emission;

    // Crisp silhouette dark-graphite rim separation against bright concrete arena floor
    half rimDark = (1.0h - smoothstep(0.12h, 0.44h, NdotV)) * p.rimOutlineStrength;
    finalRGB = lerp(finalRGB, half3(0.04h, 0.05h, 0.08h), rimDark * 0.80h);

    // Instant crisp white-cyan Hit-Flash on impact (0.06s)
    if (p.hitFlash > 0.001h)
    {
        finalRGB = lerp(finalRGB, half3(3.2h, 3.5h, 3.8h), saturate(p.hitFlash));
    }

    return half4(finalRGB, 1.0h);
}

#endif
