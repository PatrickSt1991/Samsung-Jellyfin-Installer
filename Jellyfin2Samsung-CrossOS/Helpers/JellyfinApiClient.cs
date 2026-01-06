using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class JellyfinApiClient
    {
        private readonly HttpClient _httpClient;

        public JellyfinApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public static bool IsValidJellyfinConfiguration()
        {
            return !string.IsNullOrEmpty(AppSettings.Default.JellyfinFullUrl) &&
                   !string.IsNullOrEmpty(AppSettings.Default.JellyfinApiKey) &&
                   AppSettings.Default.JellyfinApiKey.Length == 32 &&
                   IsValidUrl($"{AppSettings.Default.JellyfinFullUrl}/Users");
        }

        public static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public async Task<List<JellyfinAuth>> LoadUsersAsync()
        {
            var users = new List<JellyfinAuth>();
            if (!IsValidJellyfinConfiguration()) return users;

            try
            {
                SetupHeaders();
                using var response = await _httpClient.GetAsync($"{AppSettings.Default.JellyfinFullUrl}/Users");
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
                Trace.WriteLine($"Error loading users: {ex}");
            }

            return users;
        }

        public async Task<List<JellyfinPluginInfo>> GetInstalledPluginsAsync(string serverUrl)
        {
            var list = new List<JellyfinPluginInfo>();
            try
            {
                string url = serverUrl.TrimEnd('/') + "/Plugins";
                Trace.WriteLine("▶ Fetching installed plugins from: " + url);
                var json = await _httpClient.GetStringAsync(url);
                var parsed = JsonSerializer.Deserialize<List<JellyfinPluginInfo>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                    list.AddRange(parsed);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("⚠ Failed to fetch /Plugins: " + ex);
            }

            return list;
        }
        public async Task<JellyfinPublicSystemInfo?> GetPublicSystemInfoAsync(string serverUrl)
        {
            try
            {
                string url = serverUrl.TrimEnd('/') + "/System/Info/Public";
                Trace.WriteLine("▶ Fetching Jellyfin public system info from: " + url);

                var json = await _httpClient.GetStringAsync(url);

                var info = JsonSerializer.Deserialize<JellyfinPublicSystemInfo>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return info;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("⚠ Failed to fetch /System/Info/Public: " + ex);
                return null;
            }
        }

        public async Task UpdateUserConfigurationsAsync(string[] userIds)
        {
            if (userIds == null || userIds.Length == 0)
                return;

            try
            {
                SetupHeaders();

                foreach (string userId in userIds.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    try
                    {
                        // Fetch user info
                        var getUserResponse = await _httpClient.GetAsync($"{AppSettings.Default.JellyfinFullUrl}/Users/{userId}");
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
                            var userResponse = await _httpClient.PostAsync($"{AppSettings.Default.JellyfinFullUrl}/Users?userId={userId}", userContent);
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
                        var configResponse = await _httpClient.PostAsync($"{AppSettings.Default.JellyfinFullUrl}/Users/Configuration?userId={userId}", configContent);
                        configResponse.EnsureSuccessStatusCode();
                    }
                    catch (Exception userEx)
                    {
                        Trace.WriteLine($"Failed to update configuration for user {userId}: {userEx}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"General error updating user configurations: {ex}");
            }
        }

        private void SetupHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{AppSettings.Default.JellyfinApiKey}\"");
        }

        /// <summary>
        /// Authenticates with Jellyfin using username and password.
        /// Returns the access token and user ID on success.
        /// </summary>
        public async Task<(string? accessToken, string? userId, string? error)> AuthenticateAsync(string username, string password)
        {
            try
            {
                var serverUrl = AppSettings.Default.JellyfinFullUrl.TrimEnd('/');
                var authUrl = $"{serverUrl}/Users/AuthenticateByName";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-Emby-Authorization",
                    "MediaBrowser Client=\"Samsung Jellyfin Installer\", Device=\"PC\", DeviceId=\"samsungjellyfin\", Version=\"1.0.0\"");

                var authPayload = new
                {
                    Username = username,
                    Pw = password
                };

                var json = JsonSerializer.Serialize(authPayload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(authUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var authResponse = JsonNode.Parse(responseJson);

                    var accessToken = authResponse?["AccessToken"]?.GetValue<string>();
                    var userId = authResponse?["User"]?["Id"]?.GetValue<string>();

                    return (accessToken, userId, null);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Trace.WriteLine($"Authentication failed: {response.StatusCode} - {errorContent}");
                    return (null, null, $"Authentication failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Authentication error: {ex}");
                return (null, null, ex.Message);
            }
        }

        /// <summary>
        /// Tests if the current server URL is reachable by checking the public system info endpoint.
        /// </summary>
        public async Task<bool> TestServerConnectionAsync()
        {
            try
            {
                var serverUrl = AppSettings.Default.JellyfinFullUrl.TrimEnd('/');
                var testUrl = $"{serverUrl}/System/Info/Public";

                _httpClient.DefaultRequestHeaders.Clear();
                var response = await _httpClient.GetAsync(testUrl);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}