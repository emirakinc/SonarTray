using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SonarTray.Converters;

/// <summary>
/// The mirror of <see cref="BooleanToVisibilityConverter"/>: visible when false.
///
/// Used for the "nothing here yet" placeholders, which are shown precisely when the thing they
/// describe is absent.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed or Visibility.Hidden;
}
