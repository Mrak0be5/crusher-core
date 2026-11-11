Shader "Crusher/BlackHoleVortex"
{
    Properties
    {
        [HDR] _InnerRimColor("Photon Ring Color (HDR)", Color) = (1.60, 3.80, 6.20, 1.0)
        [HDR] _SwirlColor("Accretion Plasma Color (HDR)", Color) = (1.40, 0.55, 3.60, 1.0)
        _CoreColor("Event Horizon Core Color", Color) = (0.01, 0.01, 0.03, 0.96)

        _HorizonRadius("Event Horizon Radius (UV)", Range(0.04, 0.45)) = 0.19
        _Twist("Spiral Twist", Range(1.0, 12.0)) = 5.2
        _SpinSpeed("Angular Spin Speed", Float) = 7.5
        _InfallSpeed("Radial Pull Speed", Float) = 2.8
        _Intensity("Overall Intensity / Fade", Range(0.0, 2.0)) = 1.0
        [ToggleUI] _FieldZoneMode("Golden Bot Field Mode (Soft Center)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "VortexForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertVortex
            #pragma fragment FragVortex
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Self-contained vortex (supportsCameraOpaqueTexture = false -> preserves MSAA tile pass on Android!)
            CBUFFER_START(UnityPerMaterial)
                half4 _InnerRimColor;
                half4 _SwirlColor;
                half4 _CoreColor;
                half  _HorizonRadius;
                float _Twist;
                float _SpinSpeed;
                float _InfallSpeed;
                half  _Intensity;
                half  _FieldZoneMode;
            CBUFFER_END

            float VortexHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float VortexNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = VortexHash(i);
                float b = VortexHash(i + float2(1.0, 0.0));
                float c = VortexHash(i + float2(0.0, 1.0));
                float d = VortexHash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertVortex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv * 2.0 - 1.0; // [-1..1] centered polar disk
                output.color      = input.color;
                return output;
            }

            half4 FragVortex(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 p = input.uv;
                float r = length(p);
                if (r > 1.0)
                    return half4(0, 0, 0, 0);

                float theta = atan2(p.y, p.x);
                float t = _Time.y;

                // Golden Bot bonus zone mode (clean pulsing outer halo + rotating induction segments)
                if (_FieldZoneMode > 0.5h)
                {
                    half outerEdge = (half)(smoothstep(0.86, 0.96, r) * (1.0 - smoothstep(0.96, 1.0, r)));
                    half dash = (half)smoothstep(-0.1, 0.2, sin(theta * 12.0 - t * _SpinSpeed));
                    half innerFill = (half)(1.0 - r * r) * 0.22h;
                    half pulseWave = (half)(0.5 + 0.5 * sin(r * 16.0 - t * 5.0)) * 0.16h * (half)(1.0 - r);
                    half3 col = lerp(_SwirlColor.rgb, _InnerRimColor.rgb, outerEdge);
                    half alpha = saturate((outerEdge * (0.45h + 0.55h * dash) + innerFill + pulseWave) * _Intensity * input.color.a);
                    return half4(col * _Intensity * input.color.rgb, alpha);
                }

                // Black Hole electromagnetic accretion vortex
                float spiralAngle = theta + _Twist / (r + 0.085) - t * _SpinSpeed;
                float2 polarUV1 = float2(spiralAngle * 1.91, r * 6.5 + t * _InfallSpeed);
                float2 polarUV2 = float2(spiralAngle * 3.82 - t * 1.7, r * 11.0 + t * (_InfallSpeed * 1.45));

                float n1 = VortexNoise(polarUV1);
                float n2 = VortexNoise(polarUV2);
                half arms = (half)saturate(0.5 + 0.5 * sin(spiralAngle * 3.0 + n1 * 2.6));
                half filaments = (half)pow(saturate(n1 * 0.6 + n2 * 0.55), 2.2);

                // Sharp Einstein photon ring right outside event horizon
                float dRim = abs(r - _HorizonRadius);
                half photonRing = (half)exp(-dRim * 26.0) * 1.35h;
                half electricArcs = (half)(smoothstep(0.76, 0.96, n2) * exp(-abs(r - (_HorizonRadius + 0.16)) * 6.0));

                // Dark gravitational event horizon + deep violet gravity well on bright concrete
                half horizonMask = (half)(1.0 - smoothstep(_HorizonRadius - 0.02, _HorizonRadius + 0.03, r));
                half outerFalloff = (half)smoothstep(1.0, 0.22, r);
                half wellBackdrop = (half)smoothstep(0.92, 0.24, r) * 0.78h;
                half accretionMask = (1.0h - horizonMask) * outerFalloff;

                half3 wellBase = half3(0.05h, 0.02h, 0.12h) * wellBackdrop;
                half3 plasmaRGB = wellBase + _SwirlColor.rgb * (arms * 0.85h + filaments * 1.45h) * accretionMask;
                plasmaRGB += _InnerRimColor.rgb * (photonRing + electricArcs * 1.35h) * outerFalloff;

                half3 finalRGB = lerp(plasmaRGB * _Intensity, _CoreColor.rgb, horizonMask * 0.99h);
                half alpha = saturate(max(horizonMask * _CoreColor.a, max(wellBackdrop, (arms * 0.65h + filaments * 0.75h + photonRing) * accretionMask)));
                alpha *= saturate(_Intensity) * input.color.a;

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }
}
