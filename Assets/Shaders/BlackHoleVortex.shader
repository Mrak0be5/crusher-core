Shader "Crusher/BlackHoleVortex"
{
    Properties
    {
        [HDR] _InnerRimColor("Photon Ring / Shockwave Color (HDR)", Color) = (1.60, 3.80, 6.20, 1.0)
        [HDR] _SwirlColor("Accretion Plasma Color (HDR)", Color) = (1.40, 0.55, 3.60, 1.0)
        _CoreColor("Event Horizon Core Color", Color) = (0.01, 0.01, 0.03, 0.96)

        _HorizonRadius("Event Horizon / Shockwave Front (UV)", Range(0.04, 0.98)) = 0.19
        _Twist("Spiral Twist", Range(1.0, 12.0)) = 5.6
        _SpinSpeed("Angular Spin Speed", Float) = 7.8
        _InfallSpeed("Radial Pull Speed", Float) = 3.1
        _Intensity("Overall Intensity / Fade", Range(0.0, 3.0)) = 1.0
        // 0 = Black Hole Vortex, 1 = Golden Bot Field, 2 = Expanding Plasma / Boss Shockwave Ring
        _FieldZoneMode("Zone Mode (0=BH,1=GoldBot,2=Shockwave)", Float) = 0.0
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

            // Self-contained vortex & shockwave shader (supportsCameraOpaqueTexture = false -> fast WebGL2 & mobile MSAA!)
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

                // Mode 2: Expanding Supersonic Plasma / Boss Shatter Shockwave Ring
                if (_FieldZoneMode > 1.5h)
                {
                    float front = clamp((float)_HorizonRadius, 0.08, 0.94);
                    float dMain = r - front;
                    // Razor-sharp outer Mach shock front + trailing wake
                    half machFront = (half)(exp(-abs(dMain) * 28.0) * 1.45);
                    half wakeGlow  = (half)(exp(min(0.0, dMain) * 8.5) * step(r, front) * 0.55);
                    // Secondary inner resonance ring + radial energy filaments
                    float innerR = front * 0.62;
                    half innerRing = (half)(exp(-abs(r - innerR) * 22.0) * 0.65);
                    half streaks   = (half)pow(saturate(0.5 + 0.5 * sin(theta * 18.0 + r * 6.0)), 4.0) * wakeGlow;

                    half edgeSoft = (half)(1.0 - smoothstep(0.92, 1.0, r));
                    half mask = (machFront + wakeGlow * 0.45h + innerRing + streaks * 0.8h) * edgeSoft;
                    half3 rgb = lerp(_SwirlColor.rgb, _InnerRimColor.rgb, saturate(machFront + innerRing * 0.5h));
                    rgb = lerp(rgb, half3(5.5h, 5.5h, 5.5h), saturate(machFront * 0.45h));
                    half alpha = saturate(mask * _Intensity * input.color.a);
                    return half4(rgb * (_Intensity * input.color.rgb), alpha);
                }

                // Mode 1: Golden Bot bonus zone (pulsing outer halo + rotating induction segments)
                if (_FieldZoneMode > 0.5h)
                {
                    half outerEdge = (half)(smoothstep(0.85, 0.95, r) * (1.0 - smoothstep(0.95, 1.0, r)));
                    half innerEdge = (half)(smoothstep(0.68, 0.72, r) * (1.0 - smoothstep(0.72, 0.76, r))) * 0.55h;
                    half dash = (half)smoothstep(-0.1, 0.2, sin(theta * 12.0 - t * _SpinSpeed));
                    half dashInner = (half)smoothstep(-0.1, 0.2, sin(theta * 8.0 + t * (_SpinSpeed * 0.7)));
                    half innerFill = (half)(1.0 - r * r) * 0.20h;
                    half pulseWave = (half)(0.5 + 0.5 * sin(r * 16.0 - t * 5.2)) * 0.16h * (half)(1.0 - r);

                    half3 col = lerp(_SwirlColor.rgb, _InnerRimColor.rgb, max(outerEdge, innerEdge));
                    half ringAlpha = outerEdge * (0.45h + 0.55h * dash) + innerEdge * dashInner;
                    half alpha = saturate((ringAlpha + innerFill + pulseWave) * _Intensity * input.color.a);
                    return half4(col * _Intensity * input.color.rgb, alpha);
                }

                // Mode 0: Black Hole electromagnetic accretion vortex + gravitational lensing halo
                float spiralAngle = theta + _Twist / (r + 0.08) - t * _SpinSpeed;
                float spiralFine  = theta + (_Twist * 1.45) / (r + 0.065) - t * (_SpinSpeed * 1.32);
                float2 polarUV1 = float2(spiralAngle * 1.91, r * 6.5 + t * _InfallSpeed);
                float2 polarUV2 = float2(spiralFine * 3.82 - t * 1.8, r * 12.0 + t * (_InfallSpeed * 1.5));

                float n1 = VortexNoise(polarUV1);
                float n2 = VortexNoise(polarUV2);
                half arms      = (half)saturate(0.5 + 0.5 * sin(spiralAngle * 3.0 + n1 * 2.8));
                half fineArms  = (half)saturate(0.5 + 0.5 * sin(spiralFine * 7.0 - n2 * 2.4));
                half filaments = (half)pow(saturate(n1 * 0.55 + n2 * 0.55), 2.0) * (0.55h + 0.75h * fineArms);

                // Sharp Einstein photon ring + outer gravitational lensing halo ring
                float dRim = abs(r - _HorizonRadius);
                half photonRing  = (half)exp(-dRim * 28.0) * 1.55h;
                half lensingHalo = (half)(exp(-abs(r - 0.86) * 14.0) * (0.55 + 0.45 * sin(theta * 4.0 + t * 3.5))) * 0.65h;
                half electricArcs = (half)(smoothstep(0.74, 0.96, n2) * exp(-abs(r - (_HorizonRadius + 0.16)) * 5.8));

                // Pitch-black Event Horizon + deep violet gravity well on bright steel deck
                half horizonMask = (half)(1.0 - smoothstep(_HorizonRadius - 0.02, _HorizonRadius + 0.028, r));
                half outerFalloff = (half)smoothstep(1.0, 0.20, r);
                half wellBackdrop = (half)smoothstep(0.92, 0.22, r) * 0.82h;
                half accretionMask = (1.0h - horizonMask) * outerFalloff;

                half3 wellBase = half3(0.04h, 0.015h, 0.11h) * wellBackdrop;
                half3 plasmaRGB = wellBase + _SwirlColor.rgb * (arms * 0.85h + filaments * 1.65h) * accretionMask;
                plasmaRGB += _InnerRimColor.rgb * (photonRing + electricArcs * 1.45h + lensingHalo) * (1.0h - horizonMask * 0.8h);

                half3 finalRGB = lerp(plasmaRGB * _Intensity, _CoreColor.rgb, horizonMask * 0.995h);
                half alpha = saturate(max(
                    horizonMask * _CoreColor.a,
                    max(wellBackdrop, (arms * 0.68h + filaments * 0.85h + photonRing + lensingHalo) * (1.0h - horizonMask))));
                alpha *= saturate(_Intensity) * input.color.a;

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }
}
