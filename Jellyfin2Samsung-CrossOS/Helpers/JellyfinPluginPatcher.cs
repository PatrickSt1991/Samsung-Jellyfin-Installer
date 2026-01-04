using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinPluginPatcher
    {
        private readonly HttpClient _httpClient;
        private readonly JellyfinApiClient _apiClient;
        private readonly PluginManager _pluginManager;

        // Track by the actual src/href string that will appear in HTML
        private readonly HashSet<string> _injectedScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _injectedStyles = new(StringComparer.OrdinalIgnoreCase);

        public JellyfinPluginPatcher(
            HttpClient httpClient,
            JellyfinApiClient apiClient,
            PluginManager pluginManager)
        {
            _httpClient = httpClient;
            _apiClient = apiClient;
            _pluginManager = pluginManager;
        }

        public async Task PatchPluginsAsync(
            PackageWorkspace workspace,
            string serverUrl,
            StringBuilder cssBuilder,
            StringBuilder headJsBuilder,
            StringBuilder bodyJsBuilder)
        {
            string pluginCacheDir = Path.Combine(workspace.Root, "www", "plugin_cache");
            Directory.CreateDirectory(pluginCacheDir);

            string serverHtml = await FetchServerIndexAsync(serverUrl);

            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                await CacheServerAssetsAsync(
                    serverHtml,
                    serverUrl,
                    pluginCacheDir,
                    cssBuilder,
                    bodyJsBuilder);
            }

            await ProcessApiPluginsAsync(
                serverUrl,
                pluginCacheDir,
                headJsBuilder,
                bodyJsBuilder,
                cssBuilder);
        }

        private async Task<string> FetchServerIndexAsync(string serverUrl)
        {
            try
            {
                var url = serverUrl.TrimEnd('/') + "/web/index.html";
                Trace.WriteLine($"▶ Fetching server index.html: {url}");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                return await _httpClient.GetStringAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"⚠ Failed to fetch server index.html: {ex}");
                return string.Empty;
            }
        }

        private async Task CacheServerAssetsAsync(
            string serverHtml,
            string serverUrl,
            string pluginCacheDir,
            StringBuilder cssBuilder,
            StringBuilder jsBuilder)
        {
            Trace.WriteLine("▶ Extracting plugin assets from server index…");

            // --- CSS ---
            var cssMatches = Regex.Matches(
                serverHtml,
                @"<link[^>]+href=[""']([^""']+)[""'][^>]*>",
                RegexOptions.IgnoreCase);

            foreach (Match m in cssMatches)
            {
                string href = m.Groups[1].Value;
                if (!IsLikelyPluginAsset(href)) continue;

                try
                {
                    Uri uri = GetAbsoluteUri(serverUrl, href);
                    string fileName = Path.GetFileName(uri.AbsolutePath);

                    if (!fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string localPath = Path.Combine(pluginCacheDir, fileName);
                    var bytes = await _httpClient.GetByteArrayAsync(uri);
                    await File.WriteAllBytesAsync(localPath, bytes);

                    // Dedup by the actual href we will inject
                    string outHref = $"plugin_cache/{fileName}";
                    AppendStyleOnce(cssBuilder, outHref);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"⚠ Failed to cache plugin CSS '{href}': {ex}");
                }
            }

            // --- JS ---
            var jsMatches = Regex.Matches(
                serverHtml,
                @"<script[^>]+src=[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>",
                RegexOptions.IgnoreCase);

            foreach (Match m in jsMatches)
            {
                string jsUrl = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(jsUrl)) continue;

                string lower = jsUrl.ToLowerInvariant();
                if (IsCoreBundle(lower)) continue;

                bool isPlugin = IsLikelyPluginAsset(jsUrl);

                // API-managed plugins are injected in ProcessApiPluginsAsync.
                if (IsApiManagedPlugin(jsUrl))
                    continue;

                if (lower.Contains("jellyfinenhanced"))
                    continue;

                if (!jsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && isPlugin)
                {
                    try
                    {
                        Uri uri = GetAbsoluteUri(serverUrl, jsUrl);
                        string fileName = Path.GetFileName(uri.AbsolutePath);
                        if (!fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                            fileName += ".js";

                        string localPath = Path.Combine(pluginCacheDir, fileName);
                        string jsContent = await _httpClient.GetStringAsync(uri);
                        jsContent = await EsbuildHelper.TranspileAsync(jsContent, uri.ToString());

                        await File.WriteAllTextAsync(localPath, jsContent);

                        string outSrc = $"plugin_cache/{fileName}";
                        AppendScriptOnce(jsBuilder, $"<script src=\"{outSrc}\"></script>", outSrc);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"⚠ Failed to cache plugin JS '{jsUrl}': {ex}");
                    }
                }

                // External plugin JS
                if (jsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && isPlugin)
                {
                    try
                    {
                        string jsContent = await _httpClient.GetStringAsync(jsUrl);
                        jsContent = await EsbuildHelper.TranspileAsync(jsContent, jsUrl);

                        var wrapped = new StringBuilder();
                        wrapped.AppendLine("window.WaitForApiClient(function(){ try {");
                        wrapped.AppendLine(jsContent);
                        wrapped.AppendLine("} catch(e) { console.error('Plugin Error:', e); } });");

                        string fileName = $"plugin_{Guid.NewGuid():N}.js";
                        string localPath = Path.Combine(pluginCacheDir, fileName);
                        await File.WriteAllTextAsync(localPath, wrapped.ToString());

                        string outSrc = $"plugin_cache/{fileName}";
                        AppendScriptOnce(jsBuilder, $"<script src=\"{outSrc}\"></script>", outSrc);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"⚠ Plugin JS (external) failed: {ex}");
                    }
                }
            }
        }

        private async Task ProcessApiPluginsAsync(
            string serverUrl,
            string pluginCacheDir,
            StringBuilder headJsBuilder,
            StringBuilder bodyJsBuilder,
            StringBuilder cssBuilder)
        {
            var apiPlugins = await _apiClient.GetInstalledPluginsAsync(serverUrl);
            var apiJsBuilder = new StringBuilder();
            string? enhancedMainScript = null;

            foreach (var plugin in apiPlugins)
            {
                Trace.WriteLine($"⚙ Processing plugin: {plugin.Name} ({plugin.Id})");
                var entry = _pluginManager.FindPluginEntry(plugin);
                if (entry == null) continue;

                string name = entry.Name.ToLowerInvariant();
                bool isMediaBar = name.Contains("media bar");
                bool isHomeScreenSections = name.Contains("home screen");

                // --- Explicit server files (Enhanced) ---
                if (entry.ExplicitServerFiles?.Count > 0)
                {
                    enhancedMainScript =
                        entry.ExplicitServerFiles
                             .Find(p => p.EndsWith("/script"));

                    await _pluginManager.DownloadExplicitFilesAsync(
                        serverUrl,
                        pluginCacheDir,
                        entry);
                    continue;
                }

                // --- Fallback JS ---
                foreach (string url in entry.FallbackUrls)
                {
                    string cleanName = Regex.Replace(
                        entry.Name.ToLowerInvariant(),
                        "[^a-z0-9]",
                        "");

                    string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                    string relPath = Path.Combine(cleanName, fileName);

                    string? path = await _pluginManager.DownloadAndTranspileAsync(
                        url,
                        pluginCacheDir,
                        relPath);

                    if (path != null)
                    {
                        string injectedSrc = $"plugin_cache/{cleanName}/{fileName}";
                        if (relPath.Contains("HomeScreenSections", StringComparison.OrdinalIgnoreCase))
                        {
                            AppendScriptOnce(bodyJsBuilder,
                                $"<script defer src=\"{injectedSrc}\"></script>",
                                injectedSrc);
                        }
                        else
                        {
                            AppendScriptOnce(bodyJsBuilder,
                                $"<script src=\"{injectedSrc}\"></script>",
                                injectedSrc);
                        }

                        break;
                    }
                }

                // --- CSS injection ---
                if (isMediaBar)
                {
                    try
                    {
                        string cssUrl =
                            "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-media-bar@main/slideshowpure.css";

                        string fileName = "slideshowpure.css";
                        string localPath = Path.Combine(pluginCacheDir, "mediabar", fileName);

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        var bytes = await _httpClient.GetByteArrayAsync(cssUrl);
                        await File.WriteAllBytesAsync(localPath, bytes);

                        string href = $"plugin_cache/mediabar/{fileName}";
                        AppendStyleOnce(cssBuilder, href);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"⚠ MediaBar CSS failed: {ex}");
                    }
                }

                if (isHomeScreenSections)
                {
                    try
                    {
                        string cssUrl =
                            "https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-home-sections/main/src/Jellyfin.Plugin.HomeScreenSections/Inject/HomeScreenSections.css";

                        string fileName = "HomeScreenSections.css";
                        string localPath = Path.Combine(pluginCacheDir, "homescreensections", fileName);

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        var bytes = await _httpClient.GetByteArrayAsync(cssUrl);
                        await File.WriteAllBytesAsync(localPath, bytes);

                        string href = $"plugin_cache/homescreensections/{fileName}";
                        AppendStyleOnce(cssBuilder, href);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"⚠ HomeScreenSections CSS failed: {ex}");
                    }
                }
            }

            // Enhanced script last (dedup by src)
            if (!string.IsNullOrEmpty(enhancedMainScript))
            {
                string rel = enhancedMainScript.TrimStart('/');
                if (!rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) rel += ".js";

                string outSrc = $"plugin_cache/{rel}";
                AppendScriptOnce(bodyJsBuilder, $"<script src=\"{outSrc}\"></script>", outSrc);
            }

            if (apiJsBuilder.Length > 0)
            {
                // This whole block might be appended multiple times by caller;
                // individual scripts inside are already deduped above.
                bodyJsBuilder.AppendLine(apiJsBuilder.ToString());
            }
        }

        private Uri GetAbsoluteUri(string serverUrl, string rel)
        {
            if (Uri.IsWellFormedUriString(rel, UriKind.Absolute))
                return new Uri(rel);

            return new Uri(new Uri(serverUrl.TrimEnd('/') + "/"), rel.TrimStart('/'));
        }

        private bool IsLikelyPluginAsset(string url)
        {
            string lower = url.ToLowerInvariant();
            return lower.Contains("/plugins/")
                || lower.Contains("javascriptinjector")
                || lower.Contains("filetransformation")
                || lower.Contains("jellyfinenhanced")
                || lower.Contains("mediabar")
                || lower.Contains("kefin")
                || lower.Contains("homescreensections")
                || lower.Contains("editorschoice");
        }

        private bool IsCoreBundle(string lower)
        {
            return lower.Contains("main.")
                || lower.Contains("runtime")
                || lower.Contains("react")
                || lower.Contains("mui")
                || lower.Contains("tanstack");
        }

        private bool IsApiManagedPlugin(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.Contains("homescreensections")
                || lower.Contains("mediabar")
                || lower.Contains("kefin")
                || lower.Contains("editorschoice")
                || lower.Contains("pluginpages");
        }

        private void AppendScriptOnce(StringBuilder js, string scriptTag, string src)
        {
            if (_injectedScripts.Add(src))
            {
                js.AppendLine(scriptTag);
            }
            else
            {
                Trace.WriteLine($"ℹ Script already injected, skipping: {src}");
            }
        }

        private void AppendStyleOnce(StringBuilder css, string href)
        {
            if (_injectedStyles.Add(href))
            {
                css.AppendLine($"<link rel=\"stylesheet\" href=\"{href}\" />");
            }
            else
            {
                Trace.WriteLine($"ℹ CSS already injected, skipping: {href}");
            }
        }
    }
}