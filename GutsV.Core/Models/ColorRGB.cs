using System.Runtime.InteropServices;

namespace GutsV.Core.Models;

[StructLayout(LayoutKind.Sequential)]
public record struct ColorRGB(byte R, byte G, byte B)
{
    public static readonly ColorRGB Black = new(0, 0, 0);
    public static readonly ColorRGB White = new(255, 255, 255);
    public static readonly ColorRGB Red = new(255, 0, 0);
    public static readonly ColorRGB Green = new(0, 255, 0);
    public static readonly ColorRGB Blue = new(0, 0, 255);



    // Fast brightness adjustment
    public ColorRGB ApplyBrightness(float brightness)
    {
        if (brightness >= 1.0f) return this;
        if (brightness <= 0.0f) return Black;

        return new ColorRGB(
            (byte)(R * brightness),
            (byte)(G * brightness),
            (byte)(B * brightness)
        );
    }
}
