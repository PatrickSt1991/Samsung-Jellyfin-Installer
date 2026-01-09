using Jellyfin2Samsung.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinWebPackagePatcher
    {
        private readonly JellyfinHtmlPatcher _html;
        private readonly JellyfinBootloaderInjector _boot;

        public JellyfinWebPackagePatcher(HttpClient http)
        {
            var api = new JellyfinApiClient(http);
            var plugins = new PluginManager(http, api);

            _html = new JellyfinHtmlPatcher(http, api, plugins);
            _boot = new JellyfinBootloaderInjector();
        }

        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath, string[] userIds)
        {
            using var ws = PackageWorkspace.Extract(packagePath);

            if (AppSettings.Default.ConfigUpdateMode.Contains("Server") ||
                AppSettings.Default.ConfigUpdateMode.Contains("All"))
            {
                await _html.EnsureTizenCorsAsync(ws);

                if (AppSettings.Default.UseServerScripts)
                    await _html.PatchServerIndexAsync(ws, AppSettings.Default.JellyfinFullUrl);

                if (AppSettings.Default.PatchYoutubePlugin)
                    await _html.PatchYoutubePlayerAsync(ws);

                await _html.UpdateMultiServerConfigAsync(ws);
            }

            if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                AppSettings.Default.ConfigUpdateMode.Contains("All"))
            {
                Trace.WriteLine("Injecting user settings into browser index.html...");
                await _html.InjectUserSettingsAsync(ws, userIds);
            }

            // Always inject auto-login credentials if available
            if (!string.IsNullOrEmpty(AppSettings.Default.JellyfinAccessToken) &&
                !string.IsNullOrEmpty(AppSettings.Default.JellyfinUserId))
            {
                Trace.WriteLine("Injecting auto-login credentials...");
                await _html.InjectAutoLoginAsync(ws);
            }

            if (AppSettings.Default.EnableDevLogs)
            {
                Trace.WriteLine("Injecting dev logs...");
                await _boot.InjectDevLogsAsync(ws);
            }

            // Inject custom CSS if configured
            if (!string.IsNullOrWhiteSpace(AppSettings.Default.CustomCss))
            {
                Trace.WriteLine("Injecting custom CSS...");
                await _html.InjectCustomCssAsync(ws);
            }

            ws.Repack();
            return InstallResult.SuccessResult();
        }
    }
}
