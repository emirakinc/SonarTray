using System.Globalization;
using System.Windows.Input;
using SonarTray.Hotkeys;
using SonarTray.Resources;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>One entry in the language dropdown.</summary>
public sealed record LanguageOption(string Code, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// The settings page. Every setter writes straight to disk: the panel has no OK/Cancel, so a
/// change the user can see must be a change that survives a restart.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly HotkeyConfig _hotkeys;
    private readonly Action _onChannelVisibilityChanged;

    /// <summary>Raised when an OSD setting changes, so the window can pick it up immediately.</summary>
    public event Action? OsdSettingsChanged;

    /// <summary>Raised when the tray wheel setting changes, so the hook is added or removed now.</summary>
    public event Action? TrayWheelChanged;

    public SettingsViewModel(AppSettings settings, HotkeyConfig hotkeys, Action onChannelVisibilityChanged)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _onChannelVisibilityChanged = onChannelVisibilityChanged;

        Languages = BuildLanguageOptions();
        OpenLogCommand = new RelayCommand(OpenLogFolder);
        DownloadUpdateCommand = new RelayCommand(OpenReleasePage);
        SkipUpdateCommand = new RelayCommand(SkipThisUpdate);
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public ICommand OpenLogCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand SkipUpdateCommand { get; }

    /// <summary>
    /// "System language" plus one entry per embedded language, each labelled in its own language
    /// so it is readable to whoever is looking for it.
    /// </summary>
    private static List<LanguageOption> BuildLanguageOptions()
    {
        var options = new List<LanguageOption> { new("auto", Strings.Settings_LanguageAuto) };

        foreach (var code in Strings.SupportedLanguages)
        {
            var native = CultureInfo.GetCultureInfo(code).NativeName;
            options.Add(new LanguageOption(code, char.ToUpper(native[0], CultureInfo.InvariantCulture) + native[1..]));
        }

        return options;
    }

    // ---- language -------------------------------------------------------

    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => string.Equals(l.Code, _settings.Language, StringComparison.OrdinalIgnoreCase))
               ?? Languages[0];
        set
        {
            if (value is null || string.Equals(value.Code, _settings.Language, StringComparison.OrdinalIgnoreCase)) return;
            _settings.Language = value.Code;
            _settings.Save();
            ShowRestartHint = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowRestartHint));
            Log.Info($"Language set to '{value.Code}' (applies on restart)");
        }
    }

    /// <summary>
    /// XAML resolves {x:Static} once, at parse time, so the already-built panel keeps the old
    /// language until the app restarts. Saying so is honest and cheaper than rebuilding every view.
    /// </summary>
    public bool ShowRestartHint { get; private set; }

    // ---- channels -------------------------------------------------------

    public bool ShowAux
    {
        get => _settings.ShowAux;
        set
        {
            if (_settings.ShowAux == value) return;
            _settings.ShowAux = value;
            _settings.Save();
            OnPropertyChanged();
            _onChannelVisibilityChanged();
        }
    }

    // ---- hotkey volume step ---------------------------------------------

    /// <summary>Shown and edited as a percentage; stored as a 0..1 fraction.</summary>
    public double VolumeStepPercent
    {
        get => Math.Round(_hotkeys.VolumeStep * 100);
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 1, 50);
            if (Math.Abs(VolumeStepPercent - clamped) < 0.5) return;
            _hotkeys.VolumeStep = clamped / 100.0;
            _hotkeys.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeStepDisplay));
        }
    }

    public string VolumeStepDisplay => Strings.Format("Osd_Percent", (int)VolumeStepPercent);

    // ---- on-screen display ----------------------------------------------

    public bool OsdEnabled
    {
        get => _settings.OsdEnabled;
        set
        {
            if (_settings.OsdEnabled == value) return;
            _settings.OsdEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            OsdSettingsChanged?.Invoke();
        }
    }

    public double OsdSeconds
    {
        get => Math.Round(_settings.OsdDurationMs / 1000.0, 1);
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 1), 0.3, 10.0);
            if (Math.Abs(OsdSeconds - clamped) < 0.05) return;
            _settings.OsdDurationMs = (int)Math.Round(clamped * 1000);
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OsdSecondsDisplay));
            OsdSettingsChanged?.Invoke();
        }
    }

    public string OsdSecondsDisplay => Strings.Format("Settings_Seconds", OsdSeconds);

    // ---- tray ------------------------------------------------------------

    public bool TrayWheelVolume
    {
        get => _settings.TrayWheelVolume;
        set
        {
            if (_settings.TrayWheelVolume == value) return;
            _settings.TrayWheelVolume = value;
            _settings.Save();
            OnPropertyChanged();
            TrayWheelChanged?.Invoke();
        }
    }

    // ---- updates ---------------------------------------------------------

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // ---- update notice ----------------------------------------------------

    private UpdateInfo? _update;

    /// <summary>Set by the background update check; null until (and unless) one is found.</summary>
    public UpdateInfo? Update
    {
        get => _update;
        set
        {
            _update = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateText));
        }
    }

    public bool HasUpdate => _update is not null;

    public string UpdateText => _update is null ? "" : Strings.Format("Update_Available", _update.Version.ToString());

    private void OpenReleasePage()
    {
        if (_update is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_update.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open the release page: {ex.Message}");
        }
    }

    /// <summary>Stops offering this version, and anything older, from now on.</summary>
    private void SkipThisUpdate()
    {
        if (_update is null) return;
        _settings.SkippedVersion = _update.Version.ToString();
        _settings.Save();
        Log.Info($"Update {_update.Version} skipped at the user's request");
        Update = null;
    }

    private static void OpenLogFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(Log.FilePath);
            if (string.IsNullOrEmpty(dir)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open the log folder: {ex.Message}");
        }
    }
}
