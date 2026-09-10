using System.Diagnostics;
using Microsoft.Win32;

namespace SonarTray.Services;

/// <summary>"Start with Windows" via HKCU\...\Run (no admin rights needed).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SonarTray";

    // Environment.ProcessPath is the only reliable path under PublishSingleFile (Assembly.Location is empty).
    private static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "SonarTray.exe";

    private static string Command => $"\"{ExePath}\"";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Cannot open HKCU Run key");
        if (enabled)
        {
            key.SetValue(ValueName, Command);
            Log.Info($"Startup enabled: {Command}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("Startup disabled");
        }
    }

    /// <summary>If the exe was moved since the Run value was written, point the value at the new location.</summary>
    public static void RepairIfMoved()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current
                && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, Command);
                Log.Info($"Startup entry repaired: {current} -> {Command}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Startup repair failed: {ex.Message}");
        }
    }
}
