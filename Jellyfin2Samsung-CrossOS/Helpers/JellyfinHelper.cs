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

        // ---------------------------------------------------------------------
        // BASIC VALIDATION
        // ---------------------------------------------------------------------

        public static bool IsValidJellyfinConfiguration()
        {
            return !string.IsNullOrEmpty(AppSettings.Default.JellyfinIP) &&
                   !string.IsNullOrEmpty(AppSettings.Default.JellyfinApiKey) &&
                   AppSettings.Default.JellyfinApiKey.Length == 32 &&
                   Uri.TryCreate($"{AppSettings.Default.JellyfinIP}/Users", UriKind.Absolute, out _);
        }

        // ---------------------------------------------------------------------
        // LOAD USERS FROM SERVER
        // ---------------------------------------------------------------------

        public async Task<List<JellyfinAuth>> LoadJellyfinUsersAsync()
        {
            var users = new List<JellyfinAuth>();

            if (!IsValidJellyfinConfiguration())
                return users;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                string url = $"{AppSettings.Default.JellyfinIP}/Users";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return users;

                var json = await response.Content.ReadAsStringAsync();

                var jellyUsers = JsonSerializer.Deserialize<List<JellyfinAuth>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (jellyUsers != null)
                {
                    users.AddRange(jellyUsers);
                    if (users.Count > 1)
                        users.Add(new JellyfinAuth { Id = "everyone", Name = "Everyone" });
                }
            }
            catch { }

            return users;
        }

        // ---------------------------------------------------------------------
        // MAIN PATCHING AND PACKAGING PIPELINE
        // ---------------------------------------------------------------------

        public async Task<InstallResult> ApplyJellyfinConfigAsync(string packagePath, string[] userIds)
        {
            string? tempDir = null;
            string? tempOutput = null;

            try
            {
                string baseDir = Path.GetDirectoryName(packagePath)!;

                tempDir = Path.Combine(baseDir, $"JellyTemp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                ZipFile.ExtractToDirectory(packagePath, tempDir);

                // -----------------------------------------------------------------
                // SELF-CONTAINED MODE → Download all /web assets INTO /www
                // -----------------------------------------------------------------
                if (AppSettings.Default.UseServerScripts)
                {
                    await DownloadAllWebAssetsAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await AddOrUpdateCspAsync(tempDir, AppSettings.Default.JellyfinIP);
                    await PatchIndexHtmlForLocalModeAsync(tempDir);
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

                // Update Jellyfin server-side user config (auto-login, language, etc.)
                await UpdateJellyfinUsersAsync(userIds);

                // -----------------------------------------------------------------
                // REPACKAGE WGT
                // -----------------------------------------------------------------
                tempOutput = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(packagePath)}_mod.wgt");
                if (File.Exists(tempOutput)) File.Delete(tempOutput);

                ZipFile.CreateFromDirectory(tempDir, tempOutput);

                // Replace original package
                File.Delete(packagePath);
                File.Move(tempOutput, packagePath);

                return InstallResult.SuccessResult();
            }
            catch (Exception ex)
            {
                return InstallResult.FailureResult($"Error modifying Jellyfin package: {ex.Message}");
            }
            finally
            {
                if (tempDir != null && Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                if (tempOutput != null && File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }


        // ---------------------------------------------------------------------
        // DOWNLOAD ENTIRE /web DIRECTORY INTO /www
        // ---------------------------------------------------------------------

        public async Task DownloadAllWebAssetsAsync(string tempDirectory, string serverUrl)
        {
            string baseUrl = serverUrl.TrimEnd('/') + "/web/";
            string targetRoot = Path.Combine(tempDirectory, "www");

            using var client = new HttpClient();

            string manifestJson = await client.GetStringAsync(baseUrl + "manifest.json");
            var manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifestJson);

            if (manifest == null || !manifest.ContainsKey("files"))
                throw new Exception("manifest.json missing 'files' entry");

            var fileList = manifest["files"].EnumerateArray()
                .Select(v => v.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var relativePath in fileList)
            {
                try
                {
                    string remote = baseUrl + relativePath;
                    string local = Path.Combine(targetRoot, relativePath.Replace("/", "\\"));

                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);

                    byte[] data = await client.GetByteArrayAsync(remote);
                    await File.WriteAllBytesAsync(local, data);

                    Debug.WriteLine($"Downloaded {relativePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to download {relativePath}: {ex.Message}");
                }
            }
        }


        // ---------------------------------------------------------------------
        // PATCH INDEX.HTML FOR LOCAL/OFFLINE OPERATION
        // ---------------------------------------------------------------------

        public async Task PatchIndexHtmlForLocalModeAsync(string tempDirectory)
        {
            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath))
                return;

            string html = await File.ReadAllTextAsync(indexPath);

            //
            // 1. Rewrite remote /web URLs → local filenames
            //
            html = Regex.Replace(
                html,
                @"(src=[""'])(https?:\/\/[^""']+\/web\/)([^""']+)([""'])",
                "$1$3$4",
                RegexOptions.IgnoreCase);

            html = Regex.Replace(
                html,
                @"(href=[""'])(https?:\/\/[^""']+\/web\/)([^""']+)([""'])",
                "$1$3$4",
                RegexOptions.IgnoreCase);

            //
            // 2. Inject local tizen.js and DEBUG LOGGER at the top of <head>
            //
            var injection = new StringBuilder();

            injection.AppendLine("<head>");
            injection.AppendLine("<script src=\"../tizen.js\"></script>");

            if (AppSettings.Default.EnableDevLogs)
            {
                injection.AppendLine("<script>");
                injection.AppendLine("(function(){");
                injection.AppendLine($"   var ws = new WebSocket('ws://{AppSettings.Default.LocalIp}:5001');");
                injection.AppendLine("   var send = (t,d) => { try { ws.send(JSON.stringify({ type:t, data:d })); } catch(e){} };");
                injection.AppendLine("   console.log = (...a) => send('log', a);");
                injection.AppendLine("   console.error = (...a) => send('error', a);");
                injection.AppendLine("   window.onerror = (m,s,l,c,e) => send('error', [m,s,l,c]);");
                injection.AppendLine("})();");
                injection.AppendLine("</script>");
            }

            // apply head patch
            html = Regex.Replace(html, "<head>", injection.ToString(), RegexOptions.IgnoreCase);

            //
            // 3. Save back
            //
            await File.WriteAllTextAsync(indexPath, html);
        }

        // ---------------------------------------------------------------------
        // PATCH CONFIG.JSON → SINGLE SERVER MODE
        // ---------------------------------------------------------------------

        public static async Task UpdateMultiServerConfig(string tempDirectory)
        {
            string configPath = Path.Combine(tempDirectory, "www", "config.json");
            if (!File.Exists(configPath)) return;

            var json = await File.ReadAllTextAsync(configPath);
            var node = JsonNode.Parse(json)!;

            node["multiserver"] = false;
            node["servers"] = new JsonArray { JsonValue.Create(AppSettings.Default.JellyfinIP) };

            await File.WriteAllTextAsync(configPath,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }


        // ---------------------------------------------------------------------
        // INJECT LOCAL USER SETTINGS (THEMES, PREFERENCES)
        // ---------------------------------------------------------------------

        public static async Task InjectUserSettingsScriptAsync(string tempDirectory, string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            string indexPath = Path.Combine(tempDirectory, "www", "index.html");
            if (!File.Exists(indexPath)) return;

            string html = await File.ReadAllTextAsync(indexPath);

            if (html.Contains("<!-- SAMSUNG_JELLYFIN_AUTO_INJECTED -->"))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("<!-- SAMSUNG_JELLYFIN_AUTO_INJECTED -->");
            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");

            foreach (var userId in userIds)
            {
                if (string.IsNullOrWhiteSpace(userId))
                    continue;

                sb.AppendLine($"localStorage.setItem('{userId}-enableBackdrops', '{AppSettings.Default.EnableBackdrops.ToString().ToLower()}');");
                sb.AppendLine($"localStorage.setItem('{userId}-enableThemeSongs', '{AppSettings.Default.EnableThemeSongs.ToString().ToLower()}');");
                sb.AppendLine($"localStorage.setItem('{userId}-enableThemeVideos', '{AppSettings.Default.EnableThemeVideos.ToString().ToLower()}');");
                sb.AppendLine($"localStorage.setItem('{userId}-nextUpEnabled', '{AppSettings.Default.NextUpEnabled.ToString().ToLower()}');");
            }

            sb.AppendLine("})();");
            sb.AppendLine("</script>");

            html = html.Replace("<head>", "<head>\n" + sb.ToString());

            await File.WriteAllTextAsync(indexPath, html);
        }


        // ---------------------------------------------------------------------
        // UPDATE USER SETTINGS DIRECTLY ON THE JELLYFIN SERVER
        // ---------------------------------------------------------------------

        public async Task UpdateJellyfinUsersAsync(string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization",
                    $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");

                foreach (var userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    try
                    {
                        // Fetch user data
                        var getUser = await _httpClient.GetAsync($"{AppSettings.Default.JellyfinIP}/Users/{userId}");
                        getUser.EnsureSuccessStatusCode();

                        var userJson = await getUser.Content.ReadAsStringAsync();
                        var userNode = JsonNode.Parse(userJson)!;

                        // Modify auto-login
                        userNode["EnableAutoLogin"] = AppSettings.Default.UserAutoLogin;

                        var userContent = new StringContent(
                            userNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                            Encoding.UTF8,
                            "application/json");

                        await _httpClient.PostAsync($"{AppSettings.Default.JellyfinIP}/Users?userId={userId}", userContent);

                        // Additional config
                        var cfg = new
                        {
                            AppSettings.Default.PlayDefaultAudioTrack,
                            AppSettings.Default.SubtitleLanguagePreference,
                            SubtitleMode = AppSettings.Default.SelectedSubtitleMode,
                            AppSettings.Default.RememberAudioSelections,
                            AppSettings.Default.RememberSubtitleSelections,
                            EnableNextEpisodeAutoPlay = AppSettings.Default.AutoPlayNextEpisode
                        };

                        var cfgJson = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                        var cfgContent = new StringContent(cfgJson, Encoding.UTF8, "application/json");

                        await _httpClient.PostAsync($"{AppSettings.Default.JellyfinIP}/Users/Configuration?userId={userId}", cfgContent);
                    }
                    catch (Exception userEx)
                    {
                        Debug.WriteLine($"User config update failed for {userId}: {userEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"General user config update error: {ex.Message}");
            }
        }


        // ---------------------------------------------------------------------
        // PATCH CSP + PRIVILEGES IN config.xml
        // ---------------------------------------------------------------------

        public async Task<bool> AddOrUpdateCspAsync(string tempDirectory, string serverUrl)
        {
            string configPath = Path.Combine(tempDirectory, "config.xml");
            if (!File.Exists(configPath))
                return false;

            var doc = XDocument.Load(configPath);
            XNamespace tizen = "http://tizen.org/ns/widgets";

            // CSP
            string csp =
                "default-src * 'unsafe-inline' 'unsafe-eval' data: blob:; " +
                "script-src * 'unsafe-inline' 'unsafe-eval' data: blob:; " +
                "style-src * 'unsafe-inline' data: blob:; " +
                "img-src * data: blob:; " +
                "connect-src * http: https: ws: wss:;";

            var cspEl = doc.Root.Elements(tizen + "content-security-policy").FirstOrDefault();
            if (cspEl == null)
                doc.Root.Add(new XElement(tizen + "content-security-policy", csp));
            else
                cspEl.Value = csp;

            // Privileges
            var required = new[]
            {
                "http://tizen.org/privilege/internet",
                "http://tizen.org/privilege/network.get",
                "http://tizen.org/privilege/tv.inputdevice",
                "http://developer.samsung.com/privilege/productinfo"
            };

            var existing = doc.Root.Elements(tizen + "privilege")
                                   .Select(x => x.Attribute("name")?.Value)
                                   .ToList();

            foreach (var priv in required)
                if (!existing.Contains(priv))
                    doc.Root.Add(new XElement(tizen + "privilege", new XAttribute("name", priv)));

            doc.Save(configPath);
            return true;
        }
    }
}
