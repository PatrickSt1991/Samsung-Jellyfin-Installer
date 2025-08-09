using Samsung_Jellyfin_Installer.ViewModels;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class JellyfinLoginWindow : Window
    {
        public JellyfinLoginWindow()
        {
            InitializeComponent();
            DataContext = new JellyfinConfigViewModel();
        }
    }
}
