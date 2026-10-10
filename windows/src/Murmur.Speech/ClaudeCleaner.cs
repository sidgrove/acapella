using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// The generative clean-up tier, on Claude.
/// </summary>
/// <remarks>
/// <para>
/// The same job as <see cref="GeminiCleaner"/>, asked in the same words: the prompt, the
/// framing of the screen and of two readings, and the contraction guard are all its. Only
/// the wire differs, so the two can be scored against each other on the same dictations
/// (<c>--compare</c>) and swapped by changing the model in Settings.
/// </para>
/// <para>
/// Built for Claude Haiku 5.5 (7/10/2026), which is priced at a third of Gemini 2.5 Flash on
/// input and a fifth on output. Effort is <c>low</c>: this is a rewrite, not a reasoning
/// task, and every hundred milliseconds is felt between key-up and text. Thinking is left
/// adaptive, not disabled; see <see cref="ReplyRule"/> for what disabling it did. No <c>temperature</c> is sent, because any value but the default is a 400 on this
/// model. The system prompt is marked for caching, as it is the same on every dictation and
/// is most of what is sent.
/// </para>
/// <para>
/// Called over REST for the reason given on <see cref="GeminiCleaner"/>. Any failure returns
/// null and the caller types the local text.
/// </para>
/// </remarks>
public sealed class ClaudeCleaner : ITranscriptCleaner, IDisposable
{
    /// <summary>The model used unless settings say otherwise.</summary>
    public const string DefaultModel = "claude-haiku-5-5";

    /// <summary>Environment variable consulted when no key is set in the app.</summary>
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    /// <summary>
    /// The most the reply may run to. The API requires a figure; this is several times the
    /// longest dictation, and a reply that reaches it is dropped rather than typed short.
    /// </summary>
    public const int MaxTokens = 16000;

    /// <summary>
    /// Added to the shared prompt for Claude only. Public so the evaluation can show it.
    /// </summary>
    /// <remarks>
    /// Measured on 10/10/2026 with Dave's own dictations. With thinking disabled Haiku 5.5
    /// treated a dictation that reads like a request as a message to itself: "yes lets build
    /// the claude client lets go" came back as "I can't build a Claude client from here…",
    /// and 40 dictations scored a 100% word error rate. This rule alone did not stop it.
    /// With adaptive thinking at low effort and this rule the same dictations were cleaned,
    /// and no thinking tokens were spent on them, so it costs no time.
    /// </remarks>
    public const string ReplyRule = "\n\nYour whole reply is typed straight into the speaker's document. Reply with the cleaned text and nothing else: no comment, no explanation, no note that nothing needed changing, no quotation marks around it.";

    private const string ApiVersion = "2023-06-01";

    private static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<string?> _customInstructions;
    private readonly Func<IReadOnlyList<string>> _vocabulary;
    private readonly string _model;

