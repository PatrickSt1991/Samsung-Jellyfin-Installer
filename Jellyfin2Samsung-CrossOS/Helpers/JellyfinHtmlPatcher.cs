using Jellyfin2Samsung.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
            var js = new StringBuilder();

            await _plugins.PatchPluginsAsync(ws, serverUrl, css, js);

            html = html.Replace("</head>", css + "\n</head>");
            html = html.Replace("</body>", js + "\n</body>");

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

                if (!File.Exists(path))
                    throw new FileNotFoundException("config.xml not found", path);

                XDocument doc;
                await using (var stream = File.OpenRead(path))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, default);
                }

                var widget = doc.Root;
                if (widget == null || widget.Name.LocalName != "widget")
                    throw new InvalidOperationException("Invalid Tizen config.xml");

                XNamespace widgetNs = widget.Name.Namespace;
                XNamespace tizenNs =
                    widget.GetNamespaceOfPrefix("tizen")
                    ?? "http://tizen.org/ns/widgets";

                // --------------------------------
                // Ensure <access origin="*" />
                // --------------------------------
                bool hasAccess = widget.Elements(widgetNs + "access")
                    .Any(e => (string?)e.Attribute("origin") == "*");

                if (!hasAccess)
                {
                    widget.AddFirst(
                        new XElement(widgetNs + "access",
                            new XAttribute("origin", "*"),
                            new XAttribute("subdomains", "true")));
                }

                // --------------------------------
                // Ensure internet privilege
                // --------------------------------
                bool hasInternetPrivilege = widget.Elements(tizenNs + "privilege")
                    .Any(e =>
                        (string?)e.Attribute("name") ==
                        "http://tizen.org/privilege/internet");

                if (!hasInternetPrivilege)
                {
                    var inputDevicePrivilege = widget.Elements(tizenNs + "privilege")
                        .FirstOrDefault(e =>
                            (string?)e.Attribute("name") ==
                            "http://tizen.org/privilege/tv.inputdevice");

                    var internetPrivilege = new XElement(
                        tizenNs + "privilege",
                        new XAttribute("name", "http://tizen.org/privilege/internet"));

                    if (inputDevicePrivilege != null)
                    {
                        inputDevicePrivilege.AddAfterSelf(internetPrivilege);
                    }
                    else
                    {
                        // Fallback: insert after <access>, or at top
                        var access = widget.Elements(widgetNs + "access").FirstOrDefault();
                        if (access != null)
                            access.AddAfterSelf(internetPrivilege);
                        else
                            widget.AddFirst(internetPrivilege);
                    }
                }

                await using (var stream = File.Create(path))
                {
                    await doc.SaveAsync(stream, SaveOptions.None, default);
                }
            }
    }
}
