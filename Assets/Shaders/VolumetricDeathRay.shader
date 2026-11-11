Shader "Crusher/VolumetricDeathRay"
{
    Properties
    {
        [HDR] _CoreColor("Inner Core Color (HDR)", Color) = (6.50, 5.80, 4.80, 1.0)
        [HDR] _CoronaColor("Outer Plasma Corona (HDR)", Color) = (3.80, 0.70, 0.15, 1.0)
        [HDR] _CounterRingColor("Magnetic Garlic Counter-Ring (HDR)", Color) = (4.20, 1.65, 0.10, 1.0)

        // 0 = CNC Death Ray, 1 = Vampire Siphon (+ Garlic counter-rings), 2 = Chain Lightning Arc
        _BeamMode("Beam Mode (0=DeathRay,1=Siphon,2=ChainZap)", Float) = 0.0
        _CoreWidth("Core Width", Range(0.05, 0.60)) = 0.22
        _ScrollSpeed("Plasma Scroll Speed", Float) = 18.0
        _NoiseAmp("Turbulence / Arc Jitter", Range(0.0, 1.0)) = 0.35
        _GarlicIntensity("Magnetic Garlic Counter-Rings", Range(0.0, 1.0)) = 0.0
        _Fade("Master Alpha / Intensity", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+40"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "BeamForward"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied-like additive-alpha glow for crisp HDR beams on bright concrete
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertBeam
            #pragma fragment FragBeam
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _CoronaColor;
                half4 _CounterRingColor;
                half  _BeamMode;
                half  _CoreWidth;
                float _ScrollSpeed;
                half  _NoiseAmp;
                half  _GarlicIntensity;
                half  _Fade;
            CBUFFER_END

            float BeamHash(float2 p)
            {
                return frac(sin(dot(p, float2(41.31, 289.17))) * 43758.5453);
            }

            float BeamNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = BeamHash(i);
                float b = BeamHash(i + float2(1.0, 0.0));
                float c = BeamHash(i + float2(0.0, 1.0));
                float d = BeamHash(i + float2(1.0, 1.0));
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
                float2 uv         : TEXCOORD0; // x = along beam [0..1], y = across beam [0..1]
                half4  color      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertBeam(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv;
                output.color      = input.color;
                return output;
            }

            half4 FragBeam(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float u = input.uv.x;
                float v = input.uv.y * 2.0 - 1.0; // [-1..1] across beam width
                float t = _Time.y;

                // Mode 2: High-voltage jagged Chain Lightning arc
                if (_BeamMode > 1.5h)
                {
                    float snapTime = floor(t * 42.0);
                    float endPin = sin(saturate(u) * PI); // pin endpoints to 0 offset
                    float jag1 = (BeamNoise(float2(u * 9.0, snapTime * 1.7)) - 0.5) * 1.15;
                    float jag2 = (BeamNoise(float2(u * 24.0, snapTime * 3.1 + 7.0)) - 0.5) * 0.45;
                    float arcCenter = (jag1 + jag2) * _NoiseAmp * endPin;

                    float dist = abs(v - arcCenter);
                    half core = (half)(1.0 - smoothstep(0.02, _CoreWidth * 0.55, dist));
                    half glow = (half)exp(-dist * 5.2);

                    // Secondary sub-branch spark
                    float branchCenter = -jag1 * 0.65 * _NoiseAmp * endPin;
                    half branch = (half)(1.0 - smoothstep(0.01, _CoreWidth * 0.30, abs(v - branchCenter))) * 0.45h;

                    half3 rgb = _CoronaColor.rgb * (glow + branch) + _CoreColor.rgb * core;
                    half alpha = saturate((glow * 0.85h + core + branch) * (half)smoothstep(0.0, 0.06, endPin) * _Fade * input.color.a);
                    return half4(rgb * input.color.rgb * _Fade, alpha);
                }

                // Mode 1: Vampire Energy Siphon (+ optional Magnetic Garlic counter-pulse rings)
                if (_BeamMode > 0.5h)
                {
                    float wave = sin(u * 20.0 - t * _ScrollSpeed) * 0.14 * _NoiseAmp;
                    float dist = abs(v + wave);
                    half drainPackets = (half)(0.55 + 0.45 * sin(u * 28.0 - t * (_ScrollSpeed * 1.4)));
                    half core = (half)(1.0 - smoothstep(0.04, _CoreWidth, dist)) * drainPackets;
                    half corona = (half)exp(-dist * 3.8) * (1.0h - (half)abs(v));

                    half3 rgb = _CoronaColor.rgb * corona + _CoreColor.rgb * core;
                    half alpha = saturate((corona * 0.80h + core) * _Fade * input.color.a);

                    // Magnetic Garlic Lab > 0: reverse orange induction rings traveling back toward Vampire!
                    if (_GarlicIntensity > 0.01h)
                    {
                        float revWave = frac(u * 4.0 + t * 2.6);
                        half ringMask = (half)(smoothstep(0.72, 0.94, revWave) * (1.0 - abs(v)));
                        rgb += _CounterRingColor.rgb * (ringMask * _GarlicIntensity * 1.5h);
                        alpha = saturate(alpha + ringMask * _GarlicIntensity * 0.65h);
                    }

                    return half4(rgb * input.color.rgb, alpha);
                }

                // Mode 0: Volumetric CNC Plasma Death Ray
                float n = BeamNoise(float2(u * 12.0 - t * _ScrollSpeed, v * 3.5));
                float nFine = BeamNoise(float2(u * 28.0 - t * (_ScrollSpeed * 1.6), v * 7.0 + 3.1));
                float turbWidth = (n * 0.65 + nFine * 0.35 - 0.5) * 0.35 * _NoiseAmp;
                float dist = max(0.0, abs(v) + turbWidth);

                half core = (half)(1.0 - smoothstep(_CoreWidth * 0.35, _CoreWidth, dist));
                half corona = (half)(exp(-dist * 3.2) * (1.0 - smoothstep(0.72, 1.0, abs(v))));
                half shockPulse = (half)(0.82 + 0.18 * sin(u * 36.0 - t * (_ScrollSpeed * 2.0)));

                half muzzleTip = (half)(smoothstep(0.0, 0.03, u) * smoothstep(1.0, 0.95, u));
                half3 rgb = (_CoronaColor.rgb * corona + _CoreColor.rgb * core) * shockPulse * _Fade;
                half alpha = saturate((corona * 0.88h + core) * muzzleTip * _Fade * input.color.a);

                return half4(rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
