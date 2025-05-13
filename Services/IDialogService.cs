namespace Samsung_Jellyfin_Installer.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(string message);
        Task ShowErrorAsync(string message);
        Task<bool> ShowConfirmationAsync(string message);
    }
}