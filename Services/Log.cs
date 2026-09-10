using System.Diagnostics;

namespace SonarTray.Services;

public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarTray", "sonartray.log");

    /// <summary>When true, every write request is logged (enable with --verbose).</summary>
    public static bool VerboseEnabled { get; set; }

    static Log()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > 1_000_000)
                File.Move(FilePath, FilePath + ".old", overwrite: true);
        }
        catch { /* logging must never throw */ }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERR ", ex is null ? message : $"{message}: {ex}");

    public static void Verbose(string message)
    {
        if (VerboseEnabled) Write("VERB", message);
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line + Environment.NewLine); }
            catch { /* ignore */ }
        }
    }
}
