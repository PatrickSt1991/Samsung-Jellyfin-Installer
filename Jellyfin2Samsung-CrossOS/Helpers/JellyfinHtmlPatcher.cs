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
            Trace.WriteLine($"[EnsureTizenCors] config.xml path = {path}");

            if (!File.Exists(path))
            {
                Trace.WriteLine("[EnsureTizenCors] ERROR: config.xml not found");
                throw new FileNotFoundException("config.xml not found", path);
            }

            XDocument doc;
            await using (var stream = File.OpenRead(path))
            {
                doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, default);
            }

            Trace.WriteLine("[EnsureTizenCors] config.xml loaded");

            var widget = doc.Root;
            if (widget == null || widget.Name.LocalName != "widget")
            {
                Trace.WriteLine("[EnsureTizenCors] ERROR: Invalid root element");
                throw new InvalidOperationException("Invalid Tizen config.xml");
            }

            XNamespace widgetNs = widget.Name.Namespace;
            XNamespace tizenNs =
                widget.GetNamespaceOfPrefix("tizen")
                ?? "http://tizen.org/ns/widgets";

            Trace.WriteLine($"[EnsureTizenCors] widget namespace = {widgetNs}");
            Trace.WriteLine($"[EnsureTizenCors] tizen namespace  = {tizenNs}");

            // --------------------------------
            // Ensure <access origin="*" />
            // --------------------------------
            var accessElements = widget.Elements(widgetNs + "access").ToList();
            Trace.WriteLine($"[EnsureTizenCors] access elements found = {accessElements.Count}");

            bool hasAccess = accessElements
                .Any(e => (string?)e.Attribute("origin") == "*");

            Trace.WriteLine($"[EnsureTizenCors] has access origin=\"*\" = {hasAccess}");

            if (!hasAccess)
            {
                widget.AddFirst(
                    new XElement(widgetNs + "access",
                        new XAttribute("origin", "*"),
                        new XAttribute("subdomains", "true")));

                Trace.WriteLine("[EnsureTizenCors] Added <access origin=\"*\" subdomains=\"true\" />");
            }

            // --------------------------------
            // Ensure internet privilege
            // --------------------------------
            var privilegeElements = widget.Elements(tizenNs + "privilege").ToList();
            Trace.WriteLine($"[EnsureTizenCors] privilege elements found = {privilegeElements.Count}");

            bool hasInternetPrivilege = privilegeElements.Any(e =>
                (string?)e.Attribute("name") ==
                "http://tizen.org/privilege/internet");

            Trace.WriteLine($"[EnsureTizenCors] has internet privilege = {hasInternetPrivilege}");

            if (!hasInternetPrivilege)
            {
                var inputDevicePrivilege = privilegeElements.FirstOrDefault(e =>
                    (string?)e.Attribute("name") ==
                    "http://tizen.org/privilege/tv.inputdevice");

                var internetPrivilege = new XElement(
                    tizenNs + "privilege",
                    new XAttribute("name", "http://tizen.org/privilege/internet"));

                if (inputDevicePrivilege != null)
                {
                    inputDevicePrivilege.AddAfterSelf(internetPrivilege);
                    Trace.WriteLine("[EnsureTizenCors] Inserted internet privilege after tv.inputdevice");
                }
                else
                {
                    var access = widget.Elements(widgetNs + "access").FirstOrDefault();
                    if (access != null)
                    {
                        access.AddAfterSelf(internetPrivilege);
                        Trace.WriteLine("[EnsureTizenCors] Inserted internet privilege after <access>");
                    }
                    else
                    {
                        widget.AddFirst(internetPrivilege);
                        Trace.WriteLine("[EnsureTizenCors] Inserted internet privilege at top of <widget>");
                    }
                }
            }

            await using (var stream = File.Create(path))
            {
                await doc.SaveAsync(stream, SaveOptions.None, default);
            }

            Trace.WriteLine("[EnsureTizenCors] config.xml saved successfully");
        }
        public async Task PatchYoutubePlayerAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            if (!Directory.Exists(www))
                return;

            var files = Directory.GetFiles(www, "youtubePlayer-plugin.*.js");

            foreach (var file in files)
            {
                var js = await File.ReadAllTextAsync(file);

                if (js.Contains("origin:\"https://www.youtube.com\""))
                    continue;

                const string marker = "playerVars:{";
                var idx = js.IndexOf(marker, StringComparison.Ordinal);

                if (idx == -1)
                    continue;

                var start = idx + marker.Length;
                var end = js.IndexOf('}', start);

                if (end == -1)
                    continue;

                var playerVars = js.Substring(start, end - start);

                var patchedPlayerVars =
                    playerVars +
                    ",origin:\"https://www.youtube.com\"" +
                    ",host:\"https://www.youtube.com\"";

                js =
                    js.Substring(0, start) +
                    patchedPlayerVars +
                    js.Substring(end);

                await File.WriteAllTextAsync(file, js);
            }
        }

    }
}
