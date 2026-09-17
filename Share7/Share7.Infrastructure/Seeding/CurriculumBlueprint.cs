namespace Share7.Infrastructure.Seeding;

/// <summary>Stable subject keys. They never reach the wire — they key the seed's own content banks.</summary>
internal static class SubjectKeys
{
    public const string Arabic = "arabic";
    public const string English = "english";
    public const string Math = "math";
    public const string Science = "science";
    public const string Social = "social";
}

/// <summary>A name in both content languages. The tree is language-shared; only its text is not.</summary>
internal readonly record struct Bilingual(string En, string Ar)
{
    public string For(bool arabic) => arabic ? Ar : En;
}

/// <summary>
/// The shape of the curriculum this seeder builds: which subjects a grade studies, what its
/// chapters are called, and how a chapter's lessons are named.
/// <para>
/// <b>Subjects vary by stage, and that is the only thing that does.</b> Every grade gets two terms,
/// every subject the same chapter count, every chapter the same lesson count — the tree is uniform
/// because its job is to be complete and navigable, not to model a real scheme of work. What is not
/// uniform is <i>which</i> subjects appear: a kindergartener has no social studies and a secondary
/// student is not learning to count to ten, so a single subject list for all fourteen grades would
/// have produced a tree that is obviously wrong on sight.
/// </para>
/// <para>
/// Chapter titles are real topic names for their stage. Lesson names are the chapter's title with an
/// aspect in front of it — <c>Practice: Fractions and Measurement</c> — which gives every lesson in
/// the tree a distinct, meaningful name from five authored words rather than from three hundred.
/// </para>
/// </summary>
internal static class CurriculumBlueprint
{
    public static readonly Bilingual[] TermNames =
    [
        new("First Term", "الفصل الدراسي الأول"),
        new("Second Term", "الفصل الدراسي الثاني")
    ];

    /// <summary>
    /// The five prefixes a lesson name is built from, cycled across a chapter's lessons. Ordered as
    /// a chapter is actually taught, so lesson 1 of every chapter is its introduction.
    /// </summary>
    public static readonly Bilingual[] LessonAspects =
    [
        new("Introduction", "تمهيد"),
        new("Core Concepts", "المفاهيم الأساسية"),
        new("Practice", "تدريبات"),
        new("Applications", "تطبيقات"),
        new("Review", "مراجعة")
    ];

    /// <summary>The subjects a grade studies, in the order they should appear.</summary>
    public static IReadOnlyList<string> SubjectsFor(int gradeOrder) => gradeOrder switch
    {
        <= 2 => [SubjectKeys.Arabic, SubjectKeys.English, SubjectKeys.Math],
        <= 8 => [SubjectKeys.Arabic, SubjectKeys.English, SubjectKeys.Math, SubjectKeys.Science],
        <= 11 => [SubjectKeys.Arabic, SubjectKeys.English, SubjectKeys.Math, SubjectKeys.Science, SubjectKeys.Social],
        _ => [SubjectKeys.Arabic, SubjectKeys.English, SubjectKeys.Math, SubjectKeys.Science]
    };

    public static Bilingual SubjectName(string subjectKey) => subjectKey switch
    {
        SubjectKeys.Arabic => new Bilingual("Arabic", "اللغة العربية"),
        SubjectKeys.English => new Bilingual("English", "اللغة الإنجليزية"),
        SubjectKeys.Math => new Bilingual("Mathematics", "الرياضيات"),
        SubjectKeys.Science => new Bilingual("Science", "العلوم"),
        _ => new Bilingual("Social Studies", "الدراسات الاجتماعية")
    };

    /// <summary>
    /// The title of chapter <paramref name="chapterIndex"/> (0-based) for this subject and grade.
    /// Cycles if a configuration asks for more chapters than there are authored titles.
    /// </summary>
    public static Bilingual ChapterName(string subjectKey, int gradeOrder, int chapterIndex)
    {
        var titles = ChapterTitles(subjectKey, gradeOrder);
        return titles[chapterIndex % titles.Length];
    }

    private static Bilingual[] ChapterTitles(string subjectKey, int gradeOrder) => subjectKey switch
    {
        SubjectKeys.Math => gradeOrder switch
        {
            <= 2 => MathKindergarten,
            <= 8 => MathPrimary,
            <= 11 => MathPreparatory,
            _ => MathSecondary
        },
        SubjectKeys.Science => gradeOrder switch
        {
            <= 2 => ScienceKindergarten,
            <= 8 => SciencePrimary,
            <= 11 => SciencePreparatory,
            _ => ScienceSecondary
        },
        SubjectKeys.English => gradeOrder switch
        {
            <= 2 => EnglishKindergarten,
            <= 8 => EnglishPrimary,
            <= 11 => EnglishPreparatory,
            _ => EnglishSecondary
        },
        SubjectKeys.Arabic => gradeOrder switch
        {
            <= 2 => ArabicKindergarten,
            <= 8 => ArabicPrimary,
            <= 11 => ArabicPreparatory,
            _ => ArabicSecondary
        },
        _ => SocialStudies
    };

