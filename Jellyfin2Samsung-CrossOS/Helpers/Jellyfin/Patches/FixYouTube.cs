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
            foreach (var file in Directory.GetFiles(www, "youtubePlayer-plugin.*.js"))
            {
                var js = await File.ReadAllTextAsync(file);

                if (js.Contains("__NATIVE_STABLE_V1__"))
                    continue;
                string nativeCode = $@"
/* === TIZEN NATIVE MP4 BRIDGE (STABLE V1.4) === */
(function () {{
    if (window.__NATIVE_STABLE_V1__) return;
    window.__NATIVE_STABLE_V1__ = true;

    var API_BASE = 'http://127.0.0.1:8123';

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
            fetch(API_BASE + '/stream', {{
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
                new XAttribute("type", "ui"),
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
        public async Task CreatePackageJsonAsync(PackageWorkspace ws)
        {
            string serviceDir = Path.Combine(ws.Root, "service");
            string packageJsonPath = Path.Combine(serviceDir, "package.json");

            if (!Directory.Exists(serviceDir)) Directory.CreateDirectory(serviceDir);

            string packageJsonContent = "{\n  \"name\": \"ytresolver\",\n  \"version\": \"1.0.0\",\n  \"main\": \"service.js\",\n  \"dependencies\": {}\n}";

            // 6. FIX 3: Save package.json without BOM
            var utf8NoBom = new UTF8Encoding(false); // <--- Critical
            await File.WriteAllTextAsync(packageJsonPath, packageJsonContent, utf8NoBom);
        }
        public async Task CreateYouTubeResolverAsync(PackageWorkspace ws)
        {
            string serviceDir = Path.Combine(ws.Root, "service");
            string serviceJsPath = Path.Combine(serviceDir, "service.js");

            if (!Directory.Exists(serviceDir))
                Directory.CreateDirectory(serviceDir);

            // ES2015-friendly (no /s flag). Uses module.exports.onStart/onStop.
            string serviceJsContent = @"const http = require('http');
const https = require('https');

const PORT = 8123;
let server = null;

function fetchUrl(url, headers) {
  headers = headers || {};

  return new Promise(function(resolve, reject) {
    https.get(url, { headers: headers }, function(res) {
      let data = '';
      res.on('data', function(c) { data += c; });
      res.on('end', function() { resolve(data); });
    }).on('error', reject);
  });
}

function extractPlayerResponse(html) {
  // NOTE: No /s flag (dotAll) to keep compatibility
  const m = html.match(/ytInitialPlayerResponse\\s*=\\s*(\\{[\\s\\S]*?\\});/);
  if (!m) return null;

  try {
    return JSON.parse(m[1]);
  } catch (e) {
    return null;
  }
}

function resolveStream(youtubeUrl) {
  return fetchUrl(youtubeUrl, {
    'User-Agent': 'Mozilla/5.0 (SMART-TV; Tizen)'
  }).then(function(html) {
    const pr = extractPlayerResponse(html);
    if (!pr || !pr.streamingData || !pr.streamingData.formats) return null;

    const formats = pr.streamingData.formats;
    for (let i = 0; i < formats.length; i++) {
      const mt = formats[i].mimeType || '';
      if (mt.indexOf('video/mp4') !== -1 && mt.indexOf('avc1') !== -1) {
        return formats[i].url || null;
      }
    }
    return null;
  });
}

function requestHandler(req, res) {
  if (req.method === 'POST' && req.url === '/stream') {
    let body = '';
    req.on('data', function(c) { body += c; });
    req.on('end', function() {
      try {
        const data = JSON.parse(body || '{}');
        const url = data.url;
        if (!url) throw new Error('missing url');

        resolveStream(url).then(function(streamUrl) {
          if (!streamUrl) throw new Error('no stream');
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ mode: 'stream', url: streamUrl }));
        }).catch(function(e) {
          res.writeHead(500, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: e.message }));
        });
      } catch (e) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: e.message }));
      }
    });
    return;
  }

  res.writeHead(404);
  res.end();
}

// Tizen Web Service lifecycle (recommended)
module.exports.onStart = function() {
  if (server) return;

  server = http.createServer(requestHandler);
  server.listen(PORT, '127.0.0.1', function() {
    console.log('[ytresolver] listening on', PORT);
  });
};

module.exports.onStop = function() {
  if (!server) return;

  try {
    server.close(function() {
      console.log('[ytresolver] stopped');
    });
  } finally {
    server = null;
  }
};
";

            var utf8NoBom = new UTF8Encoding(false);
            await File.WriteAllTextAsync(serviceJsPath, serviceJsContent, utf8NoBom);
        }

    }
}