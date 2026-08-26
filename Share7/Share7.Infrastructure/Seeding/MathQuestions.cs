using System.Globalization;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Generates arithmetic that is actually correct, banded by grade.
/// <para>
/// <b>Generated rather than curated, because mathematics is the one subject where it can be.</b> A
/// bank of hand-written sums would repeat itself across fifteen hundred lessons; a generator keyed
/// on the lesson's own coordinates gives every lesson different numbers while the answer stays a
/// fact rather than an opinion. The distractors are built from the same numbers — an off-by-one, a
/// transposed operator, a plausible slip — so a wrong answer looks like a mistake a child would
/// actually make instead of an obviously absurd one.
/// </para>
/// <para>
/// Digits stay Western in both languages. Egyptian classrooms use them, and mixing Arabic-Indic
/// numerals into a string the client may render left-to-right is a presentation bug waiting to
/// happen.
/// </para>
/// </summary>
internal static class MathQuestions
{
    /// <summary>A question for this grade, varying with <paramref name="stream"/>.</summary>
    public static SeedQuestion For(int gradeOrder, bool arabic, int stream)
    {
        var s = stream < 0 ? -stream : stream;

        return gradeOrder switch
        {
            <= 2 => Kindergarten(s, arabic),
            <= 4 => EarlyPrimary(s, arabic),
            <= 6 => MiddlePrimary(s, arabic),
            <= 8 => UpperPrimary(s, arabic),
            <= 11 => Preparatory(s, arabic),
            _ => Secondary(s, arabic)
        };
    }

    // ── KG: counting, ordering, sums inside ten ───────────────────────────────

