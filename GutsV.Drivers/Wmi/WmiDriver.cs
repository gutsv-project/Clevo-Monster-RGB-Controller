using System.Management;
using System.Runtime.Versioning;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Drivers.Wmi;

[SupportedOSPlatform("windows")]
public class WmiDriver : IKeyboardController
{
    private ManagementObject? _wmiInstance;
    private const string WmiNamespace = @"root\WMI";
    private const string InstanceQuery = @"SELECT * FROM CLEVO_GET WHERE InstanceName = 'ACPI\PNP0C14\0_0'";
    private const string MethodName = "SetKBLED";

    private static readonly Zone[] ThreeZones = { Zone.Left, Zone.Center, Zone.Right };

    public IReadOnlyList<Zone> Zones => ThreeZones;
    public KeyboardFeatures Features => KeyboardFeatures.HardwareEffects | KeyboardFeatures.HardwareBrightness;

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            if (_wmiInstance != null) return; // already initialized

            using var searcher = new ManagementObjectSearcher(WmiNamespace, InstanceQuery);
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                _wmiInstance = obj;
                break;
            }

            // Throwing allows DeviceManager to fall back to Insyde or other methods.
            if (_wmiInstance == null)
                throw new PlatformNotSupportedException("Clevo WMI device not found.");
        });
    }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        if (_wmiInstance == null) return Task.CompletedTask;

        return Task.Run(() =>
        {
            if (zone == Zone.All)
            {
                foreach (var z in ThreeZones) SendWord(ClevoLed.ColorWord(z, color));
            }
            else if (Array.IndexOf(ThreeZones, zone) >= 0)
            {
                SendWord(ClevoLed.ColorWord(zone, color));
            }
        });
    }

    public Task SetHardwareEffectAsync(HardwareEffect effect) =>
        _wmiInstance == null ? Task.CompletedTask : Task.Run(() => SendWord(ClevoLed.EffectWord(effect)));

    public Task SetHardwareBrightnessAsync(int level) =>
        _wmiInstance == null ? Task.CompletedTask : Task.Run(() => SendWord(ClevoLed.WmiBrightnessWord(level)));

    private void SendWord(uint packet)
    {
        try
        {
            // Protocol: [Green, Red, Blue, Command] little endian, see ClevoLed
            ManagementBaseObject inParams = _wmiInstance!.GetMethodParameters(MethodName);
            inParams["Data"] = packet;
            _wmiInstance.InvokeMethod(MethodName, inParams, null);
        }
        catch (Exception)
        {
            // WMI can be flaky under load.
            // In a loop, one missed frame is acceptable.
        }
    }

    public Task ShutdownAsync()
    {
        _wmiInstance?.Dispose();
        _wmiInstance = null;
        return Task.CompletedTask;
    }
}
