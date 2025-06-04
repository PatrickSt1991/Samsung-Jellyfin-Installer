using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class InstallingWindow : Window
    {
        public InstallingWindow()
        {
            InitializeComponent();
        }
        public void SetStatusText(string message)
        {
            StatusTextBlock.Text = message;
        }

    }
}
