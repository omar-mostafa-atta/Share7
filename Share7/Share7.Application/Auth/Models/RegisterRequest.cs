using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Auth.Models;

public class RegisterRequest
{
    [Required, MinLength(3), MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;


}
