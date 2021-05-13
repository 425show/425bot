using System.Text.Json.Serialization;

namespace _425bot
{
    public class Subscription
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("condition")]
        public Condition Condition { get; set; }

        [JsonPropertyName("transport")]
        public Transport Transport { get; set; }
        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }
    }

    public class Condition
    {
        [JsonPropertyName("broadcaster_user_id")]
        public string BroadcasterUserId { get; set; }
        [JsonPropertyName("reward_id")]
        public string RewardId { get; set; }
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

    public class EventSubscription<T> where T : Event
    {
        [JsonPropertyName("subscription")]
        public Subscription Subscription { get; set; }
        [JsonPropertyName("event")]
        public T Event { get; set; }
    }

    public class Event
    {
        [JsonPropertyName("broadcaster_user_id")]
        public string BroadcasterUserId { get; set; }

        [JsonPropertyName("broadcaster_user_login")]
        public string BroadcasterUserLogin { get; set; }

        [JsonPropertyName("broadcaster_user_name")]
        public string BroadcasterUserName { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    public class ChannelPointsRedeemedEvent : Event
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; }

        [JsonPropertyName("user_name")]
        public string UserName { get; set; }

        [JsonPropertyName("user_input")]
        public string UserInput { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("redeemed_at")]
        public string RedeemedAt { get; set; }

        [JsonPropertyName("reward")]
        public Reward Reward { get; set; }
    }

    public class Reward
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }

        [JsonPropertyName("cost")]
        public int Cost { get; set; }
    }

    public class StreamStartEvent : Event
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("started_at")]
        public string StartedAt { get; set; }
    }

}