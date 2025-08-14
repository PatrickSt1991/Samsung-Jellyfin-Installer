using System.Diagnostics;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class InstallationCompleteWindow : Window
    {
        public InstallationCompleteWindow()
        {
            InitializeComponent();
        }

        private void Validation_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/patrickst",
                UseShellExecute = true
            });
            
            this.Close();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
