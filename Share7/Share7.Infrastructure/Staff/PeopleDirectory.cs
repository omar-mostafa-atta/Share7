using Microsoft.EntityFrameworkCore;
using Share7.Application.Staff.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// Resolves user ids to the name a person should see — a staff member's full name, otherwise the
/// account's username. Records (the audit trail, "created by") store ids only; names are looked up
/// here, at read time, so a record never holds personal details and survives the account.
/// </summary>
public class PeopleDirectory
{
    /// <summary>Shown for an id whose account no longer exists.</summary>
    public const string RemovedAccount = "Removed account";

    private readonly ApplicationDbContext _db;

    public PeopleDirectory(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<Guid, string>();

        var usernames = await _db.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? RemovedAccount, cancellationToken);

        var fullNames = await _db.StaffProfiles.AsNoTracking()
            .Where(p => wanted.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FullName })
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return wanted.ToDictionary(
            id => id,
            id => fullNames.TryGetValue(id, out var full) ? full
                : usernames.TryGetValue(id, out var username) ? username
                : RemovedAccount);
    }

    public static PersonRefDto? Ref(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } value ? new PersonRefDto(value, names.TryGetValue(value, out var name) ? name : RemovedAccount) : null;
}
