using SonarTray.Services;
using Xunit;

namespace SonarTray.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings;
    private readonly List<UpdateChecker> _checkers = new();

    public UpdateCheckerTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new AppSettings { SourcePath = Path.Combine(_dir, "settings.json") };
    }

    public void Dispose()
    {
        foreach (var checker in _checkers) checker.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup noise is not interesting */ }
    }

    private UpdateChecker Build(string currentVersion = "0.1.0")
    {
        Assert.True(SemanticVersion.TryParse(currentVersion, out var current));
        var checker = new UpdateChecker(_settings, current);
        _checkers.Add(checker);
        return checker;
    }

    private static GitHubReleaseDto Release(string tag, bool prerelease = false, bool draft = false)
        => new()
        {
            TagName = tag,
            HtmlUrl = $"https://github.com/emirakinc/SonarTray/releases/tag/{tag}",
            PreRelease = prerelease,
            Draft = draft,
        };

    // ---- Evaluate --------------------------------------------------------

    [Fact]
    public void Evaluate_NewerStableRelease_IsOffered()
    {
        var info = Build("0.1.0").Evaluate(Release("v0.2.0"));

        Assert.NotNull(info);
        Assert.Equal("0.2.0", info!.Version.ToString());
        Assert.Contains("v0.2.0", info.ReleaseUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v0.1.0")]   // the running version
    [InlineData("v0.0.9")]   // older
    public void Evaluate_NotNewer_IsNotOffered(string tag)
    {
        Assert.Null(Build("0.1.0").Evaluate(Release(tag)));
    }

    [Fact]
    public void Evaluate_Draft_IsIgnored()
    {
        Assert.Null(Build("0.1.0").Evaluate(Release("v9.0.0", draft: true)));
    }

    [Fact]
    public void Evaluate_Null_IsIgnored()
    {
        Assert.Null(Build().Evaluate(null));
    }

    [Fact]
    public void Evaluate_UnparseableTag_IsIgnored()
    {
        // A tag like "nightly" must not be read as 0.0.0 or crash the check.
        Assert.Null(Build("0.1.0").Evaluate(Release("nightly")));
    }

    [Fact]
    public void Evaluate_PreRelease_IsNotOfferedToAStableBuild()
    {
        Assert.Null(Build("0.1.0").Evaluate(Release("v0.2.0-beta.1")));
    }

    [Fact]
    public void Evaluate_PreRelease_IsOfferedToSomeoneAlreadyOnOne()
    {
        // Running a beta is opting in; the next beta is what that person wants to hear about.
        var info = Build("0.2.0-beta.1").Evaluate(Release("v0.2.0-beta.2"));

        Assert.NotNull(info);
        Assert.Equal("0.2.0-beta.2", info!.Version.ToString());
    }

    [Fact]
    public void Evaluate_StableRelease_IsOfferedToSomeoneOnAPreRelease()
    {
        var info = Build("0.2.0-beta.1").Evaluate(Release("v0.2.0"));

        Assert.NotNull(info);
        Assert.Equal("0.2.0", info!.Version.ToString());
    }

    [Fact]
    public void Evaluate_SkippedVersion_IsNotOfferedAgain()
    {
        _settings.SkippedVersion = "0.2.0";

        Assert.Null(Build("0.1.0").Evaluate(Release("v0.2.0")));
    }

    [Fact]
    public void Evaluate_SomethingNewerThanTheSkippedVersion_IsStillOffered()
    {
        _settings.SkippedVersion = "0.2.0";

        Assert.NotNull(Build("0.1.0").Evaluate(Release("v0.3.0")));
    }

    [Fact]
    public void Evaluate_MissingUrl_FallsBackToTheReleasesPage()
    {
        var info = Build("0.1.0").Evaluate(new GitHubReleaseDto { TagName = "v0.2.0" });

        Assert.NotNull(info);
        Assert.Equal("https://github.com/emirakinc/SonarTray/releases/latest", info!.ReleaseUrl);
    }

    [Fact]
    public void Dto_MatchesTheShapeGitHubActuallyReturns()
    {
        // Trimmed from a real api.github.com/releases/latest response. If GitHub renames a field,
        // or someone "tidies" the JsonPropertyName attributes, the check would silently stop
        // finding releases - this catches that instead.
        const string Payload = """
        {
          "url": "https://api.github.com/repos/emirakinc/SonarTray/releases/387707736",
          "html_url": "https://github.com/emirakinc/SonarTray/releases/tag/v0.1.0",
          "id": 387707736,
          "tag_name": "v0.1.0",
          "name": "SonarTray v0.1.0",
          "draft": false,
          "prerelease": false
        }
        """;

        var release = System.Text.Json.JsonSerializer.Deserialize<GitHubReleaseDto>(Payload);

        Assert.NotNull(release);
        Assert.Equal("v0.1.0", release!.TagName);
        Assert.Equal("https://github.com/emirakinc/SonarTray/releases/tag/v0.1.0", release.HtmlUrl);
        Assert.False(release.PreRelease);
        Assert.False(release.Draft);
    }

    [Fact]
    public void Evaluate_TheCurrentlyPublishedRelease_IsNotOfferedToTheMatchingBuild()
    {
        // v0.1.0 is what is on GitHub today and 0.1.0 is what this builds; offering it would be
        // an endless "update available" badge.
        Assert.Null(Build("0.1.0").Evaluate(Release("v0.1.0")));
    }

    // ---- IsDue -----------------------------------------------------------

    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsDue_NeverCheckedBefore_IsDue()
    {
        Assert.True(Build().IsDue(Now));
    }

    [Fact]
    public void IsDue_CheckedAnHourAgo_IsNotDue()
    {
        _settings.LastUpdateCheckUtc = Now.AddHours(-1);

        Assert.False(Build().IsDue(Now));
    }

    [Fact]
    public void IsDue_CheckedTwoDaysAgo_IsDue()
    {
        _settings.LastUpdateCheckUtc = Now.AddDays(-2);

        Assert.True(Build().IsDue(Now));
    }

    [Fact]
    public void IsDue_ATimestampInTheFuture_IsDue()
    {
        // A clock change or a hand-edited file must not switch the check off permanently.
        _settings.LastUpdateCheckUtc = Now.AddYears(1);

        Assert.True(Build().IsDue(Now));
    }

    // ---- the off switch --------------------------------------------------

    [Fact]
    public async Task CheckAsync_WhenSwitchedOff_MakesNoRequestAndReturnsNull()
    {
        _settings.CheckForUpdates = false;
        _settings.LastUpdateCheckUtc = null;

        Assert.Null(await Build().CheckAsync(CancellationToken.None));

        // Nothing was attempted, so nothing was recorded either.
        Assert.Null(_settings.LastUpdateCheckUtc);
    }
}
