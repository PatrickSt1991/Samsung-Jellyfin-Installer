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

                if (!js.Contains("__NATIVE_STABLE_V1__"))
                {
                    string nativeCode = @"
/* === TIZEN NATIVE MP4 BRIDGE (STABLE V1) === */
(function() {
    if (window.__NATIVE_STABLE_V1__) return;
    window.__NATIVE_STABLE_V1__ = true;

    const log = (m) => console.log('[NATIVE-FIX] ' + m);
    const API_BASE = 'http://192.168.2.195:8123';
    
    window.YT = {
        PlayerState: { UNSTARTED: -1, ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 },
        Player: function(id, config) {
            log('Constructor: Initializing for ' + config.videoId);
            const self = this;
            const container = document.getElementById(id);
            
            // Create the video element
            const v = document.createElement('video');
            v.style.cssText = 'width:100%;height:100%;background:#000;position:absolute;top:0;left:0;z-index:99999;';
            v.autoplay = true;
            
            if (container) {
                container.innerHTML = '';
                container.appendChild(v);
            }

            // --- JELLYFIN COMPATIBLE METHODS ---
            // We define these so Jellyfin can call THEM to close the player
            this.playVideo = () => v.play();
            this.pauseVideo = () => v.pause();
            this.stopVideo = () => {
                log('Jellyfin requested STOP');
                v.pause();
                v.src = '';
                v.load();
                v.remove();
            };
            this.destroy = () => this.stopVideo();
            this.getCurrentTime = () => v.currentTime || 0;
            this.getDuration = () => v.duration || 0;
            this.getPlayerState = () => v.paused ? 2 : 1;

            // Handle hardware back button BY EMULATING A STOP
            // This prevents the freeze because we let Jellyfin handle the 'Back'
            const onKeyDown = (e) => {
                if (e.keyCode === 10009 || e.key === 'Back') {
                    log('Back Button -> Triggering natural exit');
                    // Don't call history.back() here! 
                    // Let the event bubble up so Jellyfin's own router catches it.
                    self.stopVideo();
                }
            };
            window.addEventListener('keydown', onKeyDown, { once: true });

            v.onplaying = () => {
                log('Playing: Hiding Spinners');
                document.querySelectorAll('.docspinner, .mdl-spinner, .dialogContainer').forEach(s => s.remove());
            };

            v.onended = () => {
                log('Video Ended');
                if (config.events && config.events.onStateChange) config.events.onStateChange({ data: 0 });
            };

            // Fetch the URL
            fetch(API_BASE + '/file', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url: 'https://www.youtube.com/watch?v=' + config.videoId })
            })
            .then(r => r.json())
            .then(data => {
                if (data.url) {
                    log('Setting SRC: ' + data.url);
                    v.src = data.url;
                }
            })
            .catch(e => log('Fetch error: ' + e));

            if (config.events && config.events.onReady) {
                setTimeout(() => config.events.onReady({ target: self }), 100);
            }
        }
    };

    if (typeof window.onYouTubeIframeAPIReady === 'function') {
        window.onYouTubeIframeAPIReady();
    }
})();
";
                    js = nativeCode + js;
                    await File.WriteAllTextAsync(file, js);
                }
            }
        }

        public async Task CorsAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "config.xml");
            XDocument doc = XDocument.Load(path);

            string[] domains = {
                "https://youtube.com",
                "http://192.168.2.195:8123", // Added your local server explicitly
            };

            foreach (var d in domains)
            {
                if (!doc.Root.Elements("access").Any(e => (string?)e.Attribute("origin") == d))
                {
                    doc.Root.Add(new XElement("access", new XAttribute("origin", d), new XAttribute("subdomains", "true")));
                }
            }

            // Ensure local IP navigation is allowed
            if (!doc.Root.Elements("allow-navigation").Any(e => (string?)e.Attribute("href") == "http://192.168.2.195:*"))
            {
                doc.Root.Add(new XElement("allow-navigation", new XAttribute("href", "http://192.168.2.195:*")));
            }

            doc.Save(path);
        }
    }
}