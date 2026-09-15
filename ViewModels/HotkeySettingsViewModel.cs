using System.Windows.Input;
using SonarTray.Hotkeys;
using SonarTray.Resources;
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
        ? Strings.Hotkeys_PressKey
        : string.IsNullOrWhiteSpace(Gesture) ? Strings.Hotkeys_None : Gesture;
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

        // Ordered for reading, not by enum order: panel first, then master, then each channel.
        Bindings = new List<HotkeyBindingViewModel>
        {
            new(HotkeyAction.TogglePanel, Strings.Action_TogglePanel),
            new(HotkeyAction.MasterDown, Strings.Action_MasterDown),
            new(HotkeyAction.MasterUp, Strings.Action_MasterUp),
            new(HotkeyAction.MasterMute, Strings.Action_MasterMute),
            new(HotkeyAction.MicMute, Strings.Action_MicMute),
            new(HotkeyAction.GameDown, Strings.Action_GameDown),
            new(HotkeyAction.GameUp, Strings.Action_GameUp),
            new(HotkeyAction.GameMute, Strings.Action_GameMute),
            new(HotkeyAction.ChatDown, Strings.Action_ChatDown),
            new(HotkeyAction.ChatUp, Strings.Action_ChatUp),
            new(HotkeyAction.ChatMute, Strings.Action_ChatMute),
            new(HotkeyAction.MediaDown, Strings.Action_MediaDown),
            new(HotkeyAction.MediaUp, Strings.Action_MediaUp),
            new(HotkeyAction.MediaMute, Strings.Action_MediaMute),
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
