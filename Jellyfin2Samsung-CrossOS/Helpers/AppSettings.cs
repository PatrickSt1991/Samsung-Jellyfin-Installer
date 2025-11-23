using Jellyfin2Samsung.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin2Samsung.Helpers
{
    public class AppSettings
    {
        private const string FileName = "settings.json";
        public static readonly string FolderPath = Environment.CurrentDirectory;
        public static readonly string FilePath = Path.Combine(FolderPath, FileName);
        public static readonly string TizenSdbPath = Path.Combine(FolderPath, "Assets", "TizenSDB");
        public static readonly string CertificatePath = Path.Combine(FolderPath, "Assets", "Certificate");
        public static readonly string ProfilePath = Path.Combine(FolderPath, "Assets", "TizenProfile");
        public static readonly string DownloadPath = Path.Combine(FolderPath, "Downloads");

        private static AppSettings? _instance;

        // --- Runtime-only cached object (not saved to disk) ---
        [JsonIgnore]
        public ExistingCertificates? ChosenCertificates { get; set; }
        [JsonIgnore]
        public string CustomWgtPath { get; set; } = "";
        [JsonIgnore]
        public string LocalIp { get; set; } = "";
        public static AppSettings Default => _instance ??= Load();

        // ----- User-scoped settings -----
        public string Language { get; set; } = "en";
        public string Certificate { get; set; } = "Jelly2Sams";
        public bool RememberCustomIP { get; set; } = false;
        public bool DeletePreviousInstall { get; set; } = false;
        public string UserCustomIP { get; set; } = "";
        public bool ForceSamsungLogin { get; set; } = false;
        public bool RTLReading { get; set; } = false;
        public bool ShowSdbWindow { get; set; } = false;
        public string JellyfinIP { get; set; } = "";
        public string AudioLanguagePreference { get; set; } = "";
        public string SubtitleLanguagePreference { get; set; } = "";
        public bool EnableBackdrops { get; set; } = false;
        public bool EnableThemeSongs { get; set; } = false;
        public bool EnableThemeVideos { get; set; } = false;
        public bool BackdropScreensaver { get; set; } = false;
        public bool DetailsBanner { get; set; } = false;
        public bool CinemaMode { get; set; } = false;
        public bool NextUpEnabled { get; set; } = false;
        public bool EnableExternalVideoPlayers { get; set; } = false;
        public bool SkipIntros { get; set; } = false;
        public bool AutoPlayNextEpisode { get; set; } = true;
        public bool RememberAudioSelections { get; set; } = true;
        public bool RememberSubtitleSelections { get; set; } = true;
        public bool PlayDefaultAudioTrack { get; set; } = true;
        public string SelectedTheme { get; set; } = "dark";
        public string SelectedSubtitleMode { get; set; } = "Default";
        public string ConfigUpdateMode { get; set; } = "None";
        public string JellyfinApiKey { get; set; } = "";
        public string JellyfinUserId { get; set; } = "";
        public bool UserAutoLogin { get; set; } = true;
        public string DistributorsEndpoint_V1 { get; set; } = "https://svdca.samsungqbe.com/apis/v1/distributors";
        public string DistributorsEndpoint_V3 { get; set; } = "https://svdca.samsungqbe.com/apis/v3/distributors";
        public string AuthorEndpoint_V3 { get; set; } = "https://svdca.samsungqbe.com/apis/v3/authors";
        public bool TryOverwrite { get; set; } = true;

        // ----- Application-scoped settings (readonly at runtime) -----
        public string ReleasesUrl { get; set; } = "https://api.github.com/repos/jeppevinkel/jellyfin-tizen-builds/releases";
        public string AuthorEndpoint { get; set; } = "https://dev.tizen.samsung.com/apis/v2/authors";
        public string AppVersion { get; set; } = "v1.8.6.0-beta";
        public string TizenSdb { get; set; } = "https://api.github.com/repos/PatrickSt1991/tizen-sdb/releases";
        public string JellyfinAvRelease { get; set; } = "https://api.github.com/repos/PatrickSt1991/tizen-jellyfin-avplay/releases";
        public string JellyfinLegacy { get; set; } = "https://api.github.com/repos/jeppevinkel/jellyfin-tizen-builds/releases/tags/2024-10-27-1821";
        public string ReleaseInfo { get; set; } = "https://raw.githubusercontent.com/jeppevinkel/jellyfin-tizen-builds/refs/heads/master/README.md";

        public AppSettings() { }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Ignore errors for now
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                        _instance = settings;
                }
            }
            catch
            {
                // ignore load errors
            }

            return _instance ??= new AppSettings();
        }
    }
}
