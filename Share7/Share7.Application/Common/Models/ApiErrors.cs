using System.Reflection;

namespace Share7.Application.Common.Models;

/// <summary>
/// Every machine code the commerce and account endpoints can return.
/// <para>
/// These are contract. Renaming one is a breaking change for the Unity client — add a new code
/// rather than repurposing an existing one.
/// </para>
/// </summary>
public static class ApiErrors
{
    private static IReadOnlyList<ApiErrorCode>? _all;

    /// <summary>
    /// Every code declared here, for looking one up by its stored <c>messageKey</c> — a replayed
    /// purchase has to report the reason it was refused with the first time, and only the key was
    /// written down.
    /// <para>
    /// Reflected over the fields rather than hand-listed, so a new code cannot be forgotten here.
    /// **Built on first use, not in a field initializer**: static initializers run in declaration
    /// order, so reflecting eagerly from up here would read every field below while it was still
    /// null and hand back a list of nulls.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ApiErrorCode> All => _all ??= typeof(ApiErrors)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(ApiErrorCode))
        .Select(field => (ApiErrorCode)field.GetValue(null)!)
        .ToList();

    // ---- account -------------------------------------------------------------------------

    public static readonly ApiErrorCode AccountDeletionRefused =
        new("ACCOUNT_DELETION_REFUSED", "account.deletion.blocked");

    public static readonly ApiErrorCode ProfileNotFound =
        new("PROFILE_NOT_FOUND", "account.profile.not_found");

    /// <summary>
    /// The authenticated account no longer exists. Distinct from <see cref="ProfileNotFound"/>: the
    /// profile row is optional and its absence is ordinary, whereas this means the account itself is
    /// gone and the token outliving it is the only reason the request arrived at all.
    /// </summary>
    public static readonly ApiErrorCode AccountNotFound =
        new("ACCOUNT_NOT_FOUND", "account.not_found");

    // ---- currency ------------------------------------------------------------------------

    public static readonly ApiErrorCode CurrencyNotFound =
        new("CURRENCY_NOT_FOUND", "commerce.currency.not_found");

    public static readonly ApiErrorCode CurrencyDisabled =
        new("CURRENCY_DISABLED", "commerce.currency.disabled");

    public static readonly ApiErrorCode CurrencyKeyTaken =
        new("CURRENCY_KEY_TAKEN", "commerce.currency.key_taken");

    public static readonly ApiErrorCode InvalidAmount =
        new("INVALID_AMOUNT", "commerce.currency.invalid_amount");

    public static readonly ApiErrorCode InsufficientBalance =
        new("INSUFFICIENT_BALANCE", "commerce.insufficient_balance");

    // ---- rewards -------------------------------------------------------------------------

    public static readonly ApiErrorCode RewardRuleNotFound =
        new("REWARD_RULE_NOT_FOUND", "rewards.rule.not_found");

    /// <summary>
    /// The rule as authored could never pay correctly — an unknown event type, a limit that its
    /// repeat policy ignores, a currency listed twice. Rejected at authoring time because a rule
    /// that silently never fires is far harder to notice than a refused request.
    /// </summary>
    public static readonly ApiErrorCode RewardRuleInvalid =
        new("REWARD_RULE_INVALID", "rewards.rule.invalid");

    // ---- product kinds ---------------------------------------------------------------------

    public static readonly ApiErrorCode ProductKindNotFound =
        new("PRODUCT_KIND_NOT_FOUND", "commerce.product_kind.not_found");

    public static readonly ApiErrorCode ProductKindNameTaken =
        new("PRODUCT_KIND_NAME_TAKEN", "commerce.product_kind.name_taken");

    public static readonly ApiErrorCode ProductKindInvalid =
        new("PRODUCT_KIND_INVALID", "commerce.product_kind.invalid");

