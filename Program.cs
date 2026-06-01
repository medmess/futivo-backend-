using GfnTvBackend.Models;
using GfnTvBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("FlutterDev", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

builder.Services.Configure<SupabaseOptions>(
    builder.Configuration.GetSection("Supabase"));
builder.Services.AddHttpClient<SupabaseAuthService>();
builder.Services.AddHttpClient<SupabaseGroupRepository>();
builder.Services.AddHttpClient<SupabaseNewsRepository>();
builder.Services.AddHttpClient<SupabaseNewsAdRepository>();
builder.Services.AddHttpClient<SupabaseManualMatchRepository>();
builder.Services.AddHttpClient<SupabaseStorageService>();
builder.Services.AddHttpClient<SportsDbService>();
builder.Services.AddHttpClient<ApiFootballService>();
builder.Services.AddSingleton<InMemoryGroupRepository>();
builder.Services.AddSingleton<InMemoryNewsRepository>();
builder.Services.AddSingleton<InMemoryNewsAdRepository>();
builder.Services.AddSingleton<InMemoryManualMatchRepository>();
builder.Services.AddSingleton<GroupCodeGenerator>();
builder.Services.AddScoped<FantasyScoringService>();
builder.Services.AddScoped<StandingsService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<NewsService>();
builder.Services.AddScoped<NewsAdService>();
builder.Services.AddScoped<ManualMatchService>();
builder.Services.AddScoped<IGroupRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabaseGroupRepository>()
        : provider.GetRequiredService<InMemoryGroupRepository>();
});
builder.Services.AddScoped<INewsRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabaseNewsRepository>()
        : provider.GetRequiredService<InMemoryNewsRepository>();
});
builder.Services.AddScoped<INewsAdRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabaseNewsAdRepository>()
        : provider.GetRequiredService<InMemoryNewsAdRepository>();
});
builder.Services.AddScoped<IManualMatchRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabaseManualMatchRepository>()
        : provider.GetRequiredService<InMemoryManualMatchRepository>();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("FlutterDev");
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (ApiFootballService apiFootball) => Results.Ok(new
{
    status = "ok",
    service = "Futivo backend",
    mode = app.Configuration.GetSection("Supabase").Get<SupabaseOptions>()?.IsConfigured == true
        ? "supabase"
        : "memory",
    apiFootballConfigured = apiFootball.IsConfigured
}));

app.MapPost("/api/fantasy/calculate-points",
    (FantasyRoundCalculationRequest request, FantasyScoringService scoring) =>
    {
        var result = scoring.CalculateRound(request);
        return Results.Ok(result);
    });

app.MapPost("/api/standings/calculate",
    (StandingsCalculationRequest request, StandingsService standings) =>
    {
        var result = standings.Calculate(request);
        return Results.Ok(result);
    });

app.MapPost("/api/news/telegram", async (
    TelegramNewsPostRequest request,
    NewsService news) =>
{
    if (request.TelegramPostId <= 0 ||
        string.IsNullOrWhiteSpace(request.Caption) ||
        string.IsNullOrWhiteSpace(request.ImagePath))
    {
        return Results.BadRequest(new { message = "telegramPostId, caption and imagePath are required." });
    }

    var post = await news.AddTelegramPostAsync(request);
    return Results.Ok(NewsPostResponse.From(post!));
});

app.MapPost("/api/news/admin", async (
    AdminNewsPostRequest request,
    NewsService news) =>
{
    if (string.IsNullOrWhiteSpace(request.Caption) ||
        string.IsNullOrWhiteSpace(request.ImageUrl))
    {
        return Results.BadRequest(new { message = "caption and imageUrl are required." });
    }

    var post = await news.AddAdminPostAsync(request);
    return Results.Ok(NewsPostResponse.From(post));
});

app.MapGet("/api/news/latest", async (int? limit, NewsService news, HttpRequest request) =>
{
    var posts = await news.GetLatestAsync(limit ?? 30);
    var forwardedHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
    var forwardedProto = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    var publicBaseUrl = request.Headers["X-Public-Base-Url"].FirstOrDefault();
    var baseUrl = !string.IsNullOrWhiteSpace(publicBaseUrl)
        ? publicBaseUrl.TrimEnd('/')
        : $"{(string.IsNullOrWhiteSpace(forwardedProto) ? request.Scheme : forwardedProto)}://{(string.IsNullOrWhiteSpace(forwardedHost) ? request.Host : forwardedHost)}";
    return Results.Ok(posts.Select(post => NewsPostResponse.From(post, baseUrl)));
});

