using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonarTray.Models;

namespace SonarTray.Services;

/// <summary>
/// The named mixer snapshots, in %LOCALAPPDATA%\SonarTray\presets.json. Its own file rather than a
/// section of settings.json: it is the one piece of configuration that grows without bound, and
/// keeping it separate leaves the hand-editable settings file short.
/// </summary>
public sealed class PresetStore
{
    /// <summary>Enough to be useful, few enough that the menu stays a menu.</summary>
    public const int MaxPresets = 20;

    /// <summary>Long enough to be descriptive, short enough to fit the popup.</summary>
    public const int MaxNameLength = 40;

    private readonly List<MixerPreset> _presets = new();

    public IReadOnlyList<MixerPreset> Presets => _presets;

    public string? SourcePath { get; set; }

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarTray", "presets.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Trims, truncates, and rejects a name that is empty once trimmed.</summary>
    public static string? Normalise(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    public MixerPreset? Find(string name)
        => _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Adds the preset, replacing any existing one with the same name. Returns false when the name
    /// is unusable or the list is full.
    /// </summary>
    public bool Save(MixerPreset preset)
    {
        var name = Normalise(preset.Name);
        if (name is null) return false;

        preset.Name = name;

        var existing = Find(name);
        if (existing is not null)
        {
            _presets[_presets.IndexOf(existing)] = preset;
        }
        else
        {
            if (_presets.Count >= MaxPresets)
            {
                Log.Warn($"Preset '{name}' not saved: already at the {MaxPresets}-preset limit");
                return false;
            }
            _presets.Add(preset);
        }

        _presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        Persist();
        Log.Info($"Preset '{name}' saved ({preset.Channels.Count} channels)");
        return true;
    }

    public bool Delete(string name)
    {
        var existing = Find(name);
        if (existing is null) return false;

        _presets.Remove(existing);
        Persist();
        Log.Info($"Preset '{existing.Name}' deleted");
        return true;
    }

    /// <summary>Never throws: a broken file yields an empty list rather than blocking startup.</summary>
    public static PresetStore Load() => Load(FilePath);

    public static PresetStore Load(string path)
    {
        var store = new PresetStore { SourcePath = path };
        try
        {
            if (!File.Exists(path)) return store;

            var loaded = JsonSerializer.Deserialize<List<MixerPreset>>(File.ReadAllText(path), Options);
            if (loaded is null) return store;

            // Drop anything unusable rather than surfacing a half-broken entry in the menu.
            foreach (var preset in loaded)
            {
                if (Normalise(preset.Name) is not { } name) continue;
                preset.Name = name;
                if (store._presets.Count >= MaxPresets) break;
                if (store.Find(name) is not null) continue;
                store._presets.Add(preset);
            }

            store._presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error("Presets could not be read; starting with none", ex);
        }

        return store;
    }

    private void Persist()
    {
        var path = SourcePath ?? FilePath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(_presets, Options));
        }
        catch (Exception ex)
        {
            Log.Error("Presets could not be written", ex);
        }
    }
}