    /// <summary>
    /// Products still use this kind, so deleting it would leave them with nothing telling the
    /// client how to read their grants. Re-categorise them first.
    /// </summary>
    public static readonly ApiErrorCode ProductKindInUse =
        new("PRODUCT_KIND_IN_USE", "commerce.product_kind.in_use");

    // ---- products / entitlements -----------------------------------------------------------

    public static readonly ApiErrorCode ProductNotFound =
        new("PRODUCT_NOT_FOUND", "commerce.product.not_found");

    public static readonly ApiErrorCode ProductInactive =
        new("PRODUCT_INACTIVE", "commerce.product.inactive");

    public static readonly ApiErrorCode ProductKeyTaken =
        new("PRODUCT_KEY_TAKEN", "commerce.product.key_taken");

    public static readonly ApiErrorCode ProductInvalid =
        new("PRODUCT_INVALID", "commerce.product.invalid");

    /// <summary>
    /// The grant set cannot be edited because accounts already own the product — changing it would
    /// silently change what they own. Author a replacement product instead.
    /// </summary>
    public static readonly ApiErrorCode ProductGrantsLocked =
        new("PRODUCT_GRANTS_LOCKED", "commerce.product.grants_locked");

    /// <summary>
    /// The product cannot be deleted because accounts own it. Their entitlements resolve what they
    /// own by reading through to it, so deleting it would strand them. Retire it instead.
    /// </summary>
    public static readonly ApiErrorCode ProductOwned =
        new("PRODUCT_OWNED", "commerce.product.owned");

    public static readonly ApiErrorCode ProductGrantNotFound =
        new("PRODUCT_GRANT_NOT_FOUND", "commerce.product_grant.not_found");

    public static readonly ApiErrorCode ProductGrantInvalid =
        new("PRODUCT_GRANT_INVALID", "commerce.product_grant.invalid");

    /// <summary>The product already grants that reference. Change the quantity instead.</summary>
    public static readonly ApiErrorCode ProductGrantReferenceTaken =
        new("PRODUCT_GRANT_REFERENCE_TAKEN", "commerce.product_grant.reference_taken");

    // ---- offers / purchase ---------------------------------------------------------------

    public static readonly ApiErrorCode OfferNotFound =
        new("OFFER_NOT_FOUND", "commerce.offer.not_found");

    public static readonly ApiErrorCode OfferUnavailable =
        new("OFFER_UNAVAILABLE", "commerce.offer.unavailable");

    public static readonly ApiErrorCode OfferExpired =
        new("OFFER_EXPIRED", "commerce.offer.expired");

    public static readonly ApiErrorCode OfferSoldOut =
        new("OFFER_SOLD_OUT", "commerce.offer.sold_out");

    public static readonly ApiErrorCode PurchaseLimitReached =
        new("PURCHASE_LIMIT_REACHED", "commerce.offer.purchase_limit_reached");

    public static readonly ApiErrorCode NotEligible =
        new("NOT_ELIGIBLE", "commerce.offer.not_eligible");

    public static readonly ApiErrorCode GradeRestricted =
        new("GRADE_RESTRICTED", "commerce.offer.grade_restricted");

    public static readonly ApiErrorCode AlreadyOwned =
        new("ALREADY_OWNED", "commerce.offer.already_owned");

    public static readonly ApiErrorCode RequestIdRequired =
        new("REQUEST_ID_REQUIRED", "commerce.purchase.request_id_required");

    /// <summary>The offer as authored could never be sold — no products, a negative price, an
    /// original price below the sale price.</summary>
    public static readonly ApiErrorCode OfferInvalid =
        new("OFFER_INVALID", "commerce.offer.invalid");

    /// <summary>
    /// The offer has completed purchases, so deleting it would strand the transactions that point
    /// at it. Switch it to <c>UNAVAILABLE</c> instead.
    /// </summary>
    public static readonly ApiErrorCode OfferPurchased =
        new("OFFER_PURCHASED", "commerce.offer.purchased");

    // ---- equipment -------------------------------------------------------------------------

