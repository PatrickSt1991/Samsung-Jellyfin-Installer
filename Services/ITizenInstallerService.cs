using Samsung_Jellyfin_Installer.Models;

namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenInstallerService
    {
        string TizenCliPath { get; }
        Task<(string, string)> EnsureTizenCliAvailable();
        Task<string> DownloadPackageAsync(string downloadUrl);
        Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, Action<string> updateStatus);
        Task<string?> GetTvNameAsync(string tvIpAddress);
        Task<bool> ConnectToTvAsync(string tvIpAddress);
    }
}