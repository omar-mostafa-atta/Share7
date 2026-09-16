using System.ComponentModel.DataAnnotations;
using Share7.Application.Common.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>How much of what a session earns is actually paid, as an operator sees it.</summary>
public class EconomyProfileDto
{
    public Guid ProfileId { get; init; }
    public string ProfileKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int PayoutPercent { get; init; }
    public bool PaysRuleRewards { get; init; }
    public bool IsDefault { get; init; }

    /// <summary>How many modes and events settle under it — what deleting one would strand.</summary>
    public int UsedByModes { get; init; }
    public int UsedByEvents { get; init; }
}

public class SaveEconomyProfileRequest
{
    [Required, MaxLength(128)]
    public string ProfileKey { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Percent of a settled payout that is granted. Capped at 500 rather than unbounded: a typo in
    /// this field is currency, and three extra zeroes would be a week of inflation before anybody
    /// noticed.
    /// </summary>
    [Range(0, 500)]
    public int PayoutPercent { get; set; } = 100;

    public bool PaysRuleRewards { get; set; } = true;

    /// <summary>Makes this the platform fallback, moving the flag off whatever currently holds it.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Authoring the pricing profiles modes and events settle under.
/// <para>
/// Small on purpose. A profile is two numbers, and the reason it is a table rather than a constant
/// is that an operator has to be able to halve an event's payout on a Friday without a deploy.
/// </para>
/// </summary>
public interface IEconomyProfileAdminService
{
    Task<IReadOnlyList<EconomyProfileDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<EconomyProfileDto>> CreateAsync(
        SaveEconomyProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Full replace. The key is immutable, as every other key in this domain is.</summary>
    Task<ServiceResult<EconomyProfileDto>> UpdateAsync(
        Guid profileId, SaveEconomyProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a profile. Refused while a mode or event still points at it, and refused for the
    /// platform default — deleting that would leave every mode with no fallback to settle under.
    /// </summary>
    Task<ServiceResult> DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
