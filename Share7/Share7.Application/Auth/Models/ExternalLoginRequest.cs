using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Auth.Models;

public static class ExternalAuthProvider
{
    public const string Google = "Google";
    public const string Facebook = "Facebook";
}

public class ExternalLoginRequest
{
    /// One of <see cref="ExternalAuthProvider"/>.
    [Required]
    public string Provider { get; set; } = string.Empty;

    /// Google: ID token from the native Google Sign-In SDK.
    /// Facebook: access token from the native Facebook SDK.
    [Required]
    public string Token { get; set; } = string.Empty;
}
