using System.Collections.Generic;
using System.Threading.Tasks;
using GutsV.Core.Models;

namespace GutsV.Core.Interfaces;

public interface IKeyboardController
{
    Task InitializeAsync();
    Task SetColorAsync(Zone zone, ColorRGB color);
    Task SetModeAsync(int mode) => Task.CompletedTask;
    Task SetBrightnessAsync(byte brightness) => Task.CompletedTask; // Default to 100%
    Task ShutdownAsync();

    // Individually addressable zones (Zone.All is always accepted by SetColorAsync).
    IReadOnlyList<Zone> Zones => new[] { Zone.All };
    KeyboardFeatures Features => KeyboardFeatures.None;
    Task SetHardwareEffectAsync(HardwareEffect effect) => Task.CompletedTask;
    // level 0 (off) .. 4 (max)
    Task SetHardwareBrightnessAsync(int level) => Task.CompletedTask;
    Task SetBootEffectAsync(bool enabled) => Task.CompletedTask;
    // null = timer off; otherwise seconds (5 .. 65535)
    Task SetSleepTimerAsync(int? seconds) => Task.CompletedTask;
    // Persist the current static state where the vendor tools / Fn hotkeys read it (no-op if not applicable)
    Task SaveStateAsync() => Task.CompletedTask;
    // What the firmware / Control Center currently has stored (unknown = nulls)
    FirmwareOptions StoredOptions => new(null, null, null);
}
