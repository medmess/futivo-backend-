using GfnTvBackend.Models;

namespace GfnTvBackend.Services;

public sealed class NewsService(INewsRepository repository, SupabaseStorageService storage)
{
    public async Task<NewsPost?> AddTelegramPostAsync(TelegramNewsPostRequest request)
    {
        var existing = await repository.FindByTelegramPostIdAsync(request.TelegramPostId);
        var imageUrl = await storage.UploadNewsImageAsync(
            request.TelegramPostId,
            request.ImageBase64,
            request.ImageContentType);

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.ImageUrl) &&
                !string.IsNullOrWhiteSpace(imageUrl))
            {
                return await repository.UpdateImageUrlAsync(
                    existing.TelegramPostId,
                    imageUrl,
                    request.ImagePath.Trim());
            }

            return existing;
        }

        var post = new NewsPost(
            Guid.NewGuid().ToString("N"),
            request.TelegramPostId,
            request.Caption.Trim(),
            request.ImagePath.Trim(),
            imageUrl,
            string.IsNullOrWhiteSpace(request.Source) ? "Offside" : request.Source.Trim(),
            NormalizeLanguage(request.Language, request.Source),
            "pending",
            request.PublishedAt,
            DateTimeOffset.UtcNow,
            null);

        return await repository.CreateAsync(post);
    }

    public Task<NewsPost> AddAdminPostAsync(AdminNewsPostRequest request)
    {
        var imageUrl = request.ImageUrl.Trim();
        var post = new NewsPost(
            Guid.NewGuid().ToString("N"),
            -DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            request.Caption.Trim(),
            imageUrl,
            imageUrl,
            string.IsNullOrWhiteSpace(request.Source) ? "Futivo Admin" : request.Source.Trim(),
            NormalizeLanguage(request.Language, request.Source),
            "approved",
            request.PublishedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        return repository.CreateAsync(post);
    }

    public Task<IReadOnlyList<NewsPost>> GetLatestAsync(int limit, string? language = null)
    {
        var requestedLimit = Math.Clamp(limit, 1, 50);
        return GetLatestFilteredAsync(requestedLimit, NormalizeLanguage(language));
    }

    private async Task<IReadOnlyList<NewsPost>> GetLatestFilteredAsync(int limit, string language)
    {
        var posts = await repository.GetLatestAsync(50, language);
        return posts
            .Where(post => IsAllowedSourceForLanguage(post.Source, language))
            .Take(limit)
            .ToArray();
    }

    public Task<bool> DeleteByTelegramPostIdAsync(long telegramPostId)
    {
        return repository.DeleteByTelegramPostIdAsync(telegramPostId);
    }

    private static string NormalizeLanguage(string? language, string? source = null)
    {
        var value = language?.Trim().ToLowerInvariant();
        if (value is "fr" or "french" or "francais" or "français")
        {
            return "fr";
        }

        if (value is "ar" or "arabic" or "arabe")
        {
            return "ar";
        }

        var sourceValue = source?.Trim().ToLowerInvariant() ?? "";
        if (sourceValue.Contains("info sport") ||
            sourceValue.Contains("infosport") ||
            sourceValue.Contains("le lien"))
        {
            return "fr";
        }

        return "ar";
    }

    private static bool IsAllowedSourceForLanguage(string source, string language)
    {
        var value = source.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (language == "fr")
        {
            return value.Contains("info sportz") ||
                   value.Contains("infosportz") ||
                   value.Contains("info sports plus") ||
                   value.Contains("infosportsplus");
        }

        return value.Contains("offside") ||
               value.Contains("european sport") ||
               value.Contains("erupean sport") ||
               value.Contains("erupean_sportt");
    }
}

public interface INewsRepository
{
    Task<NewsPost?> FindByTelegramPostIdAsync(long telegramPostId);
    Task<NewsPost> CreateAsync(NewsPost post);
    Task<IReadOnlyList<NewsPost>> GetLatestAsync(int limit, string language);
    Task<NewsPost?> UpdateImageUrlAsync(long telegramPostId, string imageUrl, string imagePath);
    Task<bool> DeleteByTelegramPostIdAsync(long telegramPostId);
}
