using System.Text.Json.Serialization;

namespace _425bot
{
    public class AddSubscriptionRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("condition")]
        public Condition Condition { get; set; }

        [JsonPropertyName("transport")]
        public Transport Transport { get; set; }
    }

    public class Condition
    {
        [JsonPropertyName("broadcaster_user_id")]
        public string BroadcasterUserId { get; set; }
    }

    public class Transport
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("callback")]
        public string Callback { get; set; }

        [JsonPropertyName("secret")]
        public string Secret { get; set; }
    }
}