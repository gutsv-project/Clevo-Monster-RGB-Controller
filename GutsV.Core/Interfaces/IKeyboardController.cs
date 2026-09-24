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
}