    /// <summary>Whether <paramref name="model"/> is one this client serves, going by its name.</summary>
    public static bool Serves(string? model) =>
        model is not null && model.Trim().StartsWith("claude-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a cleaner that reads the key each call, so a key pasted into Settings works immediately.</summary>
    /// <param name="apiKey">Returns the key, or null/empty when none is configured.</param>
    /// <param name="model">Model id; defaults to <see cref="DefaultModel"/>.</param>
    /// <param name="handler">Transport, for tests.</param>
    /// <param name="customInstructions">Returns the user's own instructions, appended to the prompt, or null.</param>
    /// <param name="vocabulary">Returns the speaker's vocabulary, read per call so dictionary edits apply at once.</param>
    public ClaudeCleaner(Func<string?> apiKey, string? model = null, HttpMessageHandler? handler = null, Func<string?>? customInstructions = null, Func<IReadOnlyList<string>>? vocabulary = null)
    {
        _apiKey = apiKey;
        _customInstructions = customInstructions ?? (static () => null);
        _vocabulary = vocabulary ?? (static () => []);
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        // Kept alive across the minutes between dictations, as the Gemini client's is.
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            })
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            }
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = GeminiCleaner.Deadline;
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
    public Task<string?> CleanAsync(string text, string? precedingCleaned, CancellationToken cancellationToken) =>
        SendAsync(text, precedingCleaned, mayStopMidSentence: false, cancellationToken);

    /// <inheritdoc />
    public Task<string?> CleanPieceAsync(string text, string? precedingCleaned, CancellationToken cancellationToken) =>
        SendAsync(text, precedingCleaned, mayStopMidSentence: true, cancellationToken);

    /// <inheritdoc />
    public Task<string?> CleanTwoReadingsAsync(string cloud, string local, CancellationToken cancellationToken) =>
        SendAsync(cloud, null, mayStopMidSentence: false, cancellationToken, local);

    /// <inheritdoc />
    public Task<string?> CleanWithScreenAsync(string text, ScreenContext? screen, CancellationToken cancellationToken) =>
        SendAsync(text, null, mayStopMidSentence: false, cancellationToken, screen: screen);

    /// <inheritdoc />
    public Task<string?> CleanTwoReadingsAsync(string cloud, string local, ScreenContext? screen, CancellationToken cancellationToken) =>
        SendAsync(cloud, null, mayStopMidSentence: false, cancellationToken, local, screen);

    private async Task<string?> SendAsync(string text, string? precedingCleaned, bool mayStopMidSentence, CancellationToken cancellationToken, string? alternative = null, ScreenContext? screen = null)
    {
        var key = ResolveKey(_apiKey());
        if (key is null)
        {
            LastError = "no API key";
            return null;
        }
        if (string.IsNullOrWhiteSpace(text)) return null;

        var request = new MessagesRequest(
            Model: _model,
            MaxTokens: MaxTokens,
            System: [new SystemBlock("text", GeminiCleaner.Prompt(_customInstructions(), _vocabulary()) + ReplyRule, new CacheControl("ephemeral"))],
            Thinking: new Thinking("adaptive"),
            OutputConfig: new OutputConfig("low"),
            Messages: [new Message("user", GeminiCleaner.Input(text, precedingCleaned, mayStopMidSentence, alternative, screen))]);

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, ClaudeJsonContext.Default.MessagesRequest), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", ApiVersion);

        try
        {
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"{(int)response.StatusCode} {response.ReasonPhrase}: {GeminiCleaner.Excerpt(payload)}";
                return null;
            }

            var parsed = JsonSerializer.Deserialize(payload, ClaudeJsonContext.Default.MessagesResponse);

            // Blocks are picked by type, never by position: a thinking block can come first.
            var reply = string.Concat((parsed?.Content ?? []).Where(b => b.Type == "text").Select(b => b.Text));
            if (string.IsNullOrWhiteSpace(reply))
            {
                LastError = parsed?.StopReason is { } why && why != "end_turn" ? $"no reply ({why})" : "empty reply";
                return null;
            }

            // Anything but a clean stop (the length cap, a refusal) means the text is not
            // the whole text. Never type a partial rewrite.
            if (parsed?.StopReason is { } reason && !string.Equals(reason, "end_turn", StringComparison.Ordinal))
            {
                LastError = $"reply was cut short ({reason})";
                return null;
            }

            LastError = null;
            return GeminiCleaner.KeepContractions(reply.Trim(), text, alternative);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or NotSupportedException)
        {
            LastError = e is TaskCanceledException && !cancellationToken.IsCancellationRequested
                ? $"no reply within {GeminiCleaner.Deadline.TotalSeconds:0} s"
                : e is TaskCanceledException ? "cancelled by the caller's deadline" : e.Message;
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>Opens the connection at key-down, as <see cref="GeminiCleaner.WarmUpAsync"/> does; any response leaves it in the pool.</remarks>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (ResolveKey(_apiKey()) is null) return;

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Head, Endpoint);
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Nothing to do; the real call will report properly if the network is down.
        }
    }

    /// <summary>Why the most recent call returned null, for the log and Settings.</summary>
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // ---- Wire shapes. Source-generated so they survive single-file publishing. ----

    internal sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] IReadOnlyList<SystemBlock> System,
        [property: JsonPropertyName("thinking")] Thinking Thinking,
        [property: JsonPropertyName("output_config")] OutputConfig OutputConfig,
        [property: JsonPropertyName("messages")] IReadOnlyList<Message> Messages);

    internal sealed record SystemBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("cache_control")] CacheControl CacheControl);

    internal sealed record CacheControl([property: JsonPropertyName("type")] string Type);

    internal sealed record Thinking([property: JsonPropertyName("type")] string Type);

    internal sealed record OutputConfig([property: JsonPropertyName("effort")] string Effort);

    internal sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    internal sealed record MessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason = null);

    internal sealed record ContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text = null);
}

/// <summary>Source-generated JSON for the Claude wire shapes.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClaudeCleaner.MessagesRequest))]
[JsonSerializable(typeof(ClaudeCleaner.MessagesResponse))]
internal sealed partial class ClaudeJsonContext : JsonSerializerContext;

/// <summary>Picks the clean-up client for a model id: Claude for <c>claude-…</c>, Gemini for everything else.</summary>
public static class TranscriptCleaners
{
    /// <summary>Creates the cleaner for <paramref name="model"/>; null or blank is Gemini's default.</summary>
    /// <param name="model">The model id from Settings.</param>
    /// <param name="geminiKey">Returns the Gemini key.</param>
    /// <param name="anthropicKey">Returns the Anthropic key.</param>
    /// <param name="customInstructions">Returns the user's own instructions, or null.</param>
    /// <param name="vocabulary">Returns the speaker's vocabulary.</param>
    public static ITranscriptCleaner Create(string? model, Func<string?> geminiKey, Func<string?> anthropicKey, Func<string?>? customInstructions = null, Func<IReadOnlyList<string>>? vocabulary = null) =>
        ClaudeCleaner.Serves(model)
            ? new ClaudeCleaner(anthropicKey, model, customInstructions: customInstructions, vocabulary: vocabulary)
            : new GeminiCleaner(geminiKey, model, customInstructions: customInstructions, vocabulary: vocabulary);
}
