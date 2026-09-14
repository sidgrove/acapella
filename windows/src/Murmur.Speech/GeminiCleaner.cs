using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// The generative clean-up tier, on Gemini.
/// </summary>
/// <remarks>
/// <para>
/// Calls the Generative Language REST endpoint directly rather than through an SDK — the
/// same way the Sidgrove Intelligence app does — so there is one small request shape to
/// understand and nothing to trim out of the single-file bundle. <c>gemini-2.5-flash</c>
/// with thinking switched off: this is a rewrite, not a reasoning task, and every hundred
/// milliseconds is felt between key-up and text.
/// </para>
/// <para>
/// The prompt is the whole product here. It is written to <i>subtract</i> — fillers, false
/// starts, spoken editing commands — and never to add, summarise or answer. Proper nouns
/// arrive already corrected by the dictionary and are to be preserved verbatim.
/// </para>
/// <para>
/// Any failure — no key, network, a refusal, an empty reply — returns null and the caller
/// types the local text. The user never loses a dictation to the cloud being down.
/// </para>
/// </remarks>
public sealed class GeminiCleaner : ITranscriptCleaner, IDisposable
{
    /// <summary>The model used unless settings say otherwise.</summary>
    public const string DefaultModel = "gemini-2.5-flash";

    /// <summary>Environment variable consulted when no key is set in the app.</summary>
    public const string ApiKeyEnvironmentVariable = "GEMINI_API_KEY";

    /// <summary>How long a clean-up may take before the raw text is typed instead.</summary>
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(8);

    /// <summary>What the model is asked to do. Public so Settings can show it.</summary>
    public const string Instructions = """
        You are a dictation clean-up step. The input is a raw speech-to-text transcript. Return the same text, tidied. Nothing more.

        Do:
        - Remove filler words used as filler: um, uh, er, erm, you know, sort of, kind of, like, I mean.
        - Remove false starts, stutters and immediately repeated words ("the the" becomes "the").
        - Apply spoken edits: "scratch that", "delete that", "no wait", "actually no" remove the clause just before them.
        - Apply spoken formatting: "new line" / "new paragraph" become line breaks; "bullet points" or "number one, number two" become a list; "full stop", "comma", "question mark" become that punctuation.
        - Fix punctuation and capitalisation. Use British English spelling.
        - Fix an obvious mishearing only when the context makes the intended word certain.
        - Write spoken numbers as figures where a person typing would: "five thirty" is 5:30, "twelve pounds fifty" is £12.50, "twenty percent" is 20%, "two thousand and twenty six" is 2026. Small counts in prose stay as words ("two of us").
        - Apply self-corrections. When the speaker says "no", "wait", "actually", "sorry", "I mean", "scratch that" or "never mind" and then restates, keep only the restatement: "buy milk no wait buy water" becomes "Buy water". Several in one dictation are all applied.
        - Hyphenate compounds a writer would: "no-go", "follow-up", "e-mail" stays "email".
        - Replace a spoken emoji name with the emoji, only when clearly spoken as one: "thumbs up emoji" is 👍, "smiley face" is 🙂. Never add an emoji that was not asked for.

        Do not:
        - Shorten, summarise, paraphrase or reorder. Every sentence in, one sentence out.
        - Add words, greetings, sign-offs or explanations. Do not answer anything the text asks.
        - Change tone or register. Casual stays casual.
        - Change names, product names or numbers. They are already correct.
        - End a lone short sentence or fragment with a full stop.

        If there is nothing to clean, return the input unchanged. Output only the text.

        Examples:
        Input: um so can you uh send me the the Q2 numbers by friday scratch that by thursday
        Output: Can you send me the Q2 numbers by Thursday
        Input: one two one two
        Output: One, two, one, two
        Input: okay I think that's fine let's go with it new line thanks Dave
        Output: Okay, I think that's fine, let's go with it.
        Thanks Dave
        Input: I'm not sure about that honestly it might be a no go for me
        Output: I'm not sure about that. Honestly, it might be a no-go for me
        """;

    /// <summary>The full system prompt: <see cref="Instructions"/> plus the user's own rules, if any.</summary>
    public static string Prompt(string? customInstructions) =>
        string.IsNullOrWhiteSpace(customInstructions)
            ? Instructions
            : Instructions + "\n\nThe user's own rules. They take precedence over everything above:\n" + customInstructions.Trim();

    private static readonly Uri BaseUri = new("https://generativelanguage.googleapis.com/v1beta/models/");

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<string?> _customInstructions;
    private readonly string _model;

