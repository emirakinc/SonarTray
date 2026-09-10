using System.Windows;
using System.Windows.Interop;
using SonarTray.Native;

namespace SonarTray.Views;

/// <summary>Which screen edge the taskbar sits on; the entrance animation slides away from it.</summary>
internal enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Places the popup next to the taskbar corner nearest the cursor, entirely in physical pixels
/// (the process is PerMonitorV2, so Cursor/Screen report physical coordinates).
/// </summary>
internal static class PopupPositioner
{
    private const double MarginDip = 8;

    /// <summary>Moves the window and reports the taskbar edge it was docked against.</summary>
    public static TaskbarEdge Place(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;
        var bounds = screen.Bounds;

        double scale = 1.0;
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = cursor.X, Y = cursor.Y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        int w = (int)Math.Ceiling(window.Width * scale);
        int h = (int)Math.Ceiling(window.Height * scale);
        int m = (int)Math.Round(MarginDip * scale);

        int x, y;
        TaskbarEdge edge;
        if (wa.Top > bounds.Top)                 // taskbar at top
        {
            edge = TaskbarEdge.Top;
            y = wa.Top + m;
            x = Clamp(cursor.X - w / 2, wa.Left + m, wa.Right - w - m);
        }
        else if (wa.Right < bounds.Right)        // taskbar on the right
        {
            edge = TaskbarEdge.Right;
            x = wa.Right - w - m;
            y = Clamp(cursor.Y - h / 2, wa.Top + m, wa.Bottom - h - m);
        }
        else if (wa.Left > bounds.Left)          // taskbar on the left
        {
            edge = TaskbarEdge.Left;
            x = wa.Left + m;
            y = Clamp(cursor.Y - h / 2, wa.Top + m, wa.Bottom - h - m);
        }
        else                                     // bottom (default)
        {
            edge = TaskbarEdge.Bottom;
            y = wa.Bottom - h - m;
            x = Clamp(cursor.X - w / 2, wa.Left + m, wa.Right - w - m);
        }

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return edge;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (max < min) max = min;
        return Math.Max(min, Math.Min(max, value));
    }
}
