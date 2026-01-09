using Jellyfin2Samsung.Helpers.Core;
using System.IO;
using System.Threading.Tasks;

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
