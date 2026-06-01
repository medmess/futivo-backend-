using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class SportsDbService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, LeagueConfig> LeagueConfigs =
        new Dictionary<string, LeagueConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["algeria-ligue-1"] = new("algeria-ligue-1", "Algerian Ligue 1", "Algeria", null, ["Algerian Ligue Professionnelle 1", "Ligue 1 Mobilis", "Algerian Ligue 1"]),
            ["ligue1-mobilis"] = new("ligue1-mobilis", "Ligue 1 Mobilis", "Algeria", null, ["Ligue 1 Mobilis", "Algerian Ligue Professionnelle 1", "Algerian Ligue 1"]),
            ["premier-league"] = new("premier-league", "English Premier League", "England", "4328", ["English Premier League", "Premier League"]),
            ["bundesliga"] = new("bundesliga", "German Bundesliga", "Germany", "4331", ["German Bundesliga", "Bundesliga"]),
            ["ligue-1"] = new("ligue-1", "French Ligue 1", "France", "4334", ["French Ligue 1", "Ligue 1"]),
            ["serie-a"] = new("serie-a", "Italian Serie A", "Italy", "4332", ["Italian Serie A", "Serie A"]),
            ["la-liga"] = new("la-liga", "Spanish La Liga", "Spain", "4335", ["Spanish La Liga", "La Liga"])
        };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();

    public SportsDbService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public IReadOnlyList<LeagueDto> GetLeagues()
    {
        return LeagueConfigs.Values
            .Where(league => league.Key != "ligue1-mobilis")
            .Select(league => new LeagueDto(league.Key, league.Name, league.Country, league.LeagueId))
            .ToList();
    }

    public async Task<IReadOnlyList<StandingRowDto>> GetStandingsAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"standings:{config.Key}", TimeSpan.FromMinutes(30), async () =>
        {
            var leagueId = await ResolveLeagueIdAsync(config);
            if (string.IsNullOrWhiteSpace(leagueId))
            {
                return IsAlgerianLeague(config) ? FallbackAlgerianStandings() : [];
            }

            var json = await GetJsonAsync($"lookuptable.php?l={Uri.EscapeDataString(leagueId)}");
            var root = JsonSerializer.Deserialize<TableResponse>(json, JsonOptions);
            var rows = root?.Table?
                .Select((row, index) => MapStanding(row, index + 1))
                .Where(row => !string.IsNullOrWhiteSpace(row.TeamName))
                .OrderBy(row => row.Position)
                .ToList() ?? [];

            return rows.Count > 0
                ? rows
                : IsAlgerianLeague(config) ? FallbackAlgerianStandings() : [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetUpcomingFixturesAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"fixtures:upcoming:{config.Key}", TimeSpan.FromMinutes(20), async () =>
        {
            var leagueId = await ResolveLeagueIdAsync(config);
            if (string.IsNullOrWhiteSpace(leagueId)) return [];

            var json = await GetJsonAsync($"eventsnextleague.php?id={Uri.EscapeDataString(leagueId)}");
            var root = JsonSerializer.Deserialize<EventsResponse>(json, JsonOptions);
            return root?.Events?.Select(MapFixture).Where(HasTeams).Take(12).ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetTodayFixturesAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"fixtures:today:{config.Key}:{DateTime.UtcNow:yyyy-MM-dd}", TimeSpan.FromMinutes(10), async () =>
        {
            var leagueId = await ResolveLeagueIdAsync(config);
            if (string.IsNullOrWhiteSpace(leagueId)) return [];

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var json = await GetJsonAsync($"eventsday.php?d={date}&l={Uri.EscapeDataString(leagueId)}");
            var root = JsonSerializer.Deserialize<EventsResponse>(json, JsonOptions);
            return root?.Events?.Select(MapFixture).Where(HasTeams).ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<FixtureDto>> GetLiveOrLatestFixturesAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"fixtures:latest:{config.Key}", TimeSpan.FromMinutes(15), async () =>
        {
            var leagueId = await ResolveLeagueIdAsync(config);
            if (string.IsNullOrWhiteSpace(leagueId)) return [];

            var json = await GetJsonAsync($"eventspastleague.php?id={Uri.EscapeDataString(leagueId)}");
            var root = JsonSerializer.Deserialize<EventsResponse>(json, JsonOptions);
            return root?.Events?.Select(MapFixture).Where(HasTeams).Take(10).ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<TeamDto>> GetTeamsAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"teams:{config.Key}", TimeSpan.FromHours(6), async () =>
        {
            var leagueId = await ResolveLeagueIdAsync(config);
            if (string.IsNullOrWhiteSpace(leagueId)) return [];

            var json = await GetJsonAsync($"lookup_all_teams.php?id={Uri.EscapeDataString(leagueId)}");
            var root = JsonSerializer.Deserialize<TeamsResponse>(json, JsonOptions);
            return root?.Teams?
                .Select(MapTeam)
                .Where(team => !string.IsNullOrWhiteSpace(team.Name))
                .ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<PlayerDto>> GetPlayersAsync(string leagueKey, int maxTeams = 24)
    {
        var config = ResolveConfig(leagueKey);
        return await GetOrCreateAsync($"players:{config.Key}:{maxTeams}", TimeSpan.FromHours(12), async () =>
        {
            var teams = await GetTeamsAsync(config.Key);
            var players = new List<PlayerDto>();

            foreach (var team in teams.Take(maxTeams))
            {
                var teamPlayers = await GetTeamPlayersAsync(team.Id, team.Name);
                players.AddRange(teamPlayers);
            }

            return players;
        });
    }

    public async Task<IReadOnlyList<PlayerDto>> GetTeamPlayersAsync(string teamId, string? teamName = null)
    {
        if (string.IsNullOrWhiteSpace(teamId)) return [];

        return await GetOrCreateAsync($"team-players:{teamId}", TimeSpan.FromHours(12), async () =>
        {
            var json = await GetJsonAsync($"lookup_all_players.php?id={Uri.EscapeDataString(teamId)}");
            var root = JsonSerializer.Deserialize<PlayersResponse>(json, JsonOptions);
            return root?.Player?
                .Select(player => MapPlayer(player, teamId, teamName))
                .Where(player => !string.IsNullOrWhiteSpace(player.Name))
                .ToList() ?? [];
        });
    }

    public async Task<EventDetailsDto?> GetEventDetailsAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;

        return await GetOrCreateAsync($"event:{eventId}", TimeSpan.FromMinutes(30), async () =>
        {
            var json = await GetJsonAsync($"lookupevent.php?id={Uri.EscapeDataString(eventId)}");
            var root = JsonSerializer.Deserialize<EventsResponse>(json, JsonOptions);
            var row = root?.Events?.FirstOrDefault();
            return row is null ? null : MapEventDetails(row);
        });
    }

    public async Task<IReadOnlyList<EventLineupDto>> GetEventLineupsAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return [];

        return await GetOrCreateAsync($"event-lineups:{eventId}", TimeSpan.FromMinutes(30), async () =>
        {
            var json = await GetJsonAsync($"lookuplineup.php?id={Uri.EscapeDataString(eventId)}");
            var root = JsonSerializer.Deserialize<LineupsResponse>(json, JsonOptions);
            return root?.Lineup?
                .Select(row => MapLineup(row, eventId))
                .Where(row => !string.IsNullOrWhiteSpace(row.PlayerName))
                .ToList() ?? [];
        });
    }

    public async Task<IReadOnlyList<EventTimelineDto>> GetEventTimelineAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return [];

        return await GetOrCreateAsync($"event-timeline:{eventId}", TimeSpan.FromMinutes(30), async () =>
        {
            var json = await GetJsonAsync($"lookuptimeline.php?id={Uri.EscapeDataString(eventId)}");
            var root = JsonSerializer.Deserialize<TimelineResponse>(json, JsonOptions);
            return root?.Timeline?
                .Select(row => MapTimeline(row, eventId))
                .Where(row => !string.IsNullOrWhiteSpace(row.Type))
                .ToList() ?? [];
        });
    }

    public async Task<LeagueDataBundleDto> GetLeagueDataBundleAsync(string leagueKey)
    {
        var config = ResolveConfig(leagueKey);
        var teams = await GetTeamsAsync(config.Key);
        var upcoming = await GetUpcomingFixturesAsync(config.Key);
        var latest = await GetLiveOrLatestFixturesAsync(config.Key);

        return new LeagueDataBundleDto(
            new LeagueDto(config.Key, config.Name, config.Country, await ResolveLeagueIdAsync(config)),
            teams,
            upcoming,
            latest);
    }

    private LeagueConfig ResolveConfig(string leagueKey)
    {
        if (LeagueConfigs.TryGetValue(leagueKey, out var config)) return config;
        throw new KeyNotFoundException($"Unknown league key: {leagueKey}");
    }

    private async Task<string?> ResolveLeagueIdAsync(LeagueConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.LeagueId)) return config.LeagueId;

        return await GetOrCreateAsync($"league-id:{config.Key}", TimeSpan.FromHours(12), async () =>
        {
            foreach (var name in config.SearchNames)
            {
                var id = await SearchLeagueInCountryAsync(config.Country, name);
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }

            return null;
        });
    }

    private async Task<string?> SearchLeagueInCountryAsync(string country, string leagueName)
    {
        var json = await GetJsonAsync($"search_all_leagues.php?c={Uri.EscapeDataString(country)}&s=Soccer");
        var root = JsonSerializer.Deserialize<LeaguesResponse>(json, JsonOptions);
        var leagues = root?.Countries ?? [];

        return leagues
            .Where(league => string.Equals(league.StrSport, "Soccer", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(league => ScoreLeagueName(league.StrLeague, leagueName))
            .FirstOrDefault(league => ScoreLeagueName(league.StrLeague, leagueName) > 0)
            ?.IdLeague;
    }

    private static int ScoreLeagueName(string? candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return 0;
        var normalizedCandidate = Normalize(candidate);
        var normalizedExpected = Normalize(expected);
        if (normalizedCandidate == normalizedExpected) return 100;
        if (normalizedCandidate.Contains(normalizedExpected) || normalizedExpected.Contains(normalizedCandidate)) return 75;
        if (expected.Contains("Mobilis", StringComparison.OrdinalIgnoreCase) &&
            normalizedCandidate.Contains("alger")) return 55;
        return 0;
    }

    private static string Normalize(string value)
    {
        return value
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private async Task<string> GetJsonAsync(string path)
    {
        var apiKey = _configuration["TheSportsDb:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = "123";

        var response = await _httpClient.GetAsync($"https://www.thesportsdb.com/api/v1/json/{apiKey}/{path}");
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

    private static StandingRowDto MapStanding(TableRow row, int fallbackPosition)
    {
        var played = ParseInt(row.IntPlayed);
        var wins = ParseInt(row.IntWin);
        var draws = ParseInt(row.IntDraw);
        var losses = ParseInt(row.IntLoss);
        var goalsFor = ParseInt(row.IntGoalsFor);
        var goalsAgainst = ParseInt(row.IntGoalsAgainst);
        var goalDifference = ParseInt(row.IntGoalDifference);

        if (goalDifference == 0 && (goalsFor != 0 || goalsAgainst != 0))
        {
            goalDifference = goalsFor - goalsAgainst;
        }

        return new StandingRowDto(
            ParseInt(row.IntRank, fallbackPosition),
            row.StrTeam ?? "Unknown team",
            row.StrTeamBadge,
            played,
            wins,
            draws,
            losses,
            goalsFor,
            goalsAgainst,
            goalDifference,
            ParseInt(row.IntPoints));
    }

    private static FixtureDto MapFixture(EventRow row)
    {
        var kickoff = ParseKickoff(row.DateEvent, row.StrTime);
        var homeScore = ParseNullableInt(row.IntHomeScore);
        var awayScore = ParseNullableInt(row.IntAwayScore);
        var status = homeScore.HasValue || awayScore.HasValue
            ? "Finished"
            : kickoff.HasValue && kickoff.Value <= DateTimeOffset.UtcNow
                ? "Not live verified"
                : "Upcoming";

        return new FixtureDto(
            row.IdEvent ?? Guid.NewGuid().ToString("N"),
            row.StrLeague ?? "Ligue 1 Mobilis",
            row.StrHomeTeam ?? "",
            row.StrAwayTeam ?? "",
            row.StrHomeTeamBadge,
            row.StrAwayTeamBadge,
            kickoff,
            status,
            homeScore,
            awayScore,
            row.StrVenue,
            false);
    }

    private static TeamDto MapTeam(TeamRow row)
    {
        return new TeamDto(
            row.IdTeam ?? "",
            row.StrTeam ?? "",
            row.StrTeamShort,
            row.StrBadge,
            row.StrLogo,
            row.StrStadium,
            row.StrCountry,
            row.StrWebsite,
            row.StrDescriptionEN);
    }

    private static PlayerDto MapPlayer(PlayerRow row, string fallbackTeamId, string? fallbackTeamName)
    {
        return new PlayerDto(
            row.IdPlayer ?? "",
            row.IdTeam ?? fallbackTeamId,
            row.StrTeam ?? fallbackTeamName ?? "",
            row.StrPlayer ?? "",
            row.StrPosition,
            row.StrNationality,
            row.StrRender,
            row.StrCutout,
            row.StrThumb,
            row.DateBorn);
    }

    private static EventDetailsDto MapEventDetails(EventRow row)
    {
        var fixture = MapFixture(row);
        return new EventDetailsDto(
            fixture.Id,
            fixture.LeagueName,
            row.StrSeason ?? "",
            fixture.HomeTeam,
            fixture.AwayTeam,
            fixture.HomeBadge,
            fixture.AwayBadge,
            fixture.HomeScore,
            fixture.AwayScore,
            fixture.Kickoff,
            fixture.Venue,
            fixture.Status,
            fixture.LiveVerified);
    }

    private static EventLineupDto MapLineup(LineupRow row, string eventId)
    {
        return new EventLineupDto(
            row.IdLineup ?? Guid.NewGuid().ToString("N"),
            eventId,
            row.StrTeam ?? "",
            row.StrPlayer ?? "",
            row.StrPosition,
            row.StrFormation,
            !string.Equals(row.StrSubstitute, "Yes", StringComparison.OrdinalIgnoreCase));
    }

    private static EventTimelineDto MapTimeline(TimelineRow row, string eventId)
    {
        return new EventTimelineDto(
            row.IdTimeline ?? Guid.NewGuid().ToString("N"),
            eventId,
            row.StrTeam ?? "",
            row.StrPlayer ?? "",
            row.StrAssist,
            row.StrTimeline ?? row.StrEvent ?? "",
            ParseNullableInt(row.IntTime),
            row.StrDetail);
    }

    private static DateTimeOffset? ParseKickoff(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var value = $"{date} {(string.IsNullOrWhiteSpace(time) ? "00:00:00" : time)}";
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static bool HasTeams(FixtureDto fixture)
    {
        return !string.IsNullOrWhiteSpace(fixture.HomeTeam) && !string.IsNullOrWhiteSpace(fixture.AwayTeam);
    }

    private static bool IsAlgerianLeague(LeagueConfig config)
    {
        return config.Key is "algeria-ligue-1" or "ligue1-mobilis";
    }

    private static int ParseInt(string? value, int fallback = 0)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<StandingRowDto> FallbackAlgerianStandings()
    {
        return
        [
            new(1, "MC Alger", null, 0, 0, 0, 0, 0, 0, 0, 0),
            new(2, "CR Belouizdad", null, 0, 0, 0, 0, 0, 0, 0, 0),
            new(3, "USM Alger", null, 0, 0, 0, 0, 0, 0, 0, 0),
            new(4, "JS Kabylie", null, 0, 0, 0, 0, 0, 0, 0, 0)
        ];
    }

    private sealed record LeagueConfig(string Key, string Name, string Country, string? LeagueId, string[] SearchNames);

    private sealed record CacheItem(object? Value, DateTimeOffset ExpiresAt);

    private sealed record LeaguesResponse(List<LeagueRow>? Countries);

    private sealed record LeagueRow(string? IdLeague, string? StrLeague, string? StrSport);

    private sealed record TableResponse(List<TableRow>? Table);

    private sealed record TableRow(
        string? IntRank,
        string? StrTeam,
        string? StrTeamBadge,
        string? IntPlayed,
        string? IntWin,
        string? IntDraw,
        string? IntLoss,
        string? IntGoalsFor,
        string? IntGoalsAgainst,
        string? IntGoalDifference,
        string? IntPoints);

    private sealed record EventsResponse(List<EventRow>? Events);

    private sealed record EventRow(
        string? IdEvent,
        string? StrLeague,
        string? StrSeason,
        string? StrHomeTeam,
        string? StrAwayTeam,
        string? StrHomeTeamBadge,
        string? StrAwayTeamBadge,
        string? IntHomeScore,
        string? IntAwayScore,
        string? DateEvent,
        string? StrTime,
        string? StrVenue);

    private sealed record TeamsResponse(List<TeamRow>? Teams);

    private sealed record TeamRow(
        string? IdTeam,
        string? StrTeam,
        string? StrTeamShort,
        string? StrBadge,
        string? StrLogo,
        string? StrStadium,
        string? StrCountry,
        string? StrWebsite,
        string? StrDescriptionEN);

    private sealed record PlayersResponse(List<PlayerRow>? Player);

    private sealed record PlayerRow(
        string? IdPlayer,
        string? IdTeam,
        string? StrTeam,
        string? StrPlayer,
        string? StrPosition,
        string? StrNationality,
        string? StrRender,
        string? StrCutout,
        string? StrThumb,
        string? DateBorn);

    private sealed record LineupsResponse(List<LineupRow>? Lineup);

    private sealed record LineupRow(
        string? IdLineup,
        string? StrTeam,
        string? StrPlayer,
        string? StrPosition,
        string? StrFormation,
        string? StrSubstitute);

    private sealed record TimelineResponse(List<TimelineRow>? Timeline);

    private sealed record TimelineRow(
        string? IdTimeline,
        string? StrTeam,
        string? StrPlayer,
        string? StrAssist,
        string? StrTimeline,
        string? StrEvent,
        string? IntTime,
        string? StrDetail);
}
