using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Auth.Models;

public class CompleteProfileRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Range(3, 100)]
    public int Age { get; set; }

    [Required, Phone]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// Must be a grade from the user's own language tree — see
    /// <c>GET /api/grades</c>, which is already filtered by their preferred language.
    /// </summary>
    [Required]
    public Guid? GradeId { get; set; }
}
