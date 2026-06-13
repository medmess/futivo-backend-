using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class MatchPredictionService(IMatchPredictionRepository repository)
{
    public Task<IReadOnlyList<MatchPrediction>> GetMineAsync(AuthenticatedUser user)
    {
        return repository.GetMineAsync(user);
    }

    public Task<MatchPrediction?> GetAsync(AuthenticatedUser user, string matchId)
    {
        return repository.GetAsync(user, matchId.Trim());
    }

    public async Task<MatchPrediction> UpsertAsync(
        AuthenticatedUser user,
        string matchId,
        MatchPredictionRequest request)
    {
        var cleanedMatchId = matchId.Trim();
        if (string.IsNullOrWhiteSpace(cleanedMatchId))
        {
            throw new ArgumentException("matchId is required.", nameof(matchId));
        }

        if (request.HomeScore is < 0 or > 30 ||
            request.AwayScore is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Scores must be between 0 and 30.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await repository.GetAsync(user, cleanedMatchId);
        var prediction = new MatchPrediction(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            user.Id,
            cleanedMatchId,
            request.HomeTeam?.Trim() ?? existing?.HomeTeam ?? "",
            request.AwayTeam?.Trim() ?? existing?.AwayTeam ?? "",
            request.HomeScore,
            request.AwayScore,
            request.Kickoff ?? existing?.Kickoff,
            existing?.CreatedAt ?? now,
            now);

        return await repository.UpsertAsync(user, prediction);
    }
}

public interface IMatchPredictionRepository
{
    Task<IReadOnlyList<MatchPrediction>> GetMineAsync(AuthenticatedUser user);
    Task<MatchPrediction?> GetAsync(AuthenticatedUser user, string matchId);
    Task<MatchPrediction> UpsertAsync(AuthenticatedUser user, MatchPrediction prediction);
}
