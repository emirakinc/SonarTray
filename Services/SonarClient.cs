using System.Globalization;
using System.Net;
using System.Text.Json;
using SonarTray.Models;

namespace SonarTray.Services;

/// <summary>
/// Typed wrapper around Sonar's (unofficial) local REST API, classic mode only.
/// All endpoint strings live here so a future GG update is a one-file fix.
/// </summary>
public sealed class SonarClient
{
    private readonly HttpClient _http;
    private HttpMethod? _redirectMethod;

    public Uri BaseUrl { get; }

    /// <summary>"PUT" or "POST" once a redirection call has succeeded; "?" before that.</summary>
    public string RedirectionMethod => _redirectMethod?.Method ?? "?";

    public SonarClient(HttpClient http, Uri baseUrl)
    {
        _http = http;
        BaseUrl = baseUrl;
    }

    // ---- reads ---------------------------------------------------------

    /// <returns>"classic" or "stream"</returns>
    public async Task<string> GetModeAsync(CancellationToken ct)
    {
        var s = await _http.GetStringAsync(U("mode"), ct).ConfigureAwait(false);
        return s.Trim().Trim('"');
    }

    public Task<VolumeSettingsDto?> GetVolumesAsync(CancellationToken ct)
        => GetJsonAsync<VolumeSettingsDto>("volumeSettings/classic", ct);

    public Task<List<AudioDeviceDto>?> GetAudioDevicesAsync(CancellationToken ct)
        => GetJsonAsync<List<AudioDeviceDto>>("audioDevices", ct);

    public Task<List<RedirectionDto>?> GetRedirectionsAsync(CancellationToken ct)
        => GetJsonAsync<List<RedirectionDto>>("classicRedirections", ct);

    // ---- writes --------------------------------------------------------

    /// <param name="volumeId">master, game, chatRender, chatCapture, media, aux</param>
    public Task SetVolumeAsync(string volumeId, double volume, CancellationToken ct)
    {
        var v = Math.Clamp(volume, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture);
        Log.Verbose($"PUT volume {volumeId}={v}");
        return SendEmptyAsync(HttpMethod.Put, $"volumeSettings/classic/{volumeId}/Volume/{v}", ct);
    }

    public Task SetMuteAsync(string volumeId, bool muted, CancellationToken ct)
    {
        Log.Verbose($"PUT mute {volumeId}={muted}");
        return SendEmptyAsync(HttpMethod.Put, $"volumeSettings/classic/{volumeId}/Mute/{(muted ? "true" : "false")}", ct);
    }

    /// <param name="redirectionId">game, chat, media, aux, mic</param>
    public async Task SetRedirectionDeviceAsync(string redirectionId, string deviceId, CancellationToken ct)
    {
        var path = $"classicRedirections/{redirectionId}/deviceId/{Uri.EscapeDataString(deviceId)}";
        var first = _redirectMethod ?? HttpMethod.Put;
        Log.Verbose($"{first.Method} redirection {redirectionId}={deviceId}");

        var status = await SendEmptyStatusAsync(first, path, ct).ConfigureAwait(false);
        if (IsSuccess(status))
        {
            if (_redirectMethod is null)
            {
                _redirectMethod = first;
                Log.Info($"Redirection method confirmed: {first.Method}");
            }
            return;
        }

        if (_redirectMethod is null && status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            var status2 = await SendEmptyStatusAsync(HttpMethod.Post, path, ct).ConfigureAwait(false);
            if (IsSuccess(status2))
            {
                _redirectMethod = HttpMethod.Post;
                Log.Info("Redirection method confirmed: POST (PUT was rejected)");
                return;
            }
            throw new HttpRequestException($"Redirection {redirectionId} failed: PUT->{(int)status}, POST->{(int)status2}");
        }

        throw new HttpRequestException($"Redirection {redirectionId} failed: {(int)status} {status}");
    }

    // ---- plumbing ------------------------------------------------------

    private Uri U(string relativePath) => new(BaseUrl, relativePath);

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(U(path), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, SonarJson.Options, ct).ConfigureAwait(false);
    }

    private async Task SendEmptyAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var status = await SendEmptyStatusAsync(method, path, ct).ConfigureAwait(false);
        if (!IsSuccess(status))
            throw new HttpRequestException($"{method.Method} {path} -> {(int)status} {status}");
    }

    /// <summary>Sends a request with an explicit empty body (Content-Length: 0) and returns the status code.</summary>
    private async Task<HttpStatusCode> SendEmptyStatusAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, U(path)) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        return resp.StatusCode;
    }

    private static bool IsSuccess(HttpStatusCode code) => (int)code is >= 200 and < 300;
}
