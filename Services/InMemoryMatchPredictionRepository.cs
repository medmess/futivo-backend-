using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class InMemoryMatchPredictionRepository : IMatchPredictionRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, MatchPrediction> _predictions = [];

    public Task<IReadOnlyList<MatchPrediction>> GetMineAsync(AuthenticatedUser user)
    {
        lock (_lock)
        {
            var predictions = _predictions.Values
                .Where(prediction => prediction.UserId == user.Id)
                .OrderByDescending(prediction => prediction.UpdatedAt)
                .ToArray();

            return Task.FromResult<IReadOnlyList<MatchPrediction>>(predictions);
        }
    }

    public Task<MatchPrediction?> GetAsync(AuthenticatedUser user, string matchId)
    {
        lock (_lock)
        {
            _predictions.TryGetValue(Key(user.Id, matchId), out var prediction);
            return Task.FromResult(prediction);
        }
    }

    public Task<MatchPrediction> UpsertAsync(
        AuthenticatedUser user,
        MatchPrediction prediction)
    {
        lock (_lock)
        {
            _predictions[Key(user.Id, prediction.MatchId)] = prediction;
            return Task.FromResult(prediction);
        }
    }

    private static string Key(string userId, string matchId)
    {
        return $"{userId}:{matchId}";
    }
}
