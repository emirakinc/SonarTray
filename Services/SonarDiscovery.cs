using System.Net.Http.Json;
using System.Text.Json;
using SonarTray.Models;

namespace SonarTray.Services;

public sealed record SonarEndpoint(Uri BaseUrl, bool IsRunning, bool IsReady);

/// <summary>
/// coreProps.json -> ggEncryptedAddress -> https://.../subApps -> subApps.sonar.metadata.webServerAddress
/// Both ports change whenever GG restarts, so this must be re-run after any connection failure.
/// </summary>
public sealed class SonarDiscovery : IDisposable
{
    private static readonly string[] CorePropsCandidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "GG", "coreProps.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "SteelSeries Engine 3", "coreProps.json"),
    };

    private readonly HttpClient _gg;

    public SonarDiscovery()
    {
        // GG's local HTTPS endpoint uses a self-signed certificate. The client is only ever
        // pointed at loopback (enforced below), so accepting any certificate is acceptable here.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        _gg = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<SonarEndpoint?> DiscoverAsync(CancellationToken ct)
    {
        try
        {
            var path = CorePropsCandidates.FirstOrDefault(File.Exists);
            if (path is null)
            {
                Log.Warn("coreProps.json not found (is SteelSeries GG installed?)");
                return null;
            }

            string json;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                json = await sr.ReadToEndAsync(ct).ConfigureAwait(false);

            var props = JsonSerializer.Deserialize<CorePropsDto>(json, SonarJson.Options);
            var addr = props?.GgEncryptedAddress;
            if (string.IsNullOrWhiteSpace(addr))
            {
                Log.Warn("coreProps.json has no ggEncryptedAddress");
                return null;
            }

            if (!Uri.TryCreate($"https://{addr}/subApps", UriKind.Absolute, out var subAppsUri) || !subAppsUri.IsLoopback)
            {
                Log.Warn($"Refusing non-loopback GG address '{addr}'");
                return null;
            }

            using var resp = await _gg.GetAsync(subAppsUri, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var root = await resp.Content.ReadFromJsonAsync<SubAppsRootDto>(SonarJson.Options, ct).ConfigureAwait(false);

            var sonar = root?.SubApps?.Sonar;
            var web = sonar?.Metadata?.WebServerAddress;
            if (sonar is null || string.IsNullOrWhiteSpace(web))
            {
                Log.Warn("subApps response has no sonar.webServerAddress (Sonar disabled?)");
                return null;
            }

            if (!Uri.TryCreate(web, UriKind.Absolute, out var baseUrl) || !baseUrl.IsLoopback)
            {
                Log.Warn($"Refusing non-loopback Sonar address '{web}'");
                return null;
            }

            return new SonarEndpoint(baseUrl, sonar.IsRunning, sonar.IsReady);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"Discovery failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _gg.Dispose();
}
