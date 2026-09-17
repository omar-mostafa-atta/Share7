using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>A term's name in one language. Composite key (TermId, LangId).</summary>
public class TermTranslation
{
    public Guid TermId { get; set; }
    public Term? Term { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Name { get; set; } = string.Empty;
}
