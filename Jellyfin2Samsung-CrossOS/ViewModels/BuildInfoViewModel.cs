using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Jellyfin2Samsung.ViewModels
{
    public partial class BuildVersion : ObservableObject
    {
        [ObservableProperty] private string fileName = string.Empty;
        [ObservableProperty] private string description = string.Empty;
    }

    public partial class BuildInfoViewModel : ViewModelBase
    {
        private const string ReadmeUrl = "https://raw.githubusercontent.com/jeppevinkel/jellyfin-tizen-builds/refs/heads/master/README.md";

        public ObservableCollection<BuildVersion> Versions { get; } = new();

        public BuildInfoViewModel()
        {
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                using var client = new HttpClient();
                var markdown = await client.GetStringAsync(ReadmeUrl);

                // Extract the "Versions" section
                var sectionMatch = Regex.Match(markdown,
                    @"## Versions\s*\n(?<table>(\|[^\n]+\n)+)",
                    RegexOptions.Multiline);

                if (!sectionMatch.Success)
                    return;

                var table = sectionMatch.Groups["table"].Value;

                // Extract rows (skip header and separator)
                var rows = Regex.Matches(table, @"^\|([^|]+)\|([^|]+)\|", RegexOptions.Multiline);

                bool isHeaderSkipped = false;
                foreach (Match row in rows)
                {
                    var file = row.Groups[1].Value.Trim();
                    var desc = row.Groups[2].Value.Trim();

                    // Skip the header
                    if (!isHeaderSkipped && file.Equals("File name", StringComparison.OrdinalIgnoreCase))
                    {
                        isHeaderSkipped = true;
                        continue;
                    }

                    // Skip separator
                    if (file.StartsWith("-"))
                        continue;

                    Versions.Add(new BuildVersion
                    {
                        FileName = file,
                        Description = desc
                    });
                }
                Versions.Add(new BuildVersion
                {
                    FileName = "Legacy",
                    Description = "Containing 10.8.z build for older model TVs"
                });
                Versions.Add(new BuildVersion
                {
                    FileName = "AVPlay",
                    Description = "Includes AVPlay video player patches for better Samsung TV compatibility"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load build info: {ex.Message}");
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
