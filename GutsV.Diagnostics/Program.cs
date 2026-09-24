using System;
using System.IO;
using System.Linq;
using System.Management;
using HidLibrary;

namespace GutsV.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================");
        Console.WriteLine("   GutsV Hardware Diagnostic Tool v1.0    ");
        Console.WriteLine("==========================================");
        Console.ResetColor();

        string logPath = Path.Combine(Environment.CurrentDirectory, "GutsV_Report.txt");
        using (StreamWriter writer = new StreamWriter(logPath))
        {
            Log(writer, "Diagnostic Started at " + DateTime.Now);
            
            // 1. System Info
            Log(writer, "\n[1] SYSTEM INFORMATION");
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    Log(writer, $"Manufacturer: {obj["Manufacturer"]}");
                    Log(writer, $"Model: {obj["Model"]}");
                }
            }
            catch (Exception ex) { Log(writer, "Error: " + ex.Message); }

            // 2. WMI Scan (Specific Clevo/Monster)
            Log(writer, "\n[2] WMI SCAN (RGB Related)");
            ScanWmi(writer, "root\\WMI", "CLEVO");
            ScanWmi(writer, "root\\WMI", "LED");
            ScanWmi(writer, "root\\WMI", "KBL");
            ScanWmi(writer, "root\\WMI", "RGB");

            // 3. HID Scan
            Log(writer, "\n[3] HID DEVICE SCAN");
            try
            {
                var devices = HidDevices.Enumerate().ToList();
                Log(writer, $"Total HID Devices Found: {devices.Count}");
                foreach (var dev in devices)
                {
                    // List ALL devices to find the controller
                    Log(writer, $"VID: 0x{dev.Attributes.VendorId:X4} | PID: 0x{dev.Attributes.ProductId:X4} | Path: {dev.DevicePath} | Desc: {dev.Description}");
                }
            }
            catch (Exception ex) { Log(writer, "Error: " + ex.Message); }

            // 4. File Check
            Log(writer, "\n[4] FILE CHECK");
            string[] checkFiles = { "InsydeDCHU.dll", "clevomof.dll" };
            foreach (var file in checkFiles)
            {
                bool exists = File.Exists(file);
                Log(writer, $"{file}: {(exists ? "FOUND" : "MISSING")}");
            }

            Log(writer, "\nDiagnostic Completed.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nReport saved to: {logPath}");
        Console.WriteLine("Please send the contents of this file to the developer.");
        Console.ResetColor();
    }

    static void ScanWmi(StreamWriter writer, string namespc, string filter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(namespc, "SELECT * FROM meta_class");
            foreach (ManagementClass obj in searcher.Get())
            {
                if (obj.ClassPath.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    Log(writer, $"Found WMI Class: {obj.ClassPath.ClassName} (Namespace: {namespc})");
                    
                    // Try to list methods
                    foreach(var method in obj.Methods)
                    {
                        Log(writer, $"  - Method: {method.Name}");
                    }
                }
            }
        }
        catch {}
    }

    static void Log(StreamWriter writer, string msg)
    {
        Console.WriteLine(msg);
        writer.WriteLine(msg);
    }
}
