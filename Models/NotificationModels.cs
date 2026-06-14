namespace GfnTvBackend.Models;

public sealed record PushTokenRequest(
    string Token,
    string? Platform);

public sealed record AdminNotificationRequest(
    string Title,
    string Body,
    string? ImageUrl,
    string? TargetType,
    string? TargetValue,
    Dictionary<string, string>? Data);

public sealed record NotificationCampaign(
    string Id,
    string Title,
    string Body,
    string? ImageUrl,
    string TargetType,
    string? TargetValue,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed record PushToken(
    string UserId,
    string Token,
    string? Platform);

public sealed record NotificationSendResult(
    int Targeted,
    int Sent,
    int Failed);
