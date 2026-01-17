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

                var apiBase = AppSettings.Default.LocalYoutubeServer.TrimEnd('/');

                if (js.Contains("__NATIVE_STABLE_V1__"))
                    continue;
                string nativeCode = $@"
/* === TIZEN NATIVE MP4 BRIDGE (STABLE V1.4) === */
(function () {{
    if (window.__NATIVE_STABLE_V1__) return;
    window.__NATIVE_STABLE_V1__ = true;

    var API_BASE = '{apiBase}';

    function jfLog() {{
        try {{
            console.log('[YT-NATIVE]', Array.prototype.join.call(arguments, ' '));
        }} catch (e) {{}}
    }}

    window.YT = {{
        PlayerState: {{
            UNSTARTED: -1,
            ENDED: 0,
            PLAYING: 1,
            PAUSED: 2,
            BUFFERING: 3,
            CUED: 5
        }},

        Player: function (id, config) {{
            jfLog('YT.Player ctor', id, config && config.videoId);

            var self = this;
            var container = document.getElementById(id);

            var muted = false;
            var volume = 100;
            var destroyed = false;
            var backHandled = false;
            var started = false;

            var v = document.createElement('video');
            v.style.cssText =
                'width:100%;height:100%;background:#000;' +
                'position:absolute;top:0;left:0;';
            v.autoplay = true;
            v.setAttribute('playsinline', 'playsinline');

            if (container) {{
                container.innerHTML = '';
                container.appendChild(v);
            }}

            function emitState(state) {{
                if (config && config.events && config.events.onStateChange) {{
                    try {{
                        config.events.onStateChange({{ data: state }});
                    }} catch (e) {{}}
                }}
            }}

            /* === Jellyfin-required API === */
            this.setSize = function () {{}};

            this.playVideo = function () {{
                jfLog('playVideo');
                try {{ v.play(); }} catch (e) {{}}
            }};

            this.pauseVideo = function () {{
                jfLog('pauseVideo');
                try {{ v.pause(); }} catch (e) {{}}
            }};

            this.stopVideo = function () {{
                jfLog('stopVideo');
                try {{ v.pause(); }} catch (e) {{}}
                try {{ v.removeAttribute('src'); }} catch (e) {{}}
                try {{ v.load(); }} catch (e) {{}}
            }};

            this.destroy = function () {{
                if (destroyed) return;
                destroyed = true;

                jfLog('destroy');

                try {{ window.removeEventListener('keydown', onKeyDown, true); }} catch (e) {{}}
                try {{ self.stopVideo(); }} catch (e) {{}}

                try {{
                    v.onplaying =
                    v.onpause =
                    v.onended =
                    v.oncanplay =
                    v.onerror = null;
                }} catch (e) {{}}

                try {{ v.remove(); }} catch (e) {{}}
            }};

            this.seekTo = function (s) {{
                jfLog('seekTo', s);
                try {{ v.currentTime = s; }} catch (e) {{}}
            }};

            this.getCurrentTime = function () {{ return v.currentTime || 0; }};
            this.getDuration = function () {{ return v.duration || 0; }};

            this.getPlayerState = function () {{
                if (v.ended) return YT.PlayerState.ENDED;
                if (v.paused) return YT.PlayerState.PAUSED;
                return YT.PlayerState.PLAYING;
            }};

            this.setVolume = function (val) {{
                volume = val;
                try {{
                    v.volume = Math.max(0, Math.min(1, val / 100));
                }} catch (e) {{}}
            }};

            this.getVolume = function () {{ return volume; }};
            this.mute = function () {{ muted = true; try {{ v.muted = true; }} catch (e) {{}} }};
            this.unMute = function () {{ muted = false; try {{ v.muted = false; }} catch (e) {{}} }};
            this.isMuted = function () {{ return muted; }};

            /* === Drive Jellyfin state machine === */
            v.onplaying = function () {{
                jfLog('video playing');
                if (!started) {{
                    started = true;
                    emitState(YT.PlayerState.PLAYING);
                }}
            }};

            v.onpause = function () {{
                jfLog('video paused');
                emitState(YT.PlayerState.PAUSED);
            }};

            v.onended = function () {{
                jfLog('video ended naturally');
                emitState(YT.PlayerState.ENDED);
            }};

            v.onerror = function () {{
                jfLog('video error');
            }};

            /* === Start only when playable === */
            v.oncanplay = function () {{
                if (destroyed) return;
                jfLog('canplay -> starting playback');
                try {{ v.play(); }} catch (e) {{}}
            }};

            /* === BACK key: PAUSE ONLY (delegate stop to Jellyfin) === */
            function onKeyDown(e) {{
                if (backHandled || destroyed) return;

                if (e && (e.keyCode === 10009 || e.key === 'Back')) {{
                    backHandled = true;
                    jfLog('BACK key (pause only)');

                    try {{ if (e.preventDefault) e.preventDefault(); }} catch (e1) {{}}
                    try {{ if (e.stopPropagation) e.stopPropagation(); }} catch (e2) {{}}

                    emitState(YT.PlayerState.PAUSED);

                    try {{ window.removeEventListener('keydown', onKeyDown, true); }} catch (e3) {{}}
                }}
            }}
            window.addEventListener('keydown', onKeyDown, true);

            /* === Fetch local MP4 === */
            jfLog('fetching mp4');
            fetch(API_BASE + '/file', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/json' }},
                body: JSON.stringify({{
                    url: 'https://www.youtube.com/watch?v=' +
                        (config ? config.videoId : '')
                }})
            }})
            .then(function (r) {{ return r.json(); }})
            .then(function (data) {{
                jfLog('fetch result', data && data.url);
                if (destroyed) return;

                if (data && data.url) {{
                    v.src = data.url;
                }}
            }})
            .catch(function (err) {{
                jfLog('fetch error', err);
            }});

            if (config && config.events && config.events.onReady) {{
                jfLog('onReady');
                try {{
                    config.events.onReady({{ target: self }});
                }} catch (e) {{}}
            }}
        }}
    }};

    // IMPORTANT:
    // Do NOT call onYouTubeIframeAPIReady here.
    // Jellyfin will invoke it when appropriate.
}})();
";


                js = nativeCode + js;
                await File.WriteAllTextAsync(file, js);
            }
        }
        public async Task CorsAsync(PackageWorkspace ws)
        {
            var apiBase = AppSettings.Default.LocalYoutubeServer.TrimEnd('/');
            string path = Path.Combine(ws.Root, "config.xml");
            XDocument doc = XDocument.Load(path);

            string[] domains = {
                "https://youtube.com",
                apiBase
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