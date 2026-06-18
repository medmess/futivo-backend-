using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class InMemoryFantasySquadRepository : IFantasySquadRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, FantasySquad> _squads = [];

    public Task<FantasySquad?> GetAsync(AuthenticatedUser user)
    {
        lock (_lock)
        {
            _squads.TryGetValue(user.Id, out var squad);
            return Task.FromResult(squad);
        }
    }

    public Task<FantasySquad> UpsertAsync(AuthenticatedUser user, FantasySquadRequest request)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var createdAt = _squads.TryGetValue(user.Id, out var existing)
                ? existing.CreatedAt
                : now;
            var squad = new FantasySquad(
                user.Id,
                request.Players,
                request.CaptainId,
                request.ViceCaptainId,
                createdAt,
                now);
            _squads[user.Id] = squad;
            return Task.FromResult(squad);
        }
    }
}