    /// <summary>
    /// The outfit breaks a structural limit — too many entries, an over-long or badly-formed key,
    /// or the same slot named twice. <c>details</c> names the offending field and value.
    /// <para>
    /// Never returned for an *unknown* key. There is no backend cosmetic catalogue by decision, so
    /// keys the server has never seen are stored verbatim; rejecting them would stop content
    /// shipping ahead of a backend deploy.
    /// </para>
    /// </summary>
    public static readonly ApiErrorCode EquipmentInvalid =
        new("EQUIPMENT_INVALID", "equipment.invalid");

    /// <summary>
    /// The account does not own a cosmetic it tried to equip. Only raised while ownership
    /// enforcement is switched on — see <c>EquipmentOptions.EnforceOwnership</c>.
    /// </summary>
    public static readonly ApiErrorCode EquipmentNotOwned =
        new("EQUIPMENT_NOT_OWNED", "equipment.not_owned");

    // ---- multiplayer -----------------------------------------------------------------------

    /// <summary>
    /// No such session — or the caller is not a member of it. **The two are deliberately answered
    /// identically.** A 403 for a session that exists would let anyone holding a guessed id learn
    /// which ids are live, so a non-member is told exactly what a stranger is told.
    /// </summary>
    public static readonly ApiErrorCode SessionNotFound =
        new("SESSION_NOT_FOUND", "multiplayer.session.not_found");

    /// <summary>Every seat is taken. Retryable — a seat may free up, so the request id is not spent.</summary>
    public static readonly ApiErrorCode SessionFull =
        new("SESSION_FULL", "multiplayer.session.full");

    /// <summary>The session has ended, or has moved past the point where joins are accepted.</summary>
    public static readonly ApiErrorCode SessionClosed =
        new("SESSION_CLOSED", "multiplayer.session.closed");

    /// <summary>
    /// The caller already holds a live membership somewhere. One account plays one match at a time —
    /// enforced by a filtered unique index, so it holds even when two joins arrive together.
    /// </summary>
    public static readonly ApiErrorCode AlreadyInSession =
        new("ALREADY_IN_SESSION", "multiplayer.session.already_in_session");

    public static readonly ApiErrorCode NotSessionMember =
        new("NOT_SESSION_MEMBER", "multiplayer.session.not_member");

    /// <summary>
    /// The caller is not the host. Also what a *former* host receives after migration, which is the
    /// mechanism that stops a returning stale host from restarting or closing a session it lost.
    /// </summary>
    public static readonly ApiErrorCode NotSessionHost =
        new("NOT_SESSION_HOST", "multiplayer.session.not_host");

    /// <summary>The move is not legal from the state the session is actually in. Nothing was mutated.</summary>
    public static readonly ApiErrorCode SessionInvalidTransition =
        new("SESSION_INVALID_TRANSITION", "multiplayer.session.invalid_transition");

    public static readonly ApiErrorCode SessionBelowMinPlayers =
        new("SESSION_BELOW_MIN_PLAYERS", "multiplayer.session.below_min_players");

    /// <summary>
    /// Another live session already holds that transport room name. Raised from the unique-index
    /// violation rather than from a lookup, so two simultaneous creates cannot both pass.
    /// </summary>
    public static readonly ApiErrorCode TransportNameTaken =
        new("TRANSPORT_NAME_TAKEN", "multiplayer.session.transport_name_taken");

    /// <summary>
    /// A host claim refused because the current host is still within its grace period. The claimant
    /// should wait rather than retry immediately.
    /// </summary>
    public static readonly ApiErrorCode HostStillActive =
        new("HOST_STILL_ACTIVE", "multiplayer.session.host_still_active");

    /// <summary>
    /// The client's realtime contract version is not one this server currently seats. **Not the app
    /// version** — see <c>MultiplayerOptions.AcceptedProtocolVersions</c>.
    /// </summary>
    public static readonly ApiErrorCode ProtocolVersionMismatch =
        new("PROTOCOL_VERSION_MISMATCH", "multiplayer.protocol_version_mismatch");

