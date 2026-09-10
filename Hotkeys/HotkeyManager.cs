using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using SonarTray.Native;
using SonarTray.Services;

namespace SonarTray.Hotkeys;

/// <summary>
/// System-wide hotkeys, delivered through a message-only window so nothing depends on the popup
/// being alive or visible.
///
/// RegisterHotKey fails when another process already owns the combination, which is common and is
/// the single most likely reason a hotkey "does nothing" - every failure is logged with the gesture,
/// and <see cref="Status"/> exposes it so the settings page can mark the row.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<int> _registered = new();
    private readonly Dictionary<HotkeyAction, bool> _status = new();
    private readonly Action<HotkeyAction> _invoke;
    private HotkeyConfig _config;
    private bool _suspended;
    private bool _disposed;

    /// <summary>True when the action is live; false when it is disabled, unparseable or taken.</summary>
    public IReadOnlyDictionary<HotkeyAction, bool> Status => _status;

    public HotkeyManager(HotkeyConfig config, Action<HotkeyAction> invoke)
    {
        _invoke = invoke;
        _config = config;

        _source = new HwndSource(new HwndSourceParameters("SonarTray.Hotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(OnMessage);

        Apply(config);
    }

    /// <summary>Drops every current registration and registers the given config instead.</summary>
    public void Apply(HotkeyConfig config)
    {
        if (_disposed) return;
        _config = config;
        UnregisterAll();
        _suspended = false;
        foreach (var (action, gesture) in config.Bindings) Register(action, gesture);
    }

    /// <summary>
    /// Releases the registrations while the settings page is capturing a keystroke. Without this,
    /// pressing an already-bound combination would fire its action instead of being captured.
    /// </summary>
    public void Suspend()
    {
        if (_disposed || _suspended) return;
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        if (_disposed || !_suspended) return;
        _suspended = false;
        foreach (var (action, gesture) in _config.Bindings) Register(action, gesture);
    }

    private void UnregisterAll()
    {
        foreach (int id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _status.Clear();
    }

    private void Register(HotkeyAction action, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            _status[action] = false;
            Log.Verbose($"Hotkey {action}: disabled");
            return;
        }

        if (!TryParse(gesture, out uint modifiers, out uint vk))
        {
            _status[action] = false;
            Log.Warn($"Hotkey {action}: could not parse \"{gesture}\"; see {HotkeyConfig.FilePath}");
            return;
        }

        int id = (int)action;
        if (NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
        {
            _registered.Add(id);
            _status[action] = true;
            Log.Info($"Hotkey {action}: {gesture}");
            return;
        }

        _status[action] = false;
        int err = Marshal.GetLastWin32Error();
        Log.Warn($"Hotkey {action}: \"{gesture}\" rejected ({new Win32Exception(err).Message}); "
                 + "another application probably owns it.");
    }

    /// <summary>
    /// Parses "Ctrl+Shift+F9" via WPF's own gesture converter, so the accepted spelling is the
    /// familiar one and key names do not have to be maintained here.
    /// </summary>
    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(gesture) is not KeyGesture parsed) return false;

            if (parsed.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
            if (parsed.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
            if (parsed.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
            if (parsed.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= NativeMethods.MOD_WIN;

            vk = (uint)KeyInterop.VirtualKeyFromKey(parsed.Key);
            return modifiers != 0 && vk != 0;
        }
        catch
        {
            return false; // the converter throws on anything it does not recognise
        }
    }

    /// <summary>Renders a captured combination in the same spelling <see cref="TryParse"/> accepts.</summary>
    public static string Format(ModifierKeys modifiers, Key key)
    {
        try
        {
            return new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, modifiers)) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY) return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (!Enum.IsDefined(typeof(HotkeyAction), id)) return IntPtr.Zero;

        handled = true;
        _invoke((HotkeyAction)id);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }
}
