using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Models;

// ─────────────────────────────────────────────────────────────── organizations

public sealed record OrganizationDto(
    Guid Id,
    string OrgKey,
    Guid? ParentOrgId,
    OrganizationKind Kind,
    string Name,
    string CountryCode,
    OrganizationStatus Status,
    Guid? ItemBankId,
    DateTime CreatedAtUtc,
    int MemberCount,
    int CohortCount,
    int LearnerCount);

public sealed record CreateOrganizationRequest(
    string OrgKey,
    string Name,
    OrganizationKind Kind,
    string CountryCode,
    Guid? ParentOrgId);

public sealed record MembershipDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string? FullName,
    Guid OrgId,
    OrgRole Role,
    MembershipStatus Status,
    DateTime GrantedAtUtc,
    DateTime? RevokedAtUtc);

public sealed record GrantMembershipRequest(Guid UserId, OrgRole Role);

// ────────────────────────────────────────────────────────────────────── cohorts

public sealed record CohortDto(
    Guid Id,
    Guid OrgId,
    string OrgName,
    string Name,
    string AcademicPeriod,
    Guid? CurriculumVersionId,
    Guid? PlacementNodeId,
    string? PlacementLabel,
    Guid? OverlayId,
    CohortStatus Status,
    DateTime CreatedAtUtc,
    int LearnerCount,
    int TeacherCount);

public sealed record CreateCohortRequest(
    Guid OrgId,
    string Name,
    string AcademicPeriod,
    Guid? CurriculumVersionId,
    Guid? PlacementNodeId);

public sealed record CohortMemberDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string? FullName,
    CohortRole Role,
    DateTime JoinedAtUtc,
    DateTime? LeftAtUtc,
    Guid? EnrollmentId);

/// <summary>
/// Adding somebody to a cohort. For a learner this provisions an org-owned enrolment, which is the
/// act that gives the organization visibility — see <c>Enrollment.OwnerOrgId</c>.
/// </summary>
public sealed record AddCohortMemberRequest(Guid UserId, CohortRole Role);

// ───────────────────────────────────────────────────────────────────── guardians

public sealed record GuardianLinkDto(
    Guid Id,
    Guid GuardianUserId,
    string GuardianUserName,
    Guid LearnerUserId,
    string LearnerUserName,
    GuardianRelationship Relationship,
    GuardianConsentScope ConsentScope,
    DateTime? VerifiedAtUtc,
    DateTime? RevokedAtUtc,
    DateTime CreatedAtUtc);

public sealed record CreateGuardianLinkRequest(
    Guid GuardianUserId,
    Guid LearnerUserId,
    GuardianRelationship Relationship,
    GuardianConsentScope ConsentScope);

// ────────────────────────────────────────────────────────────────────── overlays

public sealed record OverlayDto(
    Guid Id,
    string OverlayKey,
    Guid OrgId,
    Guid CurriculumVersionId,
    string Name,
    OverlayStatus Status,
    string? SourceNote,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    IReadOnlyList<OverlayEditDto> Edits);

public sealed record OverlayEditDto(
    Guid Id,
    OverlayEditKind Kind,
    Guid TargetNodeId,
    string? TargetNodeTitle,
    Guid? ReplacementNodeId,
    string? ReplacementNodeTitle,
    int? NewOrder,
    DateTime? ScheduledFromUtc,
    DateTime? ScheduledToUtc,
    int ApplyOrder,
    string? Note);

public sealed record CreateOverlayRequest(
    Guid OrgId,
    Guid CurriculumVersionId,
    string OverlayKey,
    string Name,
    string? SourceNote);

public sealed record AddOverlayEditRequest(
    OverlayEditKind Kind,
    Guid TargetNodeId,
    Guid? ReplacementNodeId,
    int? NewOrder,
    DateTime? ScheduledFromUtc,
    DateTime? ScheduledToUtc,
    string? Note);

// ─────────────────────────────────────────────────────────────────── assignments

public sealed record AssignmentDto(
    Guid Id,
    Guid CohortId,
    string Title,
    Guid? NodeId,
    string? NodeTitle,
    Guid? AssessmentFormId,
    DateTime AssignedAtUtc,
    DateTime? DueAtUtc,
    bool IsSupervised,
    DateTime? WithdrawnAtUtc,
    int LearnerCount,
    int CompletedCount);

public sealed record CreateAssignmentRequest(
    Guid CohortId,
    string Title,
    Guid? NodeId,
    Guid? AssessmentFormId,
    DateTime? DueAtUtc,
    bool IsSupervised);