    /// <summary>
    /// The host removed the caller from this session, and they may not take a seat in it again. Only
    /// ever returned to the removed account itself, on an attempt to rejoin.
    /// </summary>
    public static readonly ApiErrorCode SessionRemoved =
        new("SESSION_REMOVED", "multiplayer.session.removed");

    /// <summary>
    /// The session holds its places for named players — a rematch, for the players of the match
    /// before — and the caller is not one of them.
    /// </summary>
    public static readonly ApiErrorCode SessionReserved =
        new("SESSION_RESERVED", "multiplayer.session.reserved");

    /// <summary>
    /// The event-feed cursor is older than retention, so events after it may be gone. Re-read state
    /// (sessions, invites) and continue from <c>details.latest</c>.
    /// </summary>
    public static readonly ApiErrorCode EventsCursorExpired =
        new("EVENTS_CURSOR_EXPIRED", "multiplayer.events.cursor_expired");

    /// <summary>
    /// The caller may not do this with that player — they share no class and are not friends, or one
    /// has blocked the other. **Deliberately one code for all of it**, so a block can never be told
    /// apart from "not connected".
    /// </summary>
    public static readonly ApiErrorCode SocialNotAllowed =
        new("SOCIAL_NOT_ALLOWED", "social.not_allowed");

    /// <summary>No such invite addressed to (or sent by) the caller.</summary>
    public static readonly ApiErrorCode InviteNotFound =
        new("INVITE_NOT_FOUND", "multiplayer.invite.not_found");

    /// <summary>The invite was already answered, withdrawn, or has expired. <c>details.state</c> says which.</summary>
    public static readonly ApiErrorCode InviteNotPending =
        new("INVITE_NOT_PENDING", "multiplayer.invite.not_pending");

    /// <summary>
    /// The caller may not have friends yet: under 18 (or of unknown age) without a guardian's
    /// <c>SocialPlay</c> consent. Classmates still work — they need no consent.
    /// </summary>
    public static readonly ApiErrorCode SocialConsentRequired =
        new("SOCIAL_CONSENT_REQUIRED", "social.consent_required");

    /// <summary>
    /// No friend code opens to a friend request. Unknown, rotated, a player who cannot have friends,
    /// a block either way — all the same answer, so a code tells a guesser nothing.
    /// </summary>
    public static readonly ApiErrorCode FriendCodeNotFound =
        new("FRIEND_CODE_NOT_FOUND", "social.friend_code.not_found");

    public static readonly ApiErrorCode FriendRequestNotFound =
        new("FRIEND_REQUEST_NOT_FOUND", "social.friend_request.not_found");

    public static readonly ApiErrorCode FriendRequestNotPending =
        new("FRIEND_REQUEST_NOT_PENDING", "social.friend_request.not_pending");

    public static readonly ApiErrorCode PartyNotFound =
        new("PARTY_NOT_FOUND", "multiplayer.party.not_found");

    public static readonly ApiErrorCode PartyFull =
        new("PARTY_FULL", "multiplayer.party.full");

    public static readonly ApiErrorCode NotPartyLeader =
        new("NOT_PARTY_LEADER", "multiplayer.party.not_leader");

    /// <summary>A member is already in a room, so the party cannot start one. <c>details.userIds</c> says who.</summary>
    public static readonly ApiErrorCode PartyMemberBusy =
        new("PARTY_MEMBER_BUSY", "multiplayer.party.member_busy");

    public static readonly ApiErrorCode TicketNotFound =
        new("TICKET_NOT_FOUND", "multiplayer.ticket.not_found");

    /// <summary>The ticket already matched (leave the session instead), or ended.</summary>
    public static readonly ApiErrorCode TicketNotSearching =
        new("TICKET_NOT_SEARCHING", "multiplayer.ticket.not_searching");

