using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseManualMatchRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : IManualMatchRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ManualMatchDetails?> GetAsync(string matchId)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"manual_match_details?select=*&match_id=eq.{Uri.EscapeDataString(matchId)}&limit=1");
        using var response = await httpClient.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var rows = document.RootElement.EnumerateArray().ToArray();
        return rows.Length == 0 ? null : Parse(rows[0]);
    }

    public async Task<ManualMatchDetails> UpsertAsync(ManualMatchDetails details)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "manual_match_details?on_conflict=match_id");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                match_id = details.MatchId,
                home_team = details.HomeTeam,
                away_team = details.AwayTeam,
                home_formation = details.HomeFormation,
                away_formation = details.AwayFormation,
                live_stream_url = details.LiveStreamUrl,
                home_lineup = details.HomeLineup,
                away_lineup = details.AwayLineup,
                events = details.Events,
                updated_at = details.UpdatedAt
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return details;
    }

    private static ManualMatchDetails Parse(JsonElement item)
    {
        return new ManualMatchDetails(
            item.GetProperty("match_id").GetString()!,
            item.GetProperty("home_team").GetString() ?? "",
            item.GetProperty("away_team").GetString() ?? "",
            item.TryGetProperty("home_formation", out var homeFormation) ? homeFormation.GetString() : null,
            item.TryGetProperty("away_formation", out var awayFormation) ? awayFormation.GetString() : null,
            item.TryGetProperty("live_stream_url", out var liveStreamUrl) ? liveStreamUrl.GetString() : null,
            ParseJsonArray<MatchLineupPlayer>(item, "home_lineup"),
            ParseJsonArray<MatchLineupPlayer>(item, "away_lineup"),
            ParseJsonArray<MatchEvent>(item, "events"),
            item.GetProperty("updated_at").GetDateTimeOffset());
    }

    private static IReadOnlyList<T> ParseJsonArray<T>(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        return JsonSerializer.Deserialize<T[]>(value.GetRawText(), JsonOptions) ?? [];
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
