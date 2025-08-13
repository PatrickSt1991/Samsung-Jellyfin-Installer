using System.Collections.ObjectModel;
using System.Security.Policy;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class JellyfinConfigViewModel : ViewModelBase
    {
        private string? _audioLanguagePreference;
        private string? _subtitleLanguagePreference;
        private string? _jellyfinServerIp;
        private string? _selectedTheme;
        private string? _selectedSubtitleMode;
        private string _jellyfinApiKey;
        private string _selectedUpdateMode;
        private int _selectedJellyfinPort;
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
                if(_jellyfinApiKey != value)
                {
                    _jellyfinApiKey = value;
                    OnPropertyChanged(nameof(JellyfinApiKey));

                    Settings.Default.JellyfinApiKey = value;
                    Settings.Default.Save();
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
                        !SelectedUpdateMode.Contains("Server") &&
                        !SelectedUpdateMode.Contains("None");

                    Settings.Default.ConfigUpdateMode = value;
                    Settings.Default.Save();
                }
            }
        }
        public int SelectedJellyfinPort
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
                if(_userAutoLogin != value)
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
                if(_apiKeyEnabled != value)
                {
                    _apiKeyEnabled = value;
                    OnPropertyChanged(nameof(ApiKeyEnabled));
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

        public JellyfinConfigViewModel()
        {
            var jellyfinIP = Settings.Default.JellyfinIP;
            if (!string.IsNullOrWhiteSpace(jellyfinIP) && jellyfinIP.Contains(':'))
            {
                var parts = jellyfinIP.Split(':');
                if (parts.Length >= 2)
                {
                    JellyfinServerIp = parts[0];
                    if (int.TryParse(parts[1], out int port))
                        SelectedJellyfinPort = port;
                }
            }
            else
            {
                JellyfinServerIp = "";
            }

            SelectedUpdateMode = Settings.Default.ConfigUpdateMode;
            JellyfinApiKey = Settings.Default.JellyfinApiKey;

            SelectedTheme = Settings.Default.SelectedTheme;
            SelectedSubtitleMode = Settings.Default.SelectedSubtitleMode;
            AudioLanguagePreference = Settings.Default.AudioLanguagePreference;
            SubtitleLanguagePreference = Settings.Default.SubtitleLanguagePreference;

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
        }
        private void UpdateJellyfinAddress()
        {
            if (!string.IsNullOrWhiteSpace(JellyfinServerIp) && SelectedJellyfinPort > 0)
            {
                Settings.Default.JellyfinIP = $"{JellyfinServerIp}:{SelectedJellyfinPort}";
                Settings.Default.Save();
            }
        }
    }
}
