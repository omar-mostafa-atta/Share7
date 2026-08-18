namespace Share7.Application.Common.Models;

/// <summary>
/// A stable machine code paired with the Unity localization key that renders it.
/// <para>
/// The two are declared together deliberately. Held apart they drift: a code gets renamed and
/// the key keeps pointing at the old string, or a new refusal ships with a code and no key and
/// the client renders a blank message. **The backend never returns UI prose** — Unity owns the
/// localized text and looks it up by <see cref="MessageKey"/>.
/// </para>
/// </summary>
public sealed record ApiErrorCode(string Code, string MessageKey);
