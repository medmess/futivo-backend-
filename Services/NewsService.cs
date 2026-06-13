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
            "approved",
            request.PublishedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        return repository.CreateAsync(post);
    }

    public Task<IReadOnlyList<NewsPost>> GetLatestAsync(int limit)
    {
        return repository.GetLatestAsync(Math.Clamp(limit, 1, 50));
    }

    public Task<bool> DeleteByTelegramPostIdAsync(long telegramPostId)
    {
        return repository.DeleteByTelegramPostIdAsync(telegramPostId);
    }
}

public interface INewsRepository
{
    Task<NewsPost?> FindByTelegramPostIdAsync(long telegramPostId);
    Task<NewsPost> CreateAsync(NewsPost post);
    Task<IReadOnlyList<NewsPost>> GetLatestAsync(int limit);
    Task<NewsPost?> UpdateImageUrlAsync(long telegramPostId, string imageUrl, string imagePath);
    Task<bool> DeleteByTelegramPostIdAsync(long telegramPostId);
}
