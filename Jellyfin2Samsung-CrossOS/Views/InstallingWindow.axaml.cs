using Avalonia.Controls;
using Jellyfin2Samsung.ViewModels;

namespace Jellyfin2Samsung;

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