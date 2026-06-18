namespace GfnTvBackend.Models;

public sealed record FantasySquadPlayer(
    string Id,
    string Name,
    string Club,
    string Position,
    double Price,
    int Points);

public sealed record FantasySquadRequest(
    IReadOnlyList<FantasySquadPlayer> Players,
    string? CaptainId,
    string? ViceCaptainId);

public sealed record FantasySquad(
    string UserId,
    IReadOnlyList<FantasySquadPlayer> Players,
    string? CaptainId,
    string? ViceCaptainId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
