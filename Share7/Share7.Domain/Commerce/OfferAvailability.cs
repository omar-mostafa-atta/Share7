namespace Share7.Domain.Commerce;

/// <summary>
/// Whether an admin has this offer switched on. **Two values on purpose** — every other reason an
/// offer cannot be bought is derived at read time rather than stored, because it depends on when you
/// ask and who is asking.
/// <para>
/// Not to be confused with the token the client receives. That vocabulary is wider
/// (<c>AVAILABLE</c>, <c>DISABLED</c>, <c>EXPIRED</c>, <c>PURCHASE_LIMIT_REACHED</c>, …) and is
/// resolved per request from this flag plus the clock and the caller's purchase history.
/// </para>
/// </summary>
public enum OfferAvailability
{
    Unknown = 0,

    /// <summary>On sale, subject to expiry and per-account limits.</summary>
    Available,

    /// <summary>
    /// Switched off. Still listed, so the client can grey it out rather than have entries vanish,
    /// and reported to the client as <c>DISABLED</c>.
    /// </summary>
    Unavailable
}
