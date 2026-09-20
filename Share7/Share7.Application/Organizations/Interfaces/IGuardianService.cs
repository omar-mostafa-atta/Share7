using Share7.Application.Organizations.Models;
using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Interfaces;

/// <summary>
/// Guardian links, their verification and their consent.
///
/// <para><b>Consent is a row with a scope and a revocation date, not a boolean set at signup</b>
/// (§18.3). A boolean cannot answer "did they agree to <i>this</i>", and on a child-audience
/// platform that question is asked again by every feature — most recently by calibration, which
/// Phase 3 correctly refuses to a learner under 18 and which a guardian can now grant.</para>
///
/// <para><b>An unverified link grants nothing.</b> Anyone can type a child's user name.</para>
/// </summary>
public interface IGuardianService
{
    Task<IReadOnlyList<GuardianLinkDto>> ListForGuardianAsync(
        Guid guardianUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GuardianLinkDto>> ListForLearnerAsync(
        Guid learnerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the link, unverified. Verification is a separate act by somebody who can actually
    /// check, which is the whole value of the field.
    /// </summary>
    Task<GuardianLinkDto> CreateAsync(
        CreateGuardianLinkRequest request, CancellationToken cancellationToken = default);

    Task<GuardianLinkDto> VerifyAsync(
        Guid linkId, Guid verifiedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Narrows or widens what the link permits. Separate from verification because the two change
    /// on different occasions: a relationship is verified once, and what it consents to changes
    /// whenever the platform asks for something new.
    /// </summary>
    Task<GuardianLinkDto> SetConsentAsync(
        Guid linkId, GuardianConsentScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the link. <b>A timestamp, not a deletion</b> — including the period during which data
    /// was legitimately shared, which is exactly what an audit needs to be able to reconstruct.
    /// </summary>
    Task RevokeAsync(Guid linkId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The reports an organization and a guardian may read, built from the same measurements as every
/// other reader and shaped for the audience before they leave this layer.
///
/// <para><b>Reports are audience-shaped by construction</b> (§9.5). The alternative — handing a raw
/// evidence feed to a UI and letting the UI choose what to render — puts the child-safety boundary
/// in whichever client was written last. The DTOs here have no field a play timestamp, a coin
/// balance or a raw response could be written into.</para>
/// </summary>
public interface IEducationalReportingService
{
    /// <summary>
    /// How a cohort stands, target by target, with small cells suppressed and the reason stated.
    ///
    /// <para><b>Every aggregate carries an N and suppresses small cells</b> (§19.2) — both because
    /// a share over four learners is statistically meaningless and because it de-anonymises them.
    /// The suppression is carried as a reason rather than a blank, so a reader never learns that
    /// blank means zero.</para>
    /// </summary>
    Task<CohortReportDto> GetCohortReportAsync(
        Guid cohortId, Guid viewerUserId, Guid langId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cohort's learners, each with verdict counts and nothing behavioural.
    /// <para>
    /// A teacher sees whether a child has worked on something and how it went. They do not see when
    /// the child was playing, for how long, or on what — that is telemetry, and it belongs to
    /// nobody outside the platform's own analytics.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CohortLearnerRowDto>> GetCohortLearnersAsync(
        Guid cohortId, Guid viewerUserId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a guardian may read about one learner — strengths, gaps, and how much evidence stands
    /// behind each, in plain language.
    ///
    /// <para>Resolves through <see cref="IEducationalAccessService"/> like everything else, so a
    /// guardian whose link is unverified, revoked, or lacks
    /// <see cref="GuardianConsentScope.ViewProgress"/> gets the same answer as a stranger.</para>
    /// </summary>
    Task<GuardianReportDto?> GetGuardianReportAsync(
        Guid learnerUserId, Guid viewerUserId, Guid langId,
        bool viewerIsPlatformAdmin = false, CancellationToken cancellationToken = default);
}
