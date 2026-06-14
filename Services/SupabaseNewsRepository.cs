using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseNewsRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : INewsRepository
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NewsPost?> FindByTelegramPostIdAsync(long telegramPostId)
    {
        var posts = await GetJsonArrayAsync(
            $"news_posts?select=*&telegram_post_id=eq.{telegramPostId}&limit=1");
        return posts.Count == 0 ? null : ParsePost(posts[0]);
    }

    public async Task<NewsPost> CreateAsync(NewsPost post)
    {
        await SendAsync(HttpMethod.Post, "news_posts", new
        {
            id = post.Id,
            telegram_post_id = post.TelegramPostId,
            caption = post.Caption,
            image_path = post.ImagePath,
            image_url = post.ImageUrl,
            source = post.Source,
            language = post.Language,
            moderation_status = post.ModerationStatus,
            published_at = post.PublishedAt,
            created_at = post.CreatedAt,
            reviewed_at = post.ReviewedAt
        });

        return post;
    }

    public async Task<IReadOnlyList<NewsPost>> GetLatestAsync(int limit, string language)
    {
        var posts = await GetJsonArrayAsync(
            $"news_posts?select=*&moderation_status=eq.approved&language=eq.{Uri.EscapeDataString(language)}&order=published_at.desc&limit={limit}");
        return posts.Select(ParsePost).ToArray();
    }

    public async Task<NewsPost?> UpdateImageUrlAsync(
        long telegramPostId,
        string imageUrl,
        string imagePath)
    {
        using var request = CreateRequest(
            HttpMethod.Patch,
            $"news_posts?telegram_post_id=eq.{telegramPostId}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                image_url = imageUrl,
                image_path = imagePath
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var posts = document.RootElement.EnumerateArray().ToArray();
        return posts.Length == 0 ? null : ParsePost(posts[0]);
    }

    public async Task<bool> DeleteByTelegramPostIdAsync(long telegramPostId)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"news_posts?telegram_post_id=eq.{telegramPostId}");
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetArrayLength() > 0;
    }

    private NewsPost ParsePost(JsonElement item)
    {
        return new NewsPost(
            item.GetProperty("id").GetString()!,
            item.GetProperty("telegram_post_id").GetInt64(),
            item.GetProperty("caption").GetString() ?? "",
            item.GetProperty("image_path").GetString() ?? "",
            item.TryGetProperty("image_url", out var imageUrl) ? imageUrl.GetString() : null,
            item.GetProperty("source").GetString() ?? "Offside",
            item.TryGetProperty("language", out var language)
                ? language.GetString() ?? "ar"
                : "ar",
            item.TryGetProperty("moderation_status", out var moderationStatus)
                ? moderationStatus.GetString() ?? "approved"
                : "approved",
            item.GetProperty("published_at").GetDateTimeOffset(),
            item.GetProperty("created_at").GetDateTimeOffset(),
            item.TryGetProperty("reviewed_at", out var reviewedAt) &&
            reviewedAt.ValueKind != JsonValueKind.Null
                ? reviewedAt.GetDateTimeOffset()
                : null);
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

    private async Task SendAsync(HttpMethod method, string path, object body)
    {
        using var request = CreateRequest(method, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
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
