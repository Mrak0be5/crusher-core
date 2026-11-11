Shader "Crusher/AnisoMetalInstanced"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.24, 0.36, 0.43, 1.0)
        _ZoneArmorColor("Secondary / Trim Color (R=0.35)", Color) = (0.16, 0.20, 0.24, 1.0)
        _ZoneTeethColor("Edge / Blade Color (R=0.70)", Color) = (0.85, 0.88, 0.92, 1.0)
        [HDR] _ZoneCoilColor("Core / Cut Glow Color (R=1.0)", Color) = (0.30, 0.85, 1.60, 1.0)

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.82
        _Roughness("Roughness", Range(0.05, 1.0)) = 0.34
        _Anisotropy("Radial Anisotropy", Range(0.0, 1.0)) = 0.55
        _EdgeWearStrength("Bevel Wear Strength", Range(0.0, 2.0)) = 1.1
        _EmissionBoost("Base Coil Emission", Range(0.0, 8.0)) = 1.4
        _GoldenMelt("Golden Melt Mask", Range(0.0, 1.0)) = 0.25
        _RimOutlineStrength("Rim Contour Strength", Range(0.0, 1.0)) = 0.78
        _RollFactor("Vertex Roll Multiplier (from InstData.w)", Float) = 0.0
        [ToggleUI] _UseInstColor("Use _InstColor Array", Float) = 0.0
        [ToggleUI] _UnlitSparkMode("Unlit HDR Spark Mode", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 250

        HLSLINCLUDE
        #include "AnisoMetalCommon.hlsl"

        half4 _BaseColor;
        half4 _ZoneArmorColor;
        half4 _ZoneTeethColor;
        half4 _ZoneCoilColor;
        half  _Metallic;
        half  _Roughness;
        half  _Anisotropy;
        half  _EdgeWearStrength;
        half  _EmissionBoost;
        half  _GoldenMelt;
        half  _RimOutlineStrength;
        float _RollFactor;
        half  _UseInstColor;
        half  _UnlitSparkMode;

        // Driven via MaterialPropertyBlock.SetVectorArray("_InstData", ...) for up to 16 instances per batch (strictly <= 180 vertex uniforms even on non-UBO GLES)
        // x = hitFlash [0..1], y = cutHeat / chargeGlow [0..2], z = squash [-0.4..0.8], w = rollPhase / custom
        float4 _InstData[16];
        float4 _InstColor[16];

        float4 FetchInstData(uint instId)
        {
        #if defined(UNITY_INSTANCING_ENABLED)
            return _InstData[min(instId, 15u)];
        #else
            return float4(0.0, 0.0, 0.0, 0.0);
        #endif
        }

        half3 FetchInstBaseColor(uint instId)
        {
        #if defined(UNITY_INSTANCING_ENABLED)
            if (_UseInstColor > 0.5h)
                return (half3)_InstColor[min(instId, 15u)].rgb;
        #endif
            return _BaseColor.rgb;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardInstanced"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertInst
            #pragma fragment FragInst

            #pragma multi_compile_instancing
            #pragma instancing_options maxcount:16
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float4 instData   : TEXCOORD4;
                half3  instCol    : TEXCOORD5;
            };

            Varyings VertInst(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                uint instId = 0u;
            #if defined(UNITY_INSTANCING_ENABLED)
                instId = unity_InstanceID;
            #endif
                float4 d = FetchInstData(instId);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, d.z, d.w * _RollFactor);

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.positionOS = posOS;
                output.normalWS   = TransformObjectToWorldNormal(nrmOS);
                output.color      = input.color;
                output.instData   = d;
                output.instCol    = FetchInstBaseColor(instId);
                return output;
            }

            half4 FragInst(Varyings input) : SV_Target
            {
                if (_UnlitSparkMode > 0.5h)
                {
                    half lifeGlow = max(0.18h, (half)input.instData.y);
                    half hotSpine = (half)saturate(1.0 - length(input.positionOS.xy) * 4.2);
                    half3 sparkHDR = lerp(input.instCol, half3(2.8h, 2.7h, 2.5h), hotSpine * 0.48h) * (_EmissionBoost * lifeGlow);
                    return half4(sparkHDR, 1.0h);
                }

                CrusherSurfaceParams p;
                p.baseColor          = input.instCol;
                p.zoneArmorColor     = _ZoneArmorColor.rgb;
                p.zoneTeethColor     = _ZoneTeethColor.rgb;
                p.zoneCoilColor      = _ZoneCoilColor.rgb;
                p.metallic           = _Metallic;
                p.roughness          = _Roughness;
                p.anisotropy         = _Anisotropy;
                p.rustAmount         = 0.0h;
                p.edgeWearStrength   = _EdgeWearStrength;
                p.hitFlash           = (half)saturate(input.instData.x);
                p.heatGlow           = (half)max(0.0, input.instData.y);
                p.goldenMelt         = _GoldenMelt;
                p.moltenSkin         = 0.0h;
                p.emissionBoost      = _EmissionBoost;
                p.rimOutlineStrength = _RimOutlineStrength;

                return ShadeCrusherAnisoMetal(
                    input.positionWS,
                    input.positionOS,
                    input.positionCS,
                    input.normalWS,
                    input.color,
                    p);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertInst
            #pragma fragment ShadowFragInst
            #pragma multi_compile_instancing
            #pragma instancing_options maxcount:16
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVertInst(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                uint instId = 0u;
            #if defined(UNITY_INSTANCING_ENABLED)
                instId = unity_InstanceID;
            #endif
                float4 d = FetchInstData(instId);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, d.z, d.w * _RollFactor);

                float3 posWS = TransformObjectToWorld(posOS);
                float3 nrmWS = TransformObjectToWorldNormal(nrmOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirWS = normalize(_LightPosition - posWS);
            #else
                float3 lightDirWS = _LightDirection;
            #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, lightDirWS));
                output.positionCS = ApplyShadowClamping(posCS);
                return output;
            }

            half4 ShadowFragInst(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
