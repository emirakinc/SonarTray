using SonarTray.Native;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;

namespace SonarTray.Tray;

/// <summary>An HICON we created ourselves; must be destroyed when no longer used.</summary>
internal sealed class OwnedIcon : IDisposable
{
    private IntPtr _handle;

    public SD.Icon Icon { get; }

    public OwnedIcon(IntPtr handle)
    {
        _handle = handle;
        Icon = SD.Icon.FromHandle(handle); // does not take ownership of the handle
    }

    public void Dispose()
    {
        Icon.Dispose();
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

/// <summary>Draws the tray glyph (three rounded mixer bars) at runtime so it can be recolored per state.</summary>
internal static class TrayIconFactory
{
    public static OwnedIcon Create(int size, SD.Color color)
    {
        using var bmp = new SD.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);

            using var brush = new SD.SolidBrush(color);
            float u = size / 16f;
            float barW = 3.2f * u;
            float gap = 1.6f * u;
            float[] heights = { 0.55f, 0.9f, 0.7f };
            float totalW = 3 * barW + 2 * gap;
            float x0 = (size - totalW) / 2f;

            for (int i = 0; i < heights.Length; i++)
            {
                float h = size * heights[i];
                float x = x0 + i * (barW + gap);
                float y = (size - h) / 2f;
                using var path = RoundedRect(x, y, barW, h, barW / 2f);
                g.FillPath(brush, path);
            }
        }

        return new OwnedIcon(bmp.GetHicon());
    }

    private static SD2.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new SD2.GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
