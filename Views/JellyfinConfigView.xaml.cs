using Samsung_Jellyfin_Installer.ViewModels;
using System.Net.Http;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class JellyfinConfigView : Window
    {
        public JellyfinConfigView()
        {
            InitializeComponent();
            var httpClient = new HttpClient();
            DataContext = new JellyfinConfigViewModel(httpClient);
        }
    }
}
