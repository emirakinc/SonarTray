using SonarTray.Native;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;

namespace SonarTray.Tray;

/// <summary>What the glyph colour says about the connection.</summary>
internal enum IconTone
{
    Online,
    Offline,
    Warning,
}

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

/// <summary>
/// Draws the tray glyph at runtime: three baseline-aligned mixer bars whose lit portion tracks the
/// master volume. The silhouette is identical at every level, so only the fill appears to move -
/// far more stable at 16px than bars that change height.
///
/// Geometry is computed in whole pixels. The old size/16f unit produced fractional bar widths at
/// 24 and 28px (150% and 175% scaling) and the result blurred.
///
/// tools/Make-Icon.ps1 mirrors this math to produce Assets/tray.ico; change both together.
/// </summary>
internal static class TrayIconFactory
{
    /// <summary>Volume is quantised into MaxLevel + 1 buckets. Five is all three bars can show.</summary>
    public const int MaxLevel = 4;

    /// <summary>Bar heights as a fraction of the usable height, left to right.</summary>
    private static readonly double[] Profile = { 0.55, 1.0, 0.75 };

    public static int LevelFor(double volume)
        => Math.Clamp((int)(Math.Clamp(volume, 0.0, 1.0) * (MaxLevel + 1)), 0, MaxLevel);

    public static OwnedIcon Create(int size, IconTone tone, int level, bool muted)
        => Create(size, ColorFor(tone), Fill(tone, level, muted), muted);

    /// <summary>Explicit-colour overload, kept so a single glyph can be rendered for the exe icon.</summary>
    public static OwnedIcon Create(int size, SD.Color color, double fill, bool muted)
    {
        using var bmp = new SD.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);

            int pad = Math.Max(1, (int)Math.Round(size * 0.125));
            int usableW = size - 2 * pad;
            int usableH = size - 2 * pad;
            int gap = Math.Max(1, (int)Math.Round(size * 0.09));
            int barW = Math.Max(2, (usableW - 2 * gap) / 3);
            int totalW = 3 * barW + 2 * gap;
            int x0 = pad + (usableW - totalW) / 2;
            int baseline = size - pad;

            // Unmuted, the dim bars are the empty part of the meter and must stay quiet. Muted,
            // they are the only content left, so they carry the glyph on their own.
            using var ghostBrush = new SD.SolidBrush(SD.Color.FromArgb(muted ? 165 : 90, color));
            using var litBrush = new SD.SolidBrush(color);

            for (int i = 0; i < Profile.Length; i++)
            {
                int h = Math.Max(barW, (int)Math.Round(usableH * Profile[i]));
                int x = x0 + i * (barW + gap);
                int y = baseline - h;

                using var path = RoundedRect(x, y, barW, h, barW / 2f);
                g.FillPath(ghostBrush, path);

                int litH = Math.Clamp((int)Math.Round(h * fill), 0, h);
                if (litH <= 0) continue;

                // Axis-aligned clip, so the fill line is a clean horizontal cut with no AA seam.
                g.SetClip(new SD.Rectangle(x, baseline - litH, barW, litH));
                g.FillPath(litBrush, path);
                g.ResetClip();
            }

            if (muted) DrawMuteSlash(g, size, pad);
        }

        return new OwnedIcon(bmp.GetHicon());
    }

    private static void DrawMuteSlash(SD.Graphics g, int size, int pad)
    {
        float slashW = Math.Max(1.5f, size * 0.10f);
        // The gap is measured perpendicular to the diagonal, so it eats ~1.4x this much vertically.
        // Keep it small or it erases the bars it is supposed to sit on top of.
        float gapW = slashW + Math.Max(1f, size * 0.035f);
        float x0 = pad, y0 = size - pad, x1 = size - pad, y1 = pad;

        // Below 20px the gap would erase most of the glyph and leave a bare diagonal, so the red
        // stroke is left to carry itself on contrast alone - it is already far from the teal.
        if (size >= 20)
        {
            // Punch a transparent gap so the slash separates from the bars. SourceCopy overwrites
            // alpha instead of blending, which is what erases - but antialiasing must be off first:
            // GDI+ still computes AA coverage and then *copies* it, leaving a black fringe.
            var savedSmoothing = g.SmoothingMode;
            g.SmoothingMode = SD2.SmoothingMode.None;
            g.CompositingMode = SD2.CompositingMode.SourceCopy;
            using (var eraser = new SD.Pen(SD.Color.FromArgb(0, 0, 0, 0), gapW))
            {
                eraser.StartCap = eraser.EndCap = SD2.LineCap.Square;
                g.DrawLine(eraser, x0, y0, x1, y1);
            }

            // Restore blending before the visible stroke, or its own AA edges get hard-copied too.
            g.CompositingMode = SD2.CompositingMode.SourceOver;
            g.SmoothingMode = savedSmoothing;
        }
        using var slash = new SD.Pen(SD.Color.FromArgb(0xEF, 0x44, 0x44), slashW)
        {
            StartCap = SD2.LineCap.Round,
            EndCap = SD2.LineCap.Round,
        };
        g.DrawLine(slash, x0, y0, x1, y1);
    }

    /// <summary>Muted shows no meter; offline and stream mode are status, not level, so they read full.</summary>
    private static double Fill(IconTone tone, int level, bool muted)
    {
        if (tone != IconTone.Online) return 1.0;
        if (muted) return 0.0;
        return 0.15 + 0.85 * (Math.Clamp(level, 0, MaxLevel) / (double)MaxLevel);
    }

    private static SD.Color ColorFor(IconTone tone) => tone switch
    {
        IconTone.Online => SD.Color.FromArgb(0x2D, 0xD4, 0xBF),
        IconTone.Warning => SD.Color.FromArgb(0xF5, 0x9E, 0x0B),
        _ => SD.Color.FromArgb(0x8A, 0x93, 0xA6),
    };

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
