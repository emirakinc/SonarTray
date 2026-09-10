using SD = System.Drawing;

namespace SonarTray.Tray;

/// <summary>Identifies one rendered glyph. Size is part of it so a DPI change cannot serve a stale icon.</summary>
internal readonly record struct TrayIconKey(int Size, IconTone Tone, int Level, bool Muted);

/// <summary>
/// Keeps every glyph the session has drawn. Caching is not about render cost - GDI+ at these sizes is
/// trivial - it is about ownership: the shell keeps drawing from the HICON handed to NotifyIcon.Icon,
/// so an icon that is currently displayed must never be destroyed. Nothing here is ever disposed early.
///
/// UI thread only: the dictionary and the GDI+ objects behind it are unsynchronised.
/// </summary>
internal sealed class TrayIconCache : IDisposable
{
    private readonly Dictionary<TrayIconKey, OwnedIcon> _icons = new();
    private bool _disposed;

    /// <summary>
    /// Collapses states that render identically, so the live set is about eight entries rather than
    /// a full cross product: a muted icon shows no level, and offline/stream show status, not volume.
    /// </summary>
    public static TrayIconKey Key(int size, IconTone tone, int level, bool muted)
    {
        if (tone != IconTone.Online) return new TrayIconKey(size, tone, 0, false);
        if (muted) return new TrayIconKey(size, tone, 0, true);
        return new TrayIconKey(size, tone, Math.Clamp(level, 0, TrayIconFactory.MaxLevel), false);
    }

    public SD.Icon Get(TrayIconKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_icons.TryGetValue(key, out var owned))
        {
            owned = TrayIconFactory.Create(key.Size, key.Tone, key.Level, key.Muted);
            _icons[key] = owned;
        }
        return owned.Icon;
    }

    /// <summary>The caller must have cleared NotifyIcon.Icon first; the shell draws from these handles.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var owned in _icons.Values) owned.Dispose();
        _icons.Clear();
    }
}
