using Avalonia.Controls;
using Jellyfin2SamsungCrossOS.ViewModels;

namespace Jellyfin2SamsungCrossOS;

public partial class InstallingWindow : Window
{
    public InstallingWindowViewModel ViewModel { get; }

    public InstallingWindow()
    {
        InitializeComponent();

        ViewModel = new InstallingWindowViewModel();
        DataContext = ViewModel;
    }
}