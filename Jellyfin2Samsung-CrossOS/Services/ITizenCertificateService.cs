using Jellyfin2SamsungCrossOS.Extensions;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public interface ITizenCertificateService
    {
        Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string userEmail, string outputPath, string jarPath, ProgressCallback? progress = null);
    }
}
