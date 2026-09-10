using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SonarTray.Native;
using SonarTray.ViewModels;

namespace SonarTray.Views;

/// <summary>
/// Hotkey feedback. The tray icon already reflects state, but it is invisible while a game is in
/// front, which is exactly when the hotkeys are used - so the confirmation has to be on screen.
///
/// Shown without activation and click-through, so it never takes focus from or blocks the game.
/// It will not appear over a game running in exclusive fullscreen; borderless windowed is fine.
/// </summary>
public partial class OsdWindow : Window
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(1300);
    private static readonly Duration FadeIn = new(TimeSpan.FromMilliseconds(110));
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(260));

    /// <summary>Distance from the bottom of the work area: clear of the taskbar, out of the way.</summary>
    private const double BottomMarginDip = 120;
    private const double BarWidth = 214;

    private readonly DispatcherTimer _hold;
    private readonly Brush _danger;

    /// <summary>Set during application shutdown; until then Close() is turned into a hide.</summary>
    public bool AllowClose { get; set; }

    public OsdWindow()
    {
        InitializeComponent();
        _danger = (Brush)FindResource("DangerBrush");

        _hold = new DispatcherTimer(DispatcherPriority.Normal) { Interval = HoldDuration };
        _hold.Tick += (_, _) => { _hold.Stop(); FadeOutAndHide(); };

        SourceInitialized += (_, _) => ApplyOverlayStyles();
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            HideNow();
        };
    }

    public void Show(ChannelViewModel channel)
    {
        Glyph.Text = channel.IsMuted ? "\uE74F" : channel.Glyph; // Mute, same glyph the panel toggle uses
        Glyph.Foreground = channel.IsMuted ? _danger : channel.Accent;
        NameText.Text = channel.Name;
        Value.Text = channel.IsMuted ? "Kapalı" : $"%{channel.Percent}";

        BarFill.Background = channel.IsMuted ? _danger : channel.Accent;
        BarFill.Opacity = channel.IsMuted ? 0.35 : 1.0;
        BarFill.Width = Math.Clamp(channel.Volume, 0.0, 1.0) * BarWidth;

        Place();
        if (!IsVisible) base.Show();

        // A HoldEnd animation shadows the local value, so the clock has to be detached before
        // Opacity is set - otherwise a second hotkey press within the fade leaves it half faded.
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, FadeIn) { FillBehavior = FillBehavior.HoldEnd });

        _hold.Stop();
        _hold.Start(); // each press restarts the countdown
    }

    private void FadeOutAndHide()
    {
        var fade = new DoubleAnimation(Opacity, 0, FadeOut) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            if (Opacity <= 0.01) HideNow();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void HideNow()
    {
        _hold.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    /// <summary>Bottom centre of the screen under the cursor, in physical pixels.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var cursor = System.Windows.Forms.Cursor.Position;
        var wa = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

        double scale = 1.0;
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = cursor.X, Y = cursor.Y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        int w = (int)Math.Ceiling(Width * scale);
        int h = (int)Math.Ceiling(Height * scale);
        int x = wa.Left + (wa.Width - w) / 2;
        int y = wa.Bottom - h - (int)Math.Round(BottomMarginDip * scale);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void ApplyOverlayStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }
}
