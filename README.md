# 425bot

The bot for [The 425 Show](https://aka.ms/425show) on Twitch!

Mondays & Fridays at 8a PT/11a ET/4p UK/5p CET

Contributions welcome

## features

- Subscription & webhook driven
- Channel point redemption
- authorization url to add bot to channel
- plumbing for client_credential authentication

## sample settings

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet",
        "Twitch:ClientId": "twitch-client-id",
        "Twitch:ApiKey": "in secrets",
        "Twitch:ClientSecret": "twitch-client-secret",
        "Twitch:BroadcasterId": "twitch-broadcaster-id",
        "Twitch:RedirectUri":"function-redirect-url - see function",
        "Twitch:ChannelPointsHandler":"function channel points handler url - see function",
        "Twitch:Scopes":"channel:read:redemptions channel:manage:redemptions",
        "Twitch:VerifierSecret":"secret used for signatures - random string, not a good solution currently"
    }
}
```
