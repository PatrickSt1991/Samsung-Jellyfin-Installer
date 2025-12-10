using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinHelper
    {
        private readonly HttpClient _httpClient;

        public JellyfinHelper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // ====================================================================
        //  ESBUILD HELPER (build-time transpilation, no runtime Babel)
        // ====================================================================

        private static string? GetEsbuildPath()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string relPath;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "win-x64", "esbuild.exe");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "linux-x64", "esbuild");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    relPath = Path.Combine(AppSettings.EsbuildPath, "osx-universal", "esbuild");
                }
                else
                {
                    return null;
                }

                string fullPath = Path.Combine(baseDir, relPath);
                return File.Exists(fullPath) ? fullPath : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Transpiles ES2015+ JavaScript to ES5 using esbuild.
        /// If esbuild is missing or fails, returns the original JS.
        /// </summary>
        private static async Task<string> TranspileWithEsbuildAsync(string js, string? relPathForLog = null)
        {
            try
            {
                string? esbuildPath = GetEsbuildPath();
                Debug.WriteLine(esbuildPath);
                Debug.WriteLine(AppSettings.EsbuildPath);
                if (string.IsNullOrEmpty(esbuildPath))
                {
                    Debug.WriteLine($"⚠ esbuild binary not found, skipping transpile for {relPathForLog ?? "unknown"}");
                    return js;
                }

                string tempRoot = Path.Combine(Path.GetTempPath(), "J2S_Esbuild");
                Directory.CreateDirectory(tempRoot);

                string inputPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".js");
                string outputPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".js");

                await File.WriteAllTextAsync(inputPath, js, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = esbuildPath,
                    Arguments = $"\"{inputPath}\" --outfile=\"{outputPath}\" --target=es2015",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();

                proc.WaitForExit();

                if (proc.ExitCode != 0 || !File.Exists(outputPath))
                {
                    Debug.WriteLine($"⚠ esbuild failed for {relPathForLog ?? "unknown"} (exit {proc.ExitCode}): {stderr}");
                    return js;
                }

                string transpiled = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);

                try
                {
                    File.Delete(inputPath);
                    File.Delete(outputPath);
                }
                catch
                {
                    // ignore cleanup errors
                }

                Debug.WriteLine($"      ✓ Transpiled {relPathForLog ?? "unknown"} via esbuild");
                return transpiled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ esbuild transpile error for {relPathForLog ?? "unknown"}: {ex.Message}");
                return js;
            }
        }

        // --------------------------------------------------------------------
        //  BASIC SERVER / USER HELPERS
        // --------------------------------------------------------------------

        public static bool IsValidJellyfinConfiguration()
        {
            return !string.IsNullOrEmpty(AppSettings.Default.JellyfinIP) &&
                   !string.IsNullOrEmpty(AppSettings.Default.JellyfinApiKey) &&
                   AppSettings.Default.JellyfinApiKey.Length == 32 &&
                   IsValidUrl($"{AppSettings.Default.JellyfinIP}/Users");
        }

        public static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public async Task<List<JellyfinAuth>> LoadJellyfinUsersAsync()
        {
            var users = new List<JellyfinAuth>();
            if (!IsValidJellyfinConfiguration()) return users;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                using var response = await _httpClient.GetAsync($"{AppSettings.Default.JellyfinIP}/Users");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var jellyfinUsers = JsonSerializer.Deserialize<List<JellyfinAuth>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (jellyfinUsers != null)
                        users.AddRange(jellyfinUsers);

                    if (users.Count > 1)
                        users.Add(new JellyfinAuth { Id = "everyone", Name = "Everyone" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading users: {ex.Message}");
            }

            return users;
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

        public static async Task UpdateMultiServerConfig(string tempDirectory)
        {
            string configPath = Path.Combine(tempDirectory, "www", "config.json");

            if (!File.Exists(configPath))
            {
                var defaultConfig = new JsonObject
                {
                    ["servers"] = new JsonArray(),
                    ["multiserver"] = false
                };
                await File.WriteAllTextAsync(configPath, defaultConfig.ToJsonString());
            }

            string jsonText = await File.ReadAllTextAsync(configPath);
            var config = JsonNode.Parse(jsonText) ?? new JsonObject();

            config["multiserver"] = false;
            string serverUrl = AppSettings.Default.JellyfinIP.TrimEnd('/');
            config["servers"] = new JsonArray { JsonValue.Create(serverUrl) };

            await File.WriteAllTextAsync(configPath,
                config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        // --------------------------------------------------------------------
        //  PLUGIN MATRIX + API HELPERS
        // --------------------------------------------------------------------

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
                UseBabel = true, // legacy field; ignored now, but kept for compatibility
                RequiresModuleBundle = false,
                ModuleRepoApiRoot = null,
                ModuleBundleFileName = null
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

        private async Task<List<JellyfinPluginInfo>> GetInstalledPluginsAsync(string serverUrl)
        {
            var list = new List<JellyfinPluginInfo>();
            try
            {
                string url = serverUrl.TrimEnd('/') + "/Plugins";
                Debug.WriteLine("▶ Fetching installed plugins from: " + url);
                var json = await _httpClient.GetStringAsync(url);
                var parsed = JsonSerializer.Deserialize<List<JellyfinPluginInfo>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                    list.AddRange(parsed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ Failed to fetch /Plugins: " + ex.Message);
            }

            return list;
        }

        private PluginMatrixEntry? FindPluginMatrixEntry(JellyfinPluginInfo plugin)
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
        private async Task DownloadExplicitPluginFilesAsync(
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
                    js = await TranspileWithEsbuildAsync(js, rel);

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
        private class ExtractedDomBlocks
        {
            public List<string> HeadInjectBlocks { get; set; } = new();
            public List<string> BodyInjectBlocks { get; set; } = new();
        }

        private ExtractedDomBlocks ExtractPluginDomBlocks(string serverHtml)
        {
            var result = new ExtractedDomBlocks();

            // <script id="...">
            var scriptPattern = new Regex(
                @"<script[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>",
                RegexOptions.IgnoreCase);

            foreach (Match m in scriptPattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.HeadInjectBlocks.Add(m.Value);
            }

            // <template id="...">
            var templatePattern = new Regex(
                @"<template[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/template>",
                RegexOptions.IgnoreCase);

            foreach (Match m in templatePattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.BodyInjectBlocks.Add(m.Value);
            }

            // <style id="...">
            var stylePattern = new Regex(
                @"<style[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/style>",
                RegexOptions.IgnoreCase);

            foreach (Match m in stylePattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.HeadInjectBlocks.Add(m.Value);
            }

            // Custom Tabs-ish <div class="...custom...tab...">
            var customTabsDivPattern = new Regex(
                @"<div[^>]+class\s*=\s*[""'][^""']*custom[^""']*tab[^""']*[""'][^>]*>[\s\S]*?<\/div>",
                RegexOptions.IgnoreCase);

            foreach (Match m in customTabsDivPattern.Matches(serverHtml))
                result.BodyInjectBlocks.Add(m.Value);

            // <li class="...custom...tab...">
            var customTabsLiPattern = new Regex(
                @"<li[^>]+class\s*=\s*[""'][^""']*custom[^""']*tab[^""']*[""'][^>]*>[\s\S]*?<\/li>",
                RegexOptions.IgnoreCase);

            foreach (Match m in customTabsLiPattern.Matches(serverHtml))
                result.BodyInjectBlocks.Add(m.Value);

            return result;
        }

        private bool IsPluginIdentifier(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            id = id.ToLowerInvariant();

            return id.Contains("custom") ||
                   id.Contains("enhanced") ||
                   id.Contains("inject") ||
                   id.Contains("mediabar") ||
                   id.Contains("plugin") ||
                   id.Contains("tabs");
        }

        private string InsertIntoHead(string localHtml, IEnumerable<string> blocks)
        {
            if (!blocks.Any()) return localHtml;
            string insert = string.Join("\n", blocks);
            return Regex.Replace(localHtml,
                @"</head>",
                insert + "\n</head>",
                RegexOptions.IgnoreCase);
        }

        private string InsertIntoBodyTop(string localHtml, IEnumerable<string> blocks)
        {
            if (!blocks.Any()) return localHtml;
            string insert = string.Join("\n", blocks);

            return Regex.Replace(localHtml,
                @"<body[^>]*>",
                match => match.Value + "\n" + insert + "\n",
                RegexOptions.IgnoreCase);
        }

        private string RewriteScriptAndCssPaths(string html)
        {
            string Localize(Match m)
            {
                string prefix = m.Groups[1].Value;
                string url = m.Groups[2].Value;
                string suffix = m.Groups[3].Value;

                if (url.Contains("tizen.js")) return m.Value;

                if (url.Contains("/web/"))
                {
                    string file = Path.GetFileName(url);
                    return $"{prefix}{file}{suffix}";
                }

                return m.Value;
            }

            html = Regex.Replace(html,
                @"(src=[""'])([^""']+\.js[^""']*)([""'])",
                Localize,
                RegexOptions.IgnoreCase);

            string pluginScriptPattern =
                @"<script[^>]+src=[""']\/(JellyfinEnhanced|JavaScriptInjector|CustomTabs|FileTransformation)[^""']+[""'][^>]*><\/script>";
            html = Regex.Replace(html, pluginScriptPattern, "", RegexOptions.IgnoreCase);

            string pluginCssPattern =
                @"<link[^>]+href=[""']\/(JellyfinEnhanced|JavaScriptInjector|CustomTabs|FileTransformation)[^""']+[""'][^>]*>";
            html = Regex.Replace(html, pluginCssPattern, "", RegexOptions.IgnoreCase);

            string inlineInjectPattern =
                @"<script>[\s\S]*?JellyfinEnhanced[\s\S]*?<\/script>";
            html = Regex.Replace(html, inlineInjectPattern, "", RegexOptions.IgnoreCase);

            html = Regex.Replace(html,
                @"(href=[""'])([^""']+\.css[^""']*)([""'])",
                Localize,
                RegexOptions.IgnoreCase);

            return html;
        }
        private bool IsLikelyPluginAsset(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();

            return lower.Contains("/plugins/")
                   || lower.Contains("javascriptinjector")
                   || lower.Contains("jellyfin-javascript-injector")
                   || lower.Contains("filetransformation")
                   || lower.Contains("file-transformation")
                   || lower.Contains("jellyfin-plugin-file-transformation")
                   || lower.Contains("jellyfinenhanced")
                   || lower.Contains("jellyfin-enhanced")
                   || lower.Contains("mediabar")
                   || lower.Contains("media-bar")
                   || lower.Contains("jellyfin-plugin-media-bar")
                   || lower.Contains("customtabs")
                   || lower.Contains("custom-tabs")
                   || lower.Contains("jellyfin-plugin-custom-tabs")
                   || lower.Contains("kefin")
                   || lower.Contains("kefintweaks");
        }
        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string serverIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";

            if (!File.Exists(localIndexPath))
                return false;

            // 1) Local Tizen HTML as base
            Debug.WriteLine("▶ Using LOCAL Tizen index.html as base (correct).");
            string localHtml = await File.ReadAllTextAsync(localIndexPath);

            // 2) Fetch server HTML
            string serverHtml = "";
            try
            {
                Debug.WriteLine($"▶ Fetching SERVER index.html from {serverIndexUrl}");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                serverHtml = await _httpClient.GetStringAsync(serverIndexUrl, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed to fetch server index.html, continuing without DOM merge. Error: {ex.Message}");
                serverHtml = "";
            }

            // 3) Extract plugin DOM from server HTML
            ExtractedDomBlocks extracted = new ExtractedDomBlocks();
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                extracted = ExtractPluginDomBlocks(serverHtml);
            }

            // 4) Ensure <base href="."> in local HTML
            if (localHtml.Contains("<base", StringComparison.OrdinalIgnoreCase))
            {
                localHtml = Regex.Replace(localHtml,
                    @"<base[^>]+>",
                    @"<base href=""."">",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                localHtml = localHtml.Replace("<head>", "<head><base href=\".\">",
                    StringComparison.OrdinalIgnoreCase);
            }

            // 5) Rewrite JS/CSS to local form
            localHtml = RewriteScriptAndCssPaths(localHtml);

            // 6) plugin_cache dir
            string pluginCacheDir = Path.Combine(tempDirectory, "www", "plugin_cache");
            Directory.CreateDirectory(pluginCacheDir);

            var pluginCssBuilder = new StringBuilder();
            var pluginJsBuilder = new StringBuilder();

            // 7) Extract plugin assets from server index
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                Debug.WriteLine("▶ Extracting plugin assets from server index…");

                // CSS
                var cssMatches = Regex.Matches(
                    serverHtml,
                    @"<link[^>]+href=[""']([^""']+)[""'][^>]*>",
                    RegexOptions.IgnoreCase);

                foreach (Match m in cssMatches)
                {
                    string href = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    if (!IsLikelyPluginAsset(href))
                        continue;

                    try
                    {
                        Uri uri;
                        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            uri = new Uri(href);
                        }
                        else
                        {
                            var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
                            var rel = href.TrimStart('/');
                            uri = new Uri(baseUri, rel);
                        }

                        string fileName = Path.GetFileName(uri.AbsolutePath);
                        if (string.IsNullOrEmpty(fileName) ||
                            !fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string localPath = Path.Combine(pluginCacheDir, fileName);

                        Debug.WriteLine($"  ✓ Plugin CSS → cache: {uri} -> plugin_cache/{fileName}");
                        var bytes = await _httpClient.GetByteArrayAsync(uri);
                        await File.WriteAllBytesAsync(localPath, bytes);

                        pluginCssBuilder.AppendLine($"<link rel=\"stylesheet\" href=\"plugin_cache/{fileName}\" />");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠ Failed to cache plugin CSS '{href}': {ex.Message}");
                    }
                }

                // JS
                var jsMatches = Regex.Matches(
                    serverHtml,
                    @"<script[^>]+src=[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>",
                    RegexOptions.IgnoreCase);

                foreach (Match m in jsMatches)
                {
                    string jsUrl = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(jsUrl))
                        continue;

                    var lower = jsUrl.ToLowerInvariant();

                    // Skip core bundles
                    if (lower.Contains("main.") ||
                        lower.Contains("runtime") ||
                        lower.Contains("jellyfin-apiclient") ||
                        lower.Contains("react") ||
                        lower.Contains("mui") ||
                        lower.Contains("tanstack"))
                    {
                        continue;
                    }

                    bool isPlugin = IsLikelyPluginAsset(jsUrl);

                    // Skip general Enhanced script; handled via explicit files.
                    if (isPlugin && lower.Contains("jellyfinenhanced"))
                    {
                        Debug.WriteLine($"  → Skipping general cache for Jellyfin Enhanced script: {jsUrl}");
                        continue;
                    }

                    // CASE 1: relative (/web/) plugin JS → cache plain
                    if (!jsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !jsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !jsUrl.StartsWith("//") &&
                        isPlugin)
                    {
                        try
                        {
                            var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
                            var rel = jsUrl.TrimStart('/');
                            var uri = new Uri(baseUri, rel);

                            string fileName = Path.GetFileName(uri.AbsolutePath);

                            if (string.IsNullOrEmpty(fileName))
                                continue;

                            if (!fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                                fileName += ".js";

                            string localPath = Path.Combine(pluginCacheDir, fileName);

                            Debug.WriteLine($"  ✓ Plugin JS (/web/) → cache: {uri} -> plugin_cache/{fileName}");
                            var bytes = await _httpClient.GetByteArrayAsync(uri);
                            await File.WriteAllBytesAsync(localPath, bytes);

                            pluginJsBuilder.AppendLine($"<script src=\"plugin_cache/{fileName}\"></script>");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"⚠ Failed to cache plugin JS '{jsUrl}': {ex.Message}");
                        }

                        continue;
                    }

                    // CASE 2: external (CDN) → esbuild-transpiled wrapper in plugin_cache
                    if (jsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        jsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        jsUrl.StartsWith("//"))
                    {
                        if (!isPlugin &&
                            !lower.Contains("cdn.jsdelivr") &&
                            !lower.Contains("unpkg.com"))
                        {
                            continue;
                        }

                        string normalized = jsUrl;
                        if (normalized.StartsWith("//"))
                            normalized = "http:" + normalized;

                        Debug.WriteLine("  ✓ Plugin JS (external, esbuild) : " + normalized);

                        try
                        {
                            string jsContent = await _httpClient.GetStringAsync(normalized);

                            // Transpile external plugin JS to es2015 as well
                            jsContent = await TranspileWithEsbuildAsync(jsContent, normalized);

                            var wrapped = new StringBuilder();
                            wrapped.AppendLine("window.WaitForApiClient(function(){");
                            wrapped.AppendLine("   try {");
                            wrapped.AppendLine(jsContent);
                            wrapped.AppendLine("   } catch(e) { console.error('Plugin Error:', e); }");
                            wrapped.AppendLine("});");

                            string fileName = $"plugin_{Guid.NewGuid():N}.js";
                            string localPath = Path.Combine(pluginCacheDir, fileName);
                            await File.WriteAllTextAsync(localPath, wrapped.ToString());

                            pluginJsBuilder.AppendLine($@"<script src=""plugin_cache/{fileName}""></script>");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("⚠ Plugin JS (external) failed: " + ex.Message);
                        }
                    }
                }
            }

            // 8) Plugin API + matrix (Enhanced, etc.)
            var apiPlugins = await GetInstalledPluginsAsync(serverUrl);
            var apiJsBuilder = new StringBuilder();

            Debug.WriteLine("▶ Resolving client-side plugins via API + matrix…");

            var enhancedComponents = new List<string>();
            string? enhancedMainScript = null;

            foreach (var plugin in apiPlugins)
            {
                Debug.WriteLine($"▶ Plugin detected: {plugin.Name}");

                var entry = FindPluginMatrixEntry(plugin);

                if (entry == null)
                {
                    continue; // server-side only
                }

                if (entry.ExplicitServerFiles != null && entry.ExplicitServerFiles.Count > 0)
                {
                    // Enhanced: pick out main script and components
                    enhancedMainScript = entry.ExplicitServerFiles.FirstOrDefault(rel =>
                        rel.EndsWith("/script", StringComparison.OrdinalIgnoreCase));

                    enhancedComponents.AddRange(entry.ExplicitServerFiles.Where(rel =>
                        !rel.Equals(enhancedMainScript, StringComparison.OrdinalIgnoreCase)));

                    // Download + transpile all Enhanced files
                    await DownloadExplicitPluginFilesAsync(serverUrl, pluginCacheDir, entry);
                    continue;
                }

                // TODO: in future, you can also add explicit esbuild-based
                // download here for other plugins if needed.
            }

            // 9) Inject Enhanced main script (transpiled es2015)
            if (!string.IsNullOrEmpty(enhancedMainScript))
            {
                Debug.WriteLine("▶ Injecting Enhanced main script (transpiled es2015)...");
                string relPath = enhancedMainScript.TrimStart('/');
                if (!relPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    relPath += ".js";

                string webPath = ("plugin_cache/" + relPath)
                    .Replace("\\", "/")
                    .Replace("//", "/");

                pluginJsBuilder.AppendLine($"<script src=\"{webPath}\"></script>");
            }

            // 10) Inject plugin CSS + JS into local HTML
            if (pluginCssBuilder.Length > 0)
            {
                localHtml = Regex.Replace(
                    localHtml,
                    "<head>",
                    "<head>\n" + pluginCssBuilder,
                    RegexOptions.IgnoreCase);
            }

            if (pluginJsBuilder.Length > 0)
            {
                localHtml = localHtml.Replace("</body>", pluginJsBuilder + "\n</body>");
            }

            // 11) Merge plugin DOM from server (Custom Tabs, etc.)
            if (!string.IsNullOrWhiteSpace(serverHtml))
            {
                if (extracted.HeadInjectBlocks.Any())
                {
                    localHtml = InsertIntoHead(localHtml, extracted.HeadInjectBlocks);
                }

                if (extracted.BodyInjectBlocks.Any())
                {
                    localHtml = InsertIntoBodyTop(localHtml, extracted.BodyInjectBlocks);
                }
            }

            // 12) Inject Tizen bootloader + Enhanced URL patch + WaitForApiClient
            var boot = new StringBuilder();
            boot.AppendLine("<script src=\"tizen.js\"></script>");
            boot.AppendLine("<script>");
            boot.AppendLine($"window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            boot.AppendLine("window.appConfig = window.appConfig || {};");
            boot.AppendLine($"window.appConfig.servers = [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}];");

            // ------------------------------------------------------
            // 1) Enhanced URL Rewrite Logic
            // ------------------------------------------------------
            boot.AppendLine("window.__EnhancedRewrite = function(path) {");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (typeof path !== 'string') return path;");

            // Rewrite main script
            boot.AppendLine("        if (path === '/JellyfinEnhanced/script')");
            boot.AppendLine("            return 'plugin_cache/JellyfinEnhanced/script.js';");

            // Rewrite JS modules
            boot.AppendLine("        if (path.startsWith('/JellyfinEnhanced/js/')) {");
            boot.AppendLine("            return 'plugin_cache/JellyfinEnhanced/js/' + path.substring('/JellyfinEnhanced/js/'.length);");
            boot.AppendLine("        }");

            // Rewrite EVERYTHING except locale JSON files
            boot.AppendLine("        if (path.startsWith('/JellyfinEnhanced/') && !path.includes('/locales/')) {");
            boot.AppendLine("            return 'plugin_cache/JellyfinEnhanced/' + path.substring('/JellyfinEnhanced/'.length);");
            boot.AppendLine("        }");

            boot.AppendLine("    } catch(e) { console.error('Enhanced rewrite failed', e); }");
            boot.AppendLine("    return path;");
            boot.AppendLine("};");

            // ------------------------------------------------------
            // 2) Patch fetch() so Enhanced cannot bypass getUrl()
            // ------------------------------------------------------
            boot.AppendLine("(function(){");
            boot.AppendLine("  const _fetch = window.fetch;");
            boot.AppendLine("  window.fetch = function(resource, init) {");
            boot.AppendLine("    try {");
            boot.AppendLine("      if (typeof resource === 'string')");
            boot.AppendLine("          resource = window.__EnhancedRewrite(resource);");
            boot.AppendLine("    } catch(e) { console.error('Enhanced fetch rewrite failed', e); }");
            boot.AppendLine("    return _fetch(resource, init);");
            boot.AppendLine("  };");
            boot.AppendLine("})();");

            // ------------------------------------------------------
            // 3) Patch ApiClient.getUrl()
            // ------------------------------------------------------
            boot.AppendLine("window.__patchEnhancedLoader = function(){");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (!window.ApiClient || window.__EnhancedLoaderPatched) return;");
            boot.AppendLine("        window.__EnhancedLoaderPatched = true;");
            boot.AppendLine("        var origGetUrl = window.ApiClient.getUrl && window.ApiClient.getUrl.bind(window.ApiClient);");
            boot.AppendLine("        if (!origGetUrl) return;");
            boot.AppendLine("        window.ApiClient.getUrl = function(path){");
            boot.AppendLine("            try { path = window.__EnhancedRewrite(path); }");
            boot.AppendLine("            catch (e) { console.error('Enhanced getUrl rewrite error', e); }");
            boot.AppendLine("            return origGetUrl(path);");
            boot.AppendLine("        };");
            boot.AppendLine("        console.log('🪼 Enhanced: ApiClient.getUrl patched');");
            boot.AppendLine("    } catch (e) { console.error('Failed to patch ApiClient.getUrl', e); }");
            boot.AppendLine("};");

            // ------------------------------------------------------
            // 4) Wait for ApiClient to exist, then patch it
            // ------------------------------------------------------
            boot.AppendLine("window.WaitForApiClient = function(cb){");
            boot.AppendLine("  let t = setInterval(()=>{");
            boot.AppendLine("    if (window.ApiClient || (window.appRouter && window.appRouter.isReady)) {");
            boot.AppendLine("      clearInterval(t);");
            boot.AppendLine("      try { if (window.__patchEnhancedLoader) window.__patchEnhancedLoader(); }");
            boot.AppendLine("      catch(e) { console.error('Error running __patchEnhancedLoader', e); }");
            boot.AppendLine("      cb();");
            boot.AppendLine("    }");
            boot.AppendLine("  }, 250);");
            boot.AppendLine("};");

            boot.AppendLine("</script>");

            // ------------------------------------------------------
            // Developer WebSocket Logging
            // ------------------------------------------------------
            if (AppSettings.Default.EnableDevLogs)
            {
                boot.AppendLine("<script>");
                boot.AppendLine("(function(){");
                boot.AppendLine($"var ws=new WebSocket('ws://{AppSettings.Default.LocalIp}:5001');");
                boot.AppendLine("var send=(t,d)=>{try{ws.send(JSON.stringify({type:t,data:d}))}catch(e){}};");
                boot.AppendLine("console.log=(...a)=>send('log',a);");
                boot.AppendLine("console.error=(...a)=>send('error',a);");
                boot.AppendLine("window.onerror=(m,s,l,c)=>send('error',[m,s,l,c]);");
                boot.AppendLine("})();");
                boot.AppendLine("</script>");
            }


            localHtml = Regex.Replace(
                localHtml,
                "<head>",
                "<head>\n" + boot,
                RegexOptions.IgnoreCase);

            // 13) Clean + add CSP
            localHtml = Regex.Replace(localHtml,
                @"<meta[^>]*Content-Security-Policy[^>]*>",
                "",
                RegexOptions.IgnoreCase);

            localHtml = localHtml.Replace(
                "</head>",
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;\">\n</head>");

            // 14) Strip server-side plugin JS tags (keep only plugin_cache & core)
            localHtml = Regex.Replace(
                localHtml,
                @"<script[^>]+src=[""']([^""']+)[""'][^>]*>\s*</script>",
                m =>
                {
                    string src = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(src))
                        return m.Value;

                    string lower = src.ToLowerInvariant();

                    // keep our local cached scripts
                    if (lower.Contains("plugin_cache/"))
                        return m.Value;

                    bool isServerPlugin =
                           lower.Contains("/plugins/")
                        || lower.Contains("javascriptinjector")
                        || lower.Contains("jellyfin-javascript-injector")
                        || lower.Contains("filetransformation")
                        || lower.Contains("file-transformation")
                        || lower.Contains("jellyfin-plugin-file-transformation")
                        || lower.Contains("customtabs")
                        || lower.Contains("custom-tabs")
                        || lower.Contains("jellyfin-plugin-custom-tabs")
                        || lower.Contains("jellyfinenhanced")
                        || lower.Contains("jellyfin-enhanced")
                        || lower.Contains("mediabar")
                        || lower.Contains("media-bar")
                        || lower.Contains("jellyfin-plugin-media-bar")
                        || lower.Contains("kefin")
                        || lower.Contains("kefintweaks")
                        || lower.Contains("translations/");

                    return isServerPlugin ? string.Empty : m.Value;
                },
                RegexOptions.IgnoreCase
            );

            await File.WriteAllTextAsync(localIndexPath, localHtml);
            return true;
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

        public async Task AddOrUpdateCspAsync(string tempDirectory, string serverUrl)
        {
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string html = await File.ReadAllTextAsync(indexPath);

            html = Regex.Replace(html,
                @"<meta[^>]*Content-Security-Policy[^>]*>",
                "",
                RegexOptions.IgnoreCase);

            string newCsp =
                @"<meta http-equiv=""Content-Security-Policy"" content=""default-src * 'unsafe-inline' 'unsafe-eval' blob: data: ws: http: https:;"">";

            html = Regex.Replace(html,
                @"</head>",
                $"{newCsp}\n</head>",
                RegexOptions.IgnoreCase);

            await File.WriteAllTextAsync(indexPath, html);
        }
        private async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            try
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
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ Failed injecting user settings script: " + ex.Message);
            }
        }
        public async Task UpdateJellyfinUsersAsync(string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                foreach (string userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    try
                    {
                        // Fetch user info
                        var getUserResponse = await _httpClient.GetAsync($"{AppSettings.Default.JellyfinIP}/Users/{userId}");
                        getUserResponse.EnsureSuccessStatusCode();
                        var userJson = await getUserResponse.Content.ReadAsStringAsync();

                        var userNode = JsonNode.Parse(userJson) ?? throw new JsonException("Failed to parse user JSON");

                        // Update auto-login setting
                        userNode["EnableAutoLogin"] = AppSettings.Default.UserAutoLogin;

                        var userContent = new StringContent(
                            userNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                            Encoding.UTF8,
                            "application/json");

                        using (userContent)
                        {
                            var userResponse = await _httpClient.PostAsync($"{AppSettings.Default.JellyfinIP}/Users?userId={userId}", userContent);
                            userResponse.EnsureSuccessStatusCode();
                        }

                        // Update additional user configurations
                        var userConfig = new
                        {
                            AppSettings.Default.PlayDefaultAudioTrack,
                            AppSettings.Default.SubtitleLanguagePreference,
                            SubtitleMode = AppSettings.Default.SelectedSubtitleMode,
                            AppSettings.Default.RememberAudioSelections,
                            AppSettings.Default.RememberSubtitleSelections,
                            EnableNextEpisodeAutoPlay = AppSettings.Default.AutoPlayNextEpisode,
                        };

                        var configJson = JsonSerializer.Serialize(userConfig, new JsonSerializerOptions { WriteIndented = true });

                        using var configContent = new StringContent(configJson, Encoding.UTF8, "application/json");
                        var configResponse = await _httpClient.PostAsync($"{AppSettings.Default.JellyfinIP}/Users/Configuration?userId={userId}", configContent);
                        configResponse.EnsureSuccessStatusCode();
                    }
                    catch (Exception userEx)
                    {
                        Debug.WriteLine($"Failed to update configuration for user {userId}: {userEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"General error updating user configurations: {ex.Message}");
            }
        }
    }
}