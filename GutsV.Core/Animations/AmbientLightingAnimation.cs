using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

[SupportedOSPlatform("windows")]
public class AmbientLightingAnimation : IAnimation, IDisposable
{
    public string Name => "Ambient (Screen Sync)";

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // Sample step: Samples every 6th pixel in both X and Y dimensions
    private const int SampleStep = 6;
    private const double Tau = 0.15;     // 150ms smoothing time constant (EMA filter)
    private const int UpdateRateMs = 16; // ~60 FPS target rate

    private Bitmap? _bitmap;
    private Graphics? _graphics;
    private int _screenWidth;
    private int _screenHeight;

    private double _smoothR = 0;
    private double _smoothG = 0;
    private double _smoothB = 0;
    private bool _disposed = false;

    public AmbientLightingAnimation()
    {
        InitializeCaptureSurface();
    }

    private void InitializeCaptureSurface()
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        if (screenWidth <= 0) screenWidth = 1920;
        if (screenHeight <= 0) screenHeight = 1080;

        _screenWidth = screenWidth;
        _screenHeight = screenHeight;

        _graphics?.Dispose();
        _bitmap?.Dispose();

        // Capture full 100% screen surface
        _bitmap = new Bitmap(_screenWidth, _screenHeight, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
    }

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        var sw = Stopwatch.StartNew();