    private static SeedQuestion Kindergarten(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var a = 1 + s % 5;
                var b = 1 + s / 5 % 4;
                return Sum(a, b, ar);
            }
            case 1:
            {
                var n = 1 + s % 8;
                return new SeedQuestion(
                    ar ? $"ما العدد الذي يأتي بعد {n}؟" : $"Which number comes after {n}?",
                    N(n + 1), N(n), N(n + 2));
            }
            case 2:
            {
                var a = 2 + s % 7;
                var b = a + 1 + s / 7 % 3;
                return new SeedQuestion(
                    ar ? $"أيهما أكبر: {a} أم {b}؟" : $"Which is bigger: {a} or {b}?",
                    N(b), N(a), ar ? "متساويان" : "They are equal");
            }
            default:
            {
                var total = 4 + s % 5;
                var taken = 1 + s % 3;
                return new SeedQuestion(
                    ar
                        ? $"لديك {total} تفاحات وأكلت {taken} منها. كم تفاحة بقيت؟"
                        : $"You have {total} apples and eat {taken}. How many are left?",
                    N(total - taken), N(total + taken), N(total));
            }
        }
    }

    // ── P1-P2: add and subtract inside a hundred ──────────────────────────────

    private static SeedQuestion EarlyPrimary(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var a = 10 + s % 40;
                var b = 5 + s / 3 % 30;
                return Sum(a, b, ar);
            }
            case 1:
            {
                var a = 30 + s % 60;
                var b = 5 + s / 5 % 25;
                return new SeedQuestion(
                    ar ? $"ما ناتج {a} − {b}؟" : $"What is {a} − {b}?",
                    N(a - b), N(a + b), N(a - b - 10));
            }
            case 2:
            {
                var n = 3 + s % 20;
                return new SeedQuestion(
                    ar ? $"ما ضعف العدد {n}؟" : $"What is double {n}?",
                    N(n * 2), N(n + 2), N(n * 2 + 1));
            }
            default:
            {
                var tens = 2 + s % 8;
                return new SeedQuestion(
                    ar ? $"كم وحدة في {tens} عشرات؟" : $"How many ones are in {tens} tens?",
                    N(tens * 10), N(tens), N(tens * 100));
            }
        }
    }

    // ── P3-P4: tables, division, small word problems ──────────────────────────

    private static SeedQuestion MiddlePrimary(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var a = 2 + s % 9;
                var b = 2 + s / 9 % 9;
                return new SeedQuestion(
                    ar ? $"ما ناتج {a} × {b}؟" : $"What is {a} × {b}?",
                    N(a * b), N(a + b), N(a * b + a));
            }
            case 1:
            {
                var b = 2 + s % 8;
                var q = 2 + s / 8 % 9;
                var a = b * q;
                return new SeedQuestion(
                    ar ? $"ما ناتج {a} ÷ {b}؟" : $"What is {a} ÷ {b}?",
                    N(q), N(q + 1), N(a - b));
            }
            case 2:
            {
                var boxes = 3 + s % 6;
                var each = 4 + s / 6 % 6;
                return new SeedQuestion(
                    ar
                        ? $"في كل صندوق {each} أقلام. كم قلمًا في {boxes} صناديق؟"
                        : $"Each box holds {each} pencils. How many pencils are in {boxes} boxes?",
                    N(boxes * each), N(boxes + each), N(boxes * each - each));
            }
            default:
            {
                var n = 12 + s % 60;
                var even = n % 2 == 0;
                return new SeedQuestion(
                    ar ? $"هل العدد {n} زوجي أم فردي؟" : $"Is {n} even or odd?",
                    ar ? even ? "زوجي" : "فردي" : even ? "Even" : "Odd",
                    ar ? even ? "فردي" : "زوجي" : even ? "Odd" : "Even",
                    ar ? "لا يمكن تحديده" : "Neither");
            }
        }
    }

    // ── P5-P6: fractions, percentages, measurement ────────────────────────────

    private static SeedQuestion UpperPrimary(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var percent = new[] { 10, 20, 25, 50, 75 }[s % 5];
                var baseValue = 20 * (1 + s % 8);
                var answer = baseValue * percent / 100;
                return new SeedQuestion(
                    ar ? $"كم يساوي {percent}% من {baseValue}؟" : $"What is {percent}% of {baseValue}?",
                    N(answer), N(answer + percent), N(baseValue - answer));
            }
            case 1:
            {
                var d = 3 + s % 6;
                return new SeedQuestion(
                    ar ? $"ما ناتج 1/{d} + 1/{d}؟" : $"What is 1/{d} + 1/{d}?",
                    $"2/{d}", $"2/{d * 2}", $"1/{d * 2}");
            }
            case 2:
            {
                var w = 3 + s % 9;
                var h = 2 + s / 9 % 8;
                return new SeedQuestion(
                    ar
                        ? $"مستطيل طوله {w} سم وعرضه {h} سم. ما مساحته بالسنتيمتر المربع؟"
                        : $"A rectangle is {w} cm by {h} cm. What is its area in cm²?",
                    N(w * h), N(2 * (w + h)), N(w + h));
            }
            default:
            {
                var w = 3 + s % 9;
                var h = 2 + s / 7 % 8;
                return new SeedQuestion(
                    ar
                        ? $"مستطيل طوله {w} سم وعرضه {h} سم. ما محيطه بالسنتيمتر؟"
                        : $"A rectangle is {w} cm by {h} cm. What is its perimeter in cm?",
                    N(2 * (w + h)), N(w * h), N(w + h));
            }
        }
    }

    // ── Prep: equations, powers, negatives, ratio ─────────────────────────────

    private static SeedQuestion Preparatory(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var m = 2 + s % 7;
                var x = 2 + s / 7 % 9;
                var c = 1 + s % 11;
                var rhs = m * x + c;
                return new SeedQuestion(
                    ar ? $"إذا كان {m}س + {c} = {rhs}، فما قيمة س؟" : $"If {m}x + {c} = {rhs}, what is x?",
                    N(x), N(x + 1), N(rhs - c));
            }
            case 1:
            {
                var b = 2 + s % 6;
                var e = 2 + s / 6 % 3;
                var value = (int)Math.Pow(b, e);
                return new SeedQuestion(
                    ar ? $"ما قيمة {b} أُس {e}؟" : $"What is {b} to the power of {e}?",
                    N(value), N(b * e), N(value + b));
            }
            case 2:
            {
                var a = 3 + s % 12;
                var b = 5 + s / 4 % 15;
                return new SeedQuestion(
                    ar ? $"ما ناتج (−{a}) + {b}؟" : $"What is (−{a}) + {b}?",
                    N(b - a), N(-(a + b)), N(a + b));
            }
            default:
            {
                var k = 2 + s % 6;
                var a = 3 * k;
                var b = 4 * k;
                return new SeedQuestion(
                    ar
                        ? $"النسبة {a} : {b} في أبسط صورة هي؟"
                        : $"The ratio {a} : {b} in its simplest form is?",
                    "3 : 4", "4 : 3", $"{a} : {b}");
            }
        }
    }

    // ── Secondary: quadratics, sequences, trigonometry, logarithms ────────────

    private static SeedQuestion Secondary(int s, bool ar)
    {
        switch (s % 4)
        {
            case 0:
            {
                var r1 = 1 + s % 6;
                var r2 = r1 + 1 + s / 6 % 4;
                var b = -(r1 + r2);
                var c = r1 * r2;
                var poly = $"x² − {Math.Abs(b)}x + {c} = 0";
                var polyAr = $"س² − {Math.Abs(b)}س + {c} = 0";
                return new SeedQuestion(
                    ar ? $"ما جذرا المعادلة {polyAr}؟" : $"What are the roots of {poly}?",
                    $"{r1}, {r2}", $"−{r1}, −{r2}", $"{r1}, {r2 + 1}");
            }
            case 1:
            {
                var first = 2 + s % 9;
                var diff = 2 + s / 9 % 7;
                var n = 5 + s % 6;
                var term = first + (n - 1) * diff;
                return new SeedQuestion(
                    ar
                        ? $"متتابعة حسابية حدها الأول {first} وأساسها {diff}. ما الحد رقم {n}؟"
                        : $"An arithmetic sequence starts at {first} with common difference {diff}. What is term {n}?",
                    N(term), N(first + n * diff), N(first * n));
            }
            case 2:
            {
                var angles = new[] { 0, 30, 45, 60, 90 };
                var values = new[] { "0", "1/2", "√2/2", "√3/2", "1" };
                var i = s % angles.Length;
                var j = (i + 1) % angles.Length;
                var k = (i + 2) % angles.Length;
                return new SeedQuestion(
                    ar ? $"ما قيمة جا {angles[i]}°؟" : $"What is sin {angles[i]}°?",
                    values[i], values[j], values[k]);
            }
            default:
            {
                var b = new[] { 2, 3, 5, 10 }[s % 4];
                var e = 2 + s / 4 % 3;
                var value = (int)Math.Pow(b, e);
                return new SeedQuestion(
                    ar ? $"ما قيمة لوغاريتم {value} للأساس {b}؟" : $"What is log base {b} of {value}?",
                    N(e), N(value / b), N(e + 1));
            }
        }
    }

    // ── shared shapes ─────────────────────────────────────────────────────────

    private static SeedQuestion Sum(int a, int b, bool ar) => new(
        ar ? $"ما ناتج {a} + {b}؟" : $"What is {a} + {b}?",
        N(a + b), N(a + b + 1), N(a > b ? a - b : b - a));

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
