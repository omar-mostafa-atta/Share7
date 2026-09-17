namespace Share7.Infrastructure.Identity.ExternalAuth;

public record ExternalUserInfo(string ProviderKey, string Email, string? Name);

public interface IExternalLoginValidator
{
    string Provider { get; }

    Task<ExternalUserInfo?> ValidateAsync(string token, CancellationToken cancellationToken);
}
