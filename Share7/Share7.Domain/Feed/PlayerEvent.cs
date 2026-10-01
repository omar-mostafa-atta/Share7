namespace Share7.Domain.Feed;

/// <summary>
/// One thing that happened to a player outside any room — an invite, a challenge, a result — kept
/// until their client has had the chance to read it.
/// <para>
/// **Written in the same transaction as the change it describes.** That is the whole outbox: an
/// invite that commits without its event, or an event whose invite rolled back, cannot exist. There
/// is no broker and no second table; the feed is read straight from here by a long-poll.
/// </para>
/// <para>
/// **At least once, in order, per recipient.** <see cref="Sequence"/> is the cursor a client keeps;
/// <see cref="EventId"/> is what it de-duplicates on, because a response lost on the way back is
/// read again from the same cursor.
/// </para>
/// </summary>
public class PlayerEvent
{
    /// <summary>Database-assigned and ever-increasing: the order, and the client's cursor.</summary>
    public long Sequence { get; set; }

    public Guid EventId { get; set; }

    public Guid RecipientUserId { get; set; }

    /// <summary><c>domain.noun.pastTense</c> — see <see cref="PlayerEventTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The payload's shape version. A new shape is a new version, never an edited one.</summary>
    public int Version { get; set; } = 1;

    public string PayloadJson { get; set; } = "{}";

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// After this the event is no longer delivered — an invite to a room that has closed is noise.
    /// Retention is separate: rows are deleted a fixed period after they occurred, expired or not.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>The event types, so a publisher and a client contract cannot disagree by typo.</summary>
public static class PlayerEventTypes
{
    public const string InviteReceived = "multiplayer.invite.received";
    public const string InviteCancelled = "multiplayer.invite.cancelled";
    public const string InviteAccepted = "multiplayer.invite.accepted";
    public const string MatchResultReady = "multiplayer.match.result_ready";
    public const string ChallengeReceived = "multiplayer.challenge.received";
    public const string ChallengeAccepted = "multiplayer.challenge.accepted";
    public const string ChallengeCancelled = "multiplayer.challenge.cancelled";
    public const string ChallengeCompleted = "multiplayer.challenge.completed";
    public const string FriendRequestReceived = "social.friend_request.received";
    public const string FriendRequestAccepted = "social.friend_request.accepted";
    public const string PartyInviteReceived = "multiplayer.party.invite_received";
    public const string PartyUpdated = "multiplayer.party.updated";
    public const string PartyPlayStarted = "multiplayer.party.play_started";
    public const string RankedUpdated = "multiplayer.ranked.updated";
    public const string MatchFound = "multiplayer.matchmaking.match_found";
    public const string MatchmakingExpired = "multiplayer.matchmaking.expired";
    public const string MatchmakingRequeued = "multiplayer.matchmaking.requeued";
    public const string TournamentMatchReady = "multiplayer.tournament.match_ready";
    public const string TournamentMatchDecided = "multiplayer.tournament.match_decided";
    public const string TournamentCompleted = "multiplayer.tournament.completed";
    public const string TournamentCancelled = "multiplayer.tournament.cancelled";
}
