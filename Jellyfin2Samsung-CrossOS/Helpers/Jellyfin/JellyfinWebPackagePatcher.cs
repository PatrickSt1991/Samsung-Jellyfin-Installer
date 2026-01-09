using Jellyfin2Samsung.Helpers.API;
using Jellyfin2Samsung.Helpers.Core;
using Jellyfin2Samsung.Helpers.Jellyfin.CSS;
using Jellyfin2Samsung.Helpers.Jellyfin.Diagnostic;
using Jellyfin2Samsung.Helpers.Jellyfin.Fixes;
using Jellyfin2Samsung.Helpers.Jellyfin.Patches;
using Jellyfin2Samsung.Helpers.Jellyfin.Plugins;
using Jellyfin2Samsung.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Jellyfin
{
    public class JellyfinWebPackagePatcher
    {
        private readonly JellyfinIndex _indexHtml;
        private readonly JellyfinDiagnostic _diagnostic;
        private readonly FixYouTube _youTube;
        private readonly CustomCss _customCss;

        public JellyfinWebPackagePatcher(HttpClient http)
        {
            var api = new JellyfinApiClient(http);
            var plugins = new PluginManager(http, api);

            _indexHtml = new JellyfinIndex(http, api, plugins);
            _diagnostic = new JellyfinDiagnostic();
            _youTube = new FixYouTube();
            _customCss = new CustomCss();
        }

        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath, string[] userIds)
        {
            using var ws = PackageWorkspace.Extract(packagePath);

            if (AppSettings.Default.ConfigUpdateMode.Contains("Server") ||
                AppSettings.Default.ConfigUpdateMode.Contains("All"))
            {
                
                if (AppSettings.Default.UseServerScripts)
                    await _indexHtml.PatchIndexAsync(ws, AppSettings.Default.JellyfinFullUrl);

                if (AppSettings.Default.PatchYoutubePlugin)
                {
                    await _youTube.CorsAsync(ws);
                    await _youTube.FixAsync(ws);
                }

                await _indexHtml.UpdateServerAddressAsync(ws);
            }

            if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                AppSettings.Default.ConfigUpdateMode.Contains("All"))
            {
                Trace.WriteLine("Injecting user settings into browser index.html...");
                await _indexHtml.InjectUserSettingsAsync(ws, userIds);
            }

            // Always inject auto-login credentials if available
            if (!string.IsNullOrEmpty(AppSettings.Default.JellyfinAccessToken) &&
                !string.IsNullOrEmpty(AppSettings.Default.JellyfinUserId))
            {
                Trace.WriteLine("Injecting auto-login credentials...");
                await _indexHtml.InjectAutoLoginAsync(ws);
            }

            if (AppSettings.Default.EnableDevLogs)
            {
                Trace.WriteLine("Injecting dev logs...");
                await _diagnostic.InjectDevLogsAsync(ws);
            }

            // Inject custom CSS if configured
            if (!string.IsNullOrWhiteSpace(AppSettings.Default.CustomCss))
            {
                Trace.WriteLine("Injecting custom CSS...");
                await _customCss.InjectAsync(ws);
            }

            ws.Repack();
            return InstallResult.SuccessResult();
        }
    }
}
