namespace Share7.Infrastructure.Identity.ExternalAuth;

/// <summary>
/// A verified external identity.
/// </summary>
/// <param name="EmailVerified">
/// Whether the provider says it has verified <paramref name="Email"/> belongs to this person. Null when
/// the provider does not say — Facebook's Graph API returns an email with no verification flag. Only an
/// explicit <c>false</c> blocks linking to an existing account today; see AuthService.ExternalLoginAsync.
/// </param>
public record ExternalUserInfo(string ProviderKey, string Email, string? Name, bool? EmailVerified = null);

public interface IExternalLoginValidator
{
    string Provider { get; }

    Task<ExternalUserInfo?> ValidateAsync(string token, CancellationToken cancellationToken);
}
