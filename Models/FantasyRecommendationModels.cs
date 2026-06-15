namespace GfnTvBackend.Models;

public sealed record FantasyRecommendationRequest(
    IReadOnlyList<FantasyRecommendationPlayer> Players,
    decimal Budget = 100m,
    string Formation = "4-3-3",
    int MaxPlayersPerClub = 3,
    string? UserSeed = null);

public sealed record FantasyPlayerRecommendationRequest(
    IReadOnlyList<FantasyRecommendationPlayer> Players,
    string Position,
    decimal RemainingBudget,
    IReadOnlyList<string>? ExcludedPlayerIds = null,
    string? UserSeed = null);

public sealed record FantasyRecommendationPlayer(
    string Id,
    string Name,
    string Club,
    string Position,
    decimal Price,
    int Points = 0,
    double? RecentPerformance = null,
    double? ExpectedPlayingTime = null,
    double? FixtureDifficulty = null,
    double? Consistency = null,
    double? Popularity = null,
    bool IsInjured = false,
    bool IsSuspended = false);

public sealed record RecommendedSquadResponse(
    IReadOnlyList<RecommendedSquadVariant> Variants,
    string Message,
    DateTimeOffset GeneratedAt);

public sealed record RecommendedSquadVariant(
    string Key,
    string Title,
    string Subtitle,
    IReadOnlyList<FantasyRecommendationPlayer> Players,
    decimal UsedBudget,
    double Score,
    bool IsComplete,
    IReadOnlyList<string> Reasons);

public sealed record RecommendedPlayerResponse(
    IReadOnlyList<RecommendedPlayerDto> Players,
    DateTimeOffset GeneratedAt);

public sealed record RecommendedPlayerDto(
    FantasyRecommendationPlayer Player,
    double Score,
    IReadOnlyList<string> Reasons);
