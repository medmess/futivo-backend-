namespace GfnTvBackend.Models;

public sealed record LeagueDto(
    string Key,
    string Name,
    string Country,
    string? SportsDbLeagueId);

public sealed record StandingRowDto(
    int Position,
    string TeamName,
    string? TeamBadge,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points);

public sealed record FixtureDto(
    string Id,
    string LeagueName,
    string HomeTeam,
    string AwayTeam,
    string? HomeBadge,
    string? AwayBadge,
    DateTimeOffset? Kickoff,
    string Status,
    int? HomeScore,
    int? AwayScore,
    string? Venue,
    bool LiveVerified);

public sealed record TeamDto(
    string Id,
    string Name,
    string? ShortName,
    string? Badge,
    string? Logo,
    string? Stadium,
    string? Country,
    string? Website,
    string? Description);

public sealed record PlayerDto(
    string Id,
    string TeamId,
    string TeamName,
    string Name,
    string? Position,
    string? Nationality,
    string? Image,
    string? Cutout,
    string? Thumb,
    string? DateBorn);

public sealed record EventDetailsDto(
    string Id,
    string LeagueName,
    string Season,
    string HomeTeam,
    string AwayTeam,
    string? HomeBadge,
    string? AwayBadge,
    int? HomeScore,
    int? AwayScore,
    DateTimeOffset? Kickoff,
    string? Venue,
    string Status,
    bool LiveVerified);

public sealed record EventLineupDto(
    string Id,
    string EventId,
    string TeamName,
    string PlayerName,
    string? Position,
    string? FormationPosition,
    bool Starter);

public sealed record EventTimelineDto(
    string Id,
    string EventId,
    string TeamName,
    string PlayerName,
    string? Assist,
    string Type,
    int? Minute,
    string? Detail);

public sealed record LeagueDataBundleDto(
    LeagueDto League,
    IReadOnlyList<TeamDto> Teams,
    IReadOnlyList<FixtureDto> UpcomingFixtures,
    IReadOnlyList<FixtureDto> LatestResults);
