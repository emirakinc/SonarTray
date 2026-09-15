using System.Text.Json.Serialization;

namespace SonarTray.Models;

/// <summary>
/// A named snapshot of the mixer: what every channel was set to, and where it was routed.
///
/// Audio profiles are deliberately not part of this. They are Sonar's EQ and effect settings
/// rather than mixer state, applying one is a separate write per channel, and someone who has
/// tuned an EQ rarely wants a volume preset to silently replace it.
/// </summary>
public sealed class MixerPreset
{
    public string Name { get; set; } = "";

    public List<PresetChannel> Channels { get; set; } = new();

    /// <summary>Captures the given channels. Nothing is written to Sonar here.</summary>
    public static MixerPreset Capture(string name, IEnumerable<(string VolumeId, double Volume, bool Muted, string? DeviceId)> channels)
        => new()
        {
            Name = name.Trim(),
            Channels = channels
                .Select(c => new PresetChannel
                {
                    VolumeId = c.VolumeId,
                    Volume = Math.Clamp(c.Volume, 0.0, 1.0),
                    Muted = c.Muted,
                    DeviceId = c.DeviceId,
                })
                .ToList(),
        };

    /// <summary>The entry for a channel, or null when the preset predates that channel.</summary>
    public PresetChannel? For(string volumeId)
        => Channels.FirstOrDefault(c => string.Equals(c.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase));
}

public sealed class PresetChannel
{
    /// <summary>master, game, chatRender, chatCapture, media, aux</summary>
    public string VolumeId { get; set; } = "";

    public double Volume { get; set; }

    public bool Muted { get; set; }

    /// <summary>Null when the channel had no device picker, or none was selected.</summary>
    public string? DeviceId { get; set; }

    [JsonIgnore]
    public bool HasDevice => !string.IsNullOrWhiteSpace(DeviceId);
}
