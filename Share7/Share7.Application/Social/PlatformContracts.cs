using Share7.Application.Common.Models;
using Share7.Domain.Social;

namespace Share7.Application.Social;

public static class SocialPlatformErrors
{
    public static readonly ApiErrorCode Unavailable = new("SOCIAL_PROFILE_UNAVAILABLE", "social.profileUnavailable");
    public static readonly ApiErrorCode ContentInvalid = new("SHOWCASE_CONTENT_INVALID", "social.showcaseInvalid");
    public static readonly ApiErrorCode Stale = new("SOCIAL_VERSION_CONFLICT", "social.refreshRequired");
    public static readonly ApiErrorCode Restricted = new("SOCIAL_RESTRICTED", "social.restricted");
    public static readonly ApiErrorCode ReportLimit = new("REPORT_LIMIT_REACHED", "social.reportLimit");
}
public sealed record CursorPage<T>(IReadOnlyList<T> Items, long? NextAfter, DateTime ServerTimeUtc);
public sealed record ShowcaseContentDto(string Key, ShowcaseContentKind Kind, string AssetKey, string? FallbackKey,
    Guid? ProductId, bool OfficialOnly, bool Enabled, int MinimumQuality, int ContractVersion);
public sealed record ShowcaseSelectionRequest(IReadOnlyList<string> Keys, int ExpectedVersion);
public sealed record SafeEquipmentSlot(string SlotKey, string CosmeticKey, string? ColorKey);
public sealed record SafeEquipment(string BodyType, IReadOnlyList<SafeEquipmentSlot> Slots);
public sealed record ProfileStatistics(int LessonsAced, int MatchesPlayed, int BestStreak);
public sealed record GamingProfileDto(Guid UserId, string? DisplayName, bool IsSelf,
    OfficialProfileKind? Identity, bool Verified, string? Title, PlayerPresenceState? Presence,
    ProfileStatistics? Statistics, SafeEquipment Equipment, IReadOnlyList<ShowcaseContentDto> Showcase,
    int ShowcaseVersion, bool CanInvite, bool CanChallenge, bool CanFollow, bool Following, DateTime ServerTimeUtc);
public sealed record OfficialProfileInput(OfficialProfileKind Kind, bool Verified, bool Discoverable,
    string DisplayNameEn, string DisplayNameAr, string TitleEn, string TitleAr);
public sealed record OfficialActivityInput(string PublicationKey, string TitleEn, string TitleAr, Guid? EventId,
    DateTime StartsAtUtc, DateTime ExpiresAtUtc);
public sealed record OfficialActivityDto(long Sequence, Guid ProfileUserId, string Title, Guid? EventId,
    DateTime StartsAtUtc, DateTime ExpiresAtUtc);
public sealed record PrivacyDto(SocialVisibility Profile, SocialVisibility Presence, SocialVisibility Statistics,
    SocialVisibility Invitations, SocialVisibility Challenges, bool FriendRequests);
public sealed record ReportRequest(Guid UserId, Guid? SessionId, ReportReason Reason, string RequestId);
public sealed record ReportReceipt(Guid Id, ModerationState State, DateTime CreatedAtUtc);
public sealed record ModerationCaseDto(Guid Id, long Sequence, Guid ReporterId, Guid ReportedUserId,
    Guid? SessionId, ReportReason Reason, string EvidenceJson, ModerationState State, string? DecisionCode, DateTime CreatedAtUtc);
public sealed record ModerationDecisionRequest(ModerationState State, string DecisionCode, int? RestrictDays, bool PermanentRestriction = false);
public sealed record RestrictionDto(Guid Id, DateTime StartsAtUtc, DateTime? ExpiresAtUtc, bool Appealed, string? AppealResolutionCode = null);
public sealed record ModerationAppealDto(Guid RestrictionId, Guid UserId, Guid ReportId, string ReasonCode, DateTime AppealedAtUtc, DateTime? ExpiresAtUtc);
public sealed record GuardianSocialConsentDto(Guid LinkId, Guid LearnerUserId, bool Enabled);
public interface IGuardianSocialConsentService
{
    Task<IReadOnlyList<GuardianSocialConsentDto>> ListAsync(Guid guardian, CancellationToken token = default);
    Task<ServiceResult> SetAsync(Guid guardian, Guid link, bool enabled, CancellationToken token = default);
}
public sealed record InboxItemDto(long Sequence, Guid EventId, string Category, string TemplateKey,
    string Type, System.Text.Json.JsonElement Payload, bool Read, DateTime OccurredAtUtc, DateTime ExpiresAtUtc);

public interface IGamingProfileService
{
    Task<ServiceResult<GamingProfileDto>> ReadAsync(Guid caller, Guid target, CancellationToken token = default);
    Task<ServiceResult<CursorPage<GamingProfileDto>>> DirectoryAsync(Guid caller, long after, CancellationToken token = default);
    Task<ServiceResult<GamingProfileDto>> SelectAsync(Guid user, ShowcaseSelectionRequest request, CancellationToken token = default);
    Task<ServiceResult<IReadOnlyList<ShowcaseContentDto>>> ContentAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult> FollowAsync(Guid user, Guid official, bool follow, CancellationToken token = default);
    Task<ServiceResult<CursorPage<OfficialActivityDto>>> ActivityAsync(Guid user, long before, CancellationToken token = default);
}
public interface ISocialProfileAdminService
{
    Task<ServiceResult> SetIdentityAsync(Guid user, OfficialProfileInput request, CancellationToken token = default);
    Task<ServiceResult> SetContentAsync(ShowcaseContentDto request, CancellationToken token = default);
    Task<ServiceResult> PublishActivityAsync(Guid user, OfficialActivityInput request, CancellationToken token = default);
    Task<IReadOnlyList<ShowcaseContentDto>> ContentAsync(CancellationToken token = default);
}
public interface ISocialSafetyService
{
    Task<PrivacyDto> PrivacyAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult<PrivacyDto>> SetPrivacyAsync(Guid user, PrivacyDto request, CancellationToken token = default);
    Task<ServiceResult> MuteAsync(Guid user, Guid target, bool mute, CancellationToken token = default);
    Task<ServiceResult<ReportReceipt>> ReportAsync(Guid user, ReportRequest request, CancellationToken token = default);
    Task<CursorPage<ModerationCaseDto>> CasesAsync(long after, ModerationState? state, CancellationToken token = default);
    Task<ServiceResult> DecideAsync(Guid report, ModerationDecisionRequest request, CancellationToken token = default);
    Task<IReadOnlyList<RestrictionDto>> RestrictionsAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult> AppealAsync(Guid user, Guid restriction, string reasonCode, CancellationToken token = default);
    Task<ServiceResult> RevokeAsync(Guid restriction, string reasonCode, CancellationToken token = default);
    Task<IReadOnlyList<ModerationAppealDto>> AppealsAsync(CancellationToken token = default);
    Task<ServiceResult> ReviewAppealAsync(Guid restriction, bool revoke, string reasonCode, CancellationToken token = default);
}
public interface IInboxService
{
    Task<ServiceResult<CursorPage<InboxItemDto>>> ReadAsync(Guid user, long before, CancellationToken token = default);
    Task<ServiceResult> MarkReadAsync(Guid user, Guid eventId, CancellationToken token = default);
    Task<IReadOnlyDictionary<string, bool>> PreferencesAsync(Guid user, CancellationToken token = default);
    Task<ServiceResult> PreferenceAsync(Guid user, string category, bool enabled, CancellationToken token = default);
}
