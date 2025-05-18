using Samsung_Jellyfin_Installer.Models;

namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenCertificateService
    {
        Task GenerateDistributorProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus);
    }
}
