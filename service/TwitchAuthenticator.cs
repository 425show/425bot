using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _425bot
{
    public interface ITwitchAuthenticator
    {
        string GenerateAuthorizationUrl(string scopes = null);
        Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string scope, bool force = false);
        Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(bool force = false);
        Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string[] scopes, bool force = false);
        Task<ServiceResult<TwitchMessageResult>> AuthenticateMessage(Microsoft.AspNetCore.Http.HttpRequest req);
    }

    public class TwitchAuthenticatorConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RedirectUri { get; set; }
        public string Scopes { get; set; }
        public string VerifierSecret { get; set; }
        public string BroadcasterId { get; set; }
        public string ChannelPointsHandler { get; set; }
        public string StreamStatusHandler { get; internal set; }
    }

    public class TwitchAuthenticator : ITwitchAuthenticator
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly HttpClient _client;
        private readonly IDictionary<string, TwitchAccessTokenResponse> _tokenCache;
        private readonly TwitchAuthenticatorConfig _config;
        private readonly ILogger<TwitchAuthenticator> _log;

        public TwitchAuthenticator(IHttpClientFactory clientFactory, IOptions<TwitchAuthenticatorConfig> config, ILoggerFactory loggerFactory) : this(clientFactory.CreateClient(), config, loggerFactory) { }

        public TwitchAuthenticator(HttpClient client, IOptions<TwitchAuthenticatorConfig> config, ILoggerFactory loggerFactory)
        {
            _log = loggerFactory.CreateLogger<TwitchAuthenticator>();
            _client = client;
            _tokenCache = new Dictionary<string, TwitchAccessTokenResponse>();
            _config = config.Value;
            _clientId = _config.ClientId;
            _clientSecret = _config.ClientSecret;
        }

        public string GenerateAuthorizationUrl(string scopes = null)
        {
            var redirectUri = $"https://id.twitch.tv/oauth2/authorize?client_id={_config.ClientId}&redirect_uri={_config.RedirectUri}&response_type=token&scope={scopes ?? _config.Scopes}";
            _log.LogInformation($"Redirecting to authorization: {redirectUri}");
            return redirectUri;
        }

        public async Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string scope, bool force = false)
        {
            return await GetAccessTokenForAppAsync(new[] { scope }, force);
        }

        public async Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(bool force = false)
        {
            return await GetAccessTokenForAppAsync(new string[] { }, force);
        }

        public async Task<TwitchAccessTokenResponse> GetAccessTokenForAppAsync(string[] scopes, bool force = false)
        {
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
                    //todo: cache
                }

                return tokenResponse;
            }
            return default;
        }

        public async Task<ServiceResult<TwitchMessageResult>> AuthenticateMessage(Microsoft.AspNetCore.Http.HttpRequest req)
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            req.Body.Position = 0;

            // run verification routine
            var messageId = req.Headers.ContainsKey("Twitch-Eventsub-Message-Id") ? req.Headers["Twitch-Eventsub-Message-Id"].ToString() : string.Empty;
            var timestamp = req.Headers.ContainsKey("Twitch-Eventsub-Message-Timestamp") ? req.Headers["Twitch-Eventsub-Message-Timestamp"].ToString() : string.Empty;
            // twitch states their verification is message id + timestamp + raw content bytes
            var headerBytes = Encoding.UTF8.GetBytes(messageId + timestamp);

            // validate signature from Twitch-Eventsub-Message-Signature
            using var alg = new HMACSHA256(Encoding.UTF8.GetBytes(_config.VerifierSecret));
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

            if (!valid) return ServiceResult<TwitchMessageResult>.FromError("Signature not valid");

            return ServiceResult<TwitchMessageResult>.FromResult(new TwitchMessageResult()
            {
                MessageValidated = true,
                Message = requestBody
            });
        }
    }

    public class TwitchMessageResult
    {
        public bool MessageValidated { get; set; }
        public string Message { get; set; }
    }

    public class ServiceResult
    {
        public string Message { get; set; }
        public bool Success { get; set; }
        public string ErrorCode { get; set; }
        public Exception Exception { get; set; }
        public ServiceResult()
        {

        }
        public static ServiceResult FromError(string message)
        {
            return new ServiceResult()
            {
                Message = message,
                Success = false
            };
        }
        public static ServiceResult FromError(Exception ex)
        {
            return new ServiceResult()
            {
                Message = ex.Message,
                Exception = ex,
                Success = false
            };
        }
    }

    public class ServiceResult<T> : ServiceResult
    {
        public T Value { get; set; }
        public ServiceResult() { }

        public ServiceResult(T value)
        {
            Value = value;
            Success = true;
        }

        public static ServiceResult<T> FromResult(T value)
        {
            return new ServiceResult<T>(value);
        }

        public new static ServiceResult<T> FromError(string message)
        {
            return new ServiceResult<T>()
            {
                Message = message,
                Success = false
            };
        }
        public new static ServiceResult<T> FromError(Exception ex)
        {
            return new ServiceResult<T>()
            {
                Message = ex.Message,
                Exception = ex,
                Success = false
            };
        }

        // public static ServiceResult FromError(string message)
        // {
        //     return new ServiceResult()
        //     {
        //         Message = message,
        //         Success = false
        //     };
        // }
        // public static ServiceResult FromError(Exception ex)
        // {
        //     return new ServiceResult()
        //     {
        //         Message = ex.Message,
        //         Exception = ex,
        //         Success = false
        //     };
        // }
    }
}