using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Web.WebView2.Core;

namespace Samsung_Jellyfin_Installer.Converters
{
    public static class WebView2Helper
    {
        // Official Evergreen Runtime installer direct link
        private const string EvergreenRuntimeInstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        public static bool IsWebView2RuntimeAvailable()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                Debug.WriteLine($"WebView2 Runtime version detected: {version}");
                return !string.IsNullOrEmpty(version);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking WebView2 Runtime availability: {ex.Message}");
                return false;
            }
        }

        public static async Task EnsureWebView2RuntimeAsync()
        {
            if (IsWebView2RuntimeAvailable())
                return;

            Debug.WriteLine("WebView2 Runtime not found. Downloading and installing...");

            string tempInstallerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Setup.exe");

            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(EvergreenRuntimeInstallerUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            Debug.WriteLine("Running WebView2 Runtime installer silently...");

            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = tempInstallerPath,
                Arguments = "/install /quiet /norestart",
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                process.WaitForExit();
                Debug.WriteLine($"Installer exited with code {process.ExitCode}");

                // Optionally check for success:
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"WebView2 Runtime installer failed with exit code {process.ExitCode}");
                }
            }

            // Re-check if runtime is now available
            if (!IsWebView2RuntimeAvailable())
            {
                throw new InvalidOperationException("WebView2 Runtime installation failed. Please install it manually and restart the application.");
            }

            Debug.WriteLine("WebView2 Runtime successfully installed.");
        }
    }

}
