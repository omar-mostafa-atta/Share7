using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Audit;
using Share7.Domain.Multiplayer;
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
/// <para>
/// <b>Every move is audited</b>, in the same transaction as the move: a prize decided by a person is
/// a decision someone may later have to explain.
/// </para>
/// </summary>
public class PrizeClaimAdminService : IPrizeClaimAdminService
{
    /// <summary>Matches before "the same opponent most of the time" means anything.</summary>
    private const int RepeatOpponentMinMatches = 4;

    /// <summary>Wins handed over before "most wins were handed over" means anything.</summary>
    private const int ForfeitMinWins = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;
    private readonly IAuditLog _audit;

    public PrizeClaimAdminService(ApplicationDbContext dbContext, ILanguageService languageService, IAuditLog audit)
    {
        _dbContext = dbContext;
        _languageService = languageService;
        _audit = audit;
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

        var signals = await SignalsAsync(
            claims.Where(c => c.Award is not null).Select(c => (c.Award!.EventId, c.UserId)).Distinct().ToList(),
            cancellationToken);

        return claims
            .Select(c => new PrizeClaimAdminDto
            {
                ClaimId = c.Id,
                EligibilityReviewedAtUtc = c.EligibilityReviewedAtUtc,
                FraudReviewedAtUtc = c.FraudReviewedAtUtc,
                GuardianConfirmedAtUtc = c.GuardianConfirmedAtUtc,
                GuardianLinkId = c.GuardianLinkId,
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
                ReviewedByUserId = c.ReviewedByUserId,
                Signals = c.Award is { } award ? signals.GetValueOrDefault((award.EventId, c.UserId)) : null
            })
            .ToList();
    }

    public async Task<ServiceResult<PrizeClaimAdminDto>> UpdateAsync(
        Guid claimId,
        UpdatePrizeClaimRequest request,
        Guid reviewedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [PrizeClaims] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {claimId}", cancellationToken);
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

        if (claim.ExpiresAtUtc <= DateTime.UtcNow && target is PrizeClaimState.AwaitingGuardian or PrizeClaimState.Fulfilled)
            return ServiceResult<PrizeClaimAdminDto>.Failure(ApiErrors.PlayPrizeClaimInvalidTransition, ServiceErrorKind.Conflict,
                "This claim has expired and cannot be approved or fulfilled.");

        if (target == PrizeClaimState.AwaitingGuardian && (!request.EligibilityReviewed || !request.FraudReviewed))
            return ServiceResult<PrizeClaimAdminDto>.Failure(ApiErrors.PlayPrizeClaimInvalidTransition, ServiceErrorKind.Validation,
                "Record eligibility and fraud review before approving a physical prize.");
        if (target == PrizeClaimState.Fulfilled)
        {
            if (claim.EligibilityReviewedAtUtc == null || claim.FraudReviewedAtUtc == null)
                return ServiceResult<PrizeClaimAdminDto>.Failure(ApiErrors.PlayPrizeClaimInvalidTransition, ServiceErrorKind.Conflict, "Review evidence is incomplete.");
            var adult = await _dbContext.StudentProfiles.AnyAsync(p => p.UserId == claim.UserId && p.Age >= 18, cancellationToken);
            if (!adult && (!request.GuardianConfirmed || request.GuardianLinkId is not { } link
                || !await _dbContext.GuardianLinks.AnyAsync(g => g.Id == link && g.LearnerUserId == claim.UserId
                    && g.VerifiedAtUtc != null && g.RevokedAtUtc == null, cancellationToken)))
                return ServiceResult<PrizeClaimAdminDto>.Failure(ApiErrors.PlayPrizeClaimInvalidTransition, ServiceErrorKind.Forbidden,
                    "Record confirmation through a real verified guardian link.");
        }
        var now = DateTime.UtcNow;
        var from = claim.State;

        claim.State = target;
        claim.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? claim.ReviewNote : request.Note.Trim();
        claim.ReviewedByUserId = reviewedByUserId;
        claim.ReviewedAtUtc = now;
        claim.UpdatedAtUtc = now;
        if (target == PrizeClaimState.AwaitingGuardian) { claim.EligibilityReviewedAtUtc = now; claim.FraudReviewedAtUtc = now; }
        if (target == PrizeClaimState.Fulfilled && request.GuardianConfirmed)
        { claim.GuardianConfirmedAtUtc = now; claim.GuardianLinkId = request.GuardianLinkId; }

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

        // Ids and states only: the note is free text an operator wrote, and may name a person.
        _audit.Record(new AuditEntry(
            AuditActions.PrizeClaimReviewed,
            AuditAreas.Competitions,
            $"Moved a prize claim from {WireEnum.ToWire(from)} to {WireEnum.ToWire(target)}.",
            "prize_claim",
            claim.Id.ToString(),
            new { claim.AwardId, eventId = claim.Award?.EventId, from = WireEnum.ToWire(from), to = WireEnum.ToWire(target),
                request.EligibilityReviewed, request.FraudReviewed, request.GuardianConfirmed, request.GuardianLinkId },
            ActingUserId: reviewedByUserId));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var updated = await ListAsync(null, claim.Award?.EventId, cancellationToken);

        return ServiceResult<PrizeClaimAdminDto>.Success(updated.First(c => c.ClaimId == claimId));
    }

