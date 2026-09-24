using System;
using System.Collections.Generic;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class ColorTransformAnimation : IAnimation
{
    public string Name => "Spectrum Cycle";
    private const int StepDuration = 50;
    private const int FrameDuration = 25;

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        var red = new ColorRGB(255, 0, 0);
        var green = new ColorRGB(0, 255, 0);
        var blue = new ColorRGB(0, 0, 255);

        // Steps = Duration / FrameDuration but here StepDuration is used?
        // Let's assume StepDuration means Number of Steps in original code or Duration of transition?
        // Original code used const int Steps = 40; with FrameDuration = 25 -> 1000ms transition
        // Let's fix Steps to 40 directly or calculate it
        int steps = 40; 

        foreach (var frame in GenerateTransition(red, green, steps)) yield return frame;
        foreach (var frame in GenerateTransition(green, blue, steps)) yield return frame;
        foreach (var frame in GenerateTransition(blue, red, steps)) yield return frame;
    }

    private IEnumerable<AnimationFrame> GenerateTransition(ColorRGB start, ColorRGB end, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            
            byte r = (byte)(start.R + (end.R - start.R) * t);
            byte g = (byte)(start.G + (end.G - start.G) * t);
            byte b = (byte)(start.B + (end.B - start.B) * t);

            yield return new AnimationFrame(new ColorRGB(r, g, b), FrameDuration);
        }
    }
}
