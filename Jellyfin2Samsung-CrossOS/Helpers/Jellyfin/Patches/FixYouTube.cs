using Jellyfin2Samsung.Helpers.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Jellyfin2Samsung.Helpers.Jellyfin.Fixes
{
    public class FixYouTube
    {
        // UPDATED PATCH - Properly integrates with Jellyfin's player lifecycle
        public async Task PatchPluginAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            var utf8NoBom = new UTF8Encoding(false);

            var candidates = Directory.GetFiles(www, "youtubePlayer-plugin*.js", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(www, "youtubePlayer-plugin*.chunk.js", SearchOption.AllDirectories))
                .Distinct()
                .ToList();

            // V16: Simple YT object protection (no script blocking)
            string injected = """
(function () {
  if (window.__YT_FIX_V16__) return;
  window.__YT_FIX_V16__ = true;

  var SERVICE_BASE = 'http://localhost:8123';
  var currentPlayerInstance = null;
  
  function sLog(msg, data) {
    try {
      var xhr = new XMLHttpRequest();
      xhr.open('POST', SERVICE_BASE + '/log', true);
      xhr.setRequestHeader('Content-Type', 'application/json');
      var cleanData = (data && typeof data === 'object') ? JSON.stringify(data) : (data || '');
      xhr.send(JSON.stringify({args: ['[V16]', msg, cleanData]}));
    } catch(e) {}
  }

  sLog('LOADED', { href: window.location.href });

  try {
    var appId = tizen.application.getCurrentApplication().appInfo.id;
    var pkgId = appId.split('.')[0];
    tizen.application.launch(pkgId + '.ytresolver', function() { sLog('SVC_LAUNCH_OK'); }, function(e) { sLog('SVC_LAUNCH_ERR', e.message); });
  } catch (e) { sLog('SVC_LAUNCH_FAIL', e.message); }

  // ========================================================================
  // CUSTOM PLAYER CLASS - Mirrors Jellyfin's YoutubePlayer interface
  // ========================================================================
  function CustomPlayer(idOrEl, cfg) {
    var self = this;
    currentPlayerInstance = this;
    
    var videoId = '';
    if (typeof cfg === 'string') videoId = cfg;
    else if (cfg && typeof cfg === 'object') videoId = cfg.videoId || cfg.id || '';
    
    this._state = -1;
    this._currentTime = 0;
    this._duration = 0;
    this._volume = 100;
    this._ready = false;
    this._queue = [];
    this._destroyed = false;
    this._container = null;
    this._iframe = null;
    this._observer = null;
    this._messageHandler = null;

    var container = (typeof idOrEl === 'string') ? document.getElementById(idOrEl) : idOrEl;
    this._container = container;
    
    var iframe = document.createElement('iframe');
    this._iframe = iframe;
    
    // Message handler for iframe communication
    this._messageHandler = function(ev) {
        if (self._destroyed) return;
        var m = ev.data;
        if (!m || !m.__ytbridge) return;
        
        if (m.type === 'ready') {
            self._ready = true;
            sLog('IFRAME_READY');
            
            // HIDE SPINNER AGAIN (in case it reappeared)
            try {
                if (window.Loading && window.Loading.hide) {
                    window.Loading.hide();
                }
                var spinners = document.querySelectorAll('.docspinner');
                for (var i = 0; i < spinners.length; i++) {
                    spinners[i].classList.remove('mdlSpinnerActive');
                }
            } catch(e) {}
            
            if (cfg.events && cfg.events.onReady) cfg.events.onReady({ target: self });
            
            // Process queued commands
            while(self._queue.length) { 
                var q = self._queue.shift(); 
                self._send(q.cmd, q.val); 
            }
            
            // ENSURE AUTOPLAY: Jellyfin expects video to start playing immediately
            // The YouTube iframe should auto-play, but we'll trigger it again to be sure
            setTimeout(function() {
                sLog('AUTOPLAY_TRIGGER');
                self._send('play');
            }, 200);
        } else if (m.type === 'state') {
            self._state = m.data;
            if (cfg.events && cfg.events.onStateChange) {
                cfg.events.onStateChange({ target: self, data: m.data });
            }
        } else if (m.type === 'time') {
            self._currentTime = m.t / 1000;
            self._duration = m.d / 1000;
            self._state = m.s;
        } else if (m.type === 'error') {
            if (cfg.events && cfg.events.onError) {
                cfg.events.onError(m.data);
            }
        }
    };
    
    window.addEventListener('message', this._messageHandler);
    
    function mount() {
        if (!container || self._destroyed) return;
        sLog('MOUNTING', { extractedId: videoId });

        if (!videoId) {
            sLog('ERR_MISSING_ID', 'Missing videoId');
            return;
        }

        // HIDE JELLYFIN LOADING SPINNER
        try {
            if (window.Loading && window.Loading.hide) {
                window.Loading.hide();
                sLog('SPINNER_HIDDEN_VIA_API');
            }
            // Fallback: directly remove spinner classes
            var spinners = document.querySelectorAll('.docspinner');
            for (var i = 0; i < spinners.length; i++) {
                spinners[i].classList.remove('mdlSpinnerActive');
            }
        } catch(e) {
            sLog('SPINNER_HIDE_ERR', e.message);
        }

        // Use fixed positioning with max z-index to stay on top
        iframe.style.cssText = 'width:100vw; height:100vh; border:0; background:#000; position:fixed; top:0; left:0; z-index:2147483647;';
        iframe.setAttribute('allow', 'autoplay; encrypted-media; fullscreen');
        iframe.src = SERVICE_BASE + '/player.html?videoId=' + encodeURIComponent(videoId);
        
        container.innerHTML = '';
        container.appendChild(iframe);

        // Watch for React wiping the container
        self._observer = new MutationObserver(function(mutations) {
            if (self._destroyed) return;
            if (container && !container.contains(iframe)) {
                sLog('REACT_WIPE_RESTORE');
                container.appendChild(iframe);
            }
        });
        self._observer.observe(container, { childList: true });
    }

    this._send = function(cmd, val) {
        if (this._destroyed) return;
        if (!this._ready) { 
            this._queue.push({cmd:cmd, val:val}); 
            return; 
        }
        if (this._iframe && this._iframe.contentWindow) {
            this._iframe.contentWindow.postMessage({ __ytbridge_cmd: true, cmd: cmd, val: val }, '*');
        }
    };

    // API Methods matching YT.Player interface
    this.playVideo = function() { 
        sLog('CMD_PLAY');
        this._send('play'); 
    };
    
    this.pauseVideo = function() { 
        sLog('CMD_PAUSE');
        this._send('pause'); 
    };
    
    this.stopVideo = function() { 
        sLog('CMD_STOP');
        this._send('stop'); 
    };
    
    this.seekTo = function(s, allowSeekAhead) { 
        sLog('CMD_SEEK', s);
        this._send('seek', s * 1000); 
    };
    
    this.setVolume = function(v) { 
        this._volume = v; 
        this._send('volume', v); 
    };
    
    this.getVolume = function() { 
        return this._volume; 
    };
    
    this.getCurrentTime = function() { 
        return this._currentTime; 
    };
    
    this.getDuration = function() { 
        return this._duration; 
    };
    
    this.getPlayerState = function() { 
        return this._state; 
    };
    
    this.mute = function() {
        this._send('mute', true);
    };
    
    this.unMute = function() {
        this._send('mute', false);
    };
    
    this.isMuted = function() {
        return this._muted || false;
    };
    
    this.setSize = function(width, height) {
        // Size is already 100vw/100vh, no action needed
        sLog('SET_SIZE', { w: width, h: height });
    };
    
    // CRITICAL: Proper cleanup to prevent background playback
    this.destroy = function() { 
        sLog('DESTROY_CALLED');
        
        if (this._destroyed) return;
        this._destroyed = true;
        
        // Stop playback first
        if (this._iframe && this._iframe.contentWindow) {
            try {
                this._iframe.contentWindow.postMessage({ __ytbridge_cmd: true, cmd: 'stop' }, '*');
            } catch(e) {
                sLog('DESTROY_STOP_ERR', e.message);
            }
        }
        
        // Clean up event listeners
        if (this._messageHandler) {
            window.removeEventListener('message', this._messageHandler);
            this._messageHandler = null;
        }
        
        // Disconnect observer
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
        
        // Remove iframe from DOM
        if (this._iframe) {
            if (this._iframe.parentNode) {
                this._iframe.parentNode.removeChild(this._iframe);
            }
            this._iframe = null;
        }
        
        // Clear container
        if (this._container) {
            this._container.innerHTML = '';
            this._container = null;
        }
        
        // Clear queue
        this._queue = [];
        
        if (currentPlayerInstance === this) {
            currentPlayerInstance = null;
        }
        
        sLog('DESTROYED');
    };

    mount();
  }

  // ========================================================================
  // YT NAMESPACE - Make it immutable to prevent overwriting
  // ========================================================================
  var customYT = {
    Player: CustomPlayer,
    PlayerState: { 
      UNSTARTED: -1, 
      ENDED: 0, 
      PLAYING: 1, 
      PAUSED: 2, 
      BUFFERING: 3, 
      CUED: 5 
    },
    loaded: 1,
    __CUSTOM__: true
  };

  // Make YT property non-writable so it can't be overwritten
  Object.defineProperty(window, 'YT', {
    value: customYT,
    writable: false,
    configurable: false,
    enumerable: true
  });
  
  sLog('YT_PROTECTED');

  // Trigger ready callback if it exists
  if (window.onYouTubeIframeAPIReady) {
    setTimeout(function() {
      sLog('TRIGGER_READY_CALLBACK');
      window.onYouTubeIframeAPIReady();
    }, 100);
  }

  // ========================================================================
  // NAVIGATION CLEANUP - Hook into Jellyfin's router (FIXED)
  // ========================================================================
  
  var lastPath = window.location.pathname;
  
  // Listen for Jellyfin page changes
  document.addEventListener('viewshow', function() {
    var currentPath = window.location.pathname;
    sLog('VIEW_SHOW_EVENT', { lastPath: lastPath, currentPath: currentPath });
    
    // Only cleanup if we actually navigated away (path changed)
    if (currentPath !== lastPath) {
      lastPath = currentPath;
      
      // If we're navigating away from video page, ensure cleanup
      if (currentPath !== '/video' && currentPlayerInstance) {
        sLog('NAV_CLEANUP_TRIGGER');
        try {
          currentPlayerInstance.destroy();
        } catch(e) {
          sLog('NAV_CLEANUP_ERR', e.message);
        }
      }
    } else {
      sLog('VIEW_SHOW_SAME_PATH');
    }
  });
  
  // Also listen for back button via popstate
  window.addEventListener('popstate', function() {
    sLog('POPSTATE_EVENT');
    setTimeout(function() {
      var currentPath = window.location.pathname;
      if (currentPath !== '/video' && currentPlayerInstance) {
        sLog('POPSTATE_CLEANUP_TRIGGER');
        try {
          currentPlayerInstance.destroy();
        } catch(e) {
          sLog('POPSTATE_CLEANUP_ERR', e.message);
        }
      }
    }, 100);
  });

  sLog('INIT_COMPLETE');
})();
""";

            foreach (var file in candidates)
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains("__YT_FIX_V16__")) continue;
                await File.WriteAllTextAsync(file, injected + "\n" + content, utf8NoBom);
            }
        }

        // 2. CREATE THE NODE.JS SERVICE (unchanged, but added mute support)
        public async Task CreateYouTubeResolverAsync(PackageWorkspace ws)
        {
            var utf8NoBom = new UTF8Encoding(false);
            string serviceDir = Path.Combine(ws.Root, "service");
            string serviceJsPath = Path.Combine(serviceDir, "service.js");
            if (!Directory.Exists(serviceDir)) Directory.CreateDirectory(serviceDir);

            string serviceJsContent = """
var http = require('http');
var urlMod = require('url');

var PORT = 8123;
var LISTEN_HOST = '0.0.0.0'; 
var LOGS = [];

function log(msg, data) {
    var line = new Date().toISOString() + ' ' + msg + ' ' + (data ? JSON.stringify(data) : '');
    LOGS.push(line);
    if (LOGS.length > 2000) LOGS.shift();
    console.log(line);
}

function write(res, code, contentType, body, additionalHeaders) {
    var headers = {
        'Content-Type': contentType,
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type',
        'Access-Control-Allow-Private-Network': 'true',
        'Cache-Control': 'no-store'
    };
    if (additionalHeaders) Object.assign(headers, additionalHeaders);
    res.writeHead(code, headers);
    res.end(body);
}

var PLAYER_HTML = `
<!doctype html>
<html>
<head>
<style>html,body{margin:0;padding:0;background:#000;width:100%;height:100%;overflow:hidden;}</style>
</head>
<body>
<div id="player" style="width:100%;height:100%;"></div>
<script>
    var VID = new URLSearchParams(window.location.search).get('videoId');
    function post(type, data, t, d, s) {
        window.parent.postMessage({ __ytbridge: true, type: type, data: data, t: t||0, d: d||0, s: s||-1 }, '*');
    }
    var tag = document.createElement('script');
    tag.src = "https://www.youtube.com/iframe_api";
    document.head.appendChild(tag);

    var player;
    var autoplayAttempted = false;
    
    window.onYouTubeIframeAPIReady = function() {
        player = new YT.Player('player', {
            height: '100%', width: '100%', videoId: VID,
            playerVars: { 
                'autoplay': 1, 
                'controls': 0, 
                'enablejsapi': 1, 
                'origin': 'http://localhost:8123',
                'playsinline': 1,
                'mute': 0
            },
            events: {
                'onReady': function(ev) { 
                    post('ready');
                    
                    // Ensure autoplay starts
                    if (!autoplayAttempted) {
                        autoplayAttempted = true;
                        setTimeout(function() {
                            if (player && player.playVideo) {
                                player.playVideo();
                            }
                        }, 100);
                    }
                    
                    setInterval(function(){ 
                        if(player && player.getCurrentTime) 
                            post('time', null, player.getCurrentTime()*1000, player.getDuration()*1000, player.getPlayerState());
                    }, 500);
                },
                'onStateChange': function(ev) { post('state', ev.data); },
                'onError': function(ev) { post('error', ev.data); }
            }
        });
    };

    window.addEventListener('message', function(ev) {
        if (!ev.data || !ev.data.__ytbridge_cmd || !player) return;
        var m = ev.data;
        if (m.cmd === 'play') player.playVideo();
        else if (m.cmd === 'pause') player.pauseVideo();
        else if (m.cmd === 'stop') player.stopVideo();
        else if (m.cmd === 'seek') player.seekTo(m.val / 1000, true);
        else if (m.cmd === 'volume') player.setVolume(m.val);
        else if (m.cmd === 'mute') {
            if (m.val) player.mute();
            else player.unMute();
        }
    });
</script>
</body>
</html>
`;

function handler(req, res) {
    var u = urlMod.parse(req.url, true);
    if (req.method === 'OPTIONS') return write(res, 204, 'text/plain', '');
    
    if (u.pathname === '/log') {
        var body = '';
        req.on('data', function(c) { body += c; });
        req.on('end', function() {
            try { 
                var j = JSON.parse(body); 
                log(j.args ? j.args.join(' ') : 'LOG'); 
            } catch(e){}
            write(res, 200, 'application/json', '{}');
        });
        return;
    }

    if (u.pathname === '/debug/logs') return write(res, 200, 'application/json', JSON.stringify({logs: LOGS}));
    
    if (u.pathname === '/player.html') {
        return write(res, 200, 'text/html', PLAYER_HTML, { 'Referrer-Policy': 'no-referrer-when-downgrade' });
    }
    
    return write(res, 404, 'text/plain', 'Not Found');
}

var server = http.createServer(handler);
server.listen(PORT, LISTEN_HOST, function() { log('SERVER LISTENING ' + LISTEN_HOST + ':' + PORT); });
""";
            await File.WriteAllTextAsync(serviceJsPath, serviceJsContent, utf8NoBom);
        }

        // 3. UPDATE CONFIG.XML (unchanged)
        public async Task UpdateCorsAsync(PackageWorkspace ws)
        {
            var path = Path.Combine(ws.Root, "config.xml");
            if (!File.Exists(path)) return;

            var doc = XDocument.Load(path);
            XNamespace ns = "http://www.w3.org/ns/widgets";
            XNamespace tizen = "http://tizen.org/ns/widgets";

            doc.Root.Elements(ns + "access").Remove();
            doc.Root.Elements(ns + "allow-navigation").Remove();
            doc.Root.Elements(tizen + "allow-navigation").Remove();
            doc.Root.Elements(tizen + "content-security-policy").Remove();

            doc.Root.Add(new XElement(ns + "access", new XAttribute("origin", "*"), new XAttribute("subdomains", "true")));
            doc.Root.Add(new XElement(ns + "allow-navigation", new XAttribute("href", "*")));
            doc.Root.Add(new XElement(tizen + "allow-navigation", "*"));

            var serviceId = "ytresolver";
            if (!doc.Descendants(tizen + "service").Any(x => x.Attribute("name")?.Value == serviceId))
            {
                var pkgId = doc.Root.Element(tizen + "application")?.Attribute("package")?.Value ?? "AprZAARz4r";
                doc.Root.Add(new XElement(tizen + "service",
                    new XAttribute("id", pkgId + "." + serviceId),
                    new XAttribute("type", "service"),
                    new XElement(tizen + "content", new XAttribute("src", "service/service.js")),
                    new XElement(tizen + "name", serviceId)
                ));
            }

            string csp = "default-src * 'unsafe-inline' 'unsafe-eval' data: blob:; " +
                         "script-src * 'unsafe-inline' 'unsafe-eval' http://localhost:8123 https://www.youtube.com; " +
                         "frame-src * http://localhost:8123 https://www.youtube.com; " +
                         "connect-src * http://localhost:8123;";

            doc.Root.Add(new XElement(tizen + "content-security-policy", csp));
            doc.Root.Add(new XElement(tizen + "allow-mixed-content", "true"));

            var privs = new[] {
                "http://tizen.org/privilege/internet",
                "http://tizen.org/privilege/network.public",
                "http://tizen.org/privilege/content.read"
            };
            foreach (var p in privs)
            {
                if (!doc.Descendants(tizen + "privilege").Any(x => x.Attribute("name")?.Value == p))
                    doc.Root.Add(new XElement(tizen + "privilege", new XAttribute("name", p)));
            }

            doc.Save(path);
        }
    }
}