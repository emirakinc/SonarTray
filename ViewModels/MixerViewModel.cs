using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using SonarTray.Models;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>Whole-panel state. Created and used on the UI thread.</summary>
public sealed class MixerViewModel : ObservableObject
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// While the panel is closed the fast poll is off, so master volume would freeze at whatever
    /// it was when the panel last closed - and that is exactly the value the tray icon draws.
    /// This slower poll fetches only the volume document to keep the icon honest.
    /// </summary>
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(8);

    private readonly SonarConnection _connection;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _idlePoll;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _idleCts;
    private ConnectionState _state;
    private bool _panelOpen;
    private bool _refreshing;

    public ObservableCollection<ChannelViewModel> Channels { get; }

    /// <summary>The master row, rendered on its own card above the rest.</summary>
    public ChannelViewModel Master { get; }

    /// <summary>Everything except master, in <see cref="ChannelSpec.VisibleSpecs"/> order.</summary>
    public IReadOnlyList<ChannelViewModel> SubChannels { get; }

    public ICommand RetryCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenGgCommand { get; }

    public MixerViewModel(SonarConnection connection, Action exit, Action openGg)
    {
        _connection = connection;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _state = connection.State;

        Channels = new ObservableCollection<ChannelViewModel>(
            ChannelSpec.VisibleSpecs.Select(spec => new ChannelViewModel(spec, connection)));

        // Never Channels[0]: VisibleSpecs filters on a flag, so the order is not guaranteed.
        Master = Channels.First(c => c.Spec.Kind == ChannelKind.Master);
        SubChannels = Channels.Where(c => c.Spec.Kind != ChannelKind.Master).ToList();

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += (_, _) => _ = RefreshAsync();

        _idlePoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = IdlePollInterval };
        _idlePoll.Tick += (_, _) => _ = RefreshVolumesAsync();
        _idlePoll.Start(); // the panel starts closed

        RetryCommand = new RelayCommand(() => _connection.RetryNow());
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ExitCommand = new RelayCommand(exit);
        OpenGgCommand = new RelayCommand(openGg);

        connection.StateChanged += state => _dispatcher.BeginInvoke(new Action(() => OnConnectionStateChanged(state)));
    }

    // ---- connection state ----------------------------------------------

    public ConnectionState ConnectionState
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsStreamMode));
            OnPropertyChanged(nameof(IsOffline));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsConnected => _state == ConnectionState.Connected;
    public bool IsStreamMode => _state == ConnectionState.StreamMode;
    public bool IsOffline => _state is ConnectionState.Searching or ConnectionState.Unavailable;

    public string StatusText => _state switch
    {
        ConnectionState.Searching => "Sonar aranıyor…",
        ConnectionState.Connected => "Bağlı",
        ConnectionState.StreamMode => "Stream modu",
        _ => "Sonar bulunamadı",
    };

    private void OnConnectionStateChanged(ConnectionState state)
    {
        ConnectionState = state;
        if (state is not (ConnectionState.Connected or ConnectionState.StreamMode)) return;

        if (_panelOpen) _ = RefreshAsync();
        else _ = RefreshVolumesAsync(); // reconnected while hidden: refresh the tray icon now, not in 8s
    }

    // ---- startup toggle -------------------------------------------------

    public bool StartWithWindows
    {
        get => StartupManager.IsEnabled;
        set
        {
            try { StartupManager.SetEnabled(value); }
            catch (Exception ex) { Log.Error("Startup toggle failed", ex); }
            OnPropertyChanged();
        }
    }

    // ---- panel lifecycle ------------------------------------------------

    public void OnPanelOpened()
    {
        _panelOpen = true;
        _idlePoll.Stop();
        _idleCts?.Cancel();
        OnPropertyChanged(nameof(StartWithWindows));
        if (IsOffline) _connection.RetryNow();
        _ = RefreshAsync();
        _poll.Start();
    }

    public void OnPanelClosed()
    {
        _panelOpen = false;
        _poll.Stop();
        _refreshCts?.Cancel();
        _idlePoll.Start();
    }

    // ---- refresh --------------------------------------------------------

    public async Task RefreshAsync()
    {
        var client = _connection.Client;
        if (client is null || _refreshing) return;

        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        _refreshing = true;
        try
        {
            var modeTask = client.GetModeAsync(ct);
            var volumesTask = client.GetVolumesAsync(ct);
            var devicesTask = client.GetAudioDevicesAsync(ct);
            var redirectionsTask = client.GetRedirectionsAsync(ct);
            await Task.WhenAll(modeTask, volumesTask, devicesTask, redirectionsTask);

            _connection.UpdateMode(modeTask.Result);
            Apply(volumesTask.Result, devicesTask.Result, redirectionsTask.Result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // panel closed mid-refresh
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// The cheapest of the four reads, for the closed-panel poll. Devices and redirections cannot
    /// have changed in any way the hidden UI would show, so they are not fetched.
    /// </summary>
    private async Task RefreshVolumesAsync()
    {
        var client = _connection.Client;
        if (client is null || _panelOpen || _refreshing) return;

        _idleCts?.Cancel();
        _idleCts = new CancellationTokenSource();
        var ct = _idleCts.Token;
        try
        {
            var volumes = await client.GetVolumesAsync(ct);
            foreach (var channel in Channels)
                channel.ApplyVolumeFromServer(StateFor(volumes, channel.Spec));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // panel opened mid-poll; the full refresh supersedes this
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
    }

    private void Apply(VolumeSettingsDto? volumes, List<AudioDeviceDto>? devices, List<RedirectionDto>? redirections)
    {
        var render = new List<AudioDeviceItem>();
        var capture = new List<AudioDeviceItem>();
        foreach (var d in devices ?? new())
        {
            if (!IsSelectable(d)) continue;
            var item = new AudioDeviceItem(d.Id, d.FriendlyName);
            if (d.DataFlow.Equals("render", StringComparison.OrdinalIgnoreCase)) render.Add(item);
            else if (d.DataFlow.Equals("capture", StringComparison.OrdinalIgnoreCase)) capture.Add(item);
        }
        render.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        capture.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        foreach (var channel in Channels)
        {
            var spec = channel.Spec;

            RedirectionDto? redirection = spec.RedirectionId is null
                ? null
                : redirections?.FirstOrDefault(r => r.Id.Equals(spec.RedirectionId, StringComparison.OrdinalIgnoreCase));

            var list = string.Equals(spec.DataFlow, "capture", StringComparison.OrdinalIgnoreCase) ? capture : render;
            channel.ApplyFromServer(StateFor(volumes, spec), redirection?.DeviceId, redirection?.IsRunning ?? true, list);
        }
    }

    private static VolumeStateDto? StateFor(VolumeSettingsDto? volumes, ChannelSpec spec)
        => spec.Kind == ChannelKind.Master
            ? volumes?.Masters?.Classic
            : volumes?.Devices?.FirstOrDefault(kv => kv.Key.Equals(spec.VolumeId, StringComparison.OrdinalIgnoreCase)).Value?.Classic;

    /// <summary>Real, active devices only; Sonar's own virtual endpoints are never valid targets.</summary>
    private static bool IsSelectable(AudioDeviceDto d)
        => !d.IsVad
           && d.Role.Equals("none", StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrEmpty(d.State) || d.State.Equals("active", StringComparison.OrdinalIgnoreCase))
           && !string.IsNullOrEmpty(d.Id);
}
