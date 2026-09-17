using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Models;

namespace Share7.API.Extensions;

/// <summary>
/// The error envelope for commerce and account endpoints: <c>{ code, messageKey, details }</c>.
/// <para>
/// Deliberately separate from <see cref="ServiceResultExtensions.ToErrorResult(ServiceResult)"/>,
/// which keeps emitting <c>{ errors: ["sentence"] }</c> for auth and curriculum. Those endpoints
/// already have a shipping Unity client parsing them, so their contract does not move.
/// </para>
/// </summary>
public static class ApiErrorExtensions
{
    public static IActionResult ToApiErrorResult(this ServiceResult result) =>
        Build(result, payload: null);

    /// <summary>
    /// Same envelope, but a refusal that carried a value returns that value instead — the
    /// purchase endpoint answers a refusal with the full authoritative response body so the
    /// client can reconcile balances from the same round trip.
    /// </summary>
    public static IActionResult ToApiErrorResult<T>(this ServiceResult<T> result) =>
        result.Value is null ? Build(result, null) : Build(result, result.Value);

    private static IActionResult Build(ServiceResult result, object? payload)
    {
        var status = result.ErrorKind switch
        {
            ServiceErrorKind.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorKind.Conflict => StatusCodes.Status409Conflict,
            ServiceErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ServiceErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };

        // A result that reached here without a code is a bug in the calling service rather than
        // something the client should have to guess at, so it degrades to a generic code instead
        // of emitting a null one.
        var error = result.Error ?? ApiErrors.ValidationFailed;

        object body = payload ?? new
        {
            code = error.Code,
            messageKey = error.MessageKey,
            details = result.Details ?? new Dictionary<string, object?>()
        };

        return new ObjectResult(body) { StatusCode = status };
    }
}
