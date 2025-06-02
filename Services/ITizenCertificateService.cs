namespace Samsung_Jellyfin_Installer.Services
{
    public interface ITizenCertificateService
    {
        Task ExtractRootCertificateAsync(string jarPath);
        Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus, string jarPath);
    }
}
