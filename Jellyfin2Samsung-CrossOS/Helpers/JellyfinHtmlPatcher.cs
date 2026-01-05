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
            XDocument doc = XDocument.Load(path);

            string[] domains = {
        "https://yewtu.be",
        "https://invidious.f5.si",
        "https://invidious.nerdvpn.de"
    };

            foreach (var d in domains)
            {
                if (!doc.Root.Elements("access").Any(e => (string?)e.Attribute("origin") == d))
                {
                    doc.Root.Add(new XElement("access", new XAttribute("origin", d), new XAttribute("subdomains", "true")));
                }
            }
            doc.Save(path);
        }
        public async Task PatchYoutubePlayerAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            foreach (var file in Directory.GetFiles(www, "youtubePlayer-plugin.*.js"))
            {
                var js = await File.ReadAllTextAsync(file);
                js = js.Replace("https://www.youtube.com/iframe_api", "data:text/javascript,console.log('[NATIVE] Bridge Active')");

                if (!js.Contains("__TIZEN_CLEAN_V33__"))
                {
                    string nativeCode = @"
/* === TIZEN CLEAN V33 === */
var __TIZEN_CLEAN_V33__ = true;

// 1. FORCED FOCUS RECOVERY: Keep focus on the main window every 2 seconds
// This ensures that even if an iframe 'steals' focus, the TV remains responsive.
setInterval(function() {
    if (document.activeElement && document.activeElement.tagName === 'IFRAME') {
        // Do nothing, let user interact with video
    } else {
        window.focus(); 
    }
}, 2000);

function __tPlayEmbed(videoId) {
    var existing = document.getElementById('tizen_youtube_player');
    if (existing) existing.remove();

    var f = document.createElement('iframe');
    f.id = 'tizen_youtube_player';
    
    // We add a 'clean' parameter to the URL to try and minimize UI
    f.src = 'https://inv.perditum.com/embed/' + videoId + '?autoplay=1&local=true&controlBar=false';
    
    f.setAttribute('allow', 'autoplay; encrypted-media');
    
    // 2. THE CSS NUKE: We inject styles directly into the player via the URL 
    // to hide the 'X', the spinner, and the error dialog.
    f.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;z-index:999999;background:#000;border:none;';
    
    document.body.appendChild(f);

    // 3. IFRAME INJECTION: Hide VideoJS error elements
    f.onload = function() {
        try {
            var style = document.createElement('style');
            style.innerHTML = '.vjs-error-display, .vjs-modal-dialog, .vjs-loading-spinner, .vjs-close-button { display: none !important; visibility: hidden !important; }';
            f.contentDocument.head.appendChild(style);
        } catch(e) { console.log('Cross-origin prevents deep CSS injection'); }
    };

    // Remove Jellyfin loading spinners
    setTimeout(function() {
        document.querySelectorAll('.docspinner, .mdl-spinner').forEach(s => s.remove());
    }, 1000);
}

window.YT = {
    Player: function(id, config) {
        if (config.videoId) __tPlayEmbed(config.videoId);
        this.destroy = function() { 
            var f = document.getElementById('tizen_youtube_player');
            if(f) f.remove();
        };
        this.stopVideo = function(){ this.destroy(); };
    }
};
";
                    js = nativeCode + js;
                }
                await File.WriteAllTextAsync(file, js);
            }
        }
    }
}