app.MapGet("/api/ads/news", async (NewsAdService ads) =>
{
    var activeAds = await ads.GetActiveAsync();
    return Results.Ok(activeAds.Select(NewsAdResponse.From));
});

app.MapGet("/api/standings/leagues", (SportsDbService sportsDb) =>
{
    return Results.Ok(sportsDb.GetLeagues());
});

app.MapGet("/api/standings/{leagueKey}", async (
    string leagueKey,
    SportsDbService sportsDb,
    ApiFootballService apiFootball) =>
{
    try
    {
        var apiFootballStandings = await apiFootball.GetStandingsAsync(leagueKey);
        if (apiFootballStandings.Count > 0)
        {
            return Results.Ok(apiFootballStandings);
        }

        if (apiFootball.IsConfigured)
        {
            return Results.Ok(Array.Empty<StandingRowDto>());
        }

        return Results.Ok(Array.Empty<StandingRowDto>());
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/standings/{leagueKey}/fixtures", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetUpcomingFixturesAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/matches/ligue1-mobilis/today", async (
    SportsDbService sportsDb,
    ApiFootballService apiFootball) =>
{
    var apiFootballFixtures = await apiFootball.GetFixturesByDateAsync("ligue1-mobilis", DateTime.UtcNow);
    return Results.Ok(apiFootballFixtures.Count > 0
        ? apiFootballFixtures
        : await sportsDb.GetTodayFixturesAsync("ligue1-mobilis"));
});

app.MapGet("/api/matches/ligue1-mobilis/upcoming", async (
    SportsDbService sportsDb,
    ApiFootballService apiFootball) =>
{
    var apiFootballFixtures = await apiFootball.GetUpcomingFixturesAsync("ligue1-mobilis");
    return Results.Ok(apiFootballFixtures.Count > 0
        ? apiFootballFixtures
        : await sportsDb.GetUpcomingFixturesAsync("ligue1-mobilis"));
});

app.MapGet("/api/matches/ligue1-mobilis/live", async (
    SportsDbService sportsDb,
    ApiFootballService apiFootball) =>
{
    var liveFixtures = await apiFootball.GetLiveFixturesAsync("ligue1-mobilis");
    return Results.Ok(liveFixtures.Count > 0
        ? liveFixtures
        : await sportsDb.GetLiveOrLatestFixturesAsync("ligue1-mobilis"));
});

app.MapGet("/api/sportsdb/{leagueKey}/teams", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetTeamsAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/sportsdb/{leagueKey}/players", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetPlayersAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/sportsdb/teams/{teamId}/players", async (string teamId, SportsDbService sportsDb) =>
{
    return Results.Ok(await sportsDb.GetTeamPlayersAsync(teamId));
});

app.MapGet("/api/sportsdb/{leagueKey}/upcoming", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetUpcomingFixturesAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/sportsdb/{leagueKey}/latest-results", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetLiveOrLatestFixturesAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/sportsdb/{leagueKey}/bundle", async (string leagueKey, SportsDbService sportsDb) =>
{
    try
    {
        return Results.Ok(await sportsDb.GetLeagueDataBundleAsync(leagueKey));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { message = "Unknown league key." });
    }
});

app.MapGet("/api/sportsdb/events/{eventId}", async (string eventId, SportsDbService sportsDb) =>
{
    var details = await sportsDb.GetEventDetailsAsync(eventId);
    return details is null ? Results.NotFound(new { message = "Event not found." }) : Results.Ok(details);
});

app.MapGet("/api/sportsdb/events/{eventId}/lineups", async (string eventId, SportsDbService sportsDb) =>
{
    return Results.Ok(await sportsDb.GetEventLineupsAsync(eventId));
});

app.MapGet("/api/sportsdb/events/{eventId}/timeline", async (string eventId, SportsDbService sportsDb) =>
{
    return Results.Ok(await sportsDb.GetEventTimelineAsync(eventId));
});

app.MapPost("/api/ads/news", async (
    NewsAdRequest request,
    NewsAdService ads) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) ||
        string.IsNullOrWhiteSpace(request.ImageUrl))
    {
        return Results.BadRequest(new { message = "title and imageUrl are required." });
    }

    var ad = await ads.CreateAsync(request);
    return Results.Ok(NewsAdResponse.From(ad));
});

app.MapGet("/api/matches/{matchId}/manual", async (
    string matchId,
    ManualMatchService matches) =>
{
    var details = await matches.GetAsync(matchId);
    return details is null
        ? Results.Ok(ManualMatchDetailsResponse.Empty(matchId))
        : Results.Ok(ManualMatchDetailsResponse.From(details));
});

