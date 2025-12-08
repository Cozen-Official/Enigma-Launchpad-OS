Shader "Cozen/Surface/Waveform"
{
    /****************************************************************
        Waveform Display (CDJ / Serato style layered bands)
        Modes:
            0 = Bass only
            1 = LowMid only
            2 = HighMid only
            3 = Treble only
            4 = Combined (all 4 bands overlaid & color–coded)
        Notes:
            - Uses AudioLink texture rows 0-3 for 4-band amplitudes
            - Uses a single waveform row (default 6) for the raw audio shape
            - Per-band amplitude modulates the vertical scale of that base shape
            - Each band has an independent color
    *****************************************************************/
    Properties
    {
        // Core Colors
        _LineColor      ("Single Mode Line Color (modes 0-3)", Color) = (0.10,0.55,1.0,1)

        // Band Colors (Combined mode)
        _ColorBass      ("Bass Color", Color) = (1,.20,0,1)
        _ColorLowMid    ("LowMid Color", Color) = (1,1,0,1)
        _ColorHighMid   ("HighMid Color", Color) = (0.14,1,0,1)
        _ColorTreble    ("Treble Color", Color) = (0,0,1,1)

        // Waveform + Dynamics
        _Gain           ("Waveform Gain", Float) = .85
        _AmplitudeScale ("Global Amplitude Scale", Float) = .36
        _BandAmplitudeBoost ("Band Amplitude Boost", Float) = 1.69
        _BaselineFactor ("Baseline Factor (min scale)", Range(0,1)) = 0.635
        _YOffset        ("Vertical Offset (-1..1)", Range(-1,1)) = 0

        // Scroll
        _ScrollSpeed    ("Scroll Speed (px/sec)", Float) = 0
        _LatencyPixels  ("Visual Delay (px)", Float) = 0
        _Direction      ("Direction (-1 left, 1 right)", Float) = -1

        // Style
        _LineThickness  ("Line Thickness (0..1)", Range(0,1)) = 0.6
        _Glow           ("Glow Intensity", Float) = 1.5
        _OverlapBlend   ("Combined Mode Overlap Blend", Range(0,2)) = 2

        // Playhead
        _PlayheadWidth      ("Playhead Width (px)", Range(0,8)) = 1.35
        _PlayheadBrightness ("Playhead Brightness", Float) = 2.0
        _PlayheadColor      ("Playhead Color", Color) = (.8,.8,.8,1)

        // Mode (0..4)
        _Mode           ("Mode 0=Bass 1=LowMid 2=HighMid 3=Treble 4=Combined", Int) = 4

        // Waveform row in AudioLink
        _WaveformRow    ("Waveform Row", Int) = 0

        // PBR Surface params
        _Metallic       ("Metallic", Range(0,1)) = 1
        _Smoothness     ("Smoothness", Range(0,1)) = .25
        _EmissionBoost  ("Emission Boost", Float) = .5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _AudioTexture;

        fixed4 _LineColor;
        fixed4 _ColorBass;
        fixed4 _ColorLowMid;
        fixed4 _ColorHighMid;
        fixed4 _ColorTreble;
        fixed4 _PlayheadColor;

        float _Gain;
        float _AmplitudeScale;
        float _BandAmplitudeBoost;
        float _BaselineFactor;
        float _YOffset;

        float _ScrollSpeed;
        float _LatencyPixels;
        float _Direction;   // 1 = left, -1 = right (per user spec)

        float _LineThickness;
        float _Glow;
        float _OverlapBlend;

        float _PlayheadWidth;
        float _PlayheadBrightness;

        int   _Mode;
        int   _WaveformRow;

        float _Metallic;
        float _Smoothness;
        float _EmissionBoost;

        // AudioLink assumed texture size
        #define AL_WIDTH 128.0
        #define AL_HEIGHT 64.0

        struct Input { float2 uv_MainTex; };

        float2 PixelToUV(float2 p) { return (p + 0.5) / float2(AL_WIDTH, AL_HEIGHT); }
        float4 SampleAudioPixel(float2 p) { return tex2D(_AudioTexture, PixelToUV(p)); }

        float2 GetWaveformLR(float column)
        {
            column = fmod(fmod(column, AL_WIDTH) + AL_WIDTH, AL_WIDTH);
            float4 wf = SampleAudioPixel(float2(column, _WaveformRow));
            return wf.rg;
        }

        float GetBandAmp(int band, float column)
        {
            column = fmod(fmod(column, AL_WIDTH) + AL_WIDTH, AL_WIDTH);
            float4 px = SampleAudioPixel(float2(column, band));
            return px.r;
        }

        void BuildBandWave(
            float y, float sampleColumn,
            float baseWave, float bandAmp,
            float3 color,
            inout float3 accumColor,
            inout float accumEmission)
        {
            float scale = _BaselineFactor + bandAmp * _BandAmplitudeBoost;
            float amp = baseWave * scale * _AmplitudeScale * _Gain;
            amp = clamp(amp, -1.0, 1.0);

            float center = 0.5 + _YOffset * 0.5;
            float halfExtent = abs(amp) * 0.5;

            float thickness = max(1e-4, _LineThickness);
            float dLine = abs(y - center) / max(halfExtent, 1e-5);

            float lineMask = 1 - smoothstep(thickness, thickness*2, dLine);
            float outline = 1 - smoothstep(thickness*2, thickness*4, dLine);
            float glow = exp(-dLine * 6) * _Glow;

            float bandMask = saturate(lineMask + outline*0.5 + glow*0.1);
            accumColor += color * bandMask * _OverlapBlend;
            accumEmission += (lineMask + glow) * 0.5;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = saturate(IN.uv_MainTex);

            // Black background
            float3 finalRGB = 0;

            // Scroll logic with new direction semantics
            // _Direction: 1 -> left, -1 -> right
            float internalDir = -_Direction; // convert to previous internal meaning
            float head = _Time.y * _ScrollSpeed;
            float sampleColumn = head - internalDir * (uv.x * AL_WIDTH) + _LatencyPixels;

            float2 lr = GetWaveformLR(sampleColumn);
            float wave = (lr.x + lr.y) * 0.5;
            float signedWave = (wave * 2.0 - 1.0);

            float y = uv.y;
            float accumEmission = 0.0;
            float3 accumBands = 0;

            if (_Mode >= 0 && _Mode <= 3)
            {
                float bandAmp = GetBandAmp(_Mode, sampleColumn);
                BuildBandWave(y, sampleColumn, signedWave, bandAmp, _LineColor.rgb, accumBands, accumEmission);
            }
            else
            {
                float bassAmp    = GetBandAmp(0, sampleColumn);
                float lowMidAmp  = GetBandAmp(1, sampleColumn);
                float highMidAmp = GetBandAmp(2, sampleColumn);
                float trebleAmp  = GetBandAmp(3, sampleColumn);

                BuildBandWave(y, sampleColumn, signedWave, bassAmp,    _ColorBass.rgb,    accumBands, accumEmission);
                BuildBandWave(y, sampleColumn, signedWave, lowMidAmp,  _ColorLowMid.rgb,  accumBands, accumEmission);
                BuildBandWave(y, sampleColumn, signedWave, highMidAmp, _ColorHighMid.rgb, accumBands, accumEmission);
                BuildBandWave(y, sampleColumn, signedWave, trebleAmp,  _ColorTreble.rgb,  accumBands, accumEmission);
            }

            finalRGB += saturate(accumBands);

            // Playhead
            float playheadDist = abs(uv.x - 0.5) * AL_WIDTH;
            float ph = 1 - smoothstep(_PlayheadWidth, _PlayheadWidth + 1.0, playheadDist);
            finalRGB = lerp(finalRGB, _PlayheadColor.rgb * _PlayheadBrightness, ph);

            o.Albedo = finalRGB;
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;

            float emissionMask = saturate(accumEmission + ph);
            o.Emission = finalRGB * emissionMask * _EmissionBoost;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}