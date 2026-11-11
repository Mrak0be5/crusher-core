Shader "Crusher/ProceduralSDFUI"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1.0, 1.0, 1.0, 1.0)

        _BgTopColor("Panel Top Color", Color) = (0.16, 0.20, 0.26, 0.96)
        _BgBottomColor("Panel Bottom Color", Color) = (0.09, 0.11, 0.15, 0.98)
        _BorderColor("Border / Frame Color", Color) = (0.34, 0.44, 0.56, 1.0)
        [HDR] _AccentColor("Fill / Glow Accent Color", Color) = (1.00, 0.68, 0.12, 1.0)
        _TrailColor("Damage Trail / Secondary Fill Color", Color) = (1.00, 0.25, 0.25, 0.95)

        // 0 = Chamfered Industrial Card/Button, 1 = Radial Core HP/Shield Ring, 2 = FTUE Pulse Arrow/Badge
        _ShapeMode("Shape Mode (0=Card,1=RadialHP,2=PulseBadge)", Float) = 0.0
        _RectSize("Rect Pixel Size (Width, Height)", Vector) = (240, 88, 0, 0)
        _ChamferPx("Corner Chamfer (px)", Float) = 12.0
        _BorderPx("Border Thickness (px)", Float) = 2.5
        _FillAmount("Primary Fill [0..1]", Range(0.0, 1.0)) = 0.0
        _TrailAmount("Damage Trail / Shield Fill [0..1]", Range(0.0, 1.0)) = 0.0
        _RingThickness("Radial Ring Thickness", Range(0.03, 0.45)) = 0.13
        _GlowStrength("Active / Affordable Glow", Range(0.0, 2.0)) = 0.0

        // Standard uGUI Mask / ScrollRect Stencil support
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "ProceduralUI"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertUI
            #pragma fragment FragUI
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _ClipRect;

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half4  _BgTopColor;
                half4  _BgBottomColor;
                half4  _BorderColor;
                half4  _AccentColor;
                half4  _TrailColor;
                half   _ShapeMode;
                float4 _RectSize;
                float  _ChamferPx;
                float  _BorderPx;
                half   _FillAmount;
                half   _TrailAmount;
                half   _RingThickness;
                half   _GlowStrength;
            CBUFFER_END

            // Exact SDF of a 45-degree chamfered industrial box
            float SdfChamferBox(float2 p, float2 halfSize, float chamfer)
            {
                float2 d = abs(p) - halfSize;
                float boxDist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                float cutDist = (abs(p.x) + abs(p.y) - (halfSize.x + halfSize.y - chamfer)) * 0.70710678;
                return max(boxDist, cutDist);
            }

            // Subtle hexagonal carbon-fiber weave distance [0..0.5]
            float HexWeaveCell(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 hC = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);
                float2 cell = (dot(h.xy, h.xy) < dot(h.zw, h.zw)) ? h.xy : h.zw;
                cell = abs(cell);
                return 0.5 - max(dot(cell, s * 0.5), cell.x);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionOS : TEXCOORD1;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings VertUI(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv;
                output.color      = input.color * _Color;
                return output;
            }

            half4 FragUI(Varyings input) : SV_Target
            {
                half4 spriteSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float2 uv = input.uv;

                // Unconditional top-scope derivatives (WebGL2 ANGLE GLSL ES 3.00 compliant!)
                float2 pUnit = uv * 2.0 - 1.0;
                float  rUnit = length(pUnit);
                float  aaRadial = max(fwidth(rUnit), 0.006);

                float  bounce = sin(_Time.y * 8.0) * 0.08;
                float2 pBadge = float2(pUnit.x, pUnit.y + bounce);
                float  chevron = abs(pBadge.y + abs(pBadge.x) * 0.72 + 0.10) - 0.20;
                float  bounds  = max(abs(pBadge.x) - 0.78, abs(pBadge.y) - 0.72);
                float  dBadge  = max(chevron, bounds);
                float  aaBadge = max(fwidth(dBadge), 0.015);

                float2 sizePx   = max(_RectSize.xy, float2(16.0, 16.0));
                float2 halfSize = sizePx * 0.5 - 1.0;
                float2 pPx      = (uv - 0.5) * sizePx;
                float  chamfer  = min(_ChamferPx, min(halfSize.x, halfSize.y) * 0.85);
                float  dCard    = SdfChamferBox(pPx, halfSize, chamfer);
                float  aaCard   = max(fwidth(dCard), 0.75);

                half4 outCol = half4(1, 1, 1, 1);

                // Mode 1: Radial Dielectric Core Integrity Ring
                if (_ShapeMode > 0.5h && _ShapeMode < 1.5h)
                {
                    float outerR = 0.94;
                    float innerR = outerR - _RingThickness;

                    half ringMask  = (half)(smoothstep(innerR - aaRadial, innerR + aaRadial, rUnit) * (1.0 - smoothstep(outerR - aaRadial, outerR + aaRadial, rUnit)));
                    half frameMask = (half)(smoothstep(innerR - aaRadial * 2.2, innerR - aaRadial, rUnit) * (1.0 - smoothstep(outerR + aaRadial, outerR + aaRadial * 2.2, rUnit)));

                    float ang = frac((atan2(pUnit.x, pUnit.y) / TWO_PI) + 1.0);
                    float seg = abs(frac(ang * 24.0) - 0.5);
                    half notGap = (half)smoothstep(0.035, 0.065, seg);

                    half isFilled = (half)(1.0 - smoothstep(_FillAmount - 0.008, _FillAmount + 0.008, ang));
                    half isTrail  = (half)(1.0 - smoothstep(_TrailAmount - 0.008, _TrailAmount + 0.008, ang)) * (1.0h - isFilled);

                    half lowHpPulse = (_FillAmount < 0.25h) ? (half)(0.65 + 0.35 * sin(_Time.y * 12.0)) : 1.0h;
                    half3 hpRGB = (_FillAmount < 0.25h) ? lerp(_TrailColor.rgb, _AccentColor.rgb, 0.25h) * (1.4h * lowHpPulse) : _AccentColor.rgb;
                    // Radial inner bevel glint along ring middle
                    half midGlint = (half)exp(-abs(rUnit - (innerR + outerR) * 0.5) * 32.0) * 0.22h;

                    half3 rgb = _BgBottomColor.rgb;
                    rgb = lerp(rgb, _TrailColor.rgb * 1.35h, isTrail);
                    rgb = lerp(rgb, hpRGB + midGlint, isFilled);
                    rgb *= notGap;

                    half alpha = max(frameMask * 0.48h, ringMask * lerp(0.45h, 0.96h, max(isFilled, isTrail)));
                    outCol = half4(rgb, alpha) * input.color;
                }
                // Mode 2: FTUE Pulse Chevron Arrow / Floating Callout Badge
                else if (_ShapeMode >= 1.5h)
                {
                    half body = (half)(1.0 - smoothstep(-aaBadge, aaBadge, dBadge));
                    half halo = (half)exp(-max(0.0, dBadge) * 6.5) * 0.55h;
                    half3 rgb = lerp(_BorderColor.rgb, _AccentColor.rgb * 1.45h, body);
                    outCol = half4(rgb, saturate(body + halo)) * input.color;
                }
                // Mode 0 (Default): Chamfered Industrial HUD Panel / Upgrade Button (Hex-Carbon Weave + Metallic Sheen)
                else
                {
                    half outerAlpha = (half)(1.0 - smoothstep(-aaCard * 0.5, aaCard * 0.5, dCard));
                    half innerMask  = (half)(1.0 - smoothstep(-_BorderPx - aaCard, -_BorderPx, dCard));
                    half borderMask = saturate(outerAlpha - innerMask);

                    // Brushed vertical gradient + subtle hexagonal carbon-weave + machined scanline
                    half3 panelBg = lerp(_BgBottomColor.rgb, _BgTopColor.rgb, (half)uv.y);
                    float hexEdge = smoothstep(0.02, 0.11, HexWeaveCell(pPx * 0.075));
                    float diag = sin((pPx.x + pPx.y) * 0.42);
                    panelBg += (half)((hexEdge - 0.5) * 0.018 + diag * 0.008);

                    // Optional bottom/horizontal progress bar inside button (_FillAmount > 0)
                    if (_FillAmount > 0.001h)
                    {
                        half barZone = (half)(1.0 - smoothstep(0.16, 0.22, uv.y));
                        half barFill = (half)(1.0 - smoothstep(_FillAmount - 0.01, _FillAmount + 0.01, uv.x));
                        panelBg = lerp(panelBg, _AccentColor.rgb * 0.85h, barZone * barFill * 0.90h);
                    }

                    // Active / affordable inner rim glow + animated diagonal metallic sheen sweep!
                    half sheenGlint = 0.0h;
                    if (_GlowStrength > 0.001h)
                    {
                        half innerGlow = (half)exp(min(0.0, dCard + _BorderPx) * 0.14) * _GlowStrength;
                        float sweepPhase = frac(_Time.y * 0.42) * 2.6 - 0.8;
                        float sweepCoord = uv.x * 0.82 + uv.y * 0.36;
                        sheenGlint = (half)exp(-abs(sweepCoord - sweepPhase) * 14.0) * saturate(_GlowStrength);
                        panelBg += _AccentColor.rgb * (innerGlow * 0.42h + sheenGlint * 0.16h);
                    }

                    half3 borderRGB = lerp(_BorderColor.rgb, _AccentColor.rgb, saturate(_GlowStrength * 0.75h));
                    borderRGB += half3(0.24h, 0.28h, 0.34h) * (half)saturate(uv.y * 1.5 - 0.5);
                    borderRGB += half3(0.45h, 0.48h, 0.52h) * sheenGlint;

                    half3 finalRGB = lerp(panelBg, borderRGB, borderMask) * spriteSample.rgb;
                    half finalA = outerAlpha * lerp(_BgBottomColor.a, _BorderColor.a, borderMask) * spriteSample.a;
                    outCol = half4(finalRGB, finalA) * input.color;
                }

            #ifdef UNITY_UI_CLIP_RECT
                float2 clipInside = step(_ClipRect.xy, input.positionOS.xy) * step(input.positionOS.xy, _ClipRect.zw);
                outCol.a *= (half)(clipInside.x * clipInside.y);
            #endif

            #ifdef UNITY_UI_ALPHACLIP
                clip(outCol.a - 0.001h);
            #endif

                return outCol;
            }
            ENDHLSL
        }
    }
}
