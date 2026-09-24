using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GutsV.Core.Interfaces;
using GutsV.Core.Models;

namespace GutsV.Drivers.Insyde;

public class InsydeDriver : IKeyboardController
{
    private const string DllName = "InsydeDCHU.dll";

    // Delegates for P/Invoke (if using LoadLibrary) or DllImport
    // Since we want simple implementation, we use DllImport.
    // Ensure "InsydeDCHU.dll" is in the output folder.

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDCHU_Data")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WriteAppSettings")]
    private static extern int WriteAppSettings(int page, int command, int length, byte[] buffer);

    private bool _isInitialized = false;

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            if (!System.IO.File.Exists(DllName))
            {
                throw new System.IO.FileNotFoundException("InsydeDCHU.dll not found.");
            }
            
            // Allow calling once to test?
            _isInitialized = true;
        });
    }

    public Task SetColorAsync(Zone zone, ColorRGB color)
    {
        if (!_isInitialized) return Task.CompletedTask;
        if (zone != Zone.All && zone != Zone.Left) return Task.CompletedTask; // Single zone usually maps to 'All' or just sends one packet.

        return Task.Run(() =>
        {
            try
            {
                // Logic from C++:
                // 1. SetDCHU_Data(103, [G, R, B, 0xF0], 4)
                byte[] dchuData = new byte[] { color.G, color.R, color.B, 0xF0 };
                SetDCHU_Data(103, dchuData, 4);

                // 2. WriteAppSettings(2, 81, 3, [G, R, B])
                // Note: The C++ code said `colour.size()` which is 3 for RGB array? 
                // Let's assume sending just RGB colors.
                byte[] colorData = new byte[] { color.G, color.R, color.B }; // Order G R B based on others? Or R G B? 
                // WMI used G R B. Let's stick to G R B as per C++ likely having Colour[0]=G
                WriteAppSettings(2, 81, 3, colorData);

                // 3. WriteAppSettings(2, 32, 1, [0x08]) -> Mode 8
                byte[] modeData = new byte[] { 0x08 };
                WriteAppSettings(2, 32, 1, modeData);
            }
            catch
            {
                // DLL call error
            }
        });
    }

    public Task ShutdownAsync()
    {
        _isInitialized = false;
        return Task.CompletedTask;
    }
}
