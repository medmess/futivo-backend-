using System.Net.Http.Headers;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseAuthService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    IWebHostEnvironment environment)
{
    private readonly SupabaseOptions _options = options.Value;

    public async Task<AuthenticatedUser?> GetUserAsync(HttpContext context)
    {
        var bearer = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(bearer) ||
            !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return environment.IsDevelopment() && !_options.IsConfigured
                ? new AuthenticatedUser("dev-user", "dev@futivo.local")
                : null;
        }

        if (!_options.IsConfigured)
        {
            return environment.IsDevelopment()
                ? new AuthenticatedUser("dev-user", "dev@futivo.local")
                : null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.Url!.TrimEnd('/')}/auth/v1/user");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearer);
        request.Headers.Add("apikey", _options.AnonKey ?? _options.ServiceRoleKey);

        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var id = root.GetProperty("id").GetString();
        var email = root.TryGetProperty("email", out var emailElement)
            ? emailElement.GetString()
            : null;

        return string.IsNullOrWhiteSpace(id) ? null : new AuthenticatedUser(id, email);
    }
}