    /// <summary>
    /// The review signals for each (event, winner): their decided matches in the event and how they
    /// went, plus tournament pairings they won without playing. Read at review time from the results
    /// themselves — nothing is stored, so nothing can drift from what happened.
    /// </summary>
    private async Task<Dictionary<(Guid EventId, Guid UserId), PrizeClaimSignalsDto>> SignalsAsync(
        IReadOnlyList<(Guid EventId, Guid UserId)> keys, CancellationToken cancellationToken)
    {
        var signals = new Dictionary<(Guid, Guid), PrizeClaimSignalsDto>();

        foreach (var group in keys.GroupBy(k => k.EventId))
        {
            var eventId = group.Key;
            var users = group.Select(k => k.UserId).Distinct().ToList();

            var mine = await _dbContext.MatchPlacements
                .AsNoTracking()
                .Where(p => users.Contains(p.UserId)
                            && _dbContext.MatchResults.Any(r => r.SessionId == p.SessionId
                                                                && r.EventId == eventId
                                                                && r.State == MatchResultState.Decided))
                .Select(p => new { p.SessionId, p.UserId, p.IsWinner, p.Flagged })
                .ToListAsync(cancellationToken);

            var sessionIds = mine.Select(p => p.SessionId).Distinct().ToList();

            var everyone = sessionIds.Count == 0
                ? []
                : await _dbContext.MatchPlacements
                    .AsNoTracking()
                    .Where(p => sessionIds.Contains(p.SessionId))
                    .Select(p => new { p.SessionId, p.UserId, p.Forfeited })
                    .ToListAsync(cancellationToken);

            var walkovers = await _dbContext.TournamentMatches
                .AsNoTracking()
                .Where(m => m.WinnerUserId != null
                            && users.Contains(m.WinnerUserId.Value)
                            && (m.Outcome == TournamentOutcomes.Walkover || m.Outcome == TournamentOutcomes.Forfeit)
                            && _dbContext.Tournaments.Any(t => t.Id == m.TournamentId && t.EventId == eventId))
                .GroupBy(m => m.WinnerUserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

            var bySession = everyone.ToLookup(p => p.SessionId);

            foreach (var user in users)
            {
                var played = mine.Where(p => p.UserId == user).ToList();
                var handedOver = walkovers.GetValueOrDefault(user);

                if (played.Count == 0 && handedOver == 0)
                    continue;

                var opponents = played
                    .SelectMany(p => bySession[p.SessionId].Where(o => o.UserId != user).Select(o => o.UserId))
                    .GroupBy(id => id)
                    .Select(g => g.Count())
                    .ToList();

                var wins = played.Count(p => p.IsWinner);

                var winsByForfeit = played.Count(p => p.IsWinner
                                                      && bySession[p.SessionId].Where(o => o.UserId != user).All(o => o.Forfeited));

                var topShare = played.Count == 0 || opponents.Count == 0 ? 0 : (double)opponents.Max() / played.Count;
                var flagged = played.Count(p => p.Flagged);

                var warnings = new List<string>();

                if (played.Count >= RepeatOpponentMinMatches && topShare >= 0.5)
                    warnings.Add("repeat_opponent");

                var unplayedWins = winsByForfeit + handedOver;

                if (unplayedWins >= ForfeitMinWins && unplayedWins * 2 >= wins + handedOver)
                    warnings.Add("opponent_forfeits");

                if (flagged > 0)
                    warnings.Add("flagged_matches");

                signals[(eventId, user)] = new PrizeClaimSignalsDto
                {
                    Matches = played.Count,
                    Wins = wins,
                    DistinctOpponents = opponents.Count,
                    TopOpponentShare = Math.Round(topShare, 2),
                    WinsByForfeit = winsByForfeit,
                    Walkovers = handedOver,
                    FlaggedMatches = flagged,
                    Warnings = warnings
                };
            }
        }

        return signals;
    }
}
