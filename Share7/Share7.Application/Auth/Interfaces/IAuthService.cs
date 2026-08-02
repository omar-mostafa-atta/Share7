using Share7.Application.Auth.Models;

namespace Share7.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResult> ExternalLoginAsync(ExternalLoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task<bool> RevokeTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task<CompleteProfileResult> CompleteProfileAsync(Guid userId, CompleteProfileRequest request, CancellationToken cancellationToken = default);
}
