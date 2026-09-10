using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SonarTray.ViewModels;

namespace SonarTray.Views;

/// <summary>
/// Volume slider interaction, shared by <see cref="ChannelRow"/> and <see cref="MasterRow"/>.
///
/// Track pressing is handled here rather than through Slider.IsMoveToPointEnabled, because that
/// only jumps the value on click and never starts a drag - you had to grab the thumb to scrub.
/// Owning the press/move/release trio gives press-and-drag anywhere on the track, and lets the
/// same IsDragging / FlushVolume bookkeeping the thumb path uses apply to it too.
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

        slider.PreviewMouseLeftButtonDown += OnPress;
        slider.PreviewMouseMove += OnMove;
        slider.PreviewMouseLeftButtonUp += OnRelease;

        slider.PreviewMouseWheel += (s, e) =>
        {
            if (s is not Slider sl) return;
            sl.Value = Math.Clamp(sl.Value + (e.Delta > 0 ? WheelStep : -WheelStep), 0.0, 1.0);
            e.Handled = true;
        };
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || TrackOf(slider) is not { } track) return;

        // A press on the thumb is the Thumb's own drag; leave it alone.
        if (track.Thumb is { IsMouseOver: true }) return;

        if (ViewModelOf(slider) is { } vm) vm.IsDragging = true;
        slider.CaptureMouse();
        SetFromPoint(slider, track, e);
        e.Handled = true; // otherwise the track RepeatButtons would also page the value
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured) return;

        // The button-up can be missed (another window steals focus mid-drag); this is the backstop.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(slider);
            return;
        }

        if (TrackOf(slider) is { } track) SetFromPoint(slider, track, e);
        e.Handled = true;
    }

    private static void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured) return;
        EndDrag(slider);
        e.Handled = true;
    }

    private static void EndDrag(Slider slider)
    {
        slider.ReleaseMouseCapture();
        if (ViewModelOf(slider) is not { } vm) return;
        vm.IsDragging = false;
        vm.FlushVolume();
    }

    /// <summary>Track maps the point for us, so the thumb width is accounted for.</summary>
    private static void SetFromPoint(Slider slider, Track track, MouseEventArgs e)
    {
        double value = track.ValueFromPoint(e.GetPosition(track));
        if (double.IsFinite(value)) slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static Track? TrackOf(Slider slider)
        => slider.Template?.FindName("PART_Track", slider) as Track;

    private static ChannelViewModel? ViewModelOf(object sender)
        => (sender as FrameworkElement)?.DataContext as ChannelViewModel;
}
