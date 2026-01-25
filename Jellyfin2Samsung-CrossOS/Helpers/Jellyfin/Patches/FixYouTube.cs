using Jellyfin2Samsung.Helpers.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin2Samsung.Helpers.Jellyfin.Fixes
{
    public class FixYouTube
    {
        public async Task PatchPluginAsync(PackageWorkspace ws)
        {
            var www = Path.Combine(ws.Root, "www");
            var utf8NoBom = new UTF8Encoding(false);

            foreach (var file in Directory.GetFiles(www, "youtubePlayer-plugin.*.js"))
            {
                var js = await File.ReadAllTextAsync(file);
                if (js.Contains("__NATIVE_STABLE_V1__"))
                    continue;

                // Jellyfin-facing YT.Player shim:
                // - Creates an <iframe> that points at the local service (/player.html?videoId=...)
                // - Bridges commands via postMessage
                // - Bridges events back into Jellyfin callbacks
                // - Keeps heavy logs for debugging
                string nativeCode = """
/* === TIZEN YT IFRAME BRIDGE (STABLE V2.9 - HEAVY LOG) === */
(function () {
    if (window.__NATIVE_STABLE_V1__) return;
    window.__NATIVE_STABLE_V1__ = true;

    var SERVICE_BASE = 'http://127.0.0.1:8123';

    function jfLog() {
        try { console.log('[YT-NATIVE]', Array.prototype.join.call(arguments, ' ')); } catch (e) {}
    }
    function sleep(ms){ return new Promise(function(r){ setTimeout(r, ms); }); }

    // --- start service once (launch is optimistic; we still wait /health) ---
    function startResolverServiceOnce() {
        if (window.__YTRESOLVER_START_PROMISE__) return window.__YTRESOLVER_START_PROMISE__;
        window.__YTRESOLVER_START_PROMISE__ = new Promise(function(resolve) {
            try {
                var pkg = tizen.application.getCurrentApplication().appInfo.packageId;
                var serviceId = pkg + '.ytresolver';
                jfLog('launch service', serviceId);

                tizen.application.launch(
                    serviceId,
                    function(){ jfLog('launch ok'); resolve({ ok:true, id: serviceId }); },
                    function(err){ jfLog('launch err', err && err.name, err && err.message); resolve({ ok:false, err: String((err && err.message) || err) }); }
                );
            } catch (e) {
                jfLog('launch exception', String(e));
                resolve({ ok:false, err: String(e) });
            }
        });
        return window.__YTRESOLVER_START_PROMISE__;
    }

    async function waitForHealth(timeoutMs) {
        var start = Date.now();
        while (Date.now() - start < timeoutMs) {
            try {
                var r = await fetch(SERVICE_BASE + '/health', { method:'GET', cache:'no-store' });
                if (r && r.ok) {
                    var t = await r.text().catch(function(){ return ''; });
                    jfLog('health ok', t && t.slice ? t.slice(0, 120) : '');
                    return true;
                }
            } catch (e) {}
            await sleep(150);
        }
        return false;
    }

    async function dumpServiceLogs(tag) {
        try {
            var r = await fetch(SERVICE_BASE + '/debug/logs?tail=80', { method:'GET', cache:'no-store' });
            var j = await r.json();
            var lines = (j && j.lines) ? j.lines : [];
            jfLog('SERVICE LOG TAIL', tag || '', '\n' + lines.join('\n'));
        } catch (e) {
            jfLog('dumpServiceLogs failed', String(e && e.message ? e.message : e));
        }
    }

    // Ensure YT namespace & constants exist for Jellyfin
    window.YT = window.YT || {};
    window.YT.PlayerState = window.YT.PlayerState || {
        UNSTARTED: -1,
        ENDED: 0,
        PLAYING: 1,
        PAUSED: 2,
        BUFFERING: 3,
        CUED: 5
    };

    // --- YT.Player override ---
    window.YT.Player = function (idOrElement, config) {
        jfLog('YT.Player ctor', idOrElement, config && config.videoId);

        var self = this;

        // Jellyfin sometimes passes element instead of string id
        var container = (typeof idOrElement === 'string') ? document.getElementById(idOrElement) : idOrElement;
        if (!container) {
            jfLog('ERROR: container not found');
            return;
        }

        var destroyed = false;
        var backHandled = false;

        // State cache for Jellyfin getters
        var lastState = window.YT.PlayerState.UNSTARTED;
        var lastTime = 0;
        var lastDur = 0;
        var lastVol = 100;
        var lastMuted = false;

        function emitState(st) {
            lastState = st;
            if (config && config.events && config.events.onStateChange) {
                try { config.events.onStateChange({ data: st }); } catch (e) {}
            }
        }

        // Build iframe
        var iframe = document.createElement('iframe');
        iframe.style.cssText = 'width:100%;height:100%;border:0;position:absolute;top:0;left:0;background:#000;z-index:2147483647;';
        iframe.setAttribute('allow', 'autoplay; encrypted-media');
        iframe.setAttribute('allowfullscreen', 'false');

        // Ensure container can host absolute iframe
        try { container.style.position = 'relative'; } catch(e){}
        container.innerHTML = '';
        container.appendChild(iframe);

        // Heavy iframe element logging
        iframe.addEventListener('load', function(){ jfLog('iframe load', iframe.src); });
        iframe.addEventListener('error', function(){ jfLog('iframe error', iframe.src); });

        // Bridge messages from iframe -> Jellyfin
        function onMsg(ev) {
            var m = ev && ev.data;
            if (!m || !m.__ytbridge) return;

            // noisy but useful:
            jfLog('msg', m.type, safeMini(m.data));

            if (m.type === 'ready') {
                if (config && config.events && config.events.onReady) {
                    try { config.events.onReady({ target: self }); } catch (e) {}
                }
            } else if (m.type === 'state') {
                var st = m.data && typeof m.data.data === 'number' ? m.data.data : null;
                if (st !== null) emitState(st);
            } else if (m.type === 'time') {
                try {
                    lastTime = (m.data && m.data.t) ? (m.data.t / 1000) : lastTime;
                    lastDur  = (m.data && m.data.d) ? (m.data.d / 1000) : lastDur;
                    if (typeof m.data.v === 'number') lastVol = m.data.v;
                    if (typeof m.data.m === 'boolean') lastMuted = m.data.m;
                    if (typeof m.data.s === 'number') lastState = m.data.s;
                } catch(e) {}
            } else if (m.type === 'error') {
                jfLog('iframe player error', safeMini(m.data));
            }
        }

        function safeMini(x){
            try {
                var s = JSON.stringify(x);
                return s && s.length > 180 ? (s.slice(0, 180) + '...') : s;
            } catch(e){ return String(x); }
        }

        window.addEventListener('message', onMsg, false);

        function postCmd(cmd, val) {
            try {
                if (!iframe.contentWindow) return;
                iframe.contentWindow.postMessage({ __ytbridge_cmd: true, cmd: cmd, val: val }, '*');
            } catch (e) {
                jfLog('postCmd error', cmd, String(e));
            }
        }

        // Jellyfin-required API surface
        this.setSize = function () {};
        this.playVideo = function () { jfLog('playVideo'); postCmd('play'); };
        this.pauseVideo = function () { jfLog('pauseVideo'); postCmd('pause'); emitState(window.YT.PlayerState.PAUSED); };
        this.stopVideo = function () { jfLog('stopVideo'); postCmd('stop'); emitState(window.YT.PlayerState.ENDED); };
        this.seekTo = function (s) { jfLog('seekTo', s); postCmd('seek', Math.floor((s || 0) * 1000)); };
        this.getCurrentTime = function () { return lastTime || 0; };
        this.getDuration = function () { return lastDur || 0; };
        this.getPlayerState = function () { return lastState; };

        this.setVolume = function (val) { jfLog('setVolume', val); lastVol = val; postCmd('setVolume', val); };
        this.getVolume = function () { return lastVol; };
        this.mute = function () { jfLog('mute'); lastMuted = true; postCmd('mute'); };
        this.unMute = function () { jfLog('unMute'); lastMuted = false; postCmd('unMute'); };
        this.isMuted = function () { return !!lastMuted; };

        // Optional Jellyfin calls (helps some builds)
        this.loadVideoById = function (vid) {
            jfLog('loadVideoById', vid);
            setVideoId(String(vid || ''));
        };
        this.cueVideoById = function (vid) {
            jfLog('cueVideoById', vid);
            setVideoId(String(vid || ''));
        };

        this.destroy = function () {
            if (destroyed) return;
            destroyed = true;
            jfLog('destroy');

            try { window.removeEventListener('message', onMsg, false); } catch(e){}
            try { window.removeEventListener('keydown', onKeyDown, true); } catch(e){}
            try { postCmd('destroy'); } catch(e){}

            try { iframe.remove(); } catch(e){}
        };

        // BACK key: PAUSE ONLY (delegate stop to Jellyfin)
        function onKeyDown(e) {
            if (backHandled || destroyed) return;
            if (e && (e.keyCode === 10009 || e.key === 'Back')) {
                backHandled = true;
                jfLog('BACK key (pause only)');
                try { if (e.preventDefault) e.preventDefault(); } catch (e1) {}
                try { if (e.stopPropagation) e.stopPropagation(); } catch (e2) {}
                emitState(window.YT.PlayerState.PAUSED);
                try { window.removeEventListener('keydown', onKeyDown, true); } catch (e3) {}
            }
        }
        window.addEventListener('keydown', onKeyDown, true);

        function setVideoId(videoId) {
            var src = SERVICE_BASE + '/player.html?videoId=' + encodeURIComponent(videoId || '');
            jfLog('iframe src set', src);
            iframe.src = src;
        }

        // Kick everything off
        (async function(){
            var vid = (config && config.videoId) ? String(config.videoId) : '';
            jfLog('init videoId', vid);

            await startResolverServiceOnce();
            var ok = await waitForHealth(15000);
            jfLog('service health', ok ? 'OK' : 'FAILED');

            if (!ok) {
                await dumpServiceLogs('health-fail');
                throw new Error('service not reachable on ' + SERVICE_BASE);
            }

            // set iframe src only after health ok (helps race conditions)
            setVideoId(vid);

            // If Jellyfin is waiting on onYouTubeIframeAPIReady, we can safely poke it once it exists.
            // (guarded; won’t spam)
            if (!window.__YTBRIDGE_SIGNALED__) {
                window.__YTBRIDGE_SIGNALED__ = true;
                var tries = 0;
                var t = setInterval(function(){
                    tries++;
                    if (typeof window.onYouTubeIframeAPIReady === 'function') {
                        jfLog('calling onYouTubeIframeAPIReady (compat)');
                        try { window.onYouTubeIframeAPIReady(); } catch(e){}
                        clearInterval(t);
                    }
                    if (tries > 50) clearInterval(t);
                }, 200);
            }
        })().catch(function(err){
            jfLog('init error', String(err && err.message ? err.message : err));
            dumpServiceLogs('init-error');
        });
    };

    jfLog('YT bridge installed');
})();
""";

                await File.WriteAllTextAsync(file, nativeCode + "\n" + js, utf8NoBom);
            }
        }
    }

    public class YouTubeWebService
    {
        public async Task UpdateCorsAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "config.xml");

            XDocument doc;
            using (var stream = File.OpenRead(path))
            {
                doc = XDocument.Load(stream);
            }

            XNamespace ns = "http://www.w3.org/ns/widgets";
            XNamespace tizenNs = "http://tizen.org/ns/widgets";

            // Ensure required_version is modern
            var appTag = doc.Root.Element(tizenNs + "application");
            if (appTag != null)
                appTag.SetAttributeValue("required_version", "8.0");

            // Remove old service tags to prevent duplicates
            doc.Root.Elements(tizenNs + "service").Remove();

            // Derive packageId from <tizen:application package="..."> (fallback to known)
            var packageId = (string)appTag?.Attribute("package") ?? "AprZAARz4r";

            // Tizen docs pattern: service id = packageId + ".Something"
            var serviceId = packageId + ".ytresolver";

            // ✅ Correct service definition per Tizen Web Service docs:
            // type="ui" and content src points to JS file, not folder
            var serviceElement = new XElement(tizenNs + "service",
                new XAttribute("id", serviceId),
                new XAttribute("type", "service"),
                new XElement(tizenNs + "content", new XAttribute("src", "service/service.js")),
                new XElement(tizenNs + "name", "ytresolver"),
                new XElement(tizenNs + "description", "YouTube Stream Resolver Service")
            );

            // Capture elements before rebuilding
            var allElements = doc.Root.Elements().ToList();
            doc.Root.RemoveNodes();

            // Pull W3C features
            var w3cFeatures = allElements.Where(e => e.Name == ns + "feature").ToList();

            // Pull existing tizen:feature elements (keep any that were already there)
            var tizenFeatures = allElements.Where(e => e.Name == tizenNs + "feature").ToList();

            // ✅ Ensure <tizen:feature name="http://tizen.org/feature/web.service"/> exists
            if (!tizenFeatures.Any(e => (string)e.Attribute("name") == "http://tizen.org/feature/web.service"))
            {
                tizenFeatures.Add(new XElement(tizenNs + "feature",
                    new XAttribute("name", "http://tizen.org/feature/web.service")));
            }

            // Rebuild XML in a stable order (your “strict order” approach)
            doc.Root.Add(allElements.Where(e => e.Name == ns + "name"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "description"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "author"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "icon"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "content"));

            // W3C feature/access/navigation first
            doc.Root.Add(w3cFeatures);
            doc.Root.Add(new XElement(ns + "access",
                new XAttribute("origin", "*"),
                new XAttribute("subdomains", "true")));
            doc.Root.Add(new XElement(ns + "allow-navigation",
                new XAttribute("href", "*")));

            // Then Tizen feature + application + service
            doc.Root.Add(tizenFeatures);
            if (appTag != null) doc.Root.Add(appTag);
            doc.Root.Add(serviceElement);

            // Then remaining tizen elements
            doc.Root.Add(allElements.Where(e => e.Name == tizenNs + "metadata"));
            doc.Root.Add(allElements.Where(e => e.Name == tizenNs + "profile"));
            doc.Root.Add(allElements.Where(e => e.Name == tizenNs + "setting"));

            // Privileges (optional for service; still OK to keep)
            string[] privileges = {
        "http://tizen.org/privilege/internet",
        "http://tizen.org/privilege/filesystem.read",
        "http://tizen.org/privilege/filesystem.write"
    };
            foreach (var priv in privileges)
                doc.Root.Add(new XElement(tizenNs + "privilege", new XAttribute("name", priv)));

            // Save without BOM
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = "\n"
            };

            using (var writer = XmlWriter.Create(path, settings))
            {
                doc.Save(writer);
            }
        }

        public async Task CreateYouTubeResolverAsync(PackageWorkspace ws)
        {
            string serviceDir = Path.Combine(ws.Root, "service");
            string serviceJsPath = Path.Combine(serviceDir, "service.js");

            if (!Directory.Exists(serviceDir))
                Directory.CreateDirectory(serviceDir);

            // Node service hosts /player.html + /player.js (YouTube iframe API) from an HTTP origin.
            // Heavy logging: ring buffer + request log + /debug/* endpoints + iframe -> service log sink (/log).
            // IMPORTANT: bind 0.0.0.0 so you can browse debug endpoints from your laptop via TV LAN IP.
            string serviceJsContent = """
var http = require('http');
var urlMod = require('url');

var PORT = 8123;
var LISTEN_HOST = '0.0.0.0';

var server = null;

// --- heavy log ring buffer ---
var LOGS = [];
var MAX_LOGS = 8000;

function safeJson(x) {
  try { return JSON.stringify(x); } catch (e) { try { return String(x); } catch (e2) { return '[unstringifiable]'; } }
}

function log() {
  try {
    var parts = [];
    for (var i = 0; i < arguments.length; i++) {
      var a = arguments[i];
      if (typeof a === 'string') parts.push(a);
      else parts.push(safeJson(a));
    }
    var line = (new Date()).toISOString() + ' ' + parts.join(' ');
    LOGS.push(line);
    if (LOGS.length > MAX_LOGS) LOGS.shift();
    try { console.log(line); } catch (e2) {}
  } catch (e3) {}
}

process.on('uncaughtException', function(e) { log('[uncaughtException]', (e && e.stack) || String(e)); });
process.on('unhandledRejection', function(e) { log('[unhandledRejection]', (e && e.stack) || String(e)); });

// --- last snapshot for /debug/state ---
var LAST = {
  startedAt: null,
  lastHttp: null,
  lastPlayerHtml: null,
  lastPlayerJs: null,
  lastLogPost: null
};

function corsHeaders() {
  return {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET,HEAD,POST,OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type',
    'Access-Control-Expose-Headers': 'Content-Length, Content-Range, Accept-Ranges, Content-Type',
    'Cache-Control': 'no-store'
  };
}

function write(res, code, headers, body) {
  var h = headers || {};
  var c = corsHeaders();
  for (var k in c) h[k] = c[k];
  res.writeHead(code, h);
  res.end(body || '');
}

function writeJson(res, code, obj) {
  write(res, code, { 'Content-Type': 'application/json; charset=utf-8' }, JSON.stringify(obj || {}));
}

function readBody(req) {
  return new Promise(function(resolve, reject) {
    var body = '';
    req.on('data', function(c) {
      body += c;
      if (body.length > 1024 * 1024) reject(new Error('payload too large'));
    });
    req.on('end', function() { resolve(body); });
    req.on('error', reject);
  });
}

// --- player assets ---
function playerHtml(videoId) {
  return '<!doctype html>' +
    '<html><head>' +
    '<meta charset="utf-8" />' +
    '<meta name="viewport" content="width=device-width,initial-scale=1" />' +
    '<style>html,body{margin:0;padding:0;background:#000;width:100%;height:100%;overflow:hidden;}#player{width:100%;height:100%;}</style>' +
    '</head><body>' +
    '<div id="player"></div>' +
    '<script>window.__VIDEO_ID__=' + JSON.stringify((videoId || '').toString()) + ';</script>' +
    '<script src="/player.js"></script>' +
    '</body></html>';
}

// IMPORTANT: PLAYER_JS is a STRING (not a function). Served verbatim.
var PLAYER_JS = (function(){/*
(function () {
  var ORIGIN = location.origin;
  var videoId = (window.__VIDEO_ID__ || '').toString();

  function postLog(level, args) {
    try {
      var payload = { t: (new Date()).toISOString(), level: level, args: args || [] };
      var xhr = new XMLHttpRequest();
      xhr.open('POST', ORIGIN + '/log', true);
      xhr.setRequestHeader('Content-Type', 'application/json');
      xhr.send(JSON.stringify(payload));
    } catch (e) {}
  }

  function ilog() { postLog('log', ['[IFRAME]'].concat([].slice.call(arguments))); }
  function ierr() { postLog('err', ['[IFRAME]'].concat([].slice.call(arguments))); }

  function postParent(type, data) {
    try {
      window.parent.postMessage({ __ytbridge: true, type: type, data: data || null }, '*');
    } catch (e) {}
  }

  ilog('boot', 'origin=', ORIGIN, 'videoId=', videoId, 'ua=', navigator.userAgent);

  function loadYT() {
    return new Promise(function(resolve) {
      if (window.YT && window.YT.Player) return resolve();
      var tag = document.createElement('script');
      tag.src = 'https://www.youtube.com/iframe_api';
      tag.onerror = function(){ ierr('iframe_api load error'); };
      var first = document.getElementsByTagName('script')[0];
      first.parentNode.insertBefore(tag, first);

      window.onYouTubeIframeAPIReady = function () {
        ilog('YT iframe api ready');
        resolve();
      };
    });
  }

  var player = null;
  var lastState = -1;
  var lastTimeMs = 0;
  var lastDurMs = 0;
  var lastVol = 100;
  var lastMuted = false;

  function startPlayer() {
    ilog('startPlayer', videoId);

    player = new YT.Player('player', {
      height: '100%',
      width: '100%',
      videoId: videoId,
      events: {
        onReady: function () {
          ilog('onReady');
          postParent('ready', {});
          try { player.playVideo(); } catch (e) { ierr('autoplay error', String(e)); }
        },
        onStateChange: function (ev) {
          lastState = ev.data;
          ilog('onStateChange', ev.data);
          postParent('state', { data: ev.data });
        },
        onError: function (ev) {
          ierr('onError', ev && ev.data);
          postParent('error', { code: ev && ev.data });
        }
      },
      playerVars: {
        controls: 0,
        enablejsapi: 1,
        modestbranding: 1,
        rel: 0,
        showinfo: 0,
        fs: 0,
        playsinline: 1
      }
    });

    // telemetry loop
    setInterval(function(){
      try {
        if (!player) return;

        var t = 0;
        var d = 0;

        try { t = player.getCurrentTime ? (player.getCurrentTime() || 0) : 0; } catch(e1){}
        try { d = player.getDuration ? (player.getDuration() || 0) : 0; } catch(e2){}
        lastTimeMs = Math.floor(t * 1000);
        lastDurMs  = Math.floor(d * 1000);

        try { lastVol = player.getVolume ? (player.getVolume() || lastVol) : lastVol; } catch(e3){}
        try { lastMuted = player.isMuted ? !!player.isMuted() : lastMuted; } catch(e4){}

        postParent('time', { t: lastTimeMs, d: lastDurMs, s: lastState, v: lastVol, m: lastMuted });
      } catch (e) {}
    }, 500);
  }

  // command handler from parent
  window.addEventListener('message', function (ev) {
    var m = ev && ev.data;
    if (!m || !m.__ytbridge_cmd) return;

    var cmd = m.cmd;
    var val = m.val;

    ilog('cmd', cmd, val);

    try {
      if (!player) return;

      if (cmd === 'play') player.playVideo();
      else if (cmd === 'pause') player.pauseVideo();
      else if (cmd === 'stop') player.stopVideo();
      else if (cmd === 'seek') player.seekTo((val || 0) / 1000, true);
      else if (cmd === 'setVolume') player.setVolume(val);
      else if (cmd === 'mute') player.mute();
      else if (cmd === 'unMute') player.unMute();
      else if (cmd === 'load') {
        // val = videoId
        try { player.loadVideoById(String(val || '')); } catch(e5){ ierr('loadVideoById error', String(e5)); }
      }
      else if (cmd === 'destroy') { try { player.destroy(); } catch(e6){} player = null; }
    } catch (e2) {
      ierr('cmd error', cmd, String(e2));
    }
  }, false);

  loadYT().then(startPlayer).catch(function(e){
    ierr('boot error', String(e && e.message ? e.message : e));
    postParent('error', { code: 'BOOT', detail: String(e) });
  });
})();
*/}).toString().split('/*')[1].split('*/')[0];

function handler(req, res) {
  var u = urlMod.parse(req.url, true);
  LAST.lastHttp = { at: (new Date()).toISOString(), method: req.method, path: u.pathname };

  log('[http]', req.method, u.pathname);

  if (req.method === 'OPTIONS') return write(res, 204, {}, '');

  if (req.method === 'GET' && u.pathname === '/health') {
    return writeJson(res, 200, { ok: true, pid: process.pid, startedAt: LAST.startedAt });
  }

  if (req.method === 'GET' && u.pathname === '/debug/logs') {
    var tail = 0;
    try { tail = parseInt(u.query && u.query.tail, 10) || 0; } catch(e) { tail = 0; }
    var lines = LOGS;
    if (tail > 0 && tail < lines.length) lines = lines.slice(lines.length - tail);
    return writeJson(res, 200, { ok: true, lines: lines });
  }

  if (req.method === 'GET' && u.pathname === '/debug/state') {
    return writeJson(res, 200, { ok: true, state: LAST, logCount: LOGS.length });
  }

  if (req.method === 'POST' && u.pathname === '/log') {
    return readBody(req).then(function(body){
      LAST.lastLogPost = { at: (new Date()).toISOString(), bytes: (body || '').length };
      try {
        var j = JSON.parse(body || '{}');
        var lvl = (j.level === 'err') ? '[IFRAME:ERR]' : '[IFRAME]';
        log(lvl, j.args || []);
      } catch (e) {
        log('[log] bad json', String(e));
      }
      return writeJson(res, 200, { ok: true });
    }).catch(function(e){
      log('[log] error', String(e));
      return writeJson(res, 500, { ok:false });
    });
  }

  if (req.method === 'GET' && u.pathname === '/player.js') {
    LAST.lastPlayerJs = { at: (new Date()).toISOString() };
    log('[player.js] served bytes=', (PLAYER_JS && PLAYER_JS.length) || 0);
    return write(res, 200, { 'Content-Type': 'application/javascript; charset=utf-8' }, PLAYER_JS);
  }

  if (req.method === 'GET' && u.pathname === '/player.html') {
    var vid = (u.query && (u.query.videoId || u.query.v)) ? String(u.query.videoId || u.query.v) : '';
    LAST.lastPlayerHtml = { at: (new Date()).toISOString(), videoId: vid };
    log('[player.html] request videoId=', vid);
    return write(res, 200, { 'Content-Type': 'text/html; charset=utf-8' }, playerHtml(vid));
  }

  return writeJson(res, 404, { error: 'not found' });
}

module.exports.onStart = function() {
  if (server) {
    log('[ytresolver] onStart (already running) pid=', process.pid);
    return;
  }

  LAST.startedAt = (new Date()).toISOString();
  log('[ytresolver] onStart pid=', process.pid);

  server = http.createServer(handler);
  server.listen(PORT, LISTEN_HOST, function() {
    log('[ytresolver] listening', LISTEN_HOST, PORT);
  });
};

module.exports.onStop = function() {
  log('[ytresolver] onStop pid=', process.pid);
  if (!server) return;

  try {
    server.close(function(){ log('[ytresolver] stopped'); });
  } finally {
    server = null;
  }
};
""";

            var utf8NoBom = new UTF8Encoding(false);
            await File.WriteAllTextAsync(serviceJsPath, serviceJsContent, utf8NoBom);
        }
    }
}