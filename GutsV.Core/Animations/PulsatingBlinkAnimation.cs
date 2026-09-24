using System;
using System.Collections.Generic;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class PulsatingBlinkAnimation : IAnimation
{
    public string Name => "Strobe Blink";
    private const int BlinkTimeMs = 200;
    private const int OffTimeMs = 1000;

    private readonly ColorRGB[] _colors = new[]
    {
        new ColorRGB(135, 206, 235), // Sky Blue
        new ColorRGB(34, 139, 34),   // Forest Green
        new ColorRGB(255, 69, 0),    // Sunset Orange
        new ColorRGB(255, 102, 204)  // Rose Pink
    };

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        var black = new ColorRGB(0, 0, 0);

        foreach (var color in _colors)
        {
            // Off
            yield return new AnimationFrame(black, OffTimeMs, Zone.All);
            // On
            yield return new AnimationFrame(color, BlinkTimeMs, Zone.All);
            // Off
            yield return new AnimationFrame(black, OffTimeMs, Zone.All);
        }
    }
}
