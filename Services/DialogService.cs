using Samsung_Jellyfin_Installer.Views;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Services
{
    public class DialogService : IDialogService
    {
        public async Task ShowMessageAsync(string message)
        {
            MessageBox.Show(message);
            await Task.CompletedTask;
        }

        public async Task ShowErrorAsync(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            await Task.CompletedTask;
        }

        public async Task<bool> ShowConfirmationAsync(string message)
        {
            var result = MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo);
            return await Task.FromResult(result == MessageBoxResult.Yes);
        }
        public async Task<string?> PromptForIpAsync(string title, string message)
        {
            var dialog = new IpInputDialog(title, message)
            {
                Owner = Application.Current.MainWindow
            };

            bool? result = dialog.ShowDialog();

            return await Task.FromResult(result == true ? dialog.EnteredIp : null);
        }
    }
}