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

                if (AppSettings.Default.EnableDevLogs)
                    await InjectDebugLoggerAsync(tempDir, AppSettings.Default.LocalIp, 5001);

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
        public static async Task InjectDebugLoggerAsync(string tempDirectory, string devPcIp, int port)
        {
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");

            if (!File.Exists(indexPath))
                return;

            string html = await File.ReadAllTextAsync(indexPath);

            // Remove previous injection
            html = Regex.Replace(
                html,
                @"<!--LOG-INJECT-START-->[\s\S]*?<!--LOG-INJECT-END-->",
                "",
                RegexOptions.Multiline);

            string script = $@"
<!--LOG-INJECT-START-->
<script>
(function() {{
    const logServer = ""ws://{devPcIp}:{port}"";
    window.ws = null;

    function send(msg) {{
        try {{
            if (window.ws && window.ws.readyState === WebSocket.OPEN) {{
                window.ws.send(JSON.stringify({{
                    time: Date.now(),
                    message: msg
                }}));
            }}
        }} catch(e) {{
            // ignore
        }}
    }}

    function connectLogger() {{
        try {{
            window.ws = new WebSocket(logServer);

            window.ws.onopen = () => {{
                console.log(""[Logger] Connected"");
                send(""Logger connected"");
                send(""==== TV DEBUG START ===="");
                send(""Location: "" + location.href);
                send(""BaseURI: "" + document.baseURI);
            }};

            window.ws.onclose = () => {{
                console.log(""[Logger] Closed, retrying..."");
                setTimeout(connectLogger, 5000);
            }};

            window.ws.onerror = (e) => {{
                console.log(""[Logger] Error"", e);
            }};
        }} catch(err) {{
            console.log(""[Logger] Exception"", err);
        }}
    }}

    // ---- OVERRIDE CONSOLE ----
    const origLog = console.log;
    console.log = function(...args) {{
        origLog.apply(console, args);
        send(""[LOG] "" + args.join("" ""));
    }};

    const origError = console.error;
    console.error = function(...args) {{
        origError.apply(console, args);
        send(""[ERROR] "" + args.join("" ""));
    }};

    // ---- UNCAUGHT ERRORS ----
    window.onerror = function(msg, src, line, col, err) {{
        send('[UNCAUGHT] ' + msg + ' @ ' + src + ':' + line);
    }};

    window.addEventListener(""unhandledrejection"", function(e) {{
        send(""[PROMISE REJECTION] "" + (e.reason?.message || e.reason));
    }});

    // ---- RESOURCE LOAD FAILURES ----
    window.addEventListener(""error"", function(e) {{
        let t = e.target || {{}};
        if (t.src || t.href) {{
            send(""[RESOURCE ERROR] "" + (t.src || t.href));
        }} else {{
            send(""[ERROR EVENT] "" + e.message);
        }}
    }}, true);

    // ---- FETCH DEBUG ----
    const origFetch = window.fetch;
    window.fetch = async function(...args) {{
        send(""[FETCH] "" + args[0]);
        try {{
            const res = await origFetch.apply(this, args);
            send(""[FETCH RESPONSE] "" + args[0] + "" -> "" + res.status);
            return res;
        }} catch(err) {{
            send(""[FETCH ERROR] "" + args[0] + "" -> "" + err);
            throw err;
        }}
    }};

    // ---- XHR DEBUG ----
    const origXHROpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {{
        send(""[XHR] "" + method + "" "" + url);
        return origXHROpen.apply(this, arguments);
    }};

    // ---- DOM CONTENT LOGGING ----
    document.addEventListener(""DOMContentLoaded"", () => {{
        send(""==== DOMContentLoaded ===="");

        document.querySelectorAll('script[src]').forEach(s =>
            send(""[SCRIPT SRC] "" + s.src)
        );

        document.querySelectorAll('link[href]').forEach(l =>
            send(""[LINK HREF] "" + l.href)
        );
    }});

    connectLogger();
})();
</script>
<!--LOG-INJECT-END-->
";

            // Insert just before </body>
            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</body>", script + "\n</body>");
            else
                html += script;

            await File.WriteAllTextAsync(indexPath, html);
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

            doc.Save(configPath);
            return true;
        }
        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath))
                return false;

            string html = await File.ReadAllTextAsync(indexPath);

            // Remove ANY existing CSP meta tag
            html = Regex.Replace(html,
                @"<meta[^>]*http-equiv=[""']Content-Security-Policy[""'][^>]*>",
                "",
                RegexOptions.IgnoreCase);

            // Extract base URL without trailing slash
            string baseUrl = serverUrl.TrimEnd('/');

            // Replace ALL script/src attributes with your server URL
            // This handles both local paths and already-http paths
            html = Regex.Replace(html,
                @"(src=[""'])(?!http|\/\/|\$WEBAPIS|data:|blob:)([^""']+\.(?:js|bundle\.js)[^""']*)([""'])",
                m => $"{m.Groups[1].Value}{baseUrl}/web/{m.Groups[2].Value}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase);

            // Replace ALL link/href attributes for CSS
            html = Regex.Replace(html,
                @"(href=[""'])(?!http|\/\/|\$WEBAPIS|data:|blob:)([^""']+\.css[^""']*)([""'])",
                m => $"{m.Groups[1].Value}{baseUrl}/web/{m.Groups[2].Value}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase);

            // Also replace manifest, icons, etc. if they're relative
            html = Regex.Replace(html,
                @"(href=[""'])(?!http|\/\/|data:|blob:)([^""']+\.(?:png|ico|svg)[^""']*)([""'])",
                m => $"{m.Groups[1].Value}{baseUrl}/web/{m.Groups[2].Value}{m.Groups[3].Value}",
                RegexOptions.IgnoreCase);

            // Add maximally permissive CSP meta tag
            string newCsp = @"
<meta http-equiv=""Content-Security-Policy"" 
      content=""default-src * file: data: blob: 'unsafe-inline' 'unsafe-eval';
               script-src * file: data: blob: 'unsafe-inline' 'unsafe-eval';
               connect-src * file: data: blob: ws: wss: http: https:;
               img-src * file: data: blob:;
               style-src * file: data: blob: 'unsafe-inline';
               font-src * file: data: blob:;
               media-src * file: data: blob:;
               frame-src *;
               worker-src * blob:;
               object-src *;"">
";

            // Insert after <head>
            html = html.Replace("<head>", "<head>\n" + newCsp);

            // Remove mobile-specific crap (optional but cleaner)
            html = Regex.Replace(html,
                @"<link[^>]*apple-touch-startup-image[^>]*>",
                "",
                RegexOptions.IgnoreCase);
            html = Regex.Replace(html,
                @"<meta[^>]*apple-mobile-web-app-capable[^>]*>",
                "",
                RegexOptions.IgnoreCase);
            html = Regex.Replace(html,
                @"<meta[^>]*mobile-web-app-capable[^>]*>",
                "",
                RegexOptions.IgnoreCase);

            await File.WriteAllTextAsync(indexPath, html);
            return true;
        }
    }
}
