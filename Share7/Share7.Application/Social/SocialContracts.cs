using Share7.Application.Common.Models;

namespace Share7.Application.Social;

/// <summary>What one player is trying to do with another.</summary>
public enum SocialAction
{
    Invite = 1,
    Challenge = 2,
    SeePresence = 3,

    /// <summary>Never allowed today — see <see cref="ISocialPolicy"/>.</summary>
    SeeRealName = 4,
    SeeProfile = 5,
    SeeStatistics = 6
}

/// <summary>How two players are connected, when they are.</summary>
public enum SocialRelation
{
    /// <summary>Both are learners in the same active class of an active organization.</summary>
    Classmate = 1,

    /// <summary>Both chose it — see <see cref="IFriendGraph"/>.</summary>
    Friend = 2
}

/// <summary>
/// The answer to "may this player do that with that player". <see cref="Reason"/> is for logs and
/// tests only; the wire gets one code (<c>SOCIAL_NOT_ALLOWED</c>) whatever it says.
/// </summary>
public sealed record SocialPermission(bool Allowed, string Reason)
{
    public static SocialPermission Allow(string reason) => new(true, reason);
    public static SocialPermission Deny(string reason) => new(false, reason);
}

public sealed record SocialConnection(Guid UserId, SocialRelation Relation);

/// <summary>
/// **The one place that decides who may reach whom.** Invites, challenges and presence all ask here,
/// so a change of rule — friends with guardian consent, a school turning cross-class play on — is a
/// change to one implementation rather than to every endpoint (<c>IRosterNameResolver</c> is the
/// pattern).
/// <para>
/// The rule today (MultiplayerPlatform.md §5.3, §15): two players may invite, challenge and see each
/// other's presence when they are **classmates** — learners in the same active class of an active
/// organization, which is an institutional decision to put them together — or **friends**. Never a
/// stranger met in a public match. A block in either direction overrides everything. Real names:
/// never.
/// </para>
/// </summary>
public interface ISocialPolicy
{
    Task<SocialPermission> CanInteractAsync(Guid actor, Guid target, SocialAction action, CancellationToken cancellationToken = default);

    /// <summary>Everyone <paramref name="actor"/> may currently <paramref name="action"/>, blocks removed.</summary>
    Task<IReadOnlyList<SocialConnection>> ConnectionsAsync(Guid actor, SocialAction action, CancellationToken cancellationToken = default);
}

public interface IBlockList
{
    Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken cancellationToken = default);

    /// <summary>Everyone <paramref name="user"/> blocked, and everyone who blocked them.</summary>
    Task<IReadOnlySet<Guid>> BlockedEitherWayAsync(Guid user, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who has chosen to be friends. Empty until friends-by-code ships; the seam exists now so the policy
/// is written against it once.
/// </summary>
public interface IFriendGraph
{
    Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> FriendsOfAsync(Guid user, CancellationToken cancellationToken = default);
}

/// <summary>Where a player is, as far as anyone allowed to know may know.</summary>
public enum PlayerPresenceState
{
    Offline = 0,

    /// <summary>The app is open — its event feed polled recently.</summary>
    Online = 1,

    /// <summary>Holds a seat in a room that has not started.</summary>
    InLobby = 2,

    /// <summary>Holds a seat in a match being played.</summary>
    InMatch = 3
}

/// <summary>
/// Presence, unfiltered. **Never exposed directly** — callers go through <see cref="ISocialPolicy"/>
/// first, so presence reaches only classmates and friends.
/// </summary>
public interface IPresenceReader
{
    Task<IReadOnlyDictionary<Guid, PlayerPresenceState>> GetAsync(IReadOnlyCollection<Guid> users, CancellationToken cancellationToken = default);

    /// <summary>Records that this player's client is alive. Cheap to call on every poll.</summary>
    Task TouchAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>One player the caller may play with, as the "play with…" list shows them.</summary>
public class ConnectionDto
{
    public Guid UserId { get; set; }

    /// <summary>The public handle. Never a real name.</summary>
    public string? DisplayName { get; set; }

    public SocialRelation Relation { get; set; }
    public PlayerPresenceState Presence { get; set; }
}

public class BlockedPlayerDto
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime BlockedAtUtc { get; set; }
}

public class BlockPlayerRequest
{
    public Guid UserId { get; set; }
}

/// <summary>The player-facing social surface: who I can play with, and who I have blocked.</summary>
public interface ISocialService
{
    Task<ServiceResult<IReadOnlyList<ConnectionDto>>> ConnectionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<BlockedPlayerDto>>> BlocksAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent, and succeeds for any id — whether the account exists is not something blocking may
    /// be used to find out. Withdraws every pending invite between the two, both ways.
    /// </summary>
    Task<ServiceResult> BlockAsync(Guid userId, Guid target, CancellationToken cancellationToken = default);

    Task<ServiceResult> UnblockAsync(Guid userId, Guid target, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a player may have friends at all: 18 or over, or a guardian granted <c>SocialPlay</c>.
/// Unknown age refuses — on a platform for children, not knowing is not a reason to treat someone as
/// an adult.
/// </summary>
public interface ISocialConsent
{
    Task<bool> MayHaveFriendsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The subset of <paramref name="users"/> who may.</summary>
    Task<IReadOnlySet<Guid>> WhoMayHaveFriendsAsync(IReadOnlyCollection<Guid> users, CancellationToken cancellationToken = default);
}

public class FriendCodeDto
{
    /// <summary>Eight characters, no I, O, 0 or 1, to read out or show. Never shown to anyone else by the server.</summary>
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class AddFriendRequest
{
    public string Code { get; set; } = string.Empty;
}

public class FriendRequestDto
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public string? FromDisplayName { get; set; }
    public Guid ToUserId { get; set; }
    public string? ToDisplayName { get; set; }
    public Domain.Social.FriendRequestState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Friends by code, and nothing else: no search, no suggestions, no "people you may know". Both sides
/// agree, both must be allowed friends (see <see cref="ISocialConsent"/>), and a block ends it.
/// </summary>
public interface IFriendService
{
    /// <summary>The caller's code, minted on first ask. <c>SOCIAL_CONSENT_REQUIRED</c> if they may not have friends.</summary>
    Task<ServiceResult<FriendCodeDto>> GetCodeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>A new code; the old one opens nothing from now on.</summary>
    Task<ServiceResult<FriendCodeDto>> RotateCodeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a friend request to whoever owns the code — or, if they had already asked the caller,
    /// makes the friendship. Idempotent. Every unusable code is <c>FRIEND_CODE_NOT_FOUND</c>.
    /// </summary>
    Task<ServiceResult<FriendRequestDto>> AddByCodeAsync(Guid userId, AddFriendRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requests waiting for the caller's answer.</summary>
    Task<ServiceResult<IReadOnlyList<FriendRequestDto>>> RequestsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ServiceResult<FriendRequestDto>> AcceptAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>The sender is not told.</summary>
    Task<ServiceResult<FriendRequestDto>> DeclineAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FriendRequestDto>> CancelAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Unfriends, both ways, silently.</summary>
    Task<ServiceResult> RemoveAsync(Guid userId, Guid friendUserId, CancellationToken cancellationToken = default);
}
