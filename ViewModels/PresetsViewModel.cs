using System.Collections.ObjectModel;
using System.Windows.Input;
using SonarTray.Models;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>The presets popup: apply one, save the current mixer as one, delete one.</summary>
public sealed class PresetsViewModel : ObservableObject
{
    private readonly PresetStore _store;
    private readonly Func<IReadOnlyList<ChannelViewModel>> _channels;
    private string _newName = "";

    public PresetsViewModel(PresetStore store, Func<IReadOnlyList<ChannelViewModel>> channels)
    {
        _store = store;
        _channels = channels;

        Presets = new ObservableCollection<MixerPreset>(store.Presets);

        ApplyCommand = new RelayCommand<MixerPreset>(Apply);
        DeleteCommand = new RelayCommand<MixerPreset>(Delete);
        SaveCommand = new RelayCommand(SaveCurrent);
    }

    public ObservableCollection<MixerPreset> Presets { get; }

    public ICommand ApplyCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>What the user is typing into the "save as" box.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            if (!SetProperty(ref _newName, value)) return;
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(IsFull));
        }
    }

    /// <summary>
    /// False when the name is unusable, and also when the list is full and the name is a new one -
    /// otherwise the button would be enabled and then silently do nothing.
    /// </summary>
    public bool CanSave => PresetStore.Normalise(_newName) is { } name && !WouldExceedLimit(name);

    /// <summary>True when saving the typed name is blocked by the limit, so the UI can say why.</summary>
    public bool IsFull => PresetStore.Normalise(_newName) is { } name && WouldExceedLimit(name);

    /// <summary>Overwriting an existing preset is always allowed, even at the limit.</summary>
    private bool WouldExceedLimit(string name)
        => _store.Find(name) is null && _store.Presets.Count >= PresetStore.MaxPresets;

    public bool HasPresets => Presets.Count > 0;

    /// <summary>
    /// Writes the snapshot back to Sonar through the normal channel setters, so the existing
    /// debounce and in-flight handling applies and a preset cannot outrun a drag.
    /// </summary>
    private void Apply(MixerPreset preset)
    {
        // RelayCommand<T> never invokes with null, so no guard is needed here.
        foreach (var channel in _channels())
        {
            if (preset.For(channel.Spec.VolumeId) is not { } saved) continue;

            channel.Volume = saved.Volume;
            channel.FlushVolume();
            channel.IsMuted = saved.Muted;

            // Only route when the preset actually recorded a device and the channel has a picker;
            // otherwise a preset saved on another machine would clear the routing.
            if (saved.HasDevice && channel.HasDevicePicker) channel.SelectedDeviceId = saved.DeviceId;
        }

        Log.Info($"Preset '{preset.Name}' applied");
    }

    private void Delete(MixerPreset preset)
    {
        if (!_store.Delete(preset.Name)) return;

        Presets.Remove(preset);
        Refresh();
    }

    private void SaveCurrent()
    {
        if (PresetStore.Normalise(_newName) is not { } name) return;

        var preset = MixerPreset.Capture(
            name,
            _channels().Select(c => (c.Spec.VolumeId, c.Volume, c.IsMuted, c.HasDevicePicker ? c.SelectedDeviceId : null)));

        if (!_store.Save(preset)) return;

        // Rebuild rather than insert: the store keeps the list sorted by name.
        Presets.Clear();
        foreach (var saved in _store.Presets) Presets.Add(saved);

        NewName = "";
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasPresets));
        OnPropertyChanged(nameof(IsFull));
        OnPropertyChanged(nameof(CanSave));
    }
}
