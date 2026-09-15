using SonarTray.Tray;
using Xunit;

namespace SonarTray.Tests;

public sealed class TrayHoverTrackerTests
{
    private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private TrayHoverTracker Build() => new(() => _now);

    private void Advance(TimeSpan by) => _now += by;

    [Fact]
    public void WithoutAnyReport_NothingIsOverTheIcon()
    {
        // Before the shell has ever told us the pointer was on the icon, every scroll on the
        // machine must pass straight through.
        Assert.False(Build().IsOver(100, 100));
    }

    [Fact]
    public void TheReportedPointItself_IsOverTheIcon()
    {
        var tracker = Build();
        tracker.Report(1000, 1400);

        Assert.True(tracker.IsOver(1000, 1400));
    }

    [Fact]
    public void ScrollingWithoutMoving_KeepsWorking()
    {
        // The pointer does not move when the wheel turns, so no new mouse-move arrives; the
        // hover must stay valid or the second notch would be ignored.
        var tracker = Build();
        tracker.Report(1000, 1400);

        Advance(TimeSpan.FromSeconds(2));

        Assert.True(tracker.IsOver(1000, 1400));
    }

    [Theory]
    [InlineData(TrayHoverTracker.Radius, 0)]
    [InlineData(0, TrayHoverTracker.Radius)]
    [InlineData(-TrayHoverTracker.Radius, -TrayHoverTracker.Radius)]
    public void JustInsideTheRadius_CountsAsOverTheIcon(int dx, int dy)
    {
        var tracker = Build();
        tracker.Report(1000, 1400);

        Assert.True(tracker.IsOver(1000 + dx, 1400 + dy));
    }

    [Theory]
    [InlineData(TrayHoverTracker.Radius + 1, 0)]
    [InlineData(0, TrayHoverTracker.Radius + 1)]
    [InlineData(300, 0)]
    public void OutsideTheRadius_DoesNotCount(int dx, int dy)
    {
        // Scrolling over the clock, or over a neighbouring icon, must not move Sonar's volume.
        var tracker = Build();
        tracker.Report(1000, 1400);

        Assert.False(tracker.IsOver(1000 + dx, 1400 + dy));
    }

    [Fact]
    public void AStaleReport_Expires()
    {
        // Icons reflow. If the pointer has sat somewhere else for a long time, the last known
        // position is no longer evidence of anything.
        var tracker = Build();
        tracker.Report(1000, 1400);

        Advance(TrayHoverTracker.MaxAge + TimeSpan.FromSeconds(1));

        Assert.False(tracker.IsOver(1000, 1400));
    }

    [Fact]
    public void AFreshReport_RenewsTheHover()
    {
        var tracker = Build();
        tracker.Report(1000, 1400);
        Advance(TrayHoverTracker.MaxAge + TimeSpan.FromSeconds(1));
        tracker.Report(1000, 1400);

        Assert.True(tracker.IsOver(1000, 1400));
    }

    [Fact]
    public void MovingAlongTheTray_FollowsThePointer()
    {
        var tracker = Build();
        tracker.Report(1000, 1400);
        tracker.Report(1100, 1400);   // the shell reports the icon under the new position

        Assert.True(tracker.IsOver(1100, 1400));
        Assert.False(tracker.IsOver(1000, 1400));
    }

    [Fact]
    public void Clear_ForgetsTheHover()
    {
        var tracker = Build();
        tracker.Report(1000, 1400);

        tracker.Clear();

        Assert.False(tracker.IsOver(1000, 1400));
    }
}
