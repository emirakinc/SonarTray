using System.Windows.Controls;

namespace SonarTray.Views;

public partial class MasterRow : UserControl
{
    public MasterRow()
    {
        InitializeComponent();
        SliderInteraction.Attach(VolumeSlider);
    }
}
