using SonarTray.Models;
using SonarTray.Services;
using Xunit;

namespace SonarTray.Tests;

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));

    private string PathFor() => Path.Combine(_dir, "presets.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup noise is not interesting */ }
    }

    private static MixerPreset Preset(string name, double masterVolume = 0.5)
        => MixerPreset.Capture(name, new[] { ("master", masterVolume, false, (string?)null) });

    // ---- Normalise -------------------------------------------------------

    [Theory]
    [InlineData("Gaming", "Gaming")]
    [InlineData("  Gaming  ", "Gaming")]
    public void Normalise_TrimsWhitespace(string input, string expected)
    {
        Assert.Equal(expected, PresetStore.Normalise(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Normalise_RejectsAnEmptyName(string? input)
    {
        // A nameless preset could never be picked out of the menu again.
        Assert.Null(PresetStore.Normalise(input));
    }

    [Fact]
    public void Normalise_TruncatesAVeryLongName()
    {
        var name = PresetStore.Normalise(new string('x', 200));

        Assert.NotNull(name);
        Assert.Equal(PresetStore.MaxNameLength, name!.Length);
    }

    // ---- save / find / delete --------------------------------------------

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = PresetStore.Load(PathFor());
        store.Save(MixerPreset.Capture("Gaming", new[]
        {
            ("master", 0.8, false, (string?)"dev-1"),
            ("game", 0.6, true, (string?)null),
        }));

        var reloaded = PresetStore.Load(PathFor());

        var preset = Assert.Single(reloaded.Presets);
        Assert.Equal("Gaming", preset.Name);
        Assert.Equal(0.8, preset.For("master")!.Volume, precision: 6);
        Assert.Equal("dev-1", preset.For("master")!.DeviceId);
        Assert.True(preset.For("game")!.Muted);
    }

    [Fact]
    public void Save_WithTheSameName_ReplacesRatherThanDuplicating()
    {
        var store = PresetStore.Load(PathFor());
        store.Save(Preset("Gaming", 0.2));

        store.Save(Preset("Gaming", 0.9));

        var preset = Assert.Single(store.Presets);
        Assert.Equal(0.9, preset.For("master")!.Volume, precision: 6);
    }

    [Fact]
    public void Save_MatchesNameCaseInsensitively()
    {
        var store = PresetStore.Load(PathFor());
        store.Save(Preset("Gaming"));

        store.Save(Preset("gaming"));

        Assert.Single(store.Presets);
    }

    [Fact]
    public void Save_RejectsAnEmptyName()
    {
        var store = PresetStore.Load(PathFor());

        Assert.False(store.Save(Preset("   ")));
        Assert.Empty(store.Presets);
    }

    [Fact]
    public void Save_StopsAtTheLimit()
    {
        var store = PresetStore.Load(PathFor());
        for (int i = 0; i < PresetStore.MaxPresets; i++) Assert.True(store.Save(Preset($"preset {i}")));

        Assert.False(store.Save(Preset("one too many")));
        Assert.Equal(PresetStore.MaxPresets, store.Presets.Count);
    }

    [Fact]
    public void Save_AtTheLimit_CanStillOverwriteAnExistingPreset()
    {
        // Being full must not stop someone re-saving a preset they already have.
        var store = PresetStore.Load(PathFor());
        for (int i = 0; i < PresetStore.MaxPresets; i++) store.Save(Preset($"preset {i}"));

        Assert.True(store.Save(Preset("preset 0", 0.99)));
        Assert.Equal(0.99, store.Find("preset 0")!.For("master")!.Volume, precision: 6);
    }

    [Fact]
    public void Presets_AreSortedByName()
    {
        var store = PresetStore.Load(PathFor());
        store.Save(Preset("Zebra"));
        store.Save(Preset("Apple"));
        store.Save(Preset("Middle"));

        Assert.Equal(new[] { "Apple", "Middle", "Zebra" }, store.Presets.Select(p => p.Name));
    }

    [Fact]
    public void Delete_RemovesAndPersists()
    {
        var store = PresetStore.Load(PathFor());
        store.Save(Preset("Gaming"));
        store.Save(Preset("Music"));

        Assert.True(store.Delete("Gaming"));

        Assert.Single(PresetStore.Load(PathFor()).Presets);
    }

    [Fact]
    public void Delete_SomethingThatIsNotThere_ReturnsFalse()
    {
        Assert.False(PresetStore.Load(PathFor()).Delete("nope"));
    }

    // ---- loading a broken or hostile file ---------------------------------

    [Fact]
    public void Load_MissingFile_StartsEmptyAndWritesNothing()
    {
        var store = PresetStore.Load(PathFor());

        Assert.Empty(store.Presets);
        Assert.False(File.Exists(PathFor()));
    }

    [Fact]
    public void Load_CorruptJson_StartsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(), "[ this is not json");

        Assert.Empty(PresetStore.Load(PathFor()).Presets);
    }

    [Fact]
    public void Load_DropsNamelessEntries()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(), """[{"name":"","channels":[]},{"name":"Good","channels":[]}]""");

        var preset = Assert.Single(PresetStore.Load(PathFor()).Presets);
        Assert.Equal("Good", preset.Name);
    }

    [Fact]
    public void Load_DropsDuplicateNames()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(), """[{"name":"Same","channels":[]},{"name":"same","channels":[]}]""");

        Assert.Single(PresetStore.Load(PathFor()).Presets);
    }

    [Fact]
    public void Load_EnforcesTheLimitOnAHandEditedFile()
    {
        Directory.CreateDirectory(_dir);
        var entries = string.Join(",", Enumerable.Range(0, 100).Select(i => $$"""{"name":"p{{i}}","channels":[]}"""));
        File.WriteAllText(PathFor(), $"[{entries}]");

        Assert.Equal(PresetStore.MaxPresets, PresetStore.Load(PathFor()).Presets.Count);
    }

    // ---- Capture ----------------------------------------------------------

    [Fact]
    public void Capture_ClampsVolumesIntoRange()
    {
        var preset = MixerPreset.Capture("x", new[] { ("master", 5.0, false, (string?)null), ("game", -3.0, false, (string?)null) });

        Assert.Equal(1.0, preset.For("master")!.Volume, precision: 6);
        Assert.Equal(0.0, preset.For("game")!.Volume, precision: 6);
    }

    [Fact]
    public void Capture_TrimsTheName()
    {
        Assert.Equal("Gaming", MixerPreset.Capture("  Gaming ", Array.Empty<(string, double, bool, string?)>()).Name);
    }

    [Fact]
    public void For_UnknownChannel_ReturnsNull()
    {
        // A preset saved before a channel existed must not break when it is applied.
        Assert.Null(Preset("x").For("aux"));
    }

    [Fact]
    public void HasDevice_IsFalseForBlankIds()
    {
        Assert.False(new PresetChannel { DeviceId = null }.HasDevice);
        Assert.False(new PresetChannel { DeviceId = "  " }.HasDevice);
        Assert.True(new PresetChannel { DeviceId = "dev-1" }.HasDevice);
    }
}
