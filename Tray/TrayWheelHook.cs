using SonarTray.Native;
using SonarTray.Services;

namespace SonarTray.Tray;

/// <summary>
/// Makes the mouse wheel work over the tray icon.
///
/// The shell never forwards <c>WM_MOUSEWHEEL</c> to a notification icon - the callback message
/// carries clicks and moves and nothing else - so the only way to see the wheel at all is a
/// low-level hook. The hook is global, which means every scroll anywhere on the machine passes
/// through <see cref="OnMouse"/>; it therefore does as little as possible and hands off
/// immediately unless the pointer is over our own icon.
/// </summary>
public sealed class TrayWheelHook : IDisposable
{
    private readonly Func<int, int, bool> _isOverIcon;
    private readonly Action<int> _onWheel;

    // The delegate must be kept alive for as long as the hook is installed: the CLR does not
    // know the unmanaged hook holds a pointer to it, and a collected delegate crashes the
    // process the next time anyone scrolls.
    private readonly NativeMethods.HookProc _proc;

    private IntPtr _hook;
    private bool _disposed;

    /// <summary>True once the hook is installed; false means the feature is quietly unavailable.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <param name="isOverIcon">Given a screen point, says whether it is over our tray icon.</param>
    /// <param name="onWheel">Receives the number of notches: positive is up, negative is down.</param>
    public TrayWheelHook(Func<int, int, bool> isOverIcon, Action<int> onWheel)
    {
        _isOverIcon = isOverIcon;
        _onWheel = onWheel;
        _proc = OnMouse;
    }

    /// <summary>
    /// Must be called from a thread with a message loop - a low-level hook is dispatched on the
    /// installing thread, so installing it from a worker would simply never fire.
    /// </summary>
    public void Install()
    {
        if (_disposed || IsInstalled) return;

        // A WH_MOUSE_LL hook takes no module handle in managed code; passing IntPtr.Zero with the
        // current thread id is the documented shape for a thread-local low-level hook.
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

        if (IsInstalled) Log.Info("Tray wheel hook installed");
        else Log.Warn("Tray wheel hook could not be installed; scrolling over the icon will do nothing");
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Verbose("Tray wheel hook removed");
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || wParam != NativeMethods.WM_MOUSEWHEEL)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (_isOverIcon(data.pt.X, data.pt.Y))
            {
                // The delta is the signed high word of mouseData.
                int delta = (short)(data.mouseData >> 16);
                int notches = delta / NativeMethods.WHEEL_DELTA;
                if (notches != 0)
                {
                    _onWheel(notches);
                    // Swallow it: the taskbar would otherwise also act on the scroll.
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            // A throw here would propagate into the hook chain and can take the shell's input
            // handling with it; logging and passing the event on is the only safe response.
            Log.Error("Tray wheel hook threw", ex);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
