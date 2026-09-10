using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using SonarTray.Models;
using SonarTray.Services;

namespace SonarTray.ViewModels;

/// <summary>One mixer row. Must only be touched on the UI thread.</summary>
public sealed class ChannelViewModel : ObservableObject
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(75);

    private readonly SonarConnection _connection;
    private readonly DispatcherTimer _debounce;

    private bool _isSyncing;          // true while server state is being applied -> setters must not write back
    private double _volume = 1.0;
    private bool _isMuted;
    private string? _selectedDeviceId;
    private bool _isDragging;
    private bool _isDeviceMissing;

    private double? _pendingVolume;   // coalesced value waiting for the in-flight request
    private bool _volumeInFlight;
    private bool _muteInFlight;
    private bool _deviceInFlight;

    public ChannelSpec Spec { get; }
    public string Name => Spec.Label;
    public string Glyph => Spec.Glyph;
    public Brush Accent { get; }
    public bool HasDevicePicker => Spec.RedirectionId is not null;
    public ObservableCollection<AudioDeviceItem> Devices { get; } = new();

    public ChannelViewModel(ChannelSpec spec, SonarConnection connection)
    {
        Spec = spec;
        _connection = connection;

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(spec.AccentHex));
        brush.Freeze();
        Accent = brush;

        _debounce = new DispatcherTimer(DispatcherPriority.Input) { Interval = DebounceInterval };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = SendVolumeAsync(_volume);
        };
    }

    // ---- bindable state ------------------------------------------------

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_volume - value) < 0.0005) return;
            _volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Percent));
            if (_isSyncing) return;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    public int Percent => (int)Math.Round(_volume * 100);

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnPropertyChanged();
            if (!_isSyncing) _ = SendMuteAsync(value);
        }
    }

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            // ComboBox pushes null transiently while its items change; never treat that as a user choice.
            if (value is null || string.Equals(value, _selectedDeviceId, StringComparison.OrdinalIgnoreCase)) return;
            _selectedDeviceId = value;
            OnPropertyChanged();
            if (!_isSyncing) _ = SendDeviceAsync(value);
        }
    }

    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }

    public bool IsDeviceMissing
    {
        get => _isDeviceMissing;
        private set => SetProperty(ref _isDeviceMissing, value);
    }

    private bool IsVolumeBusy => _isDragging || _debounce.IsEnabled || _volumeInFlight;

    /// <summary>Called on Thumb.DragCompleted so the final value lands immediately.</summary>
    public void FlushVolume()
    {
        if (!_debounce.IsEnabled) return;
        _debounce.Stop();
        _ = SendVolumeAsync(_volume);
    }

    // ---- server -> UI --------------------------------------------------

    public void ApplyFromServer(VolumeStateDto? state, string? deviceId, bool redirectionRunning, IReadOnlyList<AudioDeviceItem> devices)
    {
        _isSyncing = true;
        try
        {
            ApplyVolumeState(state);

            if (HasDevicePicker)
            {
                var wanted = new List<AudioDeviceItem>(devices);
                bool missing = false;
                if (deviceId is not null && !wanted.Any(d => SameId(d.Id, deviceId)))
                {
                    wanted.Add(new AudioDeviceItem(deviceId, "(bağlı değil)", IsMissing: true));
                    missing = true;
                }

                SyncDevices(wanted);

                if (!_deviceInFlight)
                {
                    _selectedDeviceId = deviceId;
                    OnPropertyChanged(nameof(SelectedDeviceId)); // always re-raise: the ComboBox may have lost its selection while items changed
                    IsDeviceMissing = missing || !redirectionRunning;
                }
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// Volume and mute only. Used by the idle poll, which fetches just the volume document
    /// while the panel is closed so the tray icon does not go stale.
    /// </summary>
    public void ApplyVolumeFromServer(VolumeStateDto? state)
    {
        _isSyncing = true;
        try { ApplyVolumeState(state); }
        finally { _isSyncing = false; }
    }

    private void ApplyVolumeState(VolumeStateDto? state)
    {
        if (state is null) return;
        if (!IsVolumeBusy) Volume = state.Volume;
        if (!_muteInFlight) IsMuted = state.Muted;
    }

    /// <summary>In-place diff so the ComboBox never sees Clear() (which would reset its selection).</summary>
    private void SyncDevices(List<AudioDeviceItem> wanted)
    {
        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(w => SameId(w.Id, Devices[i].Id))) Devices.RemoveAt(i);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            var item = wanted[i];
            int idx = -1;
            for (int j = 0; j < Devices.Count; j++)
            {
                if (SameId(Devices[j].Id, item.Id)) { idx = j; break; }
            }

            if (idx < 0)
            {
                Devices.Insert(i, item);
                continue;
            }
            if (idx != i) Devices.Move(idx, i);
            if (!Devices[i].Equals(item)) Devices[i] = item;
        }
    }

    // ---- UI -> server --------------------------------------------------

    private async Task SendVolumeAsync(double value)
    {
        if (_volumeInFlight)
        {
            _pendingVolume = value; // coalesce: only the latest value matters
            return;
        }

        _volumeInFlight = true;
        try
        {
            double? next = value;
            while (next is { } v)
            {
                _pendingVolume = null;
                var client = _connection.Client;
                if (client is null) return;
                try
                {
                    await client.SetVolumeAsync(Spec.VolumeId, v, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _connection.ReportFailure(ex);
                    return;
                }
                next = _pendingVolume;
            }
        }
        finally
        {
            _volumeInFlight = false;
            _pendingVolume = null;
        }
    }

    private async Task SendMuteAsync(bool muted)
    {
        var client = _connection.Client;
        if (client is null) return;
        _muteInFlight = true;
        try
        {
            await client.SetMuteAsync(Spec.VolumeId, muted, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
        finally
        {
            _muteInFlight = false;
        }
    }

    private async Task SendDeviceAsync(string deviceId)
    {
        var client = _connection.Client;
        if (client is null || Spec.RedirectionId is null) return;
        _deviceInFlight = true;
        try
        {
            await client.SetRedirectionDeviceAsync(Spec.RedirectionId, deviceId, CancellationToken.None);
            IsDeviceMissing = false;
        }
        catch (Exception ex)
        {
            _connection.ReportFailure(ex);
        }
        finally
        {
            _deviceInFlight = false;
        }
    }

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
