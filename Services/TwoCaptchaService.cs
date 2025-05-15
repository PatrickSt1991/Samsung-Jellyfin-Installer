using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TwoCaptchaService : ICaptchaSolver
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly int _timeoutSeconds;

        public TwoCaptchaService(string apiKey, int timeoutSeconds = 120)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _timeoutSeconds = timeoutSeconds;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<string> SolveReCaptchaV2Async(string siteKey, string pageUrl)
        {
            try
            {
                // Submit captcha
                var submitResponse = await _httpClient.GetStringAsync(
                    $"http://2captcha.com/in.php?key={_apiKey}&method=userrecaptcha&googlekey={siteKey}&pageurl={pageUrl}");

                if (!submitResponse.StartsWith("OK|"))
                    throw new Exception($"Captcha submission failed: {submitResponse}");

                var captchaId = submitResponse[3..];
                var startTime = DateTime.Now;

                // Poll for solution
                while (DateTime.Now - startTime < TimeSpan.FromSeconds(_timeoutSeconds))
                {
                    await Task.Delay(5000); // Wait 5 seconds between checks

                    var solutionResponse = await _httpClient.GetStringAsync(
                        $"http://2captcha.com/res.php?key={_apiKey}&action=get&id={captchaId}");

                    if (solutionResponse == "CAPCHA_NOT_READY")
                        continue;

                    if (solutionResponse.StartsWith("OK|"))
                        return solutionResponse[3..];

                    throw new Exception($"Captcha solving failed: {solutionResponse}");
                }

                throw new Exception("Captcha solving timed out");
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Captcha solving request timed out");
            }
        }
    }
}