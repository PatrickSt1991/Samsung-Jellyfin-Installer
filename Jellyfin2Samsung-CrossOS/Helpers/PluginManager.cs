using Avalonia.Controls;
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
using static System.Collections.Specialized.BitVector32;

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
        private string PatchEditorsChoice(string js)
        {
            if (js.Contains("__HSS_NATIVE_FIX_V9__")) return js;

            string css = @"
        .hss-hero-container { position: relative; width: 94%; height: 480px; margin: 100px auto 40px auto; overflow: hidden; border-radius: 20px; background: #000; }
        .hss-bg-wrapper { position: absolute; inset: 0; z-index: 1; }
        .hss-bg-img { width: 100%; height: 100%; object-fit: cover; opacity: 0; transition: opacity 1.2s ease-in-out; }
        .hss-overlay { position: absolute; inset: 0; background: linear-gradient(90deg, rgba(0,0,0,0.95) 15%, rgba(0,0,0,0.3) 50%, transparent 100%); z-index: 2; }
        .hss-content { position: absolute; inset: 0; padding: 0 6%; display: flex; flex-direction: column; justify-content: center; z-index: 3; }
        .editorsChoiceItemLogo { max-width: 400px; max-height: 120px; object-fit: contain; margin-bottom: 25px; }
        .editorsChoiceItemOverview { color: #eee; width: 48%; font-size: 1.15em; line-height: 1.5; margin-bottom: 30px; display: -webkit-box; -webkit-line-clamp: 4; -webkit-box-orient: vertical; overflow: hidden; text-shadow: 2px 2px 4px rgba(0,0,0,0.5); }
        .hss-watch-btn { background: #00a4dc; color: #fff; border: none; padding: 18px 50px; border-radius: 8px; font-weight: bold; font-size: 1.3em; cursor: pointer; width: fit-content; transition: transform 0.2s; }
        .hss-watch-btn:focus { background: #fff; color: #000; transform: scale(1.1); outline: none; }
    ";

            string injection = @"
/* __HSS_NATIVE_FIX_V9__ */
(function () {
    var style = document.createElement('style');
    style.innerText = `" + css.Replace("\r\n", " ").Replace("\"", "\\\"") + @"`;
    document.head.appendChild(style);

    var items = [];
    var idx = 0;

    function getApi() {
        return window.ApiClient || (window.ConnectionManager && window.ConnectionManager.getcurrentitem().apiClient);
    }

    function updateHero() {
        var hero = document.getElementById('hss-hero-container');
        if (!hero || items.length === 0) return;
        
        var item = items[idx];
        var api = getApi();
        var host = api.serverAddress();
        var token = api.accessToken();
        
        // We forceren de URL met api_key voor de <img> tag
        var bgUrl = host + '/Items/' + item.Id + '/Images/Backdrop/0?api_key=' + token;
        var logoUrl = host + '/Items/' + item.Id + '/Images/Logo/0?api_key=' + token;

        console.log('[HSS] Wisselen naar:', item.Name);

        var bgImg = hero.querySelector('.hss-bg-img');
        bgImg.style.opacity = '0';
        
        setTimeout(function() {
            bgImg.src = bgUrl;
            bgImg.onload = function() { bgImg.style.opacity = '0.75'; };
            
            var logo = hero.querySelector('.editorsChoiceItemLogo');
            if (item.ImageTags && item.ImageTags.Logo) {
                logo.src = logoUrl;
                logo.style.display = 'block';
            } else {
                logo.style.display = 'none';
            }
            
            hero.querySelector('.editorsChoiceItemOverview').innerText = item.Overview || '';
            
            // Native Jellyfin/Emby navigatie voor TV
            hero.querySelector('.hss-watch-btn').onclick = function() {
                console.log('[HSS] Navigeren naar:', item.Id);
                if (window.Emby && window.Emby.Page) {
                    window.Emby.Page.showItem(item.Id);
                } else {
                    window.location.hash = '#!/item?id=' + item.Id;
                }
            };
            
            idx = (idx + 1) % items.length;
        }, 500);
    }

    function init() {
        var api = getApi();
        if (!api || !api.getCurrentUserId() || document.getElementById('hss-hero-container')) return;

        var container = document.querySelector('.sections') || document.querySelector('.homeSectionsContainer');
        if (!container) return;

        var html = `
            <div id=""hss-hero-container"" class=""hss-hero-container"">
                <div class=""hss-bg-wrapper""><img class=""hss-bg-img"" crossorigin=""anonymous""></div>
                <div class=""hss-overlay""></div>
                <div class=""hss-content"">
                    <img class=""editorsChoiceItemLogo"" src="""">
                    <p class=""editorsChoiceItemOverview""></p>
                    <button class=""hss-watch-btn"" tabindex=""0"">Watch Now</button>
                </div>
            </div>`;
        
        container.insertAdjacentHTML('afterbegin', html);

        api.getItems(api.getCurrentUserId(), { 
            IncludeItemTypes: 'Movie', 
            SortBy: 'DateCreated', 
            SortOrder: 'Descending', 
            Limit: 6, 
            Recursive: true, 
            Fields: 'Overview,ImageTags' 
        }).then(function(res) {
            items = res.Items || [];
            if (items.length > 0) {
                updateHero();
                setInterval(updateHero, 12000);
            }
        });
    }

    setInterval(init, 3000);
})();
";
            return js + injection;
        }

        public async Task<string?> DownloadAndTranspileAsync(string url, string cacheDir, string relPath)
        {
            try
            {
                Trace.WriteLine($"▶ Downloading plugin JS: {url}");

                string js = await _httpClient.GetStringAsync(url);
                js = await EsbuildHelper.TranspileAsync(js, relPath);

                if (relPath.Contains("EditorsChoice", StringComparison.OrdinalIgnoreCase))
                    js = PatchEditorsChoice(js);

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