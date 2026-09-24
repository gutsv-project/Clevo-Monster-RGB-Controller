using System;
using System.Collections.Generic;
using GutsV.Core.Audio;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class AudioSyncAnimation : IAnimation, IDisposable
{
    public string Name => "Audio Sync (Spectrum)";
    private readonly AudioAnalyzer _analyzer;
    private const int UpdateRateMs = 30; // ~30fps
    
    // Automatic Gain Control (AGC) state
    private float _maxObservedLevel = 0.1f;
    private const float DecayRate = 0.995f; // Slow decay for max level

    // Hue rotation state
    private float _currentHue = 0f;

    public AudioSyncAnimation()
    {
        _analyzer = new AudioAnalyzer();
        _analyzer.StartListening();
    }

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        while (true)
        {
            if (!_analyzer.IsListening) _analyzer.StartListening();
            
            float rawLevel = _analyzer.BassLevel; 
            
            // AGC Logic: Track peak to normalize volume
            if (rawLevel > _maxObservedLevel) _maxObservedLevel = rawLevel;
            else _maxObservedLevel *= DecayRate;
            
            // Prevent division by zero
            if (_maxObservedLevel < 0.05f) _maxObservedLevel = 0.05f;

            // Normalized intensity (0.0 to 1.0)
            float intensity = Math.Min(1.0f, rawLevel / _maxObservedLevel);
            
            // Curve the intensity for better visual punch (gamma correction-ish)
            intensity = intensity * intensity; 

            // Color Logic:
            // Rotate Hue slowly over time
            _currentHue += 1.0f; // Speed of rotation
            if (_currentHue >= 360f) _currentHue = 0f;

            // Shift hue based on intensity properly (Bass kicks change color slightly)
            float dynamicHue = (_currentHue + (intensity * 60f)) % 360f;
            
            // Saturation always high
            // Value (Brightness) depends on intensity but min 0.1 to not go fully black unless silence
            float value = Math.Max(0.1f, intensity);

            var color = ColorFromHSV(dynamicHue, 1.0f, value);
            
            yield return new AnimationFrame(color, UpdateRateMs, Zone.All);
        }
    }

    // Helper: HSV to RGB
    private static ColorRGB ColorFromHSV(float hue, float saturation, float value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        int v = Convert.ToInt32(value);
        int p = Convert.ToInt32(value * (1 - saturation));
        int q = Convert.ToInt32(value * (1 - f * saturation));
        int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

        if (hi == 0) return new ColorRGB((byte)v, (byte)t, (byte)p);
        else if (hi == 1) return new ColorRGB((byte)q, (byte)v, (byte)p);
        else if (hi == 2) return new ColorRGB((byte)p, (byte)v, (byte)t);
        else if (hi == 3) return new ColorRGB((byte)p, (byte)q, (byte)v);
        else if (hi == 4) return new ColorRGB((byte)t, (byte)p, (byte)v);
        else return new ColorRGB((byte)v, (byte)p, (byte)q);
    }

    public void Dispose()
    {
        _analyzer.Dispose();
    }
}