    /// <summary>The mode is not offered as ranked.</summary>
    public static readonly ApiErrorCode ModeNotRanked =
        new("MODE_NOT_RANKED", "multiplayer.ranked.mode_not_ranked");

    /// <summary>Ranked is queued alone: a party in a ranked match is the easiest way to boost a friend.</summary>
    public static readonly ApiErrorCode RankedSoloOnly =
        new("RANKED_SOLO_ONLY", "multiplayer.ranked.solo_only");

    public static readonly ApiErrorCode ChallengeNotFound =
        new("CHALLENGE_NOT_FOUND", "multiplayer.challenge.not_found");

    /// <summary>The challenge was already answered, withdrawn, decided, or has run out. <c>details.state</c> says which.</summary>
    public static readonly ApiErrorCode ChallengeNotOpen =
        new("CHALLENGE_NOT_OPEN", "multiplayer.challenge.not_open");

    /// <summary>The challenger has no graded score on that lesson in that game to set as the bar. Play it first.</summary>
    public static readonly ApiErrorCode ChallengeNoScore =
        new("CHALLENGE_NO_SCORE", "multiplayer.challenge.no_score");

    /// <summary>The recipient cannot play that lesson yet, so the challenge could not be won.</summary>
    public static readonly ApiErrorCode ChallengeLessonLocked =
        new("CHALLENGE_LESSON_LOCKED", "multiplayer.challenge.lesson_locked");

    /// <summary>No such tournament, or one the caller cannot see (another class's).</summary>
    public static readonly ApiErrorCode TournamentNotFound =
        new("TOURNAMENT_NOT_FOUND", "multiplayer.tournament.not_found");

    /// <summary>Entries are closed: it has started, finished or been called off.</summary>
    public static readonly ApiErrorCode TournamentRegistrationClosed =
        new("TOURNAMENT_REGISTRATION_CLOSED", "multiplayer.tournament.registration_closed");

    public static readonly ApiErrorCode TournamentFull =
        new("TOURNAMENT_FULL", "multiplayer.tournament.full");

    /// <summary>A classroom tournament, and the caller is not a learner in that class.</summary>
    public static readonly ApiErrorCode TournamentNotEligible =
        new("TOURNAMENT_NOT_ELIGIBLE", "multiplayer.tournament.not_eligible");

    /// <summary>The fixed lesson every match plays is not unlocked for the caller yet.</summary>
    public static readonly ApiErrorCode TournamentLessonLocked =
        new("TOURNAMENT_LESSON_LOCKED", "multiplayer.tournament.lesson_locked");

    public static readonly ApiErrorCode TournamentNotEntered =
        new("TOURNAMENT_NOT_ENTERED", "multiplayer.tournament.not_entered");

    /// <summary>Only its organiser — the class's teacher, or an operator — may do that.</summary>
    public static readonly ApiErrorCode TournamentNotOrganiser =
        new("TOURNAMENT_NOT_ORGANISER", "multiplayer.tournament.not_organiser");

    /// <summary>The tournament is not in a state that allows this — starting one that is running, playing in one that has not started.</summary>
    public static readonly ApiErrorCode TournamentInvalidState =
        new("TOURNAMENT_INVALID_STATE", "multiplayer.tournament.invalid_state");

    /// <summary>The mode cannot host a tournament: not played versus by two, or no win rule to decide a match.</summary>
    public static readonly ApiErrorCode TournamentModeUnsuitable =
        new("TOURNAMENT_MODE_UNSUITABLE", "multiplayer.tournament.mode_unsuitable");

    /// <summary>The event cannot host a tournament: called off, closed, limited per entry, or too short for the bracket.</summary>
    public static readonly ApiErrorCode TournamentEventUnsuitable =
        new("TOURNAMENT_EVENT_UNSUITABLE", "multiplayer.tournament.event_unsuitable");

    public static readonly ApiErrorCode TournamentMatchNotFound =
        new("TOURNAMENT_MATCH_NOT_FOUND", "multiplayer.tournament.match_not_found");

