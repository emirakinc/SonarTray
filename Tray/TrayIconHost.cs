using System.ComponentModel;
using System.Windows.Threading;
using SonarTray.Native;
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
    private readonly int _size;

    private TrayIconKey? _applied;
    private DateTime _lastApplyUtc = DateTime.MinValue;
    private bool _disposed;

    public TrayIconHost(SonarConnection connection, MixerViewModel mixer, PopupWindow popup, Action leftClick)
    {
        _connection = connection;
        _master = mixer.Master;
        _popup = popup;
        _leftClick = leftClick;
        _size = SmallIconSize();

        _coalesce = new DispatcherTimer(DispatcherPriority.Background) { Interval = MinInterval };
        _coalesce.Tick += (_, _) => { _coalesce.Stop(); Apply(); };

        _icon = new WF.NotifyIcon { Visible = false };
        _icon.MouseUp += OnMouseUp;
        Apply();
        _icon.Visible = true;

        connection.StateChanged += OnStateChanged;
        _master.PropertyChanged += OnMasterChanged;
    }

    private static int SmallIconSize()
    {
        uint dpi = NativeMethods.GetDpiForSystem();
        int metric = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
        return metric > 0 ? metric : Math.Max(16, (int)Math.Round(16 * dpi / 96.0));
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
        => TrayIconCache.Key(_size, ToneFor(_connection.State), TrayIconFactory.LevelFor(_master.Volume), _master.IsMuted);

    private static IconTone ToneFor(ConnectionState state) => state switch
    {
        ConnectionState.Connected => IconTone.Online,
        ConnectionState.StreamMode => IconTone.Warning,
        _ => IconTone.Offline,
    };

    private string Text() => _connection.State switch
    {
        ConnectionState.Connected => _master.IsMuted
            ? "SonarTray – Ana ses kapalı"
            : $"SonarTray – Ana ses %{_master.Percent}",
        ConnectionState.StreamMode => "SonarTray – Sonar Stream modunda",
        ConnectionState.Searching => "SonarTray – Sonar aranıyor…",
        _ => "SonarTray – Sonar bulunamadı",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coalesce.Stop();
        _connection.StateChanged -= OnStateChanged;
        _master.PropertyChanged -= OnMasterChanged;

        _icon.Visible = false; // otherwise a ghost icon lingers until hovered
        _icon.Icon = null;     // the shell must let go of the HICON before the cache destroys it
        _icon.Dispose();
        _cache.Dispose();
    }
}
