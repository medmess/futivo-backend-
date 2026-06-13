namespace GfnTvBackend.Models;

public sealed record MatchPredictionRequest(
    string? HomeTeam,
    string? AwayTeam,
    int HomeScore,
    int AwayScore,
    DateTimeOffset? Kickoff);

public sealed record MatchPrediction(
    string Id,
    string UserId,
    string MatchId,
    string HomeTeam,
    string AwayTeam,
    int HomeScore,
    int AwayScore,
    DateTimeOffset? Kickoff,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