    private static readonly Bilingual[] MathKindergarten =
    [
        new("Numbers to Ten", "الأعداد حتى العشرة"),
        new("Adding and Taking Away", "الجمع والطرح"),
        new("Shapes Around Us", "الأشكال من حولنا")
    ];

    private static readonly Bilingual[] MathPrimary =
    [
        new("Numbers and Place Value", "الأعداد والقيمة المكانية"),
        new("Multiplication and Division", "الضرب والقسمة"),
        new("Fractions and Measurement", "الكسور والقياس")
    ];

    private static readonly Bilingual[] MathPreparatory =
    [
        new("Algebraic Expressions", "المقادير الجبرية"),
        new("Equations and Inequalities", "المعادلات والمتباينات"),
        new("Ratio, Proportion and Statistics", "النسبة والتناسب والإحصاء")
    ];

    private static readonly Bilingual[] MathSecondary =
    [
        new("Quadratic Functions", "الدوال التربيعية"),
        new("Sequences and Series", "المتتابعات والمتسلسلات"),
        new("Trigonometry", "حساب المثلثات")
    ];

    private static readonly Bilingual[] ScienceKindergarten =
    [
        new("My Body and My Senses", "جسمي وحواسي"),
        new("Animals and Plants", "الحيوانات والنباتات"),
        new("Day and Night", "الليل والنهار")
    ];

    private static readonly Bilingual[] SciencePrimary =
    [
        new("Living Things", "الكائنات الحية"),
        new("Matter and Energy", "المادة والطاقة"),
        new("Earth and Space", "الأرض والفضاء")
    ];

    private static readonly Bilingual[] SciencePreparatory =
    [
        new("Cells and Life Processes", "الخلية والعمليات الحيوية"),
        new("Forces and Motion", "القوى والحركة"),
        new("Matter and Chemical Change", "المادة والتغير الكيميائي")
    ];

    private static readonly Bilingual[] ScienceSecondary =
    [
        new("Mechanics", "الميكانيكا"),
        new("Chemical Reactions", "التفاعلات الكيميائية"),
        new("Genetics and Heredity", "الوراثة والجينات")
    ];

    private static readonly Bilingual[] EnglishKindergarten =
    [
        new("Letters and Sounds", "الحروف والأصوات"),
        new("My First Words", "كلماتي الأولى"),
        new("Colours and Numbers", "الألوان والأعداد")
    ];

    private static readonly Bilingual[] EnglishPrimary =
    [
        new("Nouns and Verbs", "الأسماء والأفعال"),
        new("Tenses", "الأزمنة"),
        new("Reading and Vocabulary", "القراءة والمفردات")
    ];

    private static readonly Bilingual[] EnglishPreparatory =
    [
        new("Perfect Tenses", "الأزمنة التامة"),
        new("Passive and Reported Speech", "المبني للمجهول والكلام المنقول"),
        new("Conditionals", "الجمل الشرطية")
    ];

    private static readonly Bilingual[] EnglishSecondary =
    [
        new("Advanced Grammar", "القواعد المتقدمة"),
        new("Vocabulary in Context", "المفردات في السياق"),
        new("Writing and Style", "الكتابة والأسلوب")
    ];

    private static readonly Bilingual[] ArabicKindergarten =
    [
        new("The Arabic Letters", "الحروف الهجائية"),
        new("My First Words", "كلماتي الأولى"),
        new("Opposites", "الأضداد")
    ];

    private static readonly Bilingual[] ArabicPrimary =
    [
        new("Nouns, Verbs and Particles", "الاسم والفعل والحرف"),
        new("Singular and Plural", "المفرد والجمع"),
        new("Prepositions and Opposites", "حروف الجر والأضداد")
    ];

    private static readonly Bilingual[] ArabicPreparatory =
    [
        new("Syntax: Nominative and Accusative", "النحو: المرفوعات والمنصوبات"),
        new("Nominal and Verbal Sentences", "الجملة الاسمية والفعلية"),
        new("Grammatical Styles", "الأساليب النحوية")
    ];

    private static readonly Bilingual[] ArabicSecondary =
    [
        new("Rhetoric: Simile and Metaphor", "البلاغة: التشبيه والاستعارة"),
        new("Rhetorical Embellishments", "المحسنات البديعية"),
        new("Prosody and Literature", "العروض والأدب")
    ];

    private static readonly Bilingual[] SocialStudies =
    [
        new("Maps and Landforms of Egypt", "خرائط وتضاريس مصر"),
        new("Modern Egyptian History", "تاريخ مصر الحديث"),
        new("Climate and Population", "المناخ والسكان")
    ];
}