    /// <summary>The pairing is decided, or its deadline has passed.</summary>
    public static readonly ApiErrorCode TournamentMatchClosed =
        new("TOURNAMENT_MATCH_CLOSED", "multiplayer.tournament.match_closed");

    /// <summary>The game exists but is not flagged <c>SupportsMultiplayer</c> in the catalog.</summary>
    public static readonly ApiErrorCode GameNotMultiplayer =
        new("GAME_NOT_MULTIPLAYER", "multiplayer.game.not_multiplayer");

    public static readonly ApiErrorCode GameNotFound =
        new("GAME_NOT_FOUND", "multiplayer.game.not_found");

    // ---- leaderboards ----------------------------------------------------------------------

    public static readonly ApiErrorCode LeaderboardBoardNotFound =
        new("LB_BOARD_NOT_FOUND", "leaderboard.board.not_found");

    public static readonly ApiErrorCode LeaderboardCycleNotFound =
        new("LB_CYCLE_NOT_FOUND", "leaderboard.cycle.not_found");

    /// <summary>The board does not offer that cohort at all.</summary>
    public static readonly ApiErrorCode LeaderboardCohortUnsupported =
        new("LB_COHORT_UNSUPPORTED", "leaderboard.cohort.unsupported");

    /// <summary>
    /// The board offers the cohort but this caller has no membership in it — no grade on their
    /// profile, for instance.
    /// <para>
    /// Deliberately distinct from an empty page. "You are not in a class yet" and "your class
    /// board has nobody on it" are different things to a child, and a client that cannot tell them
    /// apart will show the wrong one.
    /// </para>
    /// </summary>
    public static readonly ApiErrorCode LeaderboardCohortUnavailable =
        new("LB_COHORT_UNAVAILABLE", "leaderboard.cohort.unavailable");

    /// <summary>Malformed, expired, or tampered-with paging cursor.</summary>
    public static readonly ApiErrorCode LeaderboardCursorInvalid =
        new("LB_CURSOR_INVALID", "leaderboard.cursor.invalid");

    public static readonly ApiErrorCode LeaderboardLimitExceeded =
        new("LB_LIMIT_EXCEEDED", "leaderboard.limit.exceeded");

    /// <summary>Paging deeper than the caller's entitlement allows.</summary>
    public static readonly ApiErrorCode LeaderboardRankLimit =
        new("LB_RANK_LIMIT", "leaderboard.rank.limit");

    /// <summary>
    /// A board as authored could never rank anything — an unknown metric, a cohort the schema
    /// cannot resolve, a key that would overflow a reward rule's reference. Refused at authoring
    /// time, because a board that silently stays empty is far harder to notice than a refusal.
    /// </summary>
    public static readonly ApiErrorCode LeaderboardBoardInvalid =
        new("LB_BOARD_INVALID", "leaderboard.board.invalid");

    public static readonly ApiErrorCode LeaderboardBoardKeyTaken =
        new("LB_BOARD_KEY_TAKEN", "leaderboard.board.key_taken");

    /// <summary>Leaderboards are switched off for this deployment.</summary>
    public static readonly ApiErrorCode LeaderboardDisabled =
        new("LB_DISABLED", "leaderboard.disabled");

    // ---- runs ------------------------------------------------------------------------------

    /// <summary>No run with that id belongs to the caller — including one that was never started.</summary>
    public static readonly ApiErrorCode RunNotFound =
        new("RUN_NOT_FOUND", "runs.run.not_found");

    /// <summary>
    /// The run was held open past its expiry and can no longer settle. Terminal: the client should
    /// drop the queued result rather than keep retrying it forever.
    /// </summary>
    public static readonly ApiErrorCode RunExpired =
        new("RUN_EXPIRED", "runs.run.expired");

    /// <summary>
    /// The run is in a state that cannot settle and is not simply a replay — a settled run returns
    /// its settlement rather than this.
    /// </summary>
    public static readonly ApiErrorCode RunNotOpen =
        new("RUN_NOT_OPEN", "runs.run.not_open");

