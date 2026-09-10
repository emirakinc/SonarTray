using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using SonarTray.Services;
using SonarTray.Tray;
using SonarTray.ViewModels;
using SonarTray.Views;

namespace SonarTray;

public partial class App : Application
{
    private const string MutexName = @"Local\SonarTray-7C2E1A0B-5F7D-4B6E-9C3A-2D1F0E8B4A61";

    private static Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _cts;
    private SonarConnection? _connection;
    private PopupWindow? _popup;
    private TrayIconHost? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.VerboseEnabled = e.Args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            _ = RunSmokeAsync();
            return;
        }

        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            Log.Info("Another instance is already running; exiting.");
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
        var mixer = new MixerViewModel(_connection, exit: () => Shutdown(0), openGg: GgLauncher.ShowGg);
        _popup = new PopupWindow(mixer);
        new WindowInteropHelper(_popup).EnsureHandle();
        _tray = new TrayIconHost(_connection, mixer, _popup, GgLauncher.ShowGg);
        _ = _connection.RunAsync(_cts.Token);

        // `--show`: open the panel immediately (development / screenshot aid)
        if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(new Action(_popup.ShowAtTray), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("SonarTray exiting");
        _cts?.Cancel();
        _tray?.Dispose();
        if (_popup is not null)
        {
            _popup.AllowClose = true;
            _popup.Close();
        }
        _connection?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
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
