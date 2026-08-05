using Microsoft.AspNetCore.Identity;

namespace Share7.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Which language tree this user sees. Null until the client calls
    /// POST /api/users/me/preferred-language. Content endpoints fall back to English
    /// when it is not set.
    /// </summary>
    public Guid? PreferredLanguageId { get; set; }
}
