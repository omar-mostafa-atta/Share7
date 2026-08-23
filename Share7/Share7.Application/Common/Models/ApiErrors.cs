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
