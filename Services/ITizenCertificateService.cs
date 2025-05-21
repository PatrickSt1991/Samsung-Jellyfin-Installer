namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenCertificateService
    {
        Task<string> GenerateProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus);
    }
}
