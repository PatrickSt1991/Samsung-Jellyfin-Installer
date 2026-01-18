using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Helpers.Core;
using Jellyfin2Samsung.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

                JellyfinVersions.Add(new BuildVersion
                {
                    FileName = "AVPlay 10.10.z - SmartHub",
                    Description = "Includes AVPlay video player patches for better Samsung TV compatibility for10.10.z SmartHub variant"
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
            var text = RegexPatterns.BuildInfo.MarkdownBold.Replace(input, "$1");
            text = RegexPatterns.BuildInfo.EmojiRange.Replace(text, "");
            return text.Trim();
        }

        private void ParseVersionsTable(string md, ObservableCollection<BuildVersion> target)
        {
            var match = RegexPatterns.BuildInfo.VersionsTable.Match(md);

            if (!match.Success) return;

            var table = match.Groups["table"].Value;

            var rows = RegexPatterns.BuildInfo.TableRow2Columns.Matches(table);

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
            var match = RegexPatterns.BuildInfo.ApplicationsTable.Match(md);

            if (!match.Success) return;

            var table = match.Groups["table"].Value;

            var rows = RegexPatterns.BuildInfo.TableRow3Columns.Matches(table);

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