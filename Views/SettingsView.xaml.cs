using Samsung_Jellyfin_Installer.ViewModels;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class SettingsView : Window
    {
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
