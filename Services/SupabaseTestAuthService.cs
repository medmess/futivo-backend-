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

        var userId = await CreateConfirmedUserAsync(
            email,
            request.Password,
            fullName,
            nickname);

        await UpsertProfileAsync(userId, fullName, nickname, email);

        return new TestRegisterResponse(userId, email, nickname);
    }

    private async Task<string> CreateConfirmedUserAsync(
        string email,
        string password,
        string fullName,
        string nickname)
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
                    email
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
        string email)
    {
        using var request = CreateRestRequest(HttpMethod.Post, "profiles");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                id = userId,
                full_name = fullName,
                nickname,
                email,
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
