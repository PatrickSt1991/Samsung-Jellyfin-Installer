using Newtonsoft.Json;

namespace Samsung_Jellyfin_Installer.Models
{
    public class TizenResults
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("inputEmailID")]
        public string Email { get; set; }
    }
}