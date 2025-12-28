using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using Jellyfin2Samsung.Services;
using Jellyfin2Samsung.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.ViewModels
{
    public partial class JellyfinConfigViewModel : ViewModelBase
    {
        private readonly JellyfinApiClient _jellyfinApiClient;
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
        private string selectedJellyfinProtocol = string.Empty;

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
        private bool useServerScripts;

        [ObservableProperty]
        private bool apiKeyEnabled = false;

        [ObservableProperty]
        private bool apiKeySet = false;

        [ObservableProperty]
        private bool serverIpSet = false;

        [ObservableProperty]
        private bool enableDevLogs = false;
        
        [ObservableProperty]
        private bool canOpenDebugWindow;
        
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
        public string LblUseServerScripts => _localizationService.GetString("lblUseServerScripts");
        public string LblEnableDevLogs => _localizationService.GetString("lblEnableDevLogs");
        public string lblOpenDebugWindow => _localizationService.GetString("lblOpenDebugWindow");
        public string TvIp => AppSettings.Default.TvIp;

        public JellyfinConfigViewModel(
            JellyfinApiClient jellyfinApiClient,
            ILocalizationService localizationService)
        {
            _jellyfinApiClient = jellyfinApiClient;
            _localizationService = localizationService;
            _localizationService.LanguageChanged += OnLanguageChanged;
            InitializeAsyncSettings();
            UpdateServerIpStatus();
            UpdateApiKeyStatus();
            _ = LoadJellyfinUsersAsync();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RefreshLocalizedProperties();
        }

        private void RefreshLocalizedProperties()
        {
            OnPropertyChanged(nameof(LblJellyfinConfig));
            OnPropertyChanged(nameof(LblServerSettings));
            OnPropertyChanged(nameof(UpdateMode));
            OnPropertyChanged(nameof(ServerIP));
            OnPropertyChanged(nameof(LblJellyfinServerApi));
            OnPropertyChanged(nameof(LblJellyfinUser));
            OnPropertyChanged(nameof(LblEnableBackdrops));
            OnPropertyChanged(nameof(LblEnableThemeSongs));
            OnPropertyChanged(nameof(LblEnableThemeVideos));
            OnPropertyChanged(nameof(LblBackdropScreensaver));
            OnPropertyChanged(nameof(LblDetailsBanner));
            OnPropertyChanged(nameof(LblCinemaMode));
            OnPropertyChanged(nameof(LblNextUpEnabled));
            OnPropertyChanged(nameof(LblEnableExternalVideoPlayers));
            OnPropertyChanged(nameof(LblSkipIntros));
            OnPropertyChanged(nameof(LblAudioLanguagePreference));
            OnPropertyChanged(nameof(LblSubtitleLanguagePreference));
            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(LblSubtitleMode));
            OnPropertyChanged(nameof(LblAutoPlayNextEpisode));
            OnPropertyChanged(nameof(LblRememberAudioSelections));
            OnPropertyChanged(nameof(LblRememberSubtitleSelections));
            OnPropertyChanged(nameof(LblPlayDefaultAudioTrack));
            OnPropertyChanged(nameof(LbluserAutoLogin));
            OnPropertyChanged(nameof(LblUserSettings));
            OnPropertyChanged(nameof(LblBrowserSettings));
            OnPropertyChanged(nameof(LblUseServerScripts));
            OnPropertyChanged(nameof(LblEnableDevLogs));
            OnPropertyChanged(nameof(lblOpenDebugWindow));
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
                           !value.Contains("None");

            AppSettings.Default.ConfigUpdateMode = value;
            AppSettings.Default.Save();
        }

        partial void OnSelectedJellyfinPortChanged(string value)
        {
            UpdateJellyfinAddress();
        }

        partial void OnSelectedJellyfinProtocolChanged(string value)
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

        partial void OnUseServerScriptsChanged(bool value)
        {
            AppSettings.Default.UseServerScripts = value;
            AppSettings.Default.Save();
        }

        partial void OnEnableDevLogsChanged(bool value)
        {
            AppSettings.Default.EnableDevLogs = value;
            AppSettings.Default.Save();

            CanOpenDebugWindow = (!string.IsNullOrWhiteSpace(TvIp)) && value;
            OpenDebugWindowCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedJellyfinUserChanged(JellyfinAuth? value)
        {
            if (value != null)
            {
                AppSettings.Default.JellyfinUserId = value.Id;
                AppSettings.Default.Save();
            }
        }
        [RelayCommand(CanExecute = nameof(CanOpenDebugWindow))]
        private void OpenDebugWindow()
        {
            if (string.IsNullOrWhiteSpace(TvIp))
                return;

            // create VM with IP from settings
            var logService = App.Services.GetRequiredService<TvLogService>();
            var vm = new TvLogsViewModel(logService, TvIp, _localizationService);

            var window = new TvLogsWindow
            {
                DataContext = vm
            };

            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                window.Show(desktop.MainWindow);
            }
            else
            {
                window.Show();
            }
        }
        private async void InitializeAsyncSettings()
        {
            var jellyfinIP = AppSettings.Default.JellyfinIP;

            if (!string.IsNullOrWhiteSpace(jellyfinIP) && Uri.TryCreate(jellyfinIP, UriKind.Absolute, out var uri))
            {
                SelectedJellyfinProtocol = uri.Scheme;
                JellyfinServerIp = uri.Host;
                SelectedJellyfinPort = uri.Port.ToString();
            }
            else
            {
                SelectedJellyfinProtocol = "http";
                JellyfinServerIp = "";
                SelectedJellyfinPort = "8096";
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
            UseServerScripts = AppSettings.Default.UseServerScripts;
            EnableDevLogs = AppSettings.Default.EnableDevLogs;
        }

        private void UpdateJellyfinAddress()
        {
            if (!string.IsNullOrWhiteSpace(JellyfinServerIp) &&
                !string.IsNullOrWhiteSpace(SelectedJellyfinPort) &&
                !string.IsNullOrWhiteSpace(SelectedJellyfinProtocol))
            {
                AppSettings.Default.JellyfinIP = $"{SelectedJellyfinProtocol}://{JellyfinServerIp}:{SelectedJellyfinPort}";

                Trace.WriteLine($"Updated Jellyfin IP: {AppSettings.Default.JellyfinIP}");
                AppSettings.Default.Save();
                UpdateServerIpStatus();
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

        private void UpdateServerIpStatus()
        {
            ServerIpSet = !string.IsNullOrEmpty(AppSettings.Default.JellyfinIP);
        }

        private async Task LoadJellyfinUsersAsync()
        {
            // Clear existing users first
            AvailableJellyfinUsers.Clear();

            var users = await _jellyfinApiClient.LoadUsersAsync();

            foreach (var user in users)
                AvailableJellyfinUsers.Add(user);

            var savedUserId = AppSettings.Default.JellyfinUserId;
            if (!string.IsNullOrEmpty(savedUserId))
                SelectedJellyfinUser = AvailableJellyfinUsers.FirstOrDefault(u => u.Id == savedUserId);

            if (SelectedJellyfinUser == null && AvailableJellyfinUsers.Count == 1)
                SelectedJellyfinUser = AvailableJellyfinUsers.First();
        }
        public void OnTvIpChanged()
        {
            OnPropertyChanged(nameof(TvIp));

            CanOpenDebugWindow = (!string.IsNullOrWhiteSpace(TvIp)) && EnableDevLogs;
            OpenDebugWindowCommand.NotifyCanExecuteChanged();
        }
        public void Dispose()
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;

        }
    }
}