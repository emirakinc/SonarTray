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
    // "stream": {} is present in the payload but intentionally not mapped.
    [JsonPropertyName("classic")] public VolumeStateDto? Classic { get; set; }
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
