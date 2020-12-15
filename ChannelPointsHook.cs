using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace _425bot
{
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

            var twitchAuth = new TwitchAuthenticator(httpClient, config["TwitchClientId"], config["TwitchClientSecret"]);
            var token = await twitchAuth.GetAccessTokenForAppAsync(new[] { "channel:read:redemptions", "channel:manage:redemptions" });

            var request = new AddSubscriptionRequest()
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

            // todo: something, e.g., queue up <some redemption action, like changing lights, etc>


            return new OkObjectResult(requestBody);
        }
    }
}