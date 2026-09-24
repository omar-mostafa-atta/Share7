using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Share7.Domain.Constants;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Domain.Entities;
using Share7.Domain.Games;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Structure;
using Share7.Tests.Infrastructure;

namespace Share7.Tests.Contracts;

/// <summary>A fixture student: who they are and what language they read in.</summary>
public sealed record ContractStudent(Guid Id, string Username, Guid LangId);

/// <summary>
/// The fixed world the contract snapshots describe: a small curriculum with every shape the client
/// has to cope with, a game, and two students — one reading in English, one in Arabic.
/// <para>
/// <b>Every id is derived from a name</b>, so the same fixture produces the same ids on every run
/// and the snapshots can name them (<c>lesson:solids</c>) instead of hiding them. An id the server
/// invents at run time — a reward transaction, an item — is replaced by a numbered placeholder
/// instead; see <see cref="ContractSnapshot"/>.
/// </para>
/// <para>
/// The shapes deliberately covered: a term with two subjects; a subject with no Arabic name (so
/// Arabic readers get an empty <c>name</c>, not a missing row); a lesson on different versions in
/// each language; a lesson with English questions only; a lesson with none; and a recovery pool.
/// </para>
/// </summary>
public sealed class ContractData
{
    public const string Password = "Contract#Student-2026";

    private static readonly Guid En = LanguageIds.English;
    private static readonly Guid Ar = LanguageIds.Arabic;

    /// <summary>Every id the fixture knows, by the label a snapshot shows for it.</summary>
    public IReadOnlyDictionary<Guid, string> Labels => _labels;
    private readonly Dictionary<Guid, string> _labels = [];

    public Guid GradeId { get; } = GradeIds.PrimaryFour;
    public Guid GameId { get; private set; }

    public Guid FirstTermId { get; private set; }
    public Guid ScienceId { get; private set; }
    public Guid MatterChapterId { get; private set; }

    /// <summary>Main questions in both languages, on English v2 and Arabic v1, plus a recovery pool.</summary>
    public Guid SolidsLessonId { get; private set; }

    /// <summary>English questions only: unplayable for an Arabic reader.</summary>
    public Guid LiquidsLessonId { get; private set; }

    /// <summary>No questions in any language.</summary>
    public Guid GasesLessonId { get; private set; }

    /// <summary>An id that names nothing, for the "unknown lesson" cases.</summary>
    public Guid UnknownLessonId { get; private set; }

    public ContractStudent EnglishStudent { get; private set; } = null!;
    public ContractStudent ArabicStudent { get; private set; } = null!;

    /// <summary>The correct choice for each main question, by language, for a perfect run.</summary>
    public IReadOnlyDictionary<(Guid LessonId, Guid LangId), IReadOnlyList<(Guid QuestionId, Guid ChoiceId)>> Answers => _answers;
    private readonly Dictionary<(Guid, Guid), IReadOnlyList<(Guid, Guid)>> _answers = [];

    private ContractData()
    {
        foreach (var field in typeof(GradeIds).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is Guid id)
                _labels[id] = $"grade:{field.Name}";
        }

        _labels[En] = "lang:en";
        _labels[Ar] = "lang:ar";

