using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Core
{
    public class AddLatestRelease
    {
        private readonly HttpClient _httpClient;

        public AddLatestRelease(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<GitHubRelease?> GetLatestReleaseAsync(string url, string displayName)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();

                // Try to parse as array first (normal GitHub releases endpoint)
                try
                {
                    var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
                        stream,
                        JsonSerializerOptionsProvider.Default);

                    var latest = releases?.Count > 0 ? releases[0] : null;

                    if (latest != null)
                        latest.Name = displayName;

                    return latest;
                }
                catch (JsonException)
                {
                    // If array parsing fails, try parsing as single object
                    stream.Position = 0;

                    var latest = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                        stream,
                        JsonSerializerOptionsProvider.Default);

                    if (latest != null)
                        latest.Name = displayName;

                    return latest;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to fetch release from {url}: {ex}");
                return null;
            }
        }
    }
}
