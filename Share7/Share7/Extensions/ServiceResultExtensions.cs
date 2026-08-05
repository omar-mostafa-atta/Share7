using Microsoft.AspNetCore.Mvc;
using Share7.Application.Common.Models;

namespace Share7.API.Extensions;

public static class ServiceResultExtensions
{
    /// <summary>
    /// Maps a service failure onto the matching status code, so controllers don't each
    /// re-derive it from the error text.
    /// </summary>
    public static IActionResult ToErrorResult(this ServiceResult result) =>
        Build(result.ErrorKind, new { errors = result.Errors });

    /// <summary>
    /// Same mapping, but keeps any payload the failure carried — a refused delete reports
    /// what it would have removed under <c>details</c>.
    /// </summary>
    public static IActionResult ToErrorResult<T>(this ServiceResult<T> result) =>
        result.Value is null
            ? Build(result.ErrorKind, new { errors = result.Errors })
            : Build(result.ErrorKind, new { errors = result.Errors, details = result.Value });

    private static IActionResult Build(ServiceErrorKind kind, object body) => kind switch
    {
        ServiceErrorKind.NotFound => new NotFoundObjectResult(body),
        ServiceErrorKind.Conflict => new ConflictObjectResult(body),
        ServiceErrorKind.Forbidden => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
        _ => new BadRequestObjectResult(body)
    };
}
