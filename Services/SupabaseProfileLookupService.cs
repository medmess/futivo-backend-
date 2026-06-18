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

    public async Task<IReadOnlyList<string>> ResolveLoginEmailsAsync(string login)
    {
        if (!_options.IsConfigured)
        {
            return [];
        }

        var cleaned = login.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return [];
        }

        var escaped = Uri.EscapeDataString(cleaned);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"profiles?select=email,auth_email&or=(email.eq.{escaped},auth_email.eq.{escaped},nickname.eq.{escaped})&limit=3");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement
            .EnumerateArray()
            .Select(row =>
            {
                var authEmail = row.TryGetProperty("auth_email", out var authEmailElement)
                    ? authEmailElement.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(authEmail))
                {
                    return authEmail;
                }

                return row.TryGetProperty("email", out var emailElement)
                    ? emailElement.GetString()
                    : null;
            })
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> ResolveLoginEmailAsync(string login)
    {
        return (await ResolveLoginEmailsAsync(login)).FirstOrDefault();
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
