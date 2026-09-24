using System;
using System.Collections.Generic;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class BreatheAnimation : IAnimation
{
    public string Name => "Breathing";
    private const int DurationMs = 3000;
    private const int FrameDuration = 25;

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        // To maintain original behavior, calculate steps based on DurationMs and FrameDuration
        // Assuming the original animation duration was Steps * FrameDuration = 40 * 25 = 1000ms
        // Now, with DurationMs = 3000ms, and FrameDuration = 25ms, Steps should be 3000 / 25 = 120
        int steps = DurationMs / FrameDuration;

        // Phase 1: Red
        foreach (var frame in GeneratePhase(255, 0, 0, steps)) yield return frame;
        
        // Phase 2: Green
        foreach (var frame in GeneratePhase(0, 255, 0, steps)) yield return frame;

        // Phase 3: Blue
        foreach (var frame in GeneratePhase(0, 0, 255, steps)) yield return frame;
    }

    private IEnumerable<AnimationFrame> GeneratePhase(byte rTarget, byte gTarget, byte bTarget, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            double angle = (180.0 / steps) * i;
            double factor = Math.Sin(angle * (Math.PI / 180.0));

            byte r = (byte)(rTarget * factor);
            byte g = (byte)(gTarget * factor);
            byte b = (byte)(bTarget * factor);

            yield return new AnimationFrame(new ColorRGB(r, g, b), FrameDuration);
        }
    }
}
