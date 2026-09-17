using System.Globalization;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// The one entry point the curriculum seeder calls for a question: it picks the right source for the
/// subject, and guarantees the three choices it returns are actually three.
/// </summary>
internal static class QuestionBank
{
    /// <summary>
    /// A question for this lesson coordinate. <paramref name="stream"/> is what makes two lessons in
    /// the same chapter ask different things; pass <see cref="StreamFor"/> of the lesson's position.
    /// </summary>
    public static SeedQuestion For(string subjectKey, int gradeOrder, bool arabic, int stream)
    {
        var question = subjectKey == SubjectKeys.Math
            ? MathQuestions.For(gradeOrder, arabic, stream)
            : FactQuestions.For(subjectKey, gradeOrder, arabic, stream);

        return Distinguish(question, arabic);
    }

    /// <summary>
    /// A stable non-negative number for a lesson coordinate.
    /// <para>
    /// FNV-1a rather than <c>string.GetHashCode</c>, which is randomised per process since .NET
    /// Core: a seeder whose content changed on every run would defeat the point of deterministic ids
    /// sitting right next to it.
    /// </para>
    /// </summary>
    public static int StreamFor(params object[] parts)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            var hash = offsetBasis;
            foreach (var part in parts)
            {
                var text = Convert.ToString(part, CultureInfo.InvariantCulture) ?? string.Empty;
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= prime;
                }

                hash ^= ':';
                hash *= prime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// Forces three distinct choices.
    /// <para>
    /// The generators build distractors arithmetically — an off-by-one, the other operation — and a
    /// few inputs make two of them land on the same number: 50% of a value equals the value minus
    /// half of it, 2 × 2 equals 2 + 2. A lesson that renders the same text in two lanes and calls one
    /// of them wrong is worse than a boring distractor, so numeric collisions are walked upward until
    /// they separate.
    /// </para>
    /// <para>
    /// The curated banks are authored distinct, so the non-numeric fallback should never fire. It
    /// exists so that a future bank entry with a typo degrades to a dull question rather than to a
    /// broken one.
    /// </para>
    /// </summary>
    private static SeedQuestion Distinguish(SeedQuestion question, bool arabic)
    {
        var correct = question.Correct;
        var wrongA = Separate(question.WrongA, arabic, 1, correct);
        var wrongB = Separate(question.WrongB, arabic, 2, correct, wrongA);

        return question with { WrongA = wrongA, WrongB = wrongB };
    }

    private static string Separate(string candidate, bool arabic, int fallbackIndex, params string[] taken)
    {
        if (!taken.Contains(candidate, StringComparer.Ordinal)) return candidate;

        if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            for (var step = 1; step <= 4; step++)
            {
                var moved = (numeric + step).ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(moved, StringComparer.Ordinal)) return moved;
            }
        }

        var fallbacks = arabic
            ? new[] { "لا شيء مما سبق", "كل ما سبق" }
            : ["None of these", "All of these"];

        var chosen = fallbacks[fallbackIndex % fallbacks.Length];
        return taken.Contains(chosen, StringComparer.Ordinal)
            ? fallbacks[(fallbackIndex + 1) % fallbacks.Length]
            : chosen;
    }
}
