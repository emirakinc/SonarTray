using SonarTray.Tray;
using Xunit;

namespace SonarTray.Tests;

public sealed class TrayWheelHookTests
{
    [Fact]
    public void Install_Succeeds()
    {
        // Mostly a check on the P/Invoke signatures: a wrong delegate shape or a bad marshalling
        // attribute shows up here as SetWindowsHookEx returning null, rather than as a wheel that
        // quietly does nothing on the user's machine.
        using var hook = new TrayWheelHook((_, _) => false, _ => { });

        hook.Install();

        Assert.True(hook.IsInstalled);
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        using var hook = new TrayWheelHook((_, _) => false, _ => { });

        hook.Install();
        hook.Install();

        Assert.True(hook.IsInstalled);
    }

    [Fact]
    public void Uninstall_ReleasesTheHook()
    {
        using var hook = new TrayWheelHook((_, _) => false, _ => { });
        hook.Install();

        hook.Uninstall();

        Assert.False(hook.IsInstalled);
    }

    [Fact]
    public void CanBeReinstalled()
    {
        // The settings toggle installs and removes it repeatedly.
        using var hook = new TrayWheelHook((_, _) => false, _ => { });

        hook.Install();
        hook.Uninstall();
        hook.Install();

        Assert.True(hook.IsInstalled);
    }

    [Fact]
    public void Dispose_Uninstalls()
    {
        var hook = new TrayWheelHook((_, _) => false, _ => { });
        hook.Install();

        hook.Dispose();

        Assert.False(hook.IsInstalled);
    }

    [Fact]
    public void InstallAfterDispose_DoesNothing()
    {
        var hook = new TrayWheelHook((_, _) => false, _ => { });
        hook.Dispose();

        hook.Install();

        Assert.False(hook.IsInstalled);
    }
}
