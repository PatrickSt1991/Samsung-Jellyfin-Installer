using Samsung_Jellyfin_Installer.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class JellyfinConfigViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;

        private string? _audioLanguagePreference;
        private string? _subtitleLanguagePreference;
        private string? _jellyfinServerIp;
        private string? _selectedTheme;
        private string? _selectedSubtitleMode;
        private string _jellyfinApiKey;
        private string _selectedUpdateMode;
        private string _selectedJellyfinPort;
        private bool _enableBackdrops;
        private bool _enableThemeSongs;
        private bool _enableThemeVideos;
        private bool _backdropScreensaver;
        private bool _detailsBanner;
        private bool _cinemaMode;
        private bool _nextUpEnabled;
        private bool _enableExternalVideoPlayers;
        private bool _skipIntros;
        private bool _autoPlayNextEpisode;
        private bool _rememberAudioSelections;
        private bool _rememberSubtitleSelections;
        private bool _playDefaultAudioTrack;
        private bool _userAutoLogin;
        private bool _apiKeyEnabled = false;
        private bool _apiKeySet = false;
        private ObservableCollection<JellyfinAuth> _availableJellyfinUsers;
        private JellyfinAuth _selectedJellyfinUser;

        public string AudioLanguagePreference
        {
            get => _audioLanguagePreference;
            set
            {
                if (_audioLanguagePreference != value)
                {
                    _audioLanguagePreference = value;
                    OnPropertyChanged(nameof(AudioLanguagePreference));

                    Settings.Default.AudioLanguagePreference = value;
                    Settings.Default.Save();
                }
            }
        }
        public string SubtitleLanguagePreference
        {
            get => _subtitleLanguagePreference;
            set
            {
                if (_subtitleLanguagePreference != value)
                {
                    _subtitleLanguagePreference = value;
                    OnPropertyChanged(nameof(SubtitleLanguagePreference));

                    Settings.Default.SubtitleLanguagePreference = value;
                    Settings.Default.Save();
                }
            }
        }
        public string JellyfinServerIp
        {
            get => _jellyfinServerIp;
            set
            {
                if (_jellyfinServerIp != value)
                {
                    _jellyfinServerIp = value;
                    OnPropertyChanged(nameof(JellyfinServerIp));
                    UpdateJellyfinAddress();
                }
            }
        }
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (_selectedTheme != value)
                {
                    _selectedTheme = value;
                    OnPropertyChanged(nameof(SelectedTheme));

                    Settings.Default.SelectedTheme = value;
                    Settings.Default.Save();
                }
            }
        }
        public string SelectedSubtitleMode
        {
            get => _selectedSubtitleMode;
            set
            {
                if (_selectedSubtitleMode != value)
                {
                    _selectedSubtitleMode = value;
                    OnPropertyChanged(nameof(SelectedSubtitleMode));

                    Settings.Default.SelectedSubtitleMode = value;
                    Settings.Default.Save();
                }
            }
        }
        public string JellyfinApiKey
        {
            get => _jellyfinApiKey;
            set
            {
                if (_jellyfinApiKey != value)
                {
                    _jellyfinApiKey = value;
                    OnPropertyChanged(nameof(JellyfinApiKey));

                    Settings.Default.JellyfinApiKey = value;
                    Settings.Default.Save();

                    UpdateApiKeyStatus();
                    _ = LoadJellyfinUsersAsync();
                }
            }
        }
        public string SelectedUpdateMode
        {
            get => _selectedUpdateMode;
            set
            {
                if (_selectedUpdateMode != value)
                {
                    _selectedUpdateMode = value;
                    OnPropertyChanged(nameof(SelectedUpdateMode));

                    ApiKeyEnabled =
                        !string.IsNullOrEmpty(value) &&
                        !value.Contains("Server") &&
                        !value.Contains("None");

                    Settings.Default.ConfigUpdateMode = value;
                    Settings.Default.Save();
                }
            }
        }
        public string SelectedJellyfinPort
        {
            get => _selectedJellyfinPort;
            set
            {
                if (_selectedJellyfinPort != value)
                {
                    _selectedJellyfinPort = value;
                    OnPropertyChanged(nameof(SelectedJellyfinPort));
                    UpdateJellyfinAddress();
                }
            }
        }
        public bool EnableBackdrops
        {
            get => _enableBackdrops;
            set
            {
                if (_enableBackdrops != value)
                {
                    _enableBackdrops = value;
                    OnPropertyChanged(nameof(EnableBackdrops));

                    Settings.Default.EnableBackdrops = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool EnableThemeSongs
        {
            get => _enableThemeSongs;
            set
            {
                if (_enableThemeSongs != value)
                {
                    _enableThemeSongs = value;
                    OnPropertyChanged(nameof(EnableThemeSongs));

                    Settings.Default.EnableThemeSongs = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool EnableThemeVideos
        {
            get => _enableThemeVideos;
            set
            {
                if (_enableThemeVideos != value)
                {
                    _enableThemeVideos = value;
                    OnPropertyChanged(nameof(EnableThemeVideos));

                    Settings.Default.EnableThemeVideos = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool BackdropScreensaver
        {
            get => _backdropScreensaver;
            set
            {
                if (_backdropScreensaver != value)
                {
                    _backdropScreensaver = value;
                    OnPropertyChanged(nameof(BackdropScreensaver));

                    Settings.Default.BackdropScreensaver = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool DetailsBanner
        {
            get => _detailsBanner;
            set
            {
                if (_detailsBanner != value)
                {
                    _detailsBanner = value;
                    OnPropertyChanged(nameof(DetailsBanner));

                    Settings.Default.DetailsBanner = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool CinemaMode
        {
            get => _cinemaMode;
            set
            {
                if (_cinemaMode != value)
                {
                    _cinemaMode = value;
                    OnPropertyChanged(nameof(CinemaMode));

                    Settings.Default.CinemaMode = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool NextUpEnabled
        {
            get => _nextUpEnabled;
            set
            {
                if (_nextUpEnabled != value)
                {
                    _nextUpEnabled = value;
                    OnPropertyChanged(nameof(NextUpEnabled));

                    Settings.Default.NextUpEnabled = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool EnableExternalVideoPlayers
        {
            get => _enableExternalVideoPlayers;
            set
            {
                if (_enableExternalVideoPlayers != value)
                {
                    _enableExternalVideoPlayers = value;
                    OnPropertyChanged(nameof(EnableExternalVideoPlayers));

                    Settings.Default.EnableExternalVideoPlayers = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool SkipIntros
        {
            get => _skipIntros;
            set
            {
                if (_skipIntros != value)
                {
                    _skipIntros = value;
                    OnPropertyChanged(nameof(SkipIntros));

                    Settings.Default.SkipIntros = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool AutoPlayNextEpisode
        {
            get => _autoPlayNextEpisode;
            set
            {
                if (_autoPlayNextEpisode != value)
                {
                    _autoPlayNextEpisode = value;
                    OnPropertyChanged(nameof(AutoPlayNextEpisode));

                    Settings.Default.AutoPlayNextEpisode = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool RememberAudioSelections
        {
            get => _rememberAudioSelections;
            set
            {
                if (_rememberAudioSelections != value)
                {
                    _rememberAudioSelections = value;
                    OnPropertyChanged(nameof(RememberAudioSelections));

                    Settings.Default.RememberAudioSelections = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool RememberSubtitleSelections
        {
            get => _rememberSubtitleSelections;
            set
            {
                if (_rememberSubtitleSelections != value)
                {
                    _rememberSubtitleSelections = value;
                    OnPropertyChanged(nameof(RememberSubtitleSelections));

                    Settings.Default.RememberSubtitleSelections = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool PlayDefaultAudioTrack
        {
            get => _playDefaultAudioTrack;
            set
            {
                if (_playDefaultAudioTrack != value)
                {
                    _playDefaultAudioTrack = value;
                    OnPropertyChanged(nameof(PlayDefaultAudioTrack));

                    Settings.Default.PlayDefaultAudioTrack = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool UserAutoLogin
        {
            get => _userAutoLogin;
            set
            {
                if (_userAutoLogin != value)
                {
                    _userAutoLogin = value;
                    OnPropertyChanged(nameof(UserAutoLogin));

                    Settings.Default.UserAutoLogin = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool ApiKeyEnabled
        {
            get => _apiKeyEnabled;
            set
            {
                if (_apiKeyEnabled != value)
                {
                    _apiKeyEnabled = value;
                    OnPropertyChanged(nameof(ApiKeyEnabled));
                }
            }
        }
        public bool ApiKeySet
        {
            get => _apiKeySet;
            set
            {
                if (_apiKeySet != value)
                {
                    _apiKeySet = value;
                    OnPropertyChanged(nameof(ApiKeySet));
                }
            }
        }
        public ObservableCollection<string> AvailableThemes { get; } =
        [
            "appletv",
            "blueradiance",
            "dark",
            "light",
            "purplehaze",
            "wmc"
        ];
        public ObservableCollection<string> AvailableSubtitleModes { get; } =
        [
            "None",
            "OnlyForced",
            "Default",
            "Always"
        ];
        public ObservableCollection<int> JellyfinPorts { get; } =
        [
            8096, 8920
        ];
        public ObservableCollection<string> AvailableUpdateModes { get; } =
        [
            "None",
            "Server Settings",
            "Browser Settings",
            "User Settings",
            "Server & Browser Settings",
            "Server & User Settings",
            "Browser & User Settings",
            "All Settings"
        ];
        public ObservableCollection<JellyfinAuth> AvailableJellyfinUsers
        {
            get => _availableJellyfinUsers;
            set
            {
                _availableJellyfinUsers = value;
                OnPropertyChanged(nameof(AvailableJellyfinUsers));
            }
        }
        public JellyfinAuth SelectedJellyfinUser
        {
            get => _selectedJellyfinUser;
            set
            {
                _selectedJellyfinUser = value;
                OnPropertyChanged(nameof(SelectedJellyfinUser));

                // Save selected user ID
                if (value != null)
                {
                    Settings.Default.JellyfinUserId = value.Id;
                    Settings.Default.Save();
                }
            }
        }

        public JellyfinConfigViewModel(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");

            AvailableJellyfinUsers = [];

            // Initialize with default values if settings are empty
            var jellyfinIP = Settings.Default.JellyfinIP;
            if (!string.IsNullOrWhiteSpace(jellyfinIP) && jellyfinIP.Contains(':'))
            {
                var parts = jellyfinIP.Split(':');
                if (parts.Length >= 2)
                {
                    JellyfinServerIp = parts[0];
                    SelectedJellyfinPort = parts[1];
                }
            }
            else
            {
                JellyfinServerIp = "";
                SelectedJellyfinPort = "8096"; // Default port
            }

            SelectedUpdateMode = Settings.Default.ConfigUpdateMode ?? "None";
            JellyfinApiKey = Settings.Default.JellyfinApiKey ?? "";

            SelectedTheme = Settings.Default.SelectedTheme ?? "dark";
            SelectedSubtitleMode = Settings.Default.SelectedSubtitleMode ?? "None";
            AudioLanguagePreference = Settings.Default.AudioLanguagePreference ?? "";
            SubtitleLanguagePreference = Settings.Default.SubtitleLanguagePreference ?? "";

            EnableBackdrops = Settings.Default.EnableBackdrops;
            EnableThemeSongs = Settings.Default.EnableThemeSongs;
            EnableThemeVideos = Settings.Default.EnableThemeVideos;
            BackdropScreensaver = Settings.Default.BackdropScreensaver;
            DetailsBanner = Settings.Default.DetailsBanner;
            CinemaMode = Settings.Default.CinemaMode;
            NextUpEnabled = Settings.Default.NextUpEnabled;
            EnableExternalVideoPlayers = Settings.Default.EnableExternalVideoPlayers;
            SkipIntros = Settings.Default.SkipIntros;
            AutoPlayNextEpisode = Settings.Default.AutoPlayNextEpisode;
            RememberAudioSelections = Settings.Default.RememberAudioSelections;
            RememberSubtitleSelections = Settings.Default.RememberSubtitleSelections;
            PlayDefaultAudioTrack = Settings.Default.PlayDefaultAudioTrack;
            UserAutoLogin = Settings.Default.UserAutoLogin;

            UpdateApiKeyStatus();
            _ = LoadJellyfinUsersAsync();
        }

        private void UpdateJellyfinAddress()
        {
            if (!string.IsNullOrWhiteSpace(JellyfinServerIp) && !string.IsNullOrWhiteSpace(SelectedJellyfinPort))
            {
                Settings.Default.JellyfinIP = $"{JellyfinServerIp}:{SelectedJellyfinPort}";
                Debug.WriteLine($"Updated Jellyfin IP: {Settings.Default.JellyfinIP}");
                Settings.Default.Save();

                UpdateApiKeyStatus();
                _ = LoadJellyfinUsersAsync();
            }
        }

        private void UpdateApiKeyStatus()
        {
            ApiKeySet = !string.IsNullOrEmpty(Settings.Default.JellyfinIP) &&
                        !string.IsNullOrEmpty(Settings.Default.JellyfinApiKey) &&
                        Settings.Default.JellyfinApiKey.Length == 32;
        }

        private bool IsValidJellyfinConfiguration()
        {
            return !string.IsNullOrEmpty(Settings.Default.JellyfinIP) &&
                   !string.IsNullOrEmpty(Settings.Default.JellyfinApiKey) &&
                   Settings.Default.JellyfinApiKey.Length == 32 &&
                   IsValidUrl($"http://{Settings.Default.JellyfinIP}/Users");
        }

        private static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private async Task LoadJellyfinUsersAsync()
        {
            // Clear existing users first
            AvailableJellyfinUsers.Clear();

            if (!IsValidJellyfinConfiguration())
            {
                Debug.WriteLine("Invalid Jellyfin configuration - skipping user load");
                return;
            }

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"MediaBrowser Token=\"{Settings.Default.JellyfinApiKey}\"");

                var url = $"http://{Settings.Default.JellyfinIP}/Users";
                Debug.WriteLine($"Attempting to load users from: {url}");

                using var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var users = JsonSerializer.Deserialize<List<JellyfinAuth>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (users != null && users.Any())
                    {
                        foreach (var user in users)
                        {
                            AvailableJellyfinUsers.Add(user);
                        }

                        if (AvailableJellyfinUsers.Count > 1)
                        {
                            AvailableJellyfinUsers.Add(new JellyfinAuth
                            {
                                Id = "everyone",
                                Name = "Everyone"
                            });
                        }

                        // Restore previously selected user
                        var savedUserId = Settings.Default.JellyfinUserId;
                        if (!string.IsNullOrEmpty(savedUserId))
                        {
                            SelectedJellyfinUser = AvailableJellyfinUsers.FirstOrDefault(u => u.Id == savedUserId);
                        }

                        // If no user selected and only one user available, select it
                        if (SelectedJellyfinUser == null && AvailableJellyfinUsers.Count == 1)
                        {
                            SelectedJellyfinUser = AvailableJellyfinUsers.First();
                        }

                        Debug.WriteLine($"Successfully loaded {AvailableJellyfinUsers.Count} users");
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
        }
    }
}