    /// <summary>
    /// The idempotency key has already been spent on a **different** run. Refused rather than paid:
    /// one key, one operation, or a retry pays for a run it did not belong to.
    /// </summary>
    public static readonly ApiErrorCode RunRequestIdReused =
        new("RUN_REQUEST_ID_REUSED", "runs.run.request_id_reused");

    /// <summary>
    /// The claim is **impossible against the run's own seeded layout** — more of a kind than the track
    /// contained, a pickup that was never spawned, or the same one twice.
    /// <para>
    /// The one refusal in the run feature that is not a state error. Everything else caps and pays,
    /// because everything else is probabilistic; this compares a claim against a layout the server
    /// generated, so it is not a judgement about likelihood.
    /// </para>
    /// </summary>
    public static readonly ApiErrorCode RunRejected =
        new("RUN_REJECTED", "runs.run.rejected");

    /// <summary>No valuation row with that id.</summary>
    public static readonly ApiErrorCode ValuationNotFound =
        new("VALUATION_NOT_FOUND", "runs.valuation.not_found");

    /// <summary>
    /// The valuation could never price safely as written — an illegal kind token, a negative value, a
    /// missing per-run bound, or a **hard currency without a daily cap**. Refused at creation because a
    /// missing bound discovered later is currency already in circulation.
    /// </summary>
    public static readonly ApiErrorCode ValuationInvalid =
        new("VALUATION_INVALID", "runs.valuation.invalid");

    /// <summary>That game already prices that kind in that currency. Update the row instead.</summary>
    public static readonly ApiErrorCode ValuationDuplicate =
        new("VALUATION_DUPLICATE", "runs.valuation.duplicate");

    /// <summary>The game exists but is retired, so no new run may be opened against it.</summary>
    public static readonly ApiErrorCode GameInactive =
        new("GAME_INACTIVE", "games.game.inactive");

    // ---- play context: modes, contexts, worlds and events ----------------------------------

    /// <summary>The <c>modeKey</c> names no row. Never defaulted silently — a default here would
    /// price every run of an unknown mode as Classic and nobody would find out.</summary>
    public static readonly ApiErrorCode PlayModeUnknown =
        new("PC_MODE_UNKNOWN", "play.mode.unknown");

    /// <summary>Withdrawn by an operator, or outside its authored window.</summary>
    public static readonly ApiErrorCode PlayModeInactive =
        new("PC_MODE_INACTIVE", "play.mode.inactive");

    /// <summary>The mode is sold, and this account does not own it.</summary>
    public static readonly ApiErrorCode PlayModeNotEntitled =
        new("PC_MODE_NOT_ENTITLED", "play.mode.not_entitled");

    /// <summary>The player's grade is below the mode's <c>minGradeOrder</c>.</summary>
    public static readonly ApiErrorCode PlayModeGradeGated =
        new("PC_MODE_GRADE_GATED", "play.mode.grade_gated");

    /// <summary>The mode belongs to another game than the one the session names.</summary>
    public static readonly ApiErrorCode PlayModeWrongGame =
        new("PC_MODE_WRONG_GAME", "play.mode.wrong_game");

    /// <summary>Unknown <c>contextKey</c>, an <c>event</c> context with no event, or an event id
    /// sent with a context that is not an event.</summary>
    public static readonly ApiErrorCode PlayContextInvalid =
        new("PC_CONTEXT_INVALID", "play.context.invalid");

    /// <summary>The player count is outside what the mode's topologies allow.</summary>
    public static readonly ApiErrorCode PlayTopologyMismatch =
        new("PC_TOPOLOGY_MISMATCH", "play.topology.mismatch");

    public static readonly ApiErrorCode PlayEventUnknown =
        new("PC_EVENT_UNKNOWN", "play.event.unknown");

