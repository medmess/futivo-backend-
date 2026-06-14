using System.Net.Http.Headers;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseProfileLookupService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options)
{
    private readonly SupabaseOptions _options = options.Value;

    public async Task<string?> ResolveLoginEmailAsync(string login)
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        var cleaned = login.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var escaped = Uri.EscapeDataString(cleaned);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"profiles?select=email&or=(email.eq.{escaped},nickname.eq.{escaped})&limit=1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var first = document.RootElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return first.TryGetProperty("email", out var email)
            ? email.GetString()
            : null;
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
