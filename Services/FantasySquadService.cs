using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public interface IFantasySquadRepository
{
    Task<FantasySquad?> GetAsync(AuthenticatedUser user);
    Task<FantasySquad> UpsertAsync(AuthenticatedUser user, FantasySquadRequest request);
}

public sealed class FantasySquadService(IFantasySquadRepository repository)
{
    public Task<FantasySquad?> GetAsync(AuthenticatedUser user)
    {
        return repository.GetAsync(user);
    }

    public Task<FantasySquad> UpsertAsync(AuthenticatedUser user, FantasySquadRequest request)
    {
        var players = request.Players
            .Where(player =>
                !string.IsNullOrWhiteSpace(player.Id) &&
                !string.IsNullOrWhiteSpace(player.Name) &&
                !string.IsNullOrWhiteSpace(player.Club) &&
                !string.IsNullOrWhiteSpace(player.Position))
            .GroupBy(player => player.Id)
            .Select(group => group.First())
            .Take(16)
            .ToArray();

        var playerIds = players.Select(player => player.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var captainId = !string.IsNullOrWhiteSpace(request.CaptainId) &&
                        playerIds.Contains(request.CaptainId)
            ? request.CaptainId
            : players.FirstOrDefault()?.Id;
        var viceCaptainId = !string.IsNullOrWhiteSpace(request.ViceCaptainId) &&
                            request.ViceCaptainId != captainId &&
                            playerIds.Contains(request.ViceCaptainId)
            ? request.ViceCaptainId
            : players.FirstOrDefault(player => player.Id != captainId)?.Id;

        return repository.UpsertAsync(
            user,
            new FantasySquadRequest(players, captainId, viceCaptainId));
    }
}
