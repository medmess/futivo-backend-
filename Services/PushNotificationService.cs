using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using GfnTvBackend.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class PushNotificationService(
    IPushNotificationRepository repository,
    IFirebasePushSender sender)
{
    public Task RegisterTokenAsync(AuthenticatedUser user, PushTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new ArgumentException("Token is required.");
        }

        return repository.UpsertTokenAsync(user.Id, request.Token.Trim(), Clean(request.Platform));
    }

    public async Task<NotificationSendResult> SendAdminAsync(
        AdminNotificationRequest request,
        string? createdBy = null)
    {
        var title = CleanRequired(request.Title, "Title");
        var body = CleanRequired(request.Body, "Body");
        var targetType = NormalizeTargetType(request.TargetType);
        var targetValue = Clean(request.TargetValue);
        var campaign = await repository.CreateCampaignAsync(
            title,
            body,
            Clean(request.ImageUrl),
            targetType,
            targetValue,
            createdBy);

        var tokens = await repository.GetTokensAsync(targetType, targetValue);
        var data = request.Data ?? new Dictionary<string, string>();
        data["campaignId"] = campaign.Id;
        data["type"] = "manual";

        var result = await sender.SendAsync(tokens.Select(token => token.Token), title, body, request.ImageUrl, data);
        await repository.MarkCampaignSentAsync(campaign.Id);
        await repository.LogAsync(tokens, campaign.Id, title, body, "manual", data);
        return result;
    }

    public async Task SendMatchEventAsync(
        ManualMatchDetails previous,
        ManualMatchDetails current)
    {
        var previousKeys = previous.Events.Select(EventKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newEvents = current.Events
            .Where(matchEvent => !previousKeys.Contains(EventKey(matchEvent)))
            .Where(IsPushWorthEvent)
            .ToArray();

        foreach (var matchEvent in newEvents)
        {
            var title = MatchEventTitle(matchEvent);
            var body = $"{current.HomeTeam} vs {current.AwayTeam} - {matchEvent.Player} ({matchEvent.Minute}')";
            var tokens = await repository.GetTokensAsync("all", null);
            var data = new Dictionary<string, string>
            {
                ["type"] = "match_event",
                ["matchId"] = current.MatchId,
                ["eventType"] = matchEvent.Type,
                ["minute"] = matchEvent.Minute.ToString()
            };

            await sender.SendAsync(tokens.Select(token => token.Token), title, body, null, data);
            await repository.LogAsync(tokens, null, title, body, "match_event", data);
        }
    }

    private static string EventKey(MatchEvent matchEvent)
    {
        return $"{matchEvent.Minute}:{matchEvent.Team}:{matchEvent.Player}:{matchEvent.Type}:{matchEvent.Scored}";
    }

    private static bool IsPushWorthEvent(MatchEvent matchEvent)
    {
        var type = matchEvent.Type.Trim().ToLowerInvariant();
        return type is "goal" or "red_card" or "yellow_card" or "penalty" or "missed_penalty";
    }

    private static string MatchEventTitle(MatchEvent matchEvent)
    {
        return matchEvent.Type.Trim().ToLowerInvariant() switch
        {
            "goal" => "But en direct",
            "red_card" => "Carton rouge",
            "yellow_card" => "Carton jaune",
            "penalty" => matchEvent.Scored == false ? "Penalty rate" : "Penalty marque",
            "missed_penalty" => "Penalty rate",
            _ => "Evenement du match"
        };
    }

    private static string NormalizeTargetType(string? targetType)
    {
        var normalized = Clean(targetType)?.ToLowerInvariant();
        return normalized is "user" or "favorite_team" ? normalized : "all";
    }

    private static string CleanRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.");
        }

        return value.Trim();
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public interface IPushNotificationRepository
{
    Task UpsertTokenAsync(string userId, string token, string? platform);
    Task<IReadOnlyList<PushToken>> GetTokensAsync(string targetType, string? targetValue);
    Task<NotificationCampaign> CreateCampaignAsync(
        string title,
        string body,
        string? imageUrl,
        string targetType,
        string? targetValue,
        string? createdBy);
    Task MarkCampaignSentAsync(string campaignId);
    Task LogAsync(
        IReadOnlyList<PushToken> tokens,
        string? campaignId,
        string title,
        string body,
        string type,
        Dictionary<string, string> data);
}

