using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Which kind of writing a dictation is going into, from the app in front and its title.
/// </summary>
/// <remarks>
/// <para>
/// Rules first, because they cost nothing and cover most of Dave's dictations (on 25/09/2026:
/// Claude 30, Slack 6, Chrome 2, ChatGPT 1). A browser says little by its process name, so
/// its title is read for the site. Whatever the rules cannot place can be put to the
/// decision model at key-down, where its half-second hides behind the talking.
/// </para>
/// <para>
/// The style only shapes layout: paragraphs, a greeting on its own line, code written as
/// code. The words stay the speaker's.
/// </para>
/// </remarks>
public static class AppStyles
{
    private static readonly (string Name, WritingStyle Style)[] Apps =
    [
        ("outlook", WritingStyle.Email), ("olk", WritingStyle.Email), ("thunderbird", WritingStyle.Email), ("hxoutlook", WritingStyle.Email),
        ("slack", WritingStyle.Chat), ("ms-teams", WritingStyle.Chat), ("teams", WritingStyle.Chat), ("whatsapp", WritingStyle.Chat),
        ("telegram", WritingStyle.Chat), ("discord", WritingStyle.Chat), ("signal", WritingStyle.Chat), ("messenger", WritingStyle.Chat),
        ("claude", WritingStyle.Prompt), ("chatgpt", WritingStyle.Prompt), ("codex", WritingStyle.Prompt), ("cursor", WritingStyle.Prompt),
        ("code", WritingStyle.Prompt), ("windsurf", WritingStyle.Prompt), ("windowsterminal", WritingStyle.Prompt), ("powershell", WritingStyle.Prompt),
        ("pwsh", WritingStyle.Prompt), ("cmd", WritingStyle.Prompt), ("warp", WritingStyle.Prompt), ("perplexity", WritingStyle.Prompt),
        ("winword", WritingStyle.Document), ("notion", WritingStyle.Document), ("obsidian", WritingStyle.Document), ("notepad", WritingStyle.Document),
        ("powerpnt", WritingStyle.Document), ("onenote", WritingStyle.Document),
    ];

    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "arc", "opera", "vivaldi", "iexplore", "comet", "dia"];

    /// <summary>Sites told apart by the browser's title, checked in order: a Gmail tab titled "Claude" is still email.</summary>
    private static readonly (string Word, WritingStyle Style)[] Sites =
    [
        ("Gmail", WritingStyle.Email), ("Outlook", WritingStyle.Email), ("Mail", WritingStyle.Email), ("Superhuman", WritingStyle.Email),
        ("Slack", WritingStyle.Chat), ("WhatsApp", WritingStyle.Chat), ("Teams", WritingStyle.Chat), ("Messenger", WritingStyle.Chat),
        ("Discord", WritingStyle.Chat), ("Telegram", WritingStyle.Chat), ("Messaging", WritingStyle.Chat),
        ("ChatGPT", WritingStyle.Prompt), ("Claude", WritingStyle.Prompt), ("Gemini", WritingStyle.Prompt), ("Perplexity", WritingStyle.Prompt),
        ("Copilot", WritingStyle.Prompt), ("Lovable", WritingStyle.Prompt), ("v0", WritingStyle.Prompt), ("AI Studio", WritingStyle.Prompt),
        ("Google Docs", WritingStyle.Document), ("Notion", WritingStyle.Document), ("Confluence", WritingStyle.Document), ("Word", WritingStyle.Document),
    ];

    /// <summary>The style the rules give <paramref name="window"/>, or <see cref="WritingStyle.Unknown"/>.</summary>
    public static WritingStyle FromWindow(FocusedWindow? window)
    {
        if (window is null) return WritingStyle.Unknown;
        var app = window.App.Trim();

        if (Browsers.Any(b => string.Equals(b, app, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (word, style) in Sites)
            {
                if (HasWord(window.Title, word)) return style;
            }
            return WritingStyle.Unknown;
        }

        foreach (var (name, style) in Apps)
        {
            if (string.Equals(name, app, StringComparison.OrdinalIgnoreCase)) return style;
        }
        return WritingStyle.Unknown;
    }

    /// <summary>The question put to the decision model when the rules cannot place the app.</summary>
    public static DecisionQuestion Question { get; } = new("style", "choice",
        "Where is the user about to dictate text, judging by the app, its window title and the text before the cursor?",
        new Dictionary<string, string?>
        {
            ["chat"] = "A chat or direct message to a person: Slack, Teams, WhatsApp, LinkedIn messages, a support chat.",
            ["email"] = "An email being written or replied to.",
            ["prompt"] = "A prompt or instruction to an AI assistant or a coding tool.",
            ["document"] = "A document, notes, a form or a post written to be read later.",
            ["other"] = "A search box, a spreadsheet cell, a short field, or cannot tell.",
        });

    /// <summary>The lowest confidence at which the model's answer is used.</summary>
    public const double MinimumConfidence = 0.6;

    /// <summary>What the decision model is shown.</summary>
    public static string State(FocusedWindow window, string? beforeCaret)
    {
        var state = $"App: {window.App}\nWindow title: {window.Title}";
        if (!string.IsNullOrWhiteSpace(beforeCaret))
        {
            var before = beforeCaret.Trim();
            state += $"\nText just before the cursor: {(before.Length > 300 ? "…" + before[^300..] : before)}";
        }
        return state;
    }

    /// <summary>The style for the model's answer, or <see cref="WritingStyle.Unknown"/> if it was not sure.</summary>
    public static WritingStyle FromDecision(Decision? decision) =>
        decision?.Choice is not { } choice || decision.Confidence is < MinimumConfidence ? WritingStyle.Unknown
        : choice switch
        {
            "chat" => WritingStyle.Chat,
            "email" => WritingStyle.Email,
            "prompt" => WritingStyle.Prompt,
            "document" => WritingStyle.Document,
            _ => WritingStyle.Unknown,
        };

    /// <summary>Asks the decision model to place a window the rules could not. Never throws.</summary>
    public static async Task<WritingStyle> AskAsync(IDecisionModel model, FocusedWindow window, string? beforeCaret, CancellationToken cancellationToken)
    {
        try
        {
            var answers = await model.DecideAsync(State(window, beforeCaret), [Question], cancellationToken).ConfigureAwait(false);
            return FromDecision(answers?.FirstOrDefault(a => a.Key == Question.Key));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn($"jev style: failed ({e.Message})");
            return WritingStyle.Unknown;
        }
    }

    private static bool HasWord(string title, string word)
    {
        var at = 0;
        while ((at = title.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = at == 0 || !char.IsLetterOrDigit(title[at - 1]);
            var end = at + word.Length;
            var after = end == title.Length || !char.IsLetterOrDigit(title[end]);
            if (before && after) return true;
            at = end;
        }
        return false;
    }
}
