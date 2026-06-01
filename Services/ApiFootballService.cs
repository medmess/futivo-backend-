using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class ApiFootballService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, ApiFootballLeagueConfig> LeagueConfigs =
        new Dictionary<string, ApiFootballLeagueConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["algeria-ligue-1"] = new("algeria-ligue-1", "Algerian Ligue 1", "Algeria", 186, 2024),
            ["ligue1-mobilis"] = new("ligue1-mobilis", "Ligue 1 Mobilis", "Algeria", 186, 2024),
            ["premier-league"] = new("premier-league", "English Premier League", "England", 39, 2024),
            ["bundesliga"] = new("bundesliga", "German Bundesliga", "Germany", 78, 2024),
            ["ligue-1"] = new("ligue-1", "French Ligue 1", "France", 61, 2024),
            ["serie-a"] = new("serie-a", "Italian Serie A", "Italy", 135, 2024),
            ["la-liga"] = new("la-liga", "Spanish La Liga", "Spain", 140, 2024)
        };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();

    public ApiFootballService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<IReadOnlyList<StandingRowDto>> GetStandingsAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        if (!IsConfigured) return [];

        return await GetOrCreateAsync($"api-football:standings:{config.Key}", TimeSpan.FromMinutes(45), async () =>
        {
            var json = await GetJsonAsync($"standings?league={config.LeagueId}&season={config.Season}");
            var root = JsonSerializer.Deserialize<ApiFootballResponse<StandingsPayload>>(json, JsonOptions);
            var standings = root?.Response?
                .FirstOrDefault()
                ?.League
                ?.Standings?
                .FirstOrDefault();

            return standings?
                .Select(MapStanding)
                .Where(row => !string.IsNullOrWhiteSpace(row.TeamName))
                .OrderBy(row => row.Position)
                .ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetFixturesByDateAsync(string leagueKey, DateTime date)
    {
        var config = ResolveConfig(leagueKey);
        if (!IsConfigured) return [];

        return await GetOrCreateAsync($"api-football:fixtures:{config.Key}:{date:yyyy-MM-dd}", TimeSpan.FromMinutes(10), async () =>
        {
            var json = await GetJsonAsync($"fixtures?league={config.LeagueId}&season={config.Season}&date={date:yyyy-MM-dd}");
            var root = JsonSerializer.Deserialize<ApiFootballResponse<FixturePayload>>(json, JsonOptions);
            return root?.Response?.Select(payload => MapFixture(payload, config.Name)).ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetUpcomingFixturesAsync(string leagueKey, int next = 10)
    {
        var config = ResolveConfig(leagueKey);
        if (!IsConfigured) return [];

        return await GetOrCreateAsync($"api-football:upcoming:{config.Key}:{next}", TimeSpan.FromMinutes(20), async () =>
        {
            var json = await GetJsonAsync($"fixtures?league={config.LeagueId}&season={config.Season}&next={next}");
            var root = JsonSerializer.Deserialize<ApiFootballResponse<FixturePayload>>(json, JsonOptions);
            return root?.Response?.Select(payload => MapFixture(payload, config.Name)).ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetLiveFixturesAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        if (!IsConfigured) return [];

        return await GetOrCreateAsync($"api-football:live:{config.Key}", TimeSpan.FromMinutes(1), async () =>
        {
            var json = await GetJsonAsync($"fixtures?league={config.LeagueId}&season={config.Season}&live=all");
            var root = JsonSerializer.Deserialize<ApiFootballResponse<FixturePayload>>(json, JsonOptions);
            return root?.Response?.Select(payload => MapFixture(payload, config.Name, liveVerified: true)).ToList() ?? [];
        });
    }

    private ApiFootballLeagueConfig ResolveConfig(string leagueKey)
    {
        if (LeagueConfigs.TryGetValue(leagueKey, out var config)) return config;
        throw new KeyNotFoundException($"Unknown league key: {leagueKey}");
    }

    private string? ApiKey =>
        _configuration["ApiFootball:Key"] ??
        _configuration["API_FOOTBALL_KEY"];

    private async Task<string> GetJsonAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://v3.football.api-sports.io/{path}");
        request.Headers.TryAddWithoutValidation("x-apisports-key", ApiKey);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out var item) && item.ExpiresAt > DateTimeOffset.UtcNow && item.Value is T cached)
        {
            return cached;
        }

        var value = await factory();
        _cache[key] = new CacheItem(value, DateTimeOffset.UtcNow.Add(ttl));
        return value;
    }

    private static StandingRowDto MapStanding(ApiStandingRow row)
    {
        return new StandingRowDto(
            row.Rank,
            row.Team?.Name ?? "",
            row.Team?.Logo,
            row.All?.Played ?? 0,
            row.All?.Win ?? 0,
            row.All?.Draw ?? 0,
            row.All?.Lose ?? 0,
            row.All?.Goals?.For ?? 0,
            row.All?.Goals?.Against ?? 0,
            row.GoalsDiff,
            row.Points);
    }

    private static FixtureDto MapFixture(FixturePayload payload, string leagueName, bool liveVerified = false)
    {
        var status = payload.Fixture?.Status?.Short?.ToUpperInvariant() switch
        {
            "NS" or "TBD" => "Upcoming",
            "FT" or "AET" or "PEN" => "Finished",
            "PST" or "CANC" or "ABD" => payload.Fixture?.Status?.Long ?? "Not available",
            _ => payload.Fixture?.Status?.Long ?? "Not live verified"
        };

        return new FixtureDto(
            (payload.Fixture?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
            payload.League?.Name ?? leagueName,
            payload.Teams?.Home?.Name ?? "",
            payload.Teams?.Away?.Name ?? "",
            payload.Teams?.Home?.Logo,
            payload.Teams?.Away?.Logo,
            payload.Fixture?.Date,
            status,
            payload.Goals?.Home,
            payload.Goals?.Away,
            payload.Fixture?.Venue?.Name,
            liveVerified);
    }

    private sealed record ApiFootballLeagueConfig(string Key, string Name, string Country, int LeagueId, int Season);

    private sealed record CacheItem(object? Value, DateTimeOffset ExpiresAt);

    private sealed record ApiFootballResponse<T>(List<T>? Response);

    private sealed record StandingsPayload(ApiLeague? League);

    private sealed record ApiLeague(List<List<ApiStandingRow>>? Standings);

    private sealed record ApiStandingRow(
        int Rank,
        ApiTeam? Team,
        int Points,
        int GoalsDiff,
        ApiStandingStats? All);

    private sealed record ApiStandingStats(
        int Played,
        int Win,
        int Draw,
        int Lose,
        ApiGoals? Goals);

    private sealed record ApiGoals(int For, int Against);

    private sealed record FixturePayload(
        ApiFixture? Fixture,
        ApiFixtureLeague? League,
        ApiFixtureTeams? Teams,
        ApiFixtureGoals? Goals);

    private sealed record ApiFixture(int Id, DateTimeOffset? Date, ApiVenue? Venue, ApiFixtureStatus? Status);

    private sealed record ApiVenue(string? Name);

    private sealed record ApiFixtureStatus(string? Long, string? Short, int? Elapsed);

    private sealed record ApiFixtureLeague(string? Name);

    private sealed record ApiFixtureTeams(ApiTeam? Home, ApiTeam? Away);

    private sealed record ApiTeam(int? Id, string? Name, string? Logo);

    private sealed record ApiFixtureGoals(int? Home, int? Away);
}
