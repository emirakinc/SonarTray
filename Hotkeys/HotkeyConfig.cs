using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonarTray.Services;

namespace SonarTray.Hotkeys;

public enum HotkeyAction
{
    MicMute,
    MasterMute,
    MasterUp,
    MasterDown,
    TogglePanel,

    // Per-channel actions. These ship unbound: the five above already take five combinations,
    // and claiming nine more by default would be rude to whatever else the user runs.
    GameUp,
    GameDown,
    GameMute,
    ChatUp,
    ChatDown,
    ChatMute,
    MediaUp,
    MediaDown,
    MediaMute,
}

/// <summary>
/// User-editable hotkey bindings. Written to %LOCALAPPDATA%\SonarTray\hotkeys.json on first run
/// so there is something to edit; an empty string disables that action.
///
/// Defaults deliberately avoid the popular letter combinations - Ctrl+Shift+M is Discord's
/// default mute, and Ctrl+Alt+letter collides with AltGr on Turkish layouts.
///
/// They also avoid Ctrl+Shift+arrow. RegisterHotKey accepts those, but a low-level keyboard hook
/// from another application can swallow the keystroke before the hotkey layer ever sees it, and
/// measurably does on at least one machine: the F-key combinations below fired every time while
/// the arrow ones fired none. A swallowed hotkey is invisible - registration still reports
/// success - so the defaults stay on keys that were observed to work.
/// </summary>
public sealed class HotkeyConfig
{
    public string MicMute { get; set; } = "Ctrl+Shift+F9";
    public string MasterMute { get; set; } = "Ctrl+Shift+F10";
    public string MasterUp { get; set; } = "Ctrl+Shift+F12";
    public string MasterDown { get; set; } = "Ctrl+Shift+F11";
    public string TogglePanel { get; set; } = "Ctrl+Shift+F8";

    public string GameUp { get; set; } = "";
    public string GameDown { get; set; } = "";
    public string GameMute { get; set; } = "";
    public string ChatUp { get; set; } = "";
    public string ChatDown { get; set; } = "";
    public string ChatMute { get; set; } = "";
    public string MediaUp { get; set; } = "";
    public string MediaDown { get; set; } = "";
    public string MediaMute { get; set; } = "";

    /// <summary>Amount the volume up/down actions move a channel, 0..1.</summary>
    public double VolumeStep { get; set; } = 0.05;

    [JsonIgnore]
    public IEnumerable<(HotkeyAction Action, string Gesture)> Bindings
        => Enum.GetValues<HotkeyAction>().Select(action => (action, Get(action)));

    public string Get(HotkeyAction action) => action switch
    {
        HotkeyAction.MicMute => MicMute,
        HotkeyAction.MasterMute => MasterMute,
        HotkeyAction.MasterUp => MasterUp,
        HotkeyAction.MasterDown => MasterDown,
        HotkeyAction.TogglePanel => TogglePanel,
        HotkeyAction.GameUp => GameUp,
        HotkeyAction.GameDown => GameDown,
        HotkeyAction.GameMute => GameMute,
        HotkeyAction.ChatUp => ChatUp,
        HotkeyAction.ChatDown => ChatDown,
        HotkeyAction.ChatMute => ChatMute,
        HotkeyAction.MediaUp => MediaUp,
        HotkeyAction.MediaDown => MediaDown,
        HotkeyAction.MediaMute => MediaMute,
        // No catch-all on purpose: a new action must be added here, and the compiler says so.
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped hotkey action"),
    };

    public void Set(HotkeyAction action, string gesture)
    {
        switch (action)
        {
            case HotkeyAction.MicMute: MicMute = gesture; break;
            case HotkeyAction.MasterMute: MasterMute = gesture; break;
            case HotkeyAction.MasterUp: MasterUp = gesture; break;
            case HotkeyAction.MasterDown: MasterDown = gesture; break;
            case HotkeyAction.TogglePanel: TogglePanel = gesture; break;
            case HotkeyAction.GameUp: GameUp = gesture; break;
            case HotkeyAction.GameDown: GameDown = gesture; break;
            case HotkeyAction.GameMute: GameMute = gesture; break;
            case HotkeyAction.ChatUp: ChatUp = gesture; break;
            case HotkeyAction.ChatDown: ChatDown = gesture; break;
            case HotkeyAction.ChatMute: ChatMute = gesture; break;
            case HotkeyAction.MediaUp: MediaUp = gesture; break;
            case HotkeyAction.MediaDown: MediaDown = gesture; break;
            case HotkeyAction.MediaMute: MediaMute = gesture; break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped hotkey action");
        }
    }

    /// <summary>
    /// Where this instance came from, so the parameterless <see cref="Save()"/> writes back to the
    /// same file rather than to the one shared user path.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarTray", "hotkeys.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Without this the default encoder writes "Ctrl+Shift+F9", which is valid JSON but
        // unreadable in a file whose whole point is that a human edits it.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Never throws: a broken or unreadable file falls back to the defaults.</summary>
    public static HotkeyConfig Load() => Load(FilePath);

    /// <summary>Path-explicit overload; <see cref="Load()"/> is this against <see cref="FilePath"/>.</summary>
    public static HotkeyConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new HotkeyConfig { SourcePath = path };
                fresh.Save(path);
                Log.Info($"Hotkey config created at {path}");
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<HotkeyConfig>(File.ReadAllText(path), Options);
            if (loaded is null)
            {
                Log.Warn("Hotkey config was empty; using defaults");
                return new HotkeyConfig { SourcePath = path };
            }
            loaded.SourcePath = path;
            loaded.VolumeStep = Math.Clamp(loaded.VolumeStep, 0.01, 0.5);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey config could not be read; using defaults", ex);
            return new HotkeyConfig { SourcePath = path };
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
            Log.Error("Hotkey config could not be written", ex);
        }
    }
}
