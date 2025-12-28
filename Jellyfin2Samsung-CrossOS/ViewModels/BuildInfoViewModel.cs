using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Models;

namespace Jellyfin2Samsung.ViewModels
{
    public partial class BuildInfoViewModel : ViewModelBase
    {
        public ObservableCollection<BuildVersion> JellyfinVersions { get; } = new();
        public ObservableCollection<BuildVersion> CommunityApps { get; } = new();

        public BuildInfoViewModel()
        {
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                using var client = new HttpClient();

                var jellyfinMd = await client.GetStringAsync(AppSettings.Default.ReleaseInfo);
                var communityMd = await client.GetStringAsync(AppSettings.Default.CommunityInfo);

                // Parse Jellyfin table
                ParseVersionsTable(jellyfinMd, JellyfinVersions);

                JellyfinVersions.Add(new BuildVersion
                {
                    FileName = "Moonfin",
                    Description = "Moonfin is optimized for the viewing experience on Samsung Smart TVs."
                });

                // Static Jellyfin entries
                JellyfinVersions.Add(new BuildVersion
                {
                    FileName = "Legacy",
                    Description = "Containing 10.8.z build for older model TVs"
                });

                JellyfinVersions.Add(new BuildVersion
                {
                    FileName = "AVPlay",
                    Description = "Includes AVPlay video player patches for better Samsung TV compatibility"
                });

                // Parse community apps
                ParseApplicationsTable(communityMd, CommunityApps);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to load build info: {ex}");
            }
        }

        // Remove markdown formatting like **bold**, emoji, etc.
        private static string CleanText(string input)
        {
            var text = Regex.Replace(input, @"\*\*(.*?)\*\*", "$1");
            text = Regex.Replace(text, @"[\u2600-\u27BF]", ""); // emoji range
            return text.Trim();
        }

        private void ParseVersionsTable(string md, ObservableCollection<BuildVersion> target)
        {
            var match = Regex.Match(md,
                @"## Versions\s*\n(?<table>(\|[^\n]+\n)+)",
                RegexOptions.Multiline);

            if (!match.Success) return;

            var table = match.Groups["table"].Value;

            var rows = Regex.Matches(table,
                @"^\|([^|]+)\|([^|]+)\|",
                RegexOptions.Multiline);

            bool headerSkipped = false;

            foreach (Match row in rows)
            {
                var col1 = row.Groups[1].Value.Trim();
                var col2 = row.Groups[2].Value.Trim();

                if (!headerSkipped &&
                    col1.Equals("File name", StringComparison.OrdinalIgnoreCase))
                {
                    headerSkipped = true;
                    continue;
                }

                if (col1.StartsWith("-"))
                    continue;

                target.Add(new BuildVersion
                {
                    FileName = CleanText(col1),
                    Description = CleanText(col2)
                });
            }
        }

        private void ParseApplicationsTable(string md, ObservableCollection<BuildVersion> target)
        {
            var match = Regex.Match(md,
                @"\|\s*🧩 Application\s*\|\s*📝 Description\s*\|\s*🔗 Repository\s*\|\s*\n(?<table>(\|[^\n]+\n)+)",
                RegexOptions.Multiline);

            if (!match.Success) return;

            var table = match.Groups["table"].Value;

            var rows = Regex.Matches(table,
                @"^\|([^|]+)\|([^|]+)\|([^|]+)\|",
                RegexOptions.Multiline);

            bool headerSkipped = false;

            foreach (Match row in rows)
            {
                var col1 = row.Groups[1].Value.Trim();
                var col2 = row.Groups[2].Value.Trim();

                if (!headerSkipped &&
                    col1.Contains("Application", StringComparison.OrdinalIgnoreCase))
                {
                    headerSkipped = true;
                    continue;
                }

                if (col1.StartsWith("-"))
                    continue;

                target.Add(new BuildVersion
                {
                    FileName = CleanText(col1),
                    Description = CleanText(col2)
                });
            }
        }

        [RelayCommand]
        private void Close()
        {
            OnRequestClose?.Invoke();
        }

        public event Action? OnRequestClose;
    }
}