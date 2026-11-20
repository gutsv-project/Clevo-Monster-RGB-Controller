using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GutsV.Core.Drivers;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Drivers.Insyde;

/// <summary>
/// Insyde DCHU path (Monster, Clevo DCHU, Acer Nitro on Clevo boards).
/// The keyboard type is read from firmware exactly like Acer Control Center does
/// (feedback/PROTOCOL.md, feedback/PROTOCOL_ZONE4.md) and selects the protocol:
///   KBType 7 / 23        -> packet 44 via SetDCHU_DataEx(4)   (4 sections, light bar, logo)
///   KBType 2 / 6 / 22/38 -> 32-bit words via SetDCHU_Data(103) (3 parts, light bar, logo)
///   KBType 1             -> white backlight (brightness via command 39)
///   anything else / detection failure -> the original GutsV single-zone behavior
/// </summary>
public class InsydeDriver : IKeyboardController
{
    private const string DllName = "InsydeDCHU.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDCHU_Data")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDCHU_DataEx")]
    private static extern int SetDCHU_DataEx(int command, byte[] buffer, int length, byte[] output);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WriteAppSettings")]
    private static extern int WriteAppSettings(int page, int command, int length, byte[] buffer);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReadAppSettings")]
    private static extern int ReadAppSettings(int page, int offset, int length, byte[] buffer);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDCHU_Data_Integer")]
    private static extern int GetDCHU_Data_Integer(int command, ref int value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDCHU_Data_Buffer")]
    private static extern int GetDCHU_Data_Buffer(int command, byte[] buffer);

    public enum LedProtocol { Legacy, White, Words, Packet44 }

    private volatile bool _isInitialized = false;
    private IReadOnlyList<Zone> _zones = new[] { Zone.All };
    private KeyboardFeatures _features = KeyboardFeatures.None;
    private readonly object _ledLock = new();

    // Keyboard type from firmware (buffer 13, byte 15), -1 = unknown
    public int KeyboardType { get; private set; } = -1;
    public LedProtocol Protocol { get; private set; } = LedProtocol.Legacy;

    public IReadOnlyList<Zone> Zones => _zones;
    public KeyboardFeatures Features => _features;
    public FirmwareOptions StoredOptions { get; private set; } = new(null, null, null);

    // Packet-44 state (the firmware always receives every zone at once)
    private readonly ColorRGB[] _packetZones = { ColorRGB.Red, ColorRGB.Green, ColorRGB.Blue, ColorRGB.Blue, ColorRGB.Blue, ColorRGB.Blue };
    private byte _packetMode = ClevoLed.Packet44StaticMode;
    private byte _packetSpeed = 3;
    private int _level = 4;
    private bool _levelKnown; // false: never send a brightness the user/firmware did not choose
    private bool _hasLightBar;
    private bool _lightBarOn = true;
    private bool _hasLogo;

    // Word-protocol state
    private readonly Dictionary<Zone, ColorRGB> _wordColors = new();
    private bool _flagsPending = true; // send the 0xE0 flags word after the next color write

    // App-settings store (driver keeps it in the registry); write at most once per interval, trailing
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(2);
    private readonly Stopwatch _sinceLastPersist = new();
    private ColorRGB? _persistedColor;
    private ColorRGB? _pendingPersistColor;
    private Timer? _persistTimer;
    private readonly object _persistLock = new();

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            string dllPath = Path.Combine(AppContext.BaseDirectory, DllName);
            if (!File.Exists(dllPath) && !File.Exists(DllName))
            {
                throw new FileNotFoundException("InsydeDCHU.dll not found.");
            }

            DetectCapabilities();
            _isInitialized = true;
        });
    }

    private void DetectCapabilities()
    {
        try
        {
            int warmup = 90;
            GetDCHU_Data_Integer(70, ref warmup);

            var info = new byte[256];
            for (int i = 0; i < 5; i++) // Acer retries while the firmware has not filled the type yet
            {
                GetDCHU_Data_Buffer(13, info);
                if (info[15] != 0) break;
                Thread.Sleep(100);
            }
            int type = info[15];
            if (type is 19 or 35 or 51 or 243) type = 3;
            KeyboardType = type;
            bool bootEffect = ClevoLed.SupportsBootEffect(info);

            switch (type)
            {
                case 7:
                case 23:
                    Protocol = LedProtocol.Packet44;
                    // KBType 23 always has the light bar (zone 5); sub 60 only says whether it is lit
                    _hasLightBar = type == 23;
                    _lightBarOn = type != 23 || ReadLightBarStatus() != 0;
                    _hasLogo = ReadLogoSupport();
                    LoadPacket44State(info);
                    var zones = new List<Zone> { Zone.Left, Zone.Center, Zone.Right, Zone.Zone4 };
                    if (_hasLightBar) zones.Add(Zone.LightBar);
                    if (_hasLogo) zones.Add(Zone.Logo);
                    _zones = zones;
                    _features = KeyboardFeatures.HardwareEffects | KeyboardFeatures.HardwareBrightness;
                    break;

                case 2:
                case 6:
                case 22:
                case 38:
                    Protocol = LedProtocol.Words;
                    int caps = 0;
                    GetDCHU_Data_Integer(122, ref caps);
                    var parts = new List<Zone> { Zone.Left, Zone.Center, Zone.Right };
                    if (((caps >> 12) & 1) != 0) parts.Add(Zone.LightBar);
                    if (((caps >> 18) & 1) != 0) { parts.Add(Zone.Logo); _hasLogo = true; }
                    _zones = parts;
                    _features = KeyboardFeatures.HardwareEffects | KeyboardFeatures.HardwareBrightness;
                    break;

                case 1:
                    Protocol = LedProtocol.White;
                    _zones = new[] { Zone.All };
                    _features = KeyboardFeatures.HardwareBrightness;
                    break;

                default:
                    // Per-key (3) and unknown types keep the original GutsV behavior
                    Protocol = LedProtocol.Legacy;
                    break;
            }

            // Command 121 works for every keyboard type Acer Control Center knows (1, 2, 3, 6, 7, 22, 23, 38)
            if (type is 1 or 2 or 3 or 6 or 7 or 22 or 23 or 38)
            {
                _features |= KeyboardFeatures.SleepTimer;
                // Per-key boards (3) also need perkey_api.dll SetLEDStatus(32, v), which GutsV does not ship
                if (bootEffect && type != 3) _features |= KeyboardFeatures.BootEffect;
                StoredOptions = ReadStoredOptions();
            }
        }
        catch
        {
            // Older DLL / firmware without these queries: keep legacy single-zone mode
            KeyboardType = -1;
            Protocol = LedProtocol.Legacy;
            _zones = new[] { Zone.All };
            _features = KeyboardFeatures.None;
        }
    }

    // Control Center's own store (page 2): 7 = boot effect, 36 = sleep timer on/off, 37 = h, m, s
    private static FirmwareOptions ReadStoredOptions()
    {
        try
        {
            var page = new byte[256];
            ReadAppSettings(2, 0, 256, page);
            int seconds = page[37] * 3600 + page[38] * 60 + page[39];
            return new FirmwareOptions(
                page[7] == 1,
                page[36] == 1,
                seconds >= ClevoLed.SleepTimerMinSeconds ? seconds : null);
        }
        catch { return new FirmwareOptions(null, null, null); }
    }

    private static int ReadLightBarStatus()
    {
        try
        {
            var req = new byte[256];
            var res = new byte[256];
            req[0] = 60;
            SetDCHU_DataEx(ClevoLed.DchuPackageCommand, req, 256, res);
            if (Array.TrueForAll(res, b => b == 0)) return 1; // empty reply = unknown -> Acer default (on)
            return res[0];
        }
        catch { return 1; } // Acer's default when the host does not report it
    }

    /// <summary>
    /// Start from the state Acer Control Center saved (page 2 of the app-settings store),
    /// falling back to the firmware default colors in WMI13 — so the first packet does not
    /// invent colors or force full brightness.
    /// </summary>
    private void LoadPacket44State(byte[] wmi13)
    {
        // Firmware defaults (WMI13): zones 1-3 at 2..10, zone 4 at 44, light bar 47, logo 51
        int[] defaults = { 2, 5, 8, 44, 47, 51 };
        for (int i = 0; i < 6; i++)
        {
            int o = defaults[i];
            var c = new ColorRGB(wmi13[o], wmi13[o + 1], wmi13[o + 2]);
            if (c != ColorRGB.Black) _packetZones[i] = c;
        }

        try
        {
            var page = new byte[256];
            ReadAppSettings(2, 0, 256, page);
            int[] saved = { 85, 89, 93, 10, 27, 13 };
            for (int i = 0; i < 6; i++)
            {
                int o = saved[i];
                var c = new ColorRGB(page[o], page[o + 1], page[o + 2]);
                if (c != ColorRGB.Black) _packetZones[i] = c;
            }
            if (page[34] is >= 1 and <= 3) _packetSpeed = page[34];
            if (page[35] is >= 1 and <= 4)
            {
                _level = page[80] == 0 ? 0 : page[35];
                _levelKnown = true;
            }
        }
        catch { }
    }

    // Acer CC40 defaults SupportLogoColor to true and the Nitro tools (built from real Control Center logs)
    // always send the logo bytes; only a readable BIOS feature list (version 01 00) that clears the bit says "no logo".
    private static bool ReadLogoSupport()
    {
        try
        {
            var list = new byte[256];
            ReadAppSettings(7, 0, 256, list); // BIOS feature list
            if (list[0] == 1 && list[1] == 0) return ((list[23] >> 5) & 1) != 0;
            return true;
        }
        catch { return true; }
    }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        if (!_isInitialized) return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    switch (Protocol)
                    {
                        case LedProtocol.Packet44:
                            if (zone == Zone.All)
                            {
                                foreach (var z in _zones) _packetZones[ClevoLed.Packet44Slot(z)] = color;
                            }
                            else
                            {
                                int slot = ClevoLed.Packet44Slot(zone);
                                if (slot < 0 || !Contains(zone)) return;
                                _packetZones[slot] = color;
                            }
                            _packetMode = ClevoLed.Packet44StaticMode;
                            SendPacket44();
                            break;

                        case LedProtocol.Words:
                            if (zone == Zone.All)
                                foreach (var z in _zones) SendZoneWord(z, color);
                            else if (Contains(zone))
                                SendZoneWord(zone, color);
                            else
                                return;
                            if (_flagsPending)
                            {
                                Send(ClevoLed.FlagsWord(_hasLogo));
                                _flagsPending = false;
                            }
                            if (zone == Zone.All || zone == Zone.Left) QueuePersist(color);
                            break;

                        default: // Legacy and White: exactly the original bytes
                            if (zone != Zone.All && zone != Zone.Left) return; // single zone maps to 'All'
                            Send(ClevoLed.ColorWord(Zone.Left, color)); // [G, R, B, 0xF0]
                            QueuePersist(color);
                            break;
                    }
                }
            }
            catch
            {
                // DLL call error
            }
        });
    }

    private void SendZoneWord(Zone zone, ColorRGB color)
    {
        _wordColors[zone] = color;
        Send(ClevoLed.ColorWord(zone, ClevoLed.VendorColorCorrection(KeyboardType, color)));
    }

    /// <summary>
    /// Minimum time between two packet-44 writes. The firmware appears to apply the zones left to right, with a
    /// new packet cutting the previous one short: at animation rates Section 4 lagged or kept an old color
    /// (Toib's Nitro video). Pending frames are coalesced by the queue meanwhile.
    /// shortcut: 100 ms is a guess from that video, tune via settings.json "Packet44MinGapMs" on hardware.
    /// </summary>
    public static int Packet44MinGapMs { get; set; } = 100;
    private readonly Stopwatch _sinceLastPacket = new();

    private void SendPacket44()
    {
        if (_sinceLastPacket.IsRunning)
        {
            int wait = Packet44MinGapMs - (int)_sinceLastPacket.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
        var packet = ClevoLed.BuildPacket44(_packetMode, _packetSpeed, _level, _packetZones, _hasLightBar, _hasLogo, _lightBarOn);
        SetDCHU_DataEx(ClevoLed.DchuPackageCommand, packet, 256, new byte[256]);
        _sinceLastPacket.Restart(); // gap counts from the end of the (blocking) firmware call
    }

    private bool Contains(Zone zone)
    {
        foreach (var z in _zones) if (z == zone) return true;
        return false;
    }

    /// <summary>
    /// Stores the current static state where FnKey / Control Center read it (page 2), so Fn+LED hotkeys
    /// restore GutsV's colors instead of old Control Center ones. Same keys as Acer CC40 and the Nitro tools:
    /// 80 on/off, 35 brightness, 34 speed, 32 mode (Control Center UI index), 85/89/93/10 zone RGB, 27 light bar, 13 logo, 8 app running.
    /// </summary>
    public Task SaveStateAsync()
    {
        if (!_isInitialized || Protocol != LedProtocol.Packet44) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    WriteAppSettings(2, 80, 1, new byte[] { (byte)(_level > 0 ? 1 : 0) });
                    if (_level > 0) WriteAppSettings(2, 35, 1, new byte[] { (byte)_level });
                    WriteAppSettings(2, 34, 1, new byte[] { _packetSpeed });
                    WriteAppSettings(2, 32, 1, new byte[] { ClevoLed.Packet44UiIndex(_packetMode) });
                    int[] keys = { 85, 89, 93, 10, 27, 13 };
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var c = _packetZones[i];
                        WriteAppSettings(2, keys[i], 3, new byte[] { c.R, c.G, c.B });
                    }
                    WriteAppSettings(2, 8, 1, new byte[] { 1 });
                }
            }
            catch { }
        });
    }

    // The color is also stored in the driver's app-settings page (mode 8 = static), as GutsV always did.
    // That store is a registry value (AcpiBridge.sys), so it is throttled instead of written every frame;
    // the last color is written by a trailing timer.
    private void QueuePersist(ColorRGB color)
    {
        lock (_persistLock)
        {
            _pendingPersistColor = color;
            if (!_isInitialized) return;
            if (!_sinceLastPersist.IsRunning || _sinceLastPersist.Elapsed >= PersistInterval)
            {
                FlushPersist();
            }
            else
            {
                _persistTimer ??= new Timer(_ =>
                {
                    lock (_persistLock)
                    {
                        try { if (_isInitialized) FlushPersist(); } catch { }
                    }
                });
                _persistTimer.Change(PersistInterval - _sinceLastPersist.Elapsed, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void FlushPersist()
    {
        if (_pendingPersistColor is not ColorRGB c || c == _persistedColor) return;
        WriteAppSettings(2, 81, 3, new byte[] { c.G, c.R, c.B }); // byte order kept as in earlier GutsV releases
        WriteAppSettings(2, 32, 1, new byte[] { 0x08 });
        _persistedColor = c;
        _sinceLastPersist.Restart();
    }

    public Task SetHardwareEffectAsync(HardwareEffect effect)
    {
        if (!_isInitialized || !_features.HasFlag(KeyboardFeatures.HardwareEffects)) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    if (Protocol == LedProtocol.Packet44)
                    {
                        _packetMode = ClevoLed.Packet44EffectCode(effect);
                        SendPacket44();
                        return;
                    }

                    // Word protocol, same order as Acer: brightness, (colors for Breathe), effect, flags
                    if (_levelKnown) Send(ClevoLed.BrightnessWord(_level, KeyboardType));
                    if (effect == HardwareEffect.Breathe)
                        foreach (var z in _zones)
                            if (_wordColors.TryGetValue(z, out var c))
                                Send(ClevoLed.ColorWord(z, ClevoLed.VendorColorCorrection(KeyboardType, c)));
                    Send(ClevoLed.EffectWord(effect));
                    Send(ClevoLed.FlagsWord(_hasLogo));
                    _flagsPending = true; // returning to static colors re-sends the flags word
                }
            }
            catch { }
        });
    }

    public Task SetHardwareBrightnessAsync(int level)
    {
        if (!_isInitialized || !_features.HasFlag(KeyboardFeatures.HardwareBrightness)) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    _level = Math.Clamp(level, 0, 4);
                    _levelKnown = true;
                    switch (Protocol)
                    {
                        case LedProtocol.Packet44:
                            SendPacket44();
                            break;
                        case LedProtocol.Words:
                            Send(ClevoLed.BrightnessWord(_level, KeyboardType));
                            Send(_level == 0 ? ClevoLed.FlagsWordOff : ClevoLed.FlagsWord(_hasLogo));
                            break;
                        case LedProtocol.White:
                            // White keyboards use command 39 with levels 0..5
                            byte whiteLevel = WhiteLevels[_level];
                            SetDCHU_Data(39, new byte[] { whiteLevel, 0, 0, 0 }, 4);
                            break;
                    }
                }
            }
            catch { }
        });
    }

    public Task SetBootEffectAsync(bool enabled)
    {
        if (!_isInitialized || !_features.HasFlag(KeyboardFeatures.BootEffect)) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    SetDCHU_Data(ClevoLed.DchuBootEffectSleepCommand, ClevoLed.ToBytes(ClevoLed.BootEffectWord(enabled)), 4);
                    // Same store Control Center reads, so it does not undo the choice on its next start
                    WriteAppSettings(2, 7, 1, new byte[] { (byte)(enabled ? 1 : 0) });
                }
            }
            catch { }
        });
    }

    public Task SetSleepTimerAsync(int? seconds)
    {
        if (!_isInitialized || !_features.HasFlag(KeyboardFeatures.SleepTimer)) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                lock (_ledLock)
                {
                    SetDCHU_Data(ClevoLed.DchuBootEffectSleepCommand, ClevoLed.ToBytes(ClevoLed.SleepTimerWord(seconds)), 4);
                    WriteAppSettings(2, 36, 1, new byte[] { (byte)(seconds.HasValue ? 1 : 0) });
                    if (seconds is int s)
                    {
                        s = Math.Clamp(s, ClevoLed.SleepTimerMinSeconds, ClevoLed.SleepTimerMaxSeconds);
                        WriteAppSettings(2, 37, 3, new byte[] { (byte)(s / 3600), (byte)(s / 60 % 60), (byte)(s % 60) });
                    }
                }
            }
            catch { }
        });
    }

    // GutsV 0..4 -> firmware 0..5 (evenly spread)
    private static readonly byte[] WhiteLevels = { 0, 1, 3, 4, 5 };

    private static void Send(uint word) => SetDCHU_Data(ClevoLed.DchuLedCommand, ClevoLed.ToBytes(word), 4);

    public Task ShutdownAsync()
    {
        try
        {
            lock (_ledLock) // wait for an in-flight LED write
            lock (_persistLock)
            {
                if (_isInitialized) FlushPersist();
                _persistTimer?.Dispose();
                _persistTimer = null;
            }
        }
        catch { }
        _isInitialized = false;
        return Task.CompletedTask;
    }
}
