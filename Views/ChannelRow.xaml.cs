using System.Windows;
using System.Windows.Controls;
using SonarTray.ViewModels;

namespace SonarTray.Views;

public partial class ChannelRow : UserControl
{
    public ChannelRow()
    {
        InitializeComponent();
        SliderInteraction.Attach(VolumeSlider);
    }

    /// <summary>
    /// The full profile list is megabytes, so it is fetched the first time someone actually asks
    /// to see it rather than on every panel refresh. The popup opens straight away and fills in
    /// when the request lands.
    /// </summary>
    private void OnProfileButtonClick(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = true;
        if (DataContext is ChannelViewModel channel) _ = channel.LoadProfilesAsync();
    }
}
