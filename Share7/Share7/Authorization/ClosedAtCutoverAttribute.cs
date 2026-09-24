using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Share7.API.Authorization;

/// <summary>
/// An old authoring endpoint that cutover closed (plan P6). The route is still mounted and still
/// answers, but it answers <c>410 Gone</c> and says where the work moved.
/// <para>
/// <b>Why a refusal rather than a deletion.</b> The point of the phase is that there is exactly one
/// way to author content and it goes through review — which a refusal satisfies as completely as a
/// removal, while leaving something for a caller to read. A deleted route answers 404, which is
/// indistinguishable from a typo. It also keeps the way back honest: if the pilot finds something
/// the Studio cannot yet do, re-opening a route is deleting one line rather than writing a
/// controller again from memory.
/// </para>
/// <para>
/// Only writes carry this. The reads beside them still serve the console pages and the operational
/// tools that did not move, and a read was never a second way to author.
/// </para>
/// </summary>
/// <para>
/// <b>An authorization filter, not an action filter.</b> Action filters run after model binding and
/// after <c>[ApiController]</c>'s automatic 400 — so a write whose body no longer matched anything
/// was answered "At least one translation is required" rather than "this moved", which is a worse
/// answer than the one it replaced. Authorization filters run before binding, so the refusal is the
/// first thing that happens and a rejected upload is never read off the wire at all.
/// </para>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class ClosedAtCutoverAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>
    /// What this endpoint used to do, named the way the content team would say it — "Adding a
    /// lesson", "Uploading a question sheet". It is read back to the caller, so it is a phrase
    /// that finishes "… is done in the Content Studio now."
    /// </summary>
    public string Was { get; }

    /// <summary>
    /// Which board in the Studio it went to, when naming it saves somebody a hunt — "the lesson's
    /// own board", "Second chances". Left unset when the Studio has no single home for it.
    /// </summary>
    public string? Now { get; set; }

    public ClosedAtCutoverAttribute(string was) => Was = was;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var board = Now is null ? string.Empty : $", on {Now}";

        // The `{ errors: [...] }` envelope, because that is what the curriculum and auth endpoints
        // have always answered with and what anything still calling this knows how to read.
        context.Result = new ObjectResult(new
        {
            errors = new[]
            {
                $"{Was} is done in the Content Studio now{board}. This way in closed when content "
                + "authoring moved, so that nothing reaches a student without a second person's review."
            }
        })
        {
            StatusCode = StatusCodes.Status410Gone
        };
    }
}
