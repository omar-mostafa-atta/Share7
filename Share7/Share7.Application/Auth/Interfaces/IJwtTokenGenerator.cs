namespace Share7.Application.Auth.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(
        Guid userId,
        string username,
        string? email,
        IEnumerable<string> roles,
        Guid? preferredLanguageId = null);

    string GenerateRefreshToken();
}
