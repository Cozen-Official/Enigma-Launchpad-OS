Shader "AudioLink/Launchpad/UI"
{
    Properties
    {
        _Gain("Gain", Range(0, 2)) = 1.0
        [ToggleUI] _AutoGain("Autogain", Float) = 0.0

        _Threshold0("Low Threshold", Range(0, 1)) = 0.5
        _Threshold1("Low Mid Threshold", Range(0, 1)) = 0.5
        _Threshold2("High Mid Threshold", Range(0, 1)) = 0.5
        _Threshold3("High Threshold", Range(0, 1)) = 0.5

        _X0("Crossover X0", Range(0.0, 0.168)) = 0.0
        _X1("Crossover X1", Range(0.242, 0.387)) = 0.25
        _X2("Crossover X2", Range(0.461, 0.628)) = 0.5
        _X3("Crossover X3", Range(0.704, 0.953)) = 0.75

        _HitFade("Hit Fade", Range(0, 1)) = 0.5
        _ExpFalloff("Exp Falloff", Range(0, 1)) = 0.5

        _BackgroundColor("Background Color", Color) = (0.033, 0.033, 0.033, 1.0)
        _ForegroundColor("Foreground Color", Color) = (0.075, 0.075, 0.075, 1.0)
        _InactiveColor("Inactive Color", Color) = (0.13, 0.13, 0.13, 1.0)
        _ActiveColor("Active Color", Color) = (0.8, 0.8, 0.8, 1.0)
        _BassColorBg("Bass Background Color", Color) = (0.1725, 0.047, 0.1686, 1.0)
        _BassColorMg("Bass Mid Color", Color) = (0.4039, 0.1059, 0.3922, 1.0)
        _BassColorFg("Bass Foreground Color", Color) = (0.5765, 0.1529, 0.5608, 1.0)
        _LowMidColorBg("Low Mid Background Color", Color) = (0.298, 0.2078, 0.0706, 1.0)
        _HighMidColorBg("High Mid Background Color", Color) = (0.1647, 0.2353, 0.0745, 1.0)
        _HighColorBg("High Background Color", Color) = (0.0471, 0.2039, 0.2667, 1.0)
        _HighColorFg("High Foreground Color", Color) = (0.1608, 0.6706, 0.8863, 1.0)

        _CornerRadius("Corner Radius", Range(0, 0.1)) = 0.025
        _FrameMargin("Frame Margin", Range(0, 0.1)) = 0.03
        _HandleRadius("Handle Radius", Range(0, 0.05)) = 0.007
        _OutlineWidth("Outline Width", Range(0, 0.01)) = 0.002
        _ElementMargin("Element Margin", Range(0, 0.1)) = 0.03
        _TopAreaHeight("Top Area Height", Range(0.1, 0.6)) = 0.35
        _GainSliderHeight("Gain Slider Height", Range(0.05, 0.3)) = 0.13
        _FadeSliderHeight("Fade Slider Height", Range(0.05, 0.3)) = 0.19
        _VerticalScale("Vertical Scale", Range(0.5, 1.0)) = 0.79

        _GainTexture ("Gain Texture", 2D) = "white" {}
        _AutoGainTexture ("Autogain Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Packages/com.llealloo.audiolink/Runtime/Shaders/AudioLink.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // Prevent z-fighting on mobile by moving the panel out a bit
                #ifdef SHADER_API_MOBILE
                v.vertex.z -= 0.0012;
                #endif
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Uniforms
            float _Gain;
            float _AutoGain;
            float _Threshold0;
            float _Threshold1;
            float _Threshold2;
            float _Threshold3;
            float _X0;
            float _X1;
            float _X2;
            float _X3;
            float _HitFade;
            float _ExpFalloff;
            float4 _BackgroundColor;
            float4 _ForegroundColor;
            float4 _InactiveColor;
            float4 _ActiveColor;
            float4 _BassColorBg;
            float4 _BassColorMg;
            float4 _BassColorFg;
            float4 _LowMidColorBg;
            float4 _HighMidColorBg;
            float4 _HighColorBg;
            float4 _HighColorFg;
            float _CornerRadius;
            float _FrameMargin;
            float _HandleRadius;
            float _OutlineWidth;
            float _ElementMargin;
            float _TopAreaHeight;
            float _GainSliderHeight;
            float _FadeSliderHeight;
            float _VerticalScale;
            sampler2D _GainTexture;
            float4 _GainTexture_TexelSize;
            sampler2D _AutoGainTexture;
            float4 _AutoGainTexture_TexelSize;

            // Colors
            float3 getBackgroundColor() { return _BackgroundColor.rgb; }
            float3 getForegroundColor() { return _ForegroundColor.rgb; }
            float3 getInactiveColor() { return _InactiveColor.rgb; }
            float3 getActiveColor() { return _ActiveColor.rgb; }
            float3 getBassColorBg() { return pow(_BassColorBg.rgb, 2.2); }
            float3 getBassColorMg() { return pow(_BassColorMg.rgb, 2.2); }
            float3 getBassColorFg() { return pow(_BassColorFg.rgb, 2.2); }
            float3 getLowMidColorBg() { return pow(_LowMidColorBg.rgb, 2.2); }
            float3 getHighMidColorBg() { return pow(_HighMidColorBg.rgb, 2.2); }
            float3 getHighColorBg() { return pow(_HighColorBg.rgb, 2.2); }
            float3 getHighColorFg() { return pow(_HighColorFg.rgb, 2.2); }

            #define BACKGROUND_COLOR getBackgroundColor()
            #define FOREGROUND_COLOR getForegroundColor()
            #define INACTIVE_COLOR getInactiveColor()
            #define ACTIVE_COLOR getActiveColor()
            #define BASS_COLOR_BG getBassColorBg()
            #define BASS_COLOR_MG getBassColorMg()
            #define BASS_COLOR_FG getBassColorFg()
            #define LOWMID_COLOR_BG getLowMidColorBg()
            #define HIGHMID_COLOR_BG getHighMidColorBg()
            #define HIGH_COLOR_BG getHighColorBg()
            #define HIGH_COLOR_FG getHighColorFg()

            // Spacing
            float getCornerRadius() { return _CornerRadius; }
            float getFrameMargin() { return _FrameMargin; }
            float getHandleRadius() { return _HandleRadius; }
            float getOutlineWidth() { return _OutlineWidth; }
            #define CORNER_RADIUS getCornerRadius()
            #define FRAME_MARGIN getFrameMargin()
            #define HANDLE_RADIUS getHandleRadius()
            #define OUTLINE_WIDTH getOutlineWidth()

            #define remap(value, low1, high1, low2, high2) ((low2) + ((value) - (low1)) * ((high2) - (low2)) / ((high1) - (low1)))

            #define COHERENT_CONDITION(condition) ((condition) || any(fwidth(condition)))
            #define ADD_ELEMENT(existing, elementColor, elementDist) [branch] if (COHERENT_CONDITION(elementDist <= 0.01)) addElement(existing, elementColor, elementDist)

            float3 selectColor(uint i, float3 a, float3 b, float3 c, float3 d)
            {
                return float4x4(
                    float4(a, 0.0),
                    float4(b, 0.0),
                    float4(c, 0.0),
                    float4(d, 0.0)
                )[i % 4];
            }

            float3 selectColorLerp(float i, float3 a, float3 b, float3 c, float3 d)
            {
                int me = floor(i);
                float3 meColor = selectColor(me, a, b, c, d);

                // avoid singularity at 0.5
                if (COHERENT_CONDITION(distance(frac(i), 0.5) < 0.1))
                    return meColor;

                int side = sign(frac(i) - 0.5);
                int other = clamp(me + side, 0, 3);

                float3 otherColor = selectColor(other, a, b, c, d);

                float dist = round(i) - i;
                const float pixelDiagonal = sqrt(2.0) / 2.0 * side;
                float distDerivativeLength = sqrt(pow(ddx(dist), 2) + pow(ddy(dist), 2));

                return lerp(otherColor, meColor, smoothstep(-pixelDiagonal, pixelDiagonal, dist/distDerivativeLength));
            }

            float3 getBandColor(uint i) { return selectColor(i, BASS_COLOR_BG, LOWMID_COLOR_BG, HIGHMID_COLOR_BG, HIGH_COLOR_BG); }

            float2x2 rotationMatrix(float angle)
            {
                return float2x2(
                    float2(cos(angle), -sin(angle)),
                    float2(sin(angle), cos(angle))
                );
            }

            float2 translate(float2 p, float2 offset)
            {
                return p - offset;
            }

            float2 rotate(float2 p, float angle)
            {
                return mul(rotationMatrix(angle), p);
            }

            float shell(float d, float thickness)
            {
                return abs(d) - thickness;
            }

            float inflate(float d, float thickness)
            {
                return d - thickness;
            }

            float lerpstep(float a, float b, float x)
            {
                return saturate((x - a)/(b - a));
            }

            void addElement(inout float3 existing, float3 elementColor, float elementDist)
            {
                const float pixelDiagonal = sqrt(2.0) / 2.0;
                float distDerivativeLength = sqrt(pow(ddx(elementDist), 2) + pow(ddy(elementDist), 2));
                existing = lerp(elementColor, existing, lerpstep(-pixelDiagonal, pixelDiagonal, elementDist/distDerivativeLength));
            }

            float sdRoundedBoxCentered(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x>0.0)?r.xy : r.zw;
                r.x  = (p.y>0.0)?r.x  : r.y;
                float2 q = abs(p)-b*0.5+r.x;
                return min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r.x;
            }

            float sdRoundedBoxTopLeft(float2 p, float2 b, float4 r)
            {
                return sdRoundedBoxCentered(translate(p, b*0.5), b, r);
            }

            float sdRoundedBoxBottomRight(float2 p, float2 b, float4 r)
            {
                return sdRoundedBoxCentered(translate(p, float2(b.x,-b.y)*0.5), b, r);
            }

            float sdSphere(float2 p, float r)
            {
                return length(p) - r;
            }

            float sdTriangleIsosceles(float2 p, float2 q)
            {
                p.x = abs(p.x);
                float2 a = p - q*clamp( dot(p,q)/dot(q,q), 0.0, 1.0 );
                float2 b = p - q*float2( clamp( p.x/q.x, 0.0, 1.0 ), 1.0 );
                float s = -sign( q.y );
                float2 d = min( float2( dot(a,a), s*(p.x*q.y-p.y*q.x) ),
                            float2( dot(b,b), s*(p.y-q.y)  ));
                return -sqrt(d.x)*sign(d.y);
            }

            float sdTriangleRight(float2 p, float halfWidth, float halfHeight)
            {
                float2 end = float2(halfWidth, -halfHeight);
                float2 d = p - end * clamp(dot(p, end) / dot(end, end), -1.0, 1.0);
                if (max(d.x, d.y) > 0.0) {
                return length(d);
                }
                p += float2(halfWidth, halfHeight);
                if (max(p.x, p.y) > 0.0) {
                    return -min(length(d), min(p.x, p.y));
                }
                return length(p);
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p-a, ba = b-a;
                float h = clamp( dot(pa,ba)/dot(ba,ba), 0.0, 1.0 );
                return length( pa - ba*h );
            }

            #define TEX2D_MSDF(tex, uv) tex2DMSDF(tex, tex##_TexelSize.xy * 4.0, uv)

            float tex2DMSDF(sampler2D tex, float2 unit, float2 uv)
            {
                float3 c = tex2D(tex, uv).rgb;
                return saturate(
                    (max(min(c.r, c.g), min(max(c.r, c.g), c.b)) - 0.5) *
                    max(dot(unit, 0.5 / fwidth(uv)), 1) + 0.5
                );
            }

            float3 drawTopArea(float2 uv)
            {
                float3 color = FOREGROUND_COLOR;

                float areaWidth = 1.0 - FRAME_MARGIN * 2;
                float areaHeight = _TopAreaHeight;
                float handleWidth = 0.015 * areaWidth;

                float threshold[4] = { _Threshold0, _Threshold1, _Threshold2, _Threshold3 };
                float crossover[4] = { _X0 * areaWidth, _X1 * areaWidth, _X2 * areaWidth, _X3 * areaWidth };

                // prefix sum to calculate offsets and sizes for boxes
                uint start = 0;
                uint stop = 4;
                float currentBoxOffset = crossover[start];
                float boxOffsets[4] = { 0, 0, 0, 0 };
                float boxWidths[4] = { 0, 0, 0, 0 };
                for (uint i = 0; i < 4; i++)
                {
                    float boxWidth = 0.0;
                    if (i == 3) // The last box should just stretch to fill
                        boxWidth = areaWidth - currentBoxOffset;
                    else
                        boxWidth = crossover[i + 1] - crossover[i];

                    boxOffsets[i] = currentBoxOffset;
                    boxWidths[i] = boxWidth;

                    // Keep track of the range of boxes we need to draw
                    if (COHERENT_CONDITION(uv.x > currentBoxOffset + OUTLINE_WIDTH))
                        start = i;
                    if (COHERENT_CONDITION(uv.x < currentBoxOffset + boxWidth - handleWidth))
                        stop = min(stop, i + 1);

                    currentBoxOffset += boxWidth;
                }

                // waveform calculation
                uint totalBins = AUDIOLINK_EXPBINS * AUDIOLINK_EXPOCT;
                uint noteno = AudioLinkRemap(uv.x, 0., 1., AUDIOLINK_4BAND_FREQFLOOR * totalBins, AUDIOLINK_4BAND_FREQCEILING * totalBins);
                float notenof = AudioLinkRemap(uv.x, 0., 1., AUDIOLINK_4BAND_FREQFLOOR * totalBins, AUDIOLINK_4BAND_FREQCEILING * totalBins);
                float4 specLow = AudioLinkData(float2(fmod(noteno, 128), (noteno/128)+4.0));
                float4 specHigh = AudioLinkData(float2(fmod(noteno+1, 128), ((noteno+1)/128)+4.0));
                float4 intensity = lerp(specLow, specHigh, frac(notenof)) * _Gain;
                float bandIntensity = AudioLinkData(float2(0., start ^ 0)); // XOR with 0 to avoid FXC miscompilation
                float funcY = areaHeight - (intensity.g * areaHeight);
                float waveformDist = smoothstep(0.005, 0.003, funcY - uv.y);
                float waveformDistAbs = abs(smoothstep(0.005, 0.003, abs(funcY - uv.y)));

                // background waveform
                color = lerp(color, color * 2, waveformDist);
                color = lerp(color, color * 2, waveformDistAbs);

                // This optimization increases performance, but introduces aliasing. The perf difference is only really noticeable on Quest.
                #if defined(UNITY_PBS_USE_BRDF2) || defined(SHADER_API_MOBILE)
                [loop] for (uint i = start; i < min(stop, 4); i++)
                #else
                for (uint i = 0; i < 4; i++)
                #endif
                {
                    float boxHeight = threshold[i] * areaHeight;
                    float boxWidth = boxWidths[i];
                    float boxOffset = boxOffsets[i];

                    float leftCornerRadius = i == 0 ? CORNER_RADIUS : 0.0;
                    float rightCornerRadius = i == 3 ? CORNER_RADIUS : 0.0;
                    float boxDist = sdRoundedBoxBottomRight(
                        translate(uv, float2(boxOffset, areaHeight)),
                        float2(boxWidth, boxHeight),
                        float4(rightCornerRadius, CORNER_RADIUS, leftCornerRadius, CORNER_RADIUS)
                    );

                    // colored inner portion
                    float3 innerColor = getBandColor(i);
                    innerColor = lerp(innerColor, innerColor * 3, waveformDist);
                    innerColor = lerp(innerColor, lerp(innerColor * 3, 1.0, bandIntensity > threshold[i]), waveformDistAbs);
                    ADD_ELEMENT(color, innerColor, boxDist+OUTLINE_WIDTH);

                    // outer shell
                    float shellDist = shell(boxDist, OUTLINE_WIDTH);
                    ADD_ELEMENT(color, ACTIVE_COLOR, shellDist);

                    // Top pivot
                    float handleDist = sdSphere(
                        translate(uv, float2(boxWidth * 0.5 + boxOffset, areaHeight-boxHeight)),
                        HANDLE_RADIUS
                    );
                    ADD_ELEMENT(color, 1.0, handleDist);

                    // Side pivot
                    handleDist = sdRoundedBoxCentered(
                        translate(uv, float2(boxOffset, areaHeight - boxHeight * 0.5)),
                        float2(handleWidth, 0.35 * boxHeight),
                        HANDLE_RADIUS
                    );
                    ADD_ELEMENT(color, 1.0, handleDist);
                }

                return color;
            }

            float3 drawGainArea(float2 uv, float2 size)
            {
                float3 inactiveColor = INACTIVE_COLOR;
                float3 activeColor = ACTIVE_COLOR;
                float3 t = _Gain / 2.0f;

                float3 color = FOREGROUND_COLOR;

                float gainIcon = TEX2D_MSDF(_GainTexture, saturate((uv - float2(0.01, 0.0)) / size.y));
                color = lerp(color, ACTIVE_COLOR, gainIcon.r);

                const float sliderOffsetLeft = 0.16;
                const float sliderOffsetRight = 0.02;

                // Background fill
                float maxTriangleWidth = size.x - sliderOffsetLeft - sliderOffsetRight;
                float bgTriangleDist = inflate(sdTriangleIsosceles(
                    rotate(translate(uv, float2(sliderOffsetLeft, size.y * 0.5)), UNITY_PI*0.5),
                    float2(size.y*0.3, maxTriangleWidth)
                ), 0.002);
                ADD_ELEMENT(color, inactiveColor, bgTriangleDist);

                // Current active area
                float currentTriangleWidth = maxTriangleWidth * t;
                float currentTriangleDist = max(bgTriangleDist, uv.x - currentTriangleWidth - sliderOffsetLeft);
                ADD_ELEMENT(color, activeColor, currentTriangleDist);

                // Slider handle
                float handleDist = sdSphere(
                    translate(uv, float2(currentTriangleWidth + sliderOffsetLeft, size.y * 0.5)),
                    HANDLE_RADIUS
                );
                ADD_ELEMENT(color, ACTIVE_COLOR, handleDist);

                // Slider vertical grip
                float gripDist = abs(uv.x - currentTriangleWidth - sliderOffsetLeft) - OUTLINE_WIDTH;
                ADD_ELEMENT(color, ACTIVE_COLOR, gripDist);

                return color;
            }

            float drawAutoGainButton(float2 uv, float2 size)
            {
                float2 scaledUV = uv / size;
                float autoGainIcon = TEX2D_MSDF(_AutoGainTexture, float2(scaledUV.x, 1-scaledUV.y));
                return lerp(FOREGROUND_COLOR, _AutoGain ? ACTIVE_COLOR : INACTIVE_COLOR, autoGainIcon);
            }

            float3 drawHitFadeArea(float2 uv, float2 size)
            {
                float3 color = FOREGROUND_COLOR;

                // Background fill
                float2 triUV = -(uv - float2(size.x / 2, size.y / 2));

                float halfWidth = 0.45 * size.x;
                float halfHeight = 0.37 * size.y;
                float fullWidth = halfWidth * 2;
                float fullHeight = halfHeight * 2;
                float bgTriangleDist = inflate(sdTriangleRight(triUV, halfWidth, halfHeight), 0.002);
                ADD_ELEMENT(color, INACTIVE_COLOR, bgTriangleDist);

                // Current active area
                float remainingWidth = size.x - fullWidth;
                float remainingHeight = size.y - fullHeight;
                float marginX = remainingWidth / 2;
                float marginY = remainingHeight / 2;

                float invHitFade = 1 - _HitFade;
                triUV.x += halfWidth * invHitFade;
                float fgTriangleDist = inflate(sdTriangleRight(triUV, halfWidth * _HitFade, halfHeight), 0.002);
                ADD_ELEMENT(color, ACTIVE_COLOR, fgTriangleDist);

                // Slider handle
                float handleDist = sdSphere(
                    translate(uv, float2(invHitFade * fullWidth + marginX, size.y * 0.5)),
                    HANDLE_RADIUS
                );
                ADD_ELEMENT(color, ACTIVE_COLOR, handleDist);

                // Slider vertical grip
                float gripDist = abs(uv.x - invHitFade * halfWidth * 2 - marginX) - OUTLINE_WIDTH;
                ADD_ELEMENT(color, ACTIVE_COLOR, gripDist);

                return color;
            }

            float3 drawExpFalloffArea(float2 uv, float2 size)
            {
                float3 color = FOREGROUND_COLOR;

                // Background fill
                float2 triUV = -(uv - float2(size.x / 2, size.y / 2));

                float halfWidth = 0.45 * size.x;
                float halfHeight = 0.37 * size.y;
                float fullWidth = halfWidth * 2;
                float fullHeight = halfHeight * 2;
                float bgTriangleDist = inflate(sdTriangleRight(triUV, halfWidth, halfHeight), 0.002);
                ADD_ELEMENT(color, INACTIVE_COLOR, bgTriangleDist);

                // Current active area
                float remainingWidth = size.x - fullWidth;
                float remainingHeight = size.y - fullHeight;
                float marginX = remainingWidth / 2;
                float marginY = remainingHeight / 2;
                float triUVx = remap(uv.x, marginX, size.x-marginX, 0, 1);
                float triUVy = remap(uv.y, marginY, size.y-marginY, 0, 1);

                float expFalloffY = (1.0 + (pow(triUVx, 4.0) * _ExpFalloff) - _ExpFalloff) * triUVx;
                float fgDist = inflate((1.0 - triUVy) - expFalloffY, 0.02);
                ADD_ELEMENT(color, ACTIVE_COLOR, max(bgTriangleDist, fgDist*0.1));

                // Slider handle
                float handleDist = sdSphere(
                    translate(uv, float2(_ExpFalloff * fullWidth + marginX, size.y * 0.5)),
                    HANDLE_RADIUS
                );
                ADD_ELEMENT(color, ACTIVE_COLOR, handleDist);

                // Slider vertical grip
                float gripDist = abs(uv.x - _ExpFalloff * halfWidth * 2 - marginX) - OUTLINE_WIDTH;
                ADD_ELEMENT(color, ACTIVE_COLOR, gripDist);

                return color;
            }

            float3 drawUI(float2 uv)
            {
                float3 color = _BackgroundColor.rgb;

                float margin = _ElementMargin;
                float currentY = 0;

                // Top area
                float2 topAreaOrigin = translate(uv, float2(FRAME_MARGIN, FRAME_MARGIN));
                float2 topAreaSize = float2(1.0 - FRAME_MARGIN * 2, _TopAreaHeight);
                float topAreaDist = sdRoundedBoxTopLeft(topAreaOrigin, topAreaSize, CORNER_RADIUS);
                ADD_ELEMENT(color, drawTopArea(topAreaOrigin), topAreaDist);
                currentY += topAreaSize.y + margin;

                const float gainSliderHeight = _GainSliderHeight;
                const float gainSliderWidth = topAreaSize.x - gainSliderHeight - margin;
                const float fadeSliderHeight = _FadeSliderHeight;

                // Gain slider
                float2 gainSliderOrigin = translate(uv, FRAME_MARGIN + float2(0, currentY));
                float2 gainSliderSize = float2(gainSliderWidth, gainSliderHeight);
                float gainSliderDist = sdRoundedBoxTopLeft(gainSliderOrigin, gainSliderSize, CORNER_RADIUS);
                ADD_ELEMENT(color, drawGainArea(gainSliderOrigin, gainSliderSize), gainSliderDist);

                // Autogain button
                float2 autogainButtonOrigin = translate(uv, FRAME_MARGIN + float2(gainSliderWidth + margin, currentY));
                float2 autogainButtonSize = float2(gainSliderHeight, gainSliderHeight);
                float autogainButtonDist = sdRoundedBoxTopLeft(autogainButtonOrigin, autogainButtonSize, CORNER_RADIUS);
                ADD_ELEMENT(color, drawAutoGainButton(autogainButtonOrigin, autogainButtonSize), autogainButtonDist);
                currentY += autogainButtonSize.y + margin;

                // Hit fade
                float2 hitFadeAreaOrigin = translate(uv, FRAME_MARGIN + float2(0, currentY));
                float2 hitFadeAreaSize = float2(topAreaSize.x * 0.5 - margin * 0.5, fadeSliderHeight);
                float hitFadeAreaDist = sdRoundedBoxTopLeft(hitFadeAreaOrigin, hitFadeAreaSize, CORNER_RADIUS);
                ADD_ELEMENT(color, drawHitFadeArea(hitFadeAreaOrigin, hitFadeAreaSize), hitFadeAreaDist);

                // Exp falloff
                float2 expFalloffAreaOrigin = translate(uv, FRAME_MARGIN + float2(hitFadeAreaSize.x + margin, currentY));
                float2 expFalloffAreaSize = float2(topAreaSize.x * 0.5 - margin * 0.5, fadeSliderHeight);
                float expFalloffAreaDist = sdRoundedBoxTopLeft(expFalloffAreaOrigin, expFalloffAreaSize, CORNER_RADIUS);
                ADD_ELEMENT(color, drawExpFalloffArea(expFalloffAreaOrigin, expFalloffAreaSize), expFalloffAreaDist);

                return color;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 uv = float2(i.uv.x, 1.0 - i.uv.y);
                uv.y *= _VerticalScale; // adjusted aspect ratio for shorter UI
                return float4(drawUI(uv), 1);
            }
            ENDCG
        }
    }
}
