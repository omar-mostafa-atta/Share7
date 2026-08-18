using Microsoft.EntityFrameworkCore;
using Share7.Domain.Constants;
using Share7.Domain.Curriculum;
using Share7.Domain.Games;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>One node at each level of the tree, plus the game the progress rows hang off.</summary>
public record CurriculumPathFixture(
    Guid GradeId,
    Guid TermId,
    Guid SubjectId,
    Guid ChapterId,
    Guid LessonId,
    Guid QuestionId,
    Guid GameId);

/// <summary>
/// Minimal row builders. These write through the DbContext rather than UserManager on purpose —
/// these tests are about the commerce and deletion logic, not about Identity's password hashing,
/// and going through UserManager would drag a full service provider into every fixture.
/// </summary>
public static class TestData
{
    public static async Task<Guid> CreateUserAsync(
        ApplicationDbContext context,
        string? username = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var name = username ?? $"user_{id:N}";

        context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            PreferredLanguageId = LanguageIds.English
        });

        await context.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// A complete Grade → Term → Subject → Chapter → Lesson path plus a game.
    /// <para>
    /// Progress rows are foreign-keyed all the way down, so there is no shortcut: a lesson id
    /// invented on the spot fails the insert. Grades are already seeded by migration, so this
    /// starts from the first one.
    /// </para>
    /// </summary>
    public static async Task<CurriculumPathFixture> CreateCurriculumPathAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var gradeId = await context.Grades.Select(g => g.Id).FirstAsync(cancellationToken);

        var term = new Term { Id = Guid.NewGuid(), GradeId = gradeId, Order = NextOrder() };
        var subject = new Subject { Id = Guid.NewGuid(), TermId = term.Id, Order = NextOrder() };
        var chapter = new Chapter { Id = Guid.NewGuid(), SubjectId = subject.Id, Order = NextOrder() };
        var lesson = new Lesson { Id = Guid.NewGuid(), ChapterId = chapter.Id, Order = NextOrder() };

        var question = new Question
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            LangId = LanguageIds.English,
            Text = "Symbol for iron?",
            Version = 1,
            RowNumber = 1,
            CreatedAt = DateTime.UtcNow
        };

        // Real choices, like the importer writes. Grading checks that a submitted choice belongs to
        // its question, so a question with none can never be answered correctly.
        question.Choices =
        [
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = question.Id, Text = "Fe", OrderIndex = 0 },
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = question.Id, Text = "Ir", OrderIndex = 1 },
            new QuestionChoice { Id = Guid.NewGuid(), QuestionId = question.Id, Text = "F",  OrderIndex = 2 }
        ];

        question.CorrectChoiceId = question.Choices.First().Id;

        var game = new Game
        {
            Id = Guid.NewGuid(),
            GameKey = $"g_{Guid.NewGuid():N}"[..20]
        };

        context.Terms.Add(term);
        context.Subjects.Add(subject);
        context.Chapters.Add(chapter);
        context.Lessons.Add(lesson);
        context.Questions.Add(question);
        context.Games.Add(game);

        await context.SaveChangesAsync(cancellationToken);

        return new CurriculumPathFixture(
            gradeId, term.Id, subject.Id, chapter.Id, lesson.Id, question.Id, game.Id);
    }

    // Order is unique per parent, and these fixtures share parents across tests, so a fixed
    // value would collide on the second call.
    private static int _order;
    private static int NextOrder() => Interlocked.Increment(ref _order);
}
