using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SonarTray.Services;

/// <summary>The GitHub release payload, trimmed to the three fields that matter.</summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
}

/// <summary>What the panel needs to know about a newer release.</summary>
public sealed record UpdateInfo(SemanticVersion Version, string ReleaseUrl);

/// <summary>
/// Asks GitHub, at most once a day, whether a newer release exists.
///
/// This is the only outbound request SonarTray ever makes to anything other than the local Sonar
/// API. It sends nothing but a User-Agent, carries no identifier, and the whole thing is off if
/// <see cref="AppSettings.CheckForUpdates"/> is false.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/emirakinc/SonarTray/releases/latest";

    /// <summary>GitHub rejects requests without one.</summary>
    private const string UserAgent = "SonarTray-update-check";

    private static readonly TimeSpan MinimumInterval = TimeSpan.FromDays(1);

    private readonly AppSettings _settings;
    private readonly SemanticVersion _current;
    private readonly HttpClient _http;

    public UpdateChecker(AppSettings settings, SemanticVersion current)
    {
        _settings = settings;
        _current = current;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Null when the check is switched off, was made recently, failed, or found nothing newer.
    /// Never throws: an offline machine or a rate-limited API is not something to report.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        if (!_settings.CheckForUpdates)
        {
            Log.Verbose("Update check skipped: switched off");
            return null;
        }

        if (!IsDue(DateTime.UtcNow))
        {
            Log.Verbose("Update check skipped: already checked today");
            return null;
        }

        try
        {
            var release = await _http.GetFromJsonAsync<GitHubReleaseDto>(LatestReleaseUrl, ct).ConfigureAwait(false);

            // Record the attempt, not the success: a failing check should still back off for a day
            // rather than retrying on every start.
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();

            return Evaluate(release);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Verbose($"Update check failed: {ex.GetType().Name}: {ex.Message}");
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            return null;
        }
    }

    /// <summary>Whether enough time has passed since the last attempt.</summary>
    public bool IsDue(DateTime utcNow)
    {
        var last = _settings.LastUpdateCheckUtc;
        if (last is null) return true;

        // A clock that has moved backwards (or a hand-edited file) must not disable the check
        // forever, so anything in the future counts as due.
        if (last > utcNow) return true;

        return utcNow - last >= MinimumInterval;
    }

    /// <summary>
    /// Decides whether a release is worth telling the user about. Separate from the HTTP call so
    /// the interesting half is testable without a network.
    /// </summary>
    public UpdateInfo? Evaluate(GitHubReleaseDto? release)
    {
        if (release is null || release.Draft) return null;

        if (!SemanticVersion.TryParse(release.TagName, out var latest))
        {
            Log.Warn($"Release tag '{release.TagName}' is not a version this understands");
            return null;
        }

        // Never move someone from a stable build onto a pre-release. Anyone already running one
        // is opted in and does get offered the next pre-release.
        if (latest.IsPreRelease && !_current.IsPreRelease) return null;

        if (latest.CompareTo(_current) <= 0) return null;

        if (_settings.SkippedVersion is { Length: > 0 } skipped
            && SemanticVersion.TryParse(skipped, out var skippedVersion)
            && latest.CompareTo(skippedVersion) <= 0)
        {
            Log.Verbose($"Update {latest} ignored: the user skipped it");
            return null;
        }

        var url = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? "https://github.com/emirakinc/SonarTray/releases/latest"
            : release.HtmlUrl;

        Log.Info($"Update available: {latest} (running {_current})");
        return new UpdateInfo(latest, url);
    }

    public void Dispose() => _http.Dispose();
}
