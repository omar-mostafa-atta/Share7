namespace Share7.Domain.Social;

public enum SocialVisibility { Nobody = 0, Friends = 1, Connections = 2 }
public enum OfficialProfileKind { Creator = 1, Official = 2, Developer = 3, Designer = 4, Moderator = 5, Educator = 6, Partner = 7, EventHost = 8 }
public enum ShowcaseContentKind { Character = 1, Scene = 2, Pose = 3, Animation = 4, Camera = 5, Prop = 6, Effect = 7, Theme = 8 }
public enum ReportReason { UnsafeBehaviour = 1, Impersonation = 2, Cheating = 3, InappropriateContent = 4, Spam = 5 }
public enum ModerationState { Open = 0, Dismissed = 1, Actioned = 2 }

/// <summary>Restrictive overrides of the existing relationship and guardian-consent policy.</summary>
public class SocialPrivacy
{
    public Guid UserId { get; set; }
    public SocialVisibility Profile { get; set; } = SocialVisibility.Connections;
    public SocialVisibility Presence { get; set; } = SocialVisibility.Connections;
    public SocialVisibility Statistics { get; set; } = SocialVisibility.Friends;
    public SocialVisibility Invitations { get; set; } = SocialVisibility.Connections;
    public SocialVisibility Challenges { get; set; } = SocialVisibility.Connections;
    public bool FriendRequests { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Approval/presentation is deliberately unrelated to authentication roles.</summary>
public class OfficialProfile
{
    public Guid UserId { get; set; }
    public long Sequence { get; set; }
    public OfficialProfileKind Kind { get; set; }
    public bool Verified { get; set; }
    public bool Discoverable { get; set; }
    public string DisplayNameEn { get; set; } = "";
    public string DisplayNameAr { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string TitleAr { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Approved asset contract. Keys map to the client's Addressable content catalog.</summary>
public class ShowcaseContent
{
    public string Key { get; set; } = "";
    public ShowcaseContentKind Kind { get; set; }
    public string AssetKey { get; set; } = "";
    public string? FallbackKey { get; set; }
    public Guid? ProductId { get; set; }
    public bool OfficialOnly { get; set; }
    public bool Enabled { get; set; }
    public int MinimumQuality { get; set; }
    public int ContractVersion { get; set; } = 1;
}

public class PlayerShowcase
{
    public Guid UserId { get; set; }
    public string SelectionJson { get; set; } = "[]";
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class OfficialFollow
{
    public Guid UserId { get; set; }
    public Guid FollowedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>One publication per official account, never one row per follower.</summary>
public class OfficialActivity
{
    public long Sequence { get; set; }
    public Guid UserId { get; set; }
    public string TitleEn { get; set; } = "";
    public string TitleAr { get; set; } = "";
    public Guid? EventId { get; set; }
    public string PublicationKey { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public class PlayerMute
{
    public Guid UserId { get; set; }
    public Guid MutedUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PlayerReport
{
    public Guid Id { get; set; }
    public long Sequence { get; set; }
    public Guid UserId { get; set; }
    public Guid ReportedUserId { get; set; }
    public Guid? SessionId { get; set; }
    public ReportReason Reason { get; set; }
    public string RequestId { get; set; } = "";
    public string EvidenceJson { get; set; } = "{}";
    public ModerationState State { get; set; }
    public string? DecisionCode { get; set; }
    public int? RestrictDays { get; set; }
    public bool PermanentRestriction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

public class SocialRestriction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ReportId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? AppealCode { get; set; }
    public DateTime? AppealedAtUtc { get; set; }
    public DateTime? AppealReviewedAtUtc { get; set; }
    public string? AppealResolutionCode { get; set; }
}

public class InboxRead
{
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
    public DateTime ReadAtUtc { get; set; }
}

public class InboxPreference
{
    public Guid UserId { get; set; }
    public string Category { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
