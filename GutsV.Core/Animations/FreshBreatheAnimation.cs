using System;
using System.Collections.Generic;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class FreshBreatheAnimation : IAnimation
{
    public string Name => "Neon Pulse";
    private const int DurationMs = 1500;
    private const int FrameDuration = 25;

    private readonly ColorRGB[] _colors = new[]
    {
        new ColorRGB(255, 255, 0),   // Yellow
        new ColorRGB(255, 0, 0),     // Red
        new ColorRGB(255, 0, 255),   // Purple
        new ColorRGB(0, 255, 0),     // Green
        new ColorRGB(255, 255, 255), // White
        new ColorRGB(0, 0, 255),     // Blue
        new ColorRGB(255, 120, 120)  // Brownish
    };

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        // Calculate steps dynamically
        int steps = DurationMs / FrameDuration;

        foreach (var color in _colors)
        {
            for (int i = 0; i < steps; i++)
            {
                double angle = (180.0 / steps) * i;
                double factor = Math.Sin(angle * (Math.PI / 180.0));

                byte r = (byte)(color.R * factor);
                byte g = (byte)(color.G * factor);
                byte b = (byte)(color.B * factor);

                yield return new AnimationFrame(new ColorRGB(r, g, b), FrameDuration);
            }
        }
    }
}
