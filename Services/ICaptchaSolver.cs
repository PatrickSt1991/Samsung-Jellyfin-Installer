namespace Samsung_Jellyfin_Installer.Services
{
    public interface ICaptchaSolver
    {
        Task<string> SolveReCaptchaEnterpriseAsync(string siteKey, string action = "login");
    }
}