namespace GfnTvBackend.Models;

public sealed record TestRegisterRequest(
    string FullName,
    string Nickname,
    string Email,
    string Password);

public sealed record TestRegisterResponse(
    string UserId,
    string Email,
    string AuthEmail,
    string Nickname);

public sealed record ResolveLoginResponse(
    string? Email,
    IReadOnlyList<string> AuthEmails);
