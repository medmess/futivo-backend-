using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class ManualMatchService(IManualMatchRepository repository)
{
    public Task<ManualMatchDetails?> GetAsync(string matchId)
    {
        return repository.GetAsync(matchId.Trim());
    }

    public Task<ManualMatchDetails> UpsertAsync(ManualMatchDetailsRequest request)
    {
        var details = new ManualMatchDetails(
            request.MatchId.Trim(),
            request.HomeTeam.Trim(),
            request.AwayTeam.Trim(),
            Clean(request.HomeFormation),
            Clean(request.AwayFormation),
            Clean(request.LiveStreamUrl),
            (request.HomeLineup ?? [])
                .Where(player => !string.IsNullOrWhiteSpace(player.Name))
                .Select(ToLineupPlayer)
                .ToArray(),
            (request.AwayLineup ?? [])
                .Where(player => !string.IsNullOrWhiteSpace(player.Name))
                .Select(ToLineupPlayer)
                .ToArray(),
            (request.Events ?? [])
                .Where(matchEvent => !string.IsNullOrWhiteSpace(matchEvent.Player))
                .OrderBy(matchEvent => matchEvent.Minute)
                .Select(ToEvent)
                .ToArray(),
            DateTimeOffset.UtcNow);

        return repository.UpsertAsync(details);
    }

    private static MatchLineupPlayer ToLineupPlayer(MatchLineupPlayerRequest player)
    {
        return new MatchLineupPlayer(
            player.Name.Trim(),
            Clean(player.Position),
            player.ShirtNumber,
            player.Starter ?? true);
    }

    private static MatchEvent ToEvent(MatchEventRequest matchEvent)
    {
        return new MatchEvent(
            Math.Clamp(matchEvent.Minute, 0, 130),
            matchEvent.Team.Trim(),
            matchEvent.Player.Trim(),
            string.IsNullOrWhiteSpace(matchEvent.Type) ? "goal" : matchEvent.Type.Trim(),
            Clean(matchEvent.Detail),
            Clean(matchEvent.Assist),
            matchEvent.Scored);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public interface IManualMatchRepository
{
    Task<ManualMatchDetails?> GetAsync(string matchId);
    Task<ManualMatchDetails> UpsertAsync(ManualMatchDetails details);
}
