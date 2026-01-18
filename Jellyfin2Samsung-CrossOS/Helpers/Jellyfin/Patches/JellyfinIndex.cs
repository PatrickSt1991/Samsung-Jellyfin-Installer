using Jellyfin2Samsung.Helpers.API;
using Jellyfin2Samsung.Helpers.Core;
using Jellyfin2Samsung.Helpers.Jellyfin.Plugins;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Jellyfin.Patches
{
    public class JellyfinIndex(
        HttpClient http,
        JellyfinApiClient api,
        PluginManager plugins)
    {
        private readonly JellyfinApiClient _apiClient = api;
        private readonly JellyfinPluginPatcher _plugins = new(http, api, plugins);

        public async Task PatchIndexAsync(PackageWorkspace ws, string serverUrl)
        {
            string index = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(index)) return;

            var html = await File.ReadAllTextAsync(index);

            html = HtmlUtils.EnsureBaseHref(html);
            html = HtmlUtils.RewriteLocalPaths(html);

            var css = new StringBuilder();
            var headJs = new StringBuilder();
            var bodyJs = new StringBuilder();

            await _plugins.PatchPluginsAsync(ws, serverUrl, css, headJs, bodyJs);

            html = html.Replace("</head>", css + "\n" + headJs + "\n</head>");
            html = html.Replace("</body>", bodyJs + "\n</body>");

            html = HtmlUtils.CleanAndApplyCsp(html);
            html = HtmlUtils.EnsurePublicJsIsLast(html);

            await File.WriteAllTextAsync(index, html);
        }
        public async Task UpdateServerAddressAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "www", "config.json");

            JsonObject config;

            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                config = JsonNode.Parse(json)?.AsObject()
                         ?? new JsonObject();
            }
            else
            {
                config = new JsonObject();
            }

            // Ensure multiserver is set
            config["multiserver"] = false;

            // Ensure servers array exists
            if (config["servers"] is not JsonArray servers)
            {
                servers = new JsonArray();
                config["servers"] = servers;
            }

            var serverUrl = UrlHelper.NormalizeServerUrl(AppSettings.Default.JellyfinFullUrl);

            // Avoid duplicates
            if (!servers.Any(s => s?.GetValue<string>() == serverUrl))
                servers.Add(serverUrl);

            await File.WriteAllTextAsync(path, config.ToJsonString());

        }

        /// <summary>
        /// Injects auto-login credentials into the Jellyfin web app.
        /// This stores the access token and server info in localStorage format.
        /// Uses the real server ID from /System/Info/Public to prevent ServerMismatch errors.
        /// </summary>
        public async Task InjectAutoLoginAsync(PackageWorkspace ws)
        {
            var accessToken = AppSettings.Default.JellyfinAccessToken;
            var userId = AppSettings.Default.JellyfinUserId;
            var serverUrl = UrlHelper.NormalizeServerUrl(AppSettings.Default.JellyfinFullUrl);
            var serverId = AppSettings.Default.JellyfinServerId;
            var localAddress = AppSettings.Default.JellyfinServerLocalAddress;

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(serverUrl))
            {
                Trace.WriteLine("[InjectAutoLogin] Missing credentials, skipping auto-login injection");
                return;
            }

            // If server ID is not stored, try to fetch it now
            if (string.IsNullOrEmpty(serverId))
            {
                Trace.WriteLine("[InjectAutoLogin] Server ID not cached, fetching from server...");
                var serverInfo = await _apiClient.GetPublicSystemInfoAsync(serverUrl);
                if (serverInfo != null && !string.IsNullOrEmpty(serverInfo.Id))
                {
                    serverId = serverInfo.Id;
                    localAddress = serverInfo.LocalAddress ?? "";
                    AppSettings.Default.JellyfinServerId = serverId;
                    AppSettings.Default.JellyfinServerLocalAddress = localAddress;
                    AppSettings.Default.Save();
                    Trace.WriteLine($"[InjectAutoLogin] Fetched and stored server ID: {serverId}");
                }
                else
                {
                    Trace.WriteLine("[InjectAutoLogin] WARNING: Could not fetch server ID, auto-login may fail with ServerMismatch");
                    return;
                }
            }

            string indexPath = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(indexPath))
            {
                Trace.WriteLine("[InjectAutoLogin] index.html not found");
                return;
            }

            var html = await File.ReadAllTextAsync(indexPath);

            // Create the credentials object that Jellyfin web expects
            // Using the REAL server ID (GUID) from /System/Info/Public to prevent ServerMismatch
            var credentialsScript = new StringBuilder();
            credentialsScript.AppendLine("<script>");
            credentialsScript.AppendLine("(function() {");
            credentialsScript.AppendLine("  try {");
            credentialsScript.AppendLine($"    var serverUrl = '{HtmlUtils.EscapeJsString(serverUrl)}';");
            credentialsScript.AppendLine($"    var serverId = '{HtmlUtils.EscapeJsString(serverId)}';");
            credentialsScript.AppendLine($"    var localAddress = '{HtmlUtils.EscapeJsString(localAddress)}';");
            credentialsScript.AppendLine($"    var userId = '{HtmlUtils.EscapeJsString(userId)}';");
            credentialsScript.AppendLine($"    var accessToken = '{HtmlUtils.EscapeJsString(accessToken)}';");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    // Create credentials object matching Jellyfin's expected format");
            credentialsScript.AppendLine("    // Using real server ID (GUID) from /System/Info/Public");
            credentialsScript.AppendLine("    var credentials = {");
            credentialsScript.AppendLine("      Servers: [{");
            credentialsScript.AppendLine("        ManualAddress: serverUrl,");
            credentialsScript.AppendLine("        LocalAddress: localAddress || serverUrl,");
            credentialsScript.AppendLine("        Id: serverId,");
            credentialsScript.AppendLine("        UserId: userId,");
            credentialsScript.AppendLine("        AccessToken: accessToken,");
            credentialsScript.AppendLine("        DateLastAccessed: new Date().getTime()");
            credentialsScript.AppendLine("      }]");
            credentialsScript.AppendLine("    };");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    // Store in localStorage");
            credentialsScript.AppendLine("    localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    console.log('[Auto-Login] Credentials injected for server: ' + serverUrl + ' with ID: ' + serverId);");
            credentialsScript.AppendLine("  } catch(e) {");
            credentialsScript.AppendLine("    console.error('[Auto-Login] Failed to inject credentials:', e);");
            credentialsScript.AppendLine("  }");
            credentialsScript.AppendLine("})();");
            credentialsScript.AppendLine("</script>");

            // Inject before </head> to ensure it runs before Jellyfin's scripts
            html = html.Replace("</head>", credentialsScript + "\n</head>");

            await File.WriteAllTextAsync(indexPath, html);
            Trace.WriteLine($"[InjectAutoLogin] Auto-login credentials injected successfully with server ID: {serverId}");
        }
    }
}