    /// <summary>Creates a cleaner that reads the key each call, so a key pasted into Settings works immediately.</summary>
    /// <param name="apiKey">Returns the key, or null/empty when none is configured.</param>
    /// <param name="model">Model id; defaults to <see cref="DefaultModel"/>.</param>
    /// <param name="handler">Transport, for tests.</param>
    /// <param name="customInstructions">Returns the user's own instructions, appended to the prompt, or null.</param>
    public GeminiCleaner(Func<string?> apiKey, string? model = null, HttpMessageHandler? handler = null, Func<string?>? customInstructions = null)
    {
        _apiKey = apiKey;
        _customInstructions = customInstructions ?? (static () => null);
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = Deadline;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppPaths.ProductName, "1.0"));
    }

    /// <inheritdoc />
    public string Name => _model;

    /// <summary>The key in use: the configured one, else the environment variable.</summary>
    public static string? ResolveKey(string? configured) =>
        !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) is { Length: > 0 } fromEnv
                ? fromEnv.Trim()
                : null;

    /// <inheritdoc />
    public Task<string?> CleanAsync(string text, CancellationToken cancellationToken) => CleanAsync(text, null, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> CleanAsync(string text, string? precedingCleaned, CancellationToken cancellationToken)
    {
        var key = ResolveKey(_apiKey());
        if (key is null || string.IsNullOrWhiteSpace(text)) return null;

        // A continuation is framed so the model neither repeats the earlier text nor treats
        // the join as a sentence boundary. The plausibility guard in Core catches a model
        // that repeats the context anyway: the word count balloons and the result is dropped.
        var input = string.IsNullOrWhiteSpace(precedingCleaned)
            ? text
            : "<<earlier part of this dictation, already cleaned: context only, do not repeat or change it>>\n"
              + precedingCleaned.Trim()
              + "\n<<end of earlier part>>\n\n"
              + "Clean only the continuation below. It follows straight on from the earlier part, possibly mid-sentence, "
              + "so add a capital letter or full stop at the join only if the words call for one.\n"
              + text;

        var request = new GenerateRequest(
            SystemInstruction: new Content([new Part(Prompt(_customInstructions()))]),
            Contents: [new Content([new Part(input)])],
            // No output cap: a fixed 2048 tokens cut a ten-minute dictation off at the
            // knees and, because 80% of the text still looked plausible, the truncated
            // version was typed. The model's own limit is far above any dictation.
            GenerationConfig: new GenerationConfig(
                Temperature: 0,
                MaxOutputTokens: null,
                ThinkingConfig: new ThinkingConfig(0)));

        var body = JsonSerializer.Serialize(request, GeminiJsonContext.Default.GenerateRequest);

        // Built as text, not with the (base, relative) overload: "gemini-2.5-flash:generateContent"
        // parses as an absolute URI whose scheme is "gemini-2.5-flash" and throws. Seen live.
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{BaseUri}{_model}:generateContent"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-goog-api-key", key);

        try
        {
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"{(int)response.StatusCode} {response.ReasonPhrase}: {Excerpt(payload)}";
                return null;
            }

            var parsed = JsonSerializer.Deserialize(payload, GeminiJsonContext.Default.GenerateResponse);
            var candidates = parsed?.Candidates;
            var candidate = candidates is { Count: > 0 } ? candidates[0] : null;
            var parts = candidate?.Content?.Parts;
            var reply = parts is { Count: > 0 } ? parts[0].Text : null;
            if (string.IsNullOrWhiteSpace(reply))
            {
                LastError = "empty reply";
                return null;
            }

            // Anything but a clean stop — a length cap, a safety filter, a recitation
            // block — means the text is not the whole text. Never type a partial rewrite.
            if (candidate?.FinishReason is { } reason && !string.Equals(reason, "STOP", StringComparison.OrdinalIgnoreCase))
            {
                LastError = $"reply was cut short ({reason})";
                return null;
            }

            LastError = null;
            return reply.Trim();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException or NotSupportedException)
        {
            LastError = e is TaskCanceledException && !cancellationToken.IsCancellationRequested
                ? $"no reply within {Deadline.TotalSeconds:0} s"
                : e.Message;
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A pooled connection is dropped after a minute idle, and the next clean-up then pays
    /// roughly 120 ms for a fresh handshake — measured on 2026-09-11 as 645 ms against 520 ms.
    /// A dictation lasts longer than a handshake, so opening the connection at key-down hides
    /// it completely. The request itself needs no key: any response, even 404, leaves the
    /// connection open in the pool.
    /// </remarks>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (ResolveKey(_apiKey()) is null) return;

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Head, BaseUri);
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Nothing to do; the real call will report properly if the network is down.
        }
    }

    /// <summary>Why the most recent call returned null, for the log and Settings.</summary>
    public string? LastError { get; private set; }

    private static string Excerpt(string payload)
    {
        // The API's error JSON carries a "message"; surface that rather than the whole blob.
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through.
        }

        return payload.Length > 200 ? payload[..200] : payload;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // ---- Wire shapes. Source-generated so they survive single-file publishing. ----

    internal sealed record GenerateRequest(
        [property: JsonPropertyName("systemInstruction")] Content SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<Content> Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

    internal sealed record Content([property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    internal sealed record Part([property: JsonPropertyName("text")] string Text);

    internal sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens,
        [property: JsonPropertyName("thinkingConfig")] ThinkingConfig ThinkingConfig);

    internal sealed record ThinkingConfig([property: JsonPropertyName("thinkingBudget")] int ThinkingBudget);

    internal sealed record GenerateResponse([property: JsonPropertyName("candidates")] IReadOnlyList<Candidate>? Candidates);

    internal sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason = null);
}

/// <summary>Source-generated JSON for the Gemini wire shapes.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GeminiCleaner.GenerateRequest))]
[JsonSerializable(typeof(GeminiCleaner.GenerateResponse))]
internal sealed partial class GeminiJsonContext : JsonSerializerContext;
