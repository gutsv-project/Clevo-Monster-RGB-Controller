using System;
using System.Management;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using GutsV.Core.Interfaces;
using GutsV.Drivers.Insyde;
using GutsV.Drivers.Wmi;

namespace GutsV.Drivers;

[SupportedOSPlatform("windows")]
public static class DeviceManager
{
    public static async Task<IKeyboardController?> CreateDriverAsync(int mode = 0)
    {
        // Mode 0: Auto
        // Mode 1: Insyde
        // Mode 2: WMI

        // Acer (Nitro on Clevo boards) only exposes the Insyde DCHU path; try it first there.
        bool insydeFirst = mode == 0 && PreferInsyde(GetManufacturer());

        if (insydeFirst)
        {
            var driver = await TryInsydeAsync(rethrow: false);
            if (driver != null) return driver;
        }

        if (mode == 2 || mode == 0) // Try WMI
        {
            var wmiDriver = new WmiDriver();
            try
            {
                await wmiDriver.InitializeAsync();
                return wmiDriver;
            }
            catch (PlatformNotSupportedException) { if (mode == 2) throw; }
            catch (Exception) { if (mode == 2) throw; }
        }

        if ((mode == 1 || mode == 0) && !insydeFirst) // Try Insyde
        {
            var driver = await TryInsydeAsync(rethrow: mode == 1);
            if (driver != null) return driver;
        }


        // 3. Fallback / Mock for Development without Hardware
        // In production this should throw or return null.
        // Returning null allows UI to start in "Preview Mode"
        return null;
    }

    private static async Task<IKeyboardController?> TryInsydeAsync(bool rethrow)
    {
        var insydeDriver = new InsydeDriver();
        try
        {
            await insydeDriver.InitializeAsync();
            return insydeDriver;
        }
        catch (Exception) { if (rethrow) throw; }
        return null;
    }

    public static bool PreferInsyde(string? manufacturer) =>
        manufacturer != null && manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase);

    public static string? GetManufacturer()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
                return obj["Manufacturer"]?.ToString();
        }
        catch { }
        return null;
    }
}
