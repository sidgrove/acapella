using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// Cloud transcription on Gemini's streaming speech-to-text model, fed while the key is held.
/// </summary>
/// <remarks>
/// <para>
/// Measured from Dave's desk on 2026-09-24 with <c>gemini-3.5-transcribe-live</c>: the socket
/// and setup take about 0.45 s, well inside the first words, and the final transcript of a
/// 60-second dictation arrives 0.6 s after the audio ends. The same audio sent in one piece
/// after the key-up took 2.2 s.
/// </para>
/// <para>
/// <b>The service's own pause detection is switched off.</b> Left on, it cuts the dictation
/// at every pause and loses the first word after each cut: "The requests" came back as
/// "requests" and "FRS 102 style format" as "FRS 102 format". The key already says exactly
/// where speech starts and ends, so the whole dictation is sent as one activity.
/// </para>
/// <para>
/// <b>Verbatim, not smart, mode.</b> Smart mode removes fillers but also reorders: "I think
/// Sonnet was better at this than Codex, to be honest" came back starting "To be honest".
/// The clean-up step does the tidying under rules that forbid that.
/// </para>
/// </remarks>
public sealed class GeminiLiveTranscriber : IStreamingTranscriber
{
    /// <summary>The model used unless settings say otherwise.</summary>
    public const string DefaultModel = "gemini-3.5-transcribe-live";

    /// <summary>The documented ceiling on the word list; about a hundred works best.</summary>
    public const int MaxKeyTerms = 1000;

    internal static readonly Uri Endpoint = new("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent");

    private readonly Func<string?> _apiKey;
    private readonly string _model;
    private readonly string _language;

    /// <summary>Creates a transcriber that reads the key at each dictation.</summary>
    /// <param name="apiKey">Returns the Gemini key, or null when none is configured.</param>
    /// <param name="model">Model id; defaults to <see cref="DefaultModel"/>.</param>
    /// <param name="language">BCP-47 language, e.g. "en-GB".</param>
    public GeminiLiveTranscriber(Func<string?> apiKey, string? model = null, string language = "en-GB")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _language = language;
    }

    /// <inheritdoc />
    public string Name => _model;

    /// <inheritdoc />
    public IStreamingTranscription? Start(IReadOnlyList<string> keyTerms)
    {
        var key = GeminiCleaner.ResolveKey(_apiKey());
        return key is null ? null : new GeminiLiveTranscription(key, Setup(_model, _language, keyTerms));
    }

    /// <summary>The setup message: one manual activity, verbatim, with the speaker's word list.</summary>
    public static byte[] Setup(string model, string language, IReadOnlyList<string> keyTerms)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("setup");
            json.WriteString("model", $"models/{model}");
            json.WriteStartObject("generationConfig");
            json.WriteStartArray("responseModalities");
            json.WriteStringValue("TEXT");
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteStartObject("realtimeInputConfig");
            json.WriteStartObject("automaticActivityDetection");
            json.WriteBoolean("disabled", true);
            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteStartObject("inputAudioTranscription");
            json.WriteStartArray("languageCodes");
            json.WriteStringValue(language);
            json.WriteEndArray();
            var terms = keyTerms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxKeyTerms).ToList();
            if (terms.Count > 0)
            {
                json.WriteStartArray("customVocabulary");
                foreach (var term in terms) json.WriteStringValue(term);
                json.WriteEndArray();
            }
            json.WriteString("mode", "VERBATIM");
            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>One dictation on one Gemini Live socket: one manual activity, ended by the key-up.</summary>
internal sealed class GeminiLiveTranscription : SocketTranscription
{
    private static readonly byte[] ActivityStart = """{"realtimeInput":{"activityStart":{}}}"""u8.ToArray();
    private static readonly byte[] ActivityEnd = """{"realtimeInput":{"activityEnd":{}}}"""u8.ToArray();

    private readonly string _key;
    private readonly byte[] _setup;

    public GeminiLiveTranscription(string key, byte[] setup)
    {
        _key = key;
        _setup = setup;
        Begin();
    }

    protected override Uri Endpoint => GeminiLiveTranscriber.Endpoint;

    protected override void Configure(ClientWebSocketOptions options) => options.SetRequestHeader("x-goog-api-key", _key);

    protected override IEnumerable<ReadOnlyMemory<byte>> Opening() => [_setup];

    protected override bool IsReady(JsonElement message) => message.TryGetProperty("setupComplete", out _);

    protected override IEnumerable<ReadOnlyMemory<byte>> Starting() => [ActivityStart];

    protected override ReadOnlyMemory<byte> Audio(byte[] pcm16, bool last) => Json(json =>
    {
        json.WriteStartObject();
        json.WriteStartObject("realtimeInput");
        json.WriteStartObject("audio");
        json.WriteBase64String("data", pcm16);
        json.WriteString("mimeType", "audio/pcm;rate=16000");
        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndObject();
    });

    protected override IEnumerable<ReadOnlyMemory<byte>> Closing() => [ActivityEnd];

    protected override (string? Words, bool Complete, string? Error) Read(JsonElement message)
    {
        if (!message.TryGetProperty("serverContent", out var content)) return (null, false, null);
        string? words = null;
        if (content.TryGetProperty("inputTranscription", out var final) && final.TryGetProperty("text", out var text)) words = text.GetString();
        var complete = (content.TryGetProperty("generationComplete", out var generation) && generation.ValueKind == JsonValueKind.True)
                    || (content.TryGetProperty("turnComplete", out var turn) && turn.ValueKind == JsonValueKind.True);
        return (words, complete, null);
    }
}
