using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Auth.Models;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
