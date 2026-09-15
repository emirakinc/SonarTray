using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using SonarTray.Models;
using SonarTray.Resources;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>The panel shows exactly one of these at a time.</summary>
public enum PanelPage { Mixer, Settings, Hotkeys }

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
    private bool _switchingMode;
    private PanelPage _page = PanelPage.Mixer;

    public ObservableCollection<ChannelViewModel> Channels { get; }

    /// <summary>The master row, rendered on its own card above the rest.</summary>
    public ChannelViewModel Master { get; }

    /// <summary>
    /// Everything except master, in spec order, minus any channel the user has switched off.
    /// Rebuilt in place rather than replaced so the panel's ItemsControl keeps its bindings.
    /// </summary>
    public ObservableCollection<ChannelViewModel> SubChannels { get; }

    /// <summary>Raised when a channel is shown or hidden, so the window can resize itself.</summary>
    public event Action? SubChannelsChanged;

    /// <summary>Null when the kind is not among the visible specs.</summary>
    public ChannelViewModel? ChannelOf(ChannelKind kind)
        => Channels.FirstOrDefault(c => c.Spec.Kind == kind);

    /// <summary>The hotkey page, shown in place of the mixer rows.</summary>
    public HotkeySettingsViewModel Hotkeys { get; }

    /// <summary>The settings page, shown in place of the mixer rows.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>The presets popup, reached from the title bar.</summary>
    public PresetsViewModel Presets { get; }

    /// <summary>Which of the three pages the panel is showing.</summary>
    public PanelPage Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(IsMixerVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(IsHotkeysVisible));
            OnPropertyChanged(nameof(IsSettingsOpen));

            // Leaving the hotkey page mid-capture would otherwise keep the manager suspended,
            // which silently disables every global shortcut until the page is opened again.
            if (value != PanelPage.Hotkeys) Hotkeys.CancelCapture();
        }
    }

    /// <summary>True on any page other than the mixer; drives the Escape key and the title bar.</summary>
    public bool IsSettingsOpen => _page != PanelPage.Mixer;

    /// <summary>Rows and their offline/stream overlays hide while another page is up.</summary>
    public bool IsMixerVisible => _page == PanelPage.Mixer;
    public bool IsSettingsVisible => _page == PanelPage.Settings;
    public bool IsHotkeysVisible => _page == PanelPage.Hotkeys;

    public ICommand ToggleSettingsCommand { get; }
    public ICommand SwitchModeCommand { get; }
    public ICommand ShowHotkeysCommand { get; }
    public ICommand BackToMixerCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenGgCommand { get; }

    public MixerViewModel(SonarConnection connection, HotkeySettingsViewModel hotkeys,
                          SettingsViewModel settings, PresetStore presets, Action exit, Action openGg)
    {
        _connection = connection;
        Hotkeys = hotkeys;
        Settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _state = connection.State;

        // Every channel is constructed, including the optional ones: a hidden channel still needs
        // a view model so its hotkeys work and the next poll has somewhere to land.
        Channels = new ObservableCollection<ChannelViewModel>(
            ChannelSpec.All.Select(spec => new ChannelViewModel(spec, connection)));

        // Never Channels[0]: the spec order is not guaranteed to start with master.
        Master = Channels.First(c => c.Spec.Kind == ChannelKind.Master);
        SubChannels = new ObservableCollection<ChannelViewModel>();
        RebuildSubChannels();

        // Passed as a callback rather than the collection itself: a preset covers every channel,
        // including any the user currently has hidden.
        Presets = new PresetsViewModel(presets, () => Channels);

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += (_, _) => _ = RefreshAsync();

        _idlePoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = IdlePollInterval };
        _idlePoll.Tick += (_, _) => _ = RefreshVolumesAsync();
        _idlePoll.Start(); // the panel starts closed

        ToggleSettingsCommand = new RelayCommand(
            () => Page = Page == PanelPage.Mixer ? PanelPage.Settings : PanelPage.Mixer);
        SwitchModeCommand = new RelayCommand(() => _ = SwitchModeAsync());
        ShowHotkeysCommand = new RelayCommand(() => Page = PanelPage.Hotkeys);
        BackToMixerCommand = new RelayCommand(() => Page = PanelPage.Mixer);
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
        ConnectionState.Searching => Strings.Status_Searching,
        ConnectionState.Connected => Strings.Status_Connected,
        ConnectionState.StreamMode => Strings.Status_StreamMode,
        _ => Strings.Status_NotFound,
    };

    /// <summary>
    /// Flips Sonar between Classic and Stream. The state comes back through the normal refresh
    /// rather than being assumed here: GG can refuse, and guessing would leave the panel lying.
    /// </summary>
    private async Task SwitchModeAsync()
    {
        var client = _connection.Client;
        if (client is null || _switchingMode) return;

        _switchingMode = true;
        try
        {
            var target = IsStreamMode ? "classic" : "stream";
            await client.SetModeAsync(target, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
        finally
        {
            _switchingMode = false;
        }
    }

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

    /// <summary>
    /// Applies the current visibility settings. <see cref="ChannelSpec.Visible"/> is the default;
    /// Aux is the one channel the user can turn back on.
    /// </summary>
    public void RebuildSubChannels()
    {
        var wanted = Channels
            .Where(c => c.Spec.Kind != ChannelKind.Master)
            .Where(c => c.Spec.Visible || (c.Spec.Kind == ChannelKind.Aux && Settings.ShowAux))
            .ToList();

        if (wanted.Count == SubChannels.Count && wanted.SequenceEqual(SubChannels)) return;

        SubChannels.Clear();
        foreach (var channel in wanted) SubChannels.Add(channel);
        SubChannelsChanged?.Invoke();
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
        _ = RefreshProfilesAsync();
        _poll.Start();
    }

    /// <summary>
    /// Which profile each channel is on. Kept out of <see cref="RefreshAsync"/> deliberately: at
    /// roughly 16 KB of JSON it is an order of magnitude more expensive than the volume document,
    /// and nothing changes it except the user - here or in GG.
    /// </summary>
    public async Task RefreshProfilesAsync()
    {
        var client = _connection.Client;
        if (client is null) return;

        try
        {
            var selected = await client.GetSelectedConfigsAsync(CancellationToken.None);
            if (selected is null) return;

            foreach (var channel in Channels)
            {
                if (!channel.HasProfilePicker) continue;
                channel.ApplySelectedProfile(
                    selected.FirstOrDefault(c => string.Equals(c.VirtualAudioDevice, channel.Spec.VolumeId,
                                                               StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
    }

    public void OnPanelClosed()
    {
        _panelOpen = false;
        Page = PanelPage.Mixer; // always reopen on the mixer
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
        _refreshCts?.Dispose();
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
        _idleCts?.Dispose();
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
