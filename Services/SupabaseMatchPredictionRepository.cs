using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseMatchPredictionRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : IMatchPredictionRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MatchPrediction>> GetMineAsync(
        AuthenticatedUser user)
    {
        var rows = await GetJsonArrayAsync(
            $"match_predictions?select=*&user_id=eq.{user.Id}&order=updated_at.desc");
        return rows.Select(Parse).ToArray();
    }

    public async Task<MatchPrediction?> GetAsync(
        AuthenticatedUser user,
        string matchId)
    {
        var rows = await GetJsonArrayAsync(
            $"match_predictions?select=*&user_id=eq.{user.Id}&match_id=eq.{Uri.EscapeDataString(matchId)}&limit=1");
        return rows.Count == 0 ? null : Parse(rows[0]);
    }

    public async Task<MatchPrediction> UpsertAsync(
        AuthenticatedUser user,
        MatchPrediction prediction)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "match_predictions?on_conflict=user_id,match_id");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                id = prediction.Id,
                user_id = user.Id,
                match_id = prediction.MatchId,
                home_team = prediction.HomeTeam,
                away_team = prediction.AwayTeam,
                home_score = prediction.HomeScore,
                away_score = prediction.AwayScore,
                kickoff = prediction.Kickoff,
                created_at = prediction.CreatedAt,
                updated_at = prediction.UpdatedAt
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
        return rows.Length == 0 ? prediction : Parse(rows[0]);
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

    private static MatchPrediction Parse(JsonElement item)
    {
        return new MatchPrediction(
            item.GetProperty("id").GetString()!,
            item.GetProperty("user_id").GetString()!,
            item.GetProperty("match_id").GetString()!,
            item.GetProperty("home_team").GetString() ?? "",
            item.GetProperty("away_team").GetString() ?? "",
            item.GetProperty("home_score").GetInt32(),
            item.GetProperty("away_score").GetInt32(),
            item.TryGetProperty("kickoff", out var kickoff) &&
            kickoff.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? kickoff.GetDateTimeOffset()
                : null,
            item.GetProperty("created_at").GetDateTimeOffset(),
            item.GetProperty("updated_at").GetDateTimeOffset());
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
