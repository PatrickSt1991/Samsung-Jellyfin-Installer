using Jellyfin2Samsung.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        public async Task<GitHubRelease?> GetLatestReleaseAsync(
            string url,
            string displayName,
            JsonSerializerSettings settings)
        {
            using var stream = await _httpClient.GetStreamAsync(url);
            using var sr = new StreamReader(stream);
            using var reader = new JsonTextReader(sr);

            var serializer = JsonSerializer.Create(settings);

            GitHubRelease? latest = null;

            if (reader.Read())
            {
                // Case 1: API returns array (normal GitHub behavior)
                if (reader.TokenType == JsonToken.StartArray)
                {
                    reader.Read(); // move to first element
                    latest = serializer.Deserialize<GitHubRelease>(reader);
                }
                // Case 2: API returns single object
                else if (reader.TokenType == JsonToken.StartObject)
                {
                    latest = serializer.Deserialize<GitHubRelease>(reader);
                }
            }

            if (latest == null)
                return null;

            latest.Name = displayName;
            return latest;
        }

    }
}