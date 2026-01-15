using Jellyfin2Samsung.Helpers.Core;
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
                    string nativeCode = @"
/* === TIZEN V114 (SIMPLE MP4 & NATIVE EXIT) === */
(function () {
    // Prevent double-loading
    if (window.YT_STABLE) return;
    window.YT_STABLE = true;

    console.log('[V114] INIT: Native MP4 Player Bridge');

    // Your local backend that returns the direct MP4
    var API_BASE = 'http://192.168.2.195:8123'; 
    var player = null;
    var currentState = -1;

    function log(m) { console.log('[V114]', m); }
    
    // Helper to sync state with Jellyfin
    function setState(s) {
        currentState = s;
        if (player && player.onStateChange) {
            player.onStateChange({ data: s });
        }
    }

    window.YT = {
        PlayerState: { UNSTARTED: -1, ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 },
        Player: function (id, config) {
            player = this;
            var self = this;
            var container = document.getElementById(id);

            // 1. Create the Video Element (Simple & Clean)
            var v = document.createElement('video');
            v.id = 'native_mp4_player';
            v.style.cssText = 'width:100%;height:100%;background:#000;position:absolute;top:0;left:0;z-index:99999;';
            v.autoplay = true;
            v.controls = false; // Jellyfin handles the UI overlay
            
            if (container) {
                container.innerHTML = '';
                container.appendChild(v);
            }

            // 2. The Native Exit Logic (Fixes the Black Screen)
            function performExit() {
                log('Exiting Player...');
                
                // Cleanup Video Hardware
                v.pause();
                v.src = """";
                v.load();
                if (v.parentNode) v.parentNode.removeChild(v);

                // Tell Jellyfin we are done
                setState(0); // ENDED

                // Cleanup Listener
                window.removeEventListener('keydown', handleKeys);

                // FORCE Jellyfin Navigation
                // This is what the native player uses to return to Movie Details
                if (window.AppHost && window.AppHost.back) {
                    window.AppHost.back();
                } else {
                    window.history.back();
                }
            }

            var handleKeys = function (e) {
                // KeyCode 10009 = Tizen Return/Back
                if (e.keyCode === 10009 || e.key === ""Back"" || e.key === ""XF86Back"") {
                    log('Return Key Detected');
                    e.preventDefault();
                    e.stopPropagation();
                    performExit();
                }
            };

            window.addEventListener('keydown', handleKeys);

            // 3. Simple Event Listeners
            v.addEventListener('playing', function () { setState(1); });
            v.addEventListener('pause', function () { setState(2); });
            v.addEventListener('ended', function () { performExit(); });
            v.onerror = function () { 
                console.error('[V114] Error: ' + (v.error ? v.error.code : 'unknown'));
                performExit(); 
            };

            // 4. Fetch the MP4 from your local backend
            async function loadVideo(videoId) {
                try {
                    log('Fetching MP4 for: ' + videoId);
                    const res = await fetch(API_BASE + '/file', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ url: 'https://www.youtube.com/watch?v=' + videoId })
                    });
                    
                    const data = await res.json();
                    
                    if (data.url) {
                        log('Playing: ' + data.url);
                        v.src = data.url;
                        v.play();
                    } else {
                        log('No URL returned from backend');
                        performExit();
                    }
                } catch (e) {
                    console.error('[V114] Backend Fetch Error: ' + e);
                    performExit();
                }
            }

            // Start the process
            loadVideo(config.videoId);

            // 5. API Stubs (Required by Jellyfin)
            this.playVideo = function () { v.play(); };
            this.pauseVideo = function () { v.pause(); };
            this.stopVideo = function () { performExit(); };
            this.getCurrentTime = function () { return v.currentTime || 0; };
            this.getDuration = function () { return v.duration || 0; };
            this.getPlayerState = function () { return currentState; };
            
            // Notify Jellyfin the ""API"" is ready
            setTimeout(function() {
                if (config.events && config.events.onReady) {
                    config.events.onReady({ target: self });
                }
            }, 200);
        }
    };
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
