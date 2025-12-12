using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinWebBuilder
    {
        private readonly HttpClient _httpClient;
        private readonly JellyfinApiClient _apiClient;
        private readonly PluginManager _pluginManager;

        public JellyfinWebBuilder(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _apiClient = new JellyfinApiClient(httpClient);
            _pluginManager = new PluginManager(httpClient);
        }

        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath, string[] userIds)
        {
            string? tempDir = null;
            string? tempPackage = null;

            try
            {
                var baseDir = Path.GetDirectoryName(packagePath)
                             ?? throw new InvalidOperationException("Invalid package path");

                tempDir = Path.Combine(baseDir, $"JellyTemp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(packagePath, tempDir);

                if (AppSettings.Default.UseServerScripts)
                {
                    await PatchServerSideIndexHtmlAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await AddOrUpdateCspAsync(tempDir, AppSettings.Default.JellyfinIP);
                }

                if (AppSettings.Default.ConfigUpdateMode.Contains("Server") ||
                    AppSettings.Default.ConfigUpdateMode.Contains("All"))
                {
                    await UpdateMultiServerConfig(tempDir);
                }

                if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                    AppSettings.Default.ConfigUpdateMode.Contains("All"))
                {
                    await InjectUserSettingsScriptAsync(tempDir, userIds);
                }

                tempPackage = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(packagePath)}_mod.wgt");
                if (File.Exists(tempPackage)) File.Delete(tempPackage);
                ZipFile.CreateFromDirectory(tempDir, tempPackage);

                File.Delete(packagePath);
                File.Move(tempPackage, packagePath);

                return InstallResult.SuccessResult();
            }
            catch (Exception ex)
            {
                return InstallResult.FailureResult($"Error modifying Jellyfin package: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                    if (tempPackage != null && File.Exists(tempPackage))
                        File.Delete(tempPackage);
                }
                catch { }
            }
        }

        // ====================================================================
        //  CORE HTML MODIFICATION LOGIC
        // ====================================================================

        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string serverIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";

            if (!File.Exists(localIndexPath))
                return false;

            Debug.WriteLine("▶ Using LOCAL Tizen index.html as base.");
            string localHtml = await File.ReadAllTextAsync(localIndexPath);

            // Fetch server HTML
            string serverHtml = "";
            try
            {
                Debug.WriteLine($"▶ Fetching SERVER index.html from {serverIndexUrl}");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                serverHtml = await _httpClient.GetStringAsync(serverIndexUrl, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed to fetch server index.html: {ex.Message}");
                serverHtml = "";
            }

            // Extract plugin DOM from server HTML
            ExtractedDomBlocks extracted = new ExtractedDomBlocks();
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                extracted = ExtractPluginDomBlocks(serverHtml);
            }

            // Ensure <base href=".">
            if (localHtml.Contains("<base", StringComparison.OrdinalIgnoreCase))
            {
                localHtml = Regex.Replace(localHtml, @"<base[^>]+>", @"<base href=""."">", RegexOptions.IgnoreCase);
            }
            else
            {
                localHtml = localHtml.Replace("<head>", "<head><base href=\".\">", StringComparison.OrdinalIgnoreCase);
            }

            // Rewrite JS/CSS to local form
            localHtml = RewriteScriptAndCssPaths(localHtml);

            // Prepare cache dir
            string pluginCacheDir = Path.Combine(tempDirectory, "www", "plugin_cache");
            Directory.CreateDirectory(pluginCacheDir);

            var pluginCssBuilder = new StringBuilder();
            var pluginJsBuilder = new StringBuilder();

            // Extract assets from server index (CSS/JS)
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                await CacheServerAssetsAsync(serverHtml, serverUrl, pluginCacheDir, pluginCssBuilder, pluginJsBuilder);
            }

            // Handle API Plugins (Enhanced, Kefin, etc.)
            await ProcessApiPluginsAsync(serverUrl, pluginCacheDir, pluginJsBuilder, pluginCssBuilder); // NOW PASSES CSS BUILDER

            // Inject Cached CSS + JS into local HTML
            if (pluginCssBuilder.Length > 0)
            {
                localHtml = Regex.Replace(localHtml, "<head>", "<head>\n" + pluginCssBuilder, RegexOptions.IgnoreCase);
            }
            if (pluginJsBuilder.Length > 0)
            {
                localHtml = localHtml.Replace("</body>", pluginJsBuilder + "\n</body>");
            }

            // Merge plugin DOM from server
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                if (extracted.HeadInjectBlocks.Any()) localHtml = InsertIntoHead(localHtml, extracted.HeadInjectBlocks);
                if (extracted.BodyInjectBlocks.Any()) localHtml = InsertIntoBodyTop(localHtml, extracted.BodyInjectBlocks);
            }

            // Inject Bootloader (Tizen specific + Enhanced patching)
            string bootloader = GenerateBootloaderScript(serverUrl);
            localHtml = Regex.Replace(localHtml, "<head>", "<head>\n" + bootloader, RegexOptions.IgnoreCase);

            // Clean + Add CSP
            localHtml = Regex.Replace(localHtml, @"<meta[^>]*Content-Security-Policy[^>]*>", "", RegexOptions.IgnoreCase);
            localHtml = localHtml.Replace("</head>", "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;\">\n</head>");

            // Strip server-side plugin JS tags (keep only plugin_cache & core)
            localHtml = StripServerPluginTags(localHtml);
            localHtml = EnsurePublicJsIsLast(localHtml);
            await File.WriteAllTextAsync(localIndexPath, localHtml);
            return true;
        }

        private async Task CacheServerAssetsAsync(string serverHtml, string serverUrl, string pluginCacheDir, StringBuilder cssBuilder, StringBuilder jsBuilder)
        {
            Debug.WriteLine("▶ Extracting plugin assets from server index…");

            // --- CSS ---
            var cssMatches = Regex.Matches(serverHtml, @"<link[^>]+href=[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
            foreach (Match m in cssMatches)
            {
                string href = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(href) || !IsLikelyPluginAsset(href)) continue;

                try
                {
                    Uri uri = GetAbsoluteUri(serverUrl, href);
                    string fileName = Path.GetFileName(uri.AbsolutePath);
                    if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) continue;

                    string localPath = Path.Combine(pluginCacheDir, fileName);
                    var bytes = await _httpClient.GetByteArrayAsync(uri);
                    await File.WriteAllBytesAsync(localPath, bytes);

                    cssBuilder.AppendLine($"<link rel=\"stylesheet\" href=\"plugin_cache/{fileName}\" />");
                }
                catch (Exception ex) { Debug.WriteLine($"⚠ Failed to cache plugin CSS '{href}': {ex.Message}"); }
            }

            // --- JS ---
            var jsMatches = Regex.Matches(serverHtml, @"<script[^>]+src=[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>", RegexOptions.IgnoreCase);
            foreach (Match m in jsMatches)
            {
                string jsUrl = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(jsUrl)) continue;

                string lower = jsUrl.ToLowerInvariant();
                if (IsCoreBundle(lower)) continue;

                bool isPlugin = IsLikelyPluginAsset(jsUrl);
                if (isPlugin && lower.Contains("jellyfinenhanced")) continue; // Skipped, handled explicitly

                // Relative /web/ JS
                if (!jsUrl.StartsWith("http") && !jsUrl.StartsWith("//") && isPlugin)
                {
                    try
                    {
                        Uri uri = GetAbsoluteUri(serverUrl, jsUrl);
                        string fileName = Path.GetFileName(uri.AbsolutePath);
                        if (!fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) fileName += ".js";

                        string localPath = Path.Combine(pluginCacheDir, fileName);
                        string jsContent = await _httpClient.GetStringAsync(uri);
                        jsContent = await EsbuildHelper.TranspileAsync(jsContent, uri.ToString());
                        // DEBUG PATCH: improve injector error logging (non-functional change)
                        if (fileName.Equals("injector.js", StringComparison.OrdinalIgnoreCase))
                        {
                            jsContent = jsContent.Replace(
                                "console.error(\"[KefinTweaks Injector] Error during initialization:\", error);",
                                "console.error(\"[KefinTweaks Injector] Error during initialization:\", error instanceof Error ? error.stack : JSON.stringify(error));"
                            );
                        }
                        // TV FIX: skinManager uses `throw {}` as an abort guard — neutralize it on TV
                        if (fileName.Equals("skinManager.js", StringComparison.OrdinalIgnoreCase))
                        {
                            jsContent = jsContent.Replace(
                                "throw {};",
                                "return;"
                            );
                        }


                        await File.WriteAllTextAsync(localPath, jsContent);

                        // Patch JS Injector public.js if needed
                        if (fileName.Equals("public.js", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("javascriptinjector", StringComparison.OrdinalIgnoreCase))
                        {
                            await _pluginManager.PatchJavaScriptInjectorPublicJsAsync(pluginCacheDir);
                        }

                        jsBuilder.AppendLine($"<script src=\"plugin_cache/{fileName}\"></script>");
                    }
                    catch (Exception ex) { Debug.WriteLine($"⚠ Failed to cache plugin JS '{jsUrl}': {ex.Message}"); }
                    continue;
                }

                // External/CDN JS
                if ((jsUrl.StartsWith("http") || jsUrl.StartsWith("//")) && (isPlugin || lower.Contains("cdn.jsdelivr") || lower.Contains("unpkg.com")))
                {
                    string normalized = jsUrl.StartsWith("//") ? "http:" + jsUrl : jsUrl;
                    try
                    {
                        string jsContent = await _httpClient.GetStringAsync(normalized);
                        jsContent = await EsbuildHelper.TranspileAsync(jsContent, normalized);

                        var wrapped = new StringBuilder();
                        wrapped.AppendLine("window.WaitForApiClient(function(){ try {");
                        wrapped.AppendLine(jsContent);
                        wrapped.AppendLine("} catch(e) { console.error('Plugin Error:', e); } });");

                        string fileName = $"plugin_{Guid.NewGuid():N}.js";
                        await File.WriteAllTextAsync(Path.Combine(pluginCacheDir, fileName), wrapped.ToString());
                        jsBuilder.AppendLine($@"<script src=""plugin_cache/{fileName}""></script>");
                    }
                    catch (Exception ex) { Debug.WriteLine($"⚠ Plugin JS (external) failed: {ex.Message}"); }
                }
            }
        }

        // UPDATED SIGNATURE TO INCLUDE CSS BUILDER
        private async Task ProcessApiPluginsAsync(string serverUrl, string pluginCacheDir, StringBuilder jsBuilder, StringBuilder cssBuilder)
        {
            var apiPlugins = await _apiClient.GetInstalledPluginsAsync(serverUrl);
            var apiJsBuilder = new StringBuilder();
            string? enhancedMainScript = null;

            foreach (var plugin in apiPlugins)
            {
                var entry = _pluginManager.FindPluginEntry(plugin);
                if (entry == null) continue;

                bool isKefin = entry.IdContains.Equals("kefin", StringComparison.OrdinalIgnoreCase);
                bool isMediaBar = entry.IdContains.Equals("mediabar", StringComparison.OrdinalIgnoreCase);

                // ---------------------------------------------------------
                // 1. Explicit Server Files (Jellyfin Enhanced)
                // ---------------------------------------------------------
                if (entry.ExplicitServerFiles != null && entry.ExplicitServerFiles.Any())
                {
                    enhancedMainScript = entry.ExplicitServerFiles.FirstOrDefault(rel => rel.EndsWith("/script", StringComparison.OrdinalIgnoreCase));
                    await _pluginManager.DownloadExplicitFilesAsync(serverUrl, pluginCacheDir, entry);
                    continue;
                }

                // ---------------------------------------------------------
                // 2. Fallback URLs (JS Injection)
                // ---------------------------------------------------------
                if (entry.FallbackUrls.Any())
                {
                    if (isKefin)
                    {
                        // Kefin JS is already handled by PatchJavaScriptInjectorPublicJsAsync, 
                        // but we inject the main kefinTweaks-plugin.js if it was downloaded here
                        string? path = await _pluginManager.DownloadAndTranspileAsync(
                            entry.FallbackUrls.First(),
                            pluginCacheDir,
                            Path.Combine("kefinTweaks", "kefinTweaks-plugin.js")
                        );

                        if (path != null)
                        {
                            apiJsBuilder.AppendLine($"<script src=\"plugin_cache/kefinTweaks/kefinTweaks-plugin.js\"></script>");
                        }
                    }
                    // Generic Handler for other matrix plugins (e.g., Media Bar JS)
                    else if (isMediaBar)
                    {
                        foreach (string url in entry.FallbackUrls)
                        {
                            try
                            {
                                string cleanId = Regex.Replace(entry.IdContains, "[^a-zA-Z0-9]", "");
                                string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                                string relPath = Path.Combine(cleanId, fileName);

                                string? path = await _pluginManager.DownloadAndTranspileAsync(url, pluginCacheDir, relPath);

                                if (path != null)
                                {
                                    string webPath = $"plugin_cache/{cleanId}/{fileName}";
                                    apiJsBuilder.AppendLine($"<script src=\"{webPath}\"></script>");
                                    Debug.WriteLine($"      ✓ Injected Generic Plugin JS: {entry.Name} -> {webPath}");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"⚠ Failed to process fallback JS URL for {entry.Name}: {ex.Message}");
                            }
                        }
                    }
                }

                // ---------------------------------------------------------
                // 3. CSS Injection Logic (Kefin & Media Bar)
                // ---------------------------------------------------------

                // KefinTweaks CSS: Scan the pre-downloaded skins directory
                if (isKefin)
                {
                    string kefinCssDir = Path.Combine(pluginCacheDir, "kefinTweaks", "skins");
                    if (Directory.Exists(kefinCssDir))
                    {
                        // Get all CSS files in the skins directory and its subdirectories
                        var cssFiles = Directory.GetFiles(kefinCssDir, "*.css", SearchOption.AllDirectories);

                        foreach (var cssPath in cssFiles)
                        {
                            // Calculate the relative path from the plugin_cache directory
                            string relPath = cssPath.Replace(pluginCacheDir + Path.DirectorySeparatorChar, "plugin_cache/");
                            cssBuilder.AppendLine($"<link rel=\"stylesheet\" href=\"{relPath}\" />");
                        }
                        Debug.WriteLine($"      ✓ Injected {cssFiles.Length} KefinTweaks CSS skins.");
                    }
                }

                // Media Bar CSS: Explicitly download and inject its CSS
                if (isMediaBar)
                {
                    try
                    {
                        string cssUrl = "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-media-bar@main/slideshowpure.css";
                        string fileName = "slideshowpure.css";
                        string cleanId = "mediabar"; // matching the JS folder above
                        string localPath = Path.Combine(pluginCacheDir, cleanId, fileName);

                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                        // Download CSS directly (no transpile needed)
                        Debug.WriteLine("      → Fetching Media Bar CSS...");
                        var cssBytes = await _httpClient.GetByteArrayAsync(cssUrl);
                        await File.WriteAllBytesAsync(localPath, cssBytes);

                        cssBuilder.AppendLine($"<link rel=\"stylesheet\" href=\"plugin_cache/{cleanId}/{fileName}\" />");
                        Debug.WriteLine($"      ✓ Injected Media Bar CSS");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠ Failed to fetch Media Bar CSS: {ex.Message}");
                    }
                }
            }

            // Enhanced script injection (always last preferred)
            if (!string.IsNullOrEmpty(enhancedMainScript))
            {
                string relPath = enhancedMainScript.TrimStart('/');
                if (!relPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) relPath += ".js";
                jsBuilder.AppendLine($"<script src=\"plugin_cache/{relPath}\"></script>");
            }

            // Append all other accumulated plugin scripts
            if (apiJsBuilder.Length > 0)
            {
                jsBuilder.AppendLine(apiJsBuilder.ToString());
            }
        }

        // ====================================================================
        //  HELPER METHODS & UTILS
        // ====================================================================

        public static async Task UpdateMultiServerConfig(string tempDirectory)
        {
            string configPath = Path.Combine(tempDirectory, "www", "config.json");

            if (!File.Exists(configPath))
            {
                var defaultConfig = new JsonObject { ["servers"] = new JsonArray(), ["multiserver"] = false };
                await File.WriteAllTextAsync(configPath, defaultConfig.ToJsonString());
            }

            string jsonText = await File.ReadAllTextAsync(configPath);
            var config = JsonNode.Parse(jsonText) ?? new JsonObject();

            config["multiserver"] = false;
            string serverUrl = AppSettings.Default.JellyfinIP.TrimEnd('/');
            config["servers"] = new JsonArray { JsonValue.Create(serverUrl) };

            await File.WriteAllTextAsync(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public async Task AddOrUpdateCspAsync(string tempDirectory, string serverUrl)
        {
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string html = await File.ReadAllTextAsync(indexPath);
            html = Regex.Replace(html, @"<meta[^>]*Content-Security-Policy[^>]*>", "", RegexOptions.IgnoreCase);
            string newCsp = @"<meta http-equiv=""Content-Security-Policy"" content=""default-src * 'unsafe-inline' 'unsafe-eval' blob: data: ws: http: https:;"">";
            html = Regex.Replace(html, @"</head>", $"{newCsp}\n</head>", RegexOptions.IgnoreCase);
            await File.WriteAllTextAsync(indexPath, html);
        }

        private async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string html = await File.ReadAllTextAsync(indexPath);
            var sb = new StringBuilder();
            sb.AppendLine("<script>");
            sb.AppendLine("window.JellyfinUserSettings = window.JellyfinUserSettings || {};");
            sb.AppendLine("window.JellyfinUserSettings.SelectedUsers = [");
            var nonEmpty = userIds.Where(u => !string.IsNullOrWhiteSpace(u)).ToArray();
            for (int i = 0; i < nonEmpty.Length; i++)
            {
                string comma = (i < nonEmpty.Length - 1) ? "," : "";
                sb.AppendLine($"    \"{nonEmpty[i]}\"{comma}");
            }
            sb.AppendLine("];");
            sb.AppendLine("</script>");

            html = html.Replace("</body>", sb.ToString() + "\n</body>");
            await File.WriteAllTextAsync(indexPath, html);
        }

        private ExtractedDomBlocks ExtractPluginDomBlocks(string serverHtml)
        {
            var result = new ExtractedDomBlocks();
            // <script id="...">
            var scriptPattern = new Regex(@"<script[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>", RegexOptions.IgnoreCase);
            foreach (Match m in scriptPattern.Matches(serverHtml))
                if (IsPluginIdentifier(m.Groups[1].Value)) result.HeadInjectBlocks.Add(m.Value);

            // <template id="...">
            var templatePattern = new Regex(@"<template[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/template>", RegexOptions.IgnoreCase);
            foreach (Match m in templatePattern.Matches(serverHtml))
                if (IsPluginIdentifier(m.Groups[1].Value)) result.BodyInjectBlocks.Add(m.Value);

            // <style id="...">
            var stylePattern = new Regex(@"<style[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/style>", RegexOptions.IgnoreCase);
            foreach (Match m in stylePattern.Matches(serverHtml))
                if (IsPluginIdentifier(m.Groups[1].Value)) result.HeadInjectBlocks.Add(m.Value);

            // Custom Tabs-ish <div class="...custom...tab...">
            var customTabsDiv = new Regex(@"<div[^>]+class\s*=\s*[""'][^""']*custom[^""']*tab[^""']*[""'][^>]*>[\s\S]*?<\/div>", RegexOptions.IgnoreCase);
            foreach (Match m in customTabsDiv.Matches(serverHtml)) result.BodyInjectBlocks.Add(m.Value);

            return result;
        }

        private string InsertIntoHead(string localHtml, IEnumerable<string> blocks)
        {
            if (!blocks.Any()) return localHtml;
            return Regex.Replace(localHtml, @"</head>", string.Join("\n", blocks) + "\n</head>", RegexOptions.IgnoreCase);
        }

        private string InsertIntoBodyTop(string localHtml, IEnumerable<string> blocks)
        {
            if (!blocks.Any()) return localHtml;
            return Regex.Replace(localHtml, @"<body[^>]*>", match => match.Value + "\n" + string.Join("\n", blocks) + "\n", RegexOptions.IgnoreCase);
        }

        private string RewriteScriptAndCssPaths(string html)
        {
            // Rewrites src/href to be local-friendly and strips server-side plugin injections
            string Localize(Match m)
            {
                string prefix = m.Groups[1].Value;
                string url = m.Groups[2].Value;
                string suffix = m.Groups[3].Value;
                if (url.Contains("tizen.js")) return m.Value;
                if (url.Contains("/web/")) return $"{prefix}{Path.GetFileName(url)}{suffix}";
                return m.Value;
            }

            html = Regex.Replace(html, @"(src=[""'])([^""']+\.js[^""']*)([""'])", Localize, RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"(href=[""'])([^""']+\.css[^""']*)([""'])", Localize, RegexOptions.IgnoreCase);

            string pluginPattern = @"\/(JellyfinEnhanced|JavaScriptInjector|CustomTabs|FileTransformation)[^""']+";
            html = Regex.Replace(html, $"<script[^>]+src=[\"']{pluginPattern}[\"'][^>]*><\\/script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, $"<link[^>]+href=[\"']{pluginPattern}[\"'][^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<script>[\s\S]*?JellyfinEnhanced[\s\S]*?<\/script>", "", RegexOptions.IgnoreCase);

            return html;
        }

        private string GenerateBootloaderScript(string serverUrl)
        {
            var boot = new StringBuilder();
            boot.AppendLine("<script src=\"tizen.js\"></script>");
            boot.AppendLine("<script>");
            boot.AppendLine($"window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            boot.AppendLine("window.appConfig = window.appConfig || {};");
            boot.AppendLine($"window.appConfig.servers = [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}];");

            // Enhanced URL Rewrite Logic
            boot.AppendLine("window.__EnhancedRewrite = function(path) {");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (typeof path !== 'string') return path;");
            boot.AppendLine("        if (path === '/JellyfinEnhanced/script') return 'plugin_cache/JellyfinEnhanced/script.js';");
            boot.AppendLine("        if (path.startsWith('/JellyfinEnhanced/js/')) return 'plugin_cache/JellyfinEnhanced/js/' + path.substring('/JellyfinEnhanced/js/'.length);");
            boot.AppendLine("        if (path.startsWith('/JellyfinEnhanced/') && !path.includes('/locales/')) return 'plugin_cache/JellyfinEnhanced/' + path.substring('/JellyfinEnhanced/'.length);");
            boot.AppendLine("    } catch(e) { console.error('Enhanced rewrite failed', e); }");
            boot.AppendLine("    return path;");
            boot.AppendLine("};");

            // Patch fetch()
            boot.AppendLine("(function(){");
            boot.AppendLine("  const _fetch = window.fetch;");
            boot.AppendLine("  window.fetch = function(resource, init) {");
            boot.AppendLine("    try { if (typeof resource === 'string') resource = window.__EnhancedRewrite(resource); }");
            boot.AppendLine("    catch(e) { console.error('Enhanced fetch rewrite failed', e); }");
            boot.AppendLine("    return _fetch(resource, init);");
            boot.AppendLine("  };");
            boot.AppendLine("})();");

            // Patch ApiClient.getUrl()
            boot.AppendLine("window.__patchEnhancedLoader = function(){");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (!window.ApiClient || window.__EnhancedLoaderPatched) return;");
            boot.AppendLine("        window.__EnhancedLoaderPatched = true;");
            boot.AppendLine("        var origGetUrl = window.ApiClient.getUrl && window.ApiClient.getUrl.bind(window.ApiClient);");
            boot.AppendLine("        if (!origGetUrl) return;");
            boot.AppendLine("        window.ApiClient.getUrl = function(path){");
            boot.AppendLine("            try { path = window.__EnhancedRewrite(path); } catch (e) {}");
            boot.AppendLine("            return origGetUrl(path);");
            boot.AppendLine("        };");
            boot.AppendLine("    } catch (e) { console.error('Failed to patch ApiClient.getUrl', e); }");
            boot.AppendLine("};");

            // WaitForApiClient
            boot.AppendLine("window.WaitForApiClient = function(cb){");
            boot.AppendLine("  let t = setInterval(()=>{");
            boot.AppendLine("    if (window.ApiClient || (window.appRouter && window.appRouter.isReady)) {");
            boot.AppendLine("      clearInterval(t);");
            boot.AppendLine("      try { if (window.__patchEnhancedLoader) window.__patchEnhancedLoader(); } catch(e) {}");
            boot.AppendLine("      cb();");
            boot.AppendLine("    }");
            boot.AppendLine("  }, 250);");
            boot.AppendLine("};");
            boot.AppendLine("</script>");

            if (AppSettings.Default.EnableDevLogs)
            {
                boot.AppendLine("<script>");
                boot.AppendLine("(function(){");
                boot.AppendLine($"var ws=new WebSocket('ws://{AppSettings.Default.LocalIp}:5001');");
                boot.AppendLine("var send=(t,d)=>{try{ws.send(JSON.stringify({type:t,data:d}))}catch(e){}};");
                boot.AppendLine("console.log=(...a)=>send('log',a); console.error=(...a)=>send('error',a);");
                boot.AppendLine("window.onerror=(m,s,l,c)=>send('error',[m,s,l,c]);");
                boot.AppendLine("})();");
                boot.AppendLine("</script>");
            }

            return boot.ToString();
        }

        private string StripServerPluginTags(string html)
        {
            return Regex.Replace(html, @"<script[^>]+src=[""']([^""']+)[""'][^>]*>\s*</script>", m =>
            {
                string src = m.Groups[1].Value.ToLowerInvariant();
                if (src.Contains("plugin_cache/")) return m.Value; // Keep our cache

                bool isServerPlugin = src.Contains("/plugins/") || src.Contains("javascriptinjector") ||
                                      src.Contains("jellyfinenhanced") || src.Contains("mediabar") ||
                                      src.Contains("kefin") || src.Contains("filetransformation");
                return isServerPlugin ? string.Empty : m.Value;
            }, RegexOptions.IgnoreCase);
        }

        private Uri GetAbsoluteUri(string serverUrl, string relativeOrAbsolute)
        {
            if (Uri.IsWellFormedUriString(relativeOrAbsolute, UriKind.Absolute))
                return new Uri(relativeOrAbsolute);
            return new Uri(new Uri(serverUrl.TrimEnd('/') + "/"), relativeOrAbsolute.TrimStart('/'));
        }

        private bool IsLikelyPluginAsset(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();
            return lower.Contains("/plugins/") || lower.Contains("javascriptinjector") ||
                   lower.Contains("filetransformation") || lower.Contains("jellyfinenhanced") ||
                   lower.Contains("mediabar") || lower.Contains("customtabs") || lower.Contains("kefin");
        }

        private bool IsPluginIdentifier(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            id = id.ToLowerInvariant();
            return id.Contains("custom") || id.Contains("enhanced") || id.Contains("inject") ||
                   id.Contains("mediabar") || id.Contains("plugin") || id.Contains("tabs");
        }

        private bool IsCoreBundle(string lower)
        {
            return lower.Contains("main.") || lower.Contains("runtime") ||
                   lower.Contains("jellyfin-apiclient") || lower.Contains("react") ||
                   lower.Contains("mui") || lower.Contains("tanstack");
        }
        private string EnsurePublicJsIsLast(string html)
        {
            const string publicJs = "<script src=\"plugin_cache/public.js\"></script>";

            int publicIndex = html.IndexOf(publicJs, StringComparison.OrdinalIgnoreCase);
            if (publicIndex == -1)
                return html; // public.js not present, nothing to do

            // Remove public.js from wherever it currently is
            html = html.Remove(publicIndex, publicJs.Length);

            // Insert it just before </body>
            int bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose == -1)
                return html + publicJs; // fallback

            return html.Insert(bodyClose, publicJs + "\n");
        }
    }
}