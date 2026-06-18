using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GfnTvBackend.Models;
using Microsoft.Extensions.Options;

namespace GfnTvBackend.Services;

public sealed class SupabaseTestAuthService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options)
{
    private readonly SupabaseOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TestRegisterResponse> RegisterWithoutEmailConfirmationAsync(
        TestRegisterRequest request)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Supabase is not configured.");
        }

        var fullName = request.FullName.Trim();
        var nickname = request.Nickname.Trim().ToLowerInvariant();
        var email = request.Email.Trim().ToLowerInvariant();
        await EnsureAuthEmailColumnAsync();

        if (await IsNicknameTakenAsync(nickname))
        {
            throw new InvalidOperationException("nickname already exists.");
        }

        var existingEmailProfiles = await LookupProfilesAsync($"email=eq.{Uri.EscapeDataString(email)}", 3);
        if (existingEmailProfiles.Count >= 3)
        {
            throw new InvalidOperationException("email account limit reached.");
        }

        var authEmail = BuildAuthEmail(email, nickname, existingEmailProfiles.Count + 1);

        string userId;
        try
        {
            userId = await CreateConfirmedUserAsync(
                authEmail,
                request.Password,
                fullName,
                nickname,
                email);
        }
        catch (InvalidOperationException exception) when (
            IsDuplicateEmailError(exception.Message))
        {
            authEmail = BuildAuthEmail(email, nickname, existingEmailProfiles.Count + 2);
            userId = await CreateConfirmedUserAsync(
                authEmail,
                request.Password,
                fullName,
                nickname,
                email);
        }

        await UpsertProfileAsync(userId, fullName, nickname, email, authEmail);

        return new TestRegisterResponse(userId, email, authEmail, nickname);
    }

    private async Task<string> CreateConfirmedUserAsync(
        string email,
        string password,
        string fullName,
        string nickname,
        string publicEmail)
    {
        using var request = CreateAuthRequest(HttpMethod.Post, "admin/users");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                email,
                password,
                email_confirm = true,
                user_metadata = new
                {
                    full_name = fullName,
                    nickname,
                    email = publicEmail
                }
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(content);
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Supabase did not return a user id.");
    }

    private async Task UpsertProfileAsync(
        string userId,
        string fullName,
        string nickname,
        string email,
        string authEmail)
    {
        using var request = CreateRestRequest(HttpMethod.Post, "profiles");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                id = userId,
                full_name = fullName,
                nickname,
                email,
                auth_email = authEmail,
                phone = (string?)null,
                role = "user",
                favorite_team = (string?)null
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "resolution=merge-duplicates,return=minimal");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureAuthEmailColumnAsync()
    {
        using var request = CreateRestRequest(HttpMethod.Get, "profiles?select=id,auth_email&limit=1");
        using var response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync();
        if (content.Contains("auth_email", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("schema cache", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Supabase profiles.auth_email column is required. Run supabase/auth_testing_aliases.sql before creating accounts.");
        }

        throw new InvalidOperationException(content);
    }

    private async Task<List<JsonElement>> LookupProfilesAsync(string filter, int limit)
    {
        using var request = CreateRestRequest(
            HttpMethod.Get,
            $"profiles?select=id,email,auth_email,nickname&{filter}&limit={limit}");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToList();
    }

    public async Task<bool> IsNicknameTakenAsync(string nickname)
    {
        if (!_options.IsConfigured)
        {
            return false;
        }

        var cleaned = nickname.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        return (await LookupProfilesAsync(
            $"nickname=eq.{Uri.EscapeDataString(cleaned)}",
            1)).Count > 0;
    }

    private static string BuildAuthEmail(string email, string nickname, int accountNumber)
    {
        var parts = email.Split('@', 2);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("email invalid.");
        }

        var local = SanitizeEmailPart(parts[0]);
        var nick = SanitizeEmailPart(nickname);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{local}.{nick}.futivo{accountNumber}.{suffix}@auth.futivo.app";
    }

    private static string SanitizeEmailPart(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? "user" : builder.ToString();
    }

    private static bool IsDuplicateEmailError(string message)
    {
        var value = message.ToLowerInvariant();
        return value.Contains("already") ||
               value.Contains("registered") ||
               value.Contains("exists") ||
               value.Contains("duplicate");
    }

    private HttpRequestMessage CreateAuthRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(
            method,
            $"{_options.Url!.TrimEnd('/')}/auth/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.ServiceRoleKey);
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        return request;
    }

    private HttpRequestMessage CreateRestRequest(HttpMethod method, string path)
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
