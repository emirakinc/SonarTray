using SonarTray.Models;
using Xunit;

namespace SonarTray.Tests;

public sealed class ChannelSpecTests
{
    [Fact]
    public void All_CoversEveryChannelKind()
    {
        var kinds = ChannelSpec.All.Select(s => s.Kind).ToHashSet();

        Assert.Equal(Enum.GetValues<ChannelKind>().ToHashSet(), kinds);
    }

    [Fact]
    public void All_HasNoDuplicateKinds()
    {
        Assert.Equal(ChannelSpec.All.Count, ChannelSpec.All.Select(s => s.Kind).Distinct().Count());
    }

    [Fact]
    public void VolumeIds_AreUnique()
    {
        var ids = ChannelSpec.All.Select(s => s.VolumeId).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Master_IsTheOnlyChannelWithoutARedirection()
    {
        // Master is a mix, not a device, so it has no redirection id and no device picker.
        var withoutRedirection = ChannelSpec.All.Where(s => s.RedirectionId is null).ToList();

        Assert.Single(withoutRedirection);
        Assert.Equal(ChannelKind.Master, withoutRedirection[0].Kind);
    }

    [Fact]
    public void RedirectionAndDataFlow_TravelTogether()
    {
        // A picker needs both: the id to write to, and the flow that decides which device list to show.
        foreach (var spec in ChannelSpec.All)
            Assert.Equal(spec.RedirectionId is null, spec.DataFlow is null);
    }

    [Theory]
    [InlineData(ChannelKind.Chat, "chatRender", "chat")]
    [InlineData(ChannelKind.Mic, "chatCapture", "mic")]
    [InlineData(ChannelKind.Game, "game", "game")]
    public void VolumeIdAndRedirectionId_MatchTheApi(ChannelKind kind, string volumeId, string redirectionId)
    {
        // Sonar names these differently on purpose; getting them backwards writes to the wrong channel.
        var spec = ChannelSpec.All.Single(s => s.Kind == kind);

        Assert.Equal(volumeId, spec.VolumeId);
        Assert.Equal(redirectionId, spec.RedirectionId);
    }

    [Fact]
    public void Mic_IsTheOnlyCaptureChannel()
    {
        var capture = ChannelSpec.All.Where(s => s.DataFlow == "capture").ToList();

        Assert.Single(capture);
        Assert.Equal(ChannelKind.Mic, capture[0].Kind);
    }

    [Fact]
    public void DataFlow_IsOnlyEverRenderOrCapture()
    {
        foreach (var spec in ChannelSpec.All)
            Assert.True(spec.DataFlow is null or "render" or "capture", $"{spec.Kind} has DataFlow '{spec.DataFlow}'");
    }

    [Fact]
    public void VisibleSpecs_IncludeMaster()
    {
        // MixerViewModel does Channels.First(Kind == Master) and would throw if this ever stopped holding.
        Assert.Contains(ChannelSpec.VisibleSpecs, s => s.Kind == ChannelKind.Master);
    }

    [Fact]
    public void AccentColours_AreParseableHex()
    {
        foreach (var spec in ChannelSpec.All)
        {
            var colour = System.Windows.Media.ColorConverter.ConvertFromString(spec.AccentHex);
            Assert.NotNull(colour);
        }
    }
}
