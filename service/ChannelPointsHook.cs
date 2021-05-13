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
using Microsoft.Azure.WebJobs.Extensions.SignalRService;

namespace _425bot
{
    public class ChannelPointsFunctions
    {
        private readonly ITwitchAuthenticator _twitchAuthenticator;
        private readonly ILogger<ChannelPointsFunctions> _log;
        private readonly TwitchAuthenticatorConfig _config;

        private readonly JsonSerializerOptions _ignoreNullJsonOptions = new JsonSerializerOptions()
        {
            IgnoreNullValues = true,
        };

        public ChannelPointsFunctions(ITwitchAuthenticator twitchAuthenticator, ILoggerFactory loggerFactory, IOptions<TwitchAuthenticatorConfig> config)
        {
            _twitchAuthenticator = twitchAuthenticator;
            _config = config.Value;
            _log = loggerFactory.CreateLogger<ChannelPointsFunctions>();
        }

        [FunctionName("negotiate")]
        public SignalRConnectionInfo GetSignalRInfo([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twitch/negotiate")] HttpRequest req,
            [SignalRConnectionInfo(HubName = "channelPoints")] SignalRConnectionInfo connectionInfo, ILogger log)
        {
            log.LogInformation($"Client negotiate: {connectionInfo.Url}, {connectionInfo.AccessToken}");
            return connectionInfo;
        }

        [FunctionName("Authorize")]
        public IActionResult Authorize([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/authorize")] HttpRequest req)
        {
            return new RedirectResult(_twitchAuthenticator.GenerateAuthorizationUrl());
        }

        [FunctionName("AuthorizationResponse")]
        public IActionResult AuthorizationResponse([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/authresp")] HttpRequest req)
        {
            _log.LogDebug($"Received {req.QueryString}"); // PII/tokens
            return new OkObjectResult(new { Message = "Got it! You can close this window now." });
        }

        [FunctionName("Subscribe")]
        public async Task<IActionResult> Subscribe(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "twitch/CreateSubscription")] HttpRequest req)
        {
            var httpClient = new HttpClient();

            var token = await _twitchAuthenticator.GetAccessTokenForAppAsync(new[] { "channel:read:redemptions", "channel:manage:redemptions" });

            var request = new Subscription()
            {
                Type = "channel.channel_points_custom_reward_redemption.add",
                Version = "1",
                Condition = new Condition()
                {
                    BroadcasterUserId = _config.BroadcasterId
                },
                Transport = new Transport()
                {
                    Method = "webhook",
                    Callback = _config.ChannelPointsHandler,
                    Secret = _config.VerifierSecret
                }
            };

            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            httpClient.DefaultRequestHeaders.Add("Client-ID", _config.ClientId);
            var subscriptionRequest = await httpClient.PostAsync("https://api.twitch.tv/helix/eventsub/subscriptions",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json"));

            return new OkObjectResult(await subscriptionRequest.Content.ReadAsStringAsync());
        }

        [FunctionName("OnChannelPointsRedeemed")]
        public async Task<IActionResult> OnChannelPointsRedeemed([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/points/OnChannelPointsRedeemed")] HttpRequest req,
            [SignalR(HubName = "channelPoints")] IAsyncCollector<SignalRMessage> obsMessages
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

            var redemption = JsonSerializer.Deserialize<EventSubscription<ChannelPointsRedeemedEvent>>(message.Value.Message, _ignoreNullJsonOptions);
            switch (redemption.Subscription.Type)
            {
                case "channel.channel_points_custom_reward_redemption.add":
                    // parse to type
                    _log.LogInformation($"{redemption.Event.UserName} redeemed {redemption.Event.Reward.Cost} points for {redemption.Event.Reward.Title}");
                    await obsMessages.AddAsync(new SignalRMessage() { Target = "Redeemed", Arguments = new[] { redemption.Event.Reward.Title } });
                    break;
                default:
                    _log.LogInformation(redemption.Subscription.Type);
                    break;
            }

            return new OkResult();
        }
    }

    public class ObsActivityMessage
    {
        public string Message { get; set; }
    }
}