        while (!_disposed)
        {
            double dt = sw.Elapsed.TotalSeconds;
            sw.Restart();
            if (dt <= 0) dt = 0.001;
            if (dt > 0.2) dt = 0.2;

            // Ensure capture surface matches current screen resolution
            int currentScreenWidth = GetSystemMetrics(SM_CXSCREEN);
            int currentScreenHeight = GetSystemMetrics(SM_CYSCREEN);
            if (currentScreenWidth != _screenWidth || currentScreenHeight != _screenHeight || _bitmap == null || _graphics == null)
            {
                InitializeCaptureSurface();
            }

            int targetR = 0, targetG = 0, targetB = 0;

            if (_bitmap != null && _graphics != null)
            {
                try
                {
                    // Copy full screen 100% surface
                    _graphics.CopyFromScreen(0, 0, 0, 0, new Size(_screenWidth, _screenHeight), CopyPixelOperation.SourceCopy);

                    BitmapData data = _bitmap.LockBits(
                        new Rectangle(0, 0, _screenWidth, _screenHeight),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

                    double weightedSumR = 0, weightedSumG = 0, weightedSumB = 0;
                    double totalWeight = 0;
                    long totalLuminance = 0;
                    long sampleCount = 0;

                    try
                    {
                        unsafe
                        {
                            byte* scan0 = (byte*)data.Scan0;
                            int stride = data.Stride;

                            for (int y = 0; y < _screenHeight; y += SampleStep)
                            {
                                byte* row = scan0 + (y * stride);
                                for (int x = 0; x < _screenWidth; x += SampleStep)
                                {
                                    int idx = x * 4;
                                    byte b = row[idx];
                                    byte g = row[idx + 1];
                                    byte r = row[idx + 2];

                                    int max = Math.Max(r, Math.Max(g, b));
                                    int min = Math.Min(r, Math.Min(g, b));
                                    int chroma = max - min; // 0 (pure gray/white/black) to 255 (pure vivid color)

                                    totalLuminance += max;
                                    sampleCount++;

                                    // Chroma Weighting:
                                    // Saturated, colorful pixels get exponentially higher weight so they dominate
                                    // over white backgrounds, white subtitles, gray borders, and text.
                                    double weight = 2.0 + (chroma * chroma) / 100.0;

                                    weightedSumR += r * weight;
                                    weightedSumG += g * weight;
                                    weightedSumB += b * weight;
                                    totalWeight += weight;
                                }
                            }
                        }
                    }
                    finally
                    {
                        _bitmap.UnlockBits(data);
                    }

                    if (sampleCount > 0 && totalWeight > 0)
                    {
                        double avgLuminance = (double)totalLuminance / sampleCount;

                        // Blackout detection: if the screen is practically black (e.g. dark scene / black bars)
                        if (avgLuminance < 12.0)
                        {
                            targetR = 0;
                            targetG = 0;
                            targetB = 0;
                        }
                        else
                        {
                            double rawR = weightedSumR / totalWeight;
                            double rawG = weightedSumG / totalWeight;
                            double rawB = weightedSumB / totalWeight;

                            // Convert to HSV for intelligent saturation and vibrance boost
                            RgbToHsv(rawR, rawG, rawB, out double h, out double s, out double v);

                            if (s >= 0.06)
                            {
                                // Boost saturation so the keyboard LEDs display deep, vibrant colors
                                // instead of washed-out, milky pastels
                                s = Math.Clamp(Math.Pow(s, 0.60) * 1.50, 0.0, 1.0);
                                v = Math.Clamp(v * 1.25, 0.35, 1.0); // Keep LED lively
                            }
                            else
                            {
                                // True black & white / document mode (e.g. Word, Notepad)
                                // Output a clean, soft neutral white
                                s = 0.0;
                                v = Math.Clamp(avgLuminance / 255.0, 0.20, 0.85);
                            }

                            HsvToRgb(h, s, v, out targetR, out targetG, out targetB);
                        }
                    }
                }
                catch
                {
                    // Ignore transient GDI errors during lockscreen/UAC transitions
                }
            }

            // Exponential Moving Average filter for cinematic, flicker-free transitions
            double alpha = 1.0 - Math.Exp(-dt / Tau);
            _smoothR += (targetR - _smoothR) * alpha;
            _smoothG += (targetG - _smoothG) * alpha;
            _smoothB += (targetB - _smoothB) * alpha;

            byte outR = (byte)Math.Clamp((int)Math.Round(_smoothR), 0, 255);
            byte outG = (byte)Math.Clamp((int)Math.Round(_smoothG), 0, 255);
            byte outB = (byte)Math.Clamp((int)Math.Round(_smoothB), 0, 255);

            yield return new AnimationFrame(new ColorRGB(outR, outG, outB), UpdateRateMs, Zone.All);
        }
    }

    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        r /= 255.0;
        g /= 255.0;
        b /= 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0)
        {
            if (max == r)
                h = (60.0 * ((g - b) / delta) + 360.0) % 360.0;
            else if (max == g)
                h = (60.0 * ((b - r) / delta) + 120.0) % 360.0;
            else
                h = (60.0 * ((r - g) / delta) + 240.0) % 360.0;
        }

        s = max <= 0 ? 0 : delta / max;
        v = max;
    }

    private static void HsvToRgb(double h, double s, double v, out int r, out int g, out int b)
    {
        if (s <= 0)
        {
            int val = (int)Math.Clamp(Math.Round(v * 255.0), 0, 255);
            r = val;
            g = val;
            b = val;
            return;
        }

        int hi = (int)Math.Floor(h / 60.0) % 6;
        double f = (h / 60.0) - Math.Floor(h / 60.0);

        double p = v * (1.0 - s);
        double q = v * (1.0 - f * s);
        double t = v * (1.0 - (1.0 - f) * s);

        double dR = 0, dG = 0, dB = 0;
        switch (hi)
        {
            case 0: dR = v; dG = t; dB = p; break;
            case 1: dR = q; dG = v; dB = p; break;
            case 2: dR = p; dG = v; dB = t; break;
            case 3: dR = p; dG = q; dB = v; break;
            case 4: dR = t; dG = p; dB = v; break;
            case 5: dR = v; dG = p; dB = q; break;
        }

        r = (int)Math.Clamp(Math.Round(dR * 255.0), 0, 255);
        g = (int)Math.Clamp(Math.Round(dG * 255.0), 0, 255);
        b = (int)Math.Clamp(Math.Round(dB * 255.0), 0, 255);
    }

    public void Dispose()
    {
        _disposed = true;
        _graphics?.Dispose();
        _bitmap?.Dispose();
        _graphics = null;
        _bitmap = null;
    }
}
