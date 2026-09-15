using SonarTray.Hotkeys;
using SonarTray.Models;
using Xunit;

namespace SonarTray.Tests;

public sealed class HotkeyActionMappingTests
{
    [Fact]
    public void EveryActionExceptTogglePanel_MapsToAChannel()
    {
        // An unmapped action registers a global hotkey that then does nothing at all - the worst
        // kind of bug here, because the key is taken from every other app and gives nothing back.
        var expected = Enum.GetValues<HotkeyAction>()
                           .Where(a => a != HotkeyAction.TogglePanel)
                           .ToHashSet();

        Assert.Equal(expected, App.ChannelActions.Keys.ToHashSet());
    }

    [Fact]
    public void TogglePanel_IsNotAChannelAction()
    {
        Assert.DoesNotContain(HotkeyAction.TogglePanel, App.ChannelActions.Keys);
    }

    [Fact]
    public void EveryMappedChannel_ExistsInTheSpecTable()
    {
        foreach (var (action, target) in App.ChannelActions)
            Assert.Contains(ChannelSpec.All, s => s.Kind == target.Kind);
    }

    [Fact]
    public void ActionsPointAtTheChannelTheirNameClaims()
    {
        // Wiring GameUp to the chat channel would be invisible until someone used it in a game.
        // App.ChannelOp is internal, so these cannot be [Theory] parameters.
        var expected = new (HotkeyAction Action, ChannelKind Kind, App.ChannelOp Op)[]
        {
            (HotkeyAction.MasterUp, ChannelKind.Master, App.ChannelOp.Up),
            (HotkeyAction.MasterDown, ChannelKind.Master, App.ChannelOp.Down),
            (HotkeyAction.MicMute, ChannelKind.Mic, App.ChannelOp.ToggleMute),
            (HotkeyAction.GameUp, ChannelKind.Game, App.ChannelOp.Up),
            (HotkeyAction.ChatMute, ChannelKind.Chat, App.ChannelOp.ToggleMute),
            (HotkeyAction.MediaDown, ChannelKind.Media, App.ChannelOp.Down),
        };

        foreach (var (action, kind, op) in expected)
            Assert.Equal((kind, op), App.ChannelActions[action]);
    }

    [Fact]
    public void MuteAndVolumeActions_ExistForEveryPerChannelName()
    {
        foreach (var kind in new[] { ChannelKind.Game, ChannelKind.Chat, ChannelKind.Media })
        {
            var ops = App.ChannelActions.Values.Where(v => v.Kind == kind).Select(v => v.Op).ToHashSet();
            Assert.Equal(Enum.GetValues<App.ChannelOp>().ToHashSet(), ops);
        }
    }
}
