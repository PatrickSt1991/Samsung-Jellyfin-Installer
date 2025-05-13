using Samsung_Jellyfin_Installer.Models;
using System.Threading.Tasks;

namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenInstallerService
    {
        string TizenCliPath { get; }
        Task<bool> EnsureTizenCliAvailable();
        Task<string> DownloadPackageAsync(string downloadUrl);
        Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, Action<string> updateStatus);
    }
}