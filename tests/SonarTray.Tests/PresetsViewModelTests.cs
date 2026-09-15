using SonarTray.Models;
using SonarTray.Services;
using SonarTray.ViewModels;
using Xunit;

namespace SonarTray.Tests;

public sealed class PresetsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));
    private readonly PresetStore _store;
    private readonly SonarConnection _connection = new();
    private readonly List<ChannelViewModel> _channels;

    public PresetsViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _store = PresetStore.Load(Path.Combine(_dir, "presets.json"));
        _channels = ChannelSpec.All.Select(spec => new ChannelViewModel(spec, _connection)).ToList();
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup noise is not interesting */ }
    }

    private PresetsViewModel Build() => new(_store, () => _channels);

    private ChannelViewModel Channel(ChannelKind kind) => _channels.Single(c => c.Spec.Kind == kind);

    [Fact]
    public void CanSave_IsFalseUntilANameIsTyped()
    {
        var vm = Build();

        Assert.False(vm.CanSave);

        vm.NewName = "Gaming";

        Assert.True(vm.CanSave);
    }

    [Fact]
    public void CanSave_IsFalseForWhitespaceOnly()
    {
        var vm = Build();
        vm.NewName = "   ";

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void SaveCurrent_CapturesEveryChannel()
    {
        Channel(ChannelKind.Master).Volume = 0.42;
        Channel(ChannelKind.Game).IsMuted = true;

        var vm = Build();
        vm.NewName = "Gaming";
        vm.SaveCommand.Execute(null);

        var preset = Assert.Single(vm.Presets);
        Assert.Equal("Gaming", preset.Name);
        Assert.Equal(0.42, preset.For("master")!.Volume, precision: 3);
        Assert.True(preset.For("game")!.Muted);
        // Every channel, including the ones the user currently has hidden.
        Assert.Equal(ChannelSpec.All.Count, preset.Channels.Count);
    }

    [Fact]
    public void SaveCurrent_ClearsTheNameBox()
    {
        var vm = Build();
        vm.NewName = "Gaming";

        vm.SaveCommand.Execute(null);

        Assert.Equal("", vm.NewName);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void Apply_RestoresVolumeAndMute()
    {
        Channel(ChannelKind.Master).Volume = 0.9;
        Channel(ChannelKind.Game).IsMuted = true;

        var vm = Build();
        vm.NewName = "Saved";
        vm.SaveCommand.Execute(null);

        // Move everything away from the saved state.
        Channel(ChannelKind.Master).Volume = 0.1;
        Channel(ChannelKind.Game).IsMuted = false;

        vm.ApplyCommand.Execute(vm.Presets[0]);

        Assert.Equal(0.9, Channel(ChannelKind.Master).Volume, precision: 3);
        Assert.True(Channel(ChannelKind.Game).IsMuted);
    }

    [Fact]
    public void Apply_APresetMissingAChannel_LeavesThatChannelAlone()
    {
        // A preset saved by an older build will not mention channels added since.
        var partial = MixerPreset.Capture("Partial", new[] { ("master", 0.3, false, (string?)null) });
        _store.Save(partial);

        Channel(ChannelKind.Game).Volume = 0.7;
        var vm = Build();

        vm.ApplyCommand.Execute(vm.Presets.Single(p => p.Name == "Partial"));

        Assert.Equal(0.3, Channel(ChannelKind.Master).Volume, precision: 3);
        Assert.Equal(0.7, Channel(ChannelKind.Game).Volume, precision: 3);
    }

    [Fact]
    public void Delete_RemovesItFromTheListAndTheStore()
    {
        var vm = Build();
        vm.NewName = "Gaming";
        vm.SaveCommand.Execute(null);

        vm.DeleteCommand.Execute(vm.Presets[0]);

        Assert.Empty(vm.Presets);
        Assert.Empty(PresetStore.Load(Path.Combine(_dir, "presets.json")).Presets);
    }

    [Fact]
    public void HasPresets_TracksTheList()
    {
        var vm = Build();
        Assert.False(vm.HasPresets);

        vm.NewName = "Gaming";
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasPresets);
    }

    [Fact]
    public void AtTheLimit_ANewNameCannotBeSavedButAnExistingOneCan()
    {
        for (int i = 0; i < PresetStore.MaxPresets; i++)
            _store.Save(MixerPreset.Capture($"preset {i}", Array.Empty<(string, double, bool, string?)>()));

        var vm = Build();

        vm.NewName = "something new";
        Assert.False(vm.CanSave);
        Assert.True(vm.IsFull);

        vm.NewName = "preset 0";
        Assert.True(vm.CanSave);
        Assert.False(vm.IsFull);
    }

    [Fact]
    public void PresetsList_StaysSortedAfterSaving()
    {
        var vm = Build();
        foreach (var name in new[] { "Zebra", "Apple", "Middle" })
        {
            vm.NewName = name;
            vm.SaveCommand.Execute(null);
        }

        Assert.Equal(new[] { "Apple", "Middle", "Zebra" }, vm.Presets.Select(p => p.Name));
    }
}
