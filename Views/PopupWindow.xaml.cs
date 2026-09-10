using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SonarTray.Native;
using SonarTray.Services;
using SonarTray.ViewModels;

namespace SonarTray.Views;

public partial class PopupWindow : Window
{
    /// <summary>Right-clicking the tray icon while the popup is open first deactivates (hides) it; the
    /// MouseUp that follows must not re-open it. Anything hidden within this window is treated as "same click".</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private static readonly Duration EntranceDuration = new(TimeSpan.FromMilliseconds(140));
    private const double SlideOffset = 8;

    /// <summary>Everything that is not a channel row: borders, padding, header, master card, rule, footer.</summary>
    private const double ChromeHeight = 175;
    private const double ChannelRowBlock = 52; // 46 card + 6 gap, matching ChannelRow.xaml

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE landed in Windows 11 22H2; older builds return E_INVALIDARG.</summary>
    private const int Win11Build22H2 = 22621;

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_THEMECHANGED = 0x031A;

    private readonly MixerViewModel _viewModel;
    private DateTime _lastHiddenUtc = DateTime.MinValue;
    private TaskbarEdge _lastEdge = TaskbarEdge.Bottom;
    private bool _backdropActive;

    /// <summary>The window is only ever hidden; Close() is allowed solely during application exit.</summary>
    public bool AllowClose { get; set; }

    public PopupWindow(MixerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Sized from the data so flipping ChannelSpec.Aux to Visible just works.
        Height = ChromeHeight + viewModel.SubChannels.Count * ChannelRowBlock;

        SourceInitialized += (_, _) =>
        {
            ApplyWindows11Chrome();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Mouse.Captured is null) HidePopup();
        }));
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnPanelOpened();
            else _viewModel.OnPanelClosed();
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible) _lastEdge = PopupPositioner.Place(this);
        };
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            HidePopup();
        };
    }

    /// <summary>
    /// Every control in the panel is Focusable=False, so the window sees the keystrokes. While a
    /// hotkey row is capturing, the press belongs to it rather than to the panel; Escape then backs
    /// out one level at a time - capture, then the settings page, then the popup.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var hotkeys = _viewModel.Hotkeys;

        if (hotkeys.IsCapturing)
        {
            e.Handled = true;
            // Alt-combinations arrive as Key.System with the real key in SystemKey.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(key)) return; // wait for the non-modifier half of the chord
            hotkeys.Capture(Keyboard.Modifiers, key);
            return;
        }

        if (e.Key != Key.Escape) return;
        e.Handled = true;

        if (_viewModel.IsSettingsOpen) _viewModel.IsSettingsOpen = false;
        else HidePopup();
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.System;

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
        _lastEdge = PopupPositioner.Place(this);
        PrepareEntrance();
        Show();
        Activate();
        // Loaded priority runs after the layout/render pass Show() queues, so the clock starts
        // on the frame the user actually sees rather than partway through.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(StartEntrance));
    }

    public void HidePopup()
    {
        if (!IsVisible) return;
        _lastHiddenUtc = DateTime.UtcNow;
        ClearEntranceAnimations();
        Hide();
    }

    // ---- entrance animation ---------------------------------------------

    /// <summary>
    /// A HoldEnd animation shadows the local value forever, so the next open would silently ignore
    /// an assignment to Opacity. Both clocks must be detached before the values are set.
    /// </summary>
    private void ClearEntranceAnimations()
    {
        ContentHost.BeginAnimation(OpacityProperty, null);
        ContentSlide.BeginAnimation(TranslateTransform.XProperty, null);
        ContentSlide.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void PrepareEntrance()
    {
        ClearEntranceAnimations();
        ContentHost.Opacity = 0;
        (ContentSlide.X, ContentSlide.Y) = _lastEdge switch
        {
            TaskbarEdge.Top => (0.0, -SlideOffset),
            TaskbarEdge.Left => (-SlideOffset, 0.0),
            TaskbarEdge.Right => (SlideOffset, 0.0),
            _ => (0.0, SlideOffset), // bottom
        };
    }

    private void StartEntrance()
    {
        if (!IsVisible) return; // the foreground lock can deactivate us within a frame of Show()

        if (!SystemParameters.ClientAreaAnimation)
        {
            ContentHost.Opacity = 1;
            ContentSlide.X = 0;
            ContentSlide.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, EntranceDuration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        ContentSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(ContentSlide.X, 0, EntranceDuration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        ContentSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(ContentSlide.Y, 0, EntranceDuration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
    }

    // ---- window chrome ---------------------------------------------------

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A theme flip re-selects the acrylic recipe, and toggling "Transparency effects" decides
        // whether the material is possible at all. Both arrive here; re-evaluate from scratch.
        bool relevant = msg == WM_THEMECHANGED
            || (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero
                && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet");

        if (relevant) Dispatcher.BeginInvoke(new Action(ApplyWindows11Chrome));
        return IntPtr.Zero;
    }

    private void ApplyWindows11Chrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || Environment.OSVersion.Version.Build < 22000) return;

        // Dark mode first: it decides whether DWM picks the light or the dark acrylic recipe.
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

        TryApplyAcrylic(hwnd);
    }

    /// <summary>
    /// All-or-nothing. A half-applied backdrop composites alpha-0 pixels as a black rectangle, and
    /// S_OK from the attribute is no proof the material will actually be drawn, so every condition
    /// is checked before any background property is touched.
    /// </summary>
    private void TryApplyAcrylic(IntPtr hwnd)
    {
        if (Environment.OSVersion.Version.Build < Win11Build22H2)
        {
            Log.Verbose($"Acrylic skipped: build {Environment.OSVersion.Version.Build} < {Win11Build22H2}");
            DisableAcrylic(hwnd);
            return;
        }

        if (!TransparencyEffectsEnabled())
        {
            // DWM would substitute a flat theme grey, which is worse than our own palette.
            Log.Verbose("Acrylic skipped: transparency effects are disabled");
            DisableAcrylic(hwnd);
            return;
        }

        if (HwndSource.FromHwnd(hwnd) is not { CompositionTarget: { } target })
        {
            Log.Verbose("Acrylic skipped: no composition target");
            return;
        }

        int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        if (hr != 0)
        {
            Log.Verbose($"Acrylic skipped: DWMWA_SYSTEMBACKDROP_TYPE returned 0x{hr:X8}");
            return;
        }

        var margins = new NativeMethods.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        hr = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (hr != 0)
        {
            Log.Verbose($"Acrylic skipped: DwmExtendFrameIntoClientArea returned 0x{hr:X8}");
            DisableAcrylic(hwnd);
            return;
        }

        // Only now is it safe to stop painting opaquely.
        target.BackgroundColor = Colors.Transparent;
        Background = Brushes.Transparent;                                            // Transparent, never null: null is not hit-testable
        RootBorder.SetResourceReference(BackgroundProperty, "BgTintBrush");           // keeps the palette and the text contrast
        _backdropActive = true;
        Log.Verbose("Acrylic backdrop applied");
    }

    /// <summary>Turns the material off and puts the opaque fallback back, in that order.</summary>
    private void DisableAcrylic(IntPtr hwnd)
    {
        int off = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref off, sizeof(int));

        if (!_backdropActive) return;
        _backdropActive = false;

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = (Color)FindResource("BgColor");
        SetResourceReference(BackgroundProperty, "BgBrush");
        RootBorder.Background = null;
        Log.Verbose("Acrylic backdrop reverted to the solid fallback");
    }

    private static bool TransparencyEffectsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch
        {
            return true; // assume on; the worst case is DWM drawing its own substitute
        }
    }
}
