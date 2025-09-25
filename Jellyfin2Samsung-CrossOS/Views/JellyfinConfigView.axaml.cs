using Avalonia.Controls;
using Jellyfin2SamsungCrossOS.ViewModels;

namespace Jellyfin2SamsungCrossOS;

public partial class JellyfinConfigView : Window
{
    public JellyfinConfigView(JellyfinConfigViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}