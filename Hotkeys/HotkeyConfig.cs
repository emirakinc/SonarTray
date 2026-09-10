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

    /// <summary>Amount MasterUp/MasterDown move the master channel, 0..1.</summary>
    public double VolumeStep { get; set; } = 0.05;

    [JsonIgnore]
    public IEnumerable<(HotkeyAction Action, string Gesture)> Bindings
    {
        get
        {
            yield return (HotkeyAction.MicMute, MicMute);
            yield return (HotkeyAction.MasterMute, MasterMute);
            yield return (HotkeyAction.MasterUp, MasterUp);
            yield return (HotkeyAction.MasterDown, MasterDown);
            yield return (HotkeyAction.TogglePanel, TogglePanel);
        }
    }

    public string Get(HotkeyAction action) => action switch
    {
        HotkeyAction.MicMute => MicMute,
        HotkeyAction.MasterMute => MasterMute,
        HotkeyAction.MasterUp => MasterUp,
        HotkeyAction.MasterDown => MasterDown,
        _ => TogglePanel,
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
        }
    }

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
    public static HotkeyConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = new HotkeyConfig();
                fresh.Save();
                Log.Info($"Hotkey config created at {FilePath}");
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<HotkeyConfig>(File.ReadAllText(FilePath), Options);
            if (loaded is null)
            {
                Log.Warn("Hotkey config was empty; using defaults");
                return new HotkeyConfig();
            }
            loaded.VolumeStep = Math.Clamp(loaded.VolumeStep, 0.01, 0.5);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey config could not be read; using defaults", ex);
            return new HotkeyConfig();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey config could not be written", ex);
        }
    }
}
