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
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinHelper
    {
        private readonly HttpClient _httpClient;
        private readonly ProcessHelper _processHelper;
        public JellyfinHelper(
            HttpClient httpClient, 
            ProcessHelper processHelper)
        {
            _httpClient = httpClient;
            _processHelper = processHelper;
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
        public async Task<InstallResult> ApplyJellyfinConfigAsync(
            string packagePath,
            string[] userIds)
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

    }
}
