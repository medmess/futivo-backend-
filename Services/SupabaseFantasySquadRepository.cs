using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseFantasySquadRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : IFantasySquadRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FantasySquad?> GetAsync(AuthenticatedUser user)
    {
        var rows = await GetJsonArrayAsync(
            $"fantasy_squads?select=*&user_id=eq.{user.Id}&limit=1");
        return rows.Count == 0 ? null : Parse(rows[0]);
    }

    public async Task<FantasySquad> UpsertAsync(
        AuthenticatedUser user,
        FantasySquadRequest squad)
    {
        var now = DateTimeOffset.UtcNow;
        using var request = CreateRequest(
            HttpMethod.Post,
            "fantasy_squads?on_conflict=user_id");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                user_id = user.Id,
                players = squad.Players,
                captain_id = squad.CaptainId,
                vice_captain_id = squad.ViceCaptainId,
                updated_at = now
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "resolution=merge-duplicates,return=representation");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var rows = document.RootElement.EnumerateArray().ToArray();
        return rows.Length == 0
            ? new FantasySquad(user.Id, squad.Players, squad.CaptainId, squad.ViceCaptainId, now, now)
            : Parse(rows[0]);
    }

    private async Task<IReadOnlyList<JsonElement>> GetJsonArrayAsync(string path)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static FantasySquad Parse(JsonElement item)
    {
        return new FantasySquad(
            item.GetProperty("user_id").GetString() ?? "",
            ParsePlayers(item.GetProperty("players")),
            item.TryGetProperty("captain_id", out var captainId) &&
            captainId.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? captainId.GetString()
                : null,
            item.TryGetProperty("vice_captain_id", out var viceCaptainId) &&
            viceCaptainId.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? viceCaptainId.GetString()
                : null,
            item.GetProperty("created_at").GetDateTimeOffset(),
            item.GetProperty("updated_at").GetDateTimeOffset());
    }

    private static IReadOnlyList<FantasySquadPlayer> ParsePlayers(JsonElement players)
    {
        if (players.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return players.EnumerateArray()
            .Select(player => player.Deserialize<FantasySquadPlayer>(JsonOptions))
            .Where(player => player is not null)
            .Select(player => player!)
            .ToArray();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(
            method,
            $"{_options.Url!.TrimEnd('/')}/rest/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.ServiceRoleKey);
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        return request;
    }
}
