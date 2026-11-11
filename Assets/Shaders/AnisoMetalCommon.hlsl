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

// Calibrated Planck blackbody thermal curve:
// h in [0..2.2]: tempered dark steel (0) -> cherry 900K (0.35) -> forge orange 1800K (0.85) -> incandescent 3500K white-yellow (1.7+)
half3 CrusherBlackbodyHDR(half heat)
{
    half h = max(0.0h, heat);
    half3 temperedRed = half3(0.52h, 0.04h, 0.01h);
    half3 forgeOrange = half3(2.45h, 0.58h, 0.05h);
    half3 yellow3000K = half3(5.20h, 3.40h, 0.85h);
    half3 white3500K  = half3(7.40h, 6.40h, 3.80h);

    half3 col = lerp(temperedRed, forgeOrange, saturate(h * 1.45h));
    col = lerp(col, yellow3000K, saturate((h - 0.55h) * 1.35h));
    col = lerp(col, white3500K, saturate((h - 1.25h) * 1.15h));
    return col * smoothstep(0.02h, 0.16h, h);
}

// Analytical Foundry Studio IBL (zero HDRI cubemap RAM):
// - Cold industrial skylight + twin overhead LED softbox strips above
// - Crisp metallic horizon ring + 4 radial foundry floodlight glints
// - Warm molten smelter pit bounce from below (boosted near pit center & during RotorHeat / Golden Overdrive)
half3 CrusherProceduralFoundryIBL(half3 reflectWS, float3 positionWS, half roughness)
{
    half y = reflectWS.y;
    half3 skyCol = half3(0.44h, 0.53h, 0.66h);
    half3 eqCol  = half3(0.32h, 0.38h, 0.46h);
    half3 pitCol = half3(0.16h, 0.14h, 0.15h);

    // Warm furnace bounce from central shredder pit & foundry floor
    float radialDist = length(positionWS.xz);
    half pitProx = (half)exp(-radialDist * 0.14);
    half smelterPower = 0.35h + 0.55h * saturate((half)_GlobalRotorHeat) + 0.85h * saturate((half)_GlobalGoldenOverdrive);
    half3 warmBounce = half3(1.15h, 0.42h, 0.08h) * ((0.35h + 0.65h * pitProx) * smelterPower);
    pitCol += warmBounce * lerp(1.0h, 0.45h, roughness);

    half3 baseEnv = (y >= 0.0h)
        ? lerp(eqCol, skyCol, saturate(y * 1.15h))
        : lerp(eqCol, pitCol, saturate(-y * 1.35h));

    // Crisp metallic horizon band + 4 foundry perimeter floodlights
    float reflAngle = atan2(reflectWS.z, reflectWS.x);
    half horizonBand = (half)exp(-abs(y - 0.08) * (7.5 - roughness * 4.0));
    half floodSpots = (half)pow(saturate(0.5 + 0.5 * cos(reflAngle * 4.0 - 0.785)), lerp(10.0, 2.5, roughness));
    half horizonPower = lerp(0.85h, 0.22h, roughness);
    baseEnv += half3(0.72h, 0.84h, 1.00h) * (horizonBand * (0.30h + 0.70h * floodSpots) * horizonPower);

    // Overhead industrial softbox light strips blurred by roughness
    float2 ceilUV = reflectWS.xz / max(0.18, abs(reflectWS.y) + 0.24);
    float stripA = smoothstep(0.50 + roughness * 0.42, 0.11, abs(frac(ceilUV.x * 0.45 + 0.5) - 0.5));
    float stripB = smoothstep(1.75, 0.28, abs(ceilUV.y));
    half stripMask = (half)(stripA * stripB * step(0.08, reflectWS.y));
    half stripPower = lerp(1.25h, 0.22h, roughness);

    return baseEnv + half3(1.00h, 0.97h, 0.92h) * (stripMask * stripPower);
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

    // Smith-GGX correlated approximation fast for WebGL2 & mobile tile GPUs
    half k = (roughness + 1.0h);
    k = (k * k) * 0.125h;
    half Gv = NdotV / (NdotV * (1.0h - k) + k);
    half Gl = NdotL / (NdotL * (1.0h - k) + k);
    half specTerm = min(15.0h, (D * Gv * Gl) / max(0.004h, 4.0h * NdotV * NdotL));

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
    half bakedAO   = lerp(1.0h, (half)max(0.08, vertexColor.b), 0.92h);
    half partHash  = (half)vertexColor.a;

    half3 geomN = NormalizeNormalPerPixel(normalWS);
    half3 V = SafeNormalize(GetWorldSpaceViewDir(positionWS));

    // Unconditional screen-space derivatives (WebGL2 ANGLE GLSL ES 3.00 compliant — outside all dynamic branches!)
    float radOS = length(positionOS.xz);
    float angOS = atan2(positionOS.z, positionOS.x);
    float fwRad = fwidth(radOS * 135.0);
    float fwAng = fwidth(angOS * 36.0);
    float radialScope = saturate(1.0 - (radOS - 2.4) * 0.55);
    float latheFade = saturate(1.0 - fwRad * 0.45) * radialScope;
    float angFade   = saturate(1.0 - fwAng * 0.35) * radialScope;
    float grain = CrusherValueNoise3D(positionOS * float3(22.0, 10.0, 22.0));
    float latheGrooves = sin(radOS * 135.0 + partHash * 15.0) * (0.55 * latheFade)
                       + sin(angOS * 36.0 + positionOS.y * 42.0) * (0.45 * angFade);
    float microHeight = latheGrooves * (0.35 + 0.65 * p.anisotropy) + (grain - 0.5) * 1.15;

    float3 dPdx = ddx(positionWS);
    float3 dPdy = ddy(positionWS);
    float  dHdx = ddx(microHeight);
    float  dHdy = ddy(microHeight);

    // Morten Mikkelsen surface-gradient bump perturbation without requiring tangent vertex streams
    float3 r1 = cross(dPdy, geomN);
    float3 r2 = cross(geomN, dPdx);
    float  det = dot(dPdx, r1);
    float  invDet = det / max(det * det, 1e-4);
    // Attenuate micro-bump at distance & clamp perturbation so grazing bevels never speckle
    float  fwWorld = max(length(dPdx), length(dPdy));
    float  pixFootprint = saturate(1.0 - fwWorld * 14.0);
    float  bumpAmp = 0.016 * pixFootprint * (1.0 - p.rustAmount * 0.5);
    float3 surfGrad = clamp((r1 * dHdx + r2 * dHdy) * (invDet * bumpAmp), -0.18, 0.18);
    half3  N = SafeNormalize(geomN - (half3)surfGrad);

    // Radial brushed-metal tangent around local Y axis (lathe / rotor machining marks)
    float3 radialOS = float3(-positionOS.z, 0.0, positionOS.x);
    if (dot(radialOS, radialOS) < 1e-5)
        radialOS = float3(1.0, 0.0, 0.0);
    float3 worldRad = SafeNormalize(TransformObjectToWorldDir(normalize(radialOS)));
    float3 orthoT = worldRad - (float3)N * dot((float3)N, worldRad);
    if (dot(orthoT, orthoT) < 1e-4)
        orthoT = cross((float3)N, abs(N.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0));
    half3 T = (half3)SafeNormalize(orthoT);
    half3 B = SafeNormalize(cross(N, T));

    // Select base zone albedo
    half isArmor = smoothstep(0.20h, 0.32h, zoneId) * (1.0h - smoothstep(0.50h, 0.62h, zoneId));
    half isTeeth = smoothstep(0.52h, 0.68h, zoneId) * (1.0h - smoothstep(0.84h, 0.94h, zoneId));
    half isCoil  = smoothstep(0.85h, 0.96h, zoneId);

    half3 albedo = p.baseColor;
    albedo = lerp(albedo, p.zoneArmorColor, isArmor);
    albedo = lerp(albedo, p.zoneTeethColor, isTeeth);
    albedo = lerp(albedo, p.zoneCoilColor, isCoil * 0.5h);

    half metallic = p.metallic;
    half roughness = clamp(p.roughness + (half)((grain - 0.5) * 0.09 + latheGrooves * 0.025), 0.06h, 0.96h);

    // Crisp machined bevel highlight along hard-surface edges
    half bevelMask = saturate(edgeWear * p.edgeWearStrength);
    albedo = lerp(albedo, half3(0.86h, 0.90h, 0.96h), bevelMask * (1.0h - p.rustAmount) * 0.48h);
    roughness = lerp(roughness, max(0.10h, roughness * 0.60h), bevelMask * (1.0h - p.rustAmount));

    // Rusty Grade 0 oxidation (teaches FTUE fragility visually!)
    if (p.rustAmount > 0.001h)
    {
        half rustPatch = saturate((half)grain * 1.18h + edgeWear * 0.55h - (1.0h - p.rustAmount));
        half3 rustCol = half3(0.42h, 0.16h, 0.07h);
        albedo = lerp(albedo, rustCol, rustPatch * p.rustAmount);
        metallic = lerp(metallic, 0.18h, rustPatch * p.rustAmount);
        roughness = lerp(roughness, 0.86h, rustPatch * p.rustAmount);
    }

    // Molten Core W80 cosmetic skin override (dark volcanic basalt + living flowing magma veins!)
    half3 emission = p.zoneCoilColor * (isCoil * p.emissionBoost);
    if (p.moltenSkin > 0.001h)
    {
        half3 basalt = half3(0.08h, 0.06h, 0.06h);
        albedo = lerp(albedo, basalt, p.moltenSkin * (1.0h - isTeeth * 0.35h));
        roughness = lerp(roughness, 0.44h, p.moltenSkin);

        float magmaFlow = CrusherValueNoise3D(positionOS * 6.8 + float3(0.0, -_Time.y * 0.75, _Time.y * 0.35));
        half veinMask = (half)(1.0 - smoothstep(0.02, 0.14, abs(magmaFlow - 0.50)));
        half crack = saturate(veinMask * 0.95h + bevelMask * 0.85h + isCoil + isTeeth * 0.55h);
        half pulse = 0.78h + 0.22h * (half)sin(_Time.y * 4.8 + partHash * 6.28);
        half3 veinHDR = lerp(half3(2.80h, 0.42h, 0.03h), half3(5.40h, 2.10h, 0.25h), veinMask);
        emission += veinHDR * (crack * p.moltenSkin * pulse);
    }

    // Golden Tower ("Golden Smelter Overdrive") global melt transformation masked by material _GoldenMelt
    half goldFactor = saturate((half)_GlobalGoldenOverdrive * p.goldenMelt * (1.0h - p.moltenSkin * 0.55h));
    if (goldFactor > 0.001h)
    {
        half3 gold24k = half3(1.00h, 0.73h, 0.16h);
        albedo = lerp(albedo, gold24k, goldFactor);
        metallic = lerp(metallic, 0.98h, goldFactor);
        roughness = lerp(roughness, 0.16h, goldFactor);
        half furnaceWave = 0.65h + 0.35h * (half)sin(_Time.y * 7.0 - radOS * 5.0);
        emission += half3(2.25h, 1.28h, 0.18h) * (goldFactor * furnaceWave * (0.18h + 0.82h * max(isCoil, isTeeth)));
    }

    // Dynamic kill-streak rotor teeth heat (_GlobalRotorHeat) + 3500K Voronoi thermal cooling (p.heatGlow)
    half toothTipMask = isTeeth * (half)smoothstep(0.32, 0.92, radOS);
    half cutThermalMask = max(isCoil, max(isTeeth * 0.68h, bevelMask * 0.42h));
    half totalHeat = p.heatGlow * cutThermalMask + (half)_GlobalRotorHeat * max(toothTipMask, isTeeth * 0.45h);
    if (totalHeat > 0.02h)
    {
        emission += CrusherBlackbodyHDR(totalHeat);
    }

    // PBR Dielectric / Metallic split
    half oneMinusReflectivity = OneMinusReflectivityMetallic(metallic);
    half3 diffColor = albedo * oneMinusReflectivity;
    half3 F0 = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);

    // Build URP 17 InputData for Forward rendering
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

    // Main directional studio key light
    Light mainLight = GetMainLight(inputData.shadowCoord);
    half3 directLighting = EvaluateAnisoDirectLight(mainLight, N, T, B, V, diffColor, F0, roughness, p.anisotropy);

    // Additional punctual lights (Foundry Pit + Muzzle Flash + Black Hole / Death Ray + Boss Shatter)
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
        directLighting += EvaluateAnisoDirectLight(addLight, N, T, B, V, diffColor, F0, roughness, p.anisotropy);
    LIGHT_LOOP_END
