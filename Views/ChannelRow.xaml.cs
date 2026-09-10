using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SonarTray.ViewModels;

namespace SonarTray.Views;

public partial class ChannelRow : UserControl
{
    public ChannelRow()
    {
        InitializeComponent();

        VolumeSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            if (ViewModel is { } vm) vm.IsDragging = true;
        }));
        VolumeSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            if (ViewModel is not { } vm) return;
            vm.IsDragging = false;
            vm.FlushVolume();
        }));
        VolumeSlider.PreviewMouseWheel += OnSliderWheel;
    }

    private ChannelViewModel? ViewModel => DataContext as ChannelViewModel;

    private void OnSliderWheel(object sender, MouseWheelEventArgs e)
    {
        const double step = 0.02;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (e.Delta > 0 ? step : -step), 0.0, 1.0);
        e.Handled = true;
    }
}
