using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using SonarTray.Hotkeys;
using SonarTray.Models;
using SonarTray.Services;
using SonarTray.Tray;
using SonarTray.ViewModels;
using SonarTray.Views;

namespace SonarTray;

public partial class App : Application
{
    private SingleInstance? _instance;
    private CancellationTokenSource? _cts;
    private SonarConnection? _connection;
    private PopupWindow? _popup;
    private TrayIconHost? _tray;
    private MixerViewModel? _mixer;
    private HotkeyManager? _hotkeys;
    private HotkeyConfig? _hotkeyConfig;
    private OsdWindow? _osd;
    private AppSettings? _settings;
    private UpdateChecker? _updates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.VerboseEnabled = e.Args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        // Culture has to be settled before any window is built: XAML resolves {x:Static} at parse
        // time, so a later change would leave the already-loaded panel in the previous language.
        _settings = AppSettings.Load();
        ApplyCulture(_settings);

        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunSmokeAsync();
            return;
        }

        if (e.Args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunProbeAsync();
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            _instance.SignalExistingInstance();
            _instance.Dispose();
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled dispatcher exception", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"SonarTray {typeof(App).Assembly.GetName().Version} starting ({Environment.ProcessPath})");
        StartupManager.RepairIfMoved();

        _cts = new CancellationTokenSource();
        _connection = new SonarConnection();
        // Hotkeys come up before the view models so the settings page can report, from the start,
        // which combinations the system actually accepted.
        _hotkeyConfig = HotkeyConfig.Load();
        _hotkeys = new HotkeyManager(_hotkeyConfig, OnHotkey);

        // The settings page needs to tell the mixer when a channel is shown or hidden, and the
        // mixer does not exist yet - hence the deferred callback rather than a direct reference.
        MixerViewModel? mixer = null;
        var settingsVm = new SettingsViewModel(_settings, _hotkeyConfig, () => mixer?.RebuildSubChannels());
        _mixer = mixer = new MixerViewModel(_connection, new HotkeySettingsViewModel(_hotkeyConfig, _hotkeys),
                                            settingsVm, PresetStore.Load(),
                                            exit: () => Shutdown(0), openGg: GgLauncher.ShowGg);
        _popup = new PopupWindow(_mixer);
        new WindowInteropHelper(_popup).EnsureHandle();
        _tray = new TrayIconHost(_connection, _mixer, _popup, GgLauncher.ShowGg, onWheel: OnTrayWheel);
        _tray.WheelEnabled = _settings.TrayWheelVolume;
        settingsVm.TrayWheelChanged += () => { if (_tray is not null && _settings is not null) _tray.WheelEnabled = _settings.TrayWheelVolume; };
        _osd = new OsdWindow();
        new WindowInteropHelper(_osd).EnsureHandle();
        ApplyOsdSettings();
        settingsVm.OsdSettingsChanged += ApplyOsdSettings;
        _ = _connection.RunAsync(_cts.Token);

        _ = CheckForUpdatesAsync(settingsVm, _cts.Token);

        // A later launch of the exe raises this instead of starting a second tray icon.
        _instance.StartListening(() => Dispatcher.BeginInvoke(new Action(() => _popup?.ShowAtTray())));

        // `--show`, `--show-settings`, `--show-hotkeys`: open the panel on a given page immediately.
        // A development and screenshot aid - tools/Capture-Screenshots.ps1 drives all three.
        var page = e.Args.Contains("--show-settings", StringComparer.OrdinalIgnoreCase) ? PanelPage.Settings
                 : e.Args.Contains("--show-hotkeys", StringComparer.OrdinalIgnoreCase) ? PanelPage.Hotkeys
                 : e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase) ? PanelPage.Mixer
                 : (PanelPage?)null;

        // `--show-osd`: park the on-screen display on screen long enough to photograph it.
        if (e.Args.Contains("--show-osd", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _osd.HoldDuration = TimeSpan.FromMinutes(5);
                _osd.Enabled = true;
                _osd.Show(_mixer.Master);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        if (page is { } startPage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _popup.ShowAtTray();
                _mixer.Page = startPage;
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// English is the neutral resource set, so "follow the system" means leaving CurrentUICulture
    /// alone and letting ResourceManager fall back. An explicit choice overrides both the resource
    /// lookup and the thread culture, so numbers and dates match the chosen language too.
    /// </summary>
    private static void ApplyCulture(AppSettings settings)
    {
        var culture = settings.ResolveCulture();
        if (culture is null)
        {
            Log.Info($"Language: system ({CultureInfo.CurrentUICulture.Name})");
            return;
        }

        // Fully qualified: inside Application, a bare Resources binds to Application.Resources.
        SonarTray.Resources.Strings.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Log.Info($"Language: {culture.Name} (from settings)");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("SonarTray exiting");
        _cts?.Cancel();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_osd is not null)
        {
            _osd.AllowClose = true;
            _osd.Close();
        }
        if (_popup is not null)
        {
            _popup.AllowClose = true;
            _popup.Close();
        }
        _connection?.Dispose();
        _updates?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Hotkeys arrive on the UI thread (the message-only window is created here), so the view
    /// models can be touched directly. Audio actions are ignored while disconnected: the local
    /// value would change and then be overwritten by the next poll.
    /// </summary>
    /// <summary>What a hotkey does to a channel.</summary>
    internal enum ChannelOp { ToggleMute, Up, Down }

    /// <summary>
    /// Every action except TogglePanel is "do one of three things to one channel", so they are
    /// handled by one code path rather than a case per action. A missing entry means the action
    /// silently does nothing, which the HotkeyActionsAreAllMapped test exists to prevent.
    /// </summary>
    internal static readonly IReadOnlyDictionary<HotkeyAction, (ChannelKind Kind, ChannelOp Op)> ChannelActions =
        new Dictionary<HotkeyAction, (ChannelKind, ChannelOp)>
        {
            [HotkeyAction.MicMute] = (ChannelKind.Mic, ChannelOp.ToggleMute),
            [HotkeyAction.MasterMute] = (ChannelKind.Master, ChannelOp.ToggleMute),
            [HotkeyAction.MasterUp] = (ChannelKind.Master, ChannelOp.Up),
            [HotkeyAction.MasterDown] = (ChannelKind.Master, ChannelOp.Down),
            [HotkeyAction.GameMute] = (ChannelKind.Game, ChannelOp.ToggleMute),
            [HotkeyAction.GameUp] = (ChannelKind.Game, ChannelOp.Up),
            [HotkeyAction.GameDown] = (ChannelKind.Game, ChannelOp.Down),
            [HotkeyAction.ChatMute] = (ChannelKind.Chat, ChannelOp.ToggleMute),
            [HotkeyAction.ChatUp] = (ChannelKind.Chat, ChannelOp.Up),
            [HotkeyAction.ChatDown] = (ChannelKind.Chat, ChannelOp.Down),
            [HotkeyAction.MediaMute] = (ChannelKind.Media, ChannelOp.ToggleMute),
            [HotkeyAction.MediaUp] = (ChannelKind.Media, ChannelOp.Up),
            [HotkeyAction.MediaDown] = (ChannelKind.Media, ChannelOp.Down),
        };

    private void OnHotkey(HotkeyAction action)
    {
        if (_mixer is null) return;
        Log.Verbose($"Hotkey fired: {action}");

        if (action == HotkeyAction.TogglePanel)
        {
            _popup?.Toggle();
            return;
        }

        if (!_mixer.IsConnected)
        {
            Log.Verbose($"Hotkey {action} ignored: not connected");
            return;
        }

        if (!ChannelActions.TryGetValue(action, out var target))
        {
            Log.Warn($"Hotkey {action} has no channel mapping");
            return;
        }

        // Aux is hidden unless the user turned it on, so a channel can legitimately be absent.
        if (_mixer.ChannelOf(target.Kind) is not { } channel)
        {
            Log.Verbose($"Hotkey {action} ignored: {target.Kind} is not shown");
            return;
        }

        switch (target.Op)
        {
            case ChannelOp.ToggleMute:
                channel.IsMuted = !channel.IsMuted;
                break;

            case ChannelOp.Up:
            case ChannelOp.Down:
                double raw = _hotkeyConfig?.VolumeStep ?? 0.05;
                double step = target.Op == ChannelOp.Up ? raw : -raw;
                channel.Volume = Math.Clamp(channel.Volume + step, 0.0, 1.0);
                channel.FlushVolume(); // no reason to sit through the drag debounce for a keypress
                break;
        }

        _osd?.Show(channel);
    }

    /// <summary>
    /// Runs the update check well after startup, so it never competes with finding Sonar - which
    /// is the thing the user is actually waiting for.
    /// </summary>
    private async Task CheckForUpdatesAsync(SettingsViewModel settingsVm, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);

            var raw = typeof(App).Assembly.GetName().Version?.ToString();
            if (_settings is null || !SemanticVersion.TryParse(raw, out var current))
            {
                Log.Warn($"Cannot check for updates: unreadable assembly version '{raw}'");
                return;
            }

            _updates = new UpdateChecker(_settings, current);
            var info = await _updates.CheckAsync(ct).ConfigureAwait(false);
            if (info is null) return;

            await Dispatcher.InvokeAsync(() => settingsVm.Update = info);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A wheel notch over the tray icon moves the master volume by the same step the up/down
    /// shortcuts use. Runs on the UI thread: the hook is installed from it.
    /// </summary>
    private void OnTrayWheel(int notches)
    {
        if (_mixer is null || !_mixer.IsConnected) return;

        double step = (_hotkeyConfig?.VolumeStep ?? 0.05) * notches;
        var master = _mixer.Master;
        master.Volume = Math.Clamp(master.Volume + step, 0.0, 1.0);
        master.FlushVolume();
        _osd?.Show(master);
    }

    /// <summary>Pushes the OSD settings into the window; also called whenever they change.</summary>
    private void ApplyOsdSettings()
    {
        if (_osd is null || _settings is null) return;
        _osd.Enabled = _settings.OsdEnabled;
        _osd.HoldDuration = TimeSpan.FromMilliseconds(_settings.OsdDurationMs);
    }

    /// <summary>
    /// `SonarTray.exe --probe`: re-derives the endpoint table in docs/sonar-api.md. Read-only.
    /// </summary>
    private async Task RunProbeAsync()
    {
        int code;
        try
        {
            code = await ApiProbe.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error("API probe failed", ex);
            code = 1;
        }
        await Dispatcher.InvokeAsync(() => Shutdown(code));
    }

    /// <summary>
    /// `SonarTray.exe --smoke`: discovers Sonar, reads everything, performs a few
    /// reversible writes and exits. Results go to the log file.
    /// </summary>
    private async Task RunSmokeAsync()
    {
        int code = 0;
        try
        {
            Log.Info("=== SMOKE TEST START ===");
            using var discovery = new SonarDiscovery();
            var ep = await discovery.DiscoverAsync(CancellationToken.None);
            if (ep is null)
            {
                Log.Error("Discovery failed (see warnings above)");
                code = 2;
                return;
            }
            Log.Info($"Endpoint: {ep.BaseUrl} running={ep.IsRunning} ready={ep.IsReady}");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var c = new SonarClient(http, ep.BaseUrl);
            var ct = CancellationToken.None;

            Log.Info("mode = " + await c.GetModeAsync(ct));

            var vol = await c.GetVolumesAsync(ct);
            Log.Info("volumes = " + JsonSerializer.Serialize(vol));

            var devices = await c.GetAudioDevicesAsync(ct) ?? new();
            foreach (var d in devices)
                Log.Info($"device {d.DataFlow,-7} role={d.Role,-11} vad={d.IsVad,-5} state={d.State,-6} {d.FriendlyName}  [{d.Id}]");

            var reds = await c.GetRedirectionsAsync(ct) ?? new();
            foreach (var r in reds)
                Log.Info($"redirection {r.Id,-5} -> {r.DeviceId} running={r.IsRunning}");

            // --- reversible writes on the Game channel ---
            var game = FindClassic(vol, "game");
            double origVol = game?.Volume ?? 1.0;
            bool origMuted = game?.Muted ?? false;

            double testVol = Math.Abs(origVol - 0.5) < 0.01 ? 0.6 : 0.5;
            await c.SetVolumeAsync("game", testVol, ct);
            var gameAfter = FindClassic(await c.GetVolumesAsync(ct), "game");
            Log.Info($"set game volume {Inv(testVol)} -> read back {Inv(gameAfter?.Volume)}");
            await c.SetVolumeAsync("game", origVol, ct);

            await c.SetMuteAsync("game", !origMuted, ct);
            gameAfter = FindClassic(await c.GetVolumesAsync(ct), "game");
            Log.Info($"set game muted {!origMuted} -> read back {gameAfter?.Muted}");
            await c.SetMuteAsync("game", origMuted, ct);

            var gameRed = reds.FirstOrDefault(r => r.Id.Equals("game", StringComparison.OrdinalIgnoreCase));
            if (gameRed?.DeviceId is string did)
            {
                await c.SetRedirectionDeviceAsync("game", did, ct);
                Log.Info($"re-assigned game redirection to its current device OK (method={c.RedirectionMethod})");
            }

            // --- audio profiles: read the list, then re-select what is already selected ---
            var selectedConfigs = await c.GetSelectedConfigsAsync(ct) ?? new();
            foreach (var cfg in selectedConfigs)
                Log.Info($"selected profile {cfg.VirtualAudioDevice,-12} {cfg.Name}  [{cfg.Id}]");

            var allConfigs = await c.GetConfigsAsync(ct) ?? new();
            Log.Info($"profiles available: {allConfigs.Count} across " +
                     $"{allConfigs.Select(x => x.VirtualAudioDevice).Distinct().Count()} devices");

            if (selectedConfigs.FirstOrDefault() is { } current)
            {
                await c.SelectConfigAsync(current.Id, ct);
                Log.Info($"re-selected the profile already in use for {current.VirtualAudioDevice} OK");
            }

            // --- stream mode: read only; switching modes is not something a smoke test should do ---
            var streamVolumes = await c.GetStreamVolumesAsync(ct);
            Log.Info("stream volumes = " + JsonSerializer.Serialize(streamVolumes));

            foreach (var sr in await c.GetStreamRedirectionsAsync(ct) ?? new())
                Log.Info($"stream redirection {sr.Id,-11} device={sr.DeviceId} running={sr.IsRunning} " +
                         $"roles={string.Join(",", sr.Status?.Where(x => x.IsEnabled).Select(x => x.Role) ?? Array.Empty<string>())}");

            Log.Info("=== SMOKE TEST DONE ===");
        }
        catch (Exception ex)
        {
            Log.Error("Smoke test failed", ex);
            code = 1;
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => Shutdown(code));
        }

        static Models.VolumeStateDto? FindClassic(Models.VolumeSettingsDto? v, string id)
            => v?.Devices?.FirstOrDefault(kv => kv.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value?.Classic;

        static string Inv(double? d) => d?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
    }
}
