using Samsung_Jellyfin_Installer.Views;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Services
{
    public class WebViewCaptchaSolver : ICaptchaSolver
    {
        public async Task<string> SolveReCaptchaEnterpriseAsync(string siteKey, string action = "login")
        {
            var html = GenerateEnterpriseCaptchaHtml(siteKey, action);

            var dispatcherTask = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new HiddenWebViewWindow();
                return window.SolveCaptchaAsync(html); // returns Task<string>
            });

            return await await dispatcherTask.Task;
        }

        private string GenerateEnterpriseCaptchaHtml(string siteKey, string action)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <script src='https://www.recaptcha.net/recaptcha/enterprise.js?render={siteKey}'></script>
</head>
<body>
<script>
  grecaptcha.enterprise.ready(function () {{
    grecaptcha.enterprise.execute('{siteKey}', {{action: '{action}'}})
      .then(function (token) {{
        window.chrome.webview.postMessage(token);
      }})
      .catch(function (error) {{
        window.chrome.webview.postMessage('error:' + error.toString());
      }});
  }});
</script>
</body>
</html>";
        }
    }
}
