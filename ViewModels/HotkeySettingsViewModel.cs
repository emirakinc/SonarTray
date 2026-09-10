using System.Windows.Input;
using SonarTray.Hotkeys;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>One rebindable row.</summary>
public sealed class HotkeyBindingViewModel : ObservableObject
{
    private string _gesture = string.Empty;
    private bool _isCapturing;
    private bool _isBlocked;

    public HotkeyAction Action { get; }
    public string Label { get; }

    public HotkeyBindingViewModel(HotkeyAction action, string label)
    {
        Action = action;
        Label = label;
    }

    public string Gesture
    {
        get => _gesture;
        set
        {
            if (!SetProperty(ref _gesture, value)) return;
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (!SetProperty(ref _isCapturing, value)) return;
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>Parsed and non-empty, but the system refused it - almost always already taken.</summary>
    public bool IsBlocked
    {
        get => _isBlocked;
        set => SetProperty(ref _isBlocked, value);
    }

    public string Display => IsCapturing
        ? "Tuşa basın…"
        : string.IsNullOrWhiteSpace(Gesture) ? "Yok" : Gesture;
}

/// <summary>
/// The hotkey page. Capture is driven by the window's PreviewKeyDown, because the panel's controls
/// are all Focusable=False and the keystroke has to be read before anything else consumes it.
/// </summary>
public sealed class HotkeySettingsViewModel : ObservableObject
{
    private readonly HotkeyConfig _config;
    private readonly HotkeyManager _manager;
    private HotkeyBindingViewModel? _capturing;

    public IReadOnlyList<HotkeyBindingViewModel> Bindings { get; }

    public ICommand CaptureCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ResetCommand { get; }

    public bool IsCapturing => _capturing is not null;

    public HotkeySettingsViewModel(HotkeyConfig config, HotkeyManager manager)
    {
        _config = config;
        _manager = manager;

        Bindings = new List<HotkeyBindingViewModel>
        {
            new(HotkeyAction.MicMute, "Mikrofon sustur/aç"),
            new(HotkeyAction.MasterMute, "Ana ses sustur/aç"),
            new(HotkeyAction.MasterDown, "Ana sesi azalt"),
            new(HotkeyAction.MasterUp, "Ana sesi artır"),
            new(HotkeyAction.TogglePanel, "Paneli aç/kapat"),
        };

        CaptureCommand = new RelayCommand<HotkeyBindingViewModel>(BeginCapture);
        ClearCommand = new RelayCommand<HotkeyBindingViewModel>(b => Assign(b, string.Empty));
        ResetCommand = new RelayCommand(ResetToDefaults);

        Refresh();
    }

    /// <summary>Pulls the current gestures and registration results back into the rows.</summary>
    private void Refresh()
    {
        foreach (var binding in Bindings)
        {
            binding.Gesture = _config.Get(binding.Action);
            binding.IsBlocked = !string.IsNullOrWhiteSpace(binding.Gesture)
                                && _manager.Status.TryGetValue(binding.Action, out bool ok) && !ok;
        }
    }

    private void BeginCapture(HotkeyBindingViewModel binding)
    {
        CancelCapture();
        _capturing = binding;
        binding.IsCapturing = true;
        // Otherwise pressing an already-bound combination would fire its action instead of landing here.
        _manager.Suspend();
        OnPropertyChanged(nameof(IsCapturing));
    }

    public void CancelCapture()
    {
        if (_capturing is null) return;
        _capturing.IsCapturing = false;
        _capturing = null;
        _manager.Resume();
        OnPropertyChanged(nameof(IsCapturing));
    }

    /// <summary>Called by the window for each key press while a row is capturing.</summary>
    public void Capture(ModifierKeys modifiers, Key key)
    {
        if (_capturing is not { } binding) return;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            Assign(binding, string.Empty);
            return;
        }

        // A bare key would swallow that key system-wide, so a modifier is required.
        if (modifiers == ModifierKeys.None) return;

        string gesture = HotkeyManager.Format(modifiers, key);
        if (string.IsNullOrEmpty(gesture)) return;

        foreach (var other in Bindings)
        {
            if (other != binding && string.Equals(other.Gesture, gesture, StringComparison.OrdinalIgnoreCase))
                other.Gesture = string.Empty; // one combination cannot drive two actions
        }

        Assign(binding, gesture);
    }

    private void Assign(HotkeyBindingViewModel binding, string gesture)
    {
        CancelCapture();
        binding.Gesture = gesture;

        foreach (var b in Bindings) _config.Set(b.Action, b.Gesture);
        _config.Save();
        _manager.Apply(_config);
        Refresh();
        Log.Info($"Hotkey {binding.Action} set to \"{(gesture.Length == 0 ? "(none)" : gesture)}\"");
    }

    private void ResetToDefaults()
    {
        var defaults = new HotkeyConfig();
        foreach (var binding in Bindings) _config.Set(binding.Action, defaults.Get(binding.Action));
        CancelCapture();
        _config.Save();
        _manager.Apply(_config);
        Refresh();
        Log.Info("Hotkeys reset to defaults");
    }
}
