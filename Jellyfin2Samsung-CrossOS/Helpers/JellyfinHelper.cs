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
                            users.Add(new JellyfinAuth { Id = "everyone", Name = "Everyone" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading Jellyfin users: {ex.Message}");
            }

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

                // 1. Extract the WGT (which contains the raw web client assets)
                ZipFile.ExtractToDirectory(packagePath, tempDir);

                // 2. Perform Transformations
                if (AppSettings.Default.UseServerScripts)
                {
                    // Fetch index.html from server but patch it to load assets locally
                    await PatchServerSideIndexHtmlAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await AddOrUpdateCspAsync(tempDir, AppSettings.Default.JellyfinIP);
                }

                // Ensure config.json points to the correct server
                if (AppSettings.Default.ConfigUpdateMode.Contains("Server") || AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    await UpdateMultiServerConfig(tempDir);

                // Inject specific user settings (Themes, etc.)
                if (AppSettings.Default.ConfigUpdateMode.Contains("Browser") || AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    await InjectUserSettingsScriptAsync(tempDir, userIds);

                // 3. Re-package
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

            // Create config if missing (robustness)
            if (!File.Exists(configPath))
            {
                var defaultConfig = new JsonObject { ["servers"] = new JsonArray(), ["multiserver"] = false };
                await File.WriteAllTextAsync(configPath, defaultConfig.ToJsonString());
            }

            string jsonText = await File.ReadAllTextAsync(configPath);
            var config = JsonNode.Parse(jsonText) ?? new JsonObject();

            // Force the app to treat this specific server as the "home" server
            config["multiserver"] = false;

            // Ensure the URL is clean (no trailing slash)
            string serverUrl = AppSettings.Default.JellyfinIP.TrimEnd('/');
            config["servers"] = new JsonArray { JsonValue.Create(serverUrl) };

            await File.WriteAllTextAsync(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public async Task<bool> PatchServerSideIndexHtmlAsync(string tempDirectory, string serverUrl)
        {
            string localIndexPath = Path.Combine(tempDirectory, "www", "index.html");
            string remoteIndexUrl = serverUrl.TrimEnd('/') + "/web/index.html";
            string htmlContent = "";

            // 1. Try to fetch the Index HTML from the server
            //    This gets us the server-side injections (dashboardVersion, etc.)
            try
            {
                Debug.WriteLine($"Fetching bootloader (index.html) from {remoteIndexUrl}...");
                // Short timeout so we don't hang if server is down during install
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                htmlContent = await _httpClient.GetStringAsync(remoteIndexUrl, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch server index.html: {ex.Message}. Falling back to local stock index.");
                if (File.Exists(localIndexPath))
                {
                    htmlContent = await File.ReadAllTextAsync(localIndexPath);
                }
                else
                {
                    return false;
                }
            }

            // 2. "Bootloader" Transformation
            //    We have the server's HTML structure, but we want to load the JS/CSS chunks
            //    from the local .wgt file (file://) to keep it fast.

            // A. Fix the <base> tag. Server usually sends <base href="/web/"> which breaks local loading.
            //    We replace it with "." to ensure relative paths work.
            if (htmlContent.Contains("<base"))
            {
                htmlContent = Regex.Replace(htmlContent, @"<base[^>]+>", @"<base href=""."">", RegexOptions.IgnoreCase);
            }
            else
            {
                // If no base tag, ensure head exists and add one (safeguard)
                htmlContent = htmlContent.Replace("<head>", "<head><base href=\".\">");
            }

            // B. Localize Scripts and Styles
            //    Convert '/web/main.bundle.js' -> 'main.bundle.js'
            //    Convert 'https://server/web/main.js' -> 'main.js'

            string LocalizePath(Match m)
            {
                // Group 1: src=" or href="
                // Group 2: The URL
                // Group 3: The closing quote
                string prefix = m.Groups[1].Value;
                string url = m.Groups[2].Value;
                string suffix = m.Groups[3].Value;

                if (url.Contains("tizen.js")) return m.Value; // Don't touch tizen.js if it exists

                // If it points to our server's web dir, strip it to be a filename
                if (url.Contains("/web/"))
                {
                    string filename = Path.GetFileName(url);
                    // Query strings (like ?v=10.8) are fine locally usually, but safer to keep
                    // If the .wgt matches the server version, the filenames match.
                    return $"{prefix}{filename}{suffix}";
                }

                return m.Value;
            }

            // Rewrite script src
            htmlContent = Regex.Replace(htmlContent,
                @"(src=[""'])([^""']+\.js[^""']*)([""'])",
                LocalizePath, RegexOptions.IgnoreCase);

            // Rewrite css href
            htmlContent = Regex.Replace(htmlContent,
                @"(href=[""'])([^""']+\.css[^""']*)([""'])",
                LocalizePath, RegexOptions.IgnoreCase);

            // C. Inject Tizen Bootloader Logic
            //    We need tizen.js and we want to enforce the server URL via JS 
            //    in case config.json is ignored by the server-provided HTML structure.

            var bootloaderScript = new StringBuilder();
            bootloaderScript.AppendLine("<script src=\"tizen.js\"></script>");
            bootloaderScript.AppendLine("<script>");
            bootloaderScript.AppendLine("    // SamsungJellyfinInstaller Bootloader Patch");
            bootloaderScript.AppendLine($"   window.tizenServerUrl = '{serverUrl.TrimEnd('/')}';");
            bootloaderScript.AppendLine("    window.appConfig = window.appConfig || {};");
            bootloaderScript.AppendLine($"   window.appConfig.servers = [{{ url: '{serverUrl.TrimEnd('/')}', name: 'Jellyfin Server' }}];");
            bootloaderScript.AppendLine("</script>");

            if (AppSettings.Default.EnableDevLogs)
            {
                // Inject WebSocket logger for debugging on TV
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

            // D. Insert into Head
            htmlContent = Regex.Replace(htmlContent, "<head>", "<head>\n" + bootloaderScript.ToString(), RegexOptions.IgnoreCase);

            // E. Clean CSP (Content Security Policy)
            //    Remove existing meta CSP to prevent conflicts, we add our own later via config.xml or here
            htmlContent = Regex.Replace(htmlContent, @"<meta[^>]*http-equiv=[""']Content-Security-Policy[""'][^>]*>", "", RegexOptions.IgnoreCase);

            // Add permissive CSP for Tizen
            string permissiveCsp = @"<meta http-equiv=""Content-Security-Policy"" content=""default-src * 'self' 'unsafe-inline' 'unsafe-eval' data: blob:;"">";
            htmlContent = htmlContent.Replace("</head>", permissiveCsp + "\n</head>");

            await File.WriteAllTextAsync(localIndexPath, htmlContent);
            return true;
        }

        public static async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0) return;

            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string htmlContent = await File.ReadAllTextAsync(indexPath);

            // Simple helper for bools
            string BoolToJs(bool val) => val.ToString().ToLower();

            var sb = new StringBuilder();
            sb.AppendLine("");
            sb.AppendLine("<script>");
            sb.AppendLine("(function() {");
            sb.AppendLine("    try {");
            foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                // We use localStorage injection so when the app loads, it finds these preferences already set
                sb.AppendLine($"        var u = '{userId}';");
                sb.AppendLine($"        if (!localStorage.getItem('injected-' + u)) {{");
                sb.AppendLine($"            localStorage.setItem(u + '-appTheme', '{AppSettings.Default.SelectedTheme ?? "dark"}');");
                sb.AppendLine($"            localStorage.setItem(u + '-enableBackdrops', '{BoolToJs(AppSettings.Default.EnableBackdrops)}');");
                sb.AppendLine($"            localStorage.setItem(u + '-enableThemeSongs', '{BoolToJs(AppSettings.Default.EnableThemeSongs)}');");
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

                // Update Access Origin to allow everything
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

                // Update Privileges
                var requiredPrivileges = new[]
                {
                    "http://tizen.org/privilege/internet",
                    "http://tizen.org/privilege/network.get",
                    "http://tizen.org/privilege/tv.inputdevice",
                    "http://developer.samsung.com/privilege/productinfo",
                    "http://tizen.org/privilege/filesystem.read", // Sometimes needed for local chunks
                    "http://tizen.org/privilege/content.read"
                };

                foreach (var priv in requiredPrivileges)
                {
                    if (!doc.Root.Elements(tizen + "privilege").Any(x => x.Attribute("name")?.Value == priv))
                    {
                        doc.Root.Add(new XElement(tizen + "privilege", new XAttribute("name", priv)));
                    }
                }

                doc.Save(configPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating config.xml: {ex.Message}");
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