namespace GfnTvBackend.Models;

public sealed record TelegramNewsPostRequest(
    long TelegramPostId,
    string Caption,
    string ImagePath,
    DateTimeOffset PublishedAt,
    string Source,
    string? Language,
    string? ImageBase64,
    string? ImageContentType);

public sealed record AdminNewsPostRequest(
    string Caption,
    string ImageUrl,
    DateTimeOffset? PublishedAt,
    string? Source,
    string? Language,
    bool? IsFeatured);

public sealed record NewsPost(
    string Id,
    long TelegramPostId,
    string Caption,
    string ImagePath,
    string? ImageUrl,
    string Source,
    string Language,
    string ModerationStatus,
    bool IsFeatured,
    DateTimeOffset PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ExpiresAt);

public sealed record NewsAdRequest(
    string Title,
    string? Subtitle,
    string ImageUrl,
    string? TargetUrl,
    bool? IsActive);

public sealed record NewsAd(
    string Id,
    string Title,
    string? Subtitle,
    string ImageUrl,
    string? TargetUrl,
    bool IsActive,
    DateTimeOffset CreatedAt);
