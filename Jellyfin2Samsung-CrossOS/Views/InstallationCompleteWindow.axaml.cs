using Avalonia.Controls;
using Jellyfin2SamsungCrossOS.ViewModels;

namespace Jellyfin2SamsungCrossOS;

public partial class InstallationCompleteWindow : Window
{
    public InstallationCompleteWindow(InstallationCompleteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}