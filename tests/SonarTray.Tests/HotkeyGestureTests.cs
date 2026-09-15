using System.Windows.Input;
using SonarTray.Hotkeys;
using SonarTray.Native;
using Xunit;

namespace SonarTray.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F9", NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, 0x78)]  // VK_F9
    [InlineData("Alt+F4", NativeMethods.MOD_ALT, 0x73)]                                        // VK_F4
    [InlineData("Ctrl+Alt+Shift+A", NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, 0x41)]
    [InlineData("Windows+L", NativeMethods.MOD_WIN, 0x4C)]
    public void TryParse_AcceptsFamiliarSpellings(string gesture, uint expectedModifiers, uint expectedVk)
    {
        Assert.True(HotkeyManager.TryParse(gesture, out var modifiers, out var vk));
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData("")]                 // an empty string means "this action is disabled"
    [InlineData("   ")]
    [InlineData("F9")]               // no modifier: RegisterHotKey would swallow a bare key globally
    [InlineData("Ctrl+")]
    [InlineData("Banana+Q")]
    [InlineData("Ctrl+Shift+NotAKey")]
    public void TryParse_RejectsUnusableGestures(string gesture)
    {
        Assert.False(HotkeyManager.TryParse(gesture, out _, out _));
    }

    [Theory]
    [InlineData("Ctrl+Shift")]    // parses as Key=LeftShift + Modifiers=Control
    [InlineData("Shift+Ctrl")]    // parses as Key=LeftCtrl  + Modifiers=Shift
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Shift+LWin")]
    public void TryParse_RejectsModifierOnly(string gesture)
    {
        // KeyGestureConverter splits on the last '+', so these arrive looking like valid gestures.
        // RegisterHotKey would accept them and then fire on the chord prefix, swallowing every
        // Ctrl+Shift+X shortcut on the machine.
        Assert.False(HotkeyManager.TryParse(gesture, out _, out _));
    }

    [Fact]
    public void IsModifierKey_CoversBothSidesOfEveryModifier()
    {
        foreach (var key in new[] { Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift,
                                    Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin, Key.System, Key.None })
            Assert.True(HotkeyManager.IsModifierKey(key), $"{key} should count as a modifier");
    }

    [Fact]
    public void IsModifierKey_LetsRealTriggerKeysThrough()
    {
        foreach (var key in new[] { Key.A, Key.F9, Key.D1, Key.Space, Key.OemComma, Key.NumPad5 })
            Assert.False(HotkeyManager.IsModifierKey(key), $"{key} should be usable as a trigger");
    }

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.F9)]
    [InlineData(ModifierKeys.Alt, Key.F4)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.A)]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Control, Key.D1)]
    public void Format_ProducesSomethingTryParseAccepts(ModifierKeys modifiers, Key key)
    {
        // The settings page captures a keystroke, calls Format, stores the string, and TryParse
        // reads it back on the next start. A mismatch here silently drops the user's binding.
        var formatted = HotkeyManager.Format(modifiers, key);

        Assert.NotEqual(string.Empty, formatted);
        Assert.True(HotkeyManager.TryParse(formatted, out _, out _), $"round-trip failed for '{formatted}'");
    }

    [Fact]
    public void Format_RoundTripsToTheSameVirtualKey()
    {
        var formatted = HotkeyManager.Format(ModifierKeys.Control | ModifierKeys.Shift, Key.F9);

        Assert.True(HotkeyManager.TryParse(formatted, out var modifiers, out var vk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, modifiers);
        Assert.Equal((uint)0x78, vk);
    }

    [Fact]
    public void Format_WithoutModifiers_IsNotAcceptedBack()
    {
        // KeyGesture itself rejects most unmodified keys; whatever comes out must not parse,
        // otherwise the settings page could store a binding that hijacks a bare keypress.
        Assert.False(HotkeyManager.TryParse(HotkeyManager.Format(ModifierKeys.None, Key.A), out _, out _));
    }

    [Fact]
    public void DefaultBindings_AllParse()
    {
        // Per-channel actions ship unbound; an empty gesture means "disabled", not "broken".
        foreach (var (action, gesture) in new HotkeyConfig().Bindings.Where(b => b.Gesture.Length > 0))
            Assert.True(HotkeyManager.TryParse(gesture, out _, out _), $"default for {action} ('{gesture}') does not parse");
    }

    [Fact]
    public void DefaultBindings_AreUnique()
    {
        // Two actions on one combination would mean whichever registered second never fires.
        var gestures = new HotkeyConfig().Bindings
                                         .Select(b => b.Gesture)
                                         .Where(g => g.Length > 0)
                                         .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DefaultBindings_LeaveThePerChannelActionsUnbound()
    {
        var config = new HotkeyConfig();

        foreach (var action in new[] { HotkeyAction.GameUp, HotkeyAction.GameDown, HotkeyAction.GameMute,
                                       HotkeyAction.ChatUp, HotkeyAction.ChatDown, HotkeyAction.ChatMute,
                                       HotkeyAction.MediaUp, HotkeyAction.MediaDown, HotkeyAction.MediaMute })
            Assert.Equal(string.Empty, config.Get(action));
    }
}
