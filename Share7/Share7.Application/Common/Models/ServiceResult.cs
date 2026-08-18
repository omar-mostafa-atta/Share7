namespace Share7.Application.Common.Models;

/// <summary>
/// Why an operation failed, so controllers can pick the right status code without
/// string-matching error messages.
/// </summary>
public enum ServiceErrorKind
{
    None = 0,
    NotFound,
    Conflict,
    Validation,
    Forbidden,

    /// <summary>
    /// Well-formed request, but the content breaks a rule — maps to <c>422</c>.
    /// <para>
    /// Distinct from <see cref="Validation"/>, which maps to <c>400</c> and is what every older
    /// endpoint returns. Added for the equipment endpoint, whose contract specifies <c>422</c>;
    /// nothing that predates it uses this, so no existing response code moves.
    /// </para>
    /// </summary>
    Unprocessable
}

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public ServiceErrorKind ErrorKind { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Machine code + localization key, set only by the commerce and account endpoints.
    /// <para>
    /// Null on everything older. That is what keeps this additive: the auth and curriculum
    /// endpoints keep returning their existing <c>{ errors: [...] }</c> shape untouched, and only
    /// results that carry an <see cref="ApiErrorCode"/> can be rendered into the new envelope.
    /// </para>
    /// </summary>
    public ApiErrorCode? Error { get; init; }

    /// <summary>Optional structured metadata for the new envelope's <c>details</c>.</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    public static ServiceResult Success() => new() { Succeeded = true };

    /// <summary>
    /// A failure carrying a machine code. <paramref name="message"/> is for logs and Swagger
    /// only — <c>ToApiErrorResult</c> never puts it on the wire.
    /// </summary>
    public static ServiceResult Failure(
        ApiErrorCode error,
        ServiceErrorKind kind,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) => new()
    {
        ErrorKind = kind,
        Error = error,
        Errors = [message],
        Details = details
    };

    public static ServiceResult NotFound(string message) =>
        new() { ErrorKind = ServiceErrorKind.NotFound, Errors = [message] };

    public static ServiceResult Conflict(string message) =>
        new() { ErrorKind = ServiceErrorKind.Conflict, Errors = [message] };

    public static ServiceResult Invalid(params string[] messages) =>
        new() { ErrorKind = ServiceErrorKind.Validation, Errors = messages };

    public static ServiceResult Forbidden(string message) =>
        new() { ErrorKind = ServiceErrorKind.Forbidden, Errors = [message] };

    /// <summary>A well-formed request whose content breaks a rule — <c>422</c>.</summary>
    public static ServiceResult Unprocessable(
        ApiErrorCode error,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) =>
        Failure(error, ServiceErrorKind.Unprocessable, message, details);
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; init; }

    public static ServiceResult<T> Success(T value) => new() { Succeeded = true, Value = value };

    /// <inheritdoc cref="ServiceResult.Failure"/>
    public new static ServiceResult<T> Failure(
        ApiErrorCode error,
        ServiceErrorKind kind,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) => new()
    {
        ErrorKind = kind,
        Error = error,
        Errors = [message],
        Details = details
    };

    /// <summary>
    /// A refusal that still carries a payload — a purchase refused for insufficient balance
    /// still reports the authoritative balances, so the client can reconcile without a second
    /// round trip.
    /// </summary>
    public static ServiceResult<T> Failure(
        ApiErrorCode error,
        ServiceErrorKind kind,
        string message,
        T value,
        IReadOnlyDictionary<string, object?>? details = null) => new()
    {
        ErrorKind = kind,
        Error = error,
        Errors = [message],
        Details = details,
        Value = value
    };

    public new static ServiceResult<T> NotFound(string message) =>
        new() { ErrorKind = ServiceErrorKind.NotFound, Errors = [message] };

    public new static ServiceResult<T> Conflict(string message) =>
        new() { ErrorKind = ServiceErrorKind.Conflict, Errors = [message] };

    /// <summary>A conflict that still carries data — e.g. what a refused delete would have removed.</summary>
    public static ServiceResult<T> Conflict(string message, T value) =>
        new() { ErrorKind = ServiceErrorKind.Conflict, Errors = [message], Value = value };

    public new static ServiceResult<T> Invalid(params string[] messages) =>
        new() { ErrorKind = ServiceErrorKind.Validation, Errors = messages };

    public new static ServiceResult<T> Forbidden(string message) =>
        new() { ErrorKind = ServiceErrorKind.Forbidden, Errors = [message] };

    /// <inheritdoc cref="ServiceResult.Unprocessable"/>
    public new static ServiceResult<T> Unprocessable(
        ApiErrorCode error,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) =>
        Failure(error, ServiceErrorKind.Unprocessable, message, details);
}
