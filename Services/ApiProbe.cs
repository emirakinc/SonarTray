using System.Net;
using System.Text;

namespace SonarTray.Services;

/// <summary>
/// `SonarTray.exe --probe`: re-derives the endpoint table in docs/sonar-api.md against the running
/// Sonar, so a GG update that moves a route shows up as a diff instead of as a silent failure.
///
/// Read-only by construction. Write endpoints are identified with OPTIONS and the Allow header;
/// nothing here ever sends a PUT, so running it cannot disturb the user's audio.
/// </summary>
public static class ApiProbe
{
    /// <summary>Paths fetched with GET. A 200 here means the route exists and returns a document.</summary>
    private static readonly string[] Reads =
    {
        "mode",
        "volumeSettings/classic",
        "volumeSettings/streamer",
        "audioDevices",
        "classicRedirections",
        "streamRedirections",
        "configs/selected",
        "audioDeviceRouting",
    };

    /// <summary>
    /// Paths probed with OPTIONS. The placeholder segments are never dereferenced - the server
    /// answers about the route, not the resource - so the ids below can be nonsense.
    /// </summary>
    private static readonly string[] Writes =
    {
        "mode/classic",
        "mode/stream",
        "volumeSettings/classic/master/Volume/0.5",
        "volumeSettings/classic/master/Mute/false",
        "volumeSettings/streamer/master/streaming/Volume/0.5",
        "volumeSettings/streamer/master/streaming/isMuted/false",
        "volumeSettings/streamer/master/monitoring/Volume/0.5",
        "classicRedirections/game/deviceId/probe",
        "streamRedirections/streaming/deviceId/probe",
        "configs/probe/select",
    };

    /// <summary>Routes that used to exist, or that people assume exist. Logged when they answer.</summary>
    private static readonly string[] Absent =
    {
        "chatMix",
        "chatMixState",
        "gameChatBalance",
        "subscribe",
        "events",
        "audioSessions",
    };

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        Log.Info("=== API PROBE START ===");

        using var discovery = new SonarDiscovery();
        var endpoint = await discovery.DiscoverAsync(ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            Log.Error("Discovery failed; is SteelSeries GG running with Sonar enabled?");
            return 2;
        }

        Log.Info($"Sonar at {endpoint.BaseUrl} (running={endpoint.IsRunning} ready={endpoint.IsReady})");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        Log.Info("-- reads (GET) --");
        foreach (var path in Reads)
        {
            var (status, length) = await GetAsync(http, endpoint.BaseUrl, path, ct).ConfigureAwait(false);
            Log.Info($"  GET     {path,-52} {status} {Size(length)}");
        }

        Log.Info("-- writes (OPTIONS, nothing is sent) --");
        foreach (var path in Writes)
        {
            var allow = await AllowAsync(http, endpoint.BaseUrl, path, ct).ConfigureAwait(false);
            Log.Info($"  {allow,-7} {path,-52}");
        }

        Log.Info("-- expected to be absent --");
        foreach (var path in Absent)
        {
            var (status, _) = await GetAsync(http, endpoint.BaseUrl, path, ct).ConfigureAwait(false);
            var note = status == HttpStatusCode.NotFound ? "absent, as documented" : "NOW PRESENT - docs are stale";
            Log.Info($"  GET     {path,-52} {(int)status} ({note})");
        }

        Log.Info("=== API PROBE DONE ===");
        Log.Info($"Compare against docs/sonar-api.md; results are in {Log.FilePath}");
        return 0;
    }

    private static async Task<(HttpStatusCode Status, long Length)> GetAsync(
        HttpClient http, Uri baseUrl, string path, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(new Uri(baseUrl, path), ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return (resp.StatusCode, body.LongLength);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"  GET {path} threw {ex.GetType().Name}: {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>
    /// Returns the verb the route accepts. A 405 carries the answer in its Allow header, which is
    /// how a write endpoint can be confirmed without ever performing a write.
    /// </summary>
    private static async Task<string> AllowAsync(HttpClient http, Uri baseUrl, string path, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Options, new Uri(baseUrl, path));
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);

            // Allow is a content header in HttpClient's model, not a response header; reading only
            // resp.Headers silently reports every write endpoint as a bare 405.
            var allowed = resp.Content.Headers.Allow;
            if (allowed.Count > 0) return string.Join(",", allowed);

            if (resp.Headers.TryGetValues("Allow", out var values))
            {
                var allow = string.Join(",", values).Trim();
                if (allow.Length > 0) return allow;
            }

            return resp.StatusCode == HttpStatusCode.NotFound ? "404" : ((int)resp.StatusCode).ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.GetType().Name;
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        0 => "",
        < 1024 => $"({bytes} B)",
        < 1024 * 1024 => $"({bytes / 1024.0:0.#} KB)",
        _ => $"({bytes / (1024.0 * 1024.0):0.#} MB)",
    };
}
