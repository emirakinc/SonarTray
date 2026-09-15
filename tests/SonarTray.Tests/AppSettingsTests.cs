using System.Globalization;
using SonarTray.Services;
using Xunit;

namespace SonarTray.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name = "settings.json") => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup noise is not interesting */ }
    }

    [Fact]
    public void Load_MissingFile_WritesDefaults()
    {
        var path = PathFor();

        var settings = AppSettings.Load(path);

        Assert.True(File.Exists(path));
        Assert.Equal("auto", settings.Language);
        Assert.True(settings.OsdEnabled);
        Assert.False(settings.ShowAux);
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaults()
    {
        var path = PathFor();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "not json at all");

        Assert.Equal("auto", AppSettings.Load(path).Language);
    }

    [Theory]
    [InlineData(0, 300)]        // below the floor
    [InlineData(-1000, 300)]
    [InlineData(99_999, 10_000)] // above the ceiling
    [InlineData(1500, 1500)]     // untouched inside the range
    public void Load_ClampsOsdDuration(int stored, int expected)
    {
        var path = PathFor();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, $$"""{"osdDurationMs": {{stored}}}""");

        Assert.Equal(expected, AppSettings.Load(path).OsdDurationMs);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = PathFor();
        var saved = new AppSettings { Language = "tr", ShowAux = true, OsdEnabled = false, TrayWheelVolume = false };

        saved.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal("tr", loaded.Language);
        Assert.True(loaded.ShowAux);
        Assert.False(loaded.OsdEnabled);
        Assert.False(loaded.TrayWheelVolume);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveCulture_AutoMeansFollowTheSystem(string language)
    {
        Assert.Null(new AppSettings { Language = language }.ResolveCulture());
    }

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("en", "en")]
    [InlineData("tr-TR", "tr-TR")]
    public void ResolveCulture_ReturnsTheNamedCulture(string language, string expected)
    {
        Assert.Equal(expected, new AppSettings { Language = language }.ResolveCulture()?.Name);
    }

    [Fact]
    public void ResolveCulture_UnknownLanguage_FallsBackToTheSystem()
    {
        // A hand-edited settings file must not be able to stop the app from starting.
        Assert.Null(new AppSettings { Language = "not-a-culture" }.ResolveCulture());
    }

    [Fact]
    public void ResolveCulture_AcceptsEverySupportedLanguage()
    {
        foreach (var language in SonarTray.Resources.Strings.SupportedLanguages)
        {
            var culture = new AppSettings { Language = language }.ResolveCulture();
            Assert.NotNull(culture);
            Assert.Equal(language, culture!.TwoLetterISOLanguageName);
        }
    }

    [Fact]
    public void Save_WithoutAPath_WritesBackToWhereItWasLoadedFrom()
    {
        // The parameterless Save() used to always target the one shared user path, so any code
        // holding a throwaway instance - the settings page under test, for one - quietly
        // overwrote the real %LOCALAPPDATA% configuration.
        var path = PathFor();
        var settings = AppSettings.Load(path);

        settings.ShowAux = true;
        settings.Save();

        Assert.True(AppSettings.Load(path).ShowAux);
        Assert.Equal(path, settings.SourcePath);
    }

    [Fact]
    public void Load_RecordsTheSourcePathEvenForABrokenFile()
    {
        var path = PathFor();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ broken");

        Assert.Equal(path, AppSettings.Load(path).SourcePath);
    }

    [Fact]
    public void Save_IsReadableByAHuman()
    {
        // The file exists so it can be hand-edited; escaped or minified output would defeat that.
        var path = PathFor();
        new AppSettings { Language = "tr" }.Save(path);

        var text = File.ReadAllText(path);
        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.Contains("\"tr\"", text, StringComparison.Ordinal);
    }
}
