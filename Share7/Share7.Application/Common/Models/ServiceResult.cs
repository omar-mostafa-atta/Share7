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
    Forbidden
}

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public ServiceErrorKind ErrorKind { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ServiceResult Success() => new() { Succeeded = true };

    public static ServiceResult NotFound(string message) =>
        new() { ErrorKind = ServiceErrorKind.NotFound, Errors = [message] };

    public static ServiceResult Conflict(string message) =>
        new() { ErrorKind = ServiceErrorKind.Conflict, Errors = [message] };

    public static ServiceResult Invalid(params string[] messages) =>
        new() { ErrorKind = ServiceErrorKind.Validation, Errors = messages };

    public static ServiceResult Forbidden(string message) =>
        new() { ErrorKind = ServiceErrorKind.Forbidden, Errors = [message] };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; init; }

    public static ServiceResult<T> Success(T value) => new() { Succeeded = true, Value = value };

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
}
