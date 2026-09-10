using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SonarTray.Native;
using SonarTray.ViewModels;

namespace SonarTray.Views;

public partial class PopupWindow : Window
{
    /// <summary>Right-clicking the tray icon while the popup is open first deactivates (hides) it; the
    /// MouseUp that follows must not re-open it. Anything hidden within this window is treated as "same click".</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly MixerViewModel _viewModel;
    private DateTime _lastHiddenUtc = DateTime.MinValue;

    /// <summary>The window is only ever hidden; Close() is allowed solely during application exit.</summary>
    public bool AllowClose { get; set; }

    public PopupWindow(MixerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        SourceInitialized += (_, _) => ApplyWindows11Chrome();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Mouse.Captured is null) HidePopup();
        }));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePopup();
                e.Handled = true;
            }
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnPanelOpened();
            else _viewModel.OnPanelClosed();
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible) PopupPositioner.Place(this);
        };
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            HidePopup();
        };
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            HidePopup();
            return;
        }
        if (DateTime.UtcNow - _lastHiddenUtc < ReopenGuard) return;
        ShowAtTray();
    }

    public void ShowAtTray()
    {
        PopupPositioner.Place(this);
        Show();
        Activate();
    }

    public void HidePopup()
    {
        if (!IsVisible) return;
        _lastHiddenUtc = DateTime.UtcNow;
        Hide();
    }

    private void ApplyWindows11Chrome()
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        int corner = NativeMethods.DWMWCP_ROUND;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) == 0)
        {
            // DWM draws the border in the theme's line color; drop the square XAML border so the corners stay clean.
            int borderColor = 0x0050342A; // COLORREF 0x00BBGGRR for #2A3450
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            RootBorder.BorderThickness = new Thickness(0);
        }
    }
}
