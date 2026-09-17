using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Play.Interfaces;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Authoring pricing profiles.
/// <para>
/// The invariant worth protecting is "exactly one default": every mode that names no profile settles
/// under it, so a platform with two has none, and one with zero silently falls back to paying full
/// price. The database enforces the first through a filtered unique index; this service enforces the
/// second by refusing to delete or unset the last one.
/// </para>
/// </summary>
public class EconomyProfileAdminService : IEconomyProfileAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public EconomyProfileAdminService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<EconomyProfileDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.EconomyProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.ProfileKey)
            .Select(p => new EconomyProfileDto
            {
                ProfileId = p.Id,
                ProfileKey = p.ProfileKey,
                Name = p.Name,
                PayoutPercent = p.PayoutPercent,
                PaysRuleRewards = p.PaysRuleRewards,
                IsDefault = p.IsDefault,
                UsedByModes = _dbContext.GameModes.Count(m => m.EconomyProfileId == p.Id),
                UsedByEvents = _dbContext.PlayEvents.Count(e => e.EconomyProfileId == p.Id)
            })
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult<EconomyProfileDto>> CreateAsync(
        SaveEconomyProfileRequest request, CancellationToken cancellationToken = default)
    {
        var key = (request.ProfileKey ?? string.Empty).Trim().ToLowerInvariant();

        if (key.Length == 0)
            return Invalid("profileKey is required.");

        if (await _dbContext.EconomyProfiles.AnyAsync(p => p.ProfileKey == key, cancellationToken))
            return Invalid($"Another profile already uses the key '{key}'.");

        var profile = new EconomyProfile
        {
            Id = Guid.NewGuid(),
            ProfileKey = key,
            Name = (request.Name ?? string.Empty).Trim(),
            PayoutPercent = request.PayoutPercent,
            PaysRuleRewards = request.PaysRuleRewards,
            IsDefault = request.IsDefault,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.EconomyProfiles.Add(profile);

        if (profile.IsDefault)
            await ClearOtherDefaultsAsync(profile.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReadAsync(profile.Id, cancellationToken);
    }

    public async Task<ServiceResult<EconomyProfileDto>> UpdateAsync(
        Guid profileId, SaveEconomyProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.EconomyProfiles
            .FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

        if (profile is null)
            return ServiceResult<EconomyProfileDto>.Failure(
                ApiErrors.NotFound, ServiceErrorKind.NotFound, "No economy profile has that id.");

        var key = (request.ProfileKey ?? string.Empty).Trim().ToLowerInvariant();

        if (!string.Equals(key, profile.ProfileKey, StringComparison.Ordinal))
            return Invalid("A profile key is immutable. Create a new profile instead.");

        if (profile.IsDefault && !request.IsDefault)
            return Invalid(
                "This is the platform default. Make another profile the default instead of clearing " +
                "the flag — every mode that names no profile settles under it.");

        profile.Name = (request.Name ?? string.Empty).Trim();
        profile.PayoutPercent = request.PayoutPercent;
        profile.PaysRuleRewards = request.PaysRuleRewards;
        profile.IsDefault = request.IsDefault;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        if (profile.IsDefault)
            await ClearOtherDefaultsAsync(profile.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReadAsync(profile.Id, cancellationToken);
    }

    public async Task<ServiceResult> DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.EconomyProfiles
            .FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

        if (profile is null)
            return ServiceResult.Failure(
                ApiErrors.NotFound, ServiceErrorKind.NotFound, "No economy profile has that id.");

        if (profile.IsDefault)
            return ServiceResult.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Conflict,
                "The platform default cannot be deleted. Make another profile the default first.");

        var modes = await _dbContext.GameModes.CountAsync(m => m.EconomyProfileId == profileId, cancellationToken);
        var events = await _dbContext.PlayEvents.CountAsync(e => e.EconomyProfileId == profileId, cancellationToken);

        if (modes + events > 0)
            return ServiceResult.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Conflict,
                $"{modes} mode(s) and {events} event(s) settle under this profile. Point them " +
                "somewhere else first.");

        _dbContext.EconomyProfiles.Remove(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    private async Task ClearOtherDefaultsAsync(Guid keepProfileId, CancellationToken cancellationToken)
    {
        var others = await _dbContext.EconomyProfiles
            .Where(p => p.IsDefault && p.Id != keepProfileId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.IsDefault = false;
            other.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<ServiceResult<EconomyProfileDto>> ReadAsync(
        Guid profileId, CancellationToken cancellationToken)
    {
        var all = await ListAsync(cancellationToken);

        return ServiceResult<EconomyProfileDto>.Success(all.First(p => p.ProfileId == profileId));
    }

    private static ServiceResult<EconomyProfileDto> Invalid(string message) =>
        ServiceResult<EconomyProfileDto>.Failure(
            ApiErrors.ValidationFailed, ServiceErrorKind.Validation, message);
}
