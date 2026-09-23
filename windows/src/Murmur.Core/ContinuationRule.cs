namespace Murmur.Core;

/// <summary>What the engine remembers about the last text it typed.</summary>
/// <param name="Text">What was typed.</param>
/// <param name="StopDropped">A trailing full stop was removed by the full-stop rule.</param>
/// <param name="Target">The control it went into, as the platform identifies it, or null.</param>
/// <param name="KeyPressesAfter">The platform's user key-press count once it was typed.</param>
/// <param name="At">When it was typed.</param>
/// <param name="PressedEnter">The dictation ended with a spoken send.</param>
public sealed record TypedDelivery(string Text, bool StopDropped, string? Target, long KeyPressesAfter, DateTimeOffset At, bool PressedEnter);

/// <summary>
/// What to type before the next dictation so that it reads on from the last one.
/// </summary>
/// <remarks>
/// <para>
/// A dictation is typed exactly as it is. Two spoken into the same field one after the
/// other therefore ran together, and with the "never end with a full stop" rule the
/// sentence boundary went too: "...or something, I don't know I also just noticed...".
/// The history has 360 pairs of dictations under fifteen seconds apart.
/// </para>
/// <para>
/// A wrong prefix is worse than none, so every check here is conservative: the same
/// control must still have focus, the user must not have typed anything in between, the
/// previous dictation must not have pressed Enter and the gap must be short.
/// </para>
/// </remarks>
public static class ContinuationRule
{
    /// <summary>After this long the next dictation is assumed to be a fresh message.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>The text to type before <paramref name="next"/>, or empty.</summary>
    /// <param name="previous">The last delivery, or null for the first of the session.</param>
    /// <param name="next">The text about to be typed.</param>
    /// <param name="target">The control that has focus now, or null where unknown.</param>
    /// <param name="keyPressesNow">The platform's user key-press count now.</param>
    /// <param name="now">The current time.</param>
    /// <param name="beforeCaret">The current text immediately before the caret, or null where unavailable.</param>
    public static string Prefix(TypedDelivery? previous, string next, string? target, long keyPressesNow, DateTimeOffset now, string? beforeCaret)
    {
        if (previous is null || next.Length == 0) return string.Empty;
        if (previous.PressedEnter) return string.Empty;
        if (target is null || previous.Target is null || target != previous.Target) return string.Empty;
        if (keyPressesNow != previous.KeyPressesAfter) return string.Empty;
        if (now - previous.At > Window) return string.Empty;
        if (!StillInField(previous.Text, beforeCaret)) return string.Empty;
        if (previous.Text.Length == 0 || char.IsWhiteSpace(previous.Text[^1]) || char.IsWhiteSpace(next[0])) return string.Empty;
        if (!char.IsLetterOrDigit(next[0])) return string.Empty;

        return previous.StopDropped && char.IsUpper(next[0]) ? ". " : " ";
    }

    /// <summary>
    /// Whether the text at the caret still ends with the last dictation. A chat app can
    /// clear its composer after a click on Send without changing focus or producing a key
    /// event; in that case every older continuation signal is stale.
    /// </summary>
    private static bool StillInField(string previous, string? beforeCaret)
    {
        var remembered = previous.TrimEnd();
        var current = beforeCaret?.TrimEnd();
        if (remembered.Length == 0 || string.IsNullOrEmpty(current)) return false;

        var tailLength = Math.Min(remembered.Length, CaretCase.ContextLength);
        return current.EndsWith(remembered[^tailLength..], StringComparison.Ordinal);
    }
}
