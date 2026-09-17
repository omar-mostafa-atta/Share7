namespace Share7.Domain.LookUps;

/// <summary>
/// Content language. Every curriculum node carries a Lang_Id pointing here — the
/// EN and AR trees are fully separate sets of rows, not translations of each other.
/// </summary>
public class Language
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
