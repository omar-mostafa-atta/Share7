using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Users.Models;

/// <summary>
/// One row of the admin user roster.
/// </summary>
/// <remarks>
/// Deliberately lighter than <see cref="UserProfileDto"/>. A roster page renders
/// fifty of these at a time and a console filtering a six-figure user table has no
/// use for a phone number per row — that is what opening the account is for.
/// </remarks>
public class AdminUserListItemDto
{
    public Guid UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    /// <summary>Null until the child completes their profile, which many never do.</summary>
    public string? FullName { get; init; }

    public string? Email { get; init; }

    public int? Age { get; init; }

    public Guid? GradeId { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public bool IsProfileComplete { get; init; }

    public DateTime? CreatedAtUtc { get; init; }

    /// <summary>
    /// The most recent run this account started, which is the closest thing the schema
    /// has to a last-seen timestamp — there is no session or login audit table.
    /// Null for an account that has never played.
    /// </summary>
    public DateTime? LastSeenAtUtc { get; init; }
}

public class AdminUserPageDto
{
    public IReadOnlyList<AdminUserListItemDto> Users { get; init; } = [];

    /// <summary>Total matching the filter, not the page — the console needs it to page.</summary>
    public int Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>
/// An account created by an admin, rather than by someone signing up — which is how a staff
/// account such as <c>ContentTeam</c> comes to exist at all, since registration only ever grants
/// <c>Student</c>.
/// </summary>
/// <remarks>
/// Deliberately the same three things a person needs to sign in with, and nothing more. The
/// password rules are Identity's, configured in <c>AddInfrastructure</c> — 8+ characters with an
/// upper-case letter, a lower-case letter and a digit — and are enforced there rather than
/// repeated here, so the two cannot drift.
/// </remarks>
public class CreateAdminUserRequest
{
    [Required, MinLength(3), MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Exactly one role. Which ones the caller may hand out is the service's decision.</summary>
    [Required]
    public string Role { get; set; } = string.Empty;
}

/// <summary>Filter for the roster. Every member is optional.</summary>
public class AdminUserQuery
{
    /// <summary>
    /// Matched against username, full name and email. Not against the id: a GUID is
    /// looked up directly, and running a LIKE over it would table-scan for nothing.
    /// </summary>
    public string? Search { get; set; }

    public Guid? GradeId { get; set; }

    /// <summary>Restricts to accounts holding this role. Case-sensitive, as Identity stores it.</summary>
    public string? Role { get; set; }

    /// <summary>1-based. Anything below 1 is treated as 1 rather than rejected.</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;
}
