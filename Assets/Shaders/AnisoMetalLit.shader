Shader "Crusher/AnisoMetalLit"
{
    Properties
    {
        [MainColor] _BaseColor("Base Steel Color", Color) = (0.34, 0.39, 0.45, 1.0)
        _ZoneArmorColor("Armor Plate Color (R=0.35)", Color) = (0.20, 0.24, 0.29, 1.0)
        _ZoneTeethColor("Carbide Teeth Color (R=0.70)", Color) = (0.72, 0.76, 0.82, 1.0)
        [HDR] _ZoneCoilColor("Inductor / Core Glow (R=1.0)", Color) = (0.20, 0.75, 1.20, 1.0)

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.88
        _Roughness("Roughness", Range(0.05, 1.0)) = 0.30
        _Anisotropy("Radial Anisotropy", Range(0.0, 1.0)) = 0.68
        _RustAmount("Grade 0 Rust Amount", Range(0.0, 1.0)) = 0.0
        _EdgeWearStrength("Bevel Wear Strength", Range(0.0, 2.0)) = 1.0

        _HitFlash("Hit Flash", Range(0.0, 1.0)) = 0.0
        _HeatGlow("Local Heat Glow", Range(0.0, 2.0)) = 0.0
        _GoldenMelt("Golden MeltMask (GT)", Range(0.0, 1.0)) = 1.0
        _MoltenSkin("Molten Core Skin", Range(0.0, 1.0)) = 0.0
        _EmissionBoost("Coil Emission Boost", Range(0.0, 8.0)) = 1.5
        _Squash("Impact Squash", Range(-0.5, 0.8)) = 0.0

        _RimOutlineStrength("Rim Contour Strength", Range(0.0, 1.0)) = 0.65
        _OutlineWidth("Hull Outline Width (m)", Range(0.0, 0.05)) = 0.014
        _OutlineColor("Hull Outline Color", Color) = (0.07, 0.09, 0.12, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "AnisoMetalCommon.hlsl"

        // Identical UnityPerMaterial CBUFFER across ALL passes -> 100% SRP Batcher compatible!
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ZoneArmorColor;
            half4 _ZoneTeethColor;
            half4 _ZoneCoilColor;
            half  _Metallic;
            half  _Roughness;
            half  _Anisotropy;
            half  _RustAmount;
            half  _EdgeWearStrength;
            half  _HitFlash;
            half  _HeatGlow;
            half  _GoldenMelt;
            half  _MoltenSkin;
            half  _EmissionBoost;
            float _Squash;
            half  _RimOutlineStrength;
            float _OutlineWidth;
            half4 _OutlineColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertLit
            #pragma fragment FragLit

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertLit(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, _Squash, 0.0);

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.positionOS = posOS;
                output.normalWS   = TransformObjectToWorldNormal(nrmOS);
                output.color      = input.color;
                return output;
            }

            half4 FragLit(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                CrusherSurfaceParams p;
                p.baseColor          = _BaseColor.rgb;
                p.zoneArmorColor     = _ZoneArmorColor.rgb;
                p.zoneTeethColor     = _ZoneTeethColor.rgb;
                p.zoneCoilColor      = _ZoneCoilColor.rgb;
                p.metallic           = _Metallic;
                p.roughness          = _Roughness;
                p.anisotropy         = _Anisotropy;
                p.rustAmount         = _RustAmount;
                p.edgeWearStrength   = _EdgeWearStrength;
                p.hitFlash           = _HitFlash;
                p.heatGlow           = _HeatGlow;
                p.goldenMelt         = _GoldenMelt;
                p.moltenSkin         = _MoltenSkin;
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
            Name "OutlineHull"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, _Squash, 0.0);

                float3 posWS = TransformObjectToWorld(posOS);
                float3 nrmWS = TransformObjectToWorldNormal(nrmOS);
                posWS += nrmWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 FragOutline(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(_OutlineColor.rgb, 1.0h);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, _Squash, 0.0);

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

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float3 nrmOS = input.normalOS;
                ApplyCrusherVertexMotion(posOS, nrmOS, _Squash, 0.0);
                output.positionCS = TransformObjectToHClip(posOS);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
