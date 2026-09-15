using SonarTray.Models;
using SonarTray.Services;
using SonarTray.ViewModels;
using Xunit;

namespace SonarTray.Tests;

/// <summary>
/// Covers the profile half of <see cref="ChannelViewModel"/>. Constructing one needs a Dispatcher
/// (it owns a DispatcherTimer) and a SolidColorBrush, both of which work on a plain thread, so no
/// STA apartment is required here.
/// </summary>
public sealed class ChannelProfileTests
{
    private static ChannelViewModel Channel(ChannelKind kind)
    {
        var spec = ChannelSpec.All.Single(s => s.Kind == kind);
        // The connection is never used by the paths under test: they all bail out when Client is
        // null, which it is until the background loop connects.
        return new ChannelViewModel(spec, new SonarConnection());
    }

    private static ConfigDto Config(string id, string name, string device)
        => new() { Id = id, Name = name, VirtualAudioDevice = device };

    [Fact]
    public void Master_HasNoProfilePicker()
    {
        // Sonar's profiles belong to virtual devices; master is a mix and has none.
        Assert.False(Channel(ChannelKind.Master).HasProfilePicker);
    }

    [Theory]
    [InlineData(ChannelKind.Game)]
    [InlineData(ChannelKind.Chat)]
    [InlineData(ChannelKind.Media)]
    [InlineData(ChannelKind.Mic)]
    [InlineData(ChannelKind.Aux)]
    public void EveryOtherChannel_HasAProfilePicker(ChannelKind kind)
    {
        Assert.True(Channel(kind).HasProfilePicker);
    }

    [Fact]
    public void ApplySelectedProfile_SetsTheSelection()
    {
        var channel = Channel(ChannelKind.Game);

        channel.ApplySelectedProfile(Config("abc", "Custom", "game"));

        Assert.Equal("abc", channel.SelectedProfileId);
    }

    [Fact]
    public void ApplySelectedProfile_AddsTheProfileSoTheMenuCanShowIt()
    {
        // The full list is not fetched until the menu is opened, so the selected entry has to be
        // put in the collection or the ComboBox would render an empty selection.
        var channel = Channel(ChannelKind.Media);

        channel.ApplySelectedProfile(Config("xyz", "Music: Bright", "media"));

        Assert.Contains(channel.Profiles, p => p.Id == "xyz" && p.Name == "Music: Bright");
    }

    [Fact]
    public void ApplySelectedProfile_DoesNotDuplicateAnExistingEntry()
    {
        var channel = Channel(ChannelKind.Media);
        channel.ApplySelectedProfile(Config("xyz", "Music: Bright", "media"));

        channel.ApplySelectedProfile(Config("xyz", "Music: Bright", "media"));

        Assert.Single(channel.Profiles);
    }

    [Fact]
    public void ApplySelectedProfile_Null_ChangesNothing()
    {
        var channel = Channel(ChannelKind.Game);
        channel.ApplySelectedProfile(Config("abc", "Custom", "game"));

        channel.ApplySelectedProfile(null);

        Assert.Equal("abc", channel.SelectedProfileId);
        Assert.Single(channel.Profiles);
    }

    [Fact]
    public void ApplySelectedProfile_OnMaster_IsIgnored()
    {
        var master = Channel(ChannelKind.Master);

        master.ApplySelectedProfile(Config("abc", "Custom", "master"));

        Assert.Empty(master.Profiles);
        Assert.Null(master.SelectedProfileId);
    }

    [Fact]
    public void SelectedProfileId_IgnoresTheTransientNullFromTheComboBox()
    {
        // WPF pushes null while the item source changes; treating that as a user choice would
        // fire a write with no id.
        var channel = Channel(ChannelKind.Game);
        channel.ApplySelectedProfile(Config("abc", "Custom", "game"));

        channel.SelectedProfileId = null;

        Assert.Equal("abc", channel.SelectedProfileId);
    }

    [Fact]
    public void ApplySelectedProfile_ChangingProfileInGg_IsReflected()
    {
        var channel = Channel(ChannelKind.Game);
        channel.ApplySelectedProfile(Config("first", "Custom", "game"));

        channel.ApplySelectedProfile(Config("second", "Footsteps", "game"));

        Assert.Equal("second", channel.SelectedProfileId);
        Assert.Equal(2, channel.Profiles.Count);
    }

    [Fact]
    public async Task LoadProfilesAsync_WithoutAConnection_DoesNothing()
    {
        var channel = Channel(ChannelKind.Game);

        await channel.LoadProfilesAsync();

        Assert.Empty(channel.Profiles);
    }

    [Fact]
    public async Task LoadProfilesAsync_OnMaster_DoesNothing()
    {
        var master = Channel(ChannelKind.Master);

        await master.LoadProfilesAsync();

        Assert.Empty(master.Profiles);
    }

    [Fact]
    public void ProfileDeviceNames_MatchTheChannelVolumeIds()
    {
        // Sonar keys profiles by virtualAudioDevice, whose values are exactly the volume ids.
        // Verified live: game, chatRender, chatCapture, media, aux.
        var expected = new[] { "game", "chatRender", "chatCapture", "media", "aux" };

        var actual = ChannelSpec.All
                                .Where(s => s.Kind != ChannelKind.Master)
                                .Select(s => s.VolumeId)
                                .ToList();

        Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
    }
}
