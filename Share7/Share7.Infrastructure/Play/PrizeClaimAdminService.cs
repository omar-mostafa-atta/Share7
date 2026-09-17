using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// The real-world prize queue.
/// <para>
/// <b>Only a person moves a claim.</b> Nothing here is automatic, and nothing here knows how the
/// prize is actually delivered: the row records that a placing won something, who looked at it, and
/// how it ended. It holds no address, no phone number and no payment detail — the users are children,
/// and a prize is not a reason to start collecting any of that.
/// </para>
/// </summary>
public class PrizeClaimAdminService : IPrizeClaimAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public PrizeClaimAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<PrizeClaimAdminDto>> ListAsync(
        string? state = null, Guid? eventId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.PrizeClaims
            .AsNoTracking()
            .Include(c => c.Award).ThenInclude(a => a!.Event).ThenInclude(e => e!.Translations)
            .Include(c => c.Award).ThenInclude(a => a!.Tier).ThenInclude(t => t!.Translations)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(state) && WireEnum.TryFromWire<PrizeClaimState>(state, out var parsed))
            query = query.Where(c => c.State == parsed);

        if (eventId is { } id)
            query = query.Where(c => c.Award!.EventId == id);

        var claims = await query
            .OrderBy(c => c.State)
            .ThenBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (claims.Count == 0) return [];

        // The public handle, not a name: the leaderboard already generates one, and it is the only
        // identifier an operator needs to talk about a winner.
        var userIds = claims.Select(c => c.UserId).Distinct().ToList();

        var handles = await _dbContext.PlayerDisplayNames
            .AsNoTracking()
            .Where(n => userIds.Contains(n.UserId))
            .ToDictionaryAsync(n => n.UserId, n => n.Handle, cancellationToken);

        return claims
            .Select(c => new PrizeClaimAdminDto
            {
                ClaimId = c.Id,
                AwardId = c.AwardId,
                EventId = c.Award?.EventId ?? Guid.Empty,
                EventKey = c.Award?.Event?.EventKey ?? string.Empty,
                EventName = c.Award?.Event?.Translations.FirstOrDefault(t => t.LangId == langId)?.Name ?? string.Empty,
                UserId = c.UserId,
                DisplayName = handles.GetValueOrDefault(c.UserId) ?? string.Empty,
                PrizeTitle = c.Award?.Tier?.Translations.FirstOrDefault(t => t.LangId == langId)?.Title ?? string.Empty,
                DeclaredValueMinor = c.Award?.Tier?.DeclaredValueMinor,
                ValueCurrencyCode = c.Award?.Tier?.ValueCurrencyCode,
                FinalRank = c.Award?.FinalRank ?? 0,
                State = WireEnum.ToWire(c.State),
                ExpiresAtUtc = c.ExpiresAtUtc,
                CreatedAtUtc = c.CreatedAtUtc,
                ReviewedAtUtc = c.ReviewedAtUtc,
                FulfilledAtUtc = c.FulfilledAtUtc,
                ReviewNote = c.ReviewNote,
                ReviewedByUserId = c.ReviewedByUserId
            })
            .ToList();
    }

    public async Task<ServiceResult<PrizeClaimAdminDto>> UpdateAsync(
        Guid claimId,
        UpdatePrizeClaimRequest request,
        Guid reviewedByUserId,
        CancellationToken cancellationToken = default)
    {
        var claim = await _dbContext.PrizeClaims
            .Include(c => c.Award)
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);

        if (claim is null)
            return ServiceResult<PrizeClaimAdminDto>.Failure(
                ApiErrors.PlayPrizeClaimNotFound, ServiceErrorKind.NotFound, "No claim has that id.");

        if (!WireEnum.TryFromWire<PrizeClaimState>(request.State, out var target))
            return ServiceResult<PrizeClaimAdminDto>.Failure(
                ApiErrors.PlayPrizeClaimInvalidTransition,
                ServiceErrorKind.Validation,
                $"'{request.State}' is not a claim state.");

        if (!PrizeClaim.CanTransition(claim.State, target))
            return ServiceResult<PrizeClaimAdminDto>.Failure(
                ApiErrors.PlayPrizeClaimInvalidTransition,
                ServiceErrorKind.Conflict,
                $"A claim cannot move from {WireEnum.ToWire(claim.State)} to {WireEnum.ToWire(target)}.",
                new Dictionary<string, object?> { ["state"] = WireEnum.ToWire(claim.State) });

        var now = DateTime.UtcNow;

        claim.State = target;
        claim.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? claim.ReviewNote : request.Note.Trim();
        claim.ReviewedByUserId = reviewedByUserId;
        claim.ReviewedAtUtc = now;
        claim.UpdatedAtUtc = now;

        if (target == PrizeClaimState.Fulfilled)
            claim.FulfilledAtUtc = now;

        // The award and its claim end together, so a winner's own screen says the same thing the
        // operator's queue does.
        if (claim.Award is { } award)
        {
            award.State = target switch
            {
                PrizeClaimState.Fulfilled => EventAwardState.Fulfilled,
                PrizeClaimState.Forfeited => EventAwardState.Forfeited,
                PrizeClaimState.Rejected => EventAwardState.Void,
                _ => EventAwardState.AwaitingClaim
            };

            award.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await ListAsync(null, claim.Award?.EventId, cancellationToken);

        return ServiceResult<PrizeClaimAdminDto>.Success(updated.First(c => c.ClaimId == claimId));
    }
}
