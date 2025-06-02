using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class IpInputDialog : Window
    {
        public string? EnteredIp { get; private set; }

        public IpInputDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = message;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredIp = InputBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
