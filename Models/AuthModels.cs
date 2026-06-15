namespace GfnTvBackend.Models;

public sealed record TestRegisterRequest(
    string FullName,
    string Nickname,
    string Email,
    string Password);

public sealed record TestRegisterResponse(
    string UserId,
    string Email,
    string Nickname);
