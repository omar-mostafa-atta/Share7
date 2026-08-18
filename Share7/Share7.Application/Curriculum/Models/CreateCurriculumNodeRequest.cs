using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Body for every "add a node under this parent" endpoint. The parent comes from the route.
/// <para>
/// A node carries no language of its own — it is one row shared by every language, with a
/// name per language. A name must be supplied for <b>every</b> configured language, so the
/// tree can never end up half-translated and unreadable to one set of students.
/// </para>
/// </summary>
public class CreateCurriculumNodeRequest
{
    [Required, MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CurriculumNodeTranslationRequest> Translations { get; set; } = [];

    /// <summary>
    /// Position among the siblings under this parent. Omit to append after the last one.
    /// Supplying a position already taken is rejected — order drives the unlock chain, so
    /// two siblings cannot share one.
    /// </summary>
    public int? Order { get; set; }
}

public class CurriculumNodeTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}
