using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Auth.Models;
using Share7.Domain.Constants;
using Share7.Domain.Entities;
using Share7.Infrastructure.Identity.ExternalAuth;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Staff;

namespace Share7.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly JwtSettings _jwtSettings;
    private readonly StudioOptions _studio;
    private readonly IEnumerable<IExternalLoginValidator> _externalLoginValidators;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IJwtTokenGenerator jwtTokenGenerator,
        IOptions<JwtSettings> jwtOptions,
        IOptions<StudioOptions> studioOptions,
        IEnumerable<IExternalLoginValidator> externalLoginValidators)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _jwtTokenGenerator = jwtTokenGenerator;
        _jwtSettings = jwtOptions.Value;
        _studio = studioOptions.Value;
        _externalLoginValidators = externalLoginValidators;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var existing = await _userManager.FindByNameAsync(request.Username);
        if (existing is not null)
            return AuthResult.Failure("A user with this username already exists.");

        //var languageExists = await _dbContext.Languages
        //    .AnyAsync(l => l.Id == request.LanguageId, cancellationToken);
        //if (!languageExists)
        //    return AuthResult.Failure("Invalid language.");

        var user = new ApplicationUser
        {
            UserName = request.Username
            //PreferredLanguageId = request.LanguageId
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

        if (await IsStudioOnlyStaffAsync(user))
        {
            // With the address when there is one to give. This sentence is the only thing a member
            // who has not heard about cutover will be shown, and "somewhere else" is not directions.
            var studio = _studio.PublicUrl;

            return AuthResult.Failure(string.IsNullOrWhiteSpace(studio)
                ? "Content-team accounts sign in to the Content Studio, not here."
                : $"Content-team accounts sign in to the Content Studio, not here: {studio.TrimEnd('/')}");
        }

        return await IssueTokensAsync(user, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Whether this account's only standing here is content authoring, and so belongs in the Studio.
    /// <para>
    /// Cutover (plan P6). Approved default #2 — staff accounts are staff-only — has been true of
    /// external sign-in since it was written (<see cref="NeverLinkedRoles"/>); the password door was
    /// held open on purpose until the Studio had a workspace to move people into, and this closes it.
    /// </para>
    /// <para>
    /// Asked after the password has already been checked, so that it never becomes a way to ask the
    /// server which usernames belong to the content team. Somebody who is also an administrator
    /// keeps their console: the refusal is for people whose standing is content authoring alone, and
    /// shutting an administrator out of the Admin Console is not what this phase is for.
    /// </para>
    /// </summary>
    private async Task<bool> IsStudioOnlyStaffAsync(ApplicationUser user) =>
        await _userManager.IsInRoleAsync(user, Roles.ContentTeam)
        && !await _userManager.IsInRoleAsync(user, Roles.Admin)
        && !await _userManager.IsInRoleAsync(user, Roles.SuperAdmin);

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

            // Attaching a provider to an account that already exists is the one path here that
            // hands a caller an identity they did not create, so it is where the checks live.
            if (user is not null && await RefuseLinkAsync(user, externalUser) is { } refusal)
                return refusal;

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

    /// <summary>
    /// Roles whose accounts an external sign-in may never attach itself to. Staff and administrators
    /// sign in with a username and password; an email match with a Google or Facebook account is not
    /// proof of being the person who holds the role.
    /// </summary>
    private static readonly string[] NeverLinkedRoles = [Roles.Admin, Roles.SuperAdmin, Roles.ContentTeam];

    /// <summary>
    /// Why this external identity may not be linked to <paramref name="existing"/>, or null when it may.
    /// <para>
    /// <b>The takeover this closes.</b> Linking used to follow the email alone: anybody able to present
    /// a provider token asserting an address inherited the account holding it, roles included. So a
    /// provider that says the address is <i>unverified</i> is refused, and a privileged or staff
    /// account is never linked at all. The message is the same for both and names neither, so it does
    /// not confirm which accounts exist.
    /// </para>
    /// </summary>
    private async Task<AuthResult?> RefuseLinkAsync(ApplicationUser existing, ExternalUserInfo externalUser)
    {
        var refused = externalUser.EmailVerified == false
            || (await _userManager.GetRolesAsync(existing)).Any(role => NeverLinkedRoles.Contains(role));

        return refused
            ? AuthResult.Failure(
                "This sign-in can't be connected to an existing account. Sign in with your username and password instead.")
            : null;
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

        // Grades are shared across languages now — there is no "grade from the other tree" to
        // guard against, and the cross-language check that used to live here is gone with it.
        if (!await _dbContext.Grades.AnyAsync(g => g.Id == request.GradeId, cancellationToken))
            return CompleteProfileResult.Failure("Invalid grade.");

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
            user.Email = request.Email;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return CompleteProfileResult.Failure(updateResult.Errors.Select(e => e.Description).ToArray());

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
        var privileged = roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin);
        var (accessToken, accessTokenExpiresAt) = _jwtTokenGenerator.GenerateAccessToken(
            user.Id, user.UserName!, user.Email, roles, user.PreferredLanguageId,
            privileged ? Staff.StaffSecrets.StampHash(user.SecurityStamp) : null);
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
