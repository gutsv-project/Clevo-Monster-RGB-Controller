using System;
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

        if (mode == 1 || mode == 0) // Try Insyde
        {
             var insydeDriver = new InsydeDriver();
            try
            {
                await insydeDriver.InitializeAsync();
                 return insydeDriver;
            }
            catch (Exception) { if (mode == 1) throw; }
        }


        // 3. Fallback / Mock for Development without Hardware
        // In production this should throw or return null.
        // Returning null allows UI to start in "Preview Mode"
        return null; 
    }
}
