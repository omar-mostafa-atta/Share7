using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Body for every "add a node under this parent" endpoint. Only the name is supplied —
/// the parent comes from the route and the language is inherited from that parent, so a
/// node can never end up in a different language tree than the thing it hangs off.
/// </summary>
public class CreateCurriculumNodeRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}
