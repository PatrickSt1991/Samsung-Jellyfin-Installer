using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinHtmlPatcher
    {
        private readonly JellyfinPluginPatcher _plugins;

        public JellyfinHtmlPatcher(
            HttpClient http,
            JellyfinApiClient api,
            PluginManager plugins)
        {
            _plugins = new JellyfinPluginPatcher(http, api, plugins);
        }

        public async Task PatchServerIndexAsync(PackageWorkspace ws, string serverUrl)
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
        public async Task UpdateMultiServerConfigAsync(PackageWorkspace ws)
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

            var serverUrl = AppSettings.Default.JellyfinIP.TrimEnd('/');

            // Avoid duplicates
            if (!servers.Any(s => s?.GetValue<string>() == serverUrl))
            {
                servers.Add(serverUrl);
            }

            await File.WriteAllTextAsync(path, config.ToJsonString());

        }
        public async Task InjectUserSettingsAsync(PackageWorkspace ws, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;

            string index = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(index)) return;

            var html = await File.ReadAllTextAsync(index);

            var sb = new StringBuilder();
            sb.AppendLine("<script>");
            sb.AppendLine("window.JellyfinUserSettings={SelectedUsers:[");
            sb.AppendLine(string.Join(",", userIds));
            sb.AppendLine("]};</script>");

            html = html.Replace("</body>", sb + "\n</body>");
            await File.WriteAllTextAsync(index, html);
        }
        public async Task EnsureTizenCorsAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "config.xml");
            if (!File.Exists(path)) return;

            XDocument doc = XDocument.Load(path);
            var widget = doc.Root;
            XNamespace widgetNs = widget.Name.Namespace;
            XNamespace tizenNs = widget.GetNamespaceOfPrefix("tizen") ?? "http://tizen.org/ns/widgets";

            // Standard Access
            if (!widget.Elements(widgetNs + "access").Any(e => (string?)e.Attribute("origin") == "*"))
            {
                widget.Add(new XElement(widgetNs + "access", new XAttribute("origin", "*"), new XAttribute("subdomains", "true")));
            }

            // Essential for Tizen 5.5 to allow cross-origin messaging between file:// and https://
            if (!widget.Elements(tizenNs + "allow-navigation").Any())
            {
                widget.Add(new XElement(tizenNs + "allow-navigation", "*"));
            }

            doc.Save(path);
        }
        public async Task PatchYoutubePlayerAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            if (!Directory.Exists(www)) return;

            var files = Directory.GetFiles(www, "youtubePlayer-plugin.*.js");

            foreach (var file in files)
            {
                var js = await File.ReadAllTextAsync(file);

                // Switch to 1 so the iframe and the parent can actually 'talk' 
                // using our spoofed origin.
                js = js.Replace("enablejsapi:0", "enablejsapi:1");

                const string marker = "playerVars:{";
                var idx = js.IndexOf(marker, StringComparison.Ordinal);
                if (idx != -1)
                {
                    var start = idx + marker.Length;
                    if (!js.Contains("widget_referrer"))
                    {
                        // We provide the origin and the widget_referrer. 
                        // This is the combination that usually satisfies the Error 153.
                        string injection =
                            "origin:\"https://www.youtube.com\"," +
                            "widget_referrer:\"https://www.youtube.com\",";

                        js = js.Insert(start, injection);
                    }
                }
                await File.WriteAllTextAsync(file, js);
            }
        }
        public async Task PatchIndexHtmlAsync(PackageWorkspace ws)
        {
            var indexPath = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            var html = await File.ReadAllTextAsync(indexPath);
            const string headMarker = "<head>";
            var headIdx = html.IndexOf(headMarker, StringComparison.Ordinal);

            if (headIdx != -1 && !html.Contains("YT-SAFE-PATCH"))
            {
                string safeScript = "\n<script id='YT-SAFE-PATCH'>\n" +
                    "  try {\n" +
                    "    console.log('[YT-PATCH] Running Safe Handshake Fix');\n" +
                    "    Object.defineProperty(window, 'origin', { get: () => 'https://www.youtube.com' });\n" +
                    "    Object.defineProperty(document, 'referrer', { get: () => 'https://www.youtube.com/' });\n" +
                    "    // Observer to fix iframe src before it loads\n" +
                    "    new MutationObserver(mutations => {\n" +
                    "      mutations.forEach(m => m.addedNodes.forEach(n => {\n" +
                    "        if(n.tagName === 'IFRAME' && n.src.includes('youtube.com')) {\n" +
                    "           if(!n.src.includes('origin=')) n.src += '&origin=https://www.youtube.com';\n" +
                    "        }\n" +
                    "      }));\n" +
                    "    }).observe(document.documentElement, { childList: true, subtree: true });\n" +
                    "  } catch(e) { console.error(e); }\n" +
                    "</script>";

                html = html.Insert(headIdx + headMarker.Length, safeScript);
                await File.WriteAllTextAsync(indexPath, html);
            }
        }
    }
}
