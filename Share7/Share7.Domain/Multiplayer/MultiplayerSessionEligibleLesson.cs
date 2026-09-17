using Share7.Domain.Curriculum;

namespace Share7.Domain.Multiplayer;

/// <summary>
/// One lesson every player seated in a session could play — the intersection, as it stands.
/// <para>
/// <b>This table is why two children no longer have to choose the same lesson to play together.</b>
/// They choose a subject; the server works out what each of them has unlocked and has questions for,
/// keeps the overlap here, and stamps one of them onto the session when the match is ready to start.
/// </para>
/// <para>
/// Rows are only ever removed, never added, after the session is created. The set starts as the
/// creator's own eligible lessons and narrows with each joiner — an intersection that could grow
/// would let a lesson somebody cannot play come back into a match they are already in.
/// </para>
/// </summary>
public class MultiplayerSessionEligibleLesson
{
    public Guid SessionId { get; set; }
    public MultiplayerSession? Session { get; set; }

    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>
    /// The best any seated player has scored on it, summed across them.
    /// <para>
    /// The tie-break that picks which lesson a match actually plays: the one the group is least
    /// practised at. Stored rather than recomputed so the pick is one indexed read at the moment the
    /// host presses start, when the roster is already known and the players are waiting.
    /// </para>
    /// </summary>
    public int SummedBestPercent { get; set; }
}
