using System;
using System.Collections.Generic;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public class ColorShiftAnimation : IAnimation
{
    public string Name => "Wave";
    private const int UpdateRateMs = 100;
    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        // Simple shift logic: Red, Green, Blue on different zones shifting
        // Frame 1
        yield return new AnimationFrame(new ColorRGB(255, 0, 0), 0, Zone.Left);
        yield return new AnimationFrame(new ColorRGB(0, 255, 0), 0, Zone.Center);
        yield return new AnimationFrame(new ColorRGB(0, 0, 255), 500, Zone.Right);

        // Frame 2
        yield return new AnimationFrame(new ColorRGB(0, 0, 255), 0, Zone.Left);
        yield return new AnimationFrame(new ColorRGB(255, 0, 0), 0, Zone.Center);
        yield return new AnimationFrame(new ColorRGB(0, 255, 0), 500, Zone.Right);

        // Frame 3
        yield return new AnimationFrame(new ColorRGB(0, 255, 0), 0, Zone.Left);
        yield return new AnimationFrame(new ColorRGB(0, 0, 255), 0, Zone.Center);
        yield return new AnimationFrame(new ColorRGB(255, 0, 0), 500, Zone.Right);
    }
}
