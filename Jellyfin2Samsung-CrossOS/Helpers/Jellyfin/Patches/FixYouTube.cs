using Jellyfin2Samsung.Helpers.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Jellyfin2Samsung.Helpers.Jellyfin.Fixes
{
    public class FixYouTube
    {
        public async Task FixAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            foreach (var file in Directory.GetFiles(www, "youtubePlayer-plugin.*.js"))
            {
                var js = await File.ReadAllTextAsync(file);
                if (!js.Contains("__V80__"))
                {
                    Debug.WriteLine(AppSettings.Default.LocalYoutubeServer);
                    string nativeCode = @"
/* === TIZEN V80 (NATIVE BACK + LOCAL MP4) === */
(function() {
    console.log('[NATIVE-V80-MP4] INIT: Best of Both Worlds');
    
    var API_BASE = 'http://192.168.2.195:8123';
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
            // v.src is removed here; we fetch it async below
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

            // --- NEW: FETCH LOCAL MP4 ---
            async function loadVideo() {
                try {
                    console.log('[V80] Fetching MP4 from ' + API_BASE);
                    var res = await fetch(API_BASE + '/file', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ url: 'https://www.youtube.com/watch?v=' + config.videoId })
                    });
                    var data = await res.json();
                    
                    if (data.url) {
                        console.log('[V80] Playing: ' + data.url);
                        v.src = data.url;
                        v.play();
                    }
                } catch (e) {
                    console.error('[V80] Fetch failed: ' + e);
                }
            }
            
            // Start loading immediately
            loadVideo();

            if (config.events && config.events.onReady) {
                setTimeout(function() { config.events.onReady({ target: self }); }, 200);
            }
        }
    };

    // --- ORIGINAL BACK BUTTON LOGIC (Preserved) ---
    window.addEventListener('keydown', function(e) {
        if (!activeVideo) return;
        // KeyCode 10009 is Tizen 'Return'
        if (e.keyCode === 10009 || e.key === 'GoBack' || e.key === 'Back' || e.key === 'XF86Back') {
            console.log('[V80] RETURN PRESSED - Executing V80 Logic');
            e.preventDefault();
            e.stopPropagation();

            // 1. Kill Video
            if (activeVideo) {
                activeVideo.pause(); activeVideo.src = ''; activeVideo.load(); activeVideo.remove();
                activeVideo = null;
            }

            // 2. Notify Jellyfin (UI Cleanup)
            if (currentConfig && currentConfig.events && currentConfig.events.onStateChange) {
                currentConfig.events.onStateChange({ data: 0 });
            }

            // 3. Navigation (The part that worked for you)
            setTimeout(function() {
                if (window.appRouter) { 
                    window.appRouter.back(); 
                } else { 
                    window.history.back(); 
                }
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
        public async Task CorsAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "config.xml");
            XDocument doc = XDocument.Load(path);

            string[] domains = {
                "https://youtube.com",
                "https://downloadapi.stuff.solutions",
            };

            foreach (var d in domains)
            {
                if (!doc.Root.Elements("access").Any(e => (string?)e.Attribute("origin") == d))
                {
                    doc.Root.Add(new XElement("access", new XAttribute("origin", d), new XAttribute("subdomains", "true")));
                }
            }
            if (!doc.Root.Elements("allow-navigation").Any(e => (string?)e.Attribute("href") == "https://*.stuff.solutions"))
            {
                doc.Root.Add(new XElement("allow-navigation", new XAttribute("href", "https://*.stuff.solutions")));
            }

            doc.Save(path);
        }
    }
}
