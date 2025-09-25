using CommunityToolkit.Mvvm.ComponentModel;
using Jellyfin2SamsungCrossOS.Helpers;
using Jellyfin2SamsungCrossOS.Models;
using Jellyfin2SamsungCrossOS.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.ViewModels
{
    public partial class JellyfinConfigViewModel : ViewModelBase
    {
        private readonly JellyfinHelper _jellyfinHelper;
        private readonly ILocalizationService _localizationService;

        [ObservableProperty]
        private string? audioLanguagePreference;

        [ObservableProperty]
        private string? subtitleLanguagePreference;

        [ObservableProperty]
        private string? jellyfinServerIp;

        [ObservableProperty]
        private string? selectedTheme;

        [ObservableProperty]
        private string? selectedSubtitleMode;

        [ObservableProperty]
        private string jellyfinApiKey = string.Empty;

        [ObservableProperty]
        private string selectedUpdateMode = string.Empty;

        [ObservableProperty]
        private string selectedJellyfinPort = string.Empty;

        [ObservableProperty]
        private bool enableBackdrops;

        [ObservableProperty]
        private bool enableThemeSongs;

        [ObservableProperty]
        private bool enableThemeVideos;

        [ObservableProperty]
        private bool backdropScreensaver;

        [ObservableProperty]
        private bool detailsBanner;

        [ObservableProperty]
        private bool cinemaMode;

        [ObservableProperty]
        private bool nextUpEnabled;

        [ObservableProperty]
        private bool enableExternalVideoPlayers;

        [ObservableProperty]
        private bool skipIntros;

        [ObservableProperty]
        private bool autoPlayNextEpisode;

        [ObservableProperty]
        private bool rememberAudioSelections;

        [ObservableProperty]
        private bool rememberSubtitleSelections;

        [ObservableProperty]
        private bool playDefaultAudioTrack;

        [ObservableProperty]
        private bool userAutoLogin;

        [ObservableProperty]
        private bool apiKeyEnabled = false;

        [ObservableProperty]
        private bool apiKeySet = false;

        [ObservableProperty]
        private ObservableCollection<JellyfinAuth> availableJellyfinUsers = new();

        [ObservableProperty]
        private JellyfinAuth? selectedJellyfinUser;

        public ObservableCollection<string> AvailableThemes { get; } = new()
        {
            "appletv",
            "blueradiance",
            "dark",
            "light",
            "purplehaze",
            "wmc"
        };

        public ObservableCollection<string> AvailableSubtitleModes { get; } = new()
        {
            "None",
            "OnlyForced",
            "Default",
            "Always"
        };

        public ObservableCollection<string> JellyfinPorts { get; } = new()
        {
            "8096", "8920"
        };

        public ObservableCollection<string> AvailableUpdateModes { get; } = new()
        {
            "None",
            "Server Settings",
            "Browser Settings",
            "User Settings",
            "Server & Browser Settings",
            "Server & User Settings",
            "Browser & User Settings",
            "All Settings"
        };

        private string L(string key) => _localizationService.GetString(key);

        public string LblJellyfinConfig => _localizationService.GetString("lblJellyfinConfig");
        public string LblServerSettings => _localizationService.GetString("lblServerSettings");
        public string UpdateMode => _localizationService.GetString("UpdateMode");
        public string ServerIP => _localizationService.GetString("ServerIP");
        public string LblJellyfinServerApi => _localizationService.GetString("lblJellyfinServerApi");
        public string LblJellyfinUser => _localizationService.GetString("lblJellyfinUser");
        public string LblEnableBackdrops => _localizationService.GetString("lblEnableBackdrops");
        public string LblEnableThemeSongs => _localizationService.GetString("lblEnableThemeSongs");
        public string LblEnableThemeVideos => _localizationService.GetString("lblEnableThemeVideos");
        public string LblBackdropScreensaver => _localizationService.GetString("lblBackdropScreensaver");
        public string LblDetailsBanner => _localizationService.GetString("lblDetailsBanner");
        public string LblCinemaMode => _localizationService.GetString("lblCinemaMode");
        public string LblNextUpEnabled => _localizationService.GetString("lblNextUpEnabled");
        public string LblEnableExternalVideoPlayers => _localizationService.GetString("lblEnableExternalVideoPlayers");
        public string LblSkipIntros => _localizationService.GetString("lblSkipIntros");
        public string LblAudioLanguagePreference => _localizationService.GetString("lblAudioLanguagePreference");
        public string LblSubtitleLanguagePreference => _localizationService.GetString("lblSubtitleLanguagePreference");
        public string Theme => _localizationService.GetString("Theme");
        public string LblSubtitleMode => _localizationService.GetString("lblSubtitleMode");
        public string LblAutoPlayNextEpisode => _localizationService.GetString("lblAutoPlayNextEpisode");
        public string LblRememberAudioSelections => _localizationService.GetString("lblRememberAudioSelections");
        public string LblRememberSubtitleSelections => _localizationService.GetString("lblRememberSubtitleSelections");
        public string LblPlayDefaultAudioTrack => _localizationService.GetString("lblPlayDefaultAudioTrack");
        public string LbluserAutoLogin => _localizationService.GetString("lbluserAutoLogin");
        public string LblUserSettings => _localizationService.GetString("lblUserSettings");
        public string LblBrowserSettings => _localizationService.GetString("lblBrowserSettings");


        public JellyfinConfigViewModel(
            JellyfinHelper jellyfinHelper,
            ILocalizationService localizationService)
        {
            _jellyfinHelper = jellyfinHelper;
            _localizationService = localizationService;
            _localizationService.LanguageChanged += OnLanguageChanged;
            InitializeAsyncSettings();
            UpdateApiKeyStatus();
            _ = LoadJellyfinUsersAsync();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            // Refresh all localized properties
            RefreshLocalizedProperties();
        }

        private void RefreshLocalizedProperties()
        {
            //OnPropertyChanged(nameof(LblRelease));
            //OnPropertyChanged(nameof(LblVersion));
            //OnPropertyChanged(nameof(LblSelectTv));
            //OnPropertyChanged(nameof(DownloadAndInstall));
            //OnPropertyChanged(nameof(FooterText));
            //OnPropertyChanged(nameof(StatusBar));
        }

        partial void OnAudioLanguagePreferenceChanged(string? value)
        {
            AppSettings.Default.AudioLanguagePreference = value;
            AppSettings.Default.Save();
        }

        partial void OnSubtitleLanguagePreferenceChanged(string? value)
        {
            AppSettings.Default.SubtitleLanguagePreference = value;
            AppSettings.Default.Save();
        }

        partial void OnJellyfinServerIpChanged(string? value)
        {
            UpdateJellyfinAddress();
        }

        partial void OnSelectedThemeChanged(string? value)
        {
            AppSettings.Default.SelectedTheme = value;
            AppSettings.Default.Save();
        }

        partial void OnSelectedSubtitleModeChanged(string? value)
        {
            AppSettings.Default.SelectedSubtitleMode = value;
            AppSettings.Default.Save();
        }

        partial void OnJellyfinApiKeyChanged(string value)
        {
            AppSettings.Default.JellyfinApiKey = value;
            AppSettings.Default.Save();

            UpdateApiKeyStatus();
            _ = LoadJellyfinUsersAsync();
        }

        partial void OnSelectedUpdateModeChanged(string value)
        {
            ApiKeyEnabled = !string.IsNullOrEmpty(value) &&
                           !value.Contains("Server") &&
                           !value.Contains("None");

            AppSettings.Default.ConfigUpdateMode = value;
            AppSettings.Default.Save();
        }

        partial void OnSelectedJellyfinPortChanged(string value)
        {
            UpdateJellyfinAddress();
        }

        partial void OnEnableBackdropsChanged(bool value)
        {
            AppSettings.Default.EnableBackdrops = value;
            AppSettings.Default.Save();
        }

        partial void OnEnableThemeSongsChanged(bool value)
        {
            AppSettings.Default.EnableThemeSongs = value;
            AppSettings.Default.Save();
        }

        partial void OnEnableThemeVideosChanged(bool value)
        {
            AppSettings.Default.EnableThemeVideos = value;
            AppSettings.Default.Save();
        }

        partial void OnBackdropScreensaverChanged(bool value)
        {
            AppSettings.Default.BackdropScreensaver = value;
            AppSettings.Default.Save();
        }

        partial void OnDetailsBannerChanged(bool value)
        {
            AppSettings.Default.DetailsBanner = value;
            AppSettings.Default.Save();
        }

        partial void OnCinemaModeChanged(bool value)
        {
            AppSettings.Default.CinemaMode = value;
            AppSettings.Default.Save();
        }

        partial void OnNextUpEnabledChanged(bool value)
        {
            AppSettings.Default.NextUpEnabled = value;
            AppSettings.Default.Save();
        }

        partial void OnEnableExternalVideoPlayersChanged(bool value)
        {
            AppSettings.Default.EnableExternalVideoPlayers = value;
            AppSettings.Default.Save();
        }

        partial void OnSkipIntrosChanged(bool value)
        {
            AppSettings.Default.SkipIntros = value;
            AppSettings.Default.Save();
        }

        partial void OnAutoPlayNextEpisodeChanged(bool value)
        {
            AppSettings.Default.AutoPlayNextEpisode = value;
            AppSettings.Default.Save();
        }

        partial void OnRememberAudioSelectionsChanged(bool value)
        {
            AppSettings.Default.RememberAudioSelections = value;
            AppSettings.Default.Save();
        }

        partial void OnRememberSubtitleSelectionsChanged(bool value)
        {
            AppSettings.Default.RememberSubtitleSelections = value;
            AppSettings.Default.Save();
        }

        partial void OnPlayDefaultAudioTrackChanged(bool value)
        {
            AppSettings.Default.PlayDefaultAudioTrack = value;
            AppSettings.Default.Save();
        }

        partial void OnUserAutoLoginChanged(bool value)
        {
            AppSettings.Default.UserAutoLogin = value;
            AppSettings.Default.Save();
        }

        partial void OnSelectedJellyfinUserChanged(JellyfinAuth? value)
        {
            if (value != null)
            {
                AppSettings.Default.JellyfinUserId = value.Id;
                AppSettings.Default.Save();
            }
        }

        private async void InitializeAsyncSettings()
        {
            // Initialize with default values if settings are empty
            var jellyfinIP = AppSettings.Default.JellyfinIP;
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

            SelectedUpdateMode = AppSettings.Default.ConfigUpdateMode ?? "None";
            JellyfinApiKey = AppSettings.Default.JellyfinApiKey ?? "";

            SelectedTheme = AppSettings.Default.SelectedTheme ?? "dark";
            SelectedSubtitleMode = AppSettings.Default.SelectedSubtitleMode ?? "None";
            AudioLanguagePreference = AppSettings.Default.AudioLanguagePreference ?? "";
            SubtitleLanguagePreference = AppSettings.Default.SubtitleLanguagePreference ?? "";

            EnableBackdrops = AppSettings.Default.EnableBackdrops;
            EnableThemeSongs = AppSettings.Default.EnableThemeSongs;
            EnableThemeVideos = AppSettings.Default.EnableThemeVideos;
            BackdropScreensaver = AppSettings.Default.BackdropScreensaver;
            DetailsBanner = AppSettings.Default.DetailsBanner;
            CinemaMode = AppSettings.Default.CinemaMode;
            NextUpEnabled = AppSettings.Default.NextUpEnabled;
            EnableExternalVideoPlayers = AppSettings.Default.EnableExternalVideoPlayers;
            SkipIntros = AppSettings.Default.SkipIntros;
            AutoPlayNextEpisode = AppSettings.Default.AutoPlayNextEpisode;
            RememberAudioSelections = AppSettings.Default.RememberAudioSelections;
            RememberSubtitleSelections = AppSettings.Default.RememberSubtitleSelections;
            PlayDefaultAudioTrack = AppSettings.Default.PlayDefaultAudioTrack;
            UserAutoLogin = AppSettings.Default.UserAutoLogin;
        }

        private void UpdateJellyfinAddress()
        {
            if (!string.IsNullOrWhiteSpace(JellyfinServerIp) && !string.IsNullOrWhiteSpace(SelectedJellyfinPort))
            {
                AppSettings.Default.JellyfinIP = $"{JellyfinServerIp}:{SelectedJellyfinPort}";
                Debug.WriteLine($"Updated Jellyfin IP: {AppSettings.Default.JellyfinIP}");
                AppSettings.Default.Save();

                UpdateApiKeyStatus();
                _ = LoadJellyfinUsersAsync();
            }
        }

        private void UpdateApiKeyStatus()
        {
            ApiKeySet = !string.IsNullOrEmpty(AppSettings.Default.JellyfinIP) &&
                        !string.IsNullOrEmpty(AppSettings.Default.JellyfinApiKey) &&
                        AppSettings.Default.JellyfinApiKey.Length == 32;
        }

        private async Task LoadJellyfinUsersAsync()
        {
            // Clear existing users first
            AvailableJellyfinUsers.Clear();

            var users = await _jellyfinHelper.LoadJellyfinUsersAsync();

            foreach (var user in users)
                AvailableJellyfinUsers.Add(user);

            var savedUserId = AppSettings.Default.JellyfinUserId;
            if (!string.IsNullOrEmpty(savedUserId))
                SelectedJellyfinUser = AvailableJellyfinUsers.FirstOrDefault(u => u.Id == savedUserId);

            if (SelectedJellyfinUser == null && AvailableJellyfinUsers.Count == 1)
                SelectedJellyfinUser = AvailableJellyfinUsers.First();
        }
        public void Dispose()
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;

        }
    }
}