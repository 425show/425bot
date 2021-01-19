using System;
using System.Text.Json.Serialization;

namespace _425bot
{
    public class TwitchAccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonIgnore]
        public DateTime ExpirationTime { get; set; }
        [JsonPropertyName("scope")]
        public string[] Scope { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }
}