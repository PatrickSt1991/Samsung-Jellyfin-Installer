using Jellyfin2Samsung.Extensions;
using Jellyfin2Samsung.Models;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Interfaces
{
    public interface ITizenInstallerService
    {
        Task<string> GetTvNameAsync(string tvIpAddress);
        Task<string> EnsureTizenSdbAvailable();
        Task<string> DownloadPackageAsync(string downloadUrl);
        Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, ProgressCallback? progress = null);
    }
}
