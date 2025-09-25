using Jellyfin2SamsungCrossOS.Extensions;
using Jellyfin2SamsungCrossOS.Models;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public interface ITizenInstallerService
    {
        string TizenCliPath { get; }
        Task<(string, string)> EnsureTizenCliAvailable();
        Task<string> DownloadPackageAsync(string downloadUrl);
        Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, ProgressCallback? progress = null);
        Task<string?> GetTvNameAsync(string tvIpAddress);
        Task<bool> ConnectToTvAsync(string tvIpAddress);
    }
}
