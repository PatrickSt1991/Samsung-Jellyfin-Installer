namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenCertificateService
    {
        Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string userEmail, string outputPath, Action<string> updateStatus, string jarPath);
    }
}
