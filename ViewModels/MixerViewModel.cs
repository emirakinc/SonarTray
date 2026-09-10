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

    private readonly SonarConnection _connection;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _poll;
    private CancellationTokenSource? _refreshCts;
    private ConnectionState _state;
    private bool _panelOpen;
    private bool _refreshing;

    public ObservableCollection<ChannelViewModel> Channels { get; }

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

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += (_, _) => _ = RefreshAsync();

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
        if (_panelOpen && state is ConnectionState.Connected or ConnectionState.StreamMode)
            _ = RefreshAsync();
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

            VolumeStateDto? state = spec.Kind == ChannelKind.Master
                ? volumes?.Masters?.Classic
                : volumes?.Devices?.FirstOrDefault(kv => kv.Key.Equals(spec.VolumeId, StringComparison.OrdinalIgnoreCase)).Value?.Classic;

            RedirectionDto? redirection = spec.RedirectionId is null
                ? null
                : redirections?.FirstOrDefault(r => r.Id.Equals(spec.RedirectionId, StringComparison.OrdinalIgnoreCase));

            var list = string.Equals(spec.DataFlow, "capture", StringComparison.OrdinalIgnoreCase) ? capture : render;
            channel.ApplyFromServer(state, redirection?.DeviceId, redirection?.IsRunning ?? true, list);
        }
    }

    /// <summary>Real, active devices only; Sonar's own virtual endpoints are never valid targets.</summary>
    private static bool IsSelectable(AudioDeviceDto d)
        => !d.IsVad
           && d.Role.Equals("none", StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrEmpty(d.State) || d.State.Equals("active", StringComparison.OrdinalIgnoreCase))
           && !string.IsNullOrEmpty(d.Id);
}
