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

        // ====================================================================
        //  RAW GITHUB ROOT - USING STABLE TAG v0.4.5
        // ====================================================================
        private const string KefinTweaksRawRoot = "https://raw.githubusercontent.com/ranaldsgift/KefinTweaks/v0.4.5/";

        // ====================================================================
        //  REGEX DEFINITIONS
        // ====================================================================
        private static readonly Regex cdnRegex = new Regex(
            @"(https?:\/\/.+?\/KefinTweaks@latest\/)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public PluginManager(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // ====================================================================
        //  PLUGIN MATRIX
        // ====================================================================
        private static readonly List<PluginMatrixEntry> PluginMatrix = new()
        {
            new PluginMatrixEntry
            {
                Name = "Jellyfin Enhanced",
                IdContains = "jellyfinenhanced",
                ServerPath = null,
                ExplicitServerFiles = new List<string>
                {
                    "/JellyfinEnhanced/script",
                    "/JellyfinEnhanced/js/splashscreen.js",
                    "/JellyfinEnhanced/js/enhanced/config.js",
                    "/JellyfinEnhanced/js/enhanced/themer.js",
                    "/JellyfinEnhanced/js/enhanced/subtitles.js",
                    "/JellyfinEnhanced/js/enhanced/ui.js",
                    "/JellyfinEnhanced/js/enhanced/playback.js",
                    "/JellyfinEnhanced/js/enhanced/features.js",
                    "/JellyfinEnhanced/js/enhanced/events.js",
                    "/JellyfinEnhanced/js/migrate.js",
                    "/JellyfinEnhanced/js/elsewhere.js",
                    "/JellyfinEnhanced/js/pausescreen.js",
                    "/JellyfinEnhanced/js/reviews.js",
                    "/JellyfinEnhanced/js/qualitytags.js",
                    "/JellyfinEnhanced/js/genretags.js",
                    "/JellyfinEnhanced/js/languagetags.js",
                    "/JellyfinEnhanced/js/jellyseerr/api.js",
                    "/JellyfinEnhanced/js/jellyseerr/modal.js",
                    "/JellyfinEnhanced/js/jellyseerr/ui.js",
                    "/JellyfinEnhanced/js/jellyseerr/jellyseerr.js",
                    "/JellyfinEnhanced/js/arr-links.js",
                    "/JellyfinEnhanced/js/arr-tag-links.js",
                    "/JellyfinEnhanced/js/letterboxd-links.js"
                },
                FallbackUrls = new List<string>(),
                RequiresModuleBundle = false,
            },
            new PluginMatrixEntry
            {
                Name = "Media Bar",
                IdContains = "mediabar",
                ServerPath = null,
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-media-bar@main/slideshowpure.js",
                    "https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-media-bar/main/slideshowpure.js"
                },
                // Assuming CSS is handled via FallbackUrls for now, but in JellyfinWebBuilder we'll use a specific URL for Media Bar CSS
                UseBabel = true
            },
            new PluginMatrixEntry
            {
                Name = "KefinTweaks",
                IdContains = "kefin",
                ServerPath = null,
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/ranaldsgift/KefinTweaks@latest/kefinTweaks-plugin.js",
                    "https://raw.githubusercontent.com/ranaldsgift/KefinTweaks/main/kefinTweaks-plugin.js"
                },
                UseBabel = true
            }
        };

        public PluginMatrixEntry? FindPluginEntry(JellyfinPluginInfo plugin)
        {
            string name = (plugin.Name ?? "").ToLowerInvariant();
            string id = (plugin.Id ?? "").ToLowerInvariant();

            return PluginMatrix.FirstOrDefault(entry =>
            {
                bool nameMatch = !string.IsNullOrEmpty(entry.Name) &&
                                 name.Contains(entry.Name.ToLowerInvariant());

                bool idMatch = !string.IsNullOrEmpty(entry.IdContains) &&
                               id.Contains(entry.IdContains.ToLowerInvariant());

                return nameMatch || idMatch;
            });
        }

        public async Task DownloadExplicitFilesAsync(
                    string serverUrl,
                    string pluginCacheDir,
                    PluginMatrixEntry entry)
        {
            if (entry?.ExplicitServerFiles == null || entry.ExplicitServerFiles.Count == 0)
                return;

            Debug.WriteLine("▶ Downloading explicit Enhanced JS modules...");

            foreach (var rel in entry.ExplicitServerFiles)
            {
                try
                {
                    string url = serverUrl.TrimEnd('/') + rel;
                    Debug.WriteLine($"   → Fetch Enhanced JS: {url}");

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
                    Debug.WriteLine($"      ✓ Saved {logPath}");
                    if (rel.Equals("/JellyfinEnhanced/script", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("      🔧 Patching Enhanced main script (script.js)...");
                        await PatchEnhancedMainScript(outPath);
                    }

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"      ⚠ Failed Enhanced JS '{rel}': {ex.Message}");
                }
            }
        }

        public async Task<string?> DownloadAndTranspileAsync(string url, string cacheDir, string relPath)
        {
            try
            {
                Debug.WriteLine($"  → Downloading: {url}");
                string js = await _httpClient.GetStringAsync(url);
                js = await EsbuildHelper.TranspileAsync(js, relPath);

                string localPath = Path.Combine(cacheDir, relPath);
                string? dir = Path.GetDirectoryName(localPath);
                if (dir != null) Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(localPath, js, Encoding.UTF8);
                Debug.WriteLine($"      ✓ Saved {relPath} → {localPath}");

                return localPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed download for {relPath}: {ex.Message}");
                return null;
            }
        }

        public async Task PatchJavaScriptInjectorPublicJsAsync(string pluginCacheDir)
        {
            try
            {
                string publicJsPath = Path.Combine(pluginCacheDir, "public.js");
                if (!File.Exists(publicJsPath))
                {
                    Debug.WriteLine("▶ JavaScript Injector: public.js not found, skipping patch.");
                    return;
                }

                string js = await File.ReadAllTextAsync(publicJsPath, Encoding.UTF8);

                // 1. Detect the original CDN root from public.js (needed for replacement)
                var match = cdnRegex.Match(js);
                if (!match.Success)
                {
                    Debug.WriteLine("▶ JavaScript Injector: No KefinTweaks CDN root found in public.js, skipping patch.");
                    return;
                }

                string cdnRoot = match.Value; // includes trailing slash
                Debug.WriteLine($"▶ JavaScript Injector: CDN root detected: {cdnRoot}");

                // Our local base directory inside the WGT
                string localRoot = "plugin_cache/kefinTweaks/";

                // Make sure kefinTweaks folder exists
                Directory.CreateDirectory(Path.Combine(pluginCacheDir, "kefinTweaks"));

                // 2. Download + transpile the main KefinTweaks plugin (using reliable Raw Root)
                string kefinTweaksPluginUrl = KefinTweaksRawRoot + "kefinTweaks-plugin.js";
                string kefinTweaksPluginRelPath = Path.Combine("kefinTweaks", "kefinTweaks-plugin.js");
                string? localKefinPath = await DownloadAndTranspileAsync(
                    kefinTweaksPluginUrl,
                    pluginCacheDir,
                    kefinTweaksPluginRelPath);

                if (localKefinPath == null)
                {
                    Debug.WriteLine("⚠ JavaScript Injector: Failed to download/transpile kefinTweaks-plugin.js");
                }

                // 3. Download + transpile injector.js itself (using reliable Raw Root)
                string injectorUrl = KefinTweaksRawRoot + "injector.js";
                string injectorRelPath = Path.Combine("kefinTweaks", "injector.js");
                string? localInjectorPath = await DownloadAndTranspileAsync(
                    injectorUrl,
                    pluginCacheDir,
                    injectorRelPath);

                if (localInjectorPath == null)
                {
                    Debug.WriteLine("⚠ JavaScript Injector: Failed to download/transpile injector.js");
                }
                else
                {
                    // 4. Download + transpile **all dependent JS modules** declared in injector.js
                    await ProcessKefinTweaksModulesAsync(pluginCacheDir, KefinTweaksRawRoot);

                    // 5. Download **all dependent CSS files**
                    await ProcessKefinTweaksCssAsync(pluginCacheDir, KefinTweaksRawRoot);
                }

                // 6. Rewrite **all** usages of the original CDN root inside public.js
                js = js.Replace(cdnRoot, localRoot);

                await File.WriteAllTextAsync(publicJsPath, js, Encoding.UTF8);
                Debug.WriteLine("      ✓ JavaScript Injector: patched public.js to use plugin_cache/kefinTweaks/");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ JavaScript Injector: error while patching public.js: {ex.Message}");
            }
        }

        private async Task ProcessKefinTweaksModulesAsync(string pluginCacheDir, string downloadRoot)
        {
            try
            {
                string injectorPath = Path.Combine(pluginCacheDir, "kefinTweaks", "injector.js");
                if (!File.Exists(injectorPath))
                {
                    Debug.WriteLine("▶ KefinTweaks: injector.js not found in plugin_cache, skipping module prefetch.");
                    return;
                }

                string injectorSource = await File.ReadAllTextAsync(injectorPath, Encoding.UTF8);

                // Grab all `script: "something.js"` entries from SCRIPT_DEFINITIONS
                var scriptRegex = new Regex(@"script\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                var matches = scriptRegex.Matches(injectorSource);

                // ... (rest of ProcessKefinTweaksModulesAsync remains unchanged) ...

                if (matches.Count == 0)
                {
                    Debug.WriteLine("▶ KefinTweaks: no SCRIPT_DEFINITIONS scripts found in injector.js, nothing to prefetch.");
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
                        Debug.WriteLine($"      ⚠ KefinTweaks: Skipping non-JS module or malformed entry: {scriptName}");
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

                Debug.WriteLine("      ✓ KefinTweaks: module scripts downloaded & transpiled into plugin_cache/kefinTweaks/");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ KefinTweaks: error while processing modules: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads all CSS files for KefinTweaks and places them in the cache.
        /// </summary>
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
                "optional/ElegantFin/elegantFin.css"
            };

            Debug.WriteLine("▶ KefinTweaks: Pre-fetching CSS skins...");

            foreach (var fileName in cssFiles)
            {
                try
                {
                    // The CSS files are in the 'skins/' directory of the repo
                    string repoPath = $"skins/{fileName}";
                    string url = downloadRoot + repoPath;

                    // The local path must preserve the 'optional/ElegantFin/' structure
                    string localRelPath = Path.Combine("kefinTweaks", repoPath.Replace('/', Path.DirectorySeparatorChar));
                    string localFullPath = Path.Combine(pluginCacheDir, localRelPath);

                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(localFullPath)!);

                    // Download CSS directly (no transpile needed)
                    var cssBytes = await _httpClient.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(localFullPath, cssBytes);

                    Debug.WriteLine($"      ✓ Downloaded CSS skin: {fileName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"      ⚠ Failed to fetch KefinTweaks CSS '{fileName}': {ex.Message}");
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
    }
}