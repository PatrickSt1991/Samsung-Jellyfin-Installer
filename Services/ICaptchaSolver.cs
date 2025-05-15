namespace Samsung_Jellyfin_Installer.Services
{
    public interface ICaptchaSolver
    {
        Task<string> SolveReCaptchaV2Async(string siteKey, string pageUrl);
    }
}