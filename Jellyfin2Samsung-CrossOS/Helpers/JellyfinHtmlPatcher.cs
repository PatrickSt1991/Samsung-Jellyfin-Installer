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
                if (!js.Contains("__V79__"))
                {
                    string nativeCode = @"
/* === TIZEN V79 (Total API Satisfaction) === */
(function() {
    console.log('[NATIVE-V79] FINALIZING API BRIDGE');
    var activeVideo = null;
    var currentConfig = null;

    window.YT = {
        PlayerState: { UNSTARTED: -1, ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 },
        Player: function(id, config) {
            var self = this;
            currentConfig = config;
            var container = document.getElementById(id);
            
            var v = document.createElement('video');
            v.id = 'tizen_final_video';
            v.src = 'https://inv.perditum.com/latest_version?id=' + config.videoId + '&itag=18&local=true';
            v.style.cssText = 'width:100%;height:100%;background:#000;position:absolute;top:0;left:0;z-index:99999;';
            v.autoplay = true;
            activeVideo = v;

            // --- MANDATORY API FUNCTIONS ---
            this.playVideo = function() { v.play(); };
            this.pauseVideo = function() { v.pause(); };
            this.stopVideo = function() {
                console.log('[V79] STOP SEQUENCE');
                v.pause(); v.src = ''; v.load(); v.remove();
                activeVideo = null;
                if (config.events && config.events.onStateChange) {
                    config.events.onStateChange({ data: 0 }); 
                }
            };
            this.destroy = function() { this.stopVideo(); };
            
            // State & Time API
            this.getPlayerState = function() { 
                if (!v) return -1;
                if (v.ended) return 0;
                if (v.paused) return 2;
                return 1; // Playing
            };
            this.getCurrentTime = function() { return v ? v.currentTime : 0; };
            this.getDuration = function() { return v ? v.duration : 0; };
            this.getVideoLoadedFraction = function() { return 1; };

            // Volume API
            this.getVolume = function() { return v ? v.volume * 100 : 100; };
            this.setVolume = function(vol) { if(v) v.volume = vol / 100; };
            this.mute = function() { if(v) v.muted = true; };
            this.unMute = function() { if(v) v.muted = false; };
            this.isMuted = function() { return v ? v.muted : false; };
            // -----------------------------

            v.onended = function() { self.stopVideo(); };
            v.onplaying = function() {
                document.querySelectorAll('.docspinner, .mdl-spinner, .dialogContainer').forEach(s => s.remove());
            };

            if (container) {
                container.innerHTML = '';
                container.appendChild(v);
            }

            if (config.events && config.events.onReady) {
                setTimeout(function() { config.events.onReady({ target: self }); }, 200);
            }
        }
    };

    window.addEventListener('keydown', function(e) {
        if (!activeVideo) return;
        if (e.keyCode === 10009 || e.key === 'GoBack') {
            console.log('[V79] CLEAN EXIT VIA RETURN');
            e.preventDefault();
            e.stopPropagation();

            if (activeVideo) {
                activeVideo.pause(); activeVideo.src = ''; v.load(); activeVideo.remove(); activeVideo = null;
            }

            if (currentConfig && currentConfig.events && currentConfig.events.onStateChange) {
                currentConfig.events.onStateChange({ data: 0 });
            }

            setTimeout(function() {
                if (window.appRouter) { window.appRouter.back(); } else { window.history.back(); }
            }, 50);
        }
    }, true);

    if (typeof window.onYouTubeIframeAPIReady === 'function') window.onYouTubeIframeAPIReady();
})();
";
                    js = nativeCode + js;
                }
                await File.WriteAllTextAsync(file, js);
            }
        }
    }
}