namespace SonarTray.Tray;

/// <summary>
/// Decides whether a screen point is over our tray icon.
///
/// There is no supported way to ask for a notification icon's rectangle without either reflecting
/// into WinForms' private fields or re-implementing the icon on top of Shell_NotifyIcon. Both are
/// worse than the observation this uses: the shell delivers a mouse-move callback to an icon only
/// while the pointer is actually over it. So the last position the shell told us about is, by
/// definition, a point on our icon.
///
/// A point counts as "over the icon" when it is within <see cref="Radius"/> of that last known
/// position. Scrolling does not move the pointer, so sitting still and spinning the wheel keeps
/// working; moving away invalidates it either by reporting a new position or by failing the
/// distance test. <see cref="MaxAge"/> is a safety net for the case where the icons reflow while
/// the pointer sits motionless somewhere else.
/// </summary>
public sealed class TrayHoverTracker
{
    /// <summary>Roughly one small icon either way, so the whole glyph counts as hovered.</summary>
    public const int Radius = 20;

    /// <summary>How long a hover report stays trustworthy without being renewed.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _now;
    private int _x;
    private int _y;
    private DateTime _reportedAt = DateTime.MinValue;

    public TrayHoverTracker() : this(() => DateTime.UtcNow) { }

    /// <param name="now">Injectable clock; the parameterless constructor uses the real one.</param>
    public TrayHoverTracker(Func<DateTime> now) => _now = now;

    /// <summary>Called whenever the shell tells us the pointer is over our icon.</summary>
    public void Report(int x, int y)
    {
        _x = x;
        _y = y;
        _reportedAt = _now();
    }

    /// <summary>Forgets the last hover, e.g. once the panel takes over.</summary>
    public void Clear() => _reportedAt = DateTime.MinValue;

    public bool IsOver(int x, int y)
    {
        if (_reportedAt == DateTime.MinValue) return false;
        if (_now() - _reportedAt > MaxAge) return false;

        return Math.Abs(x - _x) <= Radius && Math.Abs(y - _y) <= Radius;
    }
}
