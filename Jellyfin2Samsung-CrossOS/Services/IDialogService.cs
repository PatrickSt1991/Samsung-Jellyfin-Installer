using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(string message);
        Task ShowErrorAsync(string message);
        Task<bool> ShowConfirmationAsync(string title, string message, string yes, string no);
        Task<string?> PromptForIpAsync();

    }
}
