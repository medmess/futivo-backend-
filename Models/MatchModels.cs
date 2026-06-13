namespace GfnTvBackend.Models;

public sealed record ManualMatchDetailsRequest(
    string MatchId,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    string? LiveStreamUrl,
    IReadOnlyList<MatchLineupPlayerRequest>? HomeLineup,
    IReadOnlyList<MatchLineupPlayerRequest>? AwayLineup,
    IReadOnlyList<MatchEventRequest>? Events);

public sealed record MatchLineupPlayerRequest(
    string Name,
    string? Position,
    int? ShirtNumber,
    bool? Starter);

public sealed record MatchEventRequest(
    int Minute,
    string Team,
    string Player,
    string Type,
    string? Detail,
    string? Assist,
    bool? Scored);

public sealed record ManualMatchDetails(
    string MatchId,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    string? LiveStreamUrl,
    IReadOnlyList<MatchLineupPlayer> HomeLineup,
    IReadOnlyList<MatchLineupPlayer> AwayLineup,
    IReadOnlyList<MatchEvent> Events,
    DateTimeOffset UpdatedAt);

public sealed record MatchLineupPlayer(
    string Name,
    string? Position,
    int? ShirtNumber,
    bool Starter);

public sealed record MatchEvent(
    int Minute,
    string Team,
    string Player,
    string Type,
    string? Detail,
    string? Assist,
    bool? Scored);