    /// <summary>The event's cycle is not open — it has not started, or it has finished.</summary>
    public static readonly ApiErrorCode PlayEventClosed =
        new("PC_EVENT_CLOSED", "play.event.closed");

    /// <summary>The event is running, but this account may not enter it: grade, level or entitlement.</summary>
    public static readonly ApiErrorCode PlayEventNotEligible =
        new("PC_EVENT_NOT_ELIGIBLE", "play.event.not_eligible");

    /// <summary>The account has already played this event as many times today as its rules allow.</summary>
    public static readonly ApiErrorCode PlayEventEntryLimit =
        new("PC_EVENT_ENTRY_LIMIT", "play.event.entry_limit");

    /// <summary>The session names a mode the event does not run.</summary>
    public static readonly ApiErrorCode PlayEventModeMismatch =
        new("PC_EVENT_MODE_MISMATCH", "play.event.mode_mismatch");

    /// <summary>The event could not be authored as asked — bad window, prize table or rules.</summary>
    public static readonly ApiErrorCode PlayEventInvalid =
        new("PC_EVENT_INVALID", "play.event.invalid");

    /// <summary>The mode could not be authored as asked. <c>details</c> carries what was wrong.</summary>
    public static readonly ApiErrorCode PlayModeInvalid =
        new("PC_MODE_INVALID", "play.mode.invalid");

    /// <summary>Another mode already uses that key. Keys are immutable, so this is never a rename.</summary>
    public static readonly ApiErrorCode PlayModeKeyTaken =
        new("PC_MODE_KEY_TAKEN", "play.mode.key_taken");

    /// <summary>The world names no row for this game, or the mode may not run in it.</summary>
    public static readonly ApiErrorCode PlayWorldUnknown =
        new("PC_WORLD_UNKNOWN", "play.world.unknown");

    /// <summary>The world exists but this account has not unlocked it.</summary>
    public static readonly ApiErrorCode PlayWorldLocked =
        new("PC_WORLD_LOCKED", "play.world.locked");

    /// <summary>The world could not be authored as asked.</summary>
    public static readonly ApiErrorCode PlayWorldInvalid =
        new("PC_WORLD_INVALID", "play.world.invalid");

    /// <summary>
    /// The players in a session have no lesson in common in the subject they chose — nothing both
    /// has unlocked with questions in the language each is playing.
    /// </summary>
    public static readonly ApiErrorCode PlayNoSharedLesson =
        new("PC_NO_SHARED_LESSON", "play.lesson.none_shared");

    /// <summary>No prize claim with that id belongs to this account.</summary>
    public static readonly ApiErrorCode PlayPrizeClaimNotFound =
        new("PC_CLAIM_NOT_FOUND", "play.claim.not_found");

    /// <summary>The claim cannot move to that state from the one it is in.</summary>
    public static readonly ApiErrorCode PlayPrizeClaimInvalidTransition =
        new("PC_CLAIM_INVALID_TRANSITION", "play.claim.invalid_transition");

    // ---- generic -------------------------------------------------------------------------

    public static readonly ApiErrorCode NotFound =
        new("NOT_FOUND", "common.not_found");

    public static readonly ApiErrorCode ValidationFailed =
        new("VALIDATION_FAILED", "common.validation_failed");

    public static readonly ApiErrorCode Forbidden =
        new("FORBIDDEN", "common.forbidden");

    /// <summary>
    /// The caller sent more requests than a rate limit allows. Carries <c>retryAfterSeconds</c> in
    /// <c>details</c>, mirroring the <c>Retry-After</c> header, so a client that only parses the
    /// body still knows how long to back off.
    /// <para>
    /// Returned by middleware rather than by a service, so it never travels inside a
    /// <c>ServiceResult</c> — it is listed here because it shares the envelope and the Unity
    /// client resolves every refusal through this table.
    /// </para>
    /// </summary>
    public static readonly ApiErrorCode RateLimited =
        new("RATE_LIMITED", "common.rate_limited");
}
