using Share7.Domain.LookUps;

namespace Share7.Domain.Games;

/// <summary>A game's display name and description in one language. Composite key (GameId, LangId).</summary>
public class GameTranslation
{
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
