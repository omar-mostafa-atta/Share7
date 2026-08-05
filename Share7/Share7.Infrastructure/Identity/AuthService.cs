using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Auth.Models;
using Share7.Domain.Constants;
using Share7.Domain.Entities;
using Share7.Infrastructure.Identity.ExternalAuth;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly JwtSettings _jwtSettings;
    private readonly IEnumerable<IExternalLoginValidator> _externalLoginValidators;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IJwtTokenGenerator jwtTokenGenerator,
        IOptions<JwtSettings> jwtOptions,
        IEnumerable<IExternalLoginValidator> externalLoginValidators)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _jwtTokenGenerator = jwtTokenGenerator;
        _jwtSettings = jwtOptions.Value;
        _externalLoginValidators = externalLoginValidators;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var existing = await _userManager.FindByNameAsync(request.Username);
        if (existing is not null)
            return AuthResult.Failure("A user with this username already exists.");

        var languageExists = await _dbContext.Languages
            .AnyAsync(l => l.Id == request.LanguageId, cancellationToken);
        if (!languageExists)
            return AuthResult.Failure("Invalid language.");

        var user = new ApplicationUser
        {
            UserName = request.Username,
            PreferredLanguageId = request.LanguageId
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return AuthResult.Failure(createResult.Errors.Select(e => e.Description).ToArray());

        await _userManager.AddToRoleAsync(user, Roles.Student);

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null)
            return AuthResult.Failure("Invalid username or password.");

        if (await _userManager.IsLockedOutAsync(user))
            return AuthResult.Failure("This account is locked out. Try again later.");

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return AuthResult.Failure("Invalid username or password.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> ExternalLoginAsync(ExternalLoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var validator = _externalLoginValidators.FirstOrDefault(
            v => string.Equals(v.Provider, request.Provider, StringComparison.OrdinalIgnoreCase));

        if (validator is null)
            return AuthResult.Failure($"Unsupported external provider '{request.Provider}'.");

        var externalUser = await validator.ValidateAsync(request.Token, cancellationToken);
        if (externalUser is null)
            return AuthResult.Failure("The external login token could not be verified.");

        var user = await _userManager.FindByLoginAsync(validator.Provider, externalUser.ProviderKey);

        if (user is null)
        {
            user = await _userManager.FindByEmailAsync(externalUser.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = externalUser.Email,
                    Email = externalUser.Email,
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return AuthResult.Failure(createResult.Errors.Select(e => e.Description).ToArray());

                await _userManager.AddToRoleAsync(user, Roles.Student);
            }

            var loginInfo = new UserLoginInfo(validator.Provider, externalUser.ProviderKey, validator.Provider);
            var addLoginResult = await _userManager.AddLoginAsync(user, loginInfo);
            if (!addLoginResult.Succeeded)
                return AuthResult.Failure(addLoginResult.Errors.Select(e => e.Description).ToArray());
        }

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var storedToken = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.Token == refreshToken, cancellationToken);

        if (storedToken is null || !storedToken.IsActive)
            return AuthResult.Failure("Invalid or expired refresh token.");

        var user = await _userManager.FindByIdAsync(storedToken.UserId.ToString());
        if (user is null)
            return AuthResult.Failure("Invalid or expired refresh token.");

        var result = await IssueTokensAsync(user, ipAddress, cancellationToken);

        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;
        storedToken.ReplacedByToken = result.RefreshToken;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var storedToken = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.Token == refreshToken, cancellationToken);

        if (storedToken is null || !storedToken.IsActive)
            return false;

        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;
        storedToken.ReasonRevoked = "Revoked by user";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<CompleteProfileResult> CompleteProfileAsync(Guid userId, CompleteProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return CompleteProfileResult.Failure("User not found.");

        var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == request.GradeId, cancellationToken);
        if (grade is null)
            return CompleteProfileResult.Failure("Invalid grade.");

        // Grades are language-scoped rows. Letting a user attach to a grade from the other
        // tree would leave them with a grade whose terms/subjects they can never see.
        if (user.PreferredLanguageId is not null && grade.LangId != user.PreferredLanguageId)
            return CompleteProfileResult.Failure("The selected grade belongs to a different language than this account.");

        var profile = await _dbContext.StudentProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var isNew = profile is null;
        profile ??= new StudentProfile { Id = Guid.NewGuid(), UserId = userId, CreatedAt = DateTime.UtcNow };

        profile.FullName = request.FullName;
        profile.Age = request.Age;
        profile.PhoneNumber = request.PhoneNumber;
        profile.Email = request.Email;
        profile.GradeId = request.GradeId!.Value;
        profile.UpdatedAt = DateTime.UtcNow;

        if (isNew)
            _dbContext.StudentProfiles.Add(profile);

        if (!string.IsNullOrWhiteSpace(request.Email) && !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = request.Email;
            await _userManager.UpdateAsync(user);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CompleteProfileResult.Success();
    }

    public async Task<AuthResult> ReissueTokensAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthResult.Failure("User not found.");

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    private async Task<AuthResult> IssueTokensAsync(ApplicationUser user, string? ipAddress, CancellationToken cancellationToken)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, accessTokenExpiresAt) = _jwtTokenGenerator.GenerateAccessToken(
            user.Id, user.UserName!, user.Email, roles, user.PreferredLanguageId);
        var refreshTokenValue = _jwtTokenGenerator.GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenValue,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = refreshTokenExpiresAt,
            CreatedByIp = ipAddress
        });

        var isProfileComplete = await _dbContext.StudentProfiles.AnyAsync(p => p.UserId == user.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AuthResult.Success(
            user.Id,
            user.UserName!,
            user.Email,
            roles.ToList(),
            isProfileComplete,
            accessToken,
            accessTokenExpiresAt,
            refreshTokenValue,
            refreshTokenExpiresAt);
    }
}
