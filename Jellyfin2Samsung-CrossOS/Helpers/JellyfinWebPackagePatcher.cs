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
                    await _html.PatchServerIndexAsync(ws, AppSettings.Default.JellyfinIP);

                await _html.UpdateMultiServerConfigAsync(ws);
            }

            if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                AppSettings.Default.ConfigUpdateMode.Contains("All"))
            {
                Trace.WriteLine("Injecting user settings into browser index.html...");
                await _html.InjectUserSettingsAsync(ws, userIds);
            }

            if (AppSettings.Default.EnableDevLogs)
            {
                Trace.WriteLine("Injecting dev logs...");   
                await _boot.InjectDevLogsAsync(ws);
            }

            ws.Repack();
            return InstallResult.SuccessResult();
        }
    }
}
