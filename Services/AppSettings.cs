using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonarTray.Services;

/// <summary>
/// Everything the settings page can change, in %LOCALAPPDATA%\SonarTray\settings.json.
/// Hotkey bindings stay in their own file (<see cref="Hotkeys.HotkeyConfig"/>) because they are
/// the one thing people hand-edit, and mixing them in would make that file noisier.
/// </summary>
public sealed class AppSettings
{
    /// <summary>"auto" follows the OS UI language; otherwise a culture name such as "en" or "tr".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Aux is hidden by default: most setups never route anything to it.</summary>
    public bool ShowAux { get; set; }

    public bool OsdEnabled { get; set; } = true;

    /// <summary>How long the on-screen display stays up, in milliseconds.</summary>
    public int OsdDurationMs { get; set; } = 1200;

    /// <summary>Scrolling over the tray icon changes the master volume.</summary>
    public bool TrayWheelVolume { get; set; } = true;

    /// <summary>Ask GitHub once a day whether a newer release exists. No telemetry is sent.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Set by the update check so a dismissed version is not offered again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>When the update check last ran, successful or not; null means it never has.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// Where this instance came from, so the parameterless <see cref="Save()"/> writes back to the
    /// same file. Without it every instance saves to the one shared user path, which means a test
    /// holding a throwaway instance silently overwrites the real configuration.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarTray", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Resolves <see cref="Language"/> to a culture, or null to follow the OS.</summary>
    public CultureInfo? ResolveCulture()
    {
        if (string.IsNullOrWhiteSpace(Language) || Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        // predefinedOnly matters: without it .NET happily invents a custom culture for any
        // well-formed-looking string, so a typo in the settings file would silently produce a
        // culture with no resources rather than falling back to the system language.
        try { return CultureInfo.GetCultureInfo(Language, predefinedOnly: true); }
        catch (CultureNotFoundException)
        {
            Log.Warn($"Unknown language '{Language}' in settings; following the system instead");
            return null;
        }
    }

    /// <summary>Never throws: a broken or unreadable file falls back to the defaults.</summary>
    public static AppSettings Load() => Load(FilePath);

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new AppSettings { SourcePath = path };
                fresh.Save(path);
                Log.Info($"Settings created at {path}");
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            if (loaded is null)
            {
                Log.Warn("Settings file was empty; using defaults");
                return new AppSettings { SourcePath = path };
            }

            loaded.SourcePath = path;
            loaded.OsdDurationMs = Math.Clamp(loaded.OsdDurationMs, 300, 10_000);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be read; using defaults", ex);
            return new AppSettings { SourcePath = path };
        }
    }

    public void Save() => Save(SourcePath ?? FilePath);

    public void Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be written", ex);
        }
    }
}
