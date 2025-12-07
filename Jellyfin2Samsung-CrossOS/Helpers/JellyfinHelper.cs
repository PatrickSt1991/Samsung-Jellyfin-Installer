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

        // ... LoadJellyfinUsersAsync (No changes needed) ...
        public async Task<List<JellyfinAuth>> LoadJellyfinUsersAsync()
        {
            var users = new List<JellyfinAuth>();
            if (!IsValidJellyfinConfiguration()) return users;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                var url = $"{AppSettings.Default.JellyfinIP}/Users";
                using var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var jellyfinUsers = JsonSerializer.Deserialize<List<JellyfinAuth>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (jellyfinUsers != null) users.AddRange(jellyfinUsers);
                    if (users.Count > 1) users.Add(new JellyfinAuth { Id = "everyone", Name = "Everyone" });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Error loading users: {ex.Message}"); }
            return users;
        }

        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath, string[] userIds)
        {
            string? tempDir = null;
            string? tempPackage = null;

            try
            {
                var baseDir = Path.GetDirectoryName(packagePath) ?? throw new InvalidOperationException("Invalid package path");
                tempDir = Path.Combine(baseDir, $"JellyTemp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                ZipFile.ExtractToDirectory(packagePath, tempDir);

                if (AppSettings.Default.UseServerScripts)
                {
                    // This now handles patching + polyfilling
                    await PatchServerSideIndexHtmlAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await AddOrUpdateCspAsync(tempDir, AppSettings.Default.JellyfinIP);
                }

                if (AppSettings.Default.ConfigUpdateMode.Contains("Server") || AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    await UpdateMultiServerConfig(tempDir);

                if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") || AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    await InjectUserSettingsScriptAsync(tempDir, userIds);

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
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                if (tempPackage != null && File.Exists(tempPackage)) File.Delete(tempPackage);
            }
        }

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

        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string remoteIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";
            string htmlContent = "";

            // 1. Fetch server HTML
            try
            {
                Debug.WriteLine($"Fetching index.html from {remoteIndexUrl}...");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                htmlContent = await _httpClient.GetStringAsync(remoteIndexUrl, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch server index.html: {ex.Message}. Using local fallback.");
                if (File.Exists(localIndexPath))
                    htmlContent = await File.ReadAllTextAsync(localIndexPath);
                else
                    return false;
            }

            // 2. Fix Base URL immediately
            if (htmlContent.Contains("<base"))
                htmlContent = Regex.Replace(htmlContent, @"<base[^>]+>", @"<base href=""."">", RegexOptions.IgnoreCase);
            else
                htmlContent = htmlContent.Replace("<head>", "<head><base href=\".\">");

            // 3. Process External Scripts (Download, Transpile, Link Locally)
            //    This fixes the "SyntaxError: Unexpected token ..."
            htmlContent = await ProcessExternalScriptsAsync(htmlContent, tempDirectory, serverUrl);

            // 4. Localize Assets (Server assets -> Local WGT)
            htmlContent = LocalizeAssetPaths(htmlContent);

            // 5. Inject Polyfills & Bootloader
            var bootloaderScript = new StringBuilder();

            // A. Inject Standard Polyfills (Fixes missing functions like Object.assign, Promise)
            bootloaderScript.AppendLine(GetPolyfillScript());

            // B. Tizen Bootloader
            bootloaderScript.AppendLine("<script src=\"tizen.js\"></script>");
            bootloaderScript.AppendLine("<script>");
            bootloaderScript.AppendLine($"   window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            bootloaderScript.AppendLine($"   window.appConfig = {{ servers: [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}] }};");
            bootloaderScript.AppendLine("</script>");

            // C. Dev Logging
            if (AppSettings.Default.EnableDevLogs)
            {
                bootloaderScript.AppendLine("<script>");
                bootloaderScript.AppendLine("    (function() {");
                bootloaderScript.AppendLine($"        var ws = new WebSocket('ws://{AppSettings.Default.LocalIp}:5001');");
                bootloaderScript.AppendLine("        var send = (t,d) => { try{ ws.send(JSON.stringify({ type:t, data:d })) } catch(e){} };");
                bootloaderScript.AppendLine("        console.log = (...a) => send('log', a);");
                bootloaderScript.AppendLine("        console.error = (...a) => send('error', a);");
                bootloaderScript.AppendLine("        window.onerror = (m,s,l,c,e) => send('error', [m,s,l,c]);");
                bootloaderScript.AppendLine("    })();");
                bootloaderScript.AppendLine("</script>");
            }

            htmlContent = Regex.Replace(htmlContent, "<head>", "<head>\n" + bootloaderScript.ToString(), RegexOptions.IgnoreCase);

            // 6. Clean CSP
            htmlContent = Regex.Replace(htmlContent, @"<meta[^>]*http-equiv=[""']Content-Security-Policy[""'][^>]*>", "", RegexOptions.IgnoreCase);
            string permissiveCsp = @"<meta http-equiv=""Content-Security-Policy"" content=""default-src * 'self' 'unsafe-inline' 'unsafe-eval' data: blob:;"">";
            htmlContent = htmlContent.Replace("</head>", permissiveCsp + "\n</head>");

            await File.WriteAllTextAsync(localIndexPath, htmlContent);
            return true;
        }

        private async Task<string> ProcessExternalScriptsAsync(string html, string tempDirectory, string serverUrl)
        {
            var jsDir = Path.Combine(tempDirectory, "www", "patched_plugins");
            Directory.CreateDirectory(jsDir);

            // Regex to find external scripts
            var regex = new Regex(@"<script[^>]+src=[""']((https?://|//)[^""']+)[""'][^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return await ReplaceAsync(html, regex, async (match) =>
            {
                string originalUrl = match.Groups[1].Value;
                if (originalUrl.StartsWith("//")) originalUrl = "http:" + originalUrl;

                try
                {
                    Debug.WriteLine($"Downloading and patching external script: {originalUrl}");

                    // Download
                    string jsContent = await _httpClient.GetStringAsync(originalUrl);

                    // --- TRANSPILATION (The Fix for "Unexpected token ...") ---

                    // 1. Fix Object Spread: { ...x } -> Object.assign({}, x)
                    // Note: This is a basic regex replacement. It handles simple cases common in plugins.
                    jsContent = Regex.Replace(jsContent, @"\{\s*\.\.\.([a-zA-Z0-9_]+)\s*\}", "Object.assign({}, $1)");
                    jsContent = Regex.Replace(jsContent, @"\{\s*\.\.\.([a-zA-Z0-9_]+)\s*,\s*", "Object.assign({}, $1, {");

                    // 2. Fix Arrow Functions: (a) => ... -> function(a) ...
                    // Converting simple arrow functions is safer than complex ones.
                    jsContent = Regex.Replace(jsContent, @"=\s*\(\)\s*=>", "= function() ");
                    jsContent = Regex.Replace(jsContent, @"=\s*([a-zA-Z0-9_]+)\s*=>", "= function($1) ");

                    // 3. Const/Let -> Var (Tizen 3.0 has partial support, mostly fine, but older ones fail)
                    jsContent = Regex.Replace(jsContent, @"\bconst\s+", "var ");
                    jsContent = Regex.Replace(jsContent, @"\blet\s+", "var ");

                    // -----------------------------------------------------------

                    string fileName = $"plugin_{Guid.NewGuid():N}.js";
                    string localPath = Path.Combine(jsDir, fileName);
                    await File.WriteAllTextAsync(localPath, jsContent);

                    // Return the new script tag pointing to our local, patched file
                    return $"<script src=\"patched_plugins/{fileName}\"></script>";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to patch script {originalUrl}: {ex.Message}");
                    // If we fail to download/patch, strip it to prevent the crash
                    return $"";
                }
            });
        }

        // Helper to handle async regex replacement
        private async Task<string> ReplaceAsync(string input, Regex regex, Func<Match, Task<string>> replacementFn)
        {
            var sb = new StringBuilder();
            var lastIndex = 0;
            foreach (Match match in regex.Matches(input))
            {
                sb.Append(input, lastIndex, match.Index - lastIndex);
                sb.Append(await replacementFn(match));
                lastIndex = match.Index + match.Length;
            }
            sb.Append(input, lastIndex, input.Length - lastIndex);
            return sb.ToString();
        }

        private string LocalizeAssetPaths(string htmlContent)
        {
            string Localize(Match m)
            {
                string prefix = m.Groups[1].Value;
                string url = m.Groups[2].Value;
                string suffix = m.Groups[3].Value;

                if (url.Contains("tizen.js")) return m.Value;

                if (url.Contains("/web/") || !url.Contains("://"))
                {
                    string filename = Path.GetFileName(url);
                    if (filename.Contains("?")) filename = filename.Substring(0, filename.IndexOf("?"));
                    return $"{prefix}{filename}{suffix}";
                }
                return m.Value;
            }

            htmlContent = Regex.Replace(htmlContent, @"(src=[""'])([^""']+\.js[^""']*)([""'])", Localize, RegexOptions.IgnoreCase);
            htmlContent = Regex.Replace(htmlContent, @"(href=[""'])([^""']+\.css[^""']*)([""'])", Localize, RegexOptions.IgnoreCase);
            return htmlContent;
        }

        private string GetPolyfillScript()
        {
            // A minimal, inline polyfill set for Tizen
            return @"
<script>
    // Minimal Polyfills for Tizen
    if (!Object.assign) {
        Object.assign = function(target) {
            for (var i = 1; i < arguments.length; i++) {
                var source = arguments[i];
                for (var key in source) {
                    if (Object.prototype.hasOwnProperty.call(source, key)) {
                        target[key] = source[key];
                    }
                }
            }
            return target;
        };
    }
    if (!String.prototype.includes) {
        String.prototype.includes = function(search, start) {
            if (typeof start !== 'number') start = 0;
            if (start + search.length > this.length) return false;
            return this.indexOf(search, start) !== -1;
        };
    }
    if (!Array.prototype.includes) {
        Object.defineProperty(Array.prototype, 'includes', {
            value: function(searchElement, fromIndex) {
                if (this == null) throw new TypeError('""this"" is null or not defined');
                var o = Object(this);
                var len = o.length >>> 0;
                if (len === 0) return false;
                var n = fromIndex | 0;
                var k = Math.max(n >= 0 ? n : len - Math.abs(n), 0);
                while (k < len) {
                    if (o[k] === searchElement) return true;
                    k++;
                }
                return false;
            }
        });
    }
    // Note: 'Promise' and 'fetch' are usually present in Tizen 3+, 
    // but if needed, a larger library like Bluebird or whatwg-fetch would be needed here.
    // This script ensures basic ES6 method compatibility.
</script>";
        }

        // ... InjectUserSettingsScriptAsync & AddOrUpdateCspAsync (Keep existing logic) ...
        public static async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;
            string htmlContent = await File.ReadAllTextAsync(indexPath);
            string BoolToJs(bool val) => val.ToString().ToLower();

            var sb = new StringBuilder();
            sb.AppendLine("");
            sb.AppendLine("<script>");
            sb.AppendLine("(function() {");
            sb.AppendLine("    try {");
            foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                sb.AppendLine($"        var u = '{userId}';");
                sb.AppendLine($"        if (!localStorage.getItem('injected-' + u)) {{");
                sb.AppendLine($"            localStorage.setItem(u + '-appTheme', '{AppSettings.Default.SelectedTheme ?? "dark"}');");
                sb.AppendLine($"            localStorage.setItem(u + '-enableBackdrops', '{BoolToJs(AppSettings.Default.EnableBackdrops)}');");
                sb.AppendLine($"            localStorage.setItem('injected-' + u, 'true');");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    } catch(e) { console.error('Settings injection failed', e); }");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");

            htmlContent = htmlContent.Replace("</body>", sb.ToString() + "\n</body>");
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
                    doc.Root.Add(new XElement(tizen + "access", new XAttribute("origin", "*"), new XAttribute("subdomains", "true")));
                else
                {
                    access.SetAttributeValue("origin", "*");
                    access.SetAttributeValue("subdomains", "true");
                }

                var requiredPrivileges = new[] {
                    "http://tizen.org/privilege/internet",
                    "http://tizen.org/privilege/network.get",
                    "http://tizen.org/privilege/filesystem.read",
                    "http://tizen.org/privilege/content.read"
                };
                foreach (var priv in requiredPrivileges)
                {
                    if (!doc.Root.Elements(tizen + "privilege").Any(x => x.Attribute("name")?.Value == priv))
                        doc.Root.Add(new XElement(tizen + "privilege", new XAttribute("name", priv)));
                }
                doc.Save(configPath);
                return true;
            }
            catch { return false; }
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