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

            var serverUrl = AppSettings.Default.JellyfinFullUrl.TrimEnd('/');

            // Avoid duplicates
            if (!servers.Any(s => s?.GetValue<string>() == serverUrl))
            {
                servers.Add(serverUrl);
            }

            await File.WriteAllTextAsync(path, config.ToJsonString());

        }

        /// <summary>
        /// Injects auto-login credentials into the Jellyfin web app.
        /// This stores the access token and server info in localStorage format.
        /// </summary>
        public async Task InjectAutoLoginAsync(PackageWorkspace ws)
        {
            var accessToken = AppSettings.Default.JellyfinAccessToken;
            var userId = AppSettings.Default.JellyfinUserId;
            var serverUrl = AppSettings.Default.JellyfinFullUrl.TrimEnd('/');

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(serverUrl))
            {
                Trace.WriteLine("[InjectAutoLogin] Missing credentials, skipping auto-login injection");
                return;
            }

            string indexPath = Path.Combine(ws.Root, "www", "index.html");
            if (!File.Exists(indexPath))
            {
                Trace.WriteLine("[InjectAutoLogin] index.html not found");
                return;
            }

            var html = await File.ReadAllTextAsync(indexPath);

            // Create the credentials object that Jellyfin web expects
            var credentialsScript = new StringBuilder();
            credentialsScript.AppendLine("<script>");
            credentialsScript.AppendLine("(function() {");
            credentialsScript.AppendLine("  try {");
            credentialsScript.AppendLine($"    var serverUrl = '{EscapeJsString(serverUrl)}';");
            credentialsScript.AppendLine($"    var userId = '{EscapeJsString(userId)}';");
            credentialsScript.AppendLine($"    var accessToken = '{EscapeJsString(accessToken)}';");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    // Create credentials object matching Jellyfin's expected format");
            credentialsScript.AppendLine("    var credentials = {");
            credentialsScript.AppendLine("      Servers: [{");
            credentialsScript.AppendLine("        ManualAddress: serverUrl,");
            credentialsScript.AppendLine("        Id: serverUrl.replace(/[^a-zA-Z0-9]/g, ''),");
            credentialsScript.AppendLine("        UserId: userId,");
            credentialsScript.AppendLine("        AccessToken: accessToken,");
            credentialsScript.AppendLine("        DateLastAccessed: new Date().getTime()");
            credentialsScript.AppendLine("      }]");
            credentialsScript.AppendLine("    };");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    // Store in localStorage");
            credentialsScript.AppendLine("    localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));");
            credentialsScript.AppendLine();
            credentialsScript.AppendLine("    console.log('[Auto-Login] Credentials injected for server: ' + serverUrl);");
            credentialsScript.AppendLine("  } catch(e) {");
            credentialsScript.AppendLine("    console.error('[Auto-Login] Failed to inject credentials:', e);");
            credentialsScript.AppendLine("  }");
            credentialsScript.AppendLine("})();");
            credentialsScript.AppendLine("</script>");

            // Inject before </head> to ensure it runs before Jellyfin's scripts
            html = html.Replace("</head>", credentialsScript + "\n</head>");

            await File.WriteAllTextAsync(indexPath, html);
            Trace.WriteLine("[InjectAutoLogin] Auto-login credentials injected successfully");
        }

        private static string EscapeJsString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
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
                if (!js.Contains("__V80__"))
                {
                    string nativeCode = @"
/* === TIZEN V80 (Iron-Clad API) === */
(function() {
    console.log('[NATIVE-V80] FINAL API BRIDGE');
    var activeVideo = null;
    var currentConfig = null;

    window.YT = {
        PlayerState: { UNSTARTED: -1, ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 },
        Player: function(id, config) {
            var self = this;
            currentConfig = config;
            var container = document.getElementById(id);
            
            var v = document.createElement('video');
            v.id = 'tizen_v80_video';
            v.src = 'https://inv.perditum.com/latest_version?id=' + config.videoId + '&itag=18&local=true';
            v.style.cssText = 'width:100%;height:100%;background:#000;position:absolute;top:0;left:0;z-index:99999;';
            v.autoplay = true;
            activeVideo = v;

            // --- API METHODS ---
            this.playVideo = function() { if(activeVideo) activeVideo.play(); };
            this.pauseVideo = function() { if(activeVideo) activeVideo.pause(); };
            this.stopVideo = function() {
                console.log('[V80] STOPPING');
                if(activeVideo) {
                    activeVideo.pause(); activeVideo.src = ''; activeVideo.load(); activeVideo.remove();
                }
                activeVideo = null;
                if (config.events && config.events.onStateChange) {
                    config.events.onStateChange({ data: 0 }); 
                }
            };
            this.destroy = function() { this.stopVideo(); };
            
            // State & Time
            this.getPlayerState = function() { 
                if (!activeVideo) return -1;
                if (activeVideo.ended) return 0;
                if (activeVideo.paused) return 2;
                return 1;
            };
            this.getCurrentTime = function() { return activeVideo ? activeVideo.currentTime : 0; };
            this.getDuration = function() { return activeVideo ? activeVideo.duration : 0; };
            this.getVideoLoadedFraction = function() { return 1; };

            // Volume
            this.getVolume = function() { return activeVideo ? activeVideo.volume * 100 : 100; };
            this.setVolume = function(vol) { if(activeVideo) activeVideo.volume = vol / 100; };
            this.mute = function() { if(activeVideo) activeVideo.muted = true; };
            this.unMute = function() { if(activeVideo) activeVideo.muted = false; };
            this.isMuted = function() { return activeVideo ? activeVideo.muted : false; };

            // Playback Rates (Jellyfin checks these)
            this.getPlaybackRate = function() { return 1; };
            this.setPlaybackRate = function(r) { };
            this.getAvailablePlaybackRates = function() { return [1]; };
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
            console.log('[V80] RETURN PRESSED');
            e.preventDefault();
            e.stopPropagation();

            if (activeVideo) {
                activeVideo.pause(); activeVideo.src = ''; activeVideo.load(); activeVideo.remove();
                activeVideo = null;
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