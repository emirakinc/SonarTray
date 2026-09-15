using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonarTray.Models;

internal static class SonarJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

// ---- GG discovery -------------------------------------------------------

public sealed class CorePropsDto
{
    [JsonPropertyName("ggEncryptedAddress")] public string? GgEncryptedAddress { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
}

public sealed class SubAppsRootDto
{
    [JsonPropertyName("subApps")] public SubAppsDto? SubApps { get; set; }
}

public sealed class SubAppsDto
{
    [JsonPropertyName("sonar")] public SubAppDto? Sonar { get; set; }
}

public sealed class SubAppDto
{
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("isRunning")] public bool IsRunning { get; set; }
    [JsonPropertyName("isReady")] public bool IsReady { get; set; }
    [JsonPropertyName("metadata")] public SubAppMetadataDto? Metadata { get; set; }
}

public sealed class SubAppMetadataDto
{
    [JsonPropertyName("webServerAddress")] public string? WebServerAddress { get; set; }
}

// ---- Sonar --------------------------------------------------------------

/// <summary>GET /volumeSettings/classic</summary>
public sealed class VolumeSettingsDto
{
    [JsonPropertyName("masters")] public ModeSetDto? Masters { get; set; }
    /// <summary>Keys: game, chatRender, chatCapture, media, aux</summary>
    [JsonPropertyName("devices")] public Dictionary<string, ModeSetDto>? Devices { get; set; }
}

public sealed class ModeSetDto
{
    [JsonPropertyName("classic")] public VolumeStateDto? Classic { get; set; }

    /// <summary>
    /// Empty in the classic document; populated in /volumeSettings/streamer, where each channel
    /// carries two independent mixes.
    /// </summary>
    [JsonPropertyName("stream")] public StreamSetDto? Stream { get; set; }
}

/// <summary>The two sub-mixes stream mode splits every channel into.</summary>
public sealed class StreamSetDto
{
    /// <summary>What goes out to viewers.</summary>
    [JsonPropertyName("streaming")] public VolumeStateDto? Streaming { get; set; }

    /// <summary>What the streamer hears.</summary>
    [JsonPropertyName("monitoring")] public VolumeStateDto? Monitoring { get; set; }
}

public sealed class VolumeStateDto
{
    [JsonPropertyName("volume")] public double Volume { get; set; }
    [JsonPropertyName("muted")] public bool Muted { get; set; }
}

/// <summary>GET /audioDevices</summary>
public sealed class AudioDeviceDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("friendlyName")] public string FriendlyName { get; set; } = "";
    /// <summary>"render" or "capture"</summary>
    [JsonPropertyName("dataFlow")] public string DataFlow { get; set; } = "";
    /// <summary>"none" for real devices; game/chatRender/media/chatCapture for Sonar's virtual devices</summary>
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("isVad")] public bool IsVad { get; set; }
    [JsonPropertyName("channels")] public int Channels { get; set; }
}

/// <summary>GET /classicRedirections</summary>
public sealed class RedirectionDto
{
    /// <summary>aux, chat, game, media, mic</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }
    [JsonPropertyName("isRunning")] public bool IsRunning { get; set; }
}

// ---- stream mode --------------------------------------------------------

/// <summary>GET /streamRedirections — one record per sub-mix, not per channel.</summary>
public sealed class StreamRedirectionDto
{
    /// <summary>"streaming" or "monitoring"</summary>
    [JsonPropertyName("streamRedirectionId")] public string Id { get; set; } = "";
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }
    [JsonPropertyName("isRunning")] public bool IsRunning { get; set; }

    /// <summary>Which channels are folded into this sub-mix.</summary>
    [JsonPropertyName("status")] public List<StreamRoleStatusDto>? Status { get; set; }
}

public sealed class StreamRoleStatusDto
{
    /// <summary>game, chatRender, chatCapture, media, aux</summary>
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
}

// ---- audio profiles -----------------------------------------------------

/// <summary>
/// GET /configs and /configs/selected. The "data" blob (EQ curves, boosts, surround) is
/// deliberately not mapped: SonarTray only ever selects a profile, never edits one, and the
/// full document runs to megabytes.
/// </summary>
public sealed class ConfigDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>game, chatRender, chatCapture, media, aux — matches a channel volume id.</summary>
    [JsonPropertyName("virtualAudioDevice")] public string VirtualAudioDevice { get; set; } = "";
}

// ---- per-application routing (read-only) --------------------------------

/// <summary>GET /audioDeviceRouting</summary>
public sealed class AudioDeviceRoutingDto
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("dataFlow")] public string DataFlow { get; set; } = "";
    [JsonPropertyName("audioSessions")] public List<AudioSessionDto>? AudioSessions { get; set; }
}

public sealed class AudioSessionDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("processId")] public int ProcessId { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("isSystemSound")] public bool IsSystemSound { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("routingErrorDetected")] public bool RoutingErrorDetected { get; set; }
}
