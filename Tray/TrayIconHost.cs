using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Win32;
using SonarTray.Native;
using SonarTray.Resources;
using SonarTray.Services;
using SonarTray.ViewModels;
using SonarTray.Views;
using WF = System.Windows.Forms;

namespace SonarTray.Tray;

/// <summary>
/// Notification-area icon. Right click toggles the mixer popup, left click opens SteelSeries GG.
/// No ContextMenuStrip is assigned on purpose: WinForms would otherwise show it on right-up.
///
/// The glyph tracks both the connection state and the master channel's volume and mute.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    /// <summary>
    /// Every icon or tooltip assignment is a cross-process Shell_NotifyIcon(NIM_MODIFY). Quantising
    /// volume already caps a full drag at five distinct icons; this caps the rate on top of that,
    /// leading-edge so a deliberate click still feels instant.
    /// </summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(120);

    private readonly WF.NotifyIcon _icon;
    private readonly SonarConnection _connection;
    private readonly ChannelViewModel _master;
    private readonly PopupWindow _popup;
    private readonly Action _leftClick;
    private readonly TrayIconCache _cache = new();
    private readonly DispatcherTimer _coalesce;
    private readonly TrayHoverTracker _hover = new();
    private readonly TrayWheelHook? _wheel;

    private TrayIconKey? _applied;
    private DateTime _lastApplyUtc = DateTime.MinValue;
    private bool _disposed;

    /// <param name="onWheel">
    /// Receives wheel notches over the icon; null disables the hook entirely, which is what the
    /// "mouse wheel over the tray icon" setting turns off.
    /// </param>
    public TrayIconHost(SonarConnection connection, MixerViewModel mixer, PopupWindow popup, Action leftClick,
                        Action<int>? onWheel)
    {
        _connection = connection;
        _master = mixer.Master;
        _popup = popup;
        _leftClick = leftClick;

        _coalesce = new DispatcherTimer(DispatcherPriority.Background) { Interval = MinInterval };
        _coalesce.Tick += (_, _) => { _coalesce.Stop(); Apply(); };

        _icon = new WF.NotifyIcon { Visible = false };
        _icon.MouseUp += OnMouseUp;
        // The shell raises this only while the pointer is over our icon, which is the whole basis
        // for deciding whether a global wheel event belongs to us.
        _icon.MouseMove += OnIconMouseMove;
        Apply();
        _icon.Visible = true;

        if (onWheel is not null) _wheel = new TrayWheelHook((x, y) => _hover.IsOver(x, y), onWheel);

        connection.StateChanged += OnStateChanged;
        _master.PropertyChanged += OnMasterChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Read fresh every time rather than cached at construction: a display-scale change would
    /// otherwise leave the tray drawing a stale, blurry size for the rest of the session. Two
    /// cheap P/Invokes, and the call sites are already throttled.
    /// </summary>
    private static int SmallIconSize()
    {
        uint dpi = NativeMethods.GetDpiForSystem();
        int metric = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
        return metric > 0 ? metric : Math.Max(16, (int)Math.Round(16 * dpi / 96.0));
    }

    /// <summary>Raised on a dedicated SystemEvents thread; the cache and GDI+ are UI-thread only.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => _popup.Dispatcher.BeginInvoke(new Action(Request));

    /// <summary>
    /// Installs or removes the wheel hook. Toggled from the settings page, so it must take effect
    /// immediately rather than on the next start.
    /// </summary>
    public bool WheelEnabled
    {
        get => _wheel?.IsInstalled ?? false;
        set
        {
            if (_wheel is null || _disposed) return;
            if (value) _wheel.Install();
            else _wheel.Uninstall();
        }
    }

    /// <summary>
    /// Records where the pointer was. The event's own coordinates are not dependable across shell
    /// versions, so the position comes from the system instead.
    /// </summary>
    private void OnIconMouseMove(object? sender, WF.MouseEventArgs e)
    {
        if (NativeMethods.GetCursorPos(out var pt)) _hover.Report(pt.X, pt.Y);
    }

    private void OnMouseUp(object? sender, WF.MouseEventArgs e)
    {
        if (e.Button == WF.MouseButtons.Right) _popup.Toggle();
        else if (e.Button == WF.MouseButtons.Left) _leftClick();
    }

    /// <summary>Raised on an arbitrary thread; the cache and GDI+ are UI-thread only.</summary>
    private void OnStateChanged(ConnectionState state)
        => _popup.Dispatcher.BeginInvoke(new Action(Request));

    private void OnMasterChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChannelViewModel.Volume):
            case nameof(ChannelViewModel.IsMuted):
                Request();
                break;
            case nameof(ChannelViewModel.IsDragging):
                // Mirrors ChannelViewModel.FlushVolume: the value the drag ended on always lands.
                if (!_master.IsDragging) Apply();
                break;
        }
    }

    private void Request()
    {
        if (_disposed) return;
        if (Desired() == _applied) return;

        var since = DateTime.UtcNow - _lastApplyUtc;
        if (since >= MinInterval)
        {
            Apply();
            return;
        }
        if (!_coalesce.IsEnabled)
        {
            _coalesce.Interval = MinInterval - since;
            _coalesce.Start();
        }
    }

    private void Apply()
    {
        if (_disposed) return;
        _coalesce.Stop();

        var key = Desired();
        var icon = _cache.Get(key);
        var text = Text();

        // Identical keys return the identical Icon object, so redundant shell calls cost nothing.
        if (!ReferenceEquals(_icon.Icon, icon)) _icon.Icon = icon;
        if (!string.Equals(_icon.Text, text, StringComparison.Ordinal)) _icon.Text = text;

        _applied = key;
        _lastApplyUtc = DateTime.UtcNow;
    }

    private TrayIconKey Desired()
        => TrayIconCache.Key(SmallIconSize(), ToneFor(_connection.State), TrayIconFactory.LevelFor(_master.Volume), _master.IsMuted);

    private static IconTone ToneFor(ConnectionState state) => state switch
    {
        ConnectionState.Connected => IconTone.Online,
        ConnectionState.StreamMode => IconTone.Warning,
        _ => IconTone.Offline,
    };

    private string Text() => _connection.State switch
    {
        ConnectionState.Connected => _master.IsMuted
            ? Strings.Tray_Muted
            : Strings.Format("Tray_Volume", _master.Percent),
        ConnectionState.StreamMode => Strings.Tray_StreamMode,
        ConnectionState.Searching => Strings.Tray_Searching,
        _ => Strings.Tray_NotFound,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coalesce.Stop();
        _wheel?.Dispose();
        _icon.MouseMove -= OnIconMouseMove;
        _connection.StateChanged -= OnStateChanged;
        _master.PropertyChanged -= OnMasterChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // static event: leaks this instance otherwise

        _icon.Visible = false; // otherwise a ghost icon lingers until hovered
        _icon.Icon = null;     // the shell must let go of the HICON before the cache destroys it
        _icon.Dispose();
        _cache.Dispose();
    }
}
