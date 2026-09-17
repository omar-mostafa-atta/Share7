namespace Share7.Application.Auth.Models;

public class CompleteProfileResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static CompleteProfileResult Success() => new() { Succeeded = true };

    public static CompleteProfileResult Failure(params string[] errors) => new()
    {
        Succeeded = false,
        Errors = errors
    };
}
