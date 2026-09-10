using SonarTray.Native;
using SonarTray.Services;
using SonarTray.Views;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace SonarTray.Tray;

/// <summary>
/// Notification-area icon. Right click toggles the mixer popup, left click opens SteelSeries GG.
/// No ContextMenuStrip is assigned on purpose: WinForms would otherwise show it on right-up.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly SonarConnection _connection;
    private readonly PopupWindow _popup;
    private readonly Action _leftClick;
    private readonly OwnedIcon _online;
    private readonly OwnedIcon _offline;
    private readonly OwnedIcon _warning;
    private bool _disposed;

    public TrayIconHost(SonarConnection connection, PopupWindow popup, Action leftClick)
    {
        _connection = connection;
        _popup = popup;
        _leftClick = leftClick;

        int size = Math.Max(16, (int)Math.Round(16 * NativeMethods.GetDpiForSystem() / 96.0));
        _online = TrayIconFactory.Create(size, SD.Color.FromArgb(0x2D, 0xD4, 0xBF));
        _offline = TrayIconFactory.Create(size, SD.Color.FromArgb(0x8A, 0x93, 0xA6));
        _warning = TrayIconFactory.Create(size, SD.Color.FromArgb(0xF5, 0x9E, 0x0B));

        _icon = new WF.NotifyIcon { Visible = false };
        _icon.MouseUp += OnMouseUp;
        Apply(connection.State);
        _icon.Visible = true;

        connection.StateChanged += OnStateChanged;
    }

    private void OnMouseUp(object? sender, WF.MouseEventArgs e)
    {
        if (e.Button == WF.MouseButtons.Right) _popup.Toggle();
        else if (e.Button == WF.MouseButtons.Left) _leftClick();
    }

    private void OnStateChanged(ConnectionState state)
        => _popup.Dispatcher.BeginInvoke(new Action(() => Apply(state)));

    private void Apply(ConnectionState state)
    {
        if (_disposed) return;
        (_icon.Icon, _icon.Text) = state switch
        {
            ConnectionState.Connected => (_online.Icon, "SonarTray – Sonar bağlı"),
            ConnectionState.StreamMode => (_warning.Icon, "SonarTray – Sonar Stream modunda"),
            ConnectionState.Searching => (_offline.Icon, "SonarTray – Sonar aranıyor…"),
            _ => (_offline.Icon, "SonarTray – Sonar bulunamadı"),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.StateChanged -= OnStateChanged;
        _icon.Visible = false; // otherwise a ghost icon lingers until hovered
        _icon.Dispose();
        _online.Dispose();
        _offline.Dispose();
        _warning.Dispose();
    }
}
