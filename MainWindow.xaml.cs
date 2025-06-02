using Samsung_Jellyfin_Installer.ViewModels;
using System.Windows;

namespace Samsung_Jellyfin_Installer;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}