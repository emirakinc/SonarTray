namespace SonarTray.Services;

/// <summary>
/// One running copy per user session. A second launch does not just exit: it signals the copy that
/// is already running to show its panel, which is what someone who double-clicked the exe again
/// (having forgotten it lives in the tray) actually wanted.
///
/// A named <see cref="EventWaitHandle"/> rather than a broadcast window message, because the only
/// window this app keeps alive at all times is message-only, and message-only windows never
/// receive HWND_BROADCAST.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Local\ scopes both objects to the logon session, matching "one per user".
    private const string MutexName = @"Local\SonarTray-7C2E1A0B-5F7D-4B6E-9C3A-2D1F0E8B4A61";
    private const string SignalName = @"Local\SonarTray-show-7C2E1A0B";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _listenCts;
    private bool _disposed;

    /// <summary>True when this process is the one that should keep running.</summary>
    public bool IsFirst => _ownsMutex;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    /// <summary>Called by the second instance to bring the first one's panel up.</summary>
    public void SignalExistingInstance()
    {
        try
        {
            _signal.Set();
            Log.Info("Another instance is already running; asked it to show its panel.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not signal the running instance: {ex.Message}");
        }
    }

    /// <summary>
    /// Watches for a later launch. <paramref name="onSignal"/> is raised on a background thread,
    /// so the caller must marshal to the UI thread itself.
    /// </summary>
    public void StartListening(Action onSignal)
    {
        if (!_ownsMutex || _disposed) return;

        _listenCts = new CancellationTokenSource();
        var ct = _listenCts.Token;

        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _signal, ct.WaitHandle };
            while (!ct.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) != 0) return; // cancellation
                try { onSignal(); }
                catch (Exception ex) { Log.Error("Show-panel signal handler threw", ex); }
            }
        })
        {
            IsBackground = true,
            Name = "SonarTray.SingleInstance",
        };
        thread.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _listenCts?.Cancel();
        _listenCts?.Dispose();

        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch { /* already gone; nothing to salvage */ }
        }
        _mutex.Dispose();
        _signal.Dispose();
    }
}
