using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Auth.Models;

public class RegisterRequest
{
    [Required, MinLength(3), MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Content language, from GET /api/languages. Stored on the user at creation and
    /// carried in the access token, so every later content call is already language-scoped.
    /// </summary>
    [Required]
    public Guid? LanguageId { get; set; }
}