app.MapPost("/api/admin/matches/manual", async (
    ManualMatchDetailsRequest request,
    ManualMatchService matches) =>
{
    if (string.IsNullOrWhiteSpace(request.MatchId) ||
        string.IsNullOrWhiteSpace(request.HomeTeam) ||
        string.IsNullOrWhiteSpace(request.AwayTeam))
    {
        return Results.BadRequest(new { message = "matchId, homeTeam and awayTeam are required." });
    }

    var details = await matches.UpsertAsync(request);
    return Results.Ok(ManualMatchDetailsResponse.From(details));
});

app.MapDelete("/api/news/telegram/{telegramPostId:long}", async (
    long telegramPostId,
    NewsService news) =>
{
    var deleted = await news.DeleteByTelegramPostIdAsync(telegramPostId);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/news/image/{fileName}", (string fileName) =>
{
    var safeName = Path.GetFileName(fileName);
    var downloads = Path.Combine(@"C:\telegram-news", "downloads");
    var fullPath = Path.GetFullPath(Path.Combine(downloads, safeName));
    var downloadsRoot = Path.GetFullPath(downloads);

    if (!fullPath.StartsWith(downloadsRoot, StringComparison.OrdinalIgnoreCase) ||
        !File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, contentType: "image/jpeg");
});

app.MapPost("/api/groups/create", async (
    HttpContext httpContext,
    CreateGroupRequest request,
    SupabaseAuthService auth,
    GroupService groups) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    var group = await groups.CreateGroupAsync(user, request);
    return Results.Ok(group);
});

app.MapPost("/api/groups/join", async (
    HttpContext httpContext,
    JoinGroupRequest request,
    SupabaseAuthService auth,
    GroupService groups) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    var group = await groups.JoinGroupAsync(user, request.Code);
    return group is null
        ? Results.NotFound(new { message = "Group not found or full." })
        : Results.Ok(group);
});

app.MapGet("/api/groups/mine", async (
    HttpContext httpContext,
    SupabaseAuthService auth,
    GroupService groups) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    return Results.Ok(await groups.GetMineAsync(user));
});

app.Run();

sealed record NewsPostResponse(
    string Id,
    long TelegramPostId,
    string Caption,
    string ImagePath,
    string ImageUrl,
    string Source,
    DateTimeOffset PublishedAt,
    DateTimeOffset CreatedAt)
{
    public static NewsPostResponse From(NewsPost post, string? baseUrl = null)
    {
        var imageUrl = post.ImageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl) && !string.IsNullOrWhiteSpace(baseUrl))
        {
            var fileName = Path.GetFileName(post.ImagePath);
            imageUrl = $"{baseUrl}/api/news/image/{Uri.EscapeDataString(fileName)}";
        }

        return new NewsPostResponse(
            post.Id,
            post.TelegramPostId,
            post.Caption,
            post.ImagePath,
            imageUrl ?? post.ImagePath,
            post.Source,
            post.PublishedAt,
            post.CreatedAt);
    }
}

sealed record NewsAdResponse(
    string Id,
    string Title,
    string? Subtitle,
    string ImageUrl,
    string? TargetUrl,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public static NewsAdResponse From(NewsAd ad)
    {
        return new NewsAdResponse(
            ad.Id,
            ad.Title,
            ad.Subtitle,
            ad.ImageUrl,
            ad.TargetUrl,
            ad.IsActive,
            ad.CreatedAt);
    }
}

sealed record ManualMatchDetailsResponse(
    string MatchId,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    IReadOnlyList<MatchLineupPlayer> HomeLineup,
    IReadOnlyList<MatchLineupPlayer> AwayLineup,
    IReadOnlyList<MatchEvent> Events,
    DateTimeOffset? UpdatedAt)
{
    public static ManualMatchDetailsResponse Empty(string matchId)
    {
        return new ManualMatchDetailsResponse(
            matchId,
            "",
            "",
            null,
            null,
            [],
            [],
            [],
            null);
    }

    public static ManualMatchDetailsResponse From(ManualMatchDetails details)
    {
        return new ManualMatchDetailsResponse(
            details.MatchId,
            details.HomeTeam,
            details.AwayTeam,
            details.HomeFormation,
            details.AwayFormation,
            details.HomeLineup,
            details.AwayLineup,
            details.Events,
            details.UpdatedAt);
    }
}
