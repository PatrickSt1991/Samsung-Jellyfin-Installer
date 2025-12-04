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
        public JellyfinHelper(
            HttpClient httpClient)
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

            if (!IsValidJellyfinConfiguration())
            {
                Debug.WriteLine("Invalid Jellyfin configuration - skipping user load");
                return users;
            }

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                var url = $"{AppSettings.Default.JellyfinIP}/Users";
                Debug.WriteLine($"Attempting to load users from: {url}");

                using var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var jellyfinUsers = JsonSerializer.Deserialize<List<JellyfinAuth>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (jellyfinUsers != null && jellyfinUsers.Any())
                    {
                        users.AddRange(jellyfinUsers);

                        if (users.Count > 1)
                        {
                            users.Add(new JellyfinAuth
                            {
                                Id = "everyone",
                                Name = "Everyone"
                            });
                        }

                        Debug.WriteLine($"Successfully loaded {users.Count} users");
                    }
                    else
                    {
                        Debug.WriteLine("No users found in response");
                    }
                }
                else
                {
                    Debug.WriteLine($"Failed to load users - Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"HTTP error loading Jellyfin users: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"Request timeout loading Jellyfin users: {ex.Message}");
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error loading Jellyfin users: {ex.Message}");
            }

            return users;
        }
        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath,string[] userIds)
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
                    await AddOrUpdateCspAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await PatchServerSideIndexHtmlAsync(tempDir, AppSettings.Default.JellyfinIP);
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
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                if (tempPackage != null && File.Exists(tempPackage))
                    File.Delete(tempPackage);
            }
        }
        public static async Task UpdateMultiServerConfig(string tempDirectory)
        {
            string configPath = Path.Combine(tempDirectory, "www", "config.json");

            if (!File.Exists(configPath))
                throw new FileNotFoundException("Jellyfin config.json not found", configPath);

            string jsonText = await File.ReadAllTextAsync(configPath);

            if (string.IsNullOrWhiteSpace(jsonText))
                throw new InvalidDataException("config.json is empty or invalid");

            var config = JsonNode.Parse(jsonText) ?? throw new JsonException("Failed to parse config.json");
            config["multiserver"] = false;

            string serverUrl = AppSettings.Default.JellyfinIP;
            config["servers"] = new JsonArray { JsonValue.Create(serverUrl) };

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(configPath, config.ToJsonString(options));
        }
        public static async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            string indexPath = Path.Combine(tempDirectory, "index.html");

            if (!File.Exists(indexPath))
                throw new FileNotFoundException("index.html not found", indexPath);

            string htmlContent = await File.ReadAllTextAsync(indexPath);

            const string injectionMarker = "<!-- SAMSUNG_JELLYFIN_AUTO_INJECTED -->";
            if (htmlContent.Contains(injectionMarker))
                return;

            string BoolToJsString(bool value) => value.ToString().ToLower();

            var scriptBuilder = new StringBuilder();
            scriptBuilder.AppendLine(injectionMarker);
            scriptBuilder.AppendLine("<script>");
            scriptBuilder.AppendLine("(function() {");

            foreach (string userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                scriptBuilder.AppendLine($"    // Settings for user: {userId}");
                scriptBuilder.AppendLine($"    var userId = '{userId}';");
                scriptBuilder.AppendLine();
                scriptBuilder.AppendLine("    if (localStorage.getItem('samsung-jellyfin-injected-' + userId)) return;");
                scriptBuilder.AppendLine();
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-appTheme', '{AppSettings.Default.SelectedTheme ?? "dark"}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-enableBackdrops', '{BoolToJsString(AppSettings.Default.EnableBackdrops)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-enableThemeSongs', '{BoolToJsString(AppSettings.Default.EnableThemeSongs)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-enableThemeVideos', '{BoolToJsString(AppSettings.Default.EnableThemeVideos)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-backdropScreensaver', '{BoolToJsString(AppSettings.Default.BackdropScreensaver)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-detailsBanner', '{BoolToJsString(AppSettings.Default.DetailsBanner)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-cinemaMode', '{BoolToJsString(AppSettings.Default.CinemaMode)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-nextUpEnabled', '{BoolToJsString(AppSettings.Default.NextUpEnabled)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-enableExternalVideoPlayers', '{BoolToJsString(AppSettings.Default.EnableExternalVideoPlayers)}');");
                scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-skipIntros', '{BoolToJsString(AppSettings.Default.SkipIntros)}');");
                scriptBuilder.AppendLine();

                // Language preferences
                if (!string.IsNullOrWhiteSpace(AppSettings.Default.AudioLanguagePreference))
                    scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-audioLanguagePreference', '{AppSettings.Default.AudioLanguagePreference}');");

                if (!string.IsNullOrWhiteSpace(AppSettings.Default.SubtitleLanguagePreference))
                    scriptBuilder.AppendLine($"    localStorage.setItem(userId + '-subtitleLanguagePreference', '{AppSettings.Default.SubtitleLanguagePreference}');");

                // Mark injected
                scriptBuilder.AppendLine("    localStorage.setItem('samsung-jellyfin-injected-' + userId, 'true');");
                scriptBuilder.AppendLine();
            }

            scriptBuilder.AppendLine("})();");
            scriptBuilder.AppendLine("</script>");

            htmlContent = htmlContent.Replace("<head>", "<head>" + scriptBuilder.ToString());
            await File.WriteAllTextAsync(indexPath, htmlContent);
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
        public async Task<bool> AddOrUpdateCspAsync(string tempDirectory, string serverUrl)
        {
            string configPath = Path.Combine(tempDirectory, "config.xml");
            if (!File.Exists(configPath))
                return false;

            XDocument doc = XDocument.Load(configPath);
            XNamespace tizen = "http://tizen.org/ns/widgets";

            // 1. Update CSP
            string csp =
                "default-src * file: data: blob: 'unsafe-inline' 'unsafe-eval'; " +
                "script-src * file: data: blob: 'unsafe-inline' 'unsafe-eval'; " +
                "connect-src * file: data: blob: ws: wss: http: https:; " +
                "img-src * file: data: blob:; " +
                "style-src * file: data: blob: 'unsafe-inline'; " +
                "font-src * file: data: blob:; " +
                "media-src * file: data: blob:; " +
                "frame-src *; " +
                "worker-src * blob:; " +
                "object-src *;";

            var cspElement = doc.Root.Elements(tizen + "content-security-policy").FirstOrDefault();

            if (cspElement == null)
                doc.Root.Add(new XElement(tizen + "content-security-policy", csp));
            else
                cspElement.Value = csp;

            // 2. Add internet privileges
            var privileges = doc.Root.Elements(tizen + "privilege")
                .Select(x => x.Attribute("name")?.Value)
                .ToList();

            var requiredPrivileges = new List<string>
    {
        "http://tizen.org/privilege/internet",
        "http://tizen.org/privilege/network.get",
        "http://tizen.org/privilege/tv.inputdevice",
        "http://developer.samsung.com/privilege/productinfo"
    };

            foreach (var priv in requiredPrivileges)
            {
                if (!privileges.Contains(priv))
                {
                    doc.Root.Add(new XElement(tizen + "privilege", new XAttribute("name", priv)));
                }
            }

            // 3. Update tizen:setting with ALL development-friendly attributes
            var settingElement = doc.Root.Elements(tizen + "setting").FirstOrDefault();

            if (settingElement == null)
            {
                // Create new with all attributes
                settingElement = new XElement(tizen + "setting");
                doc.Root.Add(settingElement);
            }

            // Define all settings we want for development
            var devSettings = new Dictionary<string, string>
            {
                { "screen-orientation", "none" },
                { "context-menu", "enable" },
                { "background-support", "enable" },
                { "encryption", "disable" },
                { "install-location", "auto" },
                { "hwkey-event", "enable" },
                { "allow-untrusted-cert", "enable" },
                { "external-link-policy", "allow" }, // Allow external links
                { "boot-time-app", "disable" },
                { "launch-mode", "single" }
            };

            foreach (var setting in devSettings)
            {
                var attr = settingElement.Attribute(setting.Key);
                if (attr != null)
                {
                    attr.Value = setting.Value;
                }
                else
                {
                    settingElement.Add(new XAttribute(setting.Key, setting.Value));
                }
            }

            // Inside AddOrUpdateCspAsync
            var accessElement = doc.Root.Elements(tizen + "access").FirstOrDefault();
            if (accessElement == null)
            {
                var access = new XElement(tizen + "access");
                access.Add(new XAttribute("origin", "*"));
                access.Add(new XAttribute("subdomains", "true"));
                doc.Root.Add(access);
            }

            doc.Save(configPath);
            return true;
        }
        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string remoteIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";
            string baseUrl = serverUrl.TrimEnd('/') + "/web/";
            string html = "";

            // 1. Download fresh index.html from server
            try
            {
                Debug.WriteLine($"Fetching fresh index.html from {remoteIndexUrl}...");
                html = await _httpClient.GetStringAsync(remoteIndexUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch server index.html: {ex.Message}");
                if (File.Exists(localIndexPath))
                    html = await File.ReadAllTextAsync(localIndexPath);
                else
                    return false;
            }

            // 2. Build a single <head> replacement block:
            //    - local ../tizen.js
            //    - early hook that rewrites dynamic script/link URLs
            var earlyScript = $@"
<script>
(function() {{
    var BASE = '{baseUrl}';

    function rewrite(url) {{
        try {{
            if (!url) return url;

            // absolute or special schemes: leave alone
            if (url.startsWith('http://') ||
                url.startsWith('https://') ||
                url.startsWith('file:') ||
                url.startsWith('//') ||
                url.startsWith('data:') ||
                url.startsWith('blob:')) {{
                return url;
            }}

            // strip leading slashes so '/web/x.js' -> 'web/x.js'
            while (url.charAt(0) === '/') {{
                url = url.substring(1);
            }}

            return BASE + url;
        }} catch(e) {{
            return url;
        }}
    }}

    // Log script/link 404s
    window.addEventListener('error', function(e) {{
        var t = e.target;
        if (t && (t.tagName === 'SCRIPT' || t.tagName === 'LINK')) {{
            try {{
                console.error('Resource 404:', t.src || t.href);
            }} catch (_e) {{}}
        }}
    }}, true);

    // Monkeypatch script.src
    try {{
        var sDesc = Object.getOwnPropertyDescriptor(HTMLScriptElement.prototype, 'src');
        if (sDesc && sDesc.set) {{
            Object.defineProperty(HTMLScriptElement.prototype, 'src', {{
                get: sDesc.get,
                set: function(v) {{ sDesc.set.call(this, rewrite(v)); }}
            }});
        }}
    }} catch (_e) {{}}

    // Monkeypatch link.href (for CSS)
    try {{
        var lDesc = Object.getOwnPropertyDescriptor(HTMLLinkElement.prototype, 'href');
        if (lDesc && lDesc.set) {{
            Object.defineProperty(HTMLLinkElement.prototype, 'href', {{
                get: lDesc.get,
                set: function(v) {{ lDesc.set.call(this, rewrite(v)); }}
            }});
        }}
    }} catch (_e) {{}}

    // Monkeypatch setAttribute as a safety net
    try {{
        var origSetAttr = Element.prototype.setAttribute;
        Element.prototype.setAttribute = function(name, value) {{
            if (name === 'src' && this.tagName === 'SCRIPT') {{
                return origSetAttr.call(this, name, rewrite(value));
            }}
            if (name === 'href' && this.tagName === 'LINK') {{
                return origSetAttr.call(this, name, rewrite(value));
            }}
            return origSetAttr.call(this, name, value);
        }};
    }} catch (_e) {{}}

    // Hint for any build that still reads this
    window.__webpack_public_path__ = BASE;
}})();
</script>";

            // If dev logs are enabled, add logger script right after early hook
            var headBlock = new StringBuilder();
            headBlock.AppendLine("<head>");
            headBlock.AppendLine("<script src=\"../tizen.js\"></script>");
            headBlock.AppendLine(earlyScript);

            if (AppSettings.Default.EnableDevLogs)
            {
                headBlock.AppendLine("<script>");
                headBlock.AppendLine("    (function() {");
                headBlock.AppendLine($"        var ws = new WebSocket('ws://{AppSettings.Default.LocalIp}:5001');");
                headBlock.AppendLine("        var send = (t,d) => { try{ ws.send(JSON.stringify({ type:t, data:d })) } catch(e){} };");
                headBlock.AppendLine("        console.log = (...a) => send('log', a);");
                headBlock.AppendLine("        console.error = (...a) => send('error', a);");
                headBlock.AppendLine("        window.onerror = (m,s,l,c,e) => send('error', [m,s,l,c]);");
                headBlock.AppendLine("    })();");
                headBlock.AppendLine("</script>");
            }

            // Single, unified <head> replacement (no double-replace)
            html = Regex.Replace(
                html,
                "<head>",
                headBlock.ToString(),
                RegexOptions.IgnoreCase
            );

            // 3. Rewrite static JS src (but skip tizen.js and already-absolute URLs)
            html = Regex.Replace(
                html,
                @"(src=[""'])([^""']+\.js[^""']*)([""'])",
                m =>
                {
                    var prefix = m.Groups[1].Value;
                    var file = m.Groups[2].Value;
                    var suffix = m.Groups[3].Value;

                    // leave tizen.js alone
                    if (file.EndsWith("tizen.js", StringComparison.OrdinalIgnoreCase))
                        return m.Value;

                    // leave absolute / protocol URLs alone
                    if (file.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                        file.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                        file.StartsWith("//"))
                        return m.Value;

                    // otherwise, rewrite relative path to server
                    return $"{prefix}{baseUrl}{file}{suffix}";
                },
                RegexOptions.IgnoreCase
            );

            // 4. Rewrite CSS href
            html = Regex.Replace(
                html,
                @"(href=[""'])(?!http|\/\/|data:|blob:)([^""']+\.css[^""']*)([""'])",
                m => $"{m.Groups[1].Value}{baseUrl}{m.Groups[2].Value}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase
            );

            // 5. Rewrite images / manifests
            html = Regex.Replace(
                html,
                @"(href=[""'])(?!http|\/\/|data:|blob:)([^""']+\.(?:png|ico|svg|json)[^""']*)([""'])",
                m => $"{m.Groups[1].Value}{baseUrl}{m.Groups[2].Value}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase
            );

            // 6. Remove any existing CSP and add permissive CSP
            html = Regex.Replace(
                html,
                @"<meta[^>]*http-equiv=[""']Content-Security-Policy[""'][^>]*>",
                "",
                RegexOptions.IgnoreCase
            );

            string newCsp = @"
<meta http-equiv=""Content-Security-Policy"" 
      content=""default-src * 'self' 'unsafe-inline' 'unsafe-eval' data: blob:;
               script-src * 'unsafe-inline' 'unsafe-eval' data: blob:;
               style-src * 'unsafe-inline' data: blob:;
               img-src * data: blob:;
               connect-src * ws: wss: http: https:;
               media-src * data: blob:;
               font-src * data: blob:;"">";

            html = html.Replace("</head>", newCsp + "\n</head>");

            // 7. Optional: keep the late Webpack patch as a best-effort extra
            string latePatch = $@"
<script>
try {{
    const wp = window.__webpack_require__;
    const newPath = '{baseUrl}';

    if (wp) {{
        wp.p = newPath;
        Object.defineProperty(wp, 'p', {{
            get: () => newPath,
            set: () => newPath,
            configurable: false
        }});
    }}

    window.__webpack_public_path__ = newPath;
}} catch(e) {{
    console.error('Late webpack path patch failed', e);
}}
</script>";

            html = Regex.Replace(html, "</body>", latePatch + "\n</body>", RegexOptions.IgnoreCase);

            // 8. Save modified index.html back into www/
            await File.WriteAllTextAsync(localIndexPath, html);
            return true;
        }

    }
}
