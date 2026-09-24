using GutsV.Core.Models;

namespace GutsV.Core.Animations;

public struct AnimationFrame
{
    public ColorRGB Color { get; }
    public int DurationMs { get; }
    public Zone TargetZone { get; }
    
    public AnimationFrame(ColorRGB color, int durationMs, Zone zone = Zone.All)
    {
        Color = color;
        DurationMs = durationMs;
        TargetZone = zone;
    }
}
