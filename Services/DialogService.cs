using System.Threading.Tasks;
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
    }
}