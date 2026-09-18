using System.Text.RegularExpressions;

namespace Murmur.Speech;

/// <summary>
/// Tidies what comes straight out of the speech model before anything else sees it.
/// </summary>
/// <remarks>
/// Parakeet has no token for "&amp;" and writes <c>&lt;unk&gt;</c> where one was said:
/// "P&lt;unk&gt;L" reached the history on 2026-09-11. Between two single capitals it can only
/// have been an ampersand; anywhere else it is noise.
/// </remarks>
public static partial class TranscriptNormaliser
{
    /// <summary>Applies every rule.</summary>
    public static string Apply(string text)
    {
        if (!text.Contains("<unk>", StringComparison.Ordinal)) return text;
        var result = Ampersand().Replace(text, "$1&$2");
        result = Unknown().Replace(result, " ");
        return Spaces().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(\p{Lu})<unk>(\p{Lu})(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex Ampersand();

    [GeneratedRegex(@"\s*<unk>\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Unknown();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