        UnknownLessonId = Id("lesson:does-not-exist");
    }

    /// <summary>A stable id for a name, registered under that name for the snapshots.</summary>
    private Guid Id(string label)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes("share7.contract/" + label));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        var id = new Guid(bytes);
        _labels[id] = label;
        return id;
    }

    public static async Task<ContractData> WriteAsync(ApplicationDbContext context)
    {
        var data = new ContractData();
        await data.WriteCurriculumAsync(context);
        await data.WriteStudentsAsync(context);

        // The fixture is written the way production content was written before the engine rebuild —
        // into the typed tables and the old recovery table — and then brought into the engine by the
        // very backfill the EngineAuthoritative migration runs. So the node reads these contracts are
        // also checked against (Curriculum:ReadModel = Generic) read exactly what production would.
        await EngineTest.BackfillAsync(context);
        return data;
    }

    private async Task WriteCurriculumAsync(ApplicationDbContext context)
    {
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        var firstTerm = Term("term:first", 1, "First Term", "الفصل الدراسي الأول");
        var secondTerm = Term("term:second", 2, "Second Term", "الفصل الدراسي الثاني");

        var science = Subject("subject:science", firstTerm.Id, 1, "Science", "العلوم");
        var maths = Subject("subject:maths-no-arabic", firstTerm.Id, 2, "Mathematics", arabic: null);

        var matter = Chapter("chapter:matter", science.Id, 1, "States of Matter", "حالات المادة");
        var energy = Chapter("chapter:energy", science.Id, 2, "Energy", "الطاقة");

        var solids = Lesson("lesson:solids", matter.Id, 1, "Solids", "المواد الصلبة");
        var liquids = Lesson("lesson:liquids-english-only", matter.Id, 2, "Liquids", "السوائل");
        var gases = Lesson("lesson:gases-no-questions", matter.Id, 3, "Gases", "الغازات");
        var heat = Lesson("lesson:heat", energy.Id, 1, "Heat", "الحرارة");

        context.Terms.AddRange(firstTerm, secondTerm);
        context.Subjects.AddRange(science, maths);
        context.Chapters.AddRange(matter, energy);
        context.Lessons.AddRange(solids, liquids, gases, heat);

        var game = new Game
        {
            Id = Id("game:lane-runner"),
            GameKey = "contract-lane-runner",
            Translations =
            [
                new GameTranslation { LangId = En, DisplayName = "Lane Runner", Description = "Run the lane with the right answer." },
                new GameTranslation { LangId = Ar, DisplayName = "عداء المسارات", Description = "اركض في مسار الإجابة الصحيحة." }
            ]
        };
        context.Games.Add(game);

        await context.SaveChangesAsync();

        var minter = new ItemIdentityMinter(context);

        // Solids: English republished once (v2), Arabic still on v1 — the two languages version
        // independently, and the client caches each separately.
        await MainAsync(context, minter, solids.Id, En, 2, now,
        [
            ("What is ice?", "A solid", "A gas", "A liquid"),
            ("Which keeps its shape?", "A rock", "Water", "Steam"),
            ("What is iron at room temperature?", "Solid", "Liquid", "Gas")
        ]);
        await MainAsync(context, minter, solids.Id, Ar, 1, now,
        [
            ("ما هو الجليد؟", "مادة صلبة", "غاز", "سائل"),
            ("أي مما يلي يحتفظ بشكله؟", "الصخر", "الماء", "البخار"),
            ("ما حالة الحديد في درجة حرارة الغرفة؟", "صلب", "سائل", "غاز")
        ]);
        Recovery(context, solids.Id, En, now, ("Is a chair a solid?", "Yes", "No", "Only in winter"));
        Recovery(context, solids.Id, Ar, now, ("هل الكرسي مادة صلبة؟", "نعم", "لا", "في الشتاء فقط"));

        await MainAsync(context, minter, liquids.Id, En, 1, now,
        [
            ("Which is a liquid?", "Milk", "Stone", "Air"),
            ("Liquids take the shape of…", "Their container", "A cube", "Nothing")
        ]);

        await MainAsync(context, minter, heat.Id, En, 1, now, [("Heat makes ice…", "Melt", "Freeze", "Grow")]);
        await MainAsync(context, minter, heat.Id, Ar, 1, now, [("الحرارة تجعل الجليد…", "ينصهر", "يتجمد", "يكبر")]);

        await context.SaveChangesAsync();

        GameId = game.Id;
        FirstTermId = firstTerm.Id;
        ScienceId = science.Id;
        MatterChapterId = matter.Id;
        SolidsLessonId = solids.Id;
        LiquidsLessonId = liquids.Id;
        GasesLessonId = gases.Id;
    }

    private async Task WriteStudentsAsync(ApplicationDbContext context)
    {
        EnglishStudent = await StudentAsync(context, "user:student-en", "contract_student_en", En);
        ArabicStudent = await StudentAsync(context, "user:student-ar", "contract_student_ar", Ar);
    }

    private async Task<ContractStudent> StudentAsync(ApplicationDbContext context, string label, string username, Guid langId)
    {
        var users = IdentityTestHost.CreateUserManager(context);
        var user = new ApplicationUser { Id = Id(label), UserName = username, PreferredLanguageId = langId };

        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, Roles.Student);

        context.StudentProfiles.Add(new StudentProfile
        {
            Id = Id(label + ":profile"),
            UserId = user.Id,
            FullName = "Contract Student",
            Age = 9,
            PhoneNumber = "01000000000",
            GradeId = GradeId,
            CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        return new ContractStudent(user.Id, username, langId);
    }

    // ------------------------------------------------------------- builders

    private Term Term(string label, int order, string english, string arabic) => new()
    {
        Id = Id(label), GradeId = GradeId, Order = order,
        Translations = [new() { LangId = En, Name = english }, new() { LangId = Ar, Name = arabic }]
    };

    private Subject Subject(string label, Guid termId, int order, string english, string? arabic) => new()
    {
        Id = Id(label), TermId = termId, Order = order,
        Translations = arabic is null
            ? [new() { LangId = En, Name = english }]
            : [new() { LangId = En, Name = english }, new() { LangId = Ar, Name = arabic }]
    };

    private Chapter Chapter(string label, Guid subjectId, int order, string english, string arabic) => new()
    {
        Id = Id(label), SubjectId = subjectId, Order = order,
        Translations = [new() { LangId = En, Name = english }, new() { LangId = Ar, Name = arabic }]
    };

    private Lesson Lesson(string label, Guid chapterId, int order, string english, string arabic) => new()
    {
        Id = Id(label), ChapterId = chapterId, Order = order,
        Translations = [new() { LangId = En, Name = english }, new() { LangId = Ar, Name = arabic }]
    };

    private string Lang(Guid langId) => _labels[langId].Replace("lang:", string.Empty);

    private async Task MainAsync(
        ApplicationDbContext context, ItemIdentityMinter minter, Guid lessonId, Guid langId, int version, DateTime now,
        IReadOnlyList<(string Text, string Correct, string Wrong1, string Wrong2)> rows)
    {
        var lessonLabel = _labels[lessonId];
        var answers = new List<(Guid, Guid)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 1;
            var itemVersion = await minter.ResolveForLessonRowAsync(lessonId, rowNumber, version, NodeItemRole.Core, now);
            var prefix = $"{lessonLabel}/{Lang(langId)}/q{rowNumber}";

            var question = new Question
            {
                Id = Id(prefix),
                ItemVersionId = itemVersion.Id,
                ItemVersion = itemVersion,
                LessonId = lessonId,
                LangId = langId,
                Text = rows[i].Text,
                Version = version,
                IsActive = true,
                RowNumber = rowNumber,
                CreatedAt = now,
                Choices =
                [
                    new QuestionChoice { Id = Id(prefix + "/correct"), Text = rows[i].Correct, OrderIndex = 0 },
                    new QuestionChoice { Id = Id(prefix + "/wrong1"), Text = rows[i].Wrong1, OrderIndex = 1 },
                    new QuestionChoice { Id = Id(prefix + "/wrong2"), Text = rows[i].Wrong2, OrderIndex = 2 }
                ]
            };
            question.CorrectChoiceId = question.Choices.First().Id;

            context.Questions.Add(question);
            answers.Add((question.Id, question.CorrectChoiceId));
        }

        context.LessonQuestionSets.Add(new LessonQuestionSet { LessonId = lessonId, LangId = langId, Version = version });
        _answers[(lessonId, langId)] = answers;
    }

    private void Recovery(
        ApplicationDbContext context, Guid lessonId, Guid langId, DateTime now,
        (string Text, string Correct, string Wrong1, string Wrong2) row)
    {
        var prefix = $"{_labels[lessonId]}/{Lang(langId)}/recovery1";

        var question = new RecoveryQuestion
        {
            Id = Id(prefix),
            LessonId = lessonId,
            LangId = langId,
            Text = row.Text,
            Version = 1,
            IsActive = true,
            RowNumber = 1,
            CreatedAt = now,
            Choices =
            [
                new RecoveryQuestionChoice { Id = Id(prefix + "/correct"), Text = row.Correct, OrderIndex = 0 },
                new RecoveryQuestionChoice { Id = Id(prefix + "/wrong1"), Text = row.Wrong1, OrderIndex = 1 },
                new RecoveryQuestionChoice { Id = Id(prefix + "/wrong2"), Text = row.Wrong2, OrderIndex = 2 }
            ]
        };
        question.CorrectChoiceId = question.Choices.First().Id;

        context.RecoveryQuestions.Add(question);
        context.LessonRecoveryQuestionSets.Add(new LessonRecoveryQuestionSet { LessonId = lessonId, LangId = langId, Version = 1 });
    }
}
