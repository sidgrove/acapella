using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// Cloud transcription on ElevenLabs Scribe v2 Realtime, fed while the key is held.
/// </summary>
/// <remarks>
/// <para>
/// In September 2026 Scribe led the independent streaming benchmark (Artificial Analysis):
/// 3.6% word errors with the final transcript 0.14 s after speech ends, against Gemini 3.5
/// Transcribe Live's 4.0% and 0.40 s. Benchmarks did not carry over to Dave's voice for
/// Gemini, so this is measured on his recordings before it is trusted.
/// </para>
/// <para>
/// Committed manually: the whole dictation is one commit, sent with the last audio, so
/// the service never cuts at a pause and loses the word after it, as Gemini's own pause
/// detection did.
/// </para>
/// </remarks>
public sealed class ElevenLabsTranscriber : IStreamingTranscriber
{
    /// <summary>The model used unless settings say otherwise.</summary>
    public const string DefaultModel = "scribe_v2_realtime";

    /// <summary>Environment variable consulted for the key.</summary>
    public const string ApiKeyEnvironmentVariable = "ELEVENLABS_API_KEY";

    /// <summary>Realtime key terms: at most this many.</summary>
    public const int MaxKeyTerms = 50;

    /// <summary>Realtime key terms: at most this many characters each.</summary>
    public const int MaxKeyTermLength = 20;

    private readonly Func<string?> _apiKey;
    private readonly string _model;
    private readonly string _language;

    /// <summary>Creates a transcriber that reads the key at each dictation.</summary>
    /// <param name="apiKey">Returns the key, or null to use <see cref="ApiKeyEnvironmentVariable"/>.</param>
    /// <param name="model">Model id; defaults to <see cref="DefaultModel"/>.</param>
    /// <param name="language">ISO 639-1 language, e.g. "en".</param>
    public ElevenLabsTranscriber(Func<string?> apiKey, string? model = null, string language = "en")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _language = language;
    }

    /// <inheritdoc />
    public string Name => _model;

    /// <summary>The key in use: the configured one, else the environment variable.</summary>
    /// <remarks>
    /// The user's stored variable is read as well as the process's own, because a key set
    /// in Windows' environment settings reaches only processes started afterwards, and the
    /// app is usually already running.
    /// </remarks>
    public static string? ResolveKey(string? configured) =>
        !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
        : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) is { Length: > 0 } fromProcess ? fromProcess.Trim()
        : OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable, EnvironmentVariableTarget.User) is { Length: > 0 } fromUser ? fromUser.Trim()
        : null;

    /// <inheritdoc />
    public IStreamingTranscription? Start(IReadOnlyList<string> keyTerms)
    {
        var key = ResolveKey(_apiKey());
        return key is null ? null : new ElevenLabsTranscription(key, Endpoint(_model, _language, keyTerms));
    }

    /// <summary>The socket address, carrying the whole configuration as query parameters.</summary>
    public static Uri Endpoint(string model, string language, IReadOnlyList<string> keyTerms)
    {
        var query = new StringBuilder("wss://api.elevenlabs.io/v1/speech-to-text/realtime")
            .Append("?model_id=").Append(Uri.EscapeDataString(model))
            .Append("&audio_format=pcm_16000")
            .Append("&language_code=").Append(Uri.EscapeDataString(language))
            .Append("&commit_strategy=manual");
        var terms = keyTerms
            .Select(t => t.Trim())
            .Where(t => t.Length is > 0 and <= MaxKeyTermLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxKeyTerms);
        foreach (var term in terms) query.Append("&keyterms=").Append(Uri.EscapeDataString(term));
        return new Uri(query.ToString());
    }
}

/// <summary>One dictation on one Scribe socket, committed once at the key-up.</summary>
internal sealed class ElevenLabsTranscription : SocketTranscription
{
    private static readonly HashSet<string> Errors = new(StringComparer.Ordinal)
    {
        "error", "auth_error", "quota_exceeded", "commit_throttled", "unaccepted_terms", "rate_limited", "queue_overflow",
        "resource_exhausted", "session_time_limit_exceeded", "input_error", "invalid_request", "chunk_size_exceeded",
        "insufficient_audio_activity", "transcriber_error",
    };

    private readonly string _key;
    private readonly Uri _endpoint;

    public ElevenLabsTranscription(string key, Uri endpoint)
    {
        _key = key;
        _endpoint = endpoint;
        Begin();
    }

    protected override Uri Endpoint => _endpoint;

    protected override void Configure(ClientWebSocketOptions options) => options.SetRequestHeader("xi-api-key", _key);

    protected override bool IsReady(JsonElement message) => Type(message) == "session_started";

    protected override ReadOnlyMemory<byte> Audio(byte[] pcm16, bool last) => Json(json =>
    {
        json.WriteStartObject();
        json.WriteString("message_type", "input_audio_chunk");
        json.WriteBase64String("audio_base_64", pcm16);
        json.WriteBoolean("commit", last);
        json.WriteNumber("sample_rate", AudioChunk.SampleRate);
        json.WriteEndObject();
    });

    protected override (string? Words, bool Complete, string? Error) Read(JsonElement message)
    {
        var type = Type(message);
        if (type is null) return (null, false, null);
        if (Errors.Contains(type))
        {
            var detail = message.TryGetProperty("error", out var error) ? error.GetString() : null;
            return (null, false, $"{type}: {detail}");
        }
        if (type is "committed_transcript")
        {
            return (message.TryGetProperty("text", out var text) ? text.GetString() : null, true, null);
        }
        return (null, false, null);
    }

    private static string? Type(JsonElement message) =>
        message.TryGetProperty("message_type", out var type) ? type.GetString() : null;
}
