Shader "Crusher/VolumetricDeathRay"
{
    Properties
    {
        [HDR] _CoreColor("Inner Core Color (HDR)", Color) = (8.20, 7.80, 7.20, 1.0)
        [HDR] _CoronaColor("Outer Plasma Corona (HDR)", Color) = (4.20, 0.72, 0.16, 1.0)
        [HDR] _CounterRingColor("Magnetic Garlic Counter-Ring (HDR)", Color) = (4.60, 1.85, 0.12, 1.0)

        // 0 = CNC Death Ray, 1 = Vampire Siphon (+ Garlic counter-rings), 2 = Chain Lightning Arc
        _BeamMode("Beam Mode (0=DeathRay,1=Siphon,2=ChainZap)", Float) = 0.0
        _CoreWidth("Core Width", Range(0.05, 0.60)) = 0.24
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

                // Mode 2: High-voltage jagged Chain Lightning arc with dual sub-branches
                if (_BeamMode > 1.5h)
                {
                    float snapTime = floor(t * 44.0);
                    float endPin = sin(saturate(u) * PI);
                    float jag1 = (BeamNoise(float2(u * 9.5, snapTime * 1.7)) - 0.5) * 1.18;
                    float jag2 = (BeamNoise(float2(u * 26.0, snapTime * 3.1 + 7.0)) - 0.5) * 0.48;
                    float arcCenter = (jag1 + jag2) * _NoiseAmp * endPin;

                    float dist = abs(v - arcCenter);
                    half whiteSpine = (half)(1.0 - smoothstep(0.015, _CoreWidth * 0.42, dist));
                    half core = (half)(1.0 - smoothstep(0.03, _CoreWidth * 0.68, dist));
                    half glow = (half)exp(-dist * 4.8);

                    float branchCenter = (-jag1 * 0.72 + jag2 * 0.5) * _NoiseAmp * endPin;
                    half branch = (half)(1.0 - smoothstep(0.01, _CoreWidth * 0.32, abs(v - branchCenter))) * 0.55h;

                    half3 rgb = _CoronaColor.rgb * (glow + branch) + _CoreColor.rgb * core + half3(4.0h, 4.5h, 5.0h) * whiteSpine;
                    half alpha = saturate((glow * 0.88h + core + branch) * (half)smoothstep(0.0, 0.05, endPin) * _Fade * input.color.a);
                    return half4(rgb * input.color.rgb * _Fade, alpha);
                }

                // Mode 1: Vampire Energy Siphon (+ Magnetic Garlic counter-pulse rings)
                if (_BeamMode > 0.5h)
                {
                    float wave = sin(u * 20.0 - t * _ScrollSpeed) * 0.14 * _NoiseAmp;
                    float dist = abs(v + wave);
                    half drainPackets = (half)(0.55 + 0.45 * sin(u * 30.0 - t * (_ScrollSpeed * 1.45)));
                    half core = (half)(1.0 - smoothstep(0.035, _CoreWidth, dist)) * drainPackets;
                    half corona = (half)exp(-dist * 3.6) * (1.0h - (half)abs(v));

                    half3 rgb = _CoronaColor.rgb * corona + _CoreColor.rgb * core;
                    half alpha = saturate((corona * 0.82h + core) * _Fade * input.color.a);

                    if (_GarlicIntensity > 0.01h)
                    {
                        float revWave = frac(u * 4.5 + t * 2.8);
                        half ringMask = (half)(smoothstep(0.70, 0.95, revWave) * (1.0 - abs(v) * 0.85));
                        rgb += _CounterRingColor.rgb * (ringMask * _GarlicIntensity * 1.65h);
                        alpha = saturate(alpha + ringMask * _GarlicIntensity * 0.72h);
                    }

                    return half4(rgb * input.color.rgb, alpha);
                }

                // Mode 0: Volumetric CNC Plasma Death Ray (blazing white plasma core + helical electric corona)
                float n = BeamNoise(float2(u * 13.0 - t * _ScrollSpeed, v * 3.6));
                float nFine = BeamNoise(float2(u * 32.0 - t * (_ScrollSpeed * 1.75), v * 7.5 + 3.1));
                float turbWidth = (n * 0.62 + nFine * 0.38 - 0.5) * 0.34 * _NoiseAmp;
                float dist = max(0.0, abs(v) + turbWidth);

                // Blazing pure-white core + crimson-orange plasma sheath
                half whiteHot = (half)(1.0 - smoothstep(_CoreWidth * 0.15, _CoreWidth * 0.58, dist));
                half core     = (half)(1.0 - smoothstep(_CoreWidth * 0.38, _CoreWidth * 1.05, dist));
                half corona   = (half)(exp(-dist * 3.0) * (1.0 - smoothstep(0.70, 1.0, abs(v))));

                // Coiling helical electric corona filaments wrapping the plasma beam
                float helixA = sin(u * 34.0 - t * (_ScrollSpeed * 1.85)) * 0.44;
                float helixB = -sin(u * 27.0 - t * (_ScrollSpeed * 1.45) + 1.57) * 0.38;
                half arcFilaments = (half)(exp(-abs(v - helixA) * 14.0) + exp(-abs(v - helixB) * 16.0) * 0.75);
                arcFilaments *= (half)(1.0 - smoothstep(0.75, 1.0, abs(v)));

                half shockPulse = (half)(0.84 + 0.16 * sin(u * 38.0 - t * (_ScrollSpeed * 2.1)));
                half muzzleFlare = (half)exp(-u * 18.0) * 0.75h;
                half muzzleTip = (half)(smoothstep(0.0, 0.025, u) * smoothstep(1.0, 0.94, u));

                half3 rgb = _CoronaColor.rgb * (corona + arcFilaments * 0.85h)
                          + _CoreColor.rgb * (core + muzzleFlare)
                          + half3(4.5h, 4.8h, 5.2h) * (whiteHot + arcFilaments * 0.35h);
                rgb *= shockPulse * _Fade;
                half alpha = saturate((corona * 0.88h + core + arcFilaments * 0.45h) * muzzleTip * _Fade * input.color.a);

                return half4(rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
