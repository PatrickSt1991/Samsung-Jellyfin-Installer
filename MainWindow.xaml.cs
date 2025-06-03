using Samsung_Jellyfin_Installer.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Samsung_Jellyfin_Installer;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}