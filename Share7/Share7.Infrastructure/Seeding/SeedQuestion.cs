namespace Share7.Infrastructure.Seeding;

/// <summary>
/// One generated question: the prompt, the right answer, and two wrong ones.
/// <para>
/// Three choices because the runner has three lanes. The seeder writes the correct answer at a
/// rotated position rather than always first, so a child who learns "the answer is on the left"
/// learns nothing useful.
/// </para>
/// </summary>
internal readonly record struct SeedQuestion(string Text, string Correct, string WrongA, string WrongB)
{
    /// <summary>
    /// The three choices in presentation order, with the correct one moved to
    /// <paramref name="correctSlot"/> (0, 1 or 2).
    /// </summary>
    public string[] Ordered(int correctSlot)
    {
        var slot = ((correctSlot % 3) + 3) % 3;
        return slot switch
        {
            0 => [Correct, WrongA, WrongB],
            1 => [WrongA, Correct, WrongB],
            _ => [WrongA, WrongB, Correct]
        };
    }
}
