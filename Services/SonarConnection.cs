namespace SonarTray.Services;

public enum ConnectionState
{
    /// <summary>Actively looking for Sonar (startup, or right after a failure).</summary>
    Searching,
    Connected,
    /// <summary>Sonar is reachable but in Stream mode, which this tool does not control.</summary>
    StreamMode,
    /// <summary>Several attempts failed; still retrying with the maximum backoff.</summary>
    Unavailable,
}

/// <summary>
/// Owns discovery + the live <see cref="SonarClient"/>. Re-runs discovery with backoff after any failure,
/// because GG changes both of its local ports whenever it restarts.
/// </summary>
public sealed class SonarConnection : IDisposable
{
    private static readonly int[] BackoffSeconds = { 1, 2, 4, 8, 15, 30 };

    private readonly SonarDiscovery _discovery = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly object _gate = new();
    private SonarClient? _client;
    private ConnectionState _state = ConnectionState.Searching;
    private volatile bool _resetBackoff;

    /// <summary>Non-null only while connected (classic or stream mode).</summary>
    public SonarClient? Client => Volatile.Read(ref _client);

    public ConnectionState State => _state;

    /// <summary>Raised on an arbitrary thread; subscribers must marshal to the UI thread.</summary>
    public event Action<ConnectionState>? StateChanged;

    public async Task RunAsync(CancellationToken ct)
    {
        int attempt = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (Client is null)
                {
                    if (_resetBackoff)
                    {
                        _resetBackoff = false;
                        attempt = 0;
                    }

                    if (await TryConnectAsync(ct).ConfigureAwait(false))
                    {
                        attempt = 0;
                    }
                    else
                    {
                        attempt++;
                        if (attempt >= 3) SetState(ConnectionState.Unavailable);
                    }
                }

                var delay = Client is null
                    ? TimeSpan.FromSeconds(BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)])
                    : Timeout.InfiniteTimeSpan;

                await _wake.WaitAsync(delay, ct).ConfigureAwait(false);
                while (_wake.CurrentCount > 0) _wake.Wait(0, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        var ep = await _discovery.DiscoverAsync(ct).ConfigureAwait(false);
        if (ep is null) return false;
        if (!ep.IsRunning)
        {
            Log.Warn("Sonar sub-app reported as not running");
            return false;
        }

        var client = new SonarClient(_http, ep.BaseUrl);
        try
        {
            var mode = await client.GetModeAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _client, client);
            Log.Info($"Connected to Sonar at {ep.BaseUrl} (mode={mode})");
            SetState(IsClassic(mode) ? ConnectionState.Connected : ConnectionState.StreamMode);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Warn($"Sonar at {ep.BaseUrl} not responding: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Called by the view model after each refresh so mode switches in GG are reflected.</summary>
    public void UpdateMode(string mode)
    {
        if (Client is null) return;
        SetState(IsClassic(mode) ? ConnectionState.Connected : ConnectionState.StreamMode);
    }

    /// <summary>Any HTTP failure against the current client: drop it and re-run discovery immediately.</summary>
    public void ReportFailure(Exception ex)
    {
        var dropped = Interlocked.Exchange(ref _client, null);
        if (dropped is null) return; // already reported
        Log.Warn($"Sonar connection lost: {ex.GetType().Name}: {ex.Message}");
        _resetBackoff = true;
        SetState(ConnectionState.Searching);
        _wake.Release();
    }

    /// <summary>User-triggered (or panel-open-triggered) retry: skip the remaining backoff.</summary>
    public void RetryNow()
    {
        if (Client is not null) return;
        _resetBackoff = true;
        SetState(ConnectionState.Searching);
        _wake.Release();
    }

    private void SetState(ConnectionState state)
    {
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
        }
        StateChanged?.Invoke(state);
    }

    private static bool IsClassic(string mode) => mode.Equals("classic", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _discovery.Dispose();
        _http.Dispose();
        _wake.Dispose();
    }
}
