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
        //  PLUGIN MATRIX + HELPERS
        // --------------------------------------------------------------------

        private static readonly List<PluginMatrixEntry> PluginMatrix = new()
        {
            // Jellyfin Enhanced
            new PluginMatrixEntry
            {
                Name = "Jellyfin Enhanced",
                IdContains = "jellyfinenhanced",
                ServerPath = "/JellyfinEnhanced/script",
                FallbackUrls = new List<string>
                {
                    "https://raw.githubusercontent.com/n00bcodr/Jellyfin-Enhanced/main/src/plugin.js"
                },
                UseBabel = true
            },

            // Custom Tabs
            new PluginMatrixEntry
            {
                Name = "Custom Tabs",
                IdContains = "custom",
                ServerPath = "/web/Plugins/CustomTabs/customtabs.js",
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-custom-tabs@main/dashboard-ui/customtabs.js",
                    "https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-custom-tabs/main/dashboard-ui/customtabs.js"
                },
                UseBabel = true
            },

            // Media Bar
            new PluginMatrixEntry
            {
                Name = "Media Bar",
                IdContains = "mediabar",
                ServerPath = null, // not exposed via /web
                FallbackUrls = new List<string>
                {
                    "https://cdn.jsdelivr.net/gh/IAmParadox27/jellyfin-plugin-media-bar@main/slideshowpure.js",
                    "https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-media-bar/main/slideshowpure.js"
                },
                UseBabel = true
            },

            // KefinTweaks
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

        /// <summary>
        /// Try a list of URLs in order. Return first that works, or null.
        /// </summary>
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

        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string serverIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";

            if (!File.Exists(localIndexPath))
                return false;

            Debug.WriteLine("▶ Using LOCAL Tizen index.html as base (correct).");
            string localHtml = await File.ReadAllTextAsync(localIndexPath);

            string serverHtml = "";
            try
            {
                Debug.WriteLine($"▶ Fetching SERVER index.html from {serverIndexUrl}");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                serverHtml = await _httpClient.GetStringAsync(serverIndexUrl, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed to fetch server index. Using local only. Error = {ex.Message}");
                serverHtml = "";
            }

            // Helper: detect plugin-ish URLs by name/path
            bool IsKnownPluginAsset(string url)
            {
                var lower = url.ToLowerInvariant();
                return lower.Contains("/plugins/") ||
                       lower.Contains("javascriptinjector") ||
                       lower.Contains("jellyfin-javascript-injector") ||
                       lower.Contains("customtabs") ||
                       lower.Contains("custom-tabs") ||
                       lower.Contains("jellyfin-plugin-custom-tabs") ||
                       lower.Contains("jellyfinenhanced") ||
                       lower.Contains("jellyfin-enhanced") ||
                       lower.Contains("mediabar") ||
                       lower.Contains("media-bar") ||
                       lower.Contains("jellyfin-plugin-media-bar") ||
                       lower.Contains("kefintweaks") ||
                       lower.Contains("kefin");
            }

            // local plugin_cache dir
            string pluginCacheDir = Path.Combine(tempDirectory, "www", "plugin_cache");
            Directory.CreateDirectory(pluginCacheDir);

            var cssBuilder = new StringBuilder();
            var jsBuilder = new StringBuilder();

            // 1) Extract plugin CSS + JS from SERVER HTML

            var cssMatches = Regex.Matches(
                serverHtml,
                @"<link[^>]+href=[""']([^""']+)[""'][^>]*>",
                RegexOptions.IgnoreCase);

            var jsMatches = Regex.Matches(
                serverHtml,
                @"<script[^>]+src=[""']([^""']+)[""'][^>]*></script>",
                RegexOptions.IgnoreCase);

            Debug.WriteLine("▶ Extracting plugin assets from server index…");

            // 1a) Plugin CSS
            foreach (Match m in cssMatches)
            {
                string href = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                if (!IsKnownPluginAsset(href))
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
                        var relative = href.TrimStart('/');
                        uri = new Uri(baseUri, relative);
                    }

                    string fileName = Path.GetFileName(uri.AbsolutePath);
                    if (string.IsNullOrEmpty(fileName) ||
                        !fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string localPath = Path.Combine(pluginCacheDir, fileName);

                    Debug.WriteLine($"  ✓ Plugin CSS → cache: {uri} -> plugin_cache/{fileName}");
                    var bytes = await _httpClient.GetByteArrayAsync(uri);
                    await File.WriteAllBytesAsync(localPath, bytes);

                    cssBuilder.AppendLine($"<link rel=\"stylesheet\" href=\"plugin_cache/{fileName}\" />");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠ Failed to cache plugin CSS '{href}': {ex.Message}");
                }
            }

            // 1b) Plugin JS from server HTML
            await DownloadBabelAsync(tempDirectory);

            foreach (Match m in jsMatches)
            {
                string jsUrl = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(jsUrl))
                    continue;

                var lower = jsUrl.ToLowerInvariant();
                bool isPlugin = IsKnownPluginAsset(jsUrl);

                // Skip known core bundles (handled by local Tizen build)
                if (lower.Contains("main.") ||
                    lower.Contains("runtime") ||
                    lower.Contains("jellyfin-apiclient") ||
                    lower.Contains("react") ||
                    lower.Contains("mui") ||
                    lower.Contains("tanstack"))
                {
                    continue;
                }

                // Skip if not plugin-related and not a CDN
                if (!isPlugin &&
                    !lower.Contains("cdn.jsdelivr") &&
                    !lower.Contains("unpkg.com"))
                {
                    continue;
                }

                // CASE 1: local/server JS (e.g. JavaScriptInjector/public.js)
                if (!jsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !jsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !jsUrl.StartsWith("//"))
                {
                    try
                    {
                        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
                        var rel = jsUrl.TrimStart('/');
                        var uri = new Uri(baseUri, rel);

                        string fileName = Path.GetFileName(uri.AbsolutePath);
                        if (string.IsNullOrEmpty(fileName) ||
                            !fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string localPath = Path.Combine(pluginCacheDir, fileName);

                        Debug.WriteLine($"  ✓ Plugin JS (/web/) → cache: {uri} -> plugin_cache/{fileName}");
                        var bytes = await _httpClient.GetByteArrayAsync(uri);
                        await File.WriteAllBytesAsync(localPath, bytes);

                        // Local plugin (including JavaScriptInjector/public.js) → no Babel
                        jsBuilder.AppendLine($"<script src=\"plugin_cache/{fileName}\"></script>");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠ Failed to cache plugin JS '{jsUrl}': {ex.Message}");
                    }

                    continue;
                }

                // CASE 2: external JS from CDN (e.g. MediaBar, etc.)
                // NOTE: we still allow external ones if plugin-related or CDN.
                if (jsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    jsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    jsUrl.StartsWith("//"))
                {
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

                        jsBuilder.AppendLine(
                            $@"<script type=""text/babel"" data-presets=""env"" src=""plugin_cache/{fileName}""></script>");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("⚠ Plugin JS (external) failed: " + ex.Message);
                    }
                }
            }

            // 1c) Plugin API + MATRIX (Enhanced, CustomTabs, MediaBar, KefinTweaks)
            var apiPlugins = await GetInstalledPluginsAsync(serverUrl);
            var apiJsBuilder = new StringBuilder();

            Debug.WriteLine("▶ Resolving client-side plugins via API + matrix…");

            foreach (var plugin in apiPlugins)
            {
                Debug.WriteLine($"▶ Plugin detected: {plugin.Name}");

                var entry = FindPluginMatrixEntry(plugin);
                if (entry == null)
                {
                    // Not in matrix → ignore (AudioDB, TMDb, etc.)
                    continue;
                }

                var urls = new List<string>();

                // 1) Server path if defined
                if (!string.IsNullOrEmpty(entry.ServerPath))
                {
                    var absolute = serverUrl.TrimEnd('/') + entry.ServerPath;
                    urls.Add(absolute);
                }

                // 2) Fallback URLs (CDN + GitHub raw)
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
                    // (We currently have no matrix entries with UseBabel=false,
                    // but this is here for future plugins.)
                    await File.WriteAllTextAsync(outPath, jsBody);
                    apiJsBuilder.AppendLine(
                        $@"<script src=""plugin_cache/{outFileName}""></script>");
                }
            }

            if (apiJsBuilder.Length > 0)
            {
                jsBuilder.AppendLine(apiJsBuilder.ToString());
            }

            // 2) Inject plugin CSS/JS into local index.html

            if (cssBuilder.Length > 0)
            {
                localHtml = Regex.Replace(
                    localHtml,
                    "<head>",
                    "<head>\n" + cssBuilder.ToString(),
                    RegexOptions.IgnoreCase);
            }

            if (jsBuilder.Length > 0)
            {
                localHtml = localHtml.Replace("</body>", jsBuilder + "\n</body>");
            }

            // 3) Bootloader / WaitForApiClient / Dev logger

            var boot = new StringBuilder();
            boot.AppendLine("<script src=\"libs/babel.min.js\"></script>");
            boot.AppendLine("<script src=\"tizen.js\"></script>");
            boot.AppendLine("<script>");
            boot.AppendLine($"window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            boot.AppendLine("window.appConfig = window.appConfig || {};");
            boot.AppendLine($"window.appConfig.servers = [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}];");
            boot.AppendLine("window.WaitForApiClient = function(cb){");
            boot.AppendLine(" let t = setInterval(()=>{");
            boot.AppendLine("   if(window.ApiClient || (window.appRouter && window.appRouter.isReady)){");
            boot.AppendLine("     clearInterval(t); cb();");
            boot.AppendLine("   }");
            boot.AppendLine(" },250);");
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

            // 4) CSP

            localHtml = Regex.Replace(localHtml,
                @"<meta[^>]*Content-Security-Policy[^>]*>",
                "",
                RegexOptions.IgnoreCase);

            localHtml = localHtml.Replace(
                "</head>",
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;\">\n</head>");

            // 5) Save

            await File.WriteAllTextAsync(localIndexPath, localHtml);
            return true;
        }

        private async Task DownloadBabelAsync(string tempDirectory)
        {
            try
            {
                string libsDir = Path.Combine(tempDirectory, "www", "libs");
                Directory.CreateDirectory(libsDir);
                string babelPath = Path.Combine(libsDir, "babel.min.js");
                if (!File.Exists(babelPath))
                {
                    var babelJs = await _httpClient.GetStringAsync("https://unpkg.com/@babel/standalone/babel.min.js");
                    await File.WriteAllTextAsync(babelPath, babelJs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download Babel: {ex.Message}");
            }
        }

        public static async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;

            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string htmlContent = await File.ReadAllTextAsync(indexPath);
            string BoolToJs(bool val) => val.ToString().ToLower();

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("<script>");
            sb.AppendLine("(function() {");

            foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                sb.AppendLine($"    var u = '{userId}';");
                sb.AppendLine($"    if (!localStorage.getItem('injected-' + u)) {{");
                sb.AppendLine($"        localStorage.setItem(u + '-appTheme', '{AppSettings.Default.SelectedTheme ?? "dark"}');");
                sb.AppendLine($"        localStorage.setItem(u + '-enableBackdrops', '{BoolToJs(AppSettings.Default.EnableBackdrops)}');");
                sb.AppendLine($"        localStorage.setItem('injected-' + u, 'true');");
                sb.AppendLine("    }");
            }

            sb.AppendLine("})();");
            sb.AppendLine("</script>");

            htmlContent = htmlContent.Replace("</body>", sb + "\n</body>");
            await File.WriteAllTextAsync(indexPath, htmlContent);
        }

        public async Task<bool> AddOrUpdateCspAsync(string tempDirectory, string serverUrl)
        {
            string configPath = Path.Combine(tempDirectory, "config.xml");
            if (!File.Exists(configPath)) return false;

            try
            {
                XDocument doc = XDocument.Load(configPath);
                XNamespace tizen = "http://tizen.org/ns/widgets";

                var access = doc.Root.Elements(tizen + "access").FirstOrDefault();
                if (access == null)
                {
                    doc.Root.Add(new XElement(tizen + "access",
                        new XAttribute("origin", "*"),
                        new XAttribute("subdomains", "true")));
                }
                else
                {
                    access.SetAttributeValue("origin", "*");
                    access.SetAttributeValue("subdomains", "true");
                }

                var requiredPrivileges = new[]
                {
                    "http://tizen.org/privilege/internet",
                    "http://tizen.org/privilege/network.get",
                    "http://tizen.org/privilege/filesystem.read",
                    "http://tizen.org/privilege/content.read"
                };

                foreach (var priv in requiredPrivileges)
                {
                    if (!doc.Root.Elements(tizen + "privilege")
                        .Any(x => x.Attribute("name")?.Value == priv))
                    {
                        doc.Root.Add(new XElement(tizen + "privilege",
                            new XAttribute("name", priv)));
                    }
                }

                doc.Save(configPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task UpdateJellyfinUsersAsync(string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization",
                    $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                foreach (string userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    try
                    {
                        var getUserResponse = await _httpClient.GetAsync(
                            $"{AppSettings.Default.JellyfinIP}/Users/{userId}");
                        getUserResponse.EnsureSuccessStatusCode();
                        var userJson = await getUserResponse.Content.ReadAsStringAsync();

                        var userNode = JsonNode.Parse(userJson)
                                       ?? throw new JsonException("Failed to parse user JSON");

                        userNode["EnableAutoLogin"] = AppSettings.Default.UserAutoLogin;

                        var userContent = new StringContent(
                            userNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                            Encoding.UTF8,
                            "application/json");

                        using (userContent)
                        {
                            var userResponse = await _httpClient.PostAsync(
                                $"{AppSettings.Default.JellyfinIP}/Users?userId={userId}",
                                userContent);
                            userResponse.EnsureSuccessStatusCode();
                        }

                        var userConfig = new
                        {
                            AppSettings.Default.PlayDefaultAudioTrack,
                            AppSettings.Default.SubtitleLanguagePreference,
                            SubtitleMode = AppSettings.Default.SelectedSubtitleMode,
                            AppSettings.Default.RememberAudioSelections,
                            AppSettings.Default.RememberSubtitleSelections,
                            EnableNextEpisodeAutoPlay = AppSettings.Default.AutoPlayNextEpisode,
                        };

                        var configJson = JsonSerializer.Serialize(userConfig,
                            new JsonSerializerOptions { WriteIndented = true });

                        using var configContent = new StringContent(configJson, Encoding.UTF8, "application/json");
                        var configResponse = await _httpClient.PostAsync(
                            $"{AppSettings.Default.JellyfinIP}/Users/Configuration?userId={userId}",
                            configContent);
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