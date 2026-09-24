using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GutsV.Core.Models;

namespace GutsV.Core.Animations;

[SupportedOSPlatform("windows")]
public class SystemMonitorAnimation : IAnimation, IDisposable
{
    public string Name => "System Monitor (Heatmap)";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    private const int UpdateRateMs = 60; // Smooth 16 FPS LED refresh rate
    private const double SmoothingTau = 0.35; // 350ms smooth transition constant

    private long _prevIdleTime = 0;
    private long _prevKernelTime = 0;
    private long _prevUserTime = 0;

    private ManagementObjectSearcher? _thermalSearcher;
    private double _lastTempCelsius = 45.0;
    private bool _hasThermalSensor = false;
    private Stopwatch? _tempPollStopwatch;

    private double _currentRatio = 0.0;
    private bool _disposed = false;

    public SystemMonitorAnimation()
    {
        // Initialize CPU Times
        GetSystemTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);

        // Initialize WMI Thermal Searcher
        try
        {
            _thermalSearcher = new ManagementObjectSearcher(
                @"root\CIMV2", 
                "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            
            // Initial poll to verify if hardware thermal sensor is present
            double initialTemp = PollThermalZone();
            if (initialTemp > 20.0 && initialTemp < 115.0)
            {
                _hasThermalSensor = true;
                _lastTempCelsius = initialTemp;
            }
        }
        catch
        {
            _hasThermalSensor = false;
        }

        _tempPollStopwatch = Stopwatch.StartNew();
    }

    private double PollThermalZone()
    {
        if (_thermalSearcher == null) return -1;

        try
        {
            foreach (ManagementObject obj in _thermalSearcher.Get())
            {
                if (obj["Temperature"] != null)
                {
                    double raw = Convert.ToDouble(obj["Temperature"]);
                    if (raw > 2000)
                    {
                        // Tenths of Kelvin (decikelvin)
                        return (raw / 10.0) - 273.15;
                    }
                    else if (raw > 200)
                    {
                        // Kelvin
                        return raw - 273.15;
                    }
                    else if (raw > 0)
                    {
                        // Direct Celsius
                        return raw;
                    }
                }
            }
        }
        catch
        {
            // Transient WMI query error
        }

        return -1;
    }

    private double MeasureCpuUsage()
    {
        if (!GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            return 0.0;

        long usr = userTime - _prevUserTime;
        long ker = kernelTime - _prevKernelTime;
        long idl = idleTime - _prevIdleTime;

        _prevUserTime = userTime;
        _prevKernelTime = kernelTime;
        _prevIdleTime = idleTime;

        long sys = ker + usr;
        if (sys <= 0) return 0.0;

        double cpu = ((double)(sys - idl) / sys) * 100.0;
        return Math.Clamp(cpu, 0.0, 100.0);
    }

    public IEnumerable<AnimationFrame> GenerateFrames()
    {
        var frameSw = Stopwatch.StartNew();

        while (!_disposed)
        {
            double dt = frameSw.Elapsed.TotalSeconds;
            frameSw.Restart();
            if (dt <= 0) dt = 0.001;
            if (dt > 0.5) dt = 0.5;

            // 1. Poll Temperature periodically (every 500ms) to reduce WMI overhead
            if (_hasThermalSensor && _tempPollStopwatch != null && _tempPollStopwatch.ElapsedMilliseconds >= 500)
            {
                _tempPollStopwatch.Restart();
                double temp = PollThermalZone();
                if (temp > 20.0 && temp < 115.0)
                {
                    _lastTempCelsius = temp;
                }
            }

            // 2. Measure instantaneous CPU load via kernel32 GetSystemTimes (extremely fast & lightweight)
            double cpuPercent = MeasureCpuUsage();
            double cpuRatio = Math.Clamp(cpuPercent / 100.0, 0.0, 1.0);

            // 3. Calculate target heat ratio (0.0 = Cold/Idle -> 1.0 = Critical Hot)
            double targetRatio;

            if (_hasThermalSensor)
            {
                // Temperature range: 40°C (cool) to 85°C (hot)
                double tempRatio = Math.Clamp((_lastTempCelsius - 40.0) / (85.0 - 40.0), 0.0, 1.0);

                // Blend: User feels immediate feedback on CPU spikes, while real temperature maintains the baseline heat glow
                targetRatio = Math.Max(tempRatio, cpuRatio * 0.95);
            }
            else
            {
                // Fallback for systems without accessible thermal ACPI zones: direct responsive CPU load
                targetRatio = cpuRatio;
            }

            // 4. Smooth interpolation (EMA filter) so transitions are fluid and cinematic
            double alpha = 1.0 - Math.Exp(-dt / SmoothingTau);
            _currentRatio += (targetRatio - _currentRatio) * alpha;

            // 5. Sample the rich 5-stop continuous heatmap gradient
            ColorRGB color = GetHeatmapColor(_currentRatio);

            yield return new AnimationFrame(color, UpdateRateMs, Zone.All);
        }
    }

    private static ColorRGB GetHeatmapColor(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);

        // 5-Stop Gradient:
        // 0.00: Glacier Cyan   (0, 212, 255)  - Cool idle (~40°C or <10% CPU)
        // 0.25: Vivid Green    (0, 255, 80)   - Light usage (~51°C or 25% CPU)
        // 0.50: Bright Yellow  (255, 225, 0)  - Moderate load (~63°C or 50% CPU)
        // 0.75: Amber Orange   (255, 110, 0)  - Heavy load (~74°C or 75% CPU)
        // 1.00: Crimson Red    (255, 0, 0)    - Thermal throttle / Max load (85°C+ / 100% CPU)

        var c0 = new ColorRGB(0, 212, 255);
        var c1 = new ColorRGB(0, 255, 80);
        var c2 = new ColorRGB(255, 225, 0);
        var c3 = new ColorRGB(255, 110, 0);
        var c4 = new ColorRGB(255, 0, 0);

        if (t <= 0.25)
        {
            double sub = t / 0.25;
            return Lerp(c0, c1, sub);
        }
        else if (t <= 0.50)
        {
            double sub = (t - 0.25) / 0.25;
            return Lerp(c1, c2, sub);
        }
        else if (t <= 0.75)
        {
            double sub = (t - 0.50) / 0.25;
            return Lerp(c2, c3, sub);
        }
        else
        {
            double sub = (t - 0.75) / 0.25;
            return Lerp(c3, c4, sub);
        }
    }

    private static ColorRGB Lerp(ColorRGB a, ColorRGB b, double t)
    {
        byte r = (byte)Math.Clamp(a.R + (b.R - a.R) * t, 0, 255);
        byte g = (byte)Math.Clamp(a.G + (b.G - a.G) * t, 0, 255);
        byte bl = (byte)Math.Clamp(a.B + (b.B - a.B) * t, 0, 255);
        return new ColorRGB(r, g, bl);
    }

    public void Dispose()
    {
        _disposed = true;
        _thermalSearcher?.Dispose();
        _thermalSearcher = null;
    }
}
