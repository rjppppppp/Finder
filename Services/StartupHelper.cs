using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FinderApp.Services;

/// <summary>
/// Manages Windows Startup registry entry so Finder launches automatically on system boot.
/// Appears in Windows Task Manager -> "Startup apps" and Settings -> Apps -> Startup.
/// </summary>
public static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Finder";

    public static void EnsureStartupRegistered()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            // Never hijack startup from local debug/dev build folders
            if (exePath.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || 
                exePath.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key != null)
            {
                string expectedVal = $"\"{exePath}\"";
                object? currentVal = key.GetValue(AppName);

                if (currentVal as string != expectedVal)
                {
                    key.SetValue(AppName, expectedVal);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register startup: {ex.Message}");
        }
    }

    public static void RemoveFromStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }
}
