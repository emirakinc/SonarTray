using System.Diagnostics;
using Microsoft.Win32;
using SonarTray.Native;

namespace SonarTray.Services;

/// <summary>Left-click action: bring the SteelSeries GG window to the front, launching the GG UI if needed.</summary>
public static class GgLauncher
{
    private static readonly string[] WindowProcessNames = { "SteelSeriesGGClient", "SteelSeriesGG" };
    private static DateTime _lastInvokeUtc = DateTime.MinValue;

    public static void ShowGg()
    {
        // NotifyIcon fires MouseUp twice on a double click; ignore the second one.
        if (DateTime.UtcNow - _lastInvokeUtc < TimeSpan.FromSeconds(1)) return;
        _lastInvokeUtc = DateTime.UtcNow;

        try
        {
            if (TryActivateExistingWindow()) return;

            var psi = BuildLaunchInfo();
            if (psi is null)
            {
                Log.Warn("SteelSeries GG installation not found");
                return;
            }

            Log.Info($"Launching {psi.FileName} {psi.Arguments}");
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error("ShowGg failed", ex);
        }
    }

    private static bool TryActivateExistingWindow()
    {
        foreach (var name in WindowProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;

                    NativeMethods.ShowWindow(hwnd, NativeMethods.IsIconic(hwnd) ? NativeMethods.SW_RESTORE : NativeMethods.SW_SHOW);
                    NativeMethods.SetForegroundWindow(hwnd);
                    Log.Verbose($"Activated GG window of {name} (pid {p.Id})");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Mirrors the Start Menu shortcut: SteelSeriesGGEZ.exe -dataPath="..." -dbEnv=production.
    /// (The HKLM autostart entry adds -auto=true, which starts GG hidden; running SteelSeriesGG.exe
    /// directly while the --hosted instance is alive does not open the window.)
    /// </summary>
    private static ProcessStartInfo? BuildLaunchInfo()
    {
        var dir = FindInstallDir();
        if (dir is null) return null;

        var ez = Path.Combine(dir, "SteelSeriesGGEZ.exe");
        if (File.Exists(ez))
        {
            var dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "GG");
            return new ProcessStartInfo(ez, $"-dataPath=\"{dataPath}\" -dbEnv=production")
            {
                UseShellExecute = true,
                WorkingDirectory = dir,
            };
        }

        var gg = Path.Combine(dir, "SteelSeriesGG.exe");
        return File.Exists(gg)
            ? new ProcessStartInfo(gg) { UseShellExecute = true, WorkingDirectory = dir }
            : null;
    }

    private static string? FindInstallDir()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SteelSeries GG");
            if (key?.GetValue("DisplayIcon") is string icon)
            {
                var path = icon.Trim().Trim('"').Split(',')[0];
                var dir = Path.GetDirectoryName(path);
                if (dir is not null && Directory.Exists(dir)) return dir;
            }
        }
        catch { /* fall through */ }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SteelSeries", "GG");
        return Directory.Exists(fallback) ? fallback : null;
    }
}
