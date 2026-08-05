namespace Share7.Application.Curriculum.Models;

public class SetPreferredLanguageRequest
{
    /// <summary>An id from GET /api/languages.</summary>
    public Guid LanguageId { get; set; }
}
