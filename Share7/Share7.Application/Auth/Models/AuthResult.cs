namespace Share7.Application.Auth.Models;

public class AuthResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public Guid UserId { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public bool IsProfileComplete { get; init; }

    public string? AccessToken { get; init; }
    public DateTime? AccessTokenExpiresAt { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? RefreshTokenExpiresAt { get; init; }

    public static AuthResult Failure(params string[] errors) => new()
    {
        Succeeded = false,
        Errors = errors
    };

    public static AuthResult Success(
        Guid userId,
        string username,
        string? email,
        IReadOnlyList<string> roles,
        bool isProfileComplete,
        string accessToken,
        DateTime accessTokenExpiresAt,
        string refreshToken,
        DateTime refreshTokenExpiresAt) => new()
    {
        Succeeded = true,
        UserId = userId,
        Username = username,
        Email = email,
        Roles = roles,
        IsProfileComplete = isProfileComplete,
        AccessToken = accessToken,
        AccessTokenExpiresAt = accessTokenExpiresAt,
        RefreshToken = refreshToken,
        RefreshTokenExpiresAt = refreshTokenExpiresAt
    };
}
