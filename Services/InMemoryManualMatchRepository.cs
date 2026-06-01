using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class InMemoryManualMatchRepository : IManualMatchRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ManualMatchDetails> _matches = [];

    public Task<ManualMatchDetails?> GetAsync(string matchId)
    {
        lock (_lock)
        {
            _matches.TryGetValue(matchId, out var details);
            return Task.FromResult(details);
        }
    }

    public Task<ManualMatchDetails> UpsertAsync(ManualMatchDetails details)
    {
        lock (_lock)
        {
            _matches[details.MatchId] = details;
            return Task.FromResult(details);
        }
    }
}
