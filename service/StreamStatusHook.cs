using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Linq;

namespace _425bot
{
    public class StreamStatusFunctions
    {
        private readonly ITwitchAuthenticator _twitchAuthenticator;
        private readonly ILogger<StreamStatusFunctions> _log;
        private readonly TwitchAuthenticatorConfig _config;
        private readonly HttpClient _httpClient;

        private readonly JsonSerializerOptions _ignoreNullJsonOptions = new JsonSerializerOptions()
        {
            IgnoreNullValues = true,
        };

        public StreamStatusFunctions(ITwitchAuthenticator twitchAuthenticator, ILoggerFactory loggerFactory, IOptions<TwitchAuthenticatorConfig> config, IHttpClientFactory clientFactory)
        {
            _twitchAuthenticator = twitchAuthenticator;
            _config = config.Value;
            _log = loggerFactory.CreateLogger<StreamStatusFunctions>();
            _httpClient = clientFactory.CreateClient();
        }

        [FunctionName("StatusCreateSubscription")]
        public async Task<IActionResult> Subscribe([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "twitch/status/CreateSubscription")] HttpRequest req)
        {
            var httpClient = new HttpClient();

            var token = await _twitchAuthenticator.GetAccessTokenForAppAsync(new string[] { });

            var request = new Subscription()
            {
                Type = "stream.online",
                Version = "1",
                Condition = new Condition()
                {
                    BroadcasterUserId = _config.BroadcasterId
                },
                Transport = new Transport()
                {
                    Method = "webhook",
                    Callback = _config.StreamStatusHandler,
                    Secret = _config.VerifierSecret
                }
            };

            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            httpClient.DefaultRequestHeaders.Add("Client-ID", _config.ClientId);
            var subscriptionRequest = await httpClient.PostAsync("https://api.twitch.tv/helix/eventsub/subscriptions",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json"));

            return new OkObjectResult(await subscriptionRequest.Content.ReadAsStringAsync());
        }

        [FunctionName("StatusOnStreamStarted")]
        public async Task<IActionResult> OnStreamStarted([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/status/OnStreamStarted")] HttpRequest req,
            [Table("ChannelStatus")] IAsyncCollector<ChannelStatus> table
        )
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            req.Body.Position = 0;

            var message = await _twitchAuthenticator.AuthenticateMessage(req);

            if (!message.Success) return new BadRequestObjectResult(new { Message = "Signature not valid" });

            if (req.Headers.ContainsKey("Twitch-Eventsub-Message-Type") && req.Headers["Twitch-Eventsub-Message-Type"] == "webhook_callback_verification")
            {
                // return challenge value from payload
                var jdoc = JsonDocument.Parse(requestBody);
                var challenge = jdoc.RootElement.GetProperty("challenge").GetString();
                _log.LogInformation($"Returning challenge: {challenge}");
                return new ContentResult()
                {
                    Content = challenge,
                    ContentType = "text/plain",
                    StatusCode = 200
                };
            }

            var eventMessage = JsonSerializer.Deserialize<EventSubscription<StreamStartEvent>>(message.Value.Message, _ignoreNullJsonOptions);

            _log.LogInformation($"Received event: {eventMessage.Subscription.Type}");
            switch (eventMessage.Subscription.Type)
            {
                case "stream.online":
                    await table.AddAsync(new ChannelStatus(eventMessage.Event));
                    break;
                case "stream.offline":
                    break;
            }

            return new OkResult();
        }

        [FunctionName("StatusGetCurrentStreams")]
        public async Task<IActionResult> GetActiveStreams([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twitch/status/cheap/{channel}")] HttpRequest req, string channel)
        {   // 544451661
            // https://api.twitch.tv/helix/streams
            var offlineResult = new OkObjectResult(new { Status = "Offline", Live = false });
            var token = await _twitchAuthenticator.GetAccessTokenForAppAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            _httpClient.DefaultRequestHeaders.Add("Client-ID", _config.ClientId);
            var request = await _httpClient.GetAsync($"https://api.twitch.tv/helix/streams?user_login={channel}");

            if (!request.IsSuccessStatusCode) return offlineResult;

            var doc = JsonDocument.Parse(await request.Content.ReadAsStreamAsync());
            var data = doc.RootElement.GetProperty("data");

            if (data.GetArrayLength() == 0) // not live, time to go!
            {
                return offlineResult;
            }

            var streamData = data.EnumerateArray().First();

            return new OkObjectResult(new
            {
                Channel = streamData.GetProperty("user_name"),
                Status = "Online",
                IsLive = true,
                Title = streamData.GetProperty("title").GetString(),
                Thumb = streamData.GetProperty("thumbnail_url").GetString(),
            });
        }
    }

    public class ChannelStatus : TableEntity
    {
        public DateTime StartedAt { get; set; }
        public string StreamId { get; set; }
        public string Type { get; set; }
        public string BroadcasterUserId { get; set; }
        public string StreamTitle { get; set; }

        public ChannelStatus() { }

        public ChannelStatus(StreamStartEvent startEvent)
        {
            this.PartitionKey = startEvent.BroadcasterUserId;
            this.RowKey = startEvent.Id;

            this.StreamId = startEvent.Id;
            this.BroadcasterUserId = startEvent.BroadcasterUserId;
            this.Type = startEvent.Type;
            if (DateTime.TryParse(startEvent.StartedAt, out var startedAt))
            {
                this.StartedAt = startedAt;
            }
        }
    }
}