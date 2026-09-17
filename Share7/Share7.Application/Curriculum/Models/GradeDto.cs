namespace Share7.Application.Curriculum.Models;

public class GradeDto
{
    public Guid Id { get; set; }

    /// <summary>Grade name in the language identified by <see cref="LangId"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid LangId { get; set; }

    /// <summary>Position in the Egyptian ladder, 1 = KG1 through 14 = Secondary Three.</summary>
    public int Order { get; set; }
}
