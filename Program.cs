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
builder.Services.AddHttpClient<SupabaseMatchPredictionRepository>();
builder.Services.AddHttpClient<SupabaseStorageService>();
builder.Services.AddHttpClient<SupabasePushNotificationRepository>();
builder.Services.AddHttpClient<SupabaseProfileLookupService>();
builder.Services.AddSingleton<InMemoryGroupRepository>();
builder.Services.AddSingleton<InMemoryNewsRepository>();
builder.Services.AddSingleton<InMemoryNewsAdRepository>();
builder.Services.AddSingleton<InMemoryManualMatchRepository>();
builder.Services.AddSingleton<InMemoryMatchPredictionRepository>();
builder.Services.AddSingleton<InMemoryPushNotificationRepository>();
builder.Services.AddSingleton<IFirebasePushSender, FirebasePushSender>();
builder.Services.AddSingleton<GroupCodeGenerator>();
builder.Services.AddScoped<FantasyScoringService>();
builder.Services.AddScoped<StandingsService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<NewsService>();
builder.Services.AddScoped<NewsAdService>();
builder.Services.AddScoped<ManualMatchService>();
builder.Services.AddScoped<MatchPredictionService>();
builder.Services.AddScoped<PushNotificationService>();
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
builder.Services.AddScoped<IMatchPredictionRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabaseMatchPredictionRepository>()
        : provider.GetRequiredService<InMemoryMatchPredictionRepository>();
});
builder.Services.AddScoped<IPushNotificationRepository>(provider =>
{
    var options = provider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>()
        .Value;

    return options.IsConfigured
        ? provider.GetRequiredService<SupabasePushNotificationRepository>()
        : provider.GetRequiredService<InMemoryPushNotificationRepository>();
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

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Futivo backend",
    mode = app.Configuration.GetSection("Supabase").Get<SupabaseOptions>()?.IsConfigured == true
        ? "supabase"
        : "memory"
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

app.MapGet("/api/news/latest", async (int? limit, string? language, NewsService news, HttpRequest request) =>
{
    var posts = await news.GetLatestAsync(limit ?? 30, language);
    var forwardedHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
    var forwardedProto = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
    var publicBaseUrl = request.Headers["X-Public-Base-Url"].FirstOrDefault();
    var baseUrl = !string.IsNullOrWhiteSpace(publicBaseUrl)
        ? publicBaseUrl.TrimEnd('/')
        : $"{(string.IsNullOrWhiteSpace(forwardedProto) ? request.Scheme : forwardedProto)}://{(string.IsNullOrWhiteSpace(forwardedHost) ? request.Host : forwardedHost)}";
    return Results.Ok(posts.Select(post => NewsPostResponse.From(post, baseUrl)));
});

app.MapGet("/api/auth/resolve-login", async (
    string? login,
    SupabaseProfileLookupService profiles) =>
{
    if (string.IsNullOrWhiteSpace(login))
    {
        return Results.BadRequest(new { message = "login is required." });
    }

    var email = await profiles.ResolveLoginEmailAsync(login);
    return string.IsNullOrWhiteSpace(email)
        ? Results.NotFound(new { message = "account not found." })
        : Results.Ok(new { email });
});

app.MapGet("/api/ads/news", async (NewsAdService ads) =>
{
    var activeAds = await ads.GetActiveAsync();
    return Results.Ok(activeAds.Select(NewsAdResponse.From));
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
    HttpContext httpContext,
    ManualMatchService matches,
    PushNotificationService pushNotifications,
    IConfiguration configuration) =>
{
    if (!IsAdminRequestAuthorized(httpContext, configuration))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.MatchId) ||
        string.IsNullOrWhiteSpace(request.HomeTeam) ||
        string.IsNullOrWhiteSpace(request.AwayTeam))
    {
        return Results.BadRequest(new { message = "matchId, homeTeam and awayTeam are required." });
    }

    var previous = await matches.GetAsync(request.MatchId);
    var details = await matches.UpsertAsync(request);
    if (previous is not null)
    {
        await pushNotifications.SendMatchEventAsync(previous, details);
    }

    return Results.Ok(ManualMatchDetailsResponse.From(details));
});

app.MapPost("/api/push-tokens", async (
    PushTokenRequest request,
    HttpContext httpContext,
    SupabaseAuthService auth,
    PushNotificationService pushNotifications) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    try
    {
        await pushNotifications.RegisterTokenAsync(user, request);
        return Results.NoContent();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
});

app.MapPost("/api/admin/notifications/send", async (
    AdminNotificationRequest request,
    HttpContext httpContext,
    PushNotificationService pushNotifications,
    IConfiguration configuration) =>
{
    if (!IsAdminRequestAuthorized(httpContext, configuration))
    {
        return Results.Unauthorized();
    }

    try
    {
        var result = await pushNotifications.SendAdminAsync(request);
        return Results.Ok(result);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
});

app.MapGet("/api/predictions/mine", async (
    HttpContext httpContext,
    SupabaseAuthService auth,
    MatchPredictionService predictions) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    var mine = await predictions.GetMineAsync(user);
    return Results.Ok(mine.Select(MatchPredictionResponse.From));
});

app.MapGet("/api/matches/{matchId}/prediction", async (
    string matchId,
    HttpContext httpContext,
    SupabaseAuthService auth,
    MatchPredictionService predictions) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    var prediction = await predictions.GetAsync(user, matchId);
    return prediction is null
        ? Results.NotFound(new { message = "Prediction not found." })
        : Results.Ok(MatchPredictionResponse.From(prediction));
});

app.MapPost("/api/matches/{matchId}/prediction", async (
    string matchId,
    MatchPredictionRequest request,
    HttpContext httpContext,
    SupabaseAuthService auth,
    MatchPredictionService predictions) =>
{
    var user = await auth.GetUserAsync(httpContext);
    if (user is null) return Results.Unauthorized();

    try
    {
        var prediction = await predictions.UpsertAsync(user, matchId, request);
        return Results.Ok(MatchPredictionResponse.From(prediction));
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
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

static bool IsAdminRequestAuthorized(HttpContext httpContext, IConfiguration configuration)
{
    var configuredKey = configuration["AdminApiKey"];
    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        return true;
    }

    var providedKey = httpContext.Request.Headers["X-Admin-Api-Key"].FirstOrDefault();
    return string.Equals(configuredKey, providedKey, StringComparison.Ordinal);
}

app.Run();

sealed record NewsPostResponse(
    string Id,
    long TelegramPostId,
    string Caption,
    string ImagePath,
    string ImageUrl,
    string Source,
    string Language,
    string ModerationStatus,
    DateTimeOffset PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt)
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
            post.Language,
            post.ModerationStatus,
            post.PublishedAt,
            post.CreatedAt,
            post.ReviewedAt);
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
    string? LiveStreamUrl,
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
            details.LiveStreamUrl,
            details.HomeLineup,
            details.AwayLineup,
            details.Events,
            details.UpdatedAt);
    }
}

sealed record MatchPredictionResponse(
    string Id,
    string UserId,
    string MatchId,
    string HomeTeam,
    string AwayTeam,
    int HomeScore,
    int AwayScore,
    DateTimeOffset? Kickoff,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static MatchPredictionResponse From(MatchPrediction prediction)
    {
        return new MatchPredictionResponse(
            prediction.Id,
            prediction.UserId,
            prediction.MatchId,
            prediction.HomeTeam,
            prediction.AwayTeam,
            prediction.HomeScore,
            prediction.AwayScore,
            prediction.Kickoff,
            prediction.CreatedAt,
            prediction.UpdatedAt);
    }
}
