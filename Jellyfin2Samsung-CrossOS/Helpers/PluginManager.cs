using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class PluginManager
    {
        private readonly HttpClient _httpClient;
        private readonly JellyfinApiClient _apiClient;

        public PluginManager(HttpClient httpClient, JellyfinApiClient apiClient)
        {
            _httpClient = httpClient;
            _apiClient = apiClient;
        }

        private const string KefinTweaksRawRoot = "https://raw.githubusercontent.com/ranaldsgift/KefinTweaks/v0.4.5/";

        private static readonly Regex kefinScriptRegex = new Regex(
            @"script\.src\s*=\s*([`""])[^`""']*kefinTweaks-plugin\.js\1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );


        private static readonly List<PluginMatrixEntry> PluginMatrix = new()
        {
            new PluginMatrixEntry
            {
                Name = "Jellyfin Enhanced",
                FallbackUrls = new(),
                ExplicitServerFiles = new List<string>
                {
                    "/JellyfinEnhanced/script",
                    "/JellyfinEnhanced/js/splashscreen.js",
                    "/JellyfinEnhanced/js/reviews.js",
                    "/JellyfinEnhanced/js/qualitytags.js",
                    "/JellyfinEnhanced/js/plugin.js",
                    "/JellyfinEnhanced/js/pausescreen.js",
                    "/JellyfinEnhanced/js/migrate.js",
                    "/JellyfinEnhanced/js/letterboxd-links.js",
                    "/JellyfinEnhanced/js/languagetags.js",
                    "/JellyfinEnhanced/js/genretags.js",
                    "/JellyfinEnhanced/js/elsewhere.js",
                    "/JellyfinEnhanced/js/arr-tag-links.js",
                    "/JellyfinEnhanced/js/arr-links.js",
                    "/JellyfinEnhanced/js/enhanced/config.js",
                    "/JellyfinEnhanced/js/enhanced/events.js",
                    "/JellyfinEnhanced/js/enhanced/features.js",
                    "/JellyfinEnhanced/js/enhanced/helpers.js",
                    "/JellyfinEnhanced/js/enhanced/playback.js",
                    "/JellyfinEnhanced/js/enhanced/subtitles.js",
                    "/JellyfinEnhanced/js/enhanced/themer.js",
                    "/JellyfinEnhanced/js/enhanced/ui.js",
                    "/JellyfinEnhanced/js/jellyseerr/api.js",
                    "/JellyfinEnhanced/js/jellyseerr/jellyseerr.js",
                    "/JellyfinEnhanced/js/jellyseerr/modal.js",
                    "/JellyfinEnhanced/js/jellyseerr/ui.js"
                }
            },
            new PluginMatrixEntry
            {
                Name = "Media Bar",
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-media-bar@main/slideshowpure.js"
                },
                UseBabel = true
            },
            new PluginMatrixEntry
            {
                Name = "EditorsChoice",
                FallbackUrls = new(),
                UseBabel = true
            },
            new PluginMatrixEntry
            {
                Name = "Home Screen Sections",
                FallbackUrls = new List<string>
                {
                    "https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-home-sections/main/src/Jellyfin.Plugin.HomeScreenSections/Inject/HomeScreenSections.js"
                },
                UseBabel = true
            },
            new PluginMatrixEntry
            {
                Name = "Plugin Pages",
                FallbackUrls = new(),   
                UseBabel = true
            },
            new PluginMatrixEntry
            {
                Name = "KefinTweaks",
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/ranaldsgift/KefinTweaks@latest/kefinTweaks-plugin.js"
                },
                UseBabel = true
            }
        };

        public PluginMatrixEntry? FindPluginEntry(JellyfinPluginInfo plugin)
        {
            if (plugin?.Name == null)
                return null;

            string pluginName = plugin.Name.ToLowerInvariant();

            return PluginMatrix.FirstOrDefault(entry =>
                pluginName.Contains(entry.Name.ToLowerInvariant()));
        }

        public async Task DownloadExplicitFilesAsync(
                    string serverUrl,
                    string pluginCacheDir,
                    PluginMatrixEntry entry)
        {
            if (entry?.ExplicitServerFiles == null || entry.ExplicitServerFiles.Count == 0)
                return;

            Trace.WriteLine("▶ Downloading explicit Enhanced JS modules...");

            foreach (var rel in entry.ExplicitServerFiles)
            {
                try
                {
                    string url = serverUrl.TrimEnd('/') + rel;
                    Trace.WriteLine($"   → Fetch Enhanced JS: {url}");

                    string js = await _httpClient.GetStringAsync(url);

                    // Transpile to es2015 using esbuild; fallback is original JS.
                    js = await EsbuildHelper.TranspileAsync(js, rel);

                    string relPath = rel.TrimStart('/');
                    string outPath = Path.Combine(pluginCacheDir, relPath.Replace('/', Path.DirectorySeparatorChar));

                    string? directory = Path.GetDirectoryName(outPath);
                    if (directory != null)
                        Directory.CreateDirectory(directory);

                    if (!Path.HasExtension(outPath) ||
                        !outPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        outPath += ".js";
                    }

                    await File.WriteAllTextAsync(outPath, js);

                    string logPath = outPath.Replace(pluginCacheDir + Path.DirectorySeparatorChar, "plugin_cache/");
                    Trace.WriteLine($"      ✓ Saved {logPath}");
                    if (rel.Equals("/JellyfinEnhanced/script", StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.WriteLine("      🔧 Patching Enhanced main script (script.js)...");
                        await PatchEnhancedMainScript(outPath);
                    }

                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"      ⚠ Failed Enhanced JS '{rel}': {ex}");
                }
            }
        }
        private string PatchHomeScreenSections(string js)
        {
            if (js.Contains("__HSS_TV_REBUILD__"))
                return js;

            // Best-effort removal of original plugin auto-inits (don’t rely on exact whitespace forever)
            js = js.Replace(
                "$(document).ready(function() {\n    setTimeout(function() {\n      HomeScreenSectionsHandler2.init();\n    }, 50);\n  });",
                "/* HSS original auto-init removed */"
            );

            js = js.Replace(
                "setTimeout(function() {\n    TopTenSectionHandler2.init();\n  }, 50);",
                "/* HSS TopTen auto-init removed */"
            );

            js += @"
;/* __HSS_TV_REBUILD__ */
(function () {
  try {
    if (window.__HSS_TV_REBUILD_LOADED__) return;
    window.__HSS_TV_REBUILD_LOADED__ = true;

    function log() {
      try { console.log.apply(console, arguments); } catch (_) {}
    }
    function warn() {
      try { console.warn.apply(console, arguments); } catch (_) {}
    }
    function err() {
      try { console.error.apply(console, arguments); } catch (_) {}
    }

    log('[HSS][TV] rebuild loaded');

    function apiReady() {
      return !!(window.ApiClient && window.ApiClient._currentUser && window.ApiClient._currentUser.Id);
    }

    function getRoot() {
      return document.getElementById('reactRoot') || document.body;
    }

    function ensureContainer() {
      var existing = document.getElementById('hssTvRoot');
      if (existing) return existing;

      var root = getRoot();
      if (!root) return null;

      var wrap = document.createElement('div');
      wrap.id = 'hssTvRoot';
      wrap.setAttribute('data-hss-tv', 'true');

      // Keep styling minimal; rely mostly on Jellyfin’s existing CSS
      wrap.style.padding = '0.5rem 0';
      wrap.style.margin = '0';
      wrap.style.width = '100%';
      wrap.style.position = 'relative';
      wrap.style.zIndex = '2';

      // Insert near top so it’s visible on home
      // If reactRoot contains the app, prepend so it shows above lists (TV friendly)
      if (root.firstChild) root.insertBefore(wrap, root.firstChild);
      else root.appendChild(wrap);

      return wrap;
    }

    function cssOnce() {
      if (document.getElementById('hssTvCss')) return;
      var style = document.createElement('style');
      style.id = 'hssTvCss';
      style.type = 'text/css';
      style.textContent = ''
        + '#hssTvRoot .hss-row { margin: 0.75rem 0; }'
        + '#hssTvRoot .hss-title { font-size: 1.2em; padding: 0 0.75rem; opacity: 0.95; }'
        + '#hssTvRoot .hss-scroller { display: flex; overflow: hidden; padding: 0.5rem 0.75rem; gap: 0.75rem; }'
        + '#hssTvRoot .hss-card { flex: 0 0 auto; width: 16rem; }'
        + '#hssTvRoot .hss-card [tabindex] { outline: none; }'
        + '#hssTvRoot .hss-focus { box-shadow: 0 0 0 3px rgba(255,255,255,0.65); border-radius: 0.5rem; }'
        + '#hssTvRoot .hss-poster { width: 100%; aspect-ratio: 16/9; border-radius: 0.5rem; background: rgba(255,255,255,0.08); overflow: hidden; }'
        + '#hssTvRoot .hss-poster img { width: 100%; height: 100%; object-fit: cover; display: block; }'
        + '#hssTvRoot .hss-name { margin-top: 0.35rem; font-size: 1.0em; opacity: 0.95; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }'
        + '#hssTvRoot .hss-sub { font-size: 0.85em; opacity: 0.75; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }'
        + '';
      document.head.appendChild(style);
    }

    // Build image URL (Jellyfin)
    function imageUrl(itemId, tag, type, maxWidth) {
      try {
        if (!window.ApiClient || !window.ApiClient.getUrl) return null;
        var q = [];
        if (maxWidth) q.push('maxWidth=' + encodeURIComponent(maxWidth));
        if (tag) q.push('tag=' + encodeURIComponent(tag));
        var qs = q.length ? ('?' + q.join('&')) : '';
        // /Items/{id}/Images/Primary
        return window.ApiClient.getUrl('Items/' + itemId + '/Images/' + type) + qs;
      } catch (e) {
        return null;
      }
    }

    function safeNavigateToItem(id) {
      try {
        // Best effort: Jellyfin web uses routes like '#!/details?id=...'
        // Some builds accept 'details?id=' as well.
        if (window.Dashboard && typeof window.Dashboard.navigate === 'function') {
          window.Dashboard.navigate('#!/details?id=' + encodeURIComponent(id));
          return;
        }
      } catch (_) {}

      try {
        window.location.hash = '#!/details?id=' + encodeURIComponent(id);
      } catch (_) {}
    }

    // ---- ApiClient wrappers ----
    function apiGet(url) {
      return window.ApiClient.ajax({
        url: window.ApiClient.getUrl(url),
        type: 'GET',
        dataType: 'json'
      });
    }

    function getViews(userId) {
      // /Users/{id}/Views
      return apiGet('Users/' + userId + '/Views');
    }

    function getLatest(userId, parentId, limit) {
      // /Users/{id}/Items/Latest?ParentId=...
      var url = 'Users/' + userId + '/Items/Latest'
        + '?Limit=' + encodeURIComponent(limit || 16)
        + '&Fields=PrimaryImageAspectRatio%2CPath%2CPrimaryImageTag'
        + '&ImageTypeLimit=1'
        + '&EnableImageTypes=Primary%2CBackdrop%2CThumb'
        + '&ParentId=' + encodeURIComponent(parentId);
      return apiGet(url);
    }

    // ---- Rendering ----
    var focus = {
      rows: [],     // [{ elRow, cards:[{el, id}] }]
      r: 0,
      c: 0
    };

    function clearFocusMap() {
      focus.rows = [];
      focus.r = 0;
      focus.c = 0;
    }

    function setFocused(r, c) {
      // remove old
      for (var i = 0; i < focus.rows.length; i++) {
        var cards = focus.rows[i].cards;
        for (var j = 0; j < cards.length; j++) {
          cards[j].el.classList.remove('hss-focus');
        }
      }

      focus.r = Math.max(0, Math.min(r, focus.rows.length - 1));
      var row = focus.rows[focus.r];
      if (!row || !row.cards.length) return;

      focus.c = Math.max(0, Math.min(c, row.cards.length - 1));
      var card = row.cards[focus.c];
      card.el.classList.add('hss-focus');

      // ensure visible in scroller
      try {
        var scroller = row.elRow.querySelector('.hss-scroller');
        if (scroller && card.el && card.el.scrollIntoView) {
          card.el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        }
      } catch (_) {}
    }

    function onKeyDown(e) {
      // Tizen remote: keyCode varies by model; handle arrows + enter + return/back
      var code = e.keyCode || 0;

      // Left 37, Up 38, Right 39, Down 40, Enter 13
      if (code === 37) { setFocused(focus.r, focus.c - 1); e.preventDefault(); return; }
      if (code === 39) { setFocused(focus.r, focus.c + 1); e.preventDefault(); return; }
      if (code === 38) { setFocused(focus.r - 1, focus.c); e.preventDefault(); return; }
      if (code === 40) { setFocused(focus.r + 1, focus.c); e.preventDefault(); return; }

      if (code === 13) {
        var row = focus.rows[focus.r];
        if (!row) return;
        var card = row.cards[focus.c];
        if (!card) return;
        log('[HSS][TV] enter -> item', card.id);
        safeNavigateToItem(card.id);
        e.preventDefault();
        return;
      }
    }

    function makeCard(item, subtitle) {
      var card = document.createElement('div');
      card.className = 'hss-card';
      card.setAttribute('tabindex', '-1');

      var poster = document.createElement('div');
      poster.className = 'hss-poster';

      var img = document.createElement('img');
      img.alt = item && item.Name ? item.Name : '';
      // Prefer Primary tag if present
      var tag = null;
      if (item && item.ImageTags && item.ImageTags.Primary) tag = item.ImageTags.Primary;
      if (item && item.PrimaryImageTag) tag = item.PrimaryImageTag;

      var src = imageUrl(item.Id, tag, 'Primary', 600) || '';
      if (src) img.src = src;

      poster.appendChild(img);

      var name = document.createElement('div');
      name.className = 'hss-name';
      name.textContent = (item && item.Name) ? item.Name : 'Unknown';

      var sub = document.createElement('div');
      sub.className = 'hss-sub';
      sub.textContent = subtitle || '';

      card.appendChild(poster);
      card.appendChild(name);
      card.appendChild(sub);

      // Click support (mouse / click)
      card.addEventListener('click', function () {
        safeNavigateToItem(item.Id);
      }, true);

      return card;
    }

    function renderRow(container, title, items, subtitleBuilder) {
      var row = document.createElement('div');
      row.className = 'hss-row';

      var h = document.createElement('div');
      h.className = 'hss-title';
      h.textContent = title;

      var sc = document.createElement('div');
      sc.className = 'hss-scroller';

      row.appendChild(h);
      row.appendChild(sc);
      container.appendChild(row);

      var cards = [];
      for (var i = 0; i < items.length; i++) {
        var it = items[i];
        var sub = '';
        try { sub = subtitleBuilder ? subtitleBuilder(it) : ''; } catch (_) {}
        var el = makeCard(it, sub);
        sc.appendChild(el);
        cards.push({ el: el, id: it.Id });
      }

      focus.rows.push({ elRow: row, cards: cards });
    }

    function activateOnce() {
      if (window.__HSS_TV_ACTIVE__) return;
      window.__HSS_TV_ACTIVE__ = true;

      cssOnce();

      var container = ensureContainer();
      if (!container) {
        warn('[HSS][TV] no container (reactRoot/body missing)');
        return;
      }

      // Clear any previous content
      container.innerHTML = '';
      clearFocusMap();

      // Attach remote handler once
      if (!window.__HSS_TV_KEYS__) {
        window.__HSS_TV_KEYS__ = true;
        document.addEventListener('keydown', onKeyDown, true);
      }

      var userId = window.ApiClient._currentUser.Id;

      // Row 1: “Mijn media” based on Views (libraries)
      getViews(userId).then(function (viewsResp) {
        try {
          var viewItems = (viewsResp && viewsResp.Items) ? viewsResp.Items : [];
          if (!viewItems.length) {
            warn('[HSS][TV] no views returned');
            return;
          }

          renderRow(container, 'Mijn media', viewItems.slice(0, 12), function (it) {
            // Types: CollectionFolder, etc
            return it.CollectionType ? it.CollectionType : (it.Type ? it.Type : '');
          });

          // Row 2+: Latest for first two libraries (like Jellyfin does)
          // This mimics your log calls: Items/Latest?ParentId=<libraryId>
          var libs = viewItems.slice(0, 2);
          var p = Promise.resolve();

          for (var i = 0; i < libs.length; i++) {
            (function (lib) {
              p = p.then(function () {
                return getLatest(userId, lib.Id, 12).then(function (latestItems) {
                  if (!latestItems || !latestItems.length) return;
                  renderRow(container, 'Nieuw in ' + (lib.Name || 'Bibliotheek'), latestItems, function (it) {
                    return it.ProductionYear ? String(it.ProductionYear) : '';
                  });
                });
              });
            })(libs[i]);
          }

          p.then(function () {
            // Set initial focus on first card
            setTimeout(function () { setFocused(0, 0); }, 50);
            log('[HSS][TV] rendered rows:', focus.rows.length);
          });

        } catch (e) {
          err('[HSS][TV] render failed', e);
        }
      }, function (e) {
        err('[HSS][TV] getViews failed', e);
      });
    }

    // Wait for ApiClient + user, then activate
    var tries = 0;
    var timer = setInterval(function () {
      tries++;

      if (apiReady()) {
        clearInterval(timer);
        log('[HSS][TV] ApiClient ready -> activate');
        activateOnce();
        return;
      }

      if (tries >= 80) {
        clearInterval(timer);
        warn('[HSS][TV] ApiClient not ready (timeout)');
      }
    }, 200);

  } catch (e) {
    try { console.error('[HSS][TV] fatal', e); } catch (_) {}
  }
})();
";

            return js;
        }


        public async Task<string?> DownloadAndTranspileAsync(string url, string cacheDir, string relPath)
        {
            try
            {
                Trace.WriteLine($"▶ Downloading plugin JS: {url}");

                string js = await _httpClient.GetStringAsync(url);
                js = await EsbuildHelper.TranspileAsync(js, relPath);

                if (relPath.Contains("HomeScreenSections", StringComparison.OrdinalIgnoreCase))
                    js = PatchHomeScreenSections(js);

                string localPath = Path.Combine(cacheDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                await File.WriteAllTextAsync(localPath, js, Encoding.UTF8);
                return localPath;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"⚠ Plugin download failed: {ex}");
                return null;
            }
        }

        public async Task PatchJavaScriptInjectorPublicJsAsync(string pluginCacheDir, string serverUrl)
        {
            try
            {
                string publicJsPath = Path.Combine(pluginCacheDir, "public.js");
                if (!File.Exists(publicJsPath))
                {
                    Trace.WriteLine("▶ JavaScript Injector: public.js not found, skipping patch.");
                    return;
                }

                string js = await File.ReadAllTextAsync(publicJsPath, Encoding.UTF8);

                // 1. Detect the original CDN root
                if (!kefinScriptRegex.IsMatch(js))
                {
                    Trace.WriteLine("▶ JavaScript Injector: No KefinTweaks script loader found in public.js, skipping patch.");
                    return;
                }


                // Ensure kefinTweaks folder exists
                Directory.CreateDirectory(Path.Combine(pluginCacheDir, "kefinTweaks"));

                // 2. Download KefinTweaks main files
                string kefinTweaksPluginUrl = KefinTweaksRawRoot + "kefinTweaks-plugin.js";
                await DownloadAndTranspileAsync(
                    kefinTweaksPluginUrl,
                    pluginCacheDir,
                    Path.Combine("kefinTweaks", "kefinTweaks-plugin.js"));

                var pluginJsPath = Path.Combine(pluginCacheDir, "kefinTweaks", "kefinTweaks-plugin.js");
                if (File.Exists(pluginJsPath))
                {
                    var pluginJs = await File.ReadAllTextAsync(pluginJsPath, Encoding.UTF8);

                    pluginJs = pluginJs.Replace(
                        "https://cdn.jsdelivr.net/gh/ranaldsgift/KefinTweaks",
                        "plugin_cache/kefinTweaks"
                    );

                    await File.WriteAllTextAsync(pluginJsPath, pluginJs, Encoding.UTF8);
                }


                string injectorUrl = KefinTweaksRawRoot + "injector.js";
                string? injectorPath = await DownloadAndTranspileAsync(
                    injectorUrl,
                    pluginCacheDir,
                    Path.Combine("kefinTweaks", "injector.js"));

                if (injectorPath != null)
                {
                    await ProcessKefinTweaksModulesAsync(pluginCacheDir, KefinTweaksRawRoot);
                    await ProcessKefinTweaksCssAsync(pluginCacheDir, KefinTweaksRawRoot);

                    // 3. Resolve selected skin
                    var skin = GetKefinDefaultSkin(pluginCacheDir);
                    Trace.WriteLine($"⚙ KefinTweaks: detected default skin: {skin ?? "null"}");

                    if (!string.IsNullOrWhiteSpace(skin))
                    {
                        var info = await _apiClient.GetPublicSystemInfoAsync(serverUrl);
                        var version = info?.Version ?? "0.0.0";
                        var majorMinor = string.Join(".", version.Split('.').Take(2));

                        string skinLower = skin.ToLowerInvariant();
                        string skinsDir = Path.Combine(pluginCacheDir, "kefinTweaks", "skins", "css");
                        Directory.CreateDirectory(skinsDir);

                        string localTheme = Path.Combine(skinsDir, $"{skinLower}-kefin.css");
                        string localFixes = Path.Combine(skinsDir, $"{skinLower}-kefin-{majorMinor}.css");

                        string themeHref =
                            $"plugin_cache/kefinTweaks/skins/css/{skinLower}-kefin.css";
                        string fixesHref =
                            $"plugin_cache/kefinTweaks/skins/css/{skinLower}-kefin-{majorMinor}.css";

                        // Theme.css
                        if (!File.Exists(localTheme))
                        {
                            try
                            {
                                var url = $"https://cdn.jsdelivr.net/gh/n00bcodr/{skinLower}@main/theme.css";
                                Trace.WriteLine($"▶ KefinTweaks: downloading theme CSS: {url}");

                                var css = await _httpClient.GetStringAsync(url);
                                await File.WriteAllTextAsync(localTheme, css);

                                js = EnsureCssLinked(js, themeHref);
                            }
                            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.IO.IOException)
                            {
                                Trace.WriteLine($"⚠ KefinTweaks: Failed to download theme CSS: {ex}");
                            }
                        }

                        if (!File.Exists(localFixes))
                        {
                            try
                            {
                                var fixesUrl =
                                    $"https://cdn.jsdelivr.net/gh/n00bcodr/{skinLower}@main/{majorMinor}_fixes.css";
                                Trace.WriteLine($"▶ KefinTweaks: downloading fixes CSS: {fixesUrl}");
                                var css = await _httpClient.GetStringAsync(fixesUrl);
                                await File.WriteAllTextAsync(localFixes, css);
                            }
                            catch
                            {
                                Trace.WriteLine("ℹ KefinTweaks: no version-specific fixes found");
                            }
                        }

                        if (File.Exists(localFixes))
                        {
                            js = EnsureCssLinked(js, fixesHref);
                        }

                        Trace.WriteLine($"✓ KefinTweaks skin cached & injected: {skin} ({majorMinor})");
                    }
                }

                // 4. Rewrite CDN → local
                js = kefinScriptRegex.Replace(
                    js,
                    "script.src = 'plugin_cache/kefinTweaks/kefinTweaks-plugin.js';"
                );

                // Rewrite KefinTweaks config root → local cache
                js = Regex.Replace(
                    js,
                    @"""kefinTweaksRoot""\s*:\s*""https:\/\/cdn\.jsdelivr\.net\/gh\/ranaldsgift\/KefinTweaks@latest\/""",
                    @"""kefinTweaksRoot"": ""plugin_cache/kefinTweaks/""",
                    RegexOptions.IgnoreCase
                );


                // 5. Write ONCE
                await File.WriteAllTextAsync(publicJsPath, js, Encoding.UTF8);
                Trace.WriteLine("✓ JavaScript Injector: public.js patched successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"⚠ JavaScript Injector error: {ex}");
            }
        }

        private async Task ProcessKefinTweaksModulesAsync(string pluginCacheDir, string downloadRoot)
        {
            try
            {
                string injectorPath = Path.Combine(pluginCacheDir, "kefinTweaks", "injector.js");
                if (!File.Exists(injectorPath))
                {
                    Trace.WriteLine("▶ KefinTweaks: injector.js not found in plugin_cache, skipping module prefetch.");
                    return;
                }

                string injectorSource = await File.ReadAllTextAsync(injectorPath, Encoding.UTF8);

                // Grab all `script: "something.js"` entries from SCRIPT_DEFINITIONS
                var scriptRegex = new Regex(@"script\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                var matches = scriptRegex.Matches(injectorSource);

                // ... (rest of ProcessKefinTweaksModulesAsync remains unchanged) ...

                if (matches.Count == 0)
                {
                    Trace.WriteLine("▶ KefinTweaks: no SCRIPT_DEFINITIONS scripts found in injector.js, nothing to prefetch.");
                    return;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    string scriptName = m.Groups[1].Value.Trim();

                    // 1. Skip empty strings.
                    if (string.IsNullOrEmpty(scriptName))
                        continue;

                    // 2. NEW FIX: Only process files that end with a .js extension to avoid malformed entries.
                    if (!scriptName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.WriteLine($"      ⚠ KefinTweaks: Skipping non-JS module or malformed entry: {scriptName}");
                        continue;
                    }

                    // Avoid duplicates
                    if (!seen.Add(scriptName))
                        continue;

                    // Explicitly skip files located in the root (kefinTweaks-plugin.js and injector.js)
                    if (string.Equals(scriptName, "kefinTweaks-plugin.js", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(scriptName, "injector.js", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string modulePath = scriptName;

                    // --- Logic to handle nested files in GitHub repo ---

                    // 1. Check for the single deeply nested file in 'scripts/third party/'
                    if (scriptName.Equals("jquery.flurry.min.js", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use URL encoding for the space in the folder name for the download URL
                        modulePath = "scripts/third%20party/" + scriptName;
                    }
                    // 2. All other module files (e.g., utils.js, collections.js, etc.)
                    //    are assumed to be in the top-level 'scripts/' directory of the repo.
                    else
                    {
                        modulePath = "scripts/" + scriptName;
                    }

                    // Use the reliable downloadRoot (raw GitHub)
                    string url = downloadRoot + modulePath;

                    // The local path for the WGT must handle the 'third party' folder correctly.
                    string localModulePath = modulePath.Replace("%20", " ");
                    string relPath = Path.Combine("kefinTweaks", localModulePath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                    await DownloadAndTranspileAsync(url, pluginCacheDir, relPath);
                }

                Trace.WriteLine("      ✓ KefinTweaks: module scripts downloaded & transpiled into plugin_cache/kefinTweaks/");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"⚠ KefinTweaks: error while processing modules: {ex}");
            }
        }
        private static string EnsureCssLinked(string js, string href)
        {
            if (js.Contains(href, StringComparison.OrdinalIgnoreCase))
                return js;

            return js + $@"

(function () {{
    try {{
        var href = '{href}';

        if (document.querySelector('link[href=""' + href + '""]'))
            return;

        var link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;

        document.head.appendChild(link);
        console.log('🪼 KefinTweaks CSS injected:', href);
    }} catch (e) {{
        console.error('Failed to inject KefinTweaks CSS', e);
    }}
}})();
";
        }

        private async Task ProcessKefinTweaksCssAsync(string pluginCacheDir, string downloadRoot)
        {
            // List of CSS files found in the KefinTweaks 'skins' directory
            var cssFiles = new List<string>
            {
                "chromic-kefin.css",
                "elegant-kefin.css",
                "fin-kefin-10.11.css",
                "flow-kefin.css",
                "glassfin-kefin.css",
                "jamfin-kefin-10.css",
                "jamfin-kefin.css",
                "neutralfin-kefin.css",
                "scyfin-kefin.css",
                "optional/ElegantFin/solidAppBar.css",
                "optional/ElegantFin/libraryLabelVisibility.css",
                "optional/ElegantFin/extraOverlayButtons.css",
                "optional/ElegantFin/centerPlayButton.css",
                "optional/ElegantFin/cardHoverEffect.css",
            };

            Trace.WriteLine("▶ KefinTweaks: Pre-fetching CSS skins...");

            foreach (var fileName in cssFiles)
            {
                try
                {
                    // The CSS files are in the 'skins/' directory of the repo
                    string repoPath = $"skins/{fileName}";
                    string url = downloadRoot + repoPath;

                    // The local path must preserve the 'optional/ElegantFin/' structure
                    string localRelPath = Path.Combine("kefinTweaks","skins","css",fileName);
                    string localFullPath = Path.Combine(pluginCacheDir, localRelPath);

                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(localFullPath)!);

                    // Download CSS directly (no transpile needed)
                    var cssBytes = await _httpClient.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(localFullPath, cssBytes);

                    Trace.WriteLine($"      ✓ Downloaded CSS skin: {fileName}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"      ⚠ Failed to fetch KefinTweaks CSS '{fileName}': {ex}");
                }
            }
        }

        private async Task PatchEnhancedMainScript(string scriptPath)
        {
            if (!File.Exists(scriptPath))
                return;

            string original = await File.ReadAllTextAsync(scriptPath);

            string patch = @"
// ---- J2S SCRIPT PATCH: FORCE LOCAL ENHANCED MODULE LOADING ----
(function () {

    // Rewrite only Enhanced JS URLs (absolute or relative) to plugin_cache
    function rewriteEnhancedUrl(url) {
        try {
            if (typeof url !== 'string') return url;

            // Strip ?v=timestamp etc
            var base = url.split('?')[0];

            // ---- ONLY REWRITE .js FILES ----
            if (
                base.endsWith('.js') &&
                base.indexOf('/JellyfinEnhanced/') !== -1
            ) {
                // Handle both absolute and relative URLs
                var idx = base.indexOf('/JellyfinEnhanced/');
                var sub = base.substring(idx + '/JellyfinEnhanced/'.length);
                return 'plugin_cache/JellyfinEnhanced/' + sub;
            }

            // ---- DO NOT REWRITE JSON OR CONFIG ----
            return url;
        }
        catch (e) {
            console.error('J2S rewriteEnhancedUrl failed', e);
            return url;
        }
    }

    // Patch document.createElement so any script.src is rewritten ONLY for JS modules
    var _createElement = document.createElement;
    document.createElement = function (tag) {
        var el = _createElement.call(document, tag);

        if (tag && tag.toLowerCase() === 'script') {

            var _setAttribute = el.setAttribute;

            // Intercept setAttribute('src', ...)
            el.setAttribute = function (name, value) {
                if (name === 'src') {
                    value = rewriteEnhancedUrl(value);
                }
                return _setAttribute.call(el, name, value);
            };

            // Intercept direct assignment: script.src = '...'
            Object.defineProperty(el, 'src', {
                configurable: true,
                get: function () {
                    return el.getAttribute('src');
                },
                set: function (value) {
                    value = rewriteEnhancedUrl(value);
                    _setAttribute.call(el, 'src', value);
                }
            });
        }

        return el;
    };

    // Optional: rewrite fetch() requests ONLY for JS Enhanced modules
    if (typeof window.fetch === 'function') {
        var _fetch = window.fetch;
        window.fetch = function (resource, init) {
            try {
                if (typeof resource === 'string') {
                    resource = rewriteEnhancedUrl(resource);
                }
            }
            catch (e) {
                console.error('FETCH rewrite failed', e);
            }
            return _fetch.call(this, resource, init);
        };
    }

    console.log('🪼 J2S: script.js loader patched to use plugin_cache for Enhanced JS modules');
})();
";
            string combined = patch + "\n\n" + original;
            await File.WriteAllTextAsync(scriptPath, combined);
        }
        private static string? GetKefinDefaultSkin(string pluginCacheDir)
        {
            var publicJs = Path.Combine(pluginCacheDir, "public.js");
            Trace.WriteLine($"SEARCHRING FOR {publicJs}");
            if (!File.Exists(publicJs))
                return null;

            var text = File.ReadAllText(publicJs);

            Trace.WriteLine(publicJs);

            var match = Regex.Match(
                text,
                @"""defaultSkin""\s*:\s*""([^""]+)""",
                RegexOptions.IgnoreCase
            );

            return match.Success ? match.Groups[1].Value : null;
        }
    }
}