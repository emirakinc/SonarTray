using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SonarTray.ViewModels;

namespace SonarTray.Views;

/// <summary>
/// Drag tracking and wheel scrubbing for a volume slider. Shared by <see cref="ChannelRow"/> and
/// <see cref="MasterRow"/>, which differ only in layout.
/// </summary>
internal static class SliderInteraction
{
    private const double WheelStep = 0.02;

    /// <summary>The view model is read from the slider's DataContext at event time, not captured.</summary>
    public static void Attach(Slider slider)
    {
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((s, _) =>
        {
            if (ViewModelOf(s) is { } vm) vm.IsDragging = true;
        }));

        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((s, _) =>
        {
            if (ViewModelOf(s) is not { } vm) return;
            vm.IsDragging = false;
            vm.FlushVolume();
        }));

        slider.PreviewMouseWheel += (s, e) =>
        {
            if (s is not Slider sl) return;
            sl.Value = Math.Clamp(sl.Value + (e.Delta > 0 ? WheelStep : -WheelStep), 0.0, 1.0);
            e.Handled = true;
        };
    }

    private static ChannelViewModel? ViewModelOf(object sender)
        => (sender as FrameworkElement)?.DataContext as ChannelViewModel;
}
