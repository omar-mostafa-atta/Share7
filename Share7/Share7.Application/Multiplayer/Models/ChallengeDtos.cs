using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

public class ChallengeDto
{
    public Guid Id { get; set; }

    public Guid ChallengerUserId { get; set; }
    public string? ChallengerDisplayName { get; set; }
    public Guid RecipientUserId { get; set; }
    public string? RecipientDisplayName { get; set; }

    /// <summary><c>challenger</c> or <c>recipient</c> — which side of it the caller is on.</summary>
    public string YouAre { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>The score to beat, in whole percent.</summary>
    public int BarPercent { get; set; }

    /// <summary>The recipient's best inside the window so far, or null before their first attempt.</summary>
    public int? RecipientBestPercent { get; set; }

    public ChallengeState State { get; set; }
    public ChallengeOutcome Outcome { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public class SendChallengeRequest
{
    /// <summary>Who to challenge — from the caller's connections list.</summary>
    public Guid UserId { get; set; }

    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>How long they have, in days: 1 to 7, default 3.</summary>
    public int? Days { get; set; }
}

public class SendLiveChallengeRequest : MultiplayerRequest
{
    /// <summary>Who to challenge — from the caller's connections list.</summary>
    public Guid UserId { get; set; }

    public Guid GameId { get; set; }
    public string? ModeKey { get; set; }
    public CurriculumPathDto? CurriculumPath { get; set; }

    /// <summary>The Photon room the caller will bring up for the two of them.</summary>
    public string TransportSessionName { get; set; } = string.Empty;
    public string? TransportRegion { get; set; }
    public int ProtocolVersion { get; set; }
}

public class LiveChallengeDto
{
    public MultiplayerSessionDto Session { get; set; } = null!;
    public SessionInvitationDto Invitation { get; set; } = null!;
}
