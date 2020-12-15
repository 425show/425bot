using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;

namespace _425bot
{
    public class TwitchAuthenticator
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly HttpClient _client;
        private readonly IDictionary<string, TwitchAccessTokenResponse> _tokenCache;

        public TwitchAuthenticator(string clientId, string secret) : this(new System.Net.Http.HttpClient(), clientId, secret) { }

        public TwitchAuthenticator(IHttpClientFactory clientFactory, string clientId, string secret) : this(clientFactory.CreateClient(), clientId, secret) { }

        public TwitchAuthenticator(HttpClient client, string clientId, string secret)
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
                    //todo: cache
                }

                return tokenResponse;
            }
            return default;
        }
    }
}