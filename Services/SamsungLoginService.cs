using Microsoft.Web.WebView2.Core;
using Samsung_Jellyfin_Installer.Views;
using System.Diagnostics;
using System.Windows;

public class SamsungLoginService
{
    public static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            // Try to get the WebView2 version
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch
        {
            // Other unexpected errors
            return false;
        }
    }
    public async Task<(string State, string Code)> PerformSamsungLoginAsync()
    {
        // Check for WebView2 runtime first
        if (!IsWebView2RuntimeAvailable())
        {
            var wv2result = MessageBox.Show(
                "Microsoft Edge WebView2 Runtime is required. Install now?",
                "Runtime Missing",
                MessageBoxButton.YesNo);

            if (wv2result == MessageBoxResult.Yes)
            {
                // Open WebView2 download page
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                    UseShellExecute = true
                });
            }
            throw new Exception("WebView2 Runtime not installed");
        }

        // Proceed with login if runtime is available
        const string callbackUrl = "http://localhost:4794/signin/callback";
        const string stateValue = "accountcheckdogeneratedstatetext";

        var loginUrl = $"https://account.samsung.com/..."; // Your URL

        (string State, string Code) result = (null, null);
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var loginWindow = new SamsungLoginWindow(callbackUrl, stateValue);
            loginWindow.StartLogin(loginUrl);

            if (loginWindow.ShowDialog() == true)
            {
                result = (loginWindow.State, loginWindow.AuthorizationCode);
            }
        });

        return result;
    }
}