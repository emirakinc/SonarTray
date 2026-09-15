using SonarTray.Hotkeys;
using Xunit;

namespace SonarTray.Tests;

public sealed class HotkeyConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* the test already passed or failed; cleanup noise is not interesting */ }
    }

    [Fact]
    public void Load_MissingFile_WritesDefaultsToDisk()
    {
        var path = PathFor("hotkeys.json");

        var config = HotkeyConfig.Load(path);

        Assert.True(File.Exists(path));
        Assert.Equal("Ctrl+Shift+F9", config.MicMute);
        Assert.Equal(0.05, config.VolumeStep);
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaults()
    {
        var path = PathFor("hotkeys.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ this is not json");

        var config = HotkeyConfig.Load(path);

        Assert.Equal(new HotkeyConfig().TogglePanel, config.TogglePanel);
    }

    [Fact]
    public void Load_EmptyJsonDocument_FallsBackToDefaults()
    {
        var path = PathFor("hotkeys.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "null");

        var config = HotkeyConfig.Load(path);

        Assert.Equal(new HotkeyConfig().MasterMute, config.MasterMute);
    }

    [Theory]
    [InlineData(0.0, 0.01)]     // below the floor
    [InlineData(-5.0, 0.01)]
    [InlineData(0.9, 0.5)]      // above the ceiling
    [InlineData(0.05, 0.05)]    // untouched inside the range
    public void Load_ClampsVolumeStep(double stored, double expected)
    {
        var path = PathFor("hotkeys.json");
        Directory.CreateDirectory(_dir);
        var literal = stored.ToString(System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(path, $$"""{"volumeStep": {{literal}}}""");

        Assert.Equal(expected, HotkeyConfig.Load(path).VolumeStep, precision: 6);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = PathFor("hotkeys.json");
        var saved = new HotkeyConfig { MicMute = "Ctrl+Alt+K", VolumeStep = 0.2 };

        saved.Save(path);
        var loaded = HotkeyConfig.Load(path);

        Assert.Equal("Ctrl+Alt+K", loaded.MicMute);
        Assert.Equal(0.2, loaded.VolumeStep, precision: 6);
    }

    [Fact]
    public void Save_WritesPlusSignsUnescaped()
    {
        // The whole point of the file is that a human can edit it; "+" would defeat that.
        var path = PathFor("hotkeys.json");
        new HotkeyConfig().Save(path);

        Assert.Contains("Ctrl+Shift+F9", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WithoutAPath_WritesBackToWhereItWasLoadedFrom()
    {
        var path = PathFor("hotkeys.json");
        var config = HotkeyConfig.Load(path);

        config.VolumeStep = 0.2;
        config.Save();

        Assert.Equal(0.2, HotkeyConfig.Load(path).VolumeStep, precision: 6);
        Assert.Equal(path, config.SourcePath);
    }

    [Fact]
    public void GetAndSet_AgreeForEveryAction()
    {
        var config = new HotkeyConfig();

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            config.Set(action, $"Ctrl+Shift+{action}");
            Assert.Equal($"Ctrl+Shift+{action}", config.Get(action));
        }
    }

    [Fact]
    public void Bindings_CoverEveryAction()
    {
        // A new HotkeyAction that nobody added to Bindings would silently never register.
        var bound = new HotkeyConfig().Bindings.Select(b => b.Action).ToHashSet();

        Assert.Equal(Enum.GetValues<HotkeyAction>().ToHashSet(), bound);
    }

    [Fact]
    public void Get_CoversEveryAction()
    {
        // Get() ends in a `_ => TogglePanel` catch-all, so a new action would quietly alias it.
        var config = new HotkeyConfig();
        foreach (var action in Enum.GetValues<HotkeyAction>()) config.Set(action, action.ToString());

        foreach (var action in Enum.GetValues<HotkeyAction>())
            Assert.Equal(action.ToString(), config.Get(action));
    }
}
