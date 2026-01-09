using Jellyfin2Samsung.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Core
{
    public class AddLatestRelease
    {
        private readonly HttpClient _httpClient;
        private readonly IList<GitHubRelease> _releases;

        public AddLatestRelease(HttpClient httpClient, IList<GitHubRelease> releases)
        {
            _httpClient = httpClient;
            _releases = releases;
        }

        public async Task AddLatestReleaseAsync(
            string url,
            string displayName,
            JsonSerializerSettings settings)
        {
            var response = await _httpClient.GetStringAsync(url);

            var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(response, settings)
                           ?? new List<GitHubRelease>();

            var latest = releases.MaxBy(r => r.PublishedAt);

            if (latest == null)
                return;

            _releases.Add(new GitHubRelease
            {
                Name = displayName,
                Assets = latest.Assets,
                PublishedAt = latest.PublishedAt,
                TagName = latest.TagName,
                Url = latest.Url
            });
        }
    }
}