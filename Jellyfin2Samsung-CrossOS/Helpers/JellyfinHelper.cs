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
using System.Xml.Linq;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinHelper
    {
        private readonly HttpClient _httpClient;

        public JellyfinHelper(HttpClient httpClient)
        {
            _httpClient = httpClient;
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
                ServerPath = "/JellyfinEnhanced/script",
                FallbackUrls = new List<string>
                {
                    "https://raw.githubusercontent.com/n00bcodr/Jellyfin-Enhanced/main/src/plugin.js"
                },
                UseBabel = true,
                RequiresModuleBundle = true,
                ModuleRepoApiRoot = "https://api.github.com/repos/n00bcodr/Jellyfin-Enhanced/contents/Jellyfin.Plugin.JellyfinEnhanced/js",
                ModuleBundleFileName = "enhanced.modules.bundle.js"
            },

            new PluginMatrixEntry
            {
                Name = "Media Bar",
                IdContains = "mediabar",
                ServerPath = null, // purely CDN-hosted
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

        private PluginMatrixEntry FindPluginMatrixEntry(JellyfinPluginInfo plugin)
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

        private async Task<(string Content, string Url)?> TryDownloadFirstWorkingUrl(IEnumerable<string> urls)
        {
            foreach (var url in urls)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    Debug.WriteLine($"   → Trying plugin URL: {url}");
                    var content = await _httpClient.GetStringAsync(url);
                    return (content, url);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"   ⚠ Plugin URL failed: {url} → {ex.Message}");
                }
            }

            return null;
        }

        private async Task CollectGitHubJsFilesAsync(string apiUrl, List<(string path, string downloadUrl)> acc)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.UserAgent.ParseAdd("Jellyfin2Samsung/1.0");
                var resp = await _httpClient.SendAsync(request);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var typeProp = item.GetProperty("type").GetString();
                    var path = item.GetProperty("path").GetString();

                    if (typeProp == "file")
                    {
                        var name = item.GetProperty("name").GetString();
                        if (!string.IsNullOrEmpty(name) &&
                            name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                        {
                            var download = item.GetProperty("download_url").GetString();
                            acc.Add((path, download));
                        }
                    }
                    else if (typeProp == "dir")
                    {
                        var childUrl = item.GetProperty("url").GetString();
                        if (!string.IsNullOrEmpty(childUrl))
                            await CollectGitHubJsFilesAsync(childUrl, acc);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ GitHub traversal error: " + ex.Message);
            }
        }

        private async Task BuildEnhancedModuleBundleAsync(string serverUrl, string pluginCacheDir, PluginMatrixEntry entry)
        {
            if (!entry.RequiresModuleBundle || string.IsNullOrEmpty(entry.ModuleRepoApiRoot))
                return;

            try
            {
                var modules = new List<(string path, string downloadUrl)>();
                Debug.WriteLine("▶ Building Jellyfin Enhanced module bundle via GitHub tree…");
                await CollectGitHubJsFilesAsync(entry.ModuleRepoApiRoot, modules);

                if (modules.Count == 0)
                {
                    Debug.WriteLine("⚠ No Enhanced JS modules discovered via GitHub.");
                    return;
                }

                var sb = new StringBuilder();

                foreach (var (path, _) in modules)
                {
                    try
                    {
                        var normalized = (path ?? "").Replace("\\", "/");
                        var idx = normalized.IndexOf("/js/", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0)
                            continue;

                        var rel = normalized.Substring(idx + 4); // after "js/"
                        var url = serverUrl.TrimEnd('/') + "/JellyfinEnhanced/js/" + rel;
                        Debug.WriteLine("   → Fetch Enhanced module: " + url);

                        string js = await _httpClient.GetStringAsync(url);
                        sb.AppendLine("// --- " + rel + " ---");
                        sb.AppendLine(js);
                        sb.AppendLine();
                    }
                    catch (Exception exMod)
                    {
                        Debug.WriteLine("⚠ Failed Enhanced module: " + path + " → " + exMod.Message);
                    }
                }

                if (sb.Length == 0)
                {
                    Debug.WriteLine("⚠ Enhanced module bundle is empty, skipping.");
                    return;
                }

                var wrapped = new StringBuilder();
                wrapped.AppendLine("if(typeof define!=='function'){var define=function(){};define.amd=true;}");
                wrapped.AppendLine("window.WaitForApiClient(function(){");
                wrapped.AppendLine("   try {");
                wrapped.Append(sb.ToString());
                wrapped.AppendLine("   } catch(e){ console.error('Enhanced bundle error', e); }");
                wrapped.AppendLine("});");

                string fileName = string.IsNullOrEmpty(entry.ModuleBundleFileName)
                    ? "enhanced.modules.bundle.js"
                    : entry.ModuleBundleFileName;

                string outPath = Path.Combine(pluginCacheDir, fileName);
                await File.WriteAllTextAsync(outPath, wrapped.ToString());

                Debug.WriteLine("✓ Enhanced module bundle written: " + fileName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ Failed building Enhanced bundle: " + ex.Message);
            }
        }

        // --------------------------------------------------------------------
        //  DOM EXTRACTION ENGINE (server → local)
        // --------------------------------------------------------------------

        private class ExtractedDomBlocks
        {
            public List<string> HeadInjectBlocks { get; set; } = new();
            public List<string> BodyInjectBlocks { get; set; } = new();
        }

        private ExtractedDomBlocks ExtractPluginDomBlocks(string serverHtml)
        {
            var result = new ExtractedDomBlocks();

            // 1) <script id="...">...</script>
            var scriptPattern = new Regex(
                @"<script[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/script>",
                RegexOptions.IgnoreCase);

            foreach (Match m in scriptPattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.HeadInjectBlocks.Add(m.Value);
            }

            // 2) <template id="...">...</template>
            var templatePattern = new Regex(
                @"<template[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/template>",
                RegexOptions.IgnoreCase);

            foreach (Match m in templatePattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.BodyInjectBlocks.Add(m.Value);
            }

            // 3) <style id="...">...</style>
            var stylePattern = new Regex(
                @"<style[^>]*id\s*=\s*[""']([^""']+)[""'][^>]*>[\s\S]*?<\/style>",
                RegexOptions.IgnoreCase);

            foreach (Match m in stylePattern.Matches(serverHtml))
            {
                string id = m.Groups[1].Value.ToLowerInvariant();
                if (IsPluginIdentifier(id))
                    result.HeadInjectBlocks.Add(m.Value);
            }

            // 4) CustomTabs-ish <div class="...custom...tab...">
            var customTabsDivPattern = new Regex(
                @"<div[^>]+class\s*=\s*[""'][^""']*custom[^""']*tab[^""']*[""'][^>]*>[\s\S]*?<\/div>",
                RegexOptions.IgnoreCase);

            foreach (Match m in customTabsDivPattern.Matches(serverHtml))
                result.BodyInjectBlocks.Add(m.Value);

            // 5) <li class="...custom...tab...">
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

        // --------------------------------------------------------------------
        //  PLUGIN ASSET HEURISTICS
        // --------------------------------------------------------------------

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

        // --------------------------------------------------------------------
        //  MAIN PATCHER — SERVER DOM + LOCAL CHUNKS + PLUGIN MATRIX
        // --------------------------------------------------------------------

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

                // Ensure Babel exists
                await DownloadBabelAsync(tempDirectory);

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

                            // If filename is empty OR missing extension, fix it
                            if (string.IsNullOrEmpty(fileName))
                                continue;

                            // *** PATCH: add .js if missing ***
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

                    // CASE 2: external (CDN) → Babel-wrap + cache
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

                        Debug.WriteLine("  ✓ Plugin JS (external, Babel) : " + normalized);

                        try
                        {
                            string jsContent = await _httpClient.GetStringAsync(normalized);

                            var wrapped = new StringBuilder();
                            wrapped.AppendLine("if(typeof define!=='function'){var define=function(){};define.amd=true;}");
                            wrapped.AppendLine("window.WaitForApiClient(function(){");
                            wrapped.AppendLine("   try {");
                            wrapped.AppendLine(jsContent);
                            wrapped.AppendLine("   } catch(e) { console.error('Plugin Error:', e); }");
                            wrapped.AppendLine("});");

                            string fileName = $"plugin_{Guid.NewGuid():N}.js";
                            string localPath = Path.Combine(pluginCacheDir, fileName);
                            await File.WriteAllTextAsync(localPath, wrapped.ToString());

                            pluginJsBuilder.AppendLine(
                                $@"<script type=""text/babel"" data-presets=""env"" src=""plugin_cache/{fileName}""></script>");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("⚠ Plugin JS (external) failed: " + ex.Message);
                        }
                    }
                }
            }

            // 8) Plugin API + matrix (Enhanced, MediaBar, KefinTweaks)
            var apiPlugins = await GetInstalledPluginsAsync(serverUrl);
            var apiJsBuilder = new StringBuilder();

            Debug.WriteLine("▶ Resolving client-side plugins via API + matrix…");

            foreach (var plugin in apiPlugins)
            {
                Debug.WriteLine($"▶ Plugin detected: {plugin.Name}");

                var entry = FindPluginMatrixEntry(plugin);
                if (entry == null)
                {
                    // AudioDB, TMDb, etc. = server-side only
                    continue;
                }

                var urls = new List<string>();

                if (!string.IsNullOrEmpty(entry.ServerPath))
                {
                    var absolute = serverUrl.TrimEnd('/') + entry.ServerPath;
                    urls.Add(absolute);
                }

                urls.AddRange(entry.FallbackUrls);

                var result = await TryDownloadFirstWorkingUrl(urls);
                if (result == null)
                {
                    Debug.WriteLine($"❌ No working URL for plugin {plugin.Name}");
                    continue;
                }

                string jsBody = result.Value.Content;

                string outFileName;
                string chosenUrl = result.Value.Url;
                try
                {
                    var uri = new Uri(chosenUrl);
                    var name = Path.GetFileName(uri.LocalPath);

                    if (string.IsNullOrWhiteSpace(name))
                        name = $"plugin_{Guid.NewGuid():N}.js";
                    else if (!name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                        name += ".js";

                    outFileName = name;
                }
                catch
                {
                    outFileName = $"plugin_{Guid.NewGuid():N}.js";
                }

                string outPath = Path.Combine(pluginCacheDir, outFileName);

                if (entry.UseBabel)
                {
                    var wrapped = new StringBuilder();
                    wrapped.AppendLine("if(typeof define!=='function'){var define=function(){};define.amd=true;}");
                    wrapped.AppendLine("window.WaitForApiClient(function(){");
                    wrapped.AppendLine("   try {");
                    wrapped.AppendLine(jsBody);
                    wrapped.AppendLine("   } catch(e) { console.error('Plugin Error:', e); }");
                    wrapped.AppendLine("});");
                    await File.WriteAllTextAsync(outPath, wrapped.ToString());

                    apiJsBuilder.AppendLine(
                        $@"<script type=""text/babel"" data-presets=""env"" src=""plugin_cache/{outFileName}""></script>");
                }
                else
                {
                    await File.WriteAllTextAsync(outPath, jsBody);
                    apiJsBuilder.AppendLine(
                        $@"<script src=""plugin_cache/{outFileName}""></script>");
                }
            }

            // 9) Enhanced module bundle (via GitHub tree, served from /JellyfinEnhanced/js/**)
            bool enhancedInstalled = apiPlugins.Any(p =>
            {
                var e = FindPluginMatrixEntry(p);
                return e != null && e.RequiresModuleBundle;
            });

            if (enhancedInstalled)
            {
                var enhancedEntry = PluginMatrix.First(e => e.RequiresModuleBundle);
                await BuildEnhancedModuleBundleAsync(serverUrl, pluginCacheDir, enhancedEntry);

                if (!string.IsNullOrEmpty(enhancedEntry.ModuleBundleFileName))
                {
                    apiJsBuilder.AppendLine(
                        $@"<script type=""text/babel"" data-presets=""env"" src=""plugin_cache/{enhancedEntry.ModuleBundleFileName}""></script>");
                }
            }

            if (apiJsBuilder.Length > 0)
            {
                pluginJsBuilder.AppendLine(apiJsBuilder.ToString());
            }

            // 10) Inject plugin CSS + JS into local HTML
            if (pluginCssBuilder.Length > 0)
            {
                localHtml = Regex.Replace(
                    localHtml,
                    "<head>",
                    "<head>\n" + pluginCssBuilder.ToString(),
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

            // 12) Inject Tizen bootloader, WaitForApiClient, Babel loader

            // Make sure Babel is referenced
            if (!localHtml.Contains("babel.min.js", StringComparison.OrdinalIgnoreCase))
            {
                localHtml = Regex.Replace(localHtml,
                    "<head>",
                    "<head>\n<script src=\"libs/babel.min.js\"></script>",
                    RegexOptions.IgnoreCase);
            }

            var boot = new StringBuilder();
            boot.AppendLine("<script src=\"tizen.js\"></script>");
            boot.AppendLine("<script>");
            boot.AppendLine($"window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            boot.AppendLine("window.appConfig = window.appConfig || {};");
            boot.AppendLine($"window.appConfig.servers = [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}];");

            // --- Enhanced URL rewriter + patch ---
            boot.AppendLine("window.__EnhancedRewrite = function(path) {");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (typeof path === 'string' && path.startsWith('/JellyfinEnhanced/js/')) {");
            boot.AppendLine("            var fname = path.split('/').pop();");
            boot.AppendLine("            return 'plugin_cache/' + fname;");
            boot.AppendLine("        }");
            boot.AppendLine("    } catch(e) { console.error('Enhanced rewrite failed', e); }");
            boot.AppendLine("    return path;");
            boot.AppendLine("};");

            boot.AppendLine("window.__patchEnhancedLoader = function(){");
            boot.AppendLine("    try {");
            boot.AppendLine("        if (!window.ApiClient || window.__EnhancedLoaderPatched) return;");
            boot.AppendLine("        window.__EnhancedLoaderPatched = true;");
            boot.AppendLine("        var origGetUrl = window.ApiClient.getUrl && window.ApiClient.getUrl.bind(window.ApiClient);");
            boot.AppendLine("        if (!origGetUrl) return;");
            boot.AppendLine("        window.ApiClient.getUrl = function(path){");
            boot.AppendLine("            try {");
            boot.AppendLine("                path = window.__EnhancedRewrite(path);");
            boot.AppendLine("            } catch (e) { console.error('Enhanced getUrl rewrite error', e); }");
            boot.AppendLine("            return origGetUrl(path);");
            boot.AppendLine("        };");
            boot.AppendLine("        console.log('🪼 Enhanced: ApiClient.getUrl patched for plugin_cache modules');");
            boot.AppendLine("    } catch (e) { console.error('Failed to patch ApiClient.getUrl for Enhanced', e); }");
            boot.AppendLine("};");

            // WaitForApiClient helper (now also ensures Enhanced patch is applied *before* callback)
            boot.AppendLine("window.WaitForApiClient = function(cb){");
            boot.AppendLine("  let t = setInterval(()=>{");
            boot.AppendLine("    if (window.ApiClient || (window.appRouter && window.appRouter.isReady)) {");
            boot.AppendLine("      clearInterval(t);");
            boot.AppendLine("      try {");
            boot.AppendLine("        if (window.__patchEnhancedLoader) window.__patchEnhancedLoader();");
            boot.AppendLine("      } catch(e) { console.error('Error running __patchEnhancedLoader', e); }");
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

            // ============================================================================
            // STRIP ALL SERVER-SIDE PLUGIN SCRIPTS (OPTION A)
            // ============================================================================
            // Remove any <script> tags that load plugin JS directly from the server.
            // We ONLY want plugin_cache/*.js — our Babel-safe versions.

            localHtml = Regex.Replace(
                localHtml,
                @"<script[^>]+src=[""']([^""']+)[""'][^>]*>\s*</script>",
                m =>
                {
                    string src = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(src))
                        return m.Value;

                    var lower = src.ToLowerInvariant();

                    // KEEP anything that comes from plugin_cache (these are ours)
                    if (lower.Contains("plugin_cache/"))
                        return m.Value;

                    // STRIP ANY SERVER-DELIVERED PLUGIN CODE
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
                        || lower.Contains("translations/");  // remove plugin translation scripts too

                    return isServerPlugin ? string.Empty : m.Value;
                },
                RegexOptions.IgnoreCase
            );


            // 14) Save back
            await File.WriteAllTextAsync(localIndexPath, localHtml);
            return true;
        }

        // --------------------------------------------------------------------
        //  BABEL + CSP + USER SETTINGS HELPERS
        // --------------------------------------------------------------------

        private async Task DownloadBabelAsync(string tempDirectory)
        {
            try
            {
                string libsDir = Path.Combine(tempDirectory, "www", "libs");
                Directory.CreateDirectory(libsDir);

                string babelPath = Path.Combine(libsDir, "babel.min.js");

                if (File.Exists(babelPath))
                {
                    Debug.WriteLine("✓ Babel already exists");
                    return;
                }

                Debug.WriteLine("▶ Downloading Babel…");

                string babelCdn =
                    "https://cdn.jsdelivr.net/npm/@babel/standalone/babel.min.js";

                byte[] data = await _httpClient.GetByteArrayAsync(babelCdn);
                await File.WriteAllBytesAsync(babelPath, data);

                Debug.WriteLine("✓ Babel downloaded");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("⚠ Babel download failed: " + ex.Message);
            }
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

        // --------------------------------------------------------------------
        //  DEBUG HELPERS
        // --------------------------------------------------------------------

        private void Log(string msg) => Debug.WriteLine("▶ " + msg);
        private void LogWarn(string msg) => Debug.WriteLine("⚠ " + msg);
        private void LogError(string msg) => Debug.WriteLine("❌ " + msg);
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
