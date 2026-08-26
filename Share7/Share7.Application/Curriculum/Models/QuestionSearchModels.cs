using System.Text.Json.Serialization;

namespace Share7.Application.Curriculum.Models;

/// <summary>Which pools a search covers.</summary>
public enum QuestionPoolFilter
{
    All = 0,
    Main,
    Recovery
}

/// <summary>What to look at, and what to look for.</summary>
public class QuestionSearchRequest
{
    /// <summary>
    /// grade | term | subject | chapter | lesson. Empty scopes the search to the whole curriculum,
    /// which is the one case worth paging hard: the seeded tree is tens of thousands of questions.
    /// </summary>
    public string? ScopeLevel { get; set; }

    public Guid? ScopeId { get; set; }

    public QuestionPoolFilter Pool { get; set; } = QuestionPoolFilter.All;

    /// <summary>Matched against the question text and every answer, in both languages.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Only rows that exist in one language and not the other — the flat equivalent of the health
    /// page's language-gap finding, so an author can fix a term's worth of them in one pass.
    /// </summary>
    public bool OnlyUnpaired { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>One question, both languages, with the trail that says where it lives.</summary>
public class QuestionSearchItemDto
{
    public Guid LessonId { get; set; }

    /// <summary>Grade → lesson, resolved into the caller's language.</summary>
    public IReadOnlyList<string> Path { get; set; } = [];

    /// <summary>The pairing key within its lesson — what a delete addresses.</summary>
    public int RowNumber { get; set; }

    public bool IsRecovery { get; set; }

    public string QuestionEn { get; set; } = string.Empty;
    public string CorrectEn { get; set; } = string.Empty;
    public string QuestionAr { get; set; } = string.Empty;
    public string CorrectAr { get; set; } = string.Empty;

    /// <summary>True when one of the two languages is missing entirely.</summary>
    public bool IsUnpaired { get; set; }

    /// <summary>
    /// Every answer of both languages, kept so a text search can match a distractor rather than only
    /// the question and its correct answer.
    /// <para>
    /// Not serialised: the console renders the question and the right answer, and shipping six more
    /// strings per row to support a filter that already ran on the server would be paying twice for
    /// one feature.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Choices { get; set; } = [];
}

public class QuestionSearchResultDto
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    /// <summary>The lessons the matches came from, which is usually the more useful count.</summary>
    public int LessonCount { get; set; }

    public IReadOnlyList<QuestionSearchItemDto> Items { get; set; } = [];
}