#endif

    // Indirect GI + Analytical Foundry IBL reflection with anisotropy normal bend
    half3 bentNormal = SafeNormalize(lerp(N, N - T * dot(V, T) * (0.45h * p.anisotropy), p.anisotropy));
    half3 reflectVec = reflect(-V, bentNormal);
    half NdotV = saturate(dot(N, V));
    half fresnel = (half)pow(1.0 - NdotV, 4.0);
    half3 F_ind = F0 + (max(half3(1.0h - roughness, 1.0h - roughness, 1.0h - roughness), F0) - F0) * fresnel;

    // Deterministic Foundry Trilight Hemisphere Ambient (immune to unbaked SH probe state on frame 0 / WebGL2)
    half3 trilightAmb = (N.y >= 0.0h)
        ? lerp(half3(0.32h, 0.37h, 0.44h), half3(0.48h, 0.55h, 0.65h), N.y)
        : lerp(half3(0.32h, 0.37h, 0.44h), half3(0.18h, 0.17h, 0.19h), -N.y);
    half3 shAmbient = trilightAmb * diffColor * bakedAO;
    half3 envSpec = CrusherProceduralFoundryIBL(reflectVec, positionWS, roughness) * F_ind * bakedAO;

    half3 finalRGB = max(half3(0.0h, 0.0h, 0.0h), directLighting + shAmbient + envSpec + emission);

    // Crisp silhouette dark-graphite rim separation against foundry floor
    half rimDark = (1.0h - smoothstep(0.12h, 0.42h, NdotV)) * p.rimOutlineStrength;
    finalRGB = lerp(finalRGB, half3(0.04h, 0.05h, 0.08h), rimDark * 0.78h);

    // Instant crisp white-cyan Hit-Flash on impact (0.06s)
    if (p.hitFlash > 0.001h)
    {
        finalRGB = lerp(finalRGB, half3(3.2h, 3.5h, 3.8h), saturate(p.hitFlash));
    }

    return half4(finalRGB, 1.0h);
}

#endif