public sealed class InMemoryPushNotificationRepository : IPushNotificationRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, PushToken> _tokensByToken = new(StringComparer.Ordinal);

    public Task UpsertTokenAsync(string userId, string token, string? platform)
    {
        lock (_lock)
        {
            _tokensByToken[token] = new PushToken(userId, token, platform);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PushToken>> GetTokensAsync(string targetType, string? targetValue)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PushToken>>(_tokensByToken.Values.ToArray());
        }
    }

    public Task<NotificationCampaign> CreateCampaignAsync(
        string title,
        string body,
        string? imageUrl,
        string targetType,
        string? targetValue,
        string? createdBy)
    {
        return Task.FromResult(new NotificationCampaign(
            Guid.NewGuid().ToString("N"),
            title,
            body,
            imageUrl,
            targetType,
            targetValue,
            createdBy,
            DateTimeOffset.UtcNow,
            null));
    }

    public Task MarkCampaignSentAsync(string campaignId) => Task.CompletedTask;

    public Task LogAsync(
        IReadOnlyList<PushToken> tokens,
        string? campaignId,
        string title,
        string body,
        string type,
        Dictionary<string, string> data) => Task.CompletedTask;
}

public sealed class SupabasePushNotificationRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : IPushNotificationRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task UpsertTokenAsync(string userId, string token, string? platform)
    {
        using var request = CreateRequest(HttpMethod.Post, "user_push_tokens?on_conflict=fcm_token");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                user_id = userId,
                fcm_token = token,
                platform,
                updated_at = DateTimeOffset.UtcNow
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PushToken>> GetTokensAsync(string targetType, string? targetValue)
    {
        if (targetType == "user" && !string.IsNullOrWhiteSpace(targetValue))
        {
            return await GetTokensByPathAsync($"user_push_tokens?select=user_id,fcm_token,platform&user_id=eq.{Uri.EscapeDataString(targetValue)}");
        }

        if (targetType == "favorite_team" && !string.IsNullOrWhiteSpace(targetValue))
        {
            var userIds = await GetUserIdsByFavoriteTeamAsync(targetValue);
            if (userIds.Count == 0) return [];
            return await GetTokensByUserIdsAsync(userIds);
        }

        return await GetTokensByPathAsync("user_push_tokens?select=user_id,fcm_token,platform");
    }

    public async Task<NotificationCampaign> CreateCampaignAsync(
        string title,
        string body,
        string? imageUrl,
        string targetType,
        string? targetValue,
        string? createdBy)
    {
        using var request = CreateRequest(HttpMethod.Post, "notification_campaigns");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                title,
                body,
                image_url = imageUrl,
                target_type = targetType,
                target_value = targetValue,
                created_by = createdBy,
                created_at = DateTimeOffset.UtcNow
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return ParseCampaign(document.RootElement.EnumerateArray().First());
    }

    public async Task MarkCampaignSentAsync(string campaignId)
    {
        using var request = CreateRequest(
            HttpMethod.Patch,
            $"notification_campaigns?id=eq.{Uri.EscapeDataString(campaignId)}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { sent_at = DateTimeOffset.UtcNow }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogAsync(
        IReadOnlyList<PushToken> tokens,
        string? campaignId,
        string title,
        string body,
        string type,
        Dictionary<string, string> data)
    {
        if (tokens.Count == 0) return;

        using var request = CreateRequest(HttpMethod.Post, "notification_logs");
        request.Content = new StringContent(
            JsonSerializer.Serialize(tokens.Select(token => new
            {
                user_id = token.UserId,
                campaign_id = campaignId,
                title,
                body,
                type,
                data,
                created_at = DateTimeOffset.UtcNow
            }), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<string>> GetUserIdsByFavoriteTeamAsync(string favoriteTeam)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"profiles?select=id&favorite_team=eq.{Uri.EscapeDataString(favoriteTeam)}");
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
    }

    private async Task<IReadOnlyList<PushToken>> GetTokensByUserIdsAsync(IReadOnlyList<string> userIds)
    {
        var chunks = userIds.Chunk(80);
        var tokens = new List<PushToken>();
        foreach (var chunk in chunks)
        {
            var ids = string.Join(",", chunk.Select(Uri.EscapeDataString));
            tokens.AddRange(await GetTokensByPathAsync(
                $"user_push_tokens?select=user_id,fcm_token,platform&user_id=in.({ids})"));
        }

        return tokens;
    }

    private async Task<IReadOnlyList<PushToken>> GetTokensByPathAsync(string path)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement
            .EnumerateArray()
            .Select(ParseToken)
            .Where(token => !string.IsNullOrWhiteSpace(token.Token))
            .ToArray();
    }

    private static PushToken ParseToken(JsonElement item)
    {
        return new PushToken(
            item.GetProperty("user_id").GetString() ?? "",
            item.GetProperty("fcm_token").GetString() ?? "",
            item.TryGetProperty("platform", out var platform) ? platform.GetString() : null);
    }

    private static NotificationCampaign ParseCampaign(JsonElement item)
    {
        return new NotificationCampaign(
            item.GetProperty("id").GetString() ?? "",
            item.GetProperty("title").GetString() ?? "",
            item.GetProperty("body").GetString() ?? "",
            item.TryGetProperty("image_url", out var imageUrl) ? imageUrl.GetString() : null,
            item.GetProperty("target_type").GetString() ?? "all",
            item.TryGetProperty("target_value", out var targetValue) ? targetValue.GetString() : null,
            item.TryGetProperty("created_by", out var createdBy) ? createdBy.GetString() : null,
            item.GetProperty("created_at").GetDateTimeOffset(),
            item.TryGetProperty("sent_at", out var sentAt) && sentAt.ValueKind != JsonValueKind.Null
                ? sentAt.GetDateTimeOffset()
                : null);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(
            method,
            $"{_options.Url!.TrimEnd('/')}/rest/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        return request;
    }
}

public interface IFirebasePushSender
{
    Task<NotificationSendResult> SendAsync(
        IEnumerable<string> tokens,
        string title,
        string body,
        string? imageUrl,
        Dictionary<string, string> data);
}

public sealed class FirebasePushSender(
    IConfiguration configuration,
    ILogger<FirebasePushSender> logger) : IFirebasePushSender
{
    private readonly Lazy<FirebaseMessaging?> _messaging = new(() =>
    {
        var credential = CreateCredential(configuration);
        if (credential is null)
        {
            logger.LogWarning("Firebase service account is not configured. Push notifications will be logged only.");
            return null;
        }

        var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
        {
            Credential = credential
        });

        return FirebaseMessaging.GetMessaging(app);
    });

    public async Task<NotificationSendResult> SendAsync(
        IEnumerable<string> tokens,
        string title,
        string body,
        string? imageUrl,
        Dictionary<string, string> data)
    {
        var tokenList = tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tokenList.Length == 0) return new NotificationSendResult(0, 0, 0);

        var messaging = _messaging.Value;
        if (messaging is null)
        {
            logger.LogInformation("Push notification skipped for {Count} tokens: {Title}", tokenList.Length, title);
            return new NotificationSendResult(tokenList.Length, 0, tokenList.Length);
        }

        var sent = 0;
        var failed = 0;
        foreach (var chunk in tokenList.Chunk(500))
        {
            var message = new MulticastMessage
            {
                Tokens = chunk.ToList(),
                Notification = new Notification
                {
                    Title = title,
                    Body = body,
                    ImageUrl = imageUrl
                },
                Data = data,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ChannelId = "futivo_alerts",
                        Sound = "default"
                    }
                }
            };

            var response = await messaging.SendEachForMulticastAsync(message);
            sent += response.SuccessCount;
            failed += response.FailureCount;
        }

        return new NotificationSendResult(tokenList.Length, sent, failed);
    }

    private static GoogleCredential? CreateCredential(IConfiguration configuration)
    {
        var rawJson = configuration["Firebase:ServiceAccountJson"];
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            return GoogleCredential.FromJson(rawJson);
        }

        var base64Json = configuration["Firebase:ServiceAccountJsonBase64"];
        if (!string.IsNullOrWhiteSpace(base64Json))
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Json));
            return GoogleCredential.FromJson(json);
        }

        var path = configuration["Firebase:ServiceAccountPath"];
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return GoogleCredential.FromFile(path);
        }

        return null;
    }
}
