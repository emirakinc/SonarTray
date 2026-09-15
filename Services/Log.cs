using System.Diagnostics;

namespace SonarTray.Services;

public static class Log
{
    private const long MaxBytes = 1_000_000;

    /// <summary>How many rotated logs to keep beside the live one.</summary>
    private const int KeptGenerations = 3;

    private static readonly object Gate = new();

    private static string _filePath = DefaultFilePath;
    private static bool _prepared;

    private static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarTray", "sonartray.log");

    public static string FilePath => _filePath;

    /// <summary>When true, every write request is logged (enable with --verbose).</summary>
    public static bool VerboseEnabled { get; set; }

    /// <summary>
    /// Points logging somewhere else. The test suite calls this before touching anything that
    /// logs, so deliberately-corrupt fixtures do not fill the user's real log with stack traces.
    /// </summary>
    public static void UseFile(string path)
    {
        lock (Gate)
        {
            _filePath = path;
            _prepared = false;
        }
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
            try
            {
                // Lazily rather than in a static constructor: the path can still be redirected
                // after the type is first touched, and preparing eagerly would create the
                // default directory even for a process that never logs there.
                if (!_prepared)
                {
                    Prepare(_filePath);
                    _prepared = true;
                }

                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch { /* logging must never throw */ }
        }
    }

    /// <summary>Creates the directory and rolls the file over once it gets too big.</summary>
    private static void Prepare(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length <= MaxBytes) return;

        // Keep a short history instead of a single .old: a crash loop used to overwrite the one
        // previous file within seconds, taking the interesting part with it.
        var oldest = $"{path}.{KeptGenerations}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = KeptGenerations - 1; i >= 1; i--)
        {
            var from = $"{path}.{i}";
            if (File.Exists(from)) File.Move(from, $"{path}.{i + 1}", overwrite: true);
        }
        File.Move(path, $"{path}.1", overwrite: true);
    }
}
