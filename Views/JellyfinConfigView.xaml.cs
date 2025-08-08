using Samsung_Jellyfin_Installer.ViewModels;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class JellyfinConfigView : Window
    {
        public JellyfinConfigView()
        {
            InitializeComponent();
            DataContext = new JellyfinConfigViewModel();
        }
    }
}
