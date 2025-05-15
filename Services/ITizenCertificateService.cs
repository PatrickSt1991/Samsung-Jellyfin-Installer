using System;
using System.Threading.Tasks;

namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenCertificateService
    {
        Task GenerateCertificateAsync(string email, string password, string[] deviceIds, Action<string> updateStatus);
    }
}