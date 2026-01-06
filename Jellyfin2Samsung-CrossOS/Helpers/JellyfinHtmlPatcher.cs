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
                "https://invidious.nerdvpn.de",
                "https://inv.perditum.com"
            };

            foreach (var d in domains)
            {
                if (!doc.Root.Elements("access").Any(e => (string?)e.Attribute("origin") == d))
                {
                    doc.Root.Add(new XElement("access", new XAttribute("origin", d), new XAttribute("subdomains", "true")));
                }
            }
            if (!doc.Root.Elements("allow-navigation").Any(e => (string?)e.Attribute("href") == "https://*.perditum.com"))
            {
                doc.Root.Add(new XElement("allow-navigation", new XAttribute("href", "https://*.perditum.com")));
            }

            doc.Save(path);
        }
        public async Task PatchYoutubePlayerAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            foreach (var file in Directory.GetFiles(www, "youtubePlayer-plugin.*.js"))
            {
                var js = await File.ReadAllTextAsync(file);
                if (!js.Contains("__V67__"))
                {
                    string nativeCode = @"
/* === TIZEN V67 (Deep Logging) === */
(function() {
    console.log('[NATIVE-V67] INITIALIZING RECOVERY SYSTEM');
    var activeVideo = null;

    window.YT = {
        PlayerState: { ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 },
        Player: function(id, config) {
            console.log('[NATIVE-V67] TARGET ID: ' + config.videoId);
            var self = this;
            
            var v = document.createElement('video');
            v.id = 'tizen_yt_video';
            
            // We'll use the URL you confirmed works on your laptop (itag 18) for stability
            var targetUrl = 'https://inv.perditum.com/latest_version?id=' + config.videoId + '&itag=18&local=true';
            console.log('[NATIVE-V67] SETTING SRC: ' + targetUrl);
            v.src = targetUrl;
            
            v.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;z-index:99998;background:#000;';
            v.autoplay = true;
            activeVideo = v;

            // TRACKING LOGS
            v.onloadstart = function() { console.log('[NATIVE-V67] VIDEO LOAD START'); };
            v.oncanplay = function() { console.log('[NATIVE-V67] CAN PLAY - CLIENT READY'); };
            v.onplaying = function() { 
                console.log('[NATIVE-V67] SUCCESS: PLAYING STARTED'); 
                document.querySelectorAll('.docspinner, .mdl-spinner, .dialogContainer').forEach(function(s){ s.remove(); });
            };
            
            v.onerror = function() {
                var err = v.error ? (v.error.code + ' ' + v.error.message) : 'Unknown Error';
                console.error('[NATIVE-V67] VIDEO ERROR: ' + err);
            };

            this.playVideo = function() { 
                console.log('[NATIVE-V67] EXECUTE .play()');
                v.play().catch(function(e) {
                    console.warn('[NATIVE-V67] PLAY BLOCKED: ' + e.message);
                });
            };
            
            this.stopVideo = function() { v.pause(); v.remove(); activeVideo = null; };
            this.destroy = function() { this.stopVideo(); };

            document.body.appendChild(v);
            
            if (config.events && config.events.onReady) {
                setTimeout(function() {
                    console.log('[NATIVE-V67] SIGNALING JELLYFIN READY');
                    config.events.onReady({ target: self });
                    self.playVideo();
                }, 1000);
            }
        }
    };

    window.addEventListener('keydown', function(e) {
        if (!activeVideo) return;
        console.log('[NATIVE-V67] KEY PRESSED: ' + e.keyCode);
        if (e.keyCode === 13 || e.key === 'Enter') {
            console.log('[NATIVE-V67] MANUAL PLAY TRIGGERED');
            activeVideo.play();
        } else if (e.keyCode === 10009 || e.key === 'GoBack') {
            activeVideo.pause(); activeVideo.remove(); activeVideo = null;
            e.preventDefault();
        }
    }, true);

    if (typeof window.onYouTubeIframeAPIReady === 'function') {
        console.log('[NATIVE-V67] BOOTSTRAPPING API');
        window.onYouTubeIframeAPIReady();
    }
})();
";
                    js = nativeCode + js;
                }
                await File.WriteAllTextAsync(file, js);
            }
        }
    }
}