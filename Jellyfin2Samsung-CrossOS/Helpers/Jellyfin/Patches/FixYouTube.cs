using Jellyfin2Samsung.Helpers.Core;
using System;
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

            var candidates = Directory.GetFiles(www, "youtubePlayer-plugin*.js", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(www, "youtubePlayer-plugin*.chunk.js", SearchOption.AllDirectories))
                .Distinct()
                .ToList();

            string injected = """
(function () {
  if (window.__YT_FIX_V8__) return;
  window.__YT_FIX_V8__ = true;

  var SERVICE_BASE = 'http://127.0.0.1:8123';
  
  function sLog(msg, data) {
    try {
      var body = JSON.stringify({args: ['[V8-BRIDGE]', msg, data || '']});
      var xhr = new XMLHttpRequest();
      xhr.open('POST', SERVICE_BASE + '/log', true);
      xhr.send(body);
    } catch (e) {}
  }

  // 1. Launch the background service immediately
  try {
    var appId = tizen.application.getCurrentApplication().appInfo.id;
    var pkgId = appId.split('.')[0];
    tizen.application.launch(pkgId + '.ytresolver', function() {
      sLog('Service launch requested');
    });
  } catch (e) {
    sLog('Service launch failed', e.message);
  }

  // 2. Mock YT API for Jellyfin
  window.YT = window.YT || {};
  window.YT.PlayerState = { UNSTARTED: -1, ENDED: 0, PLAYING: 1, PAUSED: 2, BUFFERING: 3, CUED: 5 };

  window.YT.Player = function (elementId, options) {
    sLog('Player created for', options.videoId);
    var self = this;
    this._options = options;
    this._ready = false;
    this._queue = [];
    this._state = -1;
    this._currentTime = 0;
    this._duration = 0;

    var container = typeof elementId === 'string' ? document.getElementById(elementId) : elementId;
    var ifr = document.createElement('iframe');
    ifr.id = 'yt_bridge_ifr';
    ifr.src = SERVICE_BASE + '/player.html?videoId=' + options.videoId;
    ifr.style.width = '100%';
    ifr.style.height = '100%';
    ifr.style.border = 'none';
    
    container.innerHTML = '';
    container.appendChild(ifr);

    window.addEventListener('message', function (e) {
      if (e.origin !== SERVICE_BASE) return;
      var msg = e.data;
      if (msg.type === 'ready') {
        self._ready = true;
        sLog('Iframe Ready');
        if (options.events && options.events.onReady) options.events.onReady({ target: self });
        while (self._queue.length) {
          var cmd = self._queue.shift();
          self[cmd.method].apply(self, cmd.args);
        }
      } else if (msg.type === 'state') {
        self._state = msg.state;
        self._currentTime = msg.time;
        self._duration = msg.duration;
        if (options.events && options.events.onStateChange) {
            options.events.onStateChange({ target: self, data: msg.state });
        }
      }
    });

    this.playVideo = function () { this._send('playVideo'); };
    this.pauseVideo = function () { this._send('pauseVideo'); };
    this.stopVideo = function () { this._send('stopVideo'); };
    this.seekTo = function (s) { this._send('seekTo', [s]); };
    this.setVolume = function (v) { this._send('setVolume', [v]); };
    this.getVolume = function () { return 100; };
    this.getCurrentTime = function () { return this._currentTime; };
    this.getDuration = function () { return this._duration; };
    this.getPlayerState = function () { return this._state; };

    this._send = function (method, args) {
      if (!this._ready) {
        this._queue.push({ method: method, args: args });
        return;
      }
      ifr.contentWindow.postMessage({ method: method, args: args }, '*');
    };
  };

  // Trigger Jellyfin's initialization
  setTimeout(function() {
    if (window.onYouTubeIframeAPIReady) window.onYouTubeIframeAPIReady();
  }, 100);
})();

""";

            foreach (var file in candidates)
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains("__YT_FIX_V8__")) continue;

                // Prepend fix to the bundle
                var newContent = injected + "\n" + content;
                await File.WriteAllTextAsync(file, newContent, utf8NoBom);
            }

        }

    }

    public class YouTubeWebService
    {
        public async Task UpdateCorsAsync(PackageWorkspace ws)
        {
            string path = Path.Combine(ws.Root, "config.xml");
            XDocument doc;
            using (var stream = File.OpenRead(path)) doc = XDocument.Load(stream);

            XNamespace ns = "http://www.w3.org/ns/widgets";
            XNamespace tizenNs = "http://tizen.org/ns/widgets";

            var appTag = doc.Root.Element(tizenNs + "application");
            var packageId = (string)appTag?.Attribute("package") ?? "AprZAARz4r";

            // 1. Clean old services
            doc.Root.Elements(tizenNs + "service").Remove();

            // 2. Define Service (UI type is sometimes more permissible for network)
            var serviceElement = new XElement(tizenNs + "service",
                new XAttribute("id", packageId + ".ytresolver"),
                new XAttribute("type", "service"),
                new XElement(tizenNs + "content", new XAttribute("src", "service/service.js")),
                new XElement(tizenNs + "name", "ytresolver")
            );

            // 3. Clear and Rebuild Root to control order
            var allElements = doc.Root.Elements().ToList();
            doc.Root.RemoveNodes();

            // 4. Set Features
            var tizenFeatures = allElements.Where(e => e.Name == tizenNs + "feature").ToList();
            if (!tizenFeatures.Any(e => (string)e.Attribute("name") == "http://tizen.org/feature/web.service"))
                tizenFeatures.Add(new XElement(tizenNs + "feature", new XAttribute("name", "http://tizen.org/feature/web.service")));

            doc.Root.Add(allElements.Where(e => e.Name == ns + "name"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "description"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "icon"));
            doc.Root.Add(allElements.Where(e => e.Name == ns + "content")); // Main app content

            // 5. SECURITY POLICIES (Crucial for file:// -> http://127.0.0.1)

            // Allow navigation to local service
            doc.Root.Add(new XElement(ns + "access", new XAttribute("origin", "*"), new XAttribute("subdomains", "true")));
            doc.Root.Add(new XElement(ns + "allow-navigation", new XAttribute("href", "*")));

            doc.Root.Add(new XElement(ns + "access", new XAttribute("origin", "http://127.0.0.1:8123"), new XAttribute("subdomains", "true")));
            doc.Root.Add(new XElement(ns + "allow-navigation", new XAttribute("href", "http://127.0.0.1:8123")));

            // Allow Mixed Content (file:// loading http://) - Not all Tizen versions support this tag, but it helps where supported
            doc.Root.Add(new XElement(tizenNs + "allow-mixed-content", "true"));

            // CSP: Explicitly allow connection to localhost port 8123 from file://
            // 'filesystem' and 'file' sources added
            string csp = "default-src * 'unsafe-inline' 'unsafe-eval' data: blob: filesystem: file:; " +
                         "script-src * 'unsafe-inline' 'unsafe-eval' http://127.0.0.1:8123; " +
                         "frame-src * http://127.0.0.1:8123; " +
                         "connect-src * http://127.0.0.1:8123;";

            doc.Root.Add(new XElement(tizenNs + "content-security-policy", csp));
            doc.Root.Add(new XElement(tizenNs + "content-security-policy-report-only", csp));

            doc.Root.Add(tizenFeatures);
            if (appTag != null) doc.Root.Add(appTag);
            doc.Root.Add(serviceElement);

            // 6. Privileges
            string[] privileges = {
                "http://tizen.org/privilege/internet",
                "http://tizen.org/privilege/filesystem.read",
                "http://tizen.org/privilege/filesystem.write",
                "http://tizen.org/privilege/network.public", // Often needed for local network access
                "http://tizen.org/privilege/network.profile"
            };
            foreach (var priv in privileges)
                doc.Root.Add(new XElement(tizenNs + "privilege", new XAttribute("name", priv)));

            // Save
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
            using (var writer = XmlWriter.Create(path, settings)) doc.Save(writer);
        }
        public async Task CreateYouTubeResolverAsync(PackageWorkspace ws)
        {
            string serviceDir = Path.Combine(ws.Root, "service");
            string serviceJsPath = Path.Combine(serviceDir, "service.js");

            if (!Directory.Exists(serviceDir))
                Directory.CreateDirectory(serviceDir);

            string serviceJsContent = """
var http = require('http');
var urlMod = require('url');

var PORT = 8123;
var LISTEN_HOST = '0.0.0.0';

var server = null;

// Plain text logs
var LOGS = [];
var MAX_LOGS = 12000;

// Structured events (for debugging without parsing text)
var EVENTS = [];
var MAX_EVENTS = 6000;

function safeJson(x) {
  try { return JSON.stringify(x); } catch (e) { try { return String(x); } catch (e2) { return '[unstringifiable]'; } }
}

function log() {
  try {
    var parts = [];
    for (var i = 0; i < arguments.length; i++) parts.push(String(arguments[i]));
    var line = new Date().toISOString() + ' ' + parts.join(' ');
    LOGS.push(line);
    if (LOGS.length > MAX_LOGS) LOGS.splice(0, LOGS.length - MAX_LOGS);
    try { console.log(line); } catch (e) {}
  } catch (e) {}
}

function pushEvent(kind, payload) {
  try {
    var evt = {
      ts: new Date().toISOString(),
      kind: String(kind || 'event'),
      payload: payload || {}
    };
    EVENTS.push(evt);
    if (EVENTS.length > MAX_EVENTS) EVENTS.splice(0, EVENTS.length - MAX_EVENTS);
    log('[evt]', evt.kind, safeJson(evt.payload).slice(0, 300));
  } catch (e) {}
}

function write(res, code, headers, body) {
  try {
    res.writeHead(code, headers || {});
    res.end(body);
  } catch (e) {
    try { res.end(); } catch (e2) {}
  }
}

function writeJson(res, code, obj) {
  write(res, code, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store'
  }, safeJson(obj));
}

function readBody(req, cb) {
  try {
    var chunks = [];
    req.on('data', function(d){ chunks.push(d); });
    req.on('end', function(){
      var b = '';
      try { b = Buffer.concat(chunks).toString('utf8'); } catch (e) {}
      cb(b);
    });
    req.on('error', function(){ cb(''); });
  } catch (e) { cb(''); }
}

function reqMeta(req) {
  try {
    return {
      ua: req.headers && req.headers['user-agent'],
      origin: req.headers && req.headers['origin'],
      referer: req.headers && (req.headers['referer'] || req.headers['referrer']),
      host: req.headers && req.headers['host'],
      accept: req.headers && req.headers['accept'],
      fetchDest: req.headers && req.headers['sec-fetch-dest'],
      fetchMode: req.headers && req.headers['sec-fetch-mode'],
      fetchSite: req.headers && req.headers['sec-fetch-site']
    };
  } catch (e) { return {}; }
}

function playerHtml(videoId) {
  var vid = (videoId || '').toString().replace(/[^a-zA-Z0-9_-]/g, '');
  var origin = 'http://127.0.0.1:' + PORT;

  return '<!doctype html>' +
    '<html><head>' +
      '<meta charset="utf-8" />' +
      '<meta name="viewport" content="width=device-width,initial-scale=1" />' +
      '<meta name="referrer" content="strict-origin-when-cross-origin" />' +
      '<style>html,body{margin:0;padding:0;background:#000;width:100%;height:100%;overflow:hidden;}#player{width:100%;height:100%;}</style>' +
    '</head><body>' +
      '<div id="player"></div>' +
      '<script>window.__VIDEO_ID__=' + JSON.stringify(vid) + ';window.__ORIGIN__=' + JSON.stringify(origin) + ';</script>' +
      // log early (even before player.js)
      '<script>(function(){try{fetch("/log",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({src:"player.html",msg:"loaded",data:{videoId:' + JSON.stringify(vid) + ',origin:' + JSON.stringify(origin) + ',href:location.href}})}).catch(function(){})}catch(e){}})();</script>' +
      '<script src="/player.js"></script>' +
    '</body></html>';
}

// served as /player.js (heavy logger, no logic change)
var PLAYER_JS = (function(){/*
(function(){
  var vid = (window.__VIDEO_ID__ || '').toString();
  var ORIGIN = (window.__ORIGIN__ || 'http://127.0.0.1:8123').toString();

  function sLog(msg, data) {
    try {
      fetch('/log', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ src: 'player.js', msg: msg, data: data || {} })
      }).catch(function(){});
    } catch(e){}
  }

  function send(type, data) {
    try { parent && parent.postMessage({ type: type, data: data || {} }, '*'); } catch(e){}
  }

  window.addEventListener('error', function(ev){
    sLog('window.error', {
      message: String(ev && ev.message || ev),
      filename: ev && ev.filename,
      lineno: ev && ev.lineno,
      colno: ev && ev.colno
    });
  });
  window.addEventListener('unhandledrejection', function(ev){
    sLog('unhandledrejection', { reason: String(ev && ev.reason || ev) });
  });

  // Accept basic commands from parent (Jellyfin overlay)
  window.addEventListener('message', function(ev){
    var m = ev && ev.data;
    if (!m || !m.type) return;

    sLog('cmd.recv', { type: m.type, data: m.data });

    if (!window.__ytPlayer__) {
      sLog('cmd.no_player_yet', { type: m.type });
      return;
    }

    try {
      if (m.type === 'cmd.play') window.__ytPlayer__.playVideo();
      else if (m.type === 'cmd.pause') window.__ytPlayer__.pauseVideo();
      else if (m.type === 'cmd.stop' || m.type === 'cmd.close') window.__ytPlayer__.stopVideo();
      else if (m.type === 'cmd.seek') window.__ytPlayer__.seekTo(Number(m.data)||0, true);
    } catch(e){
      sLog('cmd.error', { type: m.type, err: String(e) });
    }
  }, false);

  function loadIframeApi(cb){
    sLog('iframe_api.start', { hasYT: !!(window.YT && window.YT.Player), videoId: vid, origin: ORIGIN });

    if (window.YT && window.YT.Player) {
      sLog('iframe_api.already_loaded', {});
      return cb();
    }

    var s = document.createElement('script');
    s.src = 'https://www.youtube.com/iframe_api';
    s.async = true;

    s.onload = function(){ sLog('iframe_api.onload', {}); };
    s.onerror = function(){ sLog('iframe_api.onerror', {}); };

    document.head.appendChild(s);
    sLog('iframe_api.injected', {});

    window.onYouTubeIframeAPIReady = function(){
      sLog('iframe_api.ready', {});
      cb();
    };
  }

  loadIframeApi(function(){
    try {
      sLog('YT.Player.create', { videoId: vid, origin: ORIGIN });

      window.__ytPlayer__ = new YT.Player('player', {
        width: '100%',
        height: '100%',
        videoId: vid,
        host: 'https://www.youtube-nocookie.com',
        playerVars: {
          autoplay: 1,
          playsinline: 1,
          controls: 0,
          rel: 0,
          modestbranding: 1,
          enablejsapi: 1,
          origin: ORIGIN
        },
        events: {
          onReady: function(){
            sLog('YT.onReady', {});
            send('evt.ready');
          },
          onStateChange: function(e){
            sLog('YT.onStateChange', { state: e && e.data });
            send('evt.state', { state: e && e.data });
          },
          onError: function(e){
            sLog('YT.onError', { code: e && e.data });
            send('evt.error', { code: e && e.data });
          }
        }
      });
    } catch (e) {
      sLog('YT.create.exception', { err: String(e) });
      send('evt.error', { code: 'init_failed', message: String(e) });
    }
  });
})();
*/}).toString().slice(14,-3);

function handle(req, res) {
  var u = urlMod.parse(req.url, true);
  var path = u.pathname || '/';

  // Always log requests
  log('[http]', req.method, path);
  // Also store metadata for key routes
  if (path === '/player.html' || path === '/player.js' || path === '/log' || path === '/event') {
    pushEvent('http', { method: req.method, path: path, query: u.query || {}, meta: reqMeta(req) });
  }

  if (req.method === 'GET' && path === '/health') {
    return writeJson(res, 200, { ok: true });
  }

  if (req.method === 'GET' && path === '/ping') {
    log('[ping]', (u.query && u.query.from) || '', (u.query && u.query.ts) || '');
    pushEvent('ping', { from: (u.query && u.query.from) || '', ts: (u.query && u.query.ts) || '' });
    return writeJson(res, 200, { ok: true, ts: Date.now() });
  }

  if (req.method === 'POST' && path === '/log') {
    return readBody(req, function(body){
      var obj = {};
      try { obj = JSON.parse(body || '{}'); } catch (e) { obj = { raw: body }; }

      log('[clientlog]', (obj.src || ''), (obj.msg || ''), safeJson(obj.data || {}).slice(0, 300));
      pushEvent('clientlog', obj);

      return writeJson(res, 200, { ok: true });
    });
  }

  if (req.method === 'POST' && path === '/event') {
    return readBody(req, function(body2){
      var obj2 = {};
      try { obj2 = JSON.parse(body2 || '{}'); } catch (e) { obj2 = { raw: body2 }; }

      pushEvent('event', obj2);
      return writeJson(res, 200, { ok: true });
    });
  }

  if (req.method === 'GET' && path === '/debug/logs') {
    var tail = parseInt(u.query && u.query.tail, 10) || 500;
    if (tail < 1) tail = 1;
    if (tail > 4000) tail = 4000;
    return writeJson(res, 200, { ok: true, lines: LOGS.slice(-tail) });
  }

  if (req.method === 'GET' && path === '/debug/events') {
    var tailE = parseInt(u.query && u.query.tail, 10) || 200;
    if (tailE < 1) tailE = 1;
    if (tailE > 4000) tailE = 4000;
    return writeJson(res, 200, { ok: true, events: EVENTS.slice(-tailE) });
  }

  if (req.method === 'GET' && path === '/player.html') {
    var vid = (u.query && (u.query.videoId || u.query.v)) || '';
    log('[player.html]', 'videoId=', String(vid));
    return write(res, 200, {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-store',
      'Referrer-Policy': 'strict-origin-when-cross-origin'
    }, playerHtml(vid));
  }

  if (req.method === 'GET' && path === '/player.js') {
    log('[player.js]', 'serve');
    return write(res, 200, {
      'Content-Type': 'application/javascript; charset=utf-8',
      'Cache-Control': 'no-store',
      'Referrer-Policy': 'strict-origin-when-cross-origin'
    }, PLAYER_JS);
  }

  return writeJson(res, 404, { ok: false, error: 'not_found' });
}

function onStart() {
  if (server) {
    log('[ytresolver] onStart called (server already running)');
    return;
  }

  log('[ytresolver] SERVICE STARTING pid=', process.pid);
  server = http.createServer(handle);
  server.on('error', function(err){
    log('[ytresolver] server error', err && err.message);
    pushEvent('server.error', { message: err && err.message, stack: err && err.stack });
  });

  server.listen(PORT, LISTEN_HOST, function(){
    log('[ytresolver] ✓ SERVER LISTENING on ' + LISTEN_HOST + ':' + PORT);
    pushEvent('server.listening', { host: LISTEN_HOST, port: PORT });
  });
}

onStart();
""";

            await File.WriteAllTextAsync(serviceJsPath, serviceJsContent, new UTF8Encoding(false));
        }
    }
}