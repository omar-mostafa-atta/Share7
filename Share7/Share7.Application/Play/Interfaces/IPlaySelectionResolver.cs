using Share7.Application.Common.Models;
using Share7.Application.Play.Models;
using Share7.Domain.Play;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// Turns what a client asked to play into what the server will let it play, once.
/// <para>
/// <b>One implementation, three callers.</b> Starting a run, submitting an attempt and matchmaking
/// all have to agree about whether a mode is offered, whether an event is open, and what the pair is
/// worth. Three copies of those checks would diverge the first time one of them gained a rule the
/// others did not — and the divergence would be invisible, because each path would still look
/// correct on its own.
/// </para>
/// </summary>
public interface IPlaySelectionResolver
{
    /// <summary>
    /// Resolves and authorises a selection for one player, or refuses it with a <c>PC_*</c> code.
    /// <para>
    /// Refusals are about eligibility — unknown, withdrawn, gated, closed, exhausted. What a session
    /// is <i>worth</i> is never a refusal: a mode that pays nothing resolves successfully and pays
    /// nothing.
    /// </para>
    /// </summary>
    Task<ServiceResult<PlaySelection>> ResolveAsync(
        Guid userId, PlaySelectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the policy of a session that was already authorised, from what was stamped on it.
    /// <para>
    /// <b>Settlement must never re-authorise.</b> A run begun while a mode was offered has to settle
    /// under that mode even if an operator withdrew it, the event closed, or the child's grade
    /// changed while they were playing — re-running the eligibility checks at the end would take a
    /// finished run away from a child for something that happened after they started it. So this
    /// reads the rows and computes what they are worth, and refuses nothing.
    /// </para>
    /// </summary>
    Task<PlaySelection> DescribeAsync(
        Guid? modeId,
        PlayContextKind context,
        Guid? eventId,
        CancellationToken cancellationToken = default);
}
