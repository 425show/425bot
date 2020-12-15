using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace _425bot
{
    public class SubscriptionAddRequest
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

    public static class ChannelPointsHook
    {
        [FunctionName("Authorize")]
        public static IActionResult Authorize([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/authorize")] HttpRequest req, ILogger log, ExecutionContext ctx)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(ctx.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables().Build();

            var redirectUri = $"https://id.twitch.tv/oauth2/authorize?client_id={config["TwitchClientId"]}&redirect_uri={config["TwitchRedirectUri"]}&response_type=token&scope={config["TwitchScopes"]}";
            log.LogInformation($"Redirecting to authorization: {redirectUri}");

            return new RedirectResult(redirectUri);
        }

        [FunctionName("AuthorizationResponse")]
        public static IActionResult AuthorizationResponse([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/authresp")] HttpRequest req, ILogger log, ExecutionContext ctx)
        {
            log.LogInformation($"Received {req.QueryString}");
            return new OkObjectResult(new { Message = "Got it! You can close this window now." });
        }


        [FunctionName("Subscribe")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "twitch/CreateSubscription")] HttpRequest req,
            ILogger log, ExecutionContext ctx)
        {
            var httpClient = new HttpClient();
            var config = new ConfigurationBuilder()
                .SetBasePath(ctx.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables().Build();

            var twitchAuth = new TwitchAuthentication(httpClient, config["TwitchClientId"], config["TwitchClientSecret"]);
            var token = await twitchAuth.GetAccessTokenForAppAsync(new[] { "channel:read:redemptions", "channel:manage:redemptions" });

            var request = new SubscriptionAddRequest()
            {
                Type = "channel.channel_points_custom_reward_redemption.add",
                Version = "1",
                Condition = new Condition()
                {
                    BroadcasterUserId = config["TwitchBroadcasterId"]
                },
                Transport = new Transport()
                {
                    Method = "webhook",
                    //"https://425bot.ngrok.io/twitch/points/OnChannelPointsRedeemed",
                    Callback = config["TwitchChannelPointsHandler"],
                    Secret = config["TwitchVerifierSecret"]
                }
            };

            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
            httpClient.DefaultRequestHeaders.Add("Client-ID", config["TwitchClientId"]);
            var subscriptionRequest = await httpClient.PostAsync("https://api.twitch.tv/helix/eventsub/subscriptions",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json"));

            return new OkObjectResult(await subscriptionRequest.Content.ReadAsStringAsync());
        }

        [FunctionName("OnChannelPointsRedeemed")]
        public static async Task<IActionResult> OnChannelPointsRedeemed(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "twitch/points/OnChannelPointsRedeemed")] HttpRequest req,
            ILogger log, ExecutionContext ctx)
        {
            var httpClient = new HttpClient();
            var config = new ConfigurationBuilder()
                .SetBasePath(ctx.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables().Build();

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            req.Body.Position = 0;

            if (req.Headers.ContainsKey("Twitch-Eventsub-Message-Type") && req.Headers["Twitch-Eventsub-Message-Type"] == "webhook_callback_verification")
            {
                // run verification routine
                var messageId = req.Headers.ContainsKey("Twitch-Eventsub-Message-Id") ? req.Headers["Twitch-Eventsub-Message-Id"].ToString() : string.Empty;
                var timestamp = req.Headers.ContainsKey("Twitch-Eventsub-Message-Timestamp") ? req.Headers["Twitch-Eventsub-Message-Timestamp"].ToString() : string.Empty;
                // twitch states their verification is message id + timestamp + raw content bytes
                var headerBytes = Encoding.UTF8.GetBytes(messageId + timestamp);

                // validate signature from Twitch-Eventsub-Message-Signature
                using var alg = new HMACSHA256(Encoding.UTF8.GetBytes(config["TwitchVerifierSecret"]));
                using var ms = new MemoryStream();
                // first add the messageId/timestamp bytes
                await ms.WriteAsync(headerBytes);
                // make the sure the stream position is the end, so we write the rest after
                ms.Position = ms.Length;
                await req.Body.CopyToAsync(ms);

                var computedHashBytes = alg.ComputeHash(ms.ToArray());
                var sBuilder = new StringBuilder();

                for (int i = 0; i < computedHashBytes.Length; i++)
                {
                    sBuilder.Append(computedHashBytes[i].ToString("x2"));
                }

                var computedHash = sBuilder.ToString();
                var sentHash = req.Headers.ContainsKey("Twitch-Eventsub-Message-Signature") ? req.Headers["Twitch-Eventsub-Message-Signature"].ToString().Split('=')[1] : string.Empty;
                StringComparer comparer = StringComparer.OrdinalIgnoreCase;
                var valid = comparer.Compare(computedHash, sentHash) == 0;

                if (!valid) return new BadRequestObjectResult(new { Message = "Signature not valid" });

                // return challenge value from payload
                var jdoc = JsonDocument.Parse(requestBody);
                var challenge = jdoc.RootElement.GetProperty("challenge").GetString();
                log.LogInformation($"Returning challenge: {challenge}");
                return new ContentResult()
                {
                    Content = challenge,
                    ContentType = "text/plain",
                    StatusCode = 200
                };
            }
            return new OkObjectResult(requestBody);
        }
    }

    public class TwitchAuthentication
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly HttpClient _client;
        private readonly IDictionary<string, TwitchAccessTokenResponse> _tokenCache;

        public TwitchAuthentication(string clientId, string secret) : this(new System.Net.Http.HttpClient(), clientId, secret) { }

        public TwitchAuthentication(IHttpClientFactory clientFactory, string clientId, string secret) : this(clientFactory.CreateClient(), clientId, secret) { }

        public TwitchAuthentication(HttpClient client, string clientId, string secret)
        {
            _client = client;
            _clientId = clientId;
            _clientSecret = secret;
            _tokenCache = new Dictionary<string, TwitchAccessTokenResponse>();
        }

        public async Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string scope, bool force = false)
        {
            return await GetAccessTokenForAppAsync(new[] { scope }, force);
        }

        public async Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string[] scopes, bool force = false)
        {
            //POST https://id.twitch.tv/oauth2/token?client_id=uo6dggojyb8d6soh92zknwmi5ej1q2&client_secret=nyo51xcdrerl8z9m56w9w6wg&grant_type=client_credentials
            var existing = _tokenCache.Where(x => scopes.Contains(x.Key, StringComparer.OrdinalIgnoreCase));
            if (existing.Any() && existing.Any(a => a.Value.ExpirationTime > DateTime.UtcNow) && !force)
            {
                return existing.First(a => a.Value.ExpirationTime > DateTime.UtcNow).Value;
            }

            var uri = $"https://id.twitch.tv/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials&scope={string.Join(' ', scopes)}";
            var response = await _client.PostAsync(uri, new StringContent(""));

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<TwitchAccessTokenResponse>(await response.Content.ReadAsStringAsync());
                foreach (var scope in scopes)
                {

                }

                return tokenResponse;
            }
            return default;
        }
    }

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