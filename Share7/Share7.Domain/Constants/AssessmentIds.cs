namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the assessment rows a deployment must have before anybody can author anything.
/// <para>
/// Deliberately short. **No examination is seeded here**, and that is a decision rather than an
/// omission: a blueprint is a claim about what a real paper contains, and shipping a plausible one
/// in a migration would put a fabricated specification in every environment wearing the schema's
/// authority. Exam specifications are authored, by someone who has read the syllabus, through the
/// admin surface — and the worked example the console can generate says in its own source note
/// that Share7 derived it from Share7's content.
/// </para>
/// </summary>
public static class AssessmentIds
{
    /// <summary>
    /// Share7 as a publisher of its own material. Trust tier 0 — a private author, which is what
    /// the platform is when it writes a benchmark rather than administering a national paper.
    /// <para>
    /// Exists so that a Share7-authored exam specification has somewhere honest to hang. Attaching
    /// one to the Egyptian ministry's authority instead would mean every parent reading the report
    /// saw a ministry's name on a number the ministry never published.
    /// </para>
    /// </summary>
    public static readonly Guid Share7Authority =
        Guid.Parse("c4a7f2e9-5b31-4d68-8a09-3e7c1b6d0f24");

    /// <summary>
    /// Share7's own competency framework — the one that holds **authored** targets, as against the
    /// placeholder framework that holds one row per lesson. Real targets are minted here as
    /// subjects are worked through, and a blueprint written against this framework is the first
    /// one whose coverage figure means anything (§20.5 phase two).
    /// </summary>
    public static readonly Guid Share7CoreFramework =
        Guid.Parse("d5b8a3f0-6c42-4e79-9b1a-4f8d2c7e1035